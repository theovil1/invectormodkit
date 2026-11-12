using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor
{
    /// <summary>One of a Unity mesh's 14 vertex channels; a Dimension of 0 (low 4 bits) means the channel is absent.</summary>
    public sealed class VertexChannel
    {
        public int Stream, Offset, Format, Dimension;
    }

    public sealed class SubMeshData
    {
        public int FirstByte, IndexCount, Topology, BaseVertex;
    }

    /// <summary>A Unity 2021 mesh as stored: vertex streams and index buffer bytes, already read from the bundle.</summary>
    public sealed class MeshData
    {
        public int VertexCount;
        public List<VertexChannel> Channels;
        public byte[] VertexBytes;
        public byte[] IndexBytes;
        public bool Index32;
        public List<SubMeshData> SubMeshes;
        public float[] BoundsCenter, BoundsExtent;
    }

    /// <summary>A mesh as the cache stores it: one float array per attribute, indices with base vertices applied.</summary>
    public sealed class DecodedMesh
    {
        public int VertexCount;
        public List<KeyValuePair<string, float[]>> Attributes = new List<KeyValuePair<string, float[]>>();
        public Dictionary<string, int> Sizes = new Dictionary<string, int>();
        public uint[] Indices;
        public bool Index32;
        public List<int[]> SubMeshes = new List<int[]>();
        public float[] BoundsCenter, BoundsExtent;
    }

    public static class MeshDecoder
    {
        /// <summary>Channel names in Unity's order; 12 and 13 (skinning weights and bone indices) are not kept.</summary>
        static readonly string[] Names = { "position", "normal", "tangent", "color", "uv0", "uv1", "uv2", "uv3", "uv4", "uv5", "uv6", "uv7" };

        /// <summary>Bytes per component of each VertexFormat (Float32, Float16, UNorm8, SNorm8, UNorm16, SNorm16, UInt8 to SInt32).</summary>
        static readonly int[] FormatSizes = { 4, 2, 1, 1, 2, 2, 1, 1, 2, 2, 4, 4 };

        public static DecodedMesh Decode(MeshData mesh)
        {
            var result = new DecodedMesh { VertexCount = mesh.VertexCount, BoundsCenter = mesh.BoundsCenter, BoundsExtent = mesh.BoundsExtent };

            // Streams follow each other, each one starting on a 16-byte boundary.
            int streams = mesh.Channels.Where(c => (c.Dimension & 0xF) != 0).Select(c => c.Stream + 1).DefaultIfEmpty(0).Max();
            var stride = new int[streams];
            foreach (var c in mesh.Channels)
                if ((c.Dimension & 0xF) != 0) stride[c.Stream] += FormatSizes[c.Format] * (c.Dimension & 0xF);
            var start = new int[streams];
            for (int s = 1; s < streams; s++) start[s] = Align16(start[s - 1] + stride[s - 1] * mesh.VertexCount);

            for (int i = 0; i < Names.Length && i < mesh.Channels.Count; i++)
            {
                var c = mesh.Channels[i];
                int dim = c.Dimension & 0xF;
                if (dim == 0) continue;
                int size = FormatSizes[c.Format];
                var values = new float[mesh.VertexCount * dim];
                for (int v = 0; v < mesh.VertexCount; v++)
                {
                    int at = start[c.Stream] + v * stride[c.Stream] + c.Offset;
                    for (int k = 0; k < dim; k++) values[v * dim + k] = ReadComponent(c.Format, mesh.VertexBytes, at + k * size);
                }
                result.Attributes.Add(new KeyValuePair<string, float[]>(Names[i], values));
                result.Sizes[Names[i]] = dim;
            }

            int indexSize = mesh.Index32 ? 4 : 2;
            var indices = new List<uint>();
            foreach (var sub in mesh.SubMeshes)
            {
                if (sub.Topology != 0) throw new NotSupportedException($"mesh topology {sub.Topology} (only triangles are read)");
                result.SubMeshes.Add(new[] { indices.Count, sub.IndexCount });
                for (int k = 0; k < sub.IndexCount; k++)
                {
                    int at = sub.FirstByte + k * indexSize;
                    uint raw = mesh.Index32 ? BitConverter.ToUInt32(mesh.IndexBytes, at) : BitConverter.ToUInt16(mesh.IndexBytes, at);
                    indices.Add((uint)(raw + sub.BaseVertex));
                }
            }
            result.Indices = indices.ToArray();
            result.Index32 = mesh.Index32 || result.Indices.Any(i => i > ushort.MaxValue);
            return result;
        }

        static int Align16(int n) => (n + 15) & ~15;

        /// <summary>One vertex component as a float; normalized formats are scaled to [0, 1] or [-1, 1], integers kept as is.</summary>
        public static float ReadComponent(int format, byte[] data, int offset)
        {
            switch (format)
            {
                case 0: return BitConverter.ToSingle(data, offset);
                case 1: return HalfToSingle(BitConverter.ToUInt16(data, offset));
                case 2: return data[offset] / 255f;
                case 3: return Math.Max((sbyte)data[offset] / 127f, -1f);
                case 4: return BitConverter.ToUInt16(data, offset) / 65535f;
                case 5: return Math.Max(BitConverter.ToInt16(data, offset) / 32767f, -1f);
                case 6: return data[offset];
                case 7: return (sbyte)data[offset];
                case 8: return BitConverter.ToUInt16(data, offset);
                case 9: return BitConverter.ToInt16(data, offset);
                case 10: return BitConverter.ToUInt32(data, offset);
                case 11: return BitConverter.ToInt32(data, offset);
                default: throw new NotSupportedException($"vertex format {format}");
            }
        }

        static float HalfToSingle(ushort h)
        {
            int sign = (h >> 15) & 1, exponent = (h >> 10) & 0x1F, mantissa = h & 0x3FF;
            float value;
            if (exponent == 0) value = mantissa * (1f / (1 << 24)); // subnormal: mantissa x 2^-24
            else if (exponent == 31) value = mantissa == 0 ? float.PositiveInfinity : float.NaN;
            else value = (1f + mantissa / 1024f) * (float)Math.Pow(2, exponent - 15);
            return sign == 1 ? -value : value;
        }

        /// <summary>The cache's .bin file: a header line, each attribute as little-endian floats, then the indices (u16 or u32).</summary>
        public static void Write(DecodedMesh mesh, Stream output)
        {
            var attributes = new JObject();
            foreach (var a in mesh.Attributes) attributes[a.Key] = mesh.Sizes[a.Key];
            CacheFiles.WriteHeaderLine(output, new JObject
            {
                ["vertexCount"] = mesh.VertexCount,
                ["attributes"] = attributes,
                ["index"] = mesh.Index32 ? "u32" : "u16",
                ["submeshes"] = new JArray(mesh.SubMeshes.Select(s => new JArray(s[0], s[1]))),
                ["bounds"] = new JObject { ["center"] = new JArray(mesh.BoundsCenter), ["extent"] = new JArray(mesh.BoundsExtent) },
            });
            var writer = new BinaryWriter(output);
            foreach (var a in mesh.Attributes)
                foreach (float f in a.Value) writer.Write(f);
            foreach (uint i in mesh.Indices)
                if (mesh.Index32) writer.Write(i); else writer.Write((ushort)i);
            writer.Flush();
        }
    }
}
