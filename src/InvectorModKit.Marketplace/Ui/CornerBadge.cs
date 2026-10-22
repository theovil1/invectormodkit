using HelloThere.Avicii;
using HelloThere.Modules.AudioModules;
using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// The menus' bottom left corner, in the game's style: the mod's name, version and the official scores status, in
    /// the game's language, and in the main menu only, a Discord button above them (animated logo, "Discord" and an
    /// invitation). Hidden while playing and while the Marketplace window is open. The mouse reads it directly (the game's input module
    /// only serves the menu that owns the controls).
    /// </summary>
    internal sealed class CornerBadge : MonoBehaviour
    {
        private Canvas _canvas;
        private Text _status, _title, _invite;
        private RectTransform _button;
        private DiscordPulse _pulse;
        private float _nextFontCheck;
        private bool _hovered;

        private void Start()
        {
            ModKit.TakeOverStatusCorner();
            Build();
        }

        private void Build()
        {
            var root = new GameObject("InvectorModKit Corner", typeof(RectTransform));
            DontDestroyOnLoad(root);
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 900;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var font = Resources.GetBuiltinResource<Font>("Arial.ttf"); // until the game's fonts are loaded
            _status = Corner(GameUi.Text(root.transform, "Status", font, 25, TextAnchor.LowerLeft, new Color(1, 1, 1, 0.66f)), 28, 18, 1000, 36);

            // the Discord button: halo and logo on the left, its words on the right
            _button = (RectTransform)new GameObject("Discord", typeof(RectTransform)).transform;
            _button.SetParent(root.transform, false);
            Corner(_button, 12, 62, 700, 132);
            var halo = GameUi.Image(_button, "Halo", Community.Purple);
            halo.sprite = Community.Glow;
            Anchor(halo.rectTransform, 84, 66, 300, 300);
            var logo = GameUi.Image(_button, "Logo", Color.white);
            logo.sprite = Community.Logo;
            logo.preserveAspect = true;
            Anchor(logo.rectTransform, 84, 66, 100, 100);
            _title = GameUi.Text(_button, "Title", font, 48, TextAnchor.LowerLeft, Color.white);
            Anchor(_title.rectTransform, 158, 92, 380, 60, pivotLeft: true);
            _invite = GameUi.Text(_button, "Invite", font, 28, TextAnchor.UpperLeft, new Color(1, 1, 1, 0.8f));
            Anchor(_invite.rectTransform, 158, 44, 540, 40, pivotLeft: true);
            _title.text = "Discord";

            _pulse = _button.gameObject.AddComponent<DiscordPulse>();
            _pulse.Logo = logo.rectTransform;
            _pulse.LogoImage = logo;
            _pulse.Halo = halo;
        }

        /// <summary>Anchored to the screen's bottom left corner, <paramref name="x"/>/<paramref name="y"/> from it.</summary>
        private static T Corner<T>(T c, float x, float y, float width, float height) where T : Component
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
            return c;
        }

        /// <summary>Inside the button: centred on (x, y) from its bottom left, or starting at x when pivotLeft.</summary>
        private static void Anchor(RectTransform rt, float x, float y, float width, float height, bool pivotLeft = false)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = pivotLeft ? new Vector2(0, 0.5f) : new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }

        private void Update()
        {
            if (!_canvas) return;
            bool show = ModKit.StatusCornerEnabled && !Playing() && !MarketplaceModal.IsOpen;
            if (_canvas.enabled != show) _canvas.enabled = show;
            if (!show) { _hovered = false; return; }

            UseGameFonts();
            _status.text = $"InvectorModKit {ModKit.Version}" + (ModKit.IsModdedSession ? "  ·  " + Loc.T("status_paused") : "");
            _invite.text = Loc.T("discord_join");
            bool discord = Community.HasDiscord && ModKit.CurrentState is StateMainMenu;
            _button.gameObject.SetActive(discord);
            if (!discord) { _hovered = false; return; }

            bool over = Cursor.visible && RectTransformUtility.RectangleContainsScreenPoint(_button, Input.mousePosition, null);
            if (over && !_hovered) AudioModule.AudioProvider.Play(AudioShortcut.SFX_MENU_NAVIGATE);
            _hovered = over;
            _pulse.Hovered = over;
            if (over && Input.GetMouseButtonDown(0)) Community.OpenDiscord();
        }

        private void UseGameFonts()
        {
            if (Time.unscaledTime < _nextFontCheck || _title.font.name.StartsWith("Exo2")) return;
            _nextFontCheck = Time.unscaledTime + 2f;
            var bold = GameUi.Font("Exo2-BoldCondensed", null);
            var regular = GameUi.Font("Exo2-RegularCondensed", null) ?? GameUi.Font("Exo2-MediumCondensed", null) ?? bold;
            if (!bold) return;
            _title.font = bold;
            _status.font = _invite.font = regular ?? bold;
        }

        private static bool Playing()
        {
            var state = ModKit.CurrentState;
            return state is StateGame || state is StateGamePause || state is global::StateGameEnd || state is StateGameResult || state is StateGameResultCampaign;
        }
    }
}
