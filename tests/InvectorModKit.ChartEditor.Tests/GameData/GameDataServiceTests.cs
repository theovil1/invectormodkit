namespace InvectorModKit.ChartEditor.Tests;

public class GameDataServiceTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "imk-service-" + Guid.NewGuid().ToString("N"));
    string Game => Path.Combine(root, "game-install");
    string Data => Path.Combine(root, "editor");
    readonly List<GameDataState> changes = new();
    readonly List<string> builds = new();

    public GameDataServiceTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GameDataStamp.CatalogPath(Game))!);
        File.WriteAllText(Path.Combine(Game, GameLocator.GameFolder + ".exe"), "");
        File.WriteAllText(GameDataStamp.CatalogPath(Game), "\"v1\"");
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    GameDataService Service(Func<string, string, IProgress<string>, GameDataReport> build)
    {
        var service = new GameDataService(Data, (game, cache, progress) =>
        {
            builds.Add(cache);
            return build(game, cache, progress);
        });
        service.Changed += changes.Add;
        return service;
    }

    /// <summary>What GameData.Build leaves: a cache whose index matches the game's catalog.</summary>
    GameDataReport WriteCache(string game, string cache, IProgress<string> progress)
    {
        progress?.Report("Prefab:Ship Beginner");
        Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(cache, "index.json"), GameDataStamp.NewIndex(game).ToString());
        return new GameDataReport();
    }

    string Log(GameDataService s) => File.ReadAllText(s.LogPath);

    [Fact]
    public void NoGameNoCacheIsNone()
    {
        var service = Service(WriteCache);

        service.Refresh(null);

        Assert.Equal(GameDataState.None, service.State);
        Assert.Empty(builds);
        Assert.Empty(changes);
    }

    [Fact]
    public void FreshCacheIsReadyWithoutReading()
    {
        var service = Service(WriteCache);
        WriteCache(Game, service.CacheDir, null);

        service.Refresh(Game);

        Assert.Equal(new[] { GameDataState.Ready }, changes);
        Assert.Empty(builds);
    }

    [Fact]
    public void StaleCacheIsRebuilt()
    {
        var service = Service(WriteCache);

        service.Refresh(Game);

        Assert.Equal(new[] { GameDataState.Reading, GameDataState.Ready }, changes);
        Assert.Equal(new[] { service.CacheDir }, builds);
        Assert.Contains(Game, Log(service));
        Assert.Contains("Prefab:Ship Beginner", Log(service));
    }

    [Fact]
    public void FailureIsReportedAndLogged()
    {
        var service = Service((_, _, _) => throw new IOException("disk full"));

        service.Refresh(Game);

        Assert.Equal(new[] { GameDataState.Reading, GameDataState.Failed }, changes);
        Assert.Contains("disk full", Log(service));
    }

    [Fact]
    public void BusyKeepsReadyWhenACacheExists()
    {
        var service = Service((_, _, _) => new GameDataReport { Busy = true });
        WriteCache(Game, service.CacheDir, null);
        File.WriteAllText(GameDataStamp.CatalogPath(Game), "\"v2\""); // the game was updated: the cache is stale

        service.Refresh(Game);

        Assert.Equal(GameDataState.Ready, service.State);
        Assert.Single(builds);
    }

    [Fact]
    public void BusyWithoutCacheIsNone()
    {
        var service = Service((_, _, _) => new GameDataReport { Busy = true });

        service.Refresh(Game);

        Assert.Equal(GameDataState.None, service.State);
    }

    [Fact]
    public async Task ConcurrentRefreshBuildsOnce()
    {
        var timeout = TimeSpan.FromSeconds(10);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var service = Service((game, cache, progress) =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return WriteCache(game, cache, progress);
        });

        var first = Task.Run(() => service.Refresh(Game));
        Assert.True(started.Wait(timeout));
        var second = Task.Run(() => service.Refresh(Game));
        Assert.True(second == await Task.WhenAny(second, Task.Delay(timeout)), "the second refresh waited for the first one");
        release.Set();
        Assert.True(first == await Task.WhenAny(first, Task.Delay(timeout)));

        Assert.Single(builds);
        Assert.Equal(GameDataState.Ready, service.State);
    }
}
