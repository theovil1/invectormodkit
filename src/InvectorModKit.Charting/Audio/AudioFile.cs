using System;
using System.IO;
using System.Security.Cryptography;

namespace InvectorModKit.Charting.Audio
{
    /// <summary>Decoded PCM audio, interleaved floats in [-1, 1].</summary>
    public sealed class DecodedAudio
    {
        public float[] Samples;
        public int Channels;
        public int SampleRate;

        public int Frames => Samples.Length / Channels;
        public double Duration => Frames / (double)SampleRate;

        public float[] ToMono()
        {
            if (Channels == 1) return Samples;
            var mono = new float[Frames];
            for (int i = 0, o = 0; i < mono.Length; i++)
            {
                float s = 0;
                for (int c = 0; c < Channels; c++) s += Samples[o++];
                mono[i] = s / Channels;
            }
            return mono;
        }
    }

    /// <summary>
    /// Pure C# decoding of .mp3 (NLayer), .ogg (NVorbis) and .wav, so songs can be decoded off the
    /// main thread in game and by the offline tools alike.
    /// </summary>
    public static class AudioFile
    {
        public static readonly string[] Extensions = { ".mp3", ".ogg", ".wav" };

        public static bool IsSupported(string path) => Array.IndexOf(Extensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

        public static DecodedAudio Decode(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".mp3": return DecodeMp3(path);
                case ".ogg": return DecodeOgg(path);
                case ".wav": return DecodeWav(path);
                default: throw new NotSupportedException("Unsupported audio format: " + path);
            }
        }

        /// <summary>Length in seconds without decoding the whole file (0 if unknown).</summary>
        public static double ProbeDuration(string path)
        {
            try
            {
                switch (Path.GetExtension(path).ToLowerInvariant())
                {
                    case ".mp3":
                        using (var mp3 = new NLayer.MpegFile(path)) return mp3.Duration.TotalSeconds;
                    case ".ogg":
                        using (var vorbis = new NVorbis.VorbisReader(path)) return vorbis.TotalTime.TotalSeconds;
                    case ".wav":
                        using (var br = new BinaryReader(File.OpenRead(path)))
                        {
                            br.BaseStream.Seek(22, SeekOrigin.Begin);
                            int channels = br.ReadInt16(), rate = br.ReadInt32();
                            br.BaseStream.Seek(34, SeekOrigin.Begin);
                            int bits = br.ReadInt16();
                            long data = br.BaseStream.Length - 44;
                            return data / (double)(rate * channels * Math.Max(1, bits / 8));
                        }
                }
            }
            catch (Exception) { }
            return 0;
        }

        /// <summary>Content hash of the file, used to know when a cached auto-chart is stale.</summary>
        public static string Hash(string path)
        {
            using (var sha = SHA1.Create())
            using (var s = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }

        /// <summary>Writes a 16-bit PCM WAV of <paramref name="seconds"/> starting at <paramref name="from"/>, with short fades.</summary>
        public static void WriteWavExcerpt(DecodedAudio audio, string path, double from, double seconds, double fade = 1.0)
        {
            int ch = audio.Channels, rate = audio.SampleRate;
            int start = Math.Max(0, Math.Min(audio.Frames - 1, (int)(from * rate)));
            int frames = Math.Max(0, Math.Min(audio.Frames - start, (int)(seconds * rate)));
            int fadeFrames = Math.Min(frames / 2, (int)(fade * rate));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (var bw = new BinaryWriter(File.Create(path)))
            {
                int dataBytes = frames * ch * 2;
                bw.Write(new[] { 'R', 'I', 'F', 'F' }); bw.Write(36 + dataBytes); bw.Write(new[] { 'W', 'A', 'V', 'E' });
                bw.Write(new[] { 'f', 'm', 't', ' ' }); bw.Write(16); bw.Write((short)1); bw.Write((short)ch);
                bw.Write(rate); bw.Write(rate * ch * 2); bw.Write((short)(ch * 2)); bw.Write((short)16);
                bw.Write(new[] { 'd', 'a', 't', 'a' }); bw.Write(dataBytes);
                for (int f = 0; f < frames; f++)
                {
                    float gain = f < fadeFrames ? f / (float)fadeFrames : f >= frames - fadeFrames ? (frames - f) / (float)fadeFrames : 1f;
                    for (int c = 0; c < ch; c++)
                    {
                        float s = audio.Samples[(start + f) * ch + c] * gain;
                        bw.Write((short)Math.Max(-32768, Math.Min(32767, (int)(s * 32767))));
                    }
                }
            }
        }

        private static DecodedAudio DecodeMp3(string path)
        {
            using (var mp3 = new NLayer.MpegFile(path))
            {
                int channels = mp3.Channels;
                var buffer = new float[mp3.SampleRate * channels];
                var output = new GrowableFloats(mp3.Length > 0 ? (int)(mp3.Length / 4) : 1 << 20);
                int read;
                while ((read = mp3.ReadSamples(buffer, 0, buffer.Length)) > 0) output.Append(buffer, read);
                return new DecodedAudio { Samples = output.ToArray(), Channels = channels, SampleRate = mp3.SampleRate };
            }
        }

        private static DecodedAudio DecodeOgg(string path)
        {
            using (var vorbis = new NVorbis.VorbisReader(path))
            {
                int channels = vorbis.Channels;
                var buffer = new float[vorbis.SampleRate * channels];
                var output = new GrowableFloats((int)Math.Min(int.MaxValue / 2, Math.Max(1 << 20, vorbis.TotalSamples * channels)));
                int read;
                while ((read = vorbis.ReadSamples(buffer, 0, buffer.Length)) > 0) output.Append(buffer, read);
                return new DecodedAudio { Samples = output.ToArray(), Channels = channels, SampleRate = vorbis.SampleRate };
            }
        }

        private static DecodedAudio DecodeWav(string path)
        {
            using (var br = new BinaryReader(File.OpenRead(path)))
            {
                if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a RIFF file");
                br.ReadInt32();
                if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAVE file");
                int format = 0, channels = 0, rate = 0, bits = 0;
                while (br.BaseStream.Position + 8 <= br.BaseStream.Length)
                {
                    string id = new string(br.ReadChars(4));
                    int size = br.ReadInt32();
                    if (id == "fmt ")
                    {
                        format = br.ReadInt16(); channels = br.ReadInt16(); rate = br.ReadInt32();
                        br.ReadInt32(); br.ReadInt16(); bits = br.ReadInt16();
                        br.BaseStream.Seek(size - 16, SeekOrigin.Current);
                        if (format == unchecked((short)0xFFFE)) format = bits == 32 ? 3 : 1; // WAVE_FORMAT_EXTENSIBLE: assume PCM/float by size
                    }
                    else if (id == "data")
                    {
                        long remaining = br.BaseStream.Length - br.BaseStream.Position;
                        if (size <= 0 || size > remaining) size = (int)remaining;
                        byte[] data = br.ReadBytes(size);
                        int bytes = bits / 8;
                        int count = data.Length / bytes;
                        var samples = new float[count];
                        for (int i = 0; i < count; i++)
                        {
                            int o = i * bytes;
                            samples[i] = format == 3 ? BitConverter.ToSingle(data, o)
                                : bits == 8 ? (data[o] - 128) / 128f
                                : bits == 16 ? BitConverter.ToInt16(data, o) / 32768f
                                : bits == 24 ? (data[o] | data[o + 1] << 8 | (sbyte)data[o + 2] << 16) / 8388608f
                                : BitConverter.ToInt32(data, o) / 2147483648f;
                        }
                        return new DecodedAudio { Samples = samples, Channels = channels, SampleRate = rate };
                    }
                    else br.BaseStream.Seek(size + (size & 1), SeekOrigin.Current);
                }
                throw new InvalidDataException("No data chunk in " + path);
            }
        }

        private sealed class GrowableFloats
        {
            private float[] _a;
            private int _n;
            public GrowableFloats(int capacity) { _a = new float[Math.Max(1024, capacity)]; }
            public void Append(float[] src, int count)
            {
                if (_n + count > _a.Length) Array.Resize(ref _a, Math.Max(_a.Length * 2, _n + count));
                Array.Copy(src, 0, _a, _n, count);
                _n += count;
            }
            public float[] ToArray()
            {
                if (_n == _a.Length) return _a;
                var r = new float[_n];
                Array.Copy(_a, r, _n);
                return r;
            }
        }
    }
}
