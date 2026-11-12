// Part 1's binary cache files: one line of JSON (padded so its end is 4-byte aligned), then the data. Meshes
// (.bin): each attribute as little-endian Float32 in header order, then the indices (u16 or u32). Textures (.tex):
// the bytes as the game stores them.

/** Unity TextureFormat numbers the preview can upload to WebGL 2. */
export const SUPPORTED_FORMATS = new Set([1, 3, 4, 10, 12, 17, 24, 25]);

/** How a format is uploaded: "s3tc" (DXT1, DXT5), "bptc" (BC6H, BC7), "half" (RGBAHalf), "plain" (Alpha8, RGB24, RGBA32). */
export function textureKind(format) {
  switch (format) {
    case 10: case 12: return "s3tc";
    case 24: case 25: return "bptc";
    case 17: return "half";
    case 1: case 3: case 4: return "plain";
    default: return null;
  }
}

function header(buffer) {
  const bytes = new Uint8Array(buffer);
  const end = bytes.indexOf(10); // "\n"
  if (end < 0) throw new Error("cache file without a header line");
  return { json: JSON.parse(new TextDecoder().decode(bytes.subarray(0, end))), offset: end + 1 };
}

/** A typed array over buffer[offset..], copied when the offset is not aligned for the type. */
function view(Type, buffer, offset, count) {
  if (offset % Type.BYTES_PER_ELEMENT === 0) return new Type(buffer, offset, count);
  return new Type(buffer.slice(offset, offset + count * Type.BYTES_PER_ELEMENT));
}

export function parseMesh(buffer) {
  const { json, offset } = header(buffer);
  const n = json.vertexCount, attributes = {}, sizes = {};
  let at = offset;
  for (const [name, size] of Object.entries(json.attributes)) {
    attributes[name] = view(Float32Array, buffer, at, n * size);
    sizes[name] = size;
    at += n * size * 4;
  }
  const Index = json.index === "u32" ? Uint32Array : Uint16Array;
  const indices = view(Index, buffer, at, (buffer.byteLength - at) / Index.BYTES_PER_ELEMENT);
  return { vertexCount: n, attributes, sizes, indices, submeshes: json.submeshes, bounds: json.bounds };
}

export function parseTexture(buffer) {
  const { json, offset } = header(buffer);
  return { ...json, data: new Uint8Array(buffer, offset) };
}
