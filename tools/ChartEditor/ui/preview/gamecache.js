// The game data cache (part 1, served under /game/) loaded into three.js: prefabs as object trees with the game's
// meshes, textures and materials. Unity is left-handed (z forward): positions and vertices get z negated,
// quaternions (-x, -y, z, w), triangle winding reversed. A prefab the cache lacks is drawn as a flat arrow.
import * as THREE from "three";
import { parseMesh, parseTexture, textureKind } from "./cachefiles.js";
import { materialFor, builtinMaterial } from "./materials.js";
import { toLinear } from "./math.js";

const WRAP = [THREE.RepeatWrapping, THREE.ClampToEdgeWrapping, THREE.MirroredRepeatWrapping];

/** A flat arrow pointing ahead, in a colour: what stands in for a prefab the cache does not have. */
export function standIn(color = 0xffffff) {
  const s = new THREE.Shape();
  s.moveTo(0, 1.2); s.lineTo(1, 0); s.lineTo(0.4, 0); s.lineTo(0.4, -1); s.lineTo(-0.4, -1); s.lineTo(-0.4, 0); s.lineTo(-1, 0); s.closePath();
  const g = new THREE.Group();
  const mesh = new THREE.Mesh(new THREE.ShapeGeometry(s).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ color, side: THREE.DoubleSide }));
  mesh.name = "stand-in";
  g.add(mesh);
  return g;
}

export async function loadGameCache(renderer, base = "/game/") {
  const fetchOk = async (path) => {
    const r = await fetch(base + path);
    if (!r.ok) throw new Error(`${path}: ${r.status}`);
    return r;
  };
  const json = async (path) => (await fetchOk(path)).json();
  const index = await json("index.json");
  const fileOf = (key) => (index.items.find((i) => i.key === key) || {}).file;
  const objects = new Map();
  await Promise.all(index.items.filter((i) => /^(Prefab|Controller):/.test(i.key)).map(async (i) => {
    try {
      objects.set(i.key.slice(i.key.indexOf(":") + 1), await json(i.file));
    } catch (e) {
      console.warn("game prefab", i.key, e.message); // drawn as a stand-in
    }
  }));
  const settings = await json("settings.json");

  const ext = (name) => renderer.extensions.has(name);
  const s3tc = ext("WEBGL_compressed_texture_s3tc"), s3tcSrgb = ext("WEBGL_compressed_texture_s3tc_srgb"), bptc = ext("EXT_texture_compression_bptc");

  /** A texture from the cache, or null when its format cannot be uploaded here. */
  function makeTexture(t) {
    const kind = textureKind(t.format);
    let tex = null;
    const srgb = t.colorSpace === 1;
    if (kind === "s3tc" || kind === "bptc") {
      if ((kind === "s3tc" && !s3tc) || (kind === "bptc" && !bptc)) return null;
      const block = t.format === 10 ? 8 : 16, mipmaps = [];
      let w = t.width, h = t.height, at = 0;
      for (let i = 0; i < Math.max(1, t.mips); i++) {
        const size = Math.max(1, Math.ceil(w / 4)) * Math.max(1, Math.ceil(h / 4)) * block;
        if (at + size > t.data.length) break;
        mipmaps.push({ data: t.data.subarray(at, at + size), width: w, height: h });
        at += size; w = Math.max(1, w >> 1); h = Math.max(1, h >> 1);
      }
      const format = { 10: THREE.RGB_S3TC_DXT1_Format, 12: THREE.RGBA_S3TC_DXT5_Format, 24: THREE.RGB_BPTC_UNSIGNED_Format, 25: THREE.RGBA_BPTC_Format }[t.format];
      tex = new THREE.CompressedTexture(mipmaps, t.width, t.height, format);
      tex.minFilter = mipmaps.length > 1 ? THREE.LinearMipmapLinearFilter : THREE.LinearFilter;
      tex.colorSpace = srgb && (kind === "bptc" || s3tcSrgb) && t.format !== 24 ? THREE.SRGBColorSpace : THREE.NoColorSpace;
    } else if (kind === "half") {
      tex = new THREE.DataTexture(new Uint16Array(t.data.buffer, t.data.byteOffset, t.width * t.height * 4), t.width, t.height, THREE.RGBAFormat, THREE.HalfFloatType);
      tex.minFilter = THREE.LinearFilter;
      tex.colorSpace = THREE.NoColorSpace;
    } else if (kind === "plain") {
      let data = t.data.subarray(0, t.width * t.height * (t.format === 1 ? 1 : t.format === 3 ? 3 : 4)), format = THREE.RGBAFormat;
      if (t.format === 3) { // RGB24 to RGBA
        const rgba = new Uint8Array(t.width * t.height * 4);
        for (let i = 0, j = 0; i < data.length; i += 3, j += 4) { rgba[j] = data[i]; rgba[j + 1] = data[i + 1]; rgba[j + 2] = data[i + 2]; rgba[j + 3] = 255; }
        data = rgba;
      } else if (t.format === 1) format = THREE.AlphaFormat;
      tex = new THREE.DataTexture(data, t.width, t.height, format);
      tex.generateMipmaps = true;
      tex.minFilter = THREE.LinearMipmapLinearFilter;
      tex.colorSpace = srgb && format !== THREE.AlphaFormat ? THREE.SRGBColorSpace : THREE.NoColorSpace;
    } else return null;
    if (t.filter === 0) { tex.magFilter = THREE.NearestFilter; tex.minFilter = THREE.NearestFilter; }
    tex.wrapS = WRAP[t.wrap[0]] ?? THREE.RepeatWrapping;
    tex.wrapT = WRAP[t.wrap[1]] ?? THREE.RepeatWrapping;
    tex.flipY = false; // Unity stores rows bottom up, as WebGL reads them
    tex.anisotropy = 8;
    tex.userData.size = [t.width, t.height];
    tex.needsUpdate = true;
    return tex;
  }

  const textures = new Map();
  const texture = (ref) => {
    if (!textures.has(ref)) {
      textures.set(ref, fetchOk(`textures/${ref}.tex`).then((r) => r.arrayBuffer()).then((b) => makeTexture(parseTexture(b))).catch((e) => {
        console.warn("game texture", ref, e.message);
        return null;
      }));
    }
    return textures.get(ref);
  };

  function makeGeometry(m) {
    const g = new THREE.BufferGeometry(), n = m.vertexCount;
    const flipZ = (a) => { const out = new Float32Array(a); for (let i = 2; i < out.length; i += 3) out[i] = -out[i]; return out; };
    g.setAttribute("position", new THREE.BufferAttribute(flipZ(m.attributes.position), 3));
    if (m.attributes.normal) g.setAttribute("normal", new THREE.BufferAttribute(flipZ(m.attributes.normal), 3));
    if (m.attributes.uv0) g.setAttribute("uv", new THREE.BufferAttribute(new Float32Array(m.attributes.uv0), 2));
    if (m.attributes.uv1) g.setAttribute("uv1", new THREE.BufferAttribute(new Float32Array(m.attributes.uv1), 2));
    if (m.attributes.color) {
      const c = m.attributes.color, col = new Float32Array(n * 3); // vertex colours are authored in sRGB
      for (let i = 0; i < n; i++) { col[i * 3] = toLinear(c[i * 4]); col[i * 3 + 1] = toLinear(c[i * 4 + 1]); col[i * 3 + 2] = toLinear(c[i * 4 + 2]); }
      g.setAttribute("color", new THREE.BufferAttribute(col, 3));
    }
    const idx = m.indices.slice();
    for (let i = 0; i + 2 < idx.length; i += 3) { const a = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = a; }
    g.setIndex(new THREE.BufferAttribute(idx, 1));
    m.submeshes.forEach(([start, count], i) => g.addGroup(start, count, i));
    if (!m.attributes.normal) g.computeVertexNormals();
    g.computeBoundingSphere();
    return g;
  }

  const geometries = new Map();
  const geometry = (ref) => {
    if (!geometries.has(ref)) {
      geometries.set(ref, fetchOk(`meshes/${ref}.bin`).then((r) => r.arrayBuffer()).then((b) => makeGeometry(parseMesh(b))).catch((e) => {
        console.warn("game mesh", ref, e.message);
        return null;
      }));
    }
    return geometries.get(ref);
  };
  const plane = new THREE.PlaneGeometry(1, 1); // Unity's built-in Quad (path id 10210) and other built-in meshes

  async function material(m, opts) {
    if (!m) return new THREE.MeshBasicMaterial({ visible: false });
    if (m.builtin || m.missing) return builtinMaterial();
    const loaded = new Map();
    await Promise.all(Object.values(m.textures || {}).filter((t) => t && t.ref).map(async (t) => loaded.set(t.ref, await texture(t.ref))));
    return materialFor(m, (ref) => loaded.get(ref) || null, opts);
  }

  async function spritePlane(fields, opts) {
    const s = fields.m_Sprite, rect = s.rect || [0, 0, 1, 1], ppu = s.pixelsPerUnit || 100;
    const w = rect[2] / ppu, h = rect[3] / ppu;
    const geo = new THREE.PlaneGeometry(w, h).translate((0.5 - s.pivot[0]) * w, (0.5 - s.pivot[1]) * h, 0);
    const mat = await material((fields.m_Materials || [])[0], opts);
    const tex = s.texture && s.texture.ref ? await texture(s.texture.ref) : null;
    if (tex && mat.color !== undefined) {
      const t = tex.clone(), [tw, th] = tex.userData.size || [rect[2], rect[3]];
      t.repeat.set(rect[2] / tw, rect[3] / th);
      t.offset.set(rect[0] / tw, rect[1] / th);
      t.needsUpdate = true;
      mat.map = t;
      const c = fields.m_Color;
      if (c && mat.color) mat.color.multiply(new THREE.Color(c.r, c.g, c.b)).multiplyScalar(opts.gain ? c.a ?? 1 : 1); // with the shader's gain, the colour's alpha scales it too
    }
    return new THREE.Mesh(geo, mat);
  }

  async function build(node, opts) {
    const g = new THREE.Group();
    g.name = node.name;
    g.userData.id = node.id;
    g.position.set(node.position[0], node.position[1], -node.position[2]);
    g.quaternion.set(-node.rotation[0], -node.rotation[1], node.rotation[2], node.rotation[3]);
    g.scale.set(node.scale[0], node.scale[1], node.scale[2]);
    if (opts.hide && opts.hide(node.name)) g.visible = false;
    const comp = (type) => node.components.find((c) => c.type === type);
    const filter = comp("MeshFilter"), meshRenderer = comp("MeshRenderer"), skinned = comp("SkinnedMeshRenderer"), sprite = comp("SpriteRenderer");
    const renderer = meshRenderer || skinned, meshRef = filter ? filter.fields.m_Mesh : skinned ? skinned.fields.m_Mesh : null;
    if (meshRef && renderer && renderer.fields.m_Enabled !== 0) {
      const geo = meshRef.builtin ? plane : meshRef.ref && !meshRef.missing ? await geometry(meshRef.ref) : null;
      if (geo) {
        const mats = await Promise.all((renderer.fields.m_Materials || []).map((m) => material(m, opts)));
        const mesh = new THREE.Mesh(geo, mats.length > 1 ? mats : mats[0] || new THREE.MeshBasicMaterial());
        mesh.name = node.name + ":mesh";
        // Sonic Ether particle shaders multiply by the mesh's vertex colours (a glow fading out at its rim); taken where
        // the shader's own gain is (opts.gain), the pickups' looks being matched by eye without them
        if (opts.gain && geo.attributes.color) for (const m of mats) if (m && m.blending === THREE.AdditiveBlending) { m.vertexColors = true; m.needsUpdate = true; }
        g.add(mesh);
      }
    }
    if (sprite && sprite.fields.m_Enabled !== 0 && sprite.fields.m_Sprite && opts.sprites && opts.sprites(node.name)) {
      const mesh = await spritePlane(sprite.fields, opts);
      mesh.name = node.name + ":sprite";
      g.add(mesh);
    }
    for (const child of node.children) if (child.active !== false) g.add(await build(child, opts));
    return g;
  }

  const templates = new Map();
  /**
   * A copy of a prefab, built once per set of options: { key, glow, tint, gain, hide(name), sprites(name) } (sprites:
   * which sprite renderers to draw; gain: additive parts glow by their shader's gain, see materialFor).
   */
  async function prefab(name, opts = {}) {
    const key = name + "|" + (opts.key || "");
    if (!templates.has(key)) {
      const obj = objects.get(name);
      templates.set(key, obj ? build(obj.root, opts).then((g) => { g.position.set(0, 0, 0); return g; }) : Promise.resolve(standIn(opts.tint ? new THREE.Color(opts.tint.r, opts.tint.g, opts.tint.b) : 0xffffff)));
    }
    return (await templates.get(key)).clone();
  }

  const levels = new Map(), clips = new Map();
  return {
    index,
    settings,
    object: (name) => objects.get(name) || null,
    level: (world) => { if (!levels.has(world)) levels.set(world, json(fileOf("Level:" + world) || `levels/${world}.json`)); return levels.get(world); },
    clip: (name) => { if (!clips.has(name)) clips.set(name, json(fileOf("Clip:" + name))); return clips.get(name); },
    texture,
    geometry,
    prefab,
  };
}
