using System;
using InvectorModKit.Installer;

namespace InvectorModKit.Setup
{
    /// <summary>Install and uninstall as the window and the silent modes both run them.</summary>
    internal static class Actions
    {
        /// <summary>The mod (and the editor when asked, or when it is already there to update), then Windows' entry.</summary>
        public static void Install(string gameDir, bool withEditor, Action<double> progress)
        {
            using (var payload = Shell.Resource("payload.zip"))
            {
                if (payload == null) throw new InvalidOperationException("this installer has no payload (build it with tools/release.ps1)");
                ModInstaller.Install(gameDir, payload, Shell.Version, p => progress?.Invoke(p * 0.85));
            }
            if (withEditor || EditorInstaller.IsInstalled(EditorInstaller.DefaultDir))
                using (var editor = Shell.Resource("editor.zip"))
                    if (editor != null)
                    {
                        string exe = EditorInstaller.Install(editor, EditorInstaller.DefaultDir);
                        try { Shell.CreateShortcuts(exe); }
                        catch (Exception e) { Shell.Log("shortcuts: " + e.Message); } // the editor works without them
                    }
            Shell.Register(gameDir, Shell.Version);
            progress?.Invoke(1);
            Shell.Log($"installed {Shell.Version} in {gameDir}");
        }

        public static void Uninstall(string gameDir)
        {
            if (GameFolder.IsGame(gameDir)) ModInstaller.Uninstall(gameDir);
            EditorInstaller.Uninstall(EditorInstaller.DefaultDir);
            Shell.RemoveShortcuts();
            Shell.Log($"uninstalled from {gameDir}");
            Shell.Unregister();
        }

        /// <summary>The game: the folder given, else the last installation's, else Steam's libraries.</summary>
        public static string FindGame(string given)
        {
            if (GameFolder.IsGame(given)) return given;
            string registered = Shell.RegisteredGameDir();
            if (GameFolder.IsGame(registered)) return registered;
            return GameFolder.Find(Shell.SteamPath());
        }

        /// <summary>The window's message for an error.</summary>
        public static string Explain(Exception e, string lang)
        {
            if (e is InstallException install) return SetupTexts.Get(lang, "err_" + install.Code);
            return SetupTexts.Get(lang, "err_other", e.Message);
        }
    }
}
