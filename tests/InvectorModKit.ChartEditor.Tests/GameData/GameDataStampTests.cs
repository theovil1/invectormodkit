using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor.Tests;

public class GameDataStampTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "imk-stamp-" + Guid.NewGuid().ToString("N"));
    string Game => Path.Combine(root, "game");
    string Cache => Path.Combine(root, "cache");

    public GameDataStampTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GameDataStamp.CatalogPath(Game))!);
        File.WriteAllText(GameDataStamp.CatalogPath(Game), "\"v1\"");
        Directory.CreateDirectory(Cache);
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    string IndexPath => Path.Combine(Cache, "index.json");
    void WriteIndex() => File.WriteAllText(IndexPath, GameDataStamp.NewIndex(Game).ToString());

    [Fact]
    public void PathsFollowTheGameLayout()
    {
        Assert.Equal(Path.Combine(Game, "Invector Rhythm Galaxy_Data"), GameDataStamp.DataDir(Game));
        Assert.Equal(Path.Combine(Game, "Invector Rhythm Galaxy_Data", "StreamingAssets", "aa", "StandaloneWindows64"), GameDataStamp.BundlesDir(Game));
        Assert.Equal(Path.Combine(Game, "Invector Rhythm Galaxy_Data", "StreamingAssets", "aa", "catalog.json"), GameDataStamp.CatalogPath(Game));
    }

    [Fact]
    public void MissingIndexIsStale() => Assert.False(GameDataStamp.IsFresh(Game, Cache));

    [Fact]
    public void IndexOfTheSameCatalogIsFresh()
    {
        WriteIndex();
        Assert.True(GameDataStamp.IsFresh(Game, Cache));
        var index = GameDataStamp.NewIndex(Game);
        Assert.Matches("^[0-9a-f]{64}$", (string)index["catalogSha256"]!);
        Assert.Equal(GameDataRecipe.Version, (int)index["recipeVersion"]!);
        Assert.NotNull(index["built"]);
    }

    [Fact]
    public void ChangedCatalogIsStale()
    {
        WriteIndex();
        File.WriteAllText(GameDataStamp.CatalogPath(Game), "\"v2\"");
        Assert.False(GameDataStamp.IsFresh(Game, Cache));
    }

    [Fact]
    public void OtherRecipeVersionIsStale()
    {
        var index = GameDataStamp.NewIndex(Game);
        index["recipeVersion"] = GameDataRecipe.Version + 1;
        File.WriteAllText(IndexPath, index.ToString());
        Assert.False(GameDataStamp.IsFresh(Game, Cache));
    }

    [Fact]
    public void CorruptIndexIsStale()
    {
        File.WriteAllText(IndexPath, "{\"catalogSha2");
        Assert.False(GameDataStamp.IsFresh(Game, Cache));
    }

    [Fact]
    public void MissingCatalogIsStale()
    {
        WriteIndex();
        File.Delete(GameDataStamp.CatalogPath(Game));
        Assert.False(GameDataStamp.IsFresh(Game, Cache));
    }
}
