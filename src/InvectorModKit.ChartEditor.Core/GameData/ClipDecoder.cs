using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor
{
    /// <summary>What one animated value is bound to: CRC-32 of its transform path and of its property, and the component class.</summary>
    public sealed class ClipBinding
    {
        public uint Path;
        public uint Attribute;
        public int TypeId;
        public bool IsPPtr;
    }

    /// <summary>An AnimationClip's muscle data as stored: streamed (cubic keys), dense (fixed rate samples) and constant curves.</summary>
    public sealed class MuscleClipData
    {
        public float StartTime, StopTime;
        public uint[] Streamed;
        public int StreamedCurveCount;
        public float[] Dense;
        public int DenseCurveCount, DenseFrameCount;
        public float DenseSampleRate, DenseBeginTime;
        public float[] Constant;
        public List<ClipBinding> Bindings;
    }

    /// <summary>
    /// Samples a clip's curves. Curves are numbered streamed first, then dense, then constant, and the bindings take
    /// them in that order (a Transform position, rotation, scale or euler binding takes 3, 4, 3 or 3 curves).
    /// </summary>
    public static class ClipDecoder
    {
        const int Transform = 4;
        static readonly string[] TransformAttributes = { null, "position", "rotation", "scale", "euler" };

        public static int CurveCount(ClipBinding binding)
        {
            if (binding.TypeId != Transform) return 1;
            switch (binding.Attribute)
            {
                case 1: case 3: case 4: return 3;
                case 2: return 4;
                default: return 1;
            }
        }

        public static float[] Sample(MuscleClipData clip, float time) => Sample(clip, StreamedKeys(clip), time);

        public static JObject ToJson(string name, MuscleClipData clip, IEnumerable<string> paths, IEnumerable<string> attributes, int fps = 60)
        {
            var pathNames = ByCrc(paths);
            var attributeNames = ByCrc(attributes);
            var keys = StreamedKeys(clip);
            float length = clip.StopTime - clip.StartTime;
            int frames = (int)Math.Round(length * fps) + 1;
            var samples = new float[frames][];
            for (int k = 0; k < frames; k++)
                samples[k] = Sample(clip, keys, Math.Min(clip.StartTime + (float)k / fps, clip.StopTime));

            var tracks = new JArray();
            int curve = 0;
            foreach (var b in clip.Bindings)
            {
                int size = CurveCount(b);
                var values = new JArray();
                foreach (var s in samples)
                    for (int c = curve; c < curve + size; c++)
                        values.Add(c < s.Length && !float.IsNaN(s[c]) && !float.IsInfinity(s[c]) ? new JValue(s[c]) : JValue.CreateNull());
                var track = new JObject
                {
                    ["path"] = pathNames.TryGetValue(b.Path, out var p) ? new JValue(p) : new JValue(b.Path),
                    ["type"] = b.TypeId,
                    ["attribute"] = b.TypeId == Transform && b.Attribute >= 1 && b.Attribute <= 4 ? new JValue(TransformAttributes[b.Attribute])
                        : attributeNames.TryGetValue(b.Attribute, out var a) ? new JValue(a) : new JValue(b.Attribute),
                    ["size"] = size,
                    ["values"] = values,
                };
                if (b.IsPPtr) track["pptr"] = true;
                tracks.Add(track);
                curve += size;
            }
            return new JObject { ["name"] = name, ["length"] = length, ["fps"] = fps, ["tracks"] = tracks };
        }

        static Dictionary<uint, string> ByCrc(IEnumerable<string> names)
        {
            var map = new Dictionary<uint, string>();
            foreach (var n in names) map[Crc32.Compute(n)] = n;
            return map;
        }

        struct Key
        {
            public float Time, C0, C1, C2, C3;
        }

        /// <summary>The streamed keys of each curve, in time order. Frames with a non-finite time (sentinels) are skipped.</summary>
        static List<Key>[] StreamedKeys(MuscleClipData clip)
        {
            var curves = Enumerable.Range(0, clip.StreamedCurveCount).Select(_ => new List<Key>()).ToArray();
            var words = clip.Streamed ?? new uint[0];
            var bytes = new byte[words.Length * 4];
            Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length);
            int at = 0;
            while (at + 8 <= bytes.Length)
            {
                float time = BitConverter.ToSingle(bytes, at);
                uint count = BitConverter.ToUInt32(bytes, at + 4);
                at += 8;
                for (uint k = 0; k < count && at + 20 <= bytes.Length; k++, at += 20)
                {
                    int index = (int)BitConverter.ToUInt32(bytes, at);
                    if (float.IsNaN(time) || float.IsInfinity(time) || index < 0 || index >= curves.Length) continue;
                    curves[index].Add(new Key
                    {
                        Time = time,
                        C0 = BitConverter.ToSingle(bytes, at + 4),
                        C1 = BitConverter.ToSingle(bytes, at + 8),
                        C2 = BitConverter.ToSingle(bytes, at + 12),
                        C3 = BitConverter.ToSingle(bytes, at + 16),
                    });
                }
            }
            return curves;
        }

        static float[] Sample(MuscleClipData clip, List<Key>[] streamed, float time)
        {
            int dense = clip.DenseCurveCount, constant = clip.Constant?.Length ?? 0;
            var values = new float[streamed.Length + dense + constant];

            for (int c = 0; c < streamed.Length; c++)
            {
                var keys = streamed[c];
                if (keys.Count == 0) continue;
                int i = keys.Count - 1;
                while (i >= 0 && keys[i].Time > time) i--;
                if (i < 0) { values[c] = keys[0].C3; continue; }
                var k = keys[i];
                float d = time - k.Time;
                values[c] = ((k.C0 * d + k.C1) * d + k.C2) * d + k.C3;
            }

            if (dense > 0 && clip.DenseFrameCount > 0)
            {
                float position = (time - clip.DenseBeginTime) * clip.DenseSampleRate;
                int last = clip.DenseFrameCount - 1;
                position = Math.Max(0, Math.Min(last, position));
                int f0 = (int)Math.Floor(position), f1 = Math.Min(f0 + 1, last);
                float u = position - f0;
                for (int c = 0; c < dense; c++)
                {
                    float a = clip.Dense[f0 * dense + c], b = clip.Dense[f1 * dense + c];
                    values[streamed.Length + c] = a + (b - a) * u;
                }
            }

            for (int c = 0; c < constant; c++) values[streamed.Length + dense + c] = clip.Constant[c];
            return values;
        }
    }
}
