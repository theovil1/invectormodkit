using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using InvectorModKit.Installer;

namespace InvectorModKit.Setup
{
    /// <summary>
    /// The one window players see: where the game is, a box for the chart editor, one big button (install, update or
    /// repair), then "Play". Uninstall is a small link. Colours and font of the game's menus.
    /// </summary>
    internal sealed class SetupForm : Form
    {
        private static readonly Color Background = Color.FromArgb(12, 9, 18), Accent = Color.FromArgb(203, 51, 255), Muted = Color.FromArgb(190, 186, 200);

        private readonly string _lang;
        private string _game;
        private bool _busy, _finished;
        private readonly Label _title = new Label(), _subtitle = new Label(), _where = new Label(), _installed = new Label(), _status = new Label(), _version = new Label();
        private readonly LinkLabel _change = new LinkLabel(), _uninstall = new LinkLabel();
        private readonly CheckBox _editor = new CheckBox();
        private readonly Button _main = new Button();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Timer _watch = new Timer { Interval = 1000 };

        /// <summary>Starts installing as soon as the window shows (after a restart as administrator).</summary>
        public bool AutoStart { get; set; }

        public SetupForm(string lang, string game, bool editor)
        {
            _lang = lang;
            _game = game;
            Text = "InvectorModKit";
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 400);
            BackColor = Background;
            ForeColor = Color.White;
            Font = new Font(FontName(), 11f);

            Place(_title, 32, 24, 576, 48, new Font(FontName(), 26f, FontStyle.Bold), Color.White, "InvectorModKit");
            var line = new Panel { BackColor = Accent, Location = new Point(34, 74), Size = new Size(64, 4) };
            Controls.Add(line);
            Place(_subtitle, 32, 88, 576, 44, Font, Muted, T("subtitle"));
            Place(_where, 32, 142, 470, 44, Font, Color.White, "");
            _where.AutoEllipsis = true;
            Link(_change, 510, 142, 98, T("change"), (s, e) => PickGame());
            Place(_installed, 32, 186, 576, 24, new Font(FontName(), 10f), Muted, "");
            _editor.Text = T("editor");
            _editor.Checked = editor;
            _editor.Location = new Point(34, 216);
            _editor.Size = new Size(570, 28);
            _editor.ForeColor = Color.White;
            Controls.Add(_editor);

            _main.Location = new Point(32, 258);
            _main.Size = new Size(280, 54);
            _main.FlatStyle = FlatStyle.Flat;
            _main.FlatAppearance.BorderSize = 0;
            _main.BackColor = Accent;
            _main.ForeColor = Color.White;
            _main.Font = new Font(FontName(), 15f, FontStyle.Bold);
            _main.Cursor = Cursors.Hand;
            _main.Click += (s, e) => OnMain();
            Controls.Add(_main);

            _progress.Location = new Point(330, 276);
            _progress.Size = new Size(278, 18);
            _progress.Visible = false;
            Controls.Add(_progress);
            Place(_status, 32, 322, 576, 46, new Font(FontName(), 10.5f), Color.White, "");
            Link(_uninstall, 32, 370, 300, T("uninstall"), (s, e) => Uninstall());
            Place(_version, 420, 370, 188, 22, new Font(FontName(), 9f), Muted, "v" + Shell.Version);
            _version.TextAlign = ContentAlignment.TopRight;

            _watch.Tick += (s, e) => UpdateState(false);
            _watch.Start();
            Shown += (s, e) => { if (AutoStart && GameFolder.IsGame(_game)) OnMain(); };
            UpdateState(true);
        }

        private string T(string key, params object[] args) => SetupTexts.Get(_lang, key, args);

        /// <summary>The game-like Bahnschrift, except for Japanese, Korean and Chinese (Segoe UI falls back to their fonts).</summary>
        private string FontName() =>
            _lang != "ja" && _lang != "ko" && _lang != "zh" && Array.Exists(FontFamily.Families, f => f.Name == "Bahnschrift") ? "Bahnschrift" : "Segoe UI";

        /// <summary>A path with its real letter case (Steam stores it in lower case).</summary>
        private static string Display(string path)
        {
            try
            {
                string full = Path.GetFullPath(path), root = Path.GetPathRoot(full);
                string result = root.ToUpperInvariant();
                foreach (string part in full.Substring(root.Length).Split(Path.DirectorySeparatorChar))
                {
                    if (part.Length == 0) continue;
                    string match = Array.Find(Directory.GetFileSystemEntries(result, part), e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase));
                    result = match ?? Path.Combine(result, part);
                }
                return result;
            }
            catch (Exception) { return path; }
        }

        private void Place(Label label, int x, int y, int w, int h, Font font, Color color, string text)
        {
            label.Location = new Point(x, y);
            label.Size = new Size(w, h);
            label.Font = font;
            label.ForeColor = color;
            label.Text = text;
            Controls.Add(label);
        }

        private void Link(LinkLabel link, int x, int y, int w, string text, LinkLabelLinkClickedEventHandler click)
        {
            link.Location = new Point(x, y);
            link.Size = new Size(w, 24);
            link.Text = text;
            link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = Accent;
            link.LinkClicked += click;
            Controls.Add(link);
        }

        /// <summary>The window's state from the game folder (and whether the game is open).</summary>
        private void UpdateState(bool full)
        {
            if (_busy || _finished) return;
            bool found = GameFolder.IsGame(_game);
            string installed = found ? ModInstaller.InstalledVersion(_game) : null;
            if (full)
            {
                _where.Text = found ? T("found", Display(_game)) : T("notFound");
                _installed.Text = installed != null ? T("installed", installed) : "";
                _main.Text = installed == null ? T("install") : IsNewer(Shell.Version, installed) ? T("update", Shell.Version) : T("repair");
                _uninstall.Visible = installed != null;
            }
            bool running = GameFolder.IsRunning();
            _main.Enabled = found && !running;
            if (running) _status.Text = T("closeGame");
            else if (_status.Text == T("closeGame")) _status.Text = "";
        }

        private static bool IsNewer(string a, string b) => System.Version.TryParse(a, out var x) && System.Version.TryParse(b, out var y) && x > y;

        private void PickGame()
        {
            using (var dialog = new FolderBrowserDialog { Description = T("pickTitle"), ShowNewFolderButton = false })
            {
                if (_game != null && Directory.Exists(_game)) dialog.SelectedPath = _game;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!GameFolder.IsGame(dialog.SelectedPath)) { _status.Text = T("err_not-game"); return; }
                _game = dialog.SelectedPath;
                _status.Text = "";
                UpdateState(true);
            }
        }

        private async void OnMain()
        {
            if (_finished)
            {
                if (_main.Tag as string == "play") Shell.LaunchGame();
                Close();
                return;
            }
            if (_busy || !GameFolder.IsGame(_game)) return;
            _busy = true;
            SetWorking(true, T("installing"));
            string game = _game;
            bool editor = _editor.Checked;
            try
            {
                await Task.Run(() => Actions.Install(game, editor, p => BeginInvoke(new Action(() => _progress.Value = (int)(p * 100)))));
                Finish(T("done") + " " + T("doneHint"), T("play"), "play");
            }
            catch (UnauthorizedAccessException) when (!Shell.IsAdministrator())
            {
                // Windows refused writing in the game folder: the same installation, as administrator
                if (Shell.RelaunchAsAdministrator($"--game \"{game}\" --auto{(editor ? " --editor" : "")} --lang {_lang}")) { Close(); return; }
                SetWorking(false, T("err_other", "access denied"));
            }
            catch (Exception e)
            {
                Shell.Log("install failed: " + e);
                SetWorking(false, Actions.Explain(e, _lang));
            }
            _busy = false;
        }

        private async void Uninstall()
        {
            if (_busy || !GameFolder.IsGame(_game) || GameFolder.IsRunning()) return;
            string songs = Path.Combine(_game, "CustomSongs");
            if (MessageBox.Show(this, T("uninstallConfirm", songs), "InvectorModKit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _busy = true;
            SetWorking(true, T("uninstalling"));
            string game = _game;
            try
            {
                await Task.Run(() => Actions.Uninstall(game));
                Finish(T("uninstalled", songs), T("close"), "close");
            }
            catch (Exception e)
            {
                Shell.Log("uninstall failed: " + e);
                SetWorking(false, Actions.Explain(e, _lang));
            }
            _busy = false;
        }

        private void SetWorking(bool working, string status)
        {
            _status.Text = status;
            _progress.Visible = working;
            _progress.Value = 0;
            _main.Enabled = !working;
            _change.Enabled = _editor.Enabled = _uninstall.Enabled = !working;
        }

        private void Finish(string status, string button, string tag)
        {
            _finished = true;
            _progress.Visible = false;
            _status.Text = status;
            _main.Text = button;
            _main.Tag = tag;
            _main.Enabled = true;
            _change.Visible = _uninstall.Visible = _editor.Enabled = false;
            _installed.Text = tag == "play" ? T("installed", Shell.Version) : "";
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _watch.Stop();
            base.OnFormClosed(e);
        }
    }
}
