using System.Linq;
using HarmonyLib;
using HelloThere.Avicii;
using HelloThere.Modules.AudioModules;
using HelloThere.Modules.MenuModules;
using HelloThere.Signals;
using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>Marks the objects this plugin added to the game's menus, so they can be found and replaced.</summary>
    internal sealed class MarketplaceEntry : MonoBehaviour { }

    /// <summary>
    /// The ways in: a "Marketplace" button under "DLC" in the main menu, a "MARKETPLACE" key hint in song
    /// select (also the M key), and the menus underneath ignoring input while the window is open.
    /// </summary>
    internal static class EntryPoints
    {
        private const string Label = "Marketplace";

        private static UIItemData _menuItem;
        private static SignalObject _noSignal;

        /// <summary>
        /// The main menu builds its buttons from the list's content every time it opens: our entry goes into
        /// that content, after "DLC", so the game builds it like the others (animations, navigation, sounds).
        /// </summary>
        [HarmonyPatch(typeof(StateMainMenu), nameof(StateMainMenu.SetupButtons)), HarmonyPrefix]
        private static void AddMenuItem()
        {
            var content = View.Get(StateMenuModuleBase.menuSceneRoot.refStartMenu)?.listStart?.GetComponent<UIListContent>();
            if (content?.Content == null) return;
            if (_menuItem == null)
                _menuItem = new UIItemData
                {
                    stringId = "",
                    submit = () =>
                    {
                        AudioModule.AudioProvider.Play(AudioShortcut.SFX_MENU_CONFIRM);
                        MarketplaceModal.Open(MarketplacePlugin.Instance);
                    },
                };
            _menuItem.label = Loc.T("menu_label"); // follows the game's language
            // The game re-resolves every entry's signal by name before building (and fails on a missing one);
            // ours resolves to nothing, which is fine: the button then only runs `submit`.
            if (!_noSignal)
            {
                _noSignal = ScriptableObject.CreateInstance<SignalObject>();
                _noSignal.name = "InvectorModKit.Marketplace";
                _noSignal.hideFlags = HideFlags.HideAndDontSave;
            }
            _menuItem.signal = _noSignal;

            var items = content.Content.ToList();
            if (items.Contains(_menuItem)) return;
            int dlc = items.FindLastIndex(i => i.stringId != null && i.stringId.Contains("dlc"));
            int options = items.FindIndex(i => i.stringId != null && i.stringId.Contains("option"));
            items.Insert(dlc >= 0 ? dlc + 1 : options >= 0 ? options : Mathf.Max(0, items.Count - 1), _menuItem);
            content.Content = items.ToArray();
        }

        [HarmonyPatch(typeof(StateSelectSongSingleplayer), nameof(StateSelectSongSingleplayer.PopulateInputIndicators)), HarmonyPostfix]
        private static void AddHintSingle() => AddSongSelectHintSoon();

        [HarmonyPatch(typeof(StateSelectSongMultiplayer), nameof(StateSelectSongMultiplayer.PopulateInputIndicators)), HarmonyPostfix]
        private static void AddHintMulti() => AddSongSelectHintSoon();

        /// <summary>
        /// The game sizes its hints itself after building them (their width is not a layout's): ours is copied from
        /// one on the next frame, once it has its final size.
        /// </summary>
        private static void AddSongSelectHintSoon()
        {
            var indicators = GameUi.SongMenu?.inputIndicators;
            if (indicators != null)
                foreach (var old in indicators.GetComponentsInChildren<MarketplaceEntry>(true)) Object.Destroy(old.gameObject);
            if (MarketplacePlugin.Instance) MarketplacePlugin.Instance.StartCoroutine(NextFrame(AddSongSelectHint));
        }

        private static System.Collections.IEnumerator NextFrame(System.Action action)
        {
            yield return null;
            try { action(); }
            catch (System.Exception e) { MarketplacePlugin.Log.LogError(e); }
        }

        /// <summary>Song select's bottom bar gets a "MARKETPLACE" hint (M key), clickable like the others.</summary>
        private static void AddSongSelectHint()
        {
            var indicators = GameUi.SongMenu?.inputIndicators;
            if (indicators == null) return;
            foreach (var old in indicators.GetComponentsInChildren<MarketplaceEntry>(true)) Object.Destroy(old.gameObject);
            var template = indicators.GetItemByName("ToggleSorting") ?? indicators.GetComponentsInChildren<InputIndicatorItem>(true).LastOrDefault();
            if (template == null) return;
            var go = GameUi.Clone(template.gameObject, indicators.transform, Label);
            go.AddComponent<MarketplaceEntry>();
            var label = GameUi.Find<Text>(go, "label");
            if (label) label.text = Loc.T("title");
            var icon = GameUi.Find<Image>(go, "icon");
            var key = GameUi.KeySprite("Keyboard_M");
            if (icon && key) icon.sprite = key;
            else if (label) label.text = "[M]  " + Loc.T("title");
            var button = go.GetComponent<Button>();
            if (button) button.onClick.AddListener(() => MarketplaceModal.Open(MarketplacePlugin.Instance));
            // Our own size, from our content (the game's hints get theirs from InputIndicatorItem, which a copy lacks).
            float width = (icon && key ? 55 + 15 : 0) + (label ? label.preferredWidth : 200);
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, 55);
            var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = width;
            layout.minHeight = layout.preferredHeight = 55;
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)indicators.transform);
        }

        // While the window is open, the menu underneath must not react (difficulty, worlds, sorting, DLC…),
        // but the base state update also shows the mouse cursor when the mouse moves: keep that part.
        [HarmonyPatch(typeof(StateMainMenu), nameof(StateMainMenu.Update)), HarmonyPrefix]
        private static bool MainMenuUpdate(StateMainMenu __instance) => Pass(__instance);

        [HarmonyPatch(typeof(StateSelectSongSingleplayer), nameof(StateSelectSongSingleplayer.Update)), HarmonyPrefix]
        private static bool SongSelectUpdate(StateSelectSongSingleplayer __instance) => Pass(__instance);

        [HarmonyPatch(typeof(StateSelectSongMultiplayer), nameof(StateSelectSongMultiplayer.Update)), HarmonyPrefix]
        private static bool SongSelectMultiUpdate(StateSelectSongMultiplayer __instance) => Pass(__instance);

        private static bool Pass(HelloThere.Applications.ApplicationState state)
        {
            if (!MarketplaceModal.IsOpen) return true;
            // Same as ApplicationState.Update: the cursor shows while the mouse is the last device used.
            bool mouse = Rewired.ReInput.isReady && Rewired.ReInput.controllers.GetLastActiveControllerType() == Rewired.ControllerType.Mouse;
            if (Cursor.visible != mouse)
            {
                Cursor.visible = mouse;
                state.UpdatePointerHoverState();
            }
            return false;
        }

        /// <summary>M opens the marketplace from the main menu and song select.</summary>
        public static bool CanOpenHere
        {
            get
            {
                var state = ModKit.CurrentState;
                return state is StateMainMenu || state is StateSelectSongSingleplayer || state is StateSelectSongMultiplayer;
            }
        }
    }
}
