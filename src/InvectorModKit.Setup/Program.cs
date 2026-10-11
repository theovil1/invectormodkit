using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using InvectorModKit.Installer;

namespace InvectorModKit.Setup
{
    /// <summary>
    /// InvectorModKitSetup.exe
    ///   (no argument)                         the window
    ///   --update --game "dir" --wait pid      silent: waits for the game to close, then installs (in-game updates)
    ///   --uninstall [--game "dir"] [--yes]    removes the mod (Windows' "Installed apps" runs this)
    ///   --render out.png [--lang xx]          draws the window into an image (checking the languages)
    ///   --game "dir", --auto, --editor, --lang xx   used when it restarts itself as administrator
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // paths over 260 characters (deep Steam libraries), where Windows allows them; no .config file beside the exe
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            string lang = SetupTexts.Resolve(Arg(args, "--lang") ?? CultureInfo.CurrentUICulture.Name);
            using (var single = new Mutex(false, @"Local\InvectorModKitSetup"))
            {
                bool render = Has(args, "--render");
                if (!render && !single.WaitOne(Has(args, "--update") ? Timeout.Infinite : 0))
                    return 1; // another installer is working on the same game
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    if (render) return Render(Arg(args, "--render"), lang);
                    if (Has(args, "--update")) return Update(Arg(args, "--game"), Arg(args, "--wait"));
                    if (Has(args, "--uninstall")) return Uninstall(Actions.FindGame(Arg(args, "--game")), Has(args, "--yes"), lang);
                    Application.Run(new SetupForm(lang, Actions.FindGame(Arg(args, "--game")), Has(args, "--editor")) { AutoStart = Has(args, "--auto") });
                    return 0;
                }
                finally
                {
                    if (!render) single.ReleaseMutex();
                }
            }
        }

        /// <summary>The in-game update: the game started this and is closing; install once it is gone.</summary>
        private static int Update(string game, string waitPid)
        {
            Shell.Log($"update to {Shell.Version} queued for {game}");
            try
            {
                if (int.TryParse(waitPid, out int pid) && pid > 0)
                    try { Process.GetProcessById(pid).WaitForExit(); }
                    catch (ArgumentException) { } // already closed
                while (GameFolder.IsRunning()) Thread.Sleep(2000);
                Actions.Install(game, false, null);
                return 0;
            }
            catch (Exception e)
            {
                Shell.Log("update failed: " + e);
                return 2;
            }
        }

        private static int Uninstall(string game, bool yes, string lang)
        {
            string songs = game == null ? "CustomSongs" : Path.Combine(game, "CustomSongs");
            if (!yes && MessageBox.Show(SetupTexts.Get(lang, "uninstallConfirm", songs), "InvectorModKit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return 1;
            while (GameFolder.IsRunning())
            {
                if (yes) { Thread.Sleep(2000); continue; }
                if (MessageBox.Show(SetupTexts.Get(lang, "closeGame"), "InvectorModKit", MessageBoxButtons.RetryCancel, MessageBoxIcon.Information) != DialogResult.Retry) return 1;
            }
            try
            {
                Actions.Uninstall(game);
                if (!yes) MessageBox.Show(SetupTexts.Get(lang, "uninstalled", songs), "InvectorModKit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception e)
            {
                Shell.Log("uninstall failed: " + e);
                if (!yes) MessageBox.Show(Actions.Explain(e, lang), "InvectorModKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }
        }

        private static int Render(string path, string lang)
        {
            using (var form = new SetupForm(lang, Actions.FindGame(null), false))
            {
                // shown far off-screen: controls only draw once their window exists
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                using (var whole = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(whole, new Rectangle(Point.Empty, form.Size));
                    var origin = form.PointToScreen(Point.Empty);
                    var client = new Rectangle(origin.X - form.Left, origin.Y - form.Top, form.ClientSize.Width, form.ClientSize.Height);
                    using (var image = whole.Clone(client, whole.PixelFormat)) image.Save(path, ImageFormat.Png);
                }
                form.Close();
            }
            return 0;
        }

        private static bool Has(string[] args, string name) => Array.Exists(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        private static string Arg(string[] args, string name)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
