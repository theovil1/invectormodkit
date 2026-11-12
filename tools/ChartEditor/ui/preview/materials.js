// The game's shaders as three.js materials, from a material as the game data cache writes it ({ name, shader,
// colors, floats, textures: { name: { ref, scale, offset } } }). The constants were read from the compiled shaders.
import * as THREE from "three";

const rgb = (c, k = 1) => new THREE.Color(c[0] * k, c[1] * k, c[2] * k);

/**
 * A material standing in for the game's shader, by its name. `texture(ref)` gives a loaded texture (or null);
 * `glow` scales emissive output above 1 (bloom); `tint` is the pickup's own colour (Pickup.color), which colours its
 * outline and ground ring.
 */
export function materialFor(info, texture, { glow = 1, tint = null } = {}) {
  const shader = info.shader || "", tex = info.textures || {}, col = info.colors || {};
  const map = (k) => (tex[k] && tex[k].ref ? texture(tex[k].ref) : null);
  const hasVC = /VertexColors/i.test(shader);
  if (/Additive/i.test(shader)) {
    const tc = tint && hasVC ? [tint.r, tint.g, tint.b, 1] : col._TintColor || col._Color || [1, 1, 1, 1];
    // a texture with the material's own tiling and offset (Unity _ST: uv * scale + offset)
    const placed = (k) => {
      const t = map(k);
      if (!t) return null;
      const c = t.clone();
      c.repeat.set(...(tex[k].scale || [1, 1]));
      c.offset.set(...(tex[k].offset || [0, 0]));
      c.needsUpdate = true;
      return c;
    };
    // Sonic Ether "AdditiveMask": a grey mask (its own tiling) scales the output, e.g. the Freefly gate's sweep
    const mask = /AdditiveMask/i.test(shader) ? placed("_MaskTex") : null;
    if (mask) mask.colorSpace = THREE.NoColorSpace;
    return new THREE.MeshBasicMaterial({
      map: placed("_MainTex"), alphaMap: mask, color: rgb(tc, 2 * tc[3] * glow), vertexColors: hasVC && !tint, transparent: true,
      blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide,
    });
  }
  if (/Emissive/i.test(shader) && hasVC) {
    // Sonic Ether "Textured Unlit VertexColors": the atlas is a grey emission mask, the vertex colour gives the hue;
    // the flat part of the pickup still shows its colour (base), the outline goes above 1 and blooms (gain)
    return new THREE.ShaderMaterial({
      uniforms: {
        map: { value: map("_Illum") || map("_MainTex") }, gain: { value: 2.2 * glow }, base: { value: 2.6 },
        tint: { value: tint ? new THREE.Color(tint.r, tint.g, tint.b) : new THREE.Color(1, 1, 1) }, useTint: { value: tint ? 1 : 0 },
      },
      vertexColors: true, side: THREE.DoubleSide,
      vertexShader: "varying vec2 vUv; varying vec3 vCol; void main(){ vUv = uv; vCol = color; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }",
      fragmentShader: "uniform sampler2D map; uniform float gain; uniform float base; uniform vec3 tint; uniform float useTint; varying vec2 vUv; varying vec3 vCol;" +
        "void main(){ vec3 t = texture2D(map, vUv).rgb; vec3 hue = mix(vCol, tint, useTint);" +
        " gl_FragColor = vec4(mix(vCol * base, hue * gain, clamp(t * 1.3, 0.0, 1.0)), 1.0); }",
    });
  }
  if (/Emissive/i.test(shader)) {
    const e = tint ? [tint.r, tint.g, tint.b, 1] : col._EmissionColor || [1, 1, 1, 1];
    // the emission mask is _Illum; a material that names _Illum without a texture gets Unity's default white (the
    // strum bars: _MainTex is their black diffuse, not their glow)
    const illum = tex._Illum ? (tex._Illum.ref ? map("_Illum") : null) : map("_MainTex");
    if (shader === "Sonic Ether/Emissive/Textured" && !tint) {
      // emission = _Illum x _EmissionColor x exp(10 x _EmissionGain) (sRGB colour, linear game)
      const E = Math.exp(10 * ((info.floats || {})._EmissionGain || 0));
      return new THREE.MeshBasicMaterial({ map: illum, color: new THREE.Color().setRGB(e[0], e[1], e[2], THREE.SRGBColorSpace).multiplyScalar(E), side: THREE.DoubleSide });
    }
    const m = new THREE.MeshBasicMaterial({ map: illum, color: rgb(e, glow * (tint ? 1.6 : 1)), vertexColors: hasVC, side: THREE.DoubleSide });
    if (!m.map) m.color = rgb(e, glow);
    return m;
  }
  if (tex._TextureSample0) {
    // the ships' shader: diffuse with baked AO, tinted by _MainColor, plus a static emissive mask
    const main = col._MainColor || [1, 1, 1, 1];
    // _DetailColor is set by ShipRecolorororerer from ShipColorScheme.colorPerPlayer[0]: HDR magenta (16.9, 0.89, 16.9)
    return new THREE.MeshStandardMaterial({
      map: map("_TextureSample0"), color: rgb(main, 0.42), emissiveMap: map("_DetailColorMask"),
      emissive: new THREE.Color(16.95, 0.89, 16.95).multiplyScalar(0.12 * glow), roughness: 0.45, metalness: 0.3, side: THREE.DoubleSide,
    });
  }
  return new THREE.MeshBasicMaterial({ map: map("_MainTex"), color: 0xffffff, side: THREE.DoubleSide });
}

/** Unity's built-in materials (the default particle material): plain additive white. */
export const builtinMaterial = () => new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide });
