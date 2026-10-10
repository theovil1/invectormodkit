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
        private TextPrompt _prompt;

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
                // The menus are drawn through the menu camera: raycasts must use it too, or clicks land elsewhere.
                var menuCanvas = canvas.GetComponentInParent<Canvas>()?.rootCanvas;
                if (menuCanvas && menuCanvas.worldCamera) layer.worldCamera = menuCanvas.worldCamera;
                var raycaster = root.AddComponent<GraphicRaycaster>();
                MarketplacePlugin.Log.LogInfo($"Marketplace window: {layer.renderMode}, camera {(layer.worldCamera ? layer.worldCamera.name : "none")}, raycaster camera {(raycaster.eventCamera ? raycaster.eventCamera.name : "none")}");
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
            AddFill(Box);
            foreach (var name in new[] { "Answer Yes", "Answer No" })
            {
                var old = Box.Find(name);
                if (old) Destroy(old.gameObject);
            }
            var title = GameUi.Find<Text>(Box.gameObject, "Text");
            Regular = title.font;
            Bold = GameUi.Font("Exo2-BoldCondensed", Regular);
            SemiBold = GameUi.Font("Exo2-SemiBoldCondensed", Regular);
            title.text = Loc.T("title");
            title.alignment = TextAnchor.MiddleLeft;
            title.resizeTextForBestFit = false;
            title.fontSize = 44;
            title.font = Bold;
            GameUi.Place(title, -Width / 2 + 60 + 300, Height / 2 - 70, 600, 60);

            _account = GameUi.Text(Box, "Account", Regular, 26, TextAnchor.MiddleRight, GameUi.Muted);
            GameUi.Place(_account, Width / 2 - 440, Height / 2 - 70, 380, 50);
            _signIn = MakeButton(Box, Loc.T("sign_in"), Width / 2 - 230, Height / 2 - 70, 220, 56, SignIn, 26);
            MakeButton(Box, "X", Width / 2 - 70, Height / 2 - 70, 64, 56, Shut, 30);

            _browseTab = MakeButton(Box, Loc.T("tab_browse"), -Width / 2 + 190, Height / 2 - 150, 260, 60, () => Show(_browse));
            _mineTab = MakeButton(Box, Loc.T("tab_mine"), -Width / 2 + 470, Height / 2 - 150, 260, 60, () => Show(_mine));

            var hints = GameUi.Text(Box, "Hints", Regular, 24, TextAnchor.MiddleCenter, GameUi.Muted);
            hints.text = Loc.T("hints");
            GameUi.Place(hints, 0, -Height / 2 + 40, Width - 120, 40);

            _browse = new BrowseTab(this);
            _mine = new MySongsTab(this);
            _confirmation = new Confirmation(this);
            _prompt = new TextPrompt(this);
            _previousSelection = GameUi.Selected;
            _lastMouse = Input.mousePosition;
            TakeInput();
            HideShip();
            RefreshAccount();
            Show(_browse);
        }

        private void Show(ITab tab)
        {
            if (_confirmation.Visible || _prompt.Visible) return;
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

        // ----- mouse
        // While the window is open it reads every input itself and the game's input module sends nothing: the
        // module only feeds the mouse to the Rewired players controlling the current menu (which may not own
        // it), and its keys (move, submit) could reach game objects behind the window.

        private Rewired.Integration.UnityUI.RewiredStandaloneInputModule _module;
        private bool _moduleMouse, _moduleNavigation;
        private Vector3 _lastMouse;
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();

        private void TakeInput()
        {
            if (EventSystem.current == null) return;
            _moduleNavigation = EventSystem.current.sendNavigationEvents;
            EventSystem.current.sendNavigationEvents = false;
            _module = EventSystem.current.currentInputModule as Rewired.Integration.UnityUI.RewiredStandaloneInputModule;
            if (_module == null) return;
            _moduleMouse = _module.allowMouseInput;
            _module.allowMouseInput = false;
        }

        private void GiveInputBack()
        {
            if (_module) _module.allowMouseInput = _moduleMouse;
            if (EventSystem.current) EventSystem.current.sendNavigationEvents = _moduleNavigation;
            _module = null;
        }

        /// <summary>Arrows / stick move along the explicit navigation, OK (Enter, A) presses the selected button.</summary>
        private void Navigate()
        {
            var current = GameUi.Selected;
            var selectable = current ? current.GetComponent<Selectable>() : null;
            var dir = Direction();
            if (selectable == null || !selectable.transform.IsChildOf(transform))
            {
                if (dir != Vector2Int.zero && _current?.FirstItem) GameUi.Select(_current.FirstItem);
                return;
            }
            Selectable next = dir.y > 0 ? selectable.FindSelectableOnUp() : dir.y < 0 ? selectable.FindSelectableOnDown()
                            : dir.x > 0 ? selectable.FindSelectableOnRight() : dir.x < 0 ? selectable.FindSelectableOnLeft() : null;
            if (next && next.gameObject.activeInHierarchy && next.IsInteractable()) GameUi.Select(next.gameObject);
            else if (dir.x < 0 && _current?.LastRow && current != _current.LastRow) GameUi.Select(_current.LastRow); // from the actions back to the list
            if (OkPressed() && selectable is Button button && button.IsInteractable()) button.onClick.Invoke();
        }

        private static Vector2Int Direction()
        {
            try { return new Vector2Int(RewiredInputHelper.GetHorizontalDirectionUIAll(), RewiredInputHelper.GetVerticalDirectionUIAll()); }
            catch (Exception) { return Vector2Int.zero; }
        }

        private static bool OkPressed()
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) return true;
            try { return RewiredInputHelper.GetButtonDownAll(RewiredInputHelper.ActionIdOk); }
            catch (Exception) { return false; }
        }

        private void HandleMouse()
        {
            var position = Input.mousePosition;
            bool moved = (position - _lastMouse).sqrMagnitude > 1f;
            _lastMouse = position;
            bool click = Input.GetMouseButtonDown(0);
            float wheel = Input.mouseScrollDelta.y;
            if ((!moved && !click && wheel == 0) || EventSystem.current == null || !Cursor.visible) return;

            var data = new PointerEventData(EventSystem.current) { position = position, button = PointerEventData.InputButton.Left, scrollDelta = Input.mouseScrollDelta };
            _hits.Clear();
            EventSystem.current.RaycastAll(data, _hits);
            var hit = _hits.Count > 0 && _hits[0].gameObject.transform.IsChildOf(transform) ? _hits[0].gameObject : null;
            if (hit == null) return;
            var selectable = hit.GetComponentInParent<Selectable>();
            bool usable = selectable && selectable.IsInteractable() && selectable.transform.IsChildOf(transform);

            if (moved && usable && GameUi.Selected != selectable.gameObject) GameUi.Select(selectable.gameObject);
            if (click && usable)
            {
                GameUi.Select(selectable.gameObject);
                ExecuteEvents.Execute(selectable.gameObject, data, ExecuteEvents.pointerClickHandler);
            }
            if (wheel != 0) ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.scrollHandler);
        }

        private void Update()
        {
            HandleMouse();
            if (_prompt.Visible)
            {
                _prompt.Tick();
                return;
            }
            if (_confirmation.Visible)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || CancelPressed()) _confirmation.Answer(false);
                else Navigate();
                return;
            }
            bool typing = _current != null && _current.Typing;
            if (!typing && (Input.GetKeyDown(KeyCode.Escape) || (!Input.GetKey(KeyCode.Backspace) && CancelPressed())))
            {
                Shut();
                return;
            }
            if (!typing && Input.GetKeyDown(KeyCode.Tab)) Show(_current == _browse ? (ITab)_mine : _browse);
            else if (!typing) Navigate();
            _current?.Tick();
            RefreshAccount();
        }

        private static bool CancelPressed()
        {
            try { return RewiredInputHelper.GetButtonDownAll(RewiredInputHelper.ActionIdCancel); }
            catch (Exception) { return false; }
        }

        // The ship selector's ship is a 3D model in front of the UI plane: it would show through the window.
        private GameObject _ship;

        private void HideShip()
        {
            var holder = GameUi.SongMenu?.shipSelector?.shipHolder3d;
            if (holder == null || !holder.gameObject.activeSelf) return;
            _ship = holder.gameObject;
            _ship.SetActive(false);
        }

        private void ShowShip()
        {
            if (_ship) _ship.SetActive(true);
            _ship = null;
        }

        private void OnDestroy()
        {
            GiveInputBack();
            ShowShip();
        }

        private void Shut()
        {
            GiveInputBack();
            ShowShip();
            _instance = null;
            Destroy(gameObject);
            if (_previousSelection) GameUi.Select(_previousSelection);
        }

        // ----- account

        private void RefreshAccount()
        {
            var session = MarketplacePlugin.Session;
            _account.text = session.SignedIn ? Loc.T("signed_in", session.Current.Name + (session.Current.Role == "admin" ? Loc.T("admin_suffix") : ""))
                          : session.SigningIn ? Loc.T("signing_in") : "";
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
                if (this) _current?.SetStatus(Loc.T("sign_in_failed", e.GetBaseException().Message));
                return false;
            }
        }

        private async void SignIn() => await EnsureSignedIn();

        public void Confirm(string question, Action onYes) => _confirmation.Ask(question, onYes);

        /// <summary>Asks for one line of text; <paramref name="validate"/> returns why it is refused, or null.</summary>
        public void Prompt(string question, string hint, string initial, Func<string, string> validate, Action<string> onOk) =>
            _prompt.Ask(question, hint, initial, validate, onOk);

        // ----- shared builders (the game's own pieces)

        public ButtonWithEvents MakeButton(Transform parent, string label, float x, float y, float width, float height, Action onClick, int fontSize = 30)
        {
            var go = GameUi.Clone(GameUi.QuestionButtonTemplate.gameObject, parent, "Button " + label);
            var button = go.GetComponent<ButtonWithEvents>();
            GameUi.Place(button, x, y, width, height);
            StyleButton(button, label, fontSize, onClick);
            return button;
        }

        /// <summary>Centred, best-fit label and a click handler on one of the game's dialog buttons.</summary>
        public static void StyleButton(ButtonWithEvents button, string label, int fontSize, Action onClick)
        {
            var text = button.GetComponentInChildren<Text>(true);
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
        }

        /// <summary>A solid fill under a dialog box's frame, so nothing shows through.</summary>
        public static void AddFill(RectTransform box)
        {
            var fill = GameUi.Image(box, "Fill", new Color32(0x0C, 0x09, 0x12, 0xF5));
            var rt = (RectTransform)fill.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(18, 12); rt.offsetMax = new Vector2(-18, -12);
            fill.transform.SetAsFirstSibling();
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
        /// <summary>Where keyboard navigation starts when nothing in the window is selected.</summary>
        GameObject FirstItem { get; }
        /// <summary>The list row last selected, to come back to from the action buttons.</summary>
        GameObject LastRow { get; }
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

    /// <summary>Yes/no question over the window: the game's own question dialog, a little larger.</summary>
    internal sealed class Confirmation
    {
        private readonly GameObject _root;
        private readonly Text _question;
        private readonly ButtonWithEvents _yes;
        private Action _onYes;

        public bool Visible => _root.activeSelf;

        public Confirmation(MarketplaceModal modal)
        {
            _root = GameUi.Clone(GameUi.DialogTemplate, modal.transform, "Confirmation", stillImage: true);
            var group = _root.GetComponent<CanvasGroup>();
            if (group) { group.alpha = 1; group.interactable = true; group.blocksRaycasts = true; }
            var backdrop = GameUi.Find<Image>(_root, "Background");
            if (backdrop) backdrop.color = new Color(0, 0, 0, 0.8f);
            var box = (RectTransform)_root.transform.Find("QuestionBox");
            GameUi.Place(box, 0, 0, 960, 380);
            MarketplaceModal.AddFill(box);

            _question = GameUi.Find<Text>(box.gameObject, "Text");
            _question.alignment = TextAnchor.MiddleCenter;
            _question.resizeTextForBestFit = true;
            _question.resizeTextMinSize = 20;
            _question.resizeTextMaxSize = 36;
            GameUi.Place(_question, 0, 60, 860, 190);

            _yes = GameUi.Find<ButtonWithEvents>(box.gameObject, "Answer Yes");
            var no = GameUi.Find<ButtonWithEvents>(box.gameObject, "Answer No");
            GameUi.Place(_yes, -165, -115, 300, 84);
            GameUi.Place(no, 165, -115, 300, 84);
            MarketplaceModal.StyleButton(_yes, Loc.T("confirm"), 34, () => Answer(true));
            MarketplaceModal.StyleButton(no, Loc.T("cancel"), 34, () => Answer(false));
            _yes.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = no };
            no.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = _yes };
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
