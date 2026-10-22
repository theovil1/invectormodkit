using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using InvectorModKit.CustomSongs;
using InvectorModKit.Marketplace.Client;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// Window that adds a song from a YouTube link on its own: the marketplace has closed, this one shows
    /// where the download stands, and the reason with an ok button when it fails. Esc closes it while the
    /// download keeps running in the background and the song still arrives.
    /// </summary>
    internal sealed class AddSongModal : MonoBehaviour
    {
        private Text _status;
        private ButtonWithEvents _ok;
        private Image _fill;
        private GameObject _previous;
        private string _waitingAudio;
        private float _nextPoll, _giveUp;

        public static void Start(string link)
        {
            string videoId = IdOf(link);
            if (videoId == null) return;
            MarketplaceModal.Close();
            var previous = GameUi.Selected; // the menu item the player will come back to
            var dialog = GameUi.DialogTemplate;
            var canvas = GameUi.StartMenu != null ? GameUi.StartMenu.transform.parent : null;
            if (dialog == null || canvas == null)
            {
                MarketplacePlugin.Log.LogWarning("Add song: the game's dialog is not loaded yet");
                return;
            }
            var root = GameUi.Clone(dialog, canvas, "AddSong", stillImage: true);
            root.transform.SetAsLastSibling();
            var layer = root.AddComponent<Canvas>();
            layer.overrideSorting = true;
            layer.sortingOrder = 1000;
            var menuCanvas = canvas.GetComponentInParent<Canvas>()?.rootCanvas;
            if (menuCanvas && menuCanvas.worldCamera) layer.worldCamera = menuCanvas.worldCamera;
            root.AddComponent<GraphicRaycaster>();
            var backdrop = GameUi.Find<Image>(root, "Background");
            if (backdrop) backdrop.raycastTarget = true; // clicks behind the window must not reach the menus
            var modal = root.AddComponent<AddSongModal>();
            modal._previous = previous && previous.scene.IsValid() ? previous : null;
            modal.Build();
            modal.Run(videoId);
        }

        /// <summary>The 11-character video id of a YouTube link (the part after its "v=").</summary>
        private static string IdOf(string link)
        {
            int p = link.IndexOf("v=", StringComparison.Ordinal);
            return p < 0 ? null : link.Substring(p + 2);
        }

        private void Build()
        {
            var group = GetComponent<CanvasGroup>();
            if (group) { group.alpha = 1; group.interactable = true; group.blocksRaycasts = true; }
            var backdrop = GameUi.Find<Image>(gameObject, "Background");
            if (backdrop) backdrop.color = new Color(0, 0, 0, 0.94f);
            var box = (RectTransform)transform.Find("QuestionBox");
            GameUi.Place(box, 0, 0, 900, 320);
            MarketplaceModal.AddFill(box);
            _status = GameUi.Find<Text>(box.gameObject, "Text");
            _status.alignment = TextAnchor.MiddleCenter;
            _status.resizeTextForBestFit = true;
            _status.resizeTextMinSize = 20;
            _status.resizeTextMaxSize = 34;
            GameUi.Place(_status, 0, 60, 800, 120);
            // The download's progress: a track and its white fill, driven by yt-dlp's percent output.
            var track = GameUi.Image(box, "Track", new Color32(0x2A, 0x25, 0x2F, 0xFF));
            GameUi.Place(track, 0, -5, 700, 14);
            _fill = GameUi.Image(track.transform, "Fill", Color.white);
            var fillRt = (RectTransform)_fill.transform;
            fillRt.anchorMin = fillRt.anchorMax = new Vector2(0f, 0.5f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = new Vector2(2, 0);
            fillRt.sizeDelta = new Vector2(0, 10);
            SetFill(0);
            var no = GameUi.Find<ButtonWithEvents>(box.gameObject, "Answer No");
            if (no) Destroy(no.gameObject);
            _ok = GameUi.Find<ButtonWithEvents>(box.gameObject, "Answer Yes");
            GameUi.Place(_ok, 0, -80, 280, 74);
            MarketplaceModal.StyleButton(_ok, Loc.T("ok"), 32, Close);
            _ok.gameObject.SetActive(false);
            _navigationEvents = !EventSystem.current || EventSystem.current.sendNavigationEvents;
            TurnNavigation(false);
        }

        private async void Run(string videoId)
        {
            try
            {
                SetStatus(Loc.T("link_adding"));
                var (ytDlp, ffmpegDir) = AudioFetch.Resolve(MarketplacePlugin.SharedDir);
                if (ytDlp == null) { Fail(Loc.T("no_tools")); return; }
                var (channel, title) = await Task.Run(() => AudioFetch.VideoName(ytDlp, videoId, line => MarketplacePlugin.Log.LogInfo(line)));
                if (string.IsNullOrEmpty(title)) { Fail(Loc.T("link_failed", "YouTube refused")); return; }
                string target = AudioFetch.Target(CustomSongsApi.SongsFolder, channel.Length > 0 ? channel : "YouTube", title);
                if (AudioFetch.Existing(CustomSongsApi.SongsFolder, channel, title) != null) { Fail(Loc.T("link_exists")); return; }
                string tmpBase = Path.Combine(Path.GetTempPath(), "invectormodkit-link-" + videoId);
                await Task.Run(() => AudioFetch.Download(ytDlp, AudioFetch.BuildArgs(videoId, tmpBase, ffmpegDir), LogLine));
                string produced = tmpBase + ".ogg";
                if (!File.Exists(produced)) throw new FileNotFoundException("yt-dlp left nothing at " + produced);
                AudioFetch.Move(produced, target);
                MarketplacePlugin.Log.LogInfo("Song added: " + target);
                Thumbnails.Save(videoId, Path.ChangeExtension(target, ".jpg"));
                CustomSongsApi.RequestMetadataReload();
                // The song only counts once its chart is generated: wait for the plugin's own pipeline.
                SetStatus(Loc.T("chart_wait"));
                SetFill(1);
                _waitingAudio = target;
                _nextPoll = Time.unscaledTime + 1f;
                _giveUp = Time.unscaledTime + 180f;
            }
            catch (Exception e)
            {
                MarketplacePlugin.Log.LogError("Link add failed: " + e);
                Fail(Loc.T("link_failed", e.GetBaseException().Message));
            }
        }

        private void SetStatus(string text) { if (this) _status.text = text; }

        private void LogLine(string line)
        {
            MarketplacePlugin.Log.LogInfo(line);
            // yt-dlp prints its progress as "[download]  12.3% of ..." on its error output.
            var m = Regex.Match(line ?? "", @"(\d+(?:\.\d+)?)%");
            if (m.Success) SetFill(float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 100f);
        }

        private void SetFill(float part)
        {
            if (!this || !_fill) return;
            ((RectTransform)_fill.transform).sizeDelta = new Vector2(Mathf.Clamp01(part) * 696f, 10);
        }

        /// <summary>
        /// While waiting for the new song's chart: it is ready (or the wait gives up) when the plugin lists
        /// the audio file among its ready songs.
        /// </summary>
        private void WaitChart()
        {
            if (_waitingAudio == null) return;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.5f;
            var song = CustomSongsApi.Songs().FirstOrDefault(s =>
                string.Equals(Path.GetFullPath(s.AudioPath ?? ""), Path.GetFullPath(_waitingAudio), StringComparison.OrdinalIgnoreCase));
            if (song != null && song.Ready) _waitingAudio = null;
            else if (Time.unscaledTime >= _giveUp) _waitingAudio = null;
            else return;
            if (this) Done(Loc.T("link_added"));
        }

        /// <summary>The work is over: the result is shown, ok (or Esc) closes the window.</summary>
        private void Fail(string message)
        {
            _waitingAudio = null;
            if (!this) return;
            _status.text = message;
            _ok.gameObject.SetActive(true);
            GameUi.Select(_ok.gameObject);
        }

        private void Done(string message) => Fail(message);

        private void Update()
        {
            WaitChart();
            if (!this) return;
            if (Input.GetKeyDown(KeyCode.Escape) || CancelPressed()) { Close(); return; }
            if (_ok && _ok.gameObject.activeSelf && OkPressed()) _ok.onClick.Invoke();
        }

        private static bool CancelPressed()
        {
            try { return RewiredInputHelper.GetButtonDownAll(RewiredInputHelper.ActionIdCancel); }
            catch (Exception) { return false; }
        }

        private static bool OkPressed()
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) return true;
            try { return RewiredInputHelper.GetButtonDownAll(RewiredInputHelper.ActionIdOk); }
            catch (Exception) { return false; }
        }

        private bool _navigationEvents;

        private void TurnNavigation(bool on)
        {
            if (EventSystem.current) EventSystem.current.sendNavigationEvents = on ? _navigationEvents : false;
        }

        private void Close()
        {
            TurnNavigation(true);
            if (_previous) GameUi.Select(_previous);
            Destroy(gameObject);
        }

        private void OnDestroy() => TurnNavigation(true);
    }
}
