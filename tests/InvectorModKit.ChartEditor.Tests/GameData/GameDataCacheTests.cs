namespace InvectorModKit.ChartEditor.Tests;

public class GameDataCacheTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "imk-cache-" + Guid.NewGuid().ToString("N"));
    readonly GameDataCache cache;

    public GameDataCacheTests()
    {
        Directory.CreateDirectory(root);
        cache = new GameDataCache(Path.Combine(root, "game"));
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    void WriteOld()
    {
        Directory.CreateDirectory(cache.Dir);
        File.WriteAllText(Path.Combine(cache.Dir, "a.txt"), "old");
    }

    string Read(string dir) => File.ReadAllText(Path.Combine(dir, "a.txt"));

    [Fact]
    public void BuildSwapsTheNewContentIn()
    {
        WriteOld();

        var result = cache.Rebuild(tmp => File.WriteAllText(Path.Combine(tmp, "a.txt"), "new"), out var error);

        Assert.Equal(RebuildResult.Built, result);
        Assert.Null(error);
        Assert.Equal("new", Read(cache.Dir));
        Assert.False(Directory.Exists(cache.TempDir));
        Assert.False(Directory.Exists(cache.OldDir));
        Assert.False(File.Exists(cache.LockPath));
    }

    [Fact]
    public void FailedBuildKeepsTheOldCache()
    {
        WriteOld();

        var result = cache.Rebuild(tmp =>
        {
            File.WriteAllText(Path.Combine(tmp, "a.txt"), "half");
            throw new InvalidOperationException("boom");
        }, out var error);

        Assert.Equal(RebuildResult.Failed, result);
        Assert.Equal("boom", error.Message);
        Assert.Equal("old", Read(cache.Dir));
        Assert.False(Directory.Exists(cache.TempDir));
    }

    [Fact]
    public void FirstBuildWithoutCache()
    {
        var result = cache.Rebuild(tmp => File.WriteAllText(Path.Combine(tmp, "a.txt"), "new"), out _);

        Assert.Equal(RebuildResult.Built, result);
        Assert.Equal("new", Read(cache.Dir));
    }

    [Fact]
    public void SecondBuilderIsBusy()
    {
        WriteOld();
        bool called = false;

        RebuildResult result;
        Exception error;
        using (new FileStream(cache.LockPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            result = cache.Rebuild(_ => called = true, out error);

        Assert.Equal(RebuildResult.Busy, result);
        Assert.Null(error);
        Assert.False(called);
        Assert.Equal("old", Read(cache.Dir));
    }

    [Fact]
    public void RecoverRestoresTheOldCacheAfterACrashMidSwap()
    {
        Directory.CreateDirectory(cache.OldDir);
        File.WriteAllText(Path.Combine(cache.OldDir, "a.txt"), "old");
        Directory.CreateDirectory(cache.TempDir);
        File.WriteAllText(Path.Combine(cache.TempDir, "partial.bin"), "x");

        cache.Recover();

        Assert.Equal("old", Read(cache.Dir));
        Assert.False(Directory.Exists(cache.OldDir));
        Assert.False(Directory.Exists(cache.TempDir));
    }
}
