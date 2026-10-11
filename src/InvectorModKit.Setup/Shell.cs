using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;

namespace InvectorModKit.Setup
{
    /// <summary>
    /// What the installer does in Windows itself: the "Installed apps" entry (uninstall), its own copy that entry
    /// runs, the level editor's shortcuts, running as administrator when writing is refused, its log.
    /// </summary>
    internal static class Shell
    {
        public const string AppId = "InvectorModKit";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId;
        private const string SteamRun = "steam://rungameid/2009450";

        public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppId);
        public static string SelfCopy => Path.Combine(DataDir, "InvectorModKitSetup.exe");
        private static string SelfPath => Assembly.GetExecutingAssembly().Location;

        public static string Version
        {
            get
            {
                var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
                int plus = info.IndexOf('+');
                return plus >= 0 ? info.Substring(0, plus) : info;
            }
        }

        public static string SteamPath() => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;

        /// <summary>The game folder the last installation used (from the "Installed apps" entry), or null.</summary>
        public static string RegisteredGameDir()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
                return key?.GetValue("InstallLocation") as string;
        }

        /// <summary>Copies the installer next to its data and lists the mod in Windows' "Installed apps".</summary>
        public static void Register(string gameDir, string version)
        {
            Directory.CreateDirectory(DataDir);
            if (!string.Equals(Path.GetFullPath(SelfPath), Path.GetFullPath(SelfCopy), StringComparison.OrdinalIgnoreCase))
                File.Copy(SelfPath, SelfCopy, true);
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "InvectorModKit");
                key.SetValue("DisplayVersion", version);
                key.SetValue("Publisher", "InvectorModKit");
                key.SetValue("DisplayIcon", SelfCopy);
                key.SetValue("InstallLocation", gameDir);
                key.SetValue("UninstallString", $"\"{SelfCopy}\" --uninstall");
                key.SetValue("QuietUninstallString", $"\"{SelfCopy}\" --uninstall --yes");
                key.SetValue("URLInfoAbout", "https://github.com/theovil1/invectormodkit");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        /// <summary>Removes the "Installed apps" entry and, once this process has ended, the installer's own copy.</summary>
        public static void Unregister()
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            if (!Directory.Exists(DataDir)) return;
            // the copy may be the running exe: a hidden command deletes the folder a few seconds later
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 3 /nobreak >nul & rmdir /s /q \"{DataDir}\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }

        // ----- the level editor's shortcuts

        private static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Invector Level Editor.lnk");
        private static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Invector Level Editor.lnk");

        public static void CreateShortcuts(string exe)
        {
            foreach (string link in new[] { StartMenuShortcut, DesktopShortcut })
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic shortcut = shell.CreateShortcut(link);
                shortcut.TargetPath = exe;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
                shortcut.IconLocation = exe + ",0";
                shortcut.Description = "Invector Level Editor";
                shortcut.Save();
            }
        }

        public static void RemoveShortcuts()
        {
            foreach (string link in new[] { StartMenuShortcut, DesktopShortcut })
                if (File.Exists(link)) File.Delete(link);
        }

        // ----- running

        public static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        /// <summary>Starts this installer again as administrator (Windows asks the player); false if they refused.</summary>
        public static bool RelaunchAsAdministrator(string arguments)
        {
            try
            {
                Process.Start(new ProcessStartInfo(SelfPath, arguments) { UseShellExecute = true, Verb = "runas" });
                return true;
            }
            catch (System.ComponentModel.Win32Exception) { return false; } // "No" in Windows' question
        }

        public static void LaunchGame()
        {
            try { Process.Start(new ProcessStartInfo(SteamRun) { UseShellExecute = true }); }
            catch (Exception e) { Log("launch: " + e.Message); }
        }

        public static void Log(string line)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(Path.Combine(DataDir, "setup.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
            catch (IOException) { }
        }

        public static Stream Resource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
    }
}
