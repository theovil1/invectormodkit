using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// The marketplace window, built from the game's own dialog (dark backdrop, InfoFrameSimple frame,
    /// QuestionButton buttons, Exo2 fonts) over whichever menu is open. While it is open the menu underneath
    /// ignores input (see Patches).
    /// </summary>
    internal sealed class MarketplaceModal : MonoBehaviour
    {
        public static bool IsOpen => _instance != null;
        private static MarketplaceModal _instance;
        private static bool _opening;

        private const float Width = 1500, Height = 880;

        public Font Regular, Bold, SemiBold;
        public RectTransform Box;
        private Text _account;
        private ButtonWithEvents _signIn;
        private ButtonWithEvents _browseTab, _mineTab;
        private BrowseTab _browse;
        private MySongsTab _mine;
        private ITab _current;
        private GameObject _previousSelection;
        private Confirmation _confirmation;

        public static void Open(MonoBehaviour host)
        {
            if (IsOpen || _opening) return;
            host.StartCoroutine(OpenRoutine());
        }

        public static void Close()
        {
            if (_instance != null) _instance.Shut();
        }

        private static IEnumerator OpenRoutine()
        {
            _opening = true;
            try
            {
                yield return GameUi.EnsureSongMenuLoaded();
                var dialog = GameUi.DialogTemplate;
                var canvas = GameUi.StartMenu != null ? GameUi.StartMenu.transform.parent : null;
                if (dialog == null || canvas == null || GameUi.QuestionButtonTemplate == null || GameUi.SongRowTemplate == null)
                {
                    MarketplacePlugin.Log.LogWarning("Marketplace: the game's menu pieces are not loaded yet");
                    yield break;
                }
                var root = GameUi.Clone(dialog, canvas, "Marketplace", stillImage: true);
                root.transform.SetAsLastSibling();
                // Song select draws in its own canvases: the window needs one of its own, in front of them,
                // with its own raycaster so the mouse reaches it (and nothing behind it).
                var layer = root.AddComponent<Canvas>();
                layer.overrideSorting = true;
                layer.sortingOrder = 1000;
                root.AddComponent<GraphicRaycaster>();
                var modal = root.AddComponent<MarketplaceModal>();
                modal.Build();
                _instance = modal;
            }
            finally
            {
                _opening = false;
            }
        }

        private void Build()
        {
            var group = GetComponent<CanvasGroup>();
            if (group) { group.alpha = 1; group.interactable = true; group.blocksRaycasts = true; }

            var backdrop = GameUi.Find<Image>(gameObject, "Background");
            if (backdrop) backdrop.color = new Color(0, 0, 0, 0.94f);
            Box = (RectTransform)transform.Find("QuestionBox");
            GameUi.Place(Box, 0, 0, Width, Height);
            // A solid fill under the game's frame, so nothing shows through the window.
            var fill = GameUi.Image(Box, "Fill", new Color32(0x0C, 0x09, 0x12, 0xF5));
            var fillRt = (RectTransform)fill.transform;
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(18, 12); fillRt.offsetMax = new Vector2(-18, -12);
            fill.transform.SetAsFirstSibling();
            foreach (var name in new[] { "Answer Yes", "Answer No" })
            {
                var old = Box.Find(name);
                if (old) Destroy(old.gameObject);
            }
            var title = GameUi.Find<Text>(Box.gameObject, "Text");
            Regular = title.font;
            Bold = GameUi.Font("Exo2-BoldCondensed", Regular);
            SemiBold = GameUi.Font("Exo2-SemiBoldCondensed", Regular);
            title.text = "MARKETPLACE";
            title.alignment = TextAnchor.MiddleLeft;
            title.resizeTextForBestFit = false;
            title.fontSize = 44;
            title.font = Bold;
            GameUi.Place(title, -Width / 2 + 60 + 300, Height / 2 - 70, 600, 60);

            _account = GameUi.Text(Box, "Account", Regular, 26, TextAnchor.MiddleRight, GameUi.Muted);
            GameUi.Place(_account, Width / 2 - 440, Height / 2 - 70, 380, 50);
            _signIn = MakeButton(Box, "SE CONNECTER", Width / 2 - 230, Height / 2 - 70, 220, 56, SignIn, 26);
            MakeButton(Box, "X", Width / 2 - 70, Height / 2 - 70, 64, 56, Shut, 30);

            _browseTab = MakeButton(Box, "PARCOURIR", -Width / 2 + 190, Height / 2 - 150, 260, 60, () => Show(_browse));
            _mineTab = MakeButton(Box, "MES MORCEAUX", -Width / 2 + 470, Height / 2 - 150, 260, 60, () => Show(_mine));

            var hints = GameUi.Text(Box, "Hints", Regular, 24, TextAnchor.MiddleCenter, GameUi.Muted);
            hints.text = "ÉCHAP  Fermer      TAB  Changer d'onglet      F  Rechercher      ENTRÉE ou DOUBLE-CLIC  Action principale      ← →  Actions";
            GameUi.Place(hints, 0, -Height / 2 + 40, Width - 120, 40);

            _browse = new BrowseTab(this);
            _mine = new MySongsTab(this);
            _confirmation = new Confirmation(this);
            _previousSelection = GameUi.Selected;
            RefreshAccount();
            Show(_browse);
        }

        private void Show(ITab tab)
        {
            if (_confirmation.Visible) return;
            _current?.Hide();
            _current = tab;
            tab.Show();
            SetTabLook(_browseTab, tab == _browse);
            SetTabLook(_mineTab, tab == _mine);
        }

        private static void SetTabLook(ButtonWithEvents tab, bool active)
        {
            var colors = tab.colors;
            colors.normalColor = active ? new Color(1, 1, 1, 0.35f) : (Color)new Color32(0x18, 0x16, 0x19, 0xFF);
            tab.colors = colors;
            tab.originalColors = colors;
        }

        private void Update()
        {
            if (_confirmation.Visible)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) _confirmation.Answer(false);
                return;
            }
            bool typing = _current != null && _current.Typing;
            if (!typing && (Input.GetKeyDown(KeyCode.Escape) || (!Input.GetKey(KeyCode.Backspace) && CancelPressed())))
            {
                Shut();
                return;
            }
            if (!typing && Input.GetKeyDown(KeyCode.Tab)) Show(_current == _browse ? (ITab)_mine : _browse);
            _current?.Tick();
            RefreshAccount();
        }

        private static bool CancelPressed()
        {
            try { return RewiredInputHelper.GetButtonDownAll(RewiredInputHelper.ActionIdCancel); }
            catch (Exception) { return false; }
        }

        private void Shut()
        {
            if (EventSystem.current) EventSystem.current.sendNavigationEvents = true;
            _instance = null;
            Destroy(gameObject);
            if (_previousSelection) GameUi.Select(_previousSelection);
        }

        // ----- account

        private void RefreshAccount()
        {
            var session = MarketplacePlugin.Session;
            _account.text = session.SignedIn ? $"Connecté : {session.Current.Name}{(session.Current.Role == "admin" ? " (admin)" : "")}"
                          : session.SigningIn ? "Connexion dans ton navigateur…" : "";
            _signIn.gameObject.SetActive(!session.SignedIn && !session.SigningIn);
            var rt = (RectTransform)_account.transform;
            rt.anchoredPosition = new Vector2(session.SignedIn || session.SigningIn ? Width / 2 - 300 : Width / 2 - 540, rt.anchoredPosition.y);
        }

        /// <summary>Opens Steam's sign-in page; true once signed in.</summary>
        public async System.Threading.Tasks.Task<bool> EnsureSignedIn()
        {
            var session = MarketplacePlugin.Session;
            if (session.SignedIn) return true;
            if (session.SigningIn) return false;
            try
            {
                await session.SignInAsync();
                return true;
            }
            catch (Exception e)
            {
                if (this) _current?.SetStatus("Connexion impossible : " + e.GetBaseException().Message);
                return false;
            }
        }

        private async void SignIn() => await EnsureSignedIn();

        public void Confirm(string question, Action onYes) => _confirmation.Ask(question, onYes);

        // ----- shared builders (the game's own pieces)

        public ButtonWithEvents MakeButton(Transform parent, string label, float x, float y, float width, float height, Action onClick, int fontSize = 30)
        {
            var go = GameUi.Clone(GameUi.QuestionButtonTemplate.gameObject, parent, "Button " + label);
            var button = go.GetComponent<ButtonWithEvents>();
            GameUi.Place(button, x, y, width, height);
            var text = go.GetComponentInChildren<Text>(true);
            foreach (var fitter in text.GetComponents<ContentSizeFitter>()) Destroy(fitter);
            var rt = (RectTransform)text.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(10, 0); rt.offsetMax = new Vector2(-10, 0);
            text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 14;
            text.resizeTextMaxSize = fontSize;
            text.fontSize = fontSize;
            text.text = label;
            button.onClick.AddListener(() =>
            {
                try { onClick(); }
                catch (Exception e) { MarketplacePlugin.Log.LogError(e); }
            });
            return button;
        }

        public static void SetLabel(ButtonWithEvents button, string label) => button.GetComponentInChildren<Text>(true).text = label;

        /// <summary>A song row from the game's song list: cover, title, artist, a right-hand note and a length.</summary>
        public SongRow MakeRow(Transform parent, Action onClick, Action onSelected)
        {
            var go = GameUi.Clone(GameUi.SongRowTemplate.gameObject, parent, "Row");
            return new SongRow(go, onClick, onSelected);
        }

        /// <summary>A vertical scroll list (viewport with mask, content laid out top-down).</summary>
        public static (ScrollRect scroll, RectTransform content) MakeList(Transform parent, float x, float y, float width, float height)
        {
            var viewport = GameUi.Image(parent, "List", new Color(0, 0, 0, 0.25f));
            viewport.raycastTarget = true;
            GameUi.Place(viewport, x, y, width, height);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            content.SetParent(viewport.transform, false);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4; layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content; scroll.viewport = (RectTransform)viewport.transform;
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
            return (scroll, content);
        }

        /// <summary>Scrolls so that <paramref name="item"/> is inside the viewport.</summary>
        public static void KeepVisible(ScrollRect scroll, RectTransform item)
        {
            var viewport = scroll.viewport; var content = scroll.content;
            Canvas.ForceUpdateCanvases();
            float top = -item.anchoredPosition.y - item.rect.height * (1 - item.pivot.y);
            float bottom = top + item.rect.height;
            float view = viewport.rect.height, y = content.anchoredPosition.y;
            if (top < y) y = top;
            else if (bottom > y + view) y = bottom - view;
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Max(0, y));
        }

        /// <summary>Explicit up/down navigation through rows, right into <paramref name="actions"/>.</summary>
        public static void ChainNavigation(IList<Selectable> rows, Selectable firstAction)
        {
            for (int i = 0; i < rows.Count; i++)
                rows[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = i > 0 ? rows[i - 1] : null,
                    selectOnDown = i + 1 < rows.Count ? rows[i + 1] : null,
                    selectOnRight = firstAction,
                };
        }
    }

    internal interface ITab
    {
        bool Typing { get; }
        void Show();
        void Hide();
        void Tick();
        void SetStatus(string text);
    }

    /// <summary>A cloned song-list row and its parts.</summary>
    internal sealed class SongRow
    {
        public readonly GameObject Root;
        public readonly ButtonWithEvents Button;
        public readonly Image Cover;
        public readonly Text Title, Artist, Note, Length;

        public SongRow(GameObject root, Action onClick, Action onSelected)
        {
            Root = root;
            Button = root.GetComponent<ButtonWithEvents>();
            Cover = GameUi.Find<Image>(root, "Node/LayoutGrp/CoverSmall");
            Title = GameUi.Find<Text>(root, "Node/LayoutGrp/SongGrp/SongTitle");
            Artist = GameUi.Find<Text>(root, "Node/LayoutGrp/SongGrp/SongArtist");
            Note = GameUi.Find<Text>(root, "Node/SongPercent");
            Length = GameUi.Find<Text>(root, "Node/SongLength");
            foreach (var hidden in new[] { "Node/SongRatingHolder", "Node/NewIcon", "Node/SongUnlockHint" })
            {
                var t = root.transform.Find(hidden);
                if (t) t.gameObject.SetActive(false);
            }
            // Keyboard/gamepad: Enter runs the action. Mouse: a click selects the row, a double-click runs it.
            Button.onClick.AddListener(() =>
            {
                try
                {
                    bool mouse = Input.GetMouseButtonUp(0) || Input.GetMouseButton(0);
                    if (!mouse) { onClick(); return; }
                    if (Time.unscaledTime - _lastClick < 0.35f) { _lastClick = -1; onClick(); }
                    else _lastClick = Time.unscaledTime;
                }
                catch (Exception e) { MarketplacePlugin.Log.LogError(e); }
            });
            var dispatcher = root.GetComponent<SelectDispatcher>() ?? root.AddComponent<SelectDispatcher>();
            dispatcher.Selected = onSelected;
        }

        private float _lastClick = -1;

        public void Set(string title, string artist, string note, double seconds, Sprite cover)
        {
            if (Title) Title.text = title;
            if (Artist) Artist.text = artist;
            if (Note) Note.text = note ?? "";
            if (Length) Length.text = seconds > 0 ? $"{(int)seconds / 60:00}:{(int)seconds % 60:00}" : "";
            if (Cover) { Cover.sprite = cover; Cover.color = cover ? Color.white : new Color(1, 1, 1, 0.1f); }
        }
    }

    /// <summary>Yes/no question over the window.</summary>
    internal sealed class Confirmation
    {
        private readonly GameObject _root;
        private readonly Text _question;
        private readonly ButtonWithEvents _yes;
        private Action _onYes;

        public bool Visible => _root.activeSelf;

        public Confirmation(MarketplaceModal modal)
        {
            var dim = GameUi.Image(modal.Box, "Confirmation", new Color(0, 0, 0, 0.85f));
            dim.raycastTarget = true;
            var rt = (RectTransform)dim.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(16, 10); rt.offsetMax = new Vector2(-16, -10);
            _root = dim.gameObject;
            _question = GameUi.Text(_root.transform, "Question", modal.Regular, 34, TextAnchor.MiddleCenter, Color.white);
            GameUi.Place(_question, 0, 80, 1100, 260);
            _yes = modal.MakeButton(_root.transform, "CONFIRMER", -170, -140, 300, 84, () => Answer(true), 34);
            modal.MakeButton(_root.transform, "ANNULER", 170, -140, 300, 84, () => Answer(false), 34);
            _root.SetActive(false);
        }

        public void Ask(string question, Action onYes)
        {
            _question.text = question;
            _onYes = onYes;
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            GameUi.Select(_yes.gameObject);
        }

        public void Answer(bool yes)
        {
            _root.SetActive(false);
            var action = _onYes;
            _onYes = null;
            if (yes) action?.Invoke();
        }
    }
}
