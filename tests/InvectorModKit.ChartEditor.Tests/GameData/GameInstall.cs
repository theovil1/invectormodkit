using Microsoft.Win32;

namespace InvectorModKit.ChartEditor.Tests;

/// <summary>
/// The installed game, found the way the editor finds it (or from INVECTOR_GAME_DIR). Tests that read it are skipped
/// on machines without it, such as CI: nothing from the game is ever stored in the repository.
/// </summary>
static class GameInstall
{
    public static readonly string Dir = Find();

    static string Find()
    {
        foreach (var candidate in Candidates())
        {
            try
            {
                if (GameLocator.IsGameDir(candidate)) return candidate;
            }
            catch (Exception) { } // a broken setting is not a game
        }
        return null;
    }

    static IEnumerable<string> Candidates()
    {
        yield return Environment.GetEnvironmentVariable("INVECTOR_GAME_DIR");
        string settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InvectorChartEditor");
        string fromSettings = null;
        try { fromSettings = EditorSettings.Load(settings).GameDir; } catch (Exception) { }
        yield return fromSettings;
        if (OperatingSystem.IsWindows())
            yield return GameLocator.Find(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string);
    }
}

/// <summary>A test that reads the installed game; skipped when the game is not found.</summary>
sealed class GameFactAttribute : FactAttribute
{
    public GameFactAttribute()
    {
        if (GameInstall.Dir == null) Skip = "Invector Rhythm Galaxy is not installed";
    }
}
