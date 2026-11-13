using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using InvectorModKit.CustomSongs;
using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// The marketplace: a page of the game over the whole display (its own canvas, opaque), opened from the main menu
    /// or song select and closed back to them. While it is open the menu underneath is hidden, silent and takes no
    /// input (<see cref="ScreenInput"/>, <see cref="MenuAudio"/>). Top bar with the tabs, the tab's body, the key bar.
    /// </summary>
    internal sealed class MarketplaceScreen : MonoBehaviour
    {
        public static bool IsOpen => _instance != null;
        private static MarketplaceScreen _instance;
        private static bool _opening;

        /// <summary>The tabs' row, under the title row: its top, its height, one tab's width.</summary>
        public const float TabsTop = 104, TabsHeight = 62, TabWidth = 400, Side = 60;

        /// <summary>The body, between the tabs and the key bar.</summary>
        public RectTransform Body;
        public readonly ScreenInput Input = new ScreenInput();
        public PreviewPlayer Player;
        public PreviewCache Excerpts;
        public KeyBar Keys;

        private CanvasScaler _scaler;
        private Text _account;
        private ButtonWithEvents _signIn, _rules, _close;
        private readonly List<(ITab tab, ButtonWithEvents button)> _tabs = new List<(ITab, ButtonWithEvents)>();
        private ITab _current;
        private GameObject _previousSelection;
        private Confirmation _confirmation;
        private TextPrompt _prompt;
        private (ButtonWithEvents button, DiscordPulse pulse) _discord;

        /// <param name="then">Runs with the screen once it is open (or at once when it already is).</param>
        public static void Open(MonoBehaviour host, Action<MarketplaceScreen> then = null)
        {
            if (IsOpen) { then?.Invoke(_instance); return; }
            if (_opening) return;
            host.StartCoroutine(OpenRoutine(then));
        }

        public static void Close()
        {
            if (_instance != null) _instance.Shut();
        }

        private static IEnumerator OpenRoutine(Action<MarketplaceScreen> then)
        {
            _opening = true;
            try
            {
                yield return GameUi.EnsureSongMenuLoaded();
                if (GameUi.DialogTemplate == null || GameUi.QuestionButtonTemplate == null || GameUi.SongRowTemplate == null)
                {
                    MarketplacePlugin.Log.LogWarning("Marketplace: the game's menu pieces are not loaded yet");
                    yield break;
                }
                var root = new GameObject("Marketplace", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                DontDestroyOnLoad(root);
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000;
                var screen = root.AddComponent<MarketplaceScreen>();
                screen._scaler = root.GetComponent<CanvasScaler>();
                screen._scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                screen._scaler.referenceResolution = new Vector2(1920, 1080);
                screen.FitScaler();
                screen.Build();
                _instance = screen;
                then?.Invoke(screen);
            }
            finally
            {
                _opening = false;
            }
        }

        /// <summary>Always at least 1920 x 1080 units: width matched on screens narrower than 16:9, height on wider ones.</summary>
        private void FitScaler()
        {
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            _scaler.matchWidthOrHeight = aspect >= 16f / 9f ? 1 : 0;
        }

        private void Build()
        {
            var dialogText = GameUi.Find<Text>(GameUi.DialogTemplate, "QuestionBox/Text");
            UiKit.Init(dialogText ? dialogText.font : Resources.GetBuiltinResource<Font>("Arial.ttf"));

            var background = GameUi.Image(transform, "Background", new Color32(0x12, 0x0A, 0x2A, 0xFF));
            background.raycastTarget = true; // nothing behind takes a click
            UiKit.Fill(background);
            var glow = GameUi.Image(background.transform, "Glow", new Color32(0x6A, 0x22, 0xB0, 0x70));
            glow.sprite = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == "Glow");
            glow.gameObject.SetActive(glow.sprite != null);
            var glowRt = (RectTransform)glow.transform;
            glowRt.anchorMin = glowRt.anchorMax = new Vector2(0.72f, 0.75f);
            glowRt.sizeDelta = new Vector2(2200, 1500);

            BuildTopBar();

            Body = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
            Body.SetParent(transform, false);
            UiKit.Fill(Body, Side, TabsTop + TabsHeight + 24, Side, KeyBar.Height + 16);

            Keys = new KeyBar(transform);
            Player = gameObject.AddComponent<PreviewPlayer>();
            Excerpts = new PreviewCache(this);

            var tabs = new List<(ITab, string)> { (new DiscoverTab(this), "tab_discover"), (new LibraryTab(this), "tab_library"), (new PublicationsTab(this), "tab_publications") };
            if (MarketplacePlugin.Session.Current?.CanModerate == true) tabs.Add((new ModerationTab(this), "tab_moderation")); // admins only
            BuildTabs(tabs.ToArray());
            _confirmation = new Confirmation(this);
            _prompt = new TextPrompt(this);
            _previousSelection = GameUi.Selected;
            Input.Take();
            MenuAudio.Silence();
            RefreshAccount();
            Show(_tabs[0].tab);
        }

        /// <summary>The tabs' own row, under the title: equal widths, room for a fourth (Moderation) in every language.</summary>
        private void BuildTabs(params (ITab tab, string labelKey)[] tabs)
        {
            float x = Side;
            foreach (var (tab, key) in tabs)
            {
                var t = tab;
                var button = UiKit.Button(transform, Loc.T(key), ButtonStyle.Tab, () => Show(t));
                Top(button, x, TabsTop, TabWidth, TabsHeight);
                x += TabWidth + 12;
                _tabs.Add((t, button));
            }
        }

        private void BuildTopBar()
        {
            var title = UiKit.Text(transform, "Title", TextRole.Title);
            title.resizeTextMaxSize = title.fontSize = 54;
            title.resizeTextMinSize = 30; // "マーケットプレイス" shrinks rather than lose its end
            title.text = Loc.T("title");
            WordFit.Add(title);
            title.alignment = TextAnchor.MiddleLeft;
            Top(title, Side, 24, 380, 66);

            _close = UiKit.Button(transform, "X", ButtonStyle.Secondary, Shut);
            TopRight(_close, Side, 26, 66, 62);
            _rules = UiKit.Button(transform, Loc.T("rules"), ButtonStyle.Secondary, () => Application.OpenURL(MarketplacePlugin.Api.RulesUrl(Loc.Code)));
            TopRight(_rules, Side + 66 + 16, 26, 200, 62);
            float right = Side + 66 + 16 + 200 + 16;
            if (Community.HasDiscord)
            {
                AddDiscordButton(right);
                right += 220 + 16;
            }
            _signIn = UiKit.Button(transform, Loc.T("sign_in"), ButtonStyle.Secondary, SignIn);
            TopRight(_signIn, right, 26, 240, 62);
            // From the title to the buttons on the right: a long name or language shrinks and wraps there.
            _account = UiKit.Text(transform, "Account", TextRole.Muted);
            _account.alignment = TextAnchor.MiddleRight;
            var account = _account.rectTransform;
            account.anchorMin = new Vector2(0, 1);
            account.anchorMax = new Vector2(1, 1);
            account.pivot = new Vector2(1, 1);
            account.offsetMin = new Vector2(Side + 380 + 24, -26 - 62);
            account.offsetMax = new Vector2(-right, -26);
        }

        private static void Top(Component c, float left, float top, float width, float height) => UiKit.At(c, left, top, width, height);

        private static void TopRight(Component c, float right, float top, float width, float height)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-right, -top);
            rt.sizeDelta = new Vector2(width, height);
        }

        /// <summary>"DISCORD" with the Discord logo (no glow): the community's server.</summary>
        private void AddDiscordButton(float right)
        {
            var button = UiKit.Button(transform, "DISCORD", ButtonStyle.Secondary, Community.OpenDiscord);
            TopRight(button, right, 26, 220, 62);
            var label = UiKit.Label(button).rectTransform;
            label.offsetMin = new Vector2(label.offsetMin.x + 52, label.offsetMin.y);
            var logo = GameUi.Image(button.transform, "Logo", Color.white);
            logo.sprite = Community.Logo;
            logo.preserveAspect = true;
            UiKit.At(logo, 20, 13, 40, 36);
            var pulse = button.gameObject.AddComponent<DiscordPulse>();
            pulse.Logo = logo.rectTransform;
            pulse.LogoImage = logo;
            pulse.Float = 2f;
            pulse.HoverColor = Community.Purple; // the game's buttons turn white when selected
            _discord = (button, pulse);
        }

        // ----- tabs

        private void Show(ITab tab)
        {
            if (_confirmation.Visible || _prompt.Visible || tab == _current) return;
            _current?.Hide();
            Player.Stop();
            _current = tab;
            tab.Show();
            var down = tab.FirstItem ? tab.FirstItem.GetComponent<Selectable>() : null;
            for (int i = 0; i < _tabs.Count; i++)
            {
                UiKit.SetSelected(_tabs[i].button, _tabs[i].tab == tab);
                _tabs[i].button.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = i > 0 ? _tabs[i - 1].button : null,
                    selectOnRight = i + 1 < _tabs.Count ? _tabs[i + 1].button : null,
                    selectOnDown = down,
                };
            }
        }

        /// <summary>Tab / RB: the next tab; Shift+Tab / LB: the previous one (round).</summary>
        private void NextTab(int dir)
        {
            int i = _tabs.FindIndex(t => t.tab == _current);
            Show(_tabs[((i + dir) % _tabs.Count + _tabs.Count) % _tabs.Count].tab);
        }

        // ----- frame

        private void Update()
        {
            FitScaler();
            if (MarketplacePlugin.LanguageCycling != null && MarketplacePlugin.LanguageCycling.Value && UnityEngine.Input.GetKeyDown(KeyCode.F6))
            {
                Loc.Override = (Loc.Column + 1) % Loc.Languages;
                Shut();
                Open(MarketplacePlugin.Instance);
                return;
            }
            Input.HoldNavigation();
            Input.Typing = (_current != null && _current.Typing) || _prompt.Visible;
            Input.Read();
            Input.Mouse(transform);
            Keys.Tick();
            if (_discord.pulse) _discord.pulse.Hovered = GameUi.Selected == _discord.button.gameObject;
            if (_prompt.Visible)
            {
                _prompt.Tick();
                return;
            }
            if (_confirmation.Visible)
            {
                if (Input.Back) _confirmation.Answer(false);
                else Navigate();
                return;
            }
            if (_current != null && _current.Typing)
            {
                _current.Tick();
                return;
            }
            if (Input.Back)
            {
                Shut();
                return;
            }
            if (Input.NextTab) NextTab(1);
            else if (Input.PreviousTab) NextTab(-1);
            else if (Input.Listen) _current?.Listen();
            else if (Input.Search) _current?.Search();
            else Navigate();
            _current?.Tick();
            RefreshAccount();
        }

        /// <summary>Arrows / stick along the explicit navigation; OK presses the selected button.</summary>
        private void Navigate()
        {
            var current = GameUi.Selected;
            var selectable = current ? current.GetComponent<Selectable>() : null;
            var dir = Input.Move;
            if (selectable == null || !selectable.transform.IsChildOf(transform))
            {
                if ((dir != Vector2Int.zero || Input.Ok) && _current?.FirstItem) GameUi.Select(_current.FirstItem);
                return;
            }
            Selectable next = dir.y > 0 ? selectable.FindSelectableOnUp() : dir.y < 0 ? selectable.FindSelectableOnDown()
                            : dir.x > 0 ? selectable.FindSelectableOnRight() : dir.x < 0 ? selectable.FindSelectableOnLeft() : null;
            if (next && next.gameObject.activeInHierarchy && next.IsInteractable()) GameUi.Select(next.gameObject);
            else if (dir.x < 0 && _current?.LastRow && current != _current.LastRow) GameUi.Select(_current.LastRow); // from the sheet back to the list
            if (Input.Ok && selectable is Button button && button.IsInteractable()) button.onClick.Invoke();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            _current?.Hide(); // tabs let go of what they listen to (the link downloads)
            LinkDownloads.ForgetFailed();
            Input.GiveBack();
            Excerpts?.Dispose(); // the last excerpt clip goes with the screen
        }

        private void Shut()
        {
            if (Player) Player.Stop(immediately: true);
            Input.GiveBack();
            MenuAudio.Restore();
            _instance = null;
            Destroy(gameObject);
            var back = SelectionToRestore();
            if (back) GameUi.Select(back);
        }

        /// <summary>
        /// Where the menu underneath takes the keys back. Song select: its own selected song, as its list may have been
        /// rebuilt while the screen was open (a song downloaded or charted): the row selected before is then gone or
        /// pooled, and with no usable selection no key worked. Elsewhere: what was selected before, if still shown.
        /// </summary>
        private GameObject SelectionToRestore()
        {
            if (ModKit.CurrentState is HelloThere.Avicii.StateSelectSongSingleplayer select && select.selectedLevelButton && select.selectedLevelButton.gameObject.activeInHierarchy)
                return select.selectedLevelButton.gameObject;
            return _previousSelection && _previousSelection.activeInHierarchy ? _previousSelection : null;
        }

        // ----- account

        private void RefreshAccount()
        {
            var session = MarketplacePlugin.Session;
            _account.text = session.SignedIn ? Loc.T("signed_in", session.Current.Name + (session.Current.Role == "admin" ? Loc.T("admin_suffix") : ""))
                          : session.SigningIn ? Loc.T("signing_in") : "";
            _signIn.gameObject.SetActive(!session.SignedIn && !session.SigningIn);
            _account.gameObject.SetActive(session.SignedIn || session.SigningIn);
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

        /// <summary>A message in the open tab's status line.</summary>
        public void Status(string text) => _current?.SetStatus(text);

        /// <summary>Asks for one line of text; <paramref name="validate"/> returns why it is refused, or null.</summary>
        public void Prompt(string question, string hint, string initial, Func<string, string> validate, Action<string> onOk) =>
            _prompt.Ask(question, hint, initial, validate, onOk);

        public static void OpenFolder()
        {
            string folder = CustomSongsApi.SongsFolder;
            if (!string.IsNullOrEmpty(folder)) Application.OpenURL("file:///" + folder.Replace('\\', '/'));
        }

        // ----- shared builders (the game's own pieces), used by the tabs and the dialogs

        /// <summary>Centred label with the screen's margins, shrinking to fit, and a click handler, on one of the game's dialog buttons.</summary>
        public static void StyleButton(ButtonWithEvents button, string label, int fontSize, Action onClick)
        {
            var text = button.GetComponentInChildren<Text>(true);
            foreach (var fitter in text.GetComponents<ContentSizeFitter>()) Destroy(fitter);
            UiKit.Fill(text, UiKit.Margin, 4, UiKit.Margin, 4);
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.lineSpacing = 0.9f;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Math.Min(fontSize, UiKit.MinFont);
            text.resizeTextMaxSize = fontSize;
            text.fontSize = fontSize;
            text.text = label;
            WordFit.Add(text);
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

        /// <summary>A vertical scroll list (viewport with mask, content laid out top-down), centred coordinates.</summary>
        public static (ScrollRect scroll, RectTransform content) MakeList(Transform parent, float x, float y, float width, float height)
        {
            var (scroll, content) = MakeList(parent);
            GameUi.Place(scroll, x, y, width, height);
            return (scroll, content);
        }

        /// <summary>A vertical scroll list, to be placed by the caller.</summary>
        public static (ScrollRect scroll, RectTransform content) MakeList(Transform parent)
        {
            var viewport = GameUi.Image(parent, "List", new Color(0, 0, 0, 0.25f));
            viewport.raycastTarget = true;
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

        /// <summary>Explicit up/down navigation through rows, right into <paramref name="firstAction"/>.</summary>
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
        /// <summary>Where keyboard navigation starts when nothing in the screen is selected.</summary>
        GameObject FirstItem { get; }
        /// <summary>The list row last selected, to come back to from the action buttons.</summary>
        GameObject LastRow { get; }
        void Show();
        void Hide();
        void Tick();
        void SetStatus(string text);
        /// <summary>Space / the controller's listen button: play or stop the selected song's excerpt.</summary>
        void Listen();
        /// <summary>F / the controller's search button.</summary>
        void Search();
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
                    bool mouse = ScreenInput.MouseClicking || UnityEngine.Input.GetMouseButtonUp(0) || UnityEngine.Input.GetMouseButton(0);
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

    /// <summary>Yes/no question over the screen: the game's own question dialog, a little larger.</summary>
    internal sealed class Confirmation
    {
        private readonly GameObject _root;
        private readonly Text _question;
        private readonly ButtonWithEvents _yes, _no;
        private Action _onYes;

        public bool Visible => _root.activeSelf;

        public Confirmation(MarketplaceScreen screen)
        {
            _root = GameUi.Clone(GameUi.DialogTemplate, screen.transform, "Confirmation", stillImage: true);
            UiKit.Fill((RectTransform)_root.transform); // its dark backdrop over the whole screen
            var group = _root.GetComponent<CanvasGroup>();
            if (group) { group.alpha = 1; group.interactable = true; group.blocksRaycasts = true; }
            var backdrop = GameUi.Find<Image>(_root, "Background");
            if (backdrop) backdrop.color = new Color(0, 0, 0, 0.8f);
            var box = (RectTransform)_root.transform.Find("QuestionBox");
            GameUi.Place(box, 0, 0, 960, 380);
            MarketplaceScreen.AddFill(box);

            _question = GameUi.Find<Text>(box.gameObject, "Text");
            _question.alignment = TextAnchor.MiddleCenter;
            _question.resizeTextForBestFit = true;
            _question.resizeTextMinSize = 20;
            _question.resizeTextMaxSize = 36;
            GameUi.Place(_question, 0, 60, 860, 190);

            _yes = GameUi.Find<ButtonWithEvents>(box.gameObject, "Answer Yes");
            var no = _no = GameUi.Find<ButtonWithEvents>(box.gameObject, "Answer No");
            GameUi.Place(_yes, -165, -115, 300, 84);
            GameUi.Place(no, 165, -115, 300, 84);
            MarketplaceScreen.StyleButton(_yes, Loc.T("confirm"), 34, () => Answer(true));
            MarketplaceScreen.StyleButton(no, Loc.T("cancel"), 34, () => Answer(false));
            _yes.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = no };
            no.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = _yes };
            _root.SetActive(false);
        }

        public void Ask(string question, Action onYes)
        {
            _question.text = InvectorModKit.Marketplace.Client.LabelText.SentencePerLine(question);
            _onYes = onYes;
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            GameUi.Select(_no.gameObject); // removing and deleting: Enter alone must not do it
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
