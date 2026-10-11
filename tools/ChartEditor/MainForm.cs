using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor.Host
{
    /// <summary>
    /// The editor's window: the HTML interface in WebView2 (served from ui/ as https://app.editor/), the open song's
    /// audio as https://app.editor/audio/…, the game's look as https://app.editor/skin/… (the pictures the mod exports in
    /// game, and the game's fonts cut out of its bundles here), and the interface's messages answered here ("host.*")
    /// or by the EditorBackend.
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

        public MainForm()
        {
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
            _fontsDir = Path.Combine(data, "fonts");
            Load += async (_, __) => await Start();
        }

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
                case "host.pickGameDir":
                    using (var dialog = new FolderBrowserDialog { Description = "Invector Rhythm Galaxy", ShowNewFolderButton = false })
                        return dialog.ShowDialog(this) == DialogResult.OK ? new JValue(dialog.SelectedPath) : JValue.CreateNull();
                case "host.mapAudio":
                {
                    // Served from the page's own origin (a folder mapping is another origin, which fetch may not read).
                    _audioPath = Path.GetFullPath((string)p["audioPath"]);
                    return new JValue($"https://{AppHost}/audio/{Uri.EscapeDataString(Path.GetFileName(_audioPath))}?v={File.GetLastWriteTimeUtc(_audioPath).Ticks}");
                }
                case "host.title":
                    Text = (string)p["text"] ?? "Invector Level Editor";
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

        private static void OpenUrl(string url)
        {
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
