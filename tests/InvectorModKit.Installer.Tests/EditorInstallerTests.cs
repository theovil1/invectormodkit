using System.IO.Compression;
using InvectorModKit.Installer;

namespace InvectorModKit.Installer.Tests;

public sealed class EditorInstallerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "imk-editor-" + Guid.NewGuid().ToString("N"), "InvectorLevelEditor");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_dir), true); } catch (IOException) { }
    }

    /// <summary>The editor's zip as pack.ps1 makes it: everything under InvectorLevelEditor/.</summary>
    private static MemoryStream EditorZip(params string[] files)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (string f in files)
                using (var w = new StreamWriter(zip.CreateEntry("InvectorLevelEditor/" + f).Open())) w.Write(f);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Install_ExtractsWithoutTheTopFolder_AndReplacesAnOldVersion()
    {
        EditorInstaller.Install(EditorZip("InvectorLevelEditor.exe", "ui/old.js"), _dir);
        string exe = EditorInstaller.Install(EditorZip("InvectorLevelEditor.exe", "ui/app.js"), _dir);

        Assert.Equal(Path.Combine(_dir, "InvectorLevelEditor.exe"), exe);
        Assert.True(File.Exists(Path.Combine(_dir, "ui", "app.js")));
        Assert.False(File.Exists(Path.Combine(_dir, "ui", "old.js")));
        Assert.True(EditorInstaller.IsInstalled(_dir));
    }

    [Fact]
    public void Uninstall_RemovesTheFolder()
    {
        EditorInstaller.Install(EditorZip("InvectorLevelEditor.exe"), _dir);
        EditorInstaller.Uninstall(_dir);
        Assert.False(Directory.Exists(_dir));
        Assert.False(EditorInstaller.IsInstalled(_dir));
    }
}
