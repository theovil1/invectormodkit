using System.Linq;
using HarmonyLib;
using HelloThere.Avicii;
using HelloThere.Modules.MenuModules;
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

        /// <summary>The main menu rebuilds its buttons every time it opens: add ours after "DLC" each time.</summary>
        [HarmonyPatch(typeof(StateMainMenu), nameof(StateMainMenu.SetupButtons)), HarmonyPostfix]
        private static void AddMenuButton(StateMainMenu __instance)
        {
            var buttons = __instance.menuButtons;
            if (buttons == null || buttons.Length == 0) return;
            var list = buttons[0].transform.parent;
            foreach (var old in list.GetComponentsInChildren<MarketplaceEntry>(true)) Object.Destroy(old.gameObject);

            int after = System.Array.FindIndex(buttons, b => b.GetComponentInChildren<Text>(true)?.text.Trim().ToUpperInvariant() == "DLC");
            if (after < 0) after = buttons.Length - 2; // before "Quit"
            var template = buttons[Mathf.Max(0, after)];
            var go = GameUi.Clone(template.gameObject, list, "button " + Label);
            go.AddComponent<MarketplaceEntry>();
            go.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            go.GetComponentInChildren<Text>(true).text = Label;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(() => MarketplaceModal.Open(MarketplacePlugin.Instance));

            // Navigation: template -> ours -> the next one.
            var next = after + 1 < buttons.Length ? buttons[after + 1] : null;
            SetVertical(template, template.navigation.selectOnUp, button);
            SetVertical(button, template, next);
            if (next) SetVertical(next, button, next.navigation.selectOnDown);
            var all = buttons.ToList();
            all.Insert(after + 1, button);
            __instance.menuButtons = all.ToArray();
        }

        private static void SetVertical(Selectable s, Selectable up, Selectable down) =>
            s.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down };

        [HarmonyPatch(typeof(StateSelectSongSingleplayer), nameof(StateSelectSongSingleplayer.PopulateInputIndicators)), HarmonyPostfix]
        private static void AddHintSingle() => AddSongSelectHint();

        [HarmonyPatch(typeof(StateSelectSongMultiplayer), nameof(StateSelectSongMultiplayer.PopulateInputIndicators)), HarmonyPostfix]
        private static void AddHintMulti() => AddSongSelectHint();

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
            if (label) label.text = "MARKETPLACE";
            var icon = GameUi.Find<Image>(go, "icon");
            var key = GameUi.KeySprite("Keyboard_M");
            if (icon && key) icon.sprite = key;
            else if (label) label.text = "[M]  MARKETPLACE";
            var button = go.GetComponent<Button>();
            if (button) button.onClick.AddListener(() => MarketplaceModal.Open(MarketplacePlugin.Instance));
        }

        // While the window is open, the menu underneath must not react (difficulty, worlds, sorting, DLC…).
        [HarmonyPatch(typeof(StateMainMenu), nameof(StateMainMenu.Update)), HarmonyPrefix]
        private static bool MainMenuUpdate() => !MarketplaceModal.IsOpen;

        [HarmonyPatch(typeof(StateSelectSongSingleplayer), nameof(StateSelectSongSingleplayer.Update)), HarmonyPrefix]
        private static bool SongSelectUpdate() => !MarketplaceModal.IsOpen;

        [HarmonyPatch(typeof(StateSelectSongMultiplayer), nameof(StateSelectSongMultiplayer.Update)), HarmonyPrefix]
        private static bool SongSelectMultiUpdate() => !MarketplaceModal.IsOpen;

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
