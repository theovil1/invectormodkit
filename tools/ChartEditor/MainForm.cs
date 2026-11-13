using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using InvectorModKit.Marketplace.Client.Discord;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor.Host
{
    /// <summary>
    /// The editor's window: the HTML interface in WebView2 (served from ui/ as https://app.editor/), the open song's
    /// audio as https://app.editor/audio/…, the game's look as https://app.editor/skin/… (the pictures the mod exports in
    /// game, and the game's fonts cut out of its bundles here), the game data for the 3D preview as
    /// https://app.editor/game/…, and the interface's messages answered here ("host.*") or by the EditorBackend.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private const string AppHost = "app.editor";
        private static readonly Color Background = Color.FromArgb(12, 9, 18);

        private readonly WebView2 _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
        private readonly EditorBackend _backend;
        private bool _ready, _allowClose;
        private string _audioPath;
        private readonly string _fontsDir;

        /// <summary>The folder of the last song zip exported or imported this session (the dialogs open there).</summary>
        private string _zipDir;
        private const string ZipFilter = "Zip (*.zip)|*.zip";

        /// <summary>The editor's copy of the game data for the 3D preview (%LOCALAPPDATA%\InvectorModKit\LevelEditor\game), served under /game/.</summary>
        private readonly GameDataService _gameData = new GameDataService(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InvectorModKit", "LevelEditor"));
        private string _gameDataDir;

        /// <summary>A song to open, from the command line or a later start; the page takes it with host.takeSong.</summary>
        private string _waitingSong;

        /// <summary>The level being made, on the player's Discord profile (null when turned off in settings).</summary>
        private readonly DiscordPresence _presence;

        public MainForm(string song = null, EditorInstance instance = null)
        {
            _waitingSong = song;
            Text = "Invector Level Editor";
            BackColor = Background;
            ClientSize = new Size(1560, 920);
            MinimumSize = new Size(1100, 680);
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            Controls.Add(_web);

            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InvectorChartEditor");
            var settings = EditorSettings.Load(data);
            if (!GameLocator.IsGameDir(settings.GameDir))
            {
                string found = GameLocator.Find(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string);
                if (found != null)
                {
                    settings.GameDir = found;
                    settings.Save();
                }
            }
            _backend = new EditorBackend(settings, OpenUrl);
            if (settings.DiscordPresence && !string.IsNullOrEmpty(DiscordPresence.EditorApplicationId))
            {
                _presence = new DiscordPresence(DiscordPresence.EditorApplicationId, Process.GetCurrentProcess().Id,
                    () => new Win32DiscordPipe(), m => Debug.WriteLine(m), DateTime.UtcNow);
                _presence.Start();
            }
            _fontsDir = Path.Combine(data, "fonts");
            _gameData.Changed += state => Post(new JObject
            {
                ["event"] = "gameData",
                ["data"] = new JObject { ["state"] = state.ToString().ToLowerInvariant() },
            }.ToString(Newtonsoft.Json.Formatting.None));
            Load += async (_, __) =>
            {
                instance?.Listen(s => BeginInvoke(new Action(() => ComeForward(s))));
                await Start();
            };
        }

        /// <summary>A later start of the editor (the game's "Level editor" button): this window comes to the front and opens its song.</summary>
        private void ComeForward(string song)
        {
            if (WindowState == FormWindowState.Minimized) ShowWindow(Handle, Restore);
            SetForegroundWindow(Handle);
            Activate();
            if (string.IsNullOrEmpty(song)) return;
            _waitingSong = song;
            Post("{\"event\":\"songWaiting\"}");
        }

        private const int Restore = 9; // SW_RESTORE: back to maximized or normal, as it was

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        private async Task Start()
        {
            try
            {
                string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InvectorChartEditor", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, userData);
                await _web.EnsureCoreWebView2Async(env);
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "Microsoft Edge WebView2 is needed: https://go.microsoft.com/fwlink/p/?LinkId=2124703\n\n" + e.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                OpenUrl(e.Uri);
            };
            core.NavigationStarting += (_, e) =>
            {
                // Dropped files or stray links would replace the editor: only its own pages load here.
                if (!e.Uri.StartsWith("https://" + AppHost + "/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.AddWebResourceRequestedFilter($"https://{AppHost}/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => e.Response = Serve(core.Environment, new Uri(e.Request.Uri));
            core.WebMessageReceived += async (_, e) => await OnMessage(e.WebMessageAsJson);
            core.Navigate($"https://{AppHost}/index.html");
            _ready = true;
            ExtractFontsOnce();
            RefreshGameData();
        }

        /// <summary>
        /// Reads the game data for the 3D preview in the background when the cache is missing or stale (the first start,
        /// a game update, a new game folder); the page hears how it goes through "gameData" events.
        /// </summary>
        private void RefreshGameData()
        {
            string game = _backend.Settings.GameDir;
            _gameDataDir = game;
            Task.Run(() =>
            {
                try { _gameData.Refresh(game); }
                catch (Exception) { } // the service logs its builds; the editor works without the cache
            });
        }

        /// <summary>The game's fonts, cut out of its bundles the first time (a few seconds, in the background).</summary>
        private void ExtractFontsOnce()
        {
            string game = _backend.Settings.GameDir;
            if (!GameLocator.IsGameDir(game) || (Directory.Exists(_fontsDir) && Directory.GetFiles(_fontsDir, "*.otf").Length > 0)) return;
            Task.Run(() =>
            {
                try
                {
                    if (GameFonts.Extract(game, _fontsDir).Count > 0) Post("{\"event\":\"fonts\"}");
                }
                catch (Exception) { } // the editor's own fonts stay
            });
        }

        private void Post(string json)
        {
            if (InvokeRequired) BeginInvoke(new Action(() => Post(json)));
            else _web.CoreWebView2?.PostWebMessageAsJson(json);
        }

        /// <summary>Where the mod writes the game's pictures for the editor (BepInEx/cache/InvectorModKit/editor-skin).</summary>
        private string SkinDir => string.IsNullOrEmpty(_backend.Settings.GameDir) ? null
            : Path.Combine(_backend.Settings.GameDir, "BepInEx", "cache", "InvectorModKit", "editor-skin");

        private static readonly string UiDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");

        /// <summary>
        /// Everything the page loads, from its own origin: the interface's files from ui/, and the open song's audio
        /// (as a copy in memory, so the file is never kept open) under /audio/.
        /// </summary>
        private CoreWebView2WebResourceResponse Serve(CoreWebView2Environment env, Uri uri)
        {
            string path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            string root = UiDir, file;
            if (path.StartsWith("audio/", StringComparison.Ordinal)) file = _audioPath;
            else
            {
                if (path.StartsWith("skin/fonts/", StringComparison.Ordinal)) { root = _fontsDir; path = path.Substring(11); }
                else if (path.StartsWith("game/", StringComparison.Ordinal)) { root = _gameData.CacheDir; path = path.Substring(5); }
                else if (path.StartsWith("skin/", StringComparison.Ordinal)) { root = SkinDir; path = path.Substring(5); }
                file = root == null ? null : Path.GetFullPath(Path.Combine(root, path.Replace('/', '\\')));
            }
            bool allowed = file != null && (file == _audioPath || file.StartsWith(Path.GetFullPath(root) + "\\", StringComparison.OrdinalIgnoreCase));
            if (!allowed || !File.Exists(file)) return env.CreateWebResourceResponse(null, 404, "Not Found", "");
            string type;
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".html": type = "text/html; charset=utf-8"; break;
                case ".js": type = "text/javascript; charset=utf-8"; break;
                case ".css": type = "text/css; charset=utf-8"; break;
                case ".json": type = "application/json"; break;
                case ".png": type = "image/png"; break;
                case ".jpg": type = "image/jpeg"; break;
                case ".otf": type = "font/otf"; break;
                case ".ttf": type = "font/ttf"; break;
                case ".svg": type = "image/svg+xml"; break;
                case ".ogg": type = "audio/ogg"; break;
                case ".wav": type = "audio/wav"; break;
                case ".mp3": type = "audio/mpeg"; break;
                default: type = "application/octet-stream"; break;
            }
            return env.CreateWebResourceResponse(new MemoryStream(File.ReadAllBytes(file)), 200, "OK", "Content-Type: " + type + "\nCache-Control: no-store");
        }

        private async Task OnMessage(string json)
        {
            JToken id = null;
            try
            {
                var message = JObject.Parse(json);
                id = message["id"];
                string method = (string)message["method"];
                var p = message["params"] as JObject ?? new JObject();
                JToken result = method.StartsWith("host.", StringComparison.Ordinal) ? HostCall(method, p) : await _backend.Handle(method, p);
                Reply(new JObject { ["id"] = id, ["result"] = result ?? JValue.CreateNull() });
                // the game folder chosen in the settings: its data for the 3D preview
                if (!string.Equals(_backend.Settings.GameDir, _gameDataDir, StringComparison.OrdinalIgnoreCase)) RefreshGameData();
            }
            catch (Exception e)
            {
                Reply(new JObject { ["id"] = id, ["error"] = e.GetBaseException().Message });
            }
        }

        private void Reply(JObject message)
        {
            if (InvokeRequired) BeginInvoke(new Action(() => Reply(message)));
            else _web.CoreWebView2?.PostWebMessageAsJson(message.ToString(Newtonsoft.Json.Formatting.None));
        }

        private JToken HostCall(string method, JObject p)
        {
            switch (method)
            {
                case "host.openAudio":
                    using (var dialog = new OpenFileDialog { Filter = "Audio (*.mp3;*.ogg;*.wav)|*.mp3;*.ogg;*.wav", Title = "Invector Level Editor" })
                    {
                        string songs = _backend.Settings.CustomSongsDir;
                        if (songs != null && Directory.Exists(songs)) dialog.InitialDirectory = songs;
                        return dialog.ShowDialog(this) == DialogResult.OK ? new JValue(dialog.FileName) : JValue.CreateNull();
                    }
                case "host.saveZip":
                    using (var dialog = new SaveFileDialog { Filter = ZipFilter, Title = "Invector Level Editor", OverwritePrompt = true })
                    {
                        dialog.FileName = SongFiles.FolderName((string)p["artist"], (string)p["title"]) + ".zip";
                        dialog.InitialDirectory = _zipDir ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        if (dialog.ShowDialog(this) != DialogResult.OK) return JValue.CreateNull();
                        _zipDir = Path.GetDirectoryName(dialog.FileName);
                        return new JValue(dialog.FileName);
                    }
                case "host.openZip":
                    using (var dialog = new OpenFileDialog { Filter = ZipFilter, Title = "Invector Level Editor" })
                    {
                        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                        dialog.InitialDirectory = _zipDir ?? (Directory.Exists(downloads) ? downloads : null);
                        if (dialog.ShowDialog(this) != DialogResult.OK) return JValue.CreateNull();
                        _zipDir = Path.GetDirectoryName(dialog.FileName);
                        return new JValue(dialog.FileName);
                    }
                case "host.pickGameDir":
                    using (var dialog = new FolderBrowserDialog { Description = "Invector Rhythm Galaxy", ShowNewFolderButton = false })
                        return dialog.ShowDialog(this) == DialogResult.OK ? new JValue(dialog.SelectedPath) : JValue.CreateNull();
                case "host.mapAudio":
                {
                    // Served from the page's own origin (a folder mapping is another origin, which fetch may not read).
                    _audioPath = Path.GetFullPath((string)p["audioPath"]);
                    return new JValue($"https://{AppHost}/audio/{Uri.EscapeDataString(Path.GetFileName(_audioPath))}?v={File.GetLastWriteTimeUtc(_audioPath).Ticks}");
                }
                case "host.takeSong":
                {
                    string song = _waitingSong;
                    _waitingSong = null;
                    return song == null ? JValue.CreateNull() : new JValue(song);
                }
                case "host.gameData":
                    return new JObject { ["state"] = _gameData.State.ToString().ToLowerInvariant() };
                case "host.title":
                    Text = (string)p["text"] ?? "Invector Level Editor";
                    return JValue.CreateNull();
                case "host.presence":
                    _presence?.Update(PresenceSnapshotOf(p), PresenceTexts.English("InvectorModKit · Level Editor", "https://discord.gg/9wXNm5NM26"));
                    return JValue.CreateNull();
                case "host.close":
                    _allowClose = true;
                    BeginInvoke(new Action(Close));
                    return JValue.CreateNull();
            }
            throw new ArgumentException("unknown method " + method);
        }

        /// <summary>Closing asks the page first (unsaved changes); it answers with host.close.</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && _ready && _web.CoreWebView2 != null && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                _web.CoreWebView2.PostWebMessageAsJson("{\"event\":\"closing\"}");
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _presence?.Dispose();
            base.OnFormClosed(e);
        }

        private static PresenceSnapshot PresenceSnapshotOf(JObject p) => new PresenceSnapshot
        {
            Screen = PresenceScreen.Editor,
            Title = (string)p["title"] ?? "",
            Artist = (string)p["artist"] ?? "",
            Difficulty = (string)p["difficulty"] ?? "",
            Notes = (int?)p["notes"] ?? 0,
        };

        private static void OpenUrl(string url)
        {
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
