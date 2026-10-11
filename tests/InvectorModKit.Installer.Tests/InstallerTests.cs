using System.IO.Compression;
using InvectorModKit.Installer;

namespace InvectorModKit.Installer.Tests;

public sealed class InstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imk-installer-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>A folder that looks like the game: its exe and its data folder.</summary>
    private string Game(string relative = "Invector Rhythm Galaxy")
    {
        string dir = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.Combine(dir, "Invector Rhythm Galaxy_Data"));
        File.WriteAllText(Path.Combine(dir, "Invector Rhythm Galaxy.exe"), "game");
        return dir;
    }

    private static void Put(string gameDir, string relative, string content)
    {
        string path = Path.Combine(gameDir, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, content);
    }

    private static string Read(string gameDir, string relative) => File.ReadAllText(Path.Combine(gameDir, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static bool Has(string gameDir, string relative) => File.Exists(Path.Combine(gameDir, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The release's payload: BepInEx, its loader, our plugins (in the game folder's layout).</summary>
    private static MemoryStream Payload(params string[] extra)
    {
        var files = new List<string>
        {
            "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "BepInEx/core/BepInEx.dll",
            "BepInEx/plugins/InvectorModKit/InvectorModKit.dll", "BepInEx/plugins/InvectorModKit.CustomSongs/vocals.fp16.onnx",
        };
        files.AddRange(extra);
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (string f in files)
                using (var w = new StreamWriter(zip.CreateEntry(f).Open())) w.Write("ours:" + f);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void FreshInstall_WritesPayloadAndReceipt()
    {
        string game = Game();
        var receipt = ModInstaller.Install(game, Payload(), "0.1.0", _ => { });
        Assert.True(Has(game, "winhttp.dll"));
        Assert.Equal("ours:BepInEx/plugins/InvectorModKit/InvectorModKit.dll", Read(game, "BepInEx/plugins/InvectorModKit/InvectorModKit.dll"));
        Assert.Equal("0.1.0", receipt.Version);
        Assert.True(receipt.BepInExOurs);
        Assert.True(Directory.Exists(Path.Combine(game, "CustomSongs")));
        Assert.Equal("0.1.0", ModInstaller.InstalledVersion(game));
        Assert.False(Directory.Exists(Path.Combine(game, ".imk-staging")));
    }

    [Fact]
    public void ExistingBepInEx_IsKept()
    {
        string game = Game();
        Put(game, "BepInEx/core/BepInEx.dll", "theirs");
        Put(game, "winhttp.dll", "theirs");
        var receipt = ModInstaller.Install(game, Payload(), "0.1.0", _ => { });
        Assert.Equal("theirs", Read(game, "BepInEx/core/BepInEx.dll"));
        Assert.Equal("theirs", Read(game, "winhttp.dll"));
        Assert.False(receipt.BepInExOurs);
        Assert.True(Has(game, "BepInEx/plugins/InvectorModKit/InvectorModKit.dll"));
    }

    [Fact]
    public void BepInEx6_IsRefused()
    {
        string game = Game();
        Put(game, "BepInEx/core/BepInEx.Core.dll", "bepinex 6");
        var e = Assert.Throws<InstallException>(() => ModInstaller.Install(game, Payload(), "0.1.0", _ => { }));
        Assert.Equal("bepinex6", e.Code);
        Assert.False(Has(game, "BepInEx/plugins/InvectorModKit/InvectorModKit.dll"));
        Assert.Null(ModInstaller.InstalledVersion(game));
    }

    [Fact]
    public void Update_RemovesStaleFiles_KeepsSettings()
    {
        string game = Game();
        ModInstaller.Install(game, Payload("BepInEx/plugins/InvectorModKit/old.dll", "BepInEx/config/BepInEx.cfg"), "0.1.0", _ => { });
        Put(game, "BepInEx/config/BepInEx.cfg", "player settings");
        Put(game, "BepInEx/config/com.invectormodkit.core.cfg", "player settings");
        Put(game, "CustomSongs/a.mp3", "song");
        Put(game, "BepInEx/plugins/InvectorModKit.DevTools/x.dll", "dev");

        var receipt = ModInstaller.Install(game, Payload("BepInEx/config/BepInEx.cfg"), "0.2.0", _ => { });

        Assert.False(Has(game, "BepInEx/plugins/InvectorModKit/old.dll"));
        Assert.Equal("player settings", Read(game, "BepInEx/config/BepInEx.cfg"));
        Assert.Equal("player settings", Read(game, "BepInEx/config/com.invectormodkit.core.cfg"));
        Assert.Equal("song", Read(game, "CustomSongs/a.mp3"));
        Assert.Equal("dev", Read(game, "BepInEx/plugins/InvectorModKit.DevTools/x.dll"));
        Assert.Equal("0.2.0", receipt.Version);
        Assert.True(receipt.BepInExOurs); // still ours from the first install
    }

    [Fact]
    public void Uninstall_RemovesOursAndBepInEx()
    {
        string game = Game();
        ModInstaller.Install(game, Payload(), "0.1.0", _ => { });
        Put(game, "CustomSongs/a.mp3", "song");
        Put(game, "BepInEx/config/BepInEx.cfg", "generated by BepInEx");

        ModInstaller.Uninstall(game);

        Assert.False(Has(game, "winhttp.dll"));
        Assert.False(Has(game, "doorstop_config.ini"));
        Assert.False(Directory.Exists(Path.Combine(game, "BepInEx")));
        Assert.Equal("song", Read(game, "CustomSongs/a.mp3"));
        Assert.True(Has(game, "Invector Rhythm Galaxy.exe"));
        Assert.Null(ModInstaller.InstalledVersion(game));
    }

    [Fact]
    public void Uninstall_KeepsBepInExWithOtherPlugins()
    {
        string game = Game();
        ModInstaller.Install(game, Payload(), "0.1.0", _ => { });
        Put(game, "BepInEx/plugins/OtherMod/o.dll", "other");
        Put(game, "BepInEx/config/com.invectormodkit.core.cfg", "ours");
        Put(game, "BepInEx/cache/InvectorModKit/editor-skin/manifest.json", "{}");

        ModInstaller.Uninstall(game);

        Assert.True(Has(game, "winhttp.dll"));
        Assert.True(Has(game, "BepInEx/core/BepInEx.dll"));
        Assert.Equal("other", Read(game, "BepInEx/plugins/OtherMod/o.dll"));
        Assert.False(Directory.Exists(Path.Combine(game, "BepInEx", "plugins", "InvectorModKit")));
        Assert.False(Has(game, "BepInEx/config/com.invectormodkit.core.cfg"));
        Assert.False(Directory.Exists(Path.Combine(game, "BepInEx", "cache", "InvectorModKit")));
    }

    [Fact]
    public void NotAGame_IsRefused()
    {
        string dir = Path.Combine(_root, "Documents");
        Directory.CreateDirectory(dir);
        var e = Assert.Throws<InstallException>(() => ModInstaller.Install(dir, Payload(), "0.1.0", _ => { }));
        Assert.Equal("not-game", e.Code);
    }

    [Fact]
    public void AccentedPath_Works()
    {
        string game = Game(Path.Combine("Jeux é", "Invector Rhythm Galaxy"));
        ModInstaller.Install(game, Payload(), "0.1.0", _ => { });
        Assert.True(GameFolder.IsGame(game));
        Assert.Equal("0.1.0", ModInstaller.InstalledVersion(game));
    }
}
