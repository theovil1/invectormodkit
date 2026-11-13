using System.IO;
using InvectorModKit.Marketplace.Client.Discord;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>Connecting to Discord, pacing the updates and surviving Discord going away.</summary>
public class DiscordPresenceTests
{
    private static readonly DateTime T = new DateTime(2026, 11, 13, 20, 0, 0, DateTimeKind.Utc);

    private sealed class FakePipe : IDiscordPipe
    {
        public readonly List<(IpcOpcode op, JToken json)> Sent = new();
        public int Opens;
        public int RefuseOpens;
        public bool ThrowOnWrite;
        public readonly Queue<byte[]> Incoming = new();
        public bool IsOpen { get; private set; }

        public bool TryOpen()
        {
            Opens++;
            if (RefuseOpens > 0) { RefuseOpens--; return false; }
            return IsOpen = true;
        }

        public void Write(byte[] data)
        {
            if (ThrowOnWrite) { IsOpen = false; throw new IOException("gone"); }
            Assert.True(IpcFrame.TryDecode(data, data.Length, out var op, out var json, out _));
            Sent.Add((op, JToken.Parse(json)));
        }

        public int ReadAvailable(byte[] buffer)
        {
            if (Incoming.Count == 0) return 0;
            var b = Incoming.Dequeue();
            Buffer.BlockCopy(b, 0, buffer, 0, b.Length);
            return b.Length;
        }

        public void Dispose() => IsOpen = false;

        public List<JToken> Activities => Sent.Where(f => f.op == IpcOpcode.Frame).Select(f => f.json["args"]["activity"]).ToList();
    }

    private readonly FakePipe _pipe = new();
    private readonly List<string> _logs = new();
    private DiscordPresence NewPresence() => new DiscordPresence("123", 42, () => _pipe, _logs.Add, T);

    private static PresenceTexts Texts() => new PresenceTexts
    {
        Menu = "Menu", SongSelect = "Select", Marketplace = "Marketplace", Editor = "Editor", Paused = "Paused", Results = "Results",
        ScoreLine = "{0} pts · combo {1}", NotesOne = "{0} note", NotesMany = "{0} notes", CustomSong = "Custom",
        GetMod = "Get", JoinDiscord = "Join", ModVersion = "IMK", LanguageCode = "en", ModUrl = "https://a", DiscordUrl = "https://b",
    };

    private static PresenceSnapshot Level(int score = 100, double remaining = 120) => new PresenceSnapshot
    {
        Screen = PresenceScreen.Playing, Title = "Song", Artist = "Artist", Difficulty = "Hard", Score = score, Combo = 5, RemainingSeconds = remaining,
    };

    [Fact]
    public void FirstTick_HandshakesThenSendsActivity()
    {
        var p = NewPresence();
        p.Update(new PresenceSnapshot { Screen = PresenceScreen.Menu }, Texts());
        p.Tick(T);
        Assert.Equal(IpcOpcode.Handshake, _pipe.Sent[0].op);
        Assert.Equal(1, (int)_pipe.Sent[0].json["v"]);
        Assert.Equal("123", (string)_pipe.Sent[0].json["client_id"]);
        Assert.Equal(IpcOpcode.Frame, _pipe.Sent[1].op);
        Assert.Equal("SET_ACTIVITY", (string)_pipe.Sent[1].json["cmd"]);
        Assert.Equal(42, (int)_pipe.Sent[1].json["args"]["pid"]);
        Assert.Equal("Menu", (string)_pipe.Sent[1].json["args"]["activity"]["details"]);
        Assert.False(string.IsNullOrEmpty((string)_pipe.Sent[1].json["nonce"]));
    }

    [Fact]
    public void SameWish_IsNotResent()
    {
        var p = NewPresence();
        p.Update(new PresenceSnapshot { Screen = PresenceScreen.Menu }, Texts());
        for (int s = 0; s <= 60; s++) p.Tick(T.AddSeconds(s));
        Assert.Single(_pipe.Activities);
    }

    [Fact]
    public void ScoreOnlyChange_WaitsFifteenSeconds()
    {
        var p = NewPresence();
        p.Update(Level(remaining: 120), Texts());
        p.Tick(T);
        p.Update(Level(score: 200, remaining: 119), Texts());
        p.Tick(T.AddSeconds(1));
        p.Update(Level(score: 200, remaining: 106), Texts());
        p.Tick(T.AddSeconds(14));
        Assert.Single(_pipe.Activities);
        p.Update(Level(score: 200, remaining: 105), Texts());
        p.Tick(T.AddSeconds(15));
        Assert.Equal(2, _pipe.Activities.Count);
        Assert.Equal("Hard · 200 pts · combo 5", (string)_pipe.Activities[1]["state"]);
    }

    [Fact]
    public void ScreenChange_WaitsFourSecondsOnly()
    {
        var p = NewPresence();
        p.Update(new PresenceSnapshot { Screen = PresenceScreen.Menu }, Texts());
        p.Tick(T);
        p.Update(new PresenceSnapshot { Screen = PresenceScreen.Marketplace }, Texts());
        p.Tick(T.AddSeconds(1));
        p.Tick(T.AddSeconds(3));
        Assert.Single(_pipe.Activities);
        p.Tick(T.AddSeconds(4));
        Assert.Equal("Marketplace", (string)_pipe.Activities[1]["details"]);
    }

    [Fact]
    public void EndDrift_UnderTwoSeconds_IsNotAChange()
    {
        var p = NewPresence();
        p.Update(Level(remaining: 120), Texts());
        p.Tick(T);
        for (int s = 1; s <= 14; s++)
        {
            p.Update(Level(remaining: 120 - s + (s % 2 == 0 ? 0.5 : -0.5)), Texts());
            p.Tick(T.AddSeconds(s));
        }
        Assert.Single(_pipe.Activities);
    }

    [Fact]
    public void Countdown_JumpAfterPause_IsAMajorChange()
    {
        var p = NewPresence();
        p.Update(Level(remaining: 120), Texts());
        p.Tick(T);
        p.Update(Level(remaining: 120), Texts()); // 5 s later, still 120 s left: the song was paused
        p.Tick(T.AddSeconds(5));
        Assert.Equal(2, _pipe.Activities.Count);
    }

    [Fact]
    public void Hidden_ClearsTheActivity()
    {
        var p = NewPresence();
        p.Update(new PresenceSnapshot { Screen = PresenceScreen.Menu }, Texts());
        p.Tick(T);
        p.Update(new PresenceSnapshot { Screen = PresenceScreen.Hidden }, Texts());
        p.Tick(T.AddSeconds(4));
        Assert.Equal(2, _pipe.Activities.Count);
        Assert.Equal(JTokenType.Null, _pipe.Activities[1].Type);
    }

    [Fact]
    public void Reconnects_AfterBackoff()
    {
        _pipe.RefuseOpens = 2;
        var p = NewPresence();
        p.Update(new PresenceSnapshot { Screen = PresenceScreen.Menu }, Texts());
        p.Tick(T);
        p.Tick(T.AddSeconds(4));
        Assert.Equal(1, _pipe.Opens);
        p.Tick(T.AddSeconds(5));
        Assert.Equal(2, _pipe.Opens);
        p.Tick(T.AddSeconds(14));
        Assert.Equal(2, _pipe.Opens);
        p.Tick(T.AddSeconds(15));
        Assert.Equal(3, _pipe.Opens);
        Assert.Single(_pipe.Activities);
        Assert.Single(_logs, l => l.Contains("paused"));
    }

    [Fact]
    public void WriteFailure_DropsConnection_AndBacksOff()
    {
        var p = NewPresence();
        p.Update(new PresenceSnapshot { Screen = PresenceScreen.Menu }, Texts());
        p.Tick(T);
        _pipe.ThrowOnWrite = true;
        p.Update(new PresenceSnapshot { Screen = PresenceScreen.Marketplace }, Texts());
        p.Tick(T.AddSeconds(4)); // throws inside, never out
        _pipe.ThrowOnWrite = false;
        p.Tick(T.AddSeconds(8));
        Assert.Equal(1, _pipe.Opens);
        p.Tick(T.AddSeconds(9));
        Assert.Equal(2, _pipe.Opens);
        Assert.Equal(IpcOpcode.Handshake, _pipe.Sent[^2].op);
        Assert.Equal("Marketplace", (string)_pipe.Activities[^1]["details"]);
    }

    [Fact]
    public void Reconnect_ResendsAnUnchangedWish()
    {
        var p = NewPresence();
        p.Update(new PresenceSnapshot { Screen = PresenceScreen.Menu }, Texts());
        p.Tick(T);
        _pipe.Incoming.Enqueue(IpcFrame.Encode(IpcOpcode.Close, "{\"code\":1000}"));
        p.Tick(T.AddSeconds(1));
        p.Tick(T.AddSeconds(6));
        Assert.Equal(2, _pipe.Opens);
        Assert.Equal(2, _pipe.Activities.Count);
    }

    [Fact]
    public void Ping_IsAnsweredWithPong()
    {
        var p = NewPresence();
        p.Update(new PresenceSnapshot { Screen = PresenceScreen.Menu }, Texts());
        p.Tick(T);
        _pipe.Incoming.Enqueue(IpcFrame.Encode(IpcOpcode.Ping, "{\"x\":1}"));
        p.Tick(T.AddSeconds(1));
        Assert.Equal(IpcOpcode.Pong, _pipe.Sent[^1].op);
        Assert.Equal(1, (int)_pipe.Sent[^1].json["x"]);
    }

    [Fact]
    public void NoWish_SendsNothing()
    {
        var p = NewPresence();
        p.Tick(T);
        Assert.Empty(_pipe.Activities);
    }
}
