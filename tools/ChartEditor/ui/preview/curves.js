// Unity's animation curves, particle "MinMaxCurve" values and gradients, as the game data cache writes them.

/**
 * A Unity AnimationCurve: Hermite between keys { time, value, inSlope, outSlope }, clamped to the first and last
 * values outside them.
 */
export function hermite(keys) {
  if (!keys || keys.length === 0) return () => 0;
  if (keys.length === 1) return () => keys[0].value;
  return (t) => {
    if (t <= keys[0].time) return keys[0].value;
    const last = keys[keys.length - 1];
    if (t >= last.time) return last.value;
    let i = 0;
    while (t > keys[i + 1].time) i++;
    const a = keys[i], b = keys[i + 1], d = b.time - a.time;
    if (d <= 0) return b.value;
    const s = (t - a.time) / d, s2 = s * s, s3 = s2 * s;
    return (2 * s3 - 3 * s2 + 1) * a.value + (s3 - 2 * s2 + s) * d * a.outSlope + (-2 * s3 + 3 * s2) * b.value + (s3 - s2) * d * b.inSlope;
  };
}

const curveOf = (c) => hermite(c && c.m_Curve);

/**
 * A particle MinMaxCurve as a function of (u: normalized lifetime, r: the particle's random number): constant,
 * curve, random between two curves, or random between two constants.
 */
export function minMaxCurve(c) {
  if (!c) return () => 0;
  const scalar = c.scalar ?? 0;
  switch (c.minMaxState) {
    case 1: { const max = curveOf(c.maxCurve); return (u) => scalar * max(u); }
    case 2: {
      const min = curveOf(c.minCurve), max = curveOf(c.maxCurve);
      return (u, r) => { const a = min(u); return scalar * (a + (max(u) - a) * r); };
    }
    case 3: { const lo = c.minScalar ?? 0; return (u, r) => lo + (scalar - lo) * r; }
    default: return () => scalar;
  }
}

const rgba = (c) => (c ? [c.r, c.g, c.b, c.a] : [1, 1, 1, 1]);

/** A Unity Gradient (up to 8 colour and 8 alpha keys, times out of 65535) at u, blended or fixed. */
export function gradient(g) {
  if (!g) return () => [1, 1, 1, 1];
  const keys = (count, time, pick) => {
    const out = [];
    for (let i = 0; i < Math.max(1, count); i++) out.push({ t: (g[time + i] ?? 0) / 65535, v: pick(g["key" + i]) });
    return out;
  };
  const colors = keys(g.m_NumColorKeys, "ctime", (k) => (k ? [k.r, k.g, k.b] : [1, 1, 1]));
  const alphas = keys(g.m_NumAlphaKeys, "atime", (k) => [k ? k.a : 1]);
  const fixed = g.m_Mode === 1;
  const at = (list, u) => {
    if (u <= list[0].t) return list[0].v;
    for (let i = 1; i < list.length; i++) {
      if (u <= list[i].t) {
        if (fixed) return list[i].v;
        const a = list[i - 1], b = list[i], k = b.t > a.t ? (u - a.t) / (b.t - a.t) : 1;
        return a.v.map((v, j) => v + (b.v[j] - v) * k);
      }
    }
    return list[list.length - 1].v;
  };
  return (u) => [...at(colors, u), at(alphas, u)[0]];
}

/** A particle MinMaxGradient as a function of (u, r): colour, gradient, two colours, two gradients. */
export function minMaxGradient(g) {
  if (!g) return () => [1, 1, 1, 1];
  const mix = (a, b, r) => a.map((v, i) => v + (b[i] - v) * r);
  switch (g.minMaxState) {
    case 1: { const max = gradient(g.maxGradient); return (u) => max(u); }
    case 2: { const a = rgba(g.minColor), b = rgba(g.maxColor); return (u, r) => mix(a, b, r); }
    case 3: { const a = gradient(g.minGradient), b = gradient(g.maxGradient); return (u, r) => mix(a(u), b(u), r); }
    default: { const c = rgba(g.maxColor); return () => c.slice(); }
  }
}
