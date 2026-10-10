using System;
using System.Collections.Generic;
using InvectorModKit.Charting.Audio;

namespace InvectorModKit.Charting.Tests;

public class FingerprintTests
{
    private const int Rate = 22050;

    /// <summary>Decaying tone bursts at random intervals (0.12-0.6 s): a rhythm with no repetition.</summary>
    private static float[] Clicks(double seconds, int seed)
    {
        var rng = new Random(seed);
        var x = new float[(int)(seconds * Rate)];
        for (double t = 0.05; t < seconds; t += 0.12 + rng.NextDouble() * 0.48)
        {
            double freq = 200 + rng.NextDouble() * 2000, amp = 0.3 + rng.NextDouble() * 0.6;
            int start = (int)(t * Rate);
            for (int i = 0; i < Rate / 8 && start + i < x.Length; i++)
                x[start + i] += (float)(amp * Math.Exp(-i / (Rate * 0.03)) * Math.Sin(2 * Math.PI * freq * i / Rate));
        }
        return x;
    }

    private static float[] Concat(params float[][] parts)
    {
        var list = new List<float>();
        foreach (var p in parts) list.AddRange(p);
        return list.ToArray();
    }

    private static float[] Slice(float[] x, double from, double to) => x[(int)(from * Rate)..(int)(to * Rate)];

    private static Fingerprint Fp(float[] mono) => Fingerprint.Of(new DecodedAudio { Samples = mono, Channels = 1, SampleRate = Rate });

    [Fact]
    public void Match_FindsOffset()
    {
        var song = Clicks(90, 1);
        var local = Concat(Clicks(12.345, 99), song);
        var m = Fingerprint.Match(Fp(song), Fp(local));
        Assert.True(m.IsMatch, $"score {m.Score:0.00} consistency {m.Consistency:0.00}");
        Assert.InRange(m.Offset, 12.340, 12.350);
    }

    [Fact]
    public void Match_NegativeOffset()
    {
        var song = Clicks(90, 2);
        var m = Fingerprint.Match(Fp(song), Fp(Slice(song, 5.5, 90)));
        Assert.True(m.IsMatch);
        Assert.InRange(m.Offset, -5.505, -5.495);
    }

    [Fact]
    public void Match_SurvivesNoiseAndGain()
    {
        var song = Clicks(90, 3);
        var rng = new Random(7);
        var local = Concat(new float[(int)(3.21 * Rate)], song);
        for (int i = 0; i < local.Length; i++) local[i] = 0.5f * local[i] + 0.005f * (float)(rng.NextDouble() * 2 - 1); // gain -6 dB, noise ~-40 dB (an encoder's noise floor, as in ChartLab fpeval)
        var m = Fingerprint.Match(Fp(song), Fp(local));
        Assert.True(m.IsMatch, $"score {m.Score:0.00} consistency {m.Consistency:0.00}");
        // Sparse synthetic bursts at -6 dB over noise are a worst case: detected attacks move by up to half a
        // frame. On real songs degraded like a re-encode, ChartLab fpeval measures 2.4 ms at most.
        Assert.InRange(m.Offset, 3.202, 3.218);
    }

    [Fact]
    public void Match_InsertedPassage_NotConsistent()
    {
        var song = Clicks(120, 4);
        var local = Concat(Slice(song, 0, 50), Clicks(8, 98), Slice(song, 50, 120));
        Assert.False(Fingerprint.Match(Fp(song), Fp(local)).IsMatch);
    }

    [Fact]
    public void Match_OtherSong_NoMatch()
    {
        Assert.False(Fingerprint.Match(Fp(Clicks(90, 5)), Fp(Clicks(90, 6))).IsMatch);
    }

    [Fact]
    public void Match_FlatEnvelope_NoMatch()
    {
        var silence = new float[60 * Rate];
        var m = Fingerprint.Match(Fp(Clicks(60, 8)), Fp(silence));
        Assert.False(m.IsMatch);
        Assert.False(double.IsNaN(m.Score));
        Assert.False(Fingerprint.Match(Fp(silence), Fp(silence)).IsMatch);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var fp = Fp(Clicks(30, 9));
        var back = Fingerprint.FromJson(fp.ToJson());
        Assert.Equal(fp.Duration, back.Duration, 3);
        Assert.Equal(fp.Envelope, back.Envelope);
        Assert.InRange(fp.Envelope.Length, Fingerprint.Rate * 30 - 2, Fingerprint.Rate * 30 + 2);
    }
}
