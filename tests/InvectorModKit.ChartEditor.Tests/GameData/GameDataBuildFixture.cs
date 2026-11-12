using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor.Tests;

/// <summary>One build of the game data cache from the installed game, into a temporary folder, shared by a test class.</summary>
public sealed class GameDataBuildFixture : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "imk-gamedata-" + Guid.NewGuid().ToString("N"));

    public GameDataBuildFixture()
    {
        if (GameInstall.Dir == null) return; // the tests using it are skipped
        Report = GameData.Build(GameInstall.Dir, Dir, null);
        Index = JObject.Parse(File.ReadAllText(Path.Combine(Dir, "index.json")));
    }

    public string Dir => Path.Combine(root, "game");
    public GameDataReport Report { get; }
    public JObject Index { get; }

    public JToken Json(string relativePath) => JToken.Parse(File.ReadAllText(Path.Combine(Dir, relativePath)));

    public void Dispose() { try { Directory.Delete(root, true); } catch { } }
}
