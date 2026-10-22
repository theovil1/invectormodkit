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
