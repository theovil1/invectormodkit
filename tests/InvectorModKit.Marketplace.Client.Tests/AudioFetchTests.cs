using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

public class AudioFetchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "imk-audiofetch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    // The chart already matches any audio of the same song name that the player has.
    [Fact]
    public void Existing_FindsThePlayersCopy() => Assert.Null(AudioFetch.Existing(_dir, "A", "T"));

    [Fact]
    public void Args_DownloadVorbisOfTheVideoIntoTheTargetFile()
    {
        string args = AudioFetch.BuildArgs("dQw4w9WgXcQ", @"C:songs\Artist - Title", @"toolsdir");
        Assert.Contains("-x", args);
        Assert.Contains("--audio-format vorbis", args);
        Assert.Contains("--no-playlist", args);
        Assert.Contains("--ffmpeg-location", args);
        Assert.Contains("toolsdir", args);
        Assert.Contains("https://youtu.be/dQw4w9WgXcQ", args);
        Assert.Contains(@"""C:songs\Artist - Title""", args);
        Assert.EndsWith("https://youtu.be/dQw4w9WgXcQ", args);
    }

    [Fact]
    public void Target_SongNameWithoutInvalidCharacters()
    {
        Assert.Equal(Path.Combine("C:songs", "A - T.ogg"), AudioFetch.Target("C:songs", "A", "T"));
        Assert.Equal(Path.Combine("C:songs", "X - a__b_c.ogg"), AudioFetch.Target("C:songs", "X", "a*?b:c"));
        Assert.Equal(Path.Combine("C:songs", "Artist - Song 12.ogg"), AudioFetch.Target("C:songs", "  Artist  ", "  Song 12  "));
    }

    [Fact]
    public void Existing_LocalExtensionCounts()
    {
        Directory.CreateDirectory(_dir);
        string wav = Path.Combine(_dir, "A - T.wav");
        File.WriteAllBytes(wav, [0]);
        Assert.Equal(wav, AudioFetch.Existing(_dir, "A", "T"));
        File.WriteAllBytes(Path.Combine(_dir, "A - T.ogg"), [0]);
        Assert.Equal(Path.Combine(_dir, "A - T.ogg"), AudioFetch.Existing(_dir, "A", "T"));
    }

    [Fact]
    public void Progress_ReadsYtDlpPercent()
    {
        Assert.Equal(0.423, AudioFetch.ProgressOf("[download]  42.3% of    3.10MiB at  1.20MiB/s ETA 00:02")!.Value, 3);
        Assert.Equal(1.0, AudioFetch.ProgressOf("[download] 100% of    3.10MiB in 00:00:02")!.Value, 3);
        Assert.Null(AudioFetch.ProgressOf("[ExtractAudio] Destination: x.ogg"));
        Assert.Null(AudioFetch.ProgressOf(null));
    }

    [Fact]
    public void Reason_SaysWhyInPlainWords()
    {
        Assert.Equal("reason_video_gone", AudioFetch.ReasonOf("ERROR: [youtube] abc: Video unavailable. This video has been removed by the uploader"));
        Assert.Equal("reason_video_gone", AudioFetch.ReasonOf("ERROR: [youtube] abc: Private video. Sign in if you've been granted access"));
        Assert.Equal("reason_video_blocked", AudioFetch.ReasonOf("ERROR: [youtube] abc: The uploader has not made this video available in your country"));
        Assert.Equal("reason_no_internet", AudioFetch.ReasonOf("ERROR: [youtube] abc: Unable to download API page: <urlopen error [Errno 11001] getaddrinfo failed>"));
        Assert.Equal("reason_tools_missing", AudioFetch.ReasonOf("ERROR: Postprocessing: ffprobe and ffmpeg not found. Please install or provide the path"));
        Assert.Null(AudioFetch.ReasonOf("ERROR: something new"));
    }

    [Fact]
    public void Args_PrintProgressOnNewLines()
    {
        Assert.Contains("--newline", AudioFetch.BuildArgs("id", "out", "tools"));
        Assert.Contains("--newline", AudioFetch.BuildSectionArgs("id", "out", "tools", 1, 21));
    }

    [Fact]
    public void MoveFileIntoSongsFolder()
    {
        Directory.CreateDirectory(_dir);
        string tmp = Path.Combine(_dir, "tmp");
        File.WriteAllBytes(tmp, new byte[] { 1, 2 });
        string dest = Path.Combine(_dir, "out.ogg");
        AudioFetch.Move(tmp, dest);
        Assert.False(File.Exists(tmp));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(dest));
    }
}
