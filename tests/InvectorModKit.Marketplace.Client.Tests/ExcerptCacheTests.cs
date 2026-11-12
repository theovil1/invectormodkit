using System.Globalization;
using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>Excerpts fetched from YouTube: only that section, kept a while, then deleted.</summary>
public class ExcerptCacheTests
{
    private const long MB = 1024 * 1024;
    private static readonly DateTime Now = new DateTime(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SectionArgs_FetchOnlyTheExcerpt()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            string args = AudioFetch.BuildSectionArgs("dQw4w9WgXcQ", @"C:\c\x", "tools", 61.5, 81.5);
            Assert.Contains("--download-sections *61.5-81.5", args);
            Assert.Contains("-x", args);
            Assert.Contains("--audio-format vorbis", args);
            Assert.Contains("--no-playlist", args);
            Assert.Contains("--ffmpeg-location tools", args);
            Assert.EndsWith("https://youtu.be/dQw4w9WgXcQ", args);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void Cache_FileNameIsStable() => Assert.Equal("abc-61.5.ogg", ExcerptCache.FileName("abc", 61.54));

    [Fact]
    public void Cache_DeletesOldFiles()
    {
        var files = new[]
        {
            new CachedFile { Path = "old.ogg", Bytes = MB, LastUsed = Now.AddDays(-8) },
            new CachedFile { Path = "recent.ogg", Bytes = MB, LastUsed = Now.AddDays(-1) },
        };
        Assert.Equal(new[] { "old.ogg" }, ExcerptCache.ToDelete(files, Now));
    }

    [Fact]
    public void Cache_TrimsOldestBeyondTheLimit()
    {
        var files = new[]
        {
            new CachedFile { Path = "today.ogg", Bytes = 90 * MB, LastUsed = Now },
            new CachedFile { Path = "two-days.ogg", Bytes = 90 * MB, LastUsed = Now.AddDays(-2) },
            new CachedFile { Path = "yesterday.ogg", Bytes = 90 * MB, LastUsed = Now.AddDays(-1) },
        };
        Assert.Equal(new[] { "two-days.ogg" }, ExcerptCache.ToDelete(files, Now));
    }
}
