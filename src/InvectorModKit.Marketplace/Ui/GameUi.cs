using System;
using System.Collections;
using System.Linq;
using HelloThere.Avicii;
using HelloThere.Modules.MenuModules;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// The game's own UI pieces, cloned so the marketplace looks exactly like the game (see the DevTools UI
    /// export): dialog frame and buttons, song list rows, key hints, Exo2 fonts. Clones lose the components
    /// that tie them to game logic.
    /// </summary>
    internal static class GameUi
    {
        public static readonly Color Muted = new Color32(0xE1, 0xE1, 0xE1, 0xFF);

        private static GameObject _holder;

        public static ViewStartMenu StartMenu => StateMenuModuleBase.menuSceneRoot?.refStartMenu?.Instance;
        public static ViewSongMenu SongMenu => StateMenuModuleBase.menuSceneRoot?.refSongMenu?.Instance;

        /// <summary>The song menu holds the song row prefab; the game loads it on entering song select.</summary>
        public static IEnumerator EnsureSongMenuLoaded()
        {
            var reference = StateMenuModuleBase.menuSceneRoot?.refSongMenu;
            if (reference == null || reference.Instance != null) yield break;
            View.Load(reference);
            float waited = 0;
            while (reference.Instance == null && waited < 20f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        public static GameObject DialogTemplate => StartMenu?.confirmDialog?.gameObject;
        public static ButtonWithEvents QuestionButtonTemplate => StartMenu?.confirmDialog?.yesButton as ButtonWithEvents;
        public static Button SongRowTemplate => SongMenu?.songList?.itemPrefab;
        public static InputIndicatorItem HintTemplate => SongMenu?.inputIndicators?.ItemPrefab ?? StartMenu?.GetComponentsInChildren<InputIndicatorItem>(true).FirstOrDefault();

        /// <summary>
        /// A copy of <paramref name="template"/> under <paramref name="parent"/>, built while inactive so the
        /// game-logic components never wake up: they are removed, click handlers and navigation reset.
        /// </summary>
        public static GameObject Clone(GameObject template, Transform parent, string name, bool stillImage = false)
        {
            if (_holder == null)
            {
                _holder = new GameObject("marketplace-clone-holder");
                _holder.SetActive(false);
                Object.DontDestroyOnLoad(_holder);
            }
            var go = Object.Instantiate(template, _holder.transform, false);
            go.name = name;
            foreach (var c in go.GetComponentsInChildren<SongMenuItem>(true)) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<UIConfirmDialog>(true)) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<InputIndicatorItem>(true)) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<UILocalizationLabel>(true)) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<CrossPlatformButtonIcon>(true)) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<ControlInputIcon>(true)) Object.DestroyImmediate(c);
            if (stillImage)
            {
                // Dialog animations start hidden: without them the copy simply shows.
                foreach (var c in go.GetComponents<UITransition>()) Object.DestroyImmediate(c);
                foreach (var c in go.GetComponents<Animator>()) Object.DestroyImmediate(c);
            }
            foreach (var b in go.GetComponentsInChildren<Button>(true))
            {
                b.onClick = new Button.ButtonClickedEvent();
                b.navigation = new Navigation { mode = Navigation.Mode.None };
            }
            go.transform.SetParent(parent, false);
            go.SetActive(true);
            return go;
        }

        public static Font Font(string name, Font fallback) =>
            Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f.name == name) ?? fallback;

        /// <summary>A key icon from the game's keyboard atlas ("Keyboard_M"…), or null.</summary>
        public static Sprite KeySprite(string name) =>
            Resources.FindObjectsOfTypeAll<SpriteAtlas>().Where(a => a.name.Contains("Keyboard")).Select(a => a.GetSprite(name)).FirstOrDefault(s => s != null);

        public static T Find<T>(GameObject root, string path) where T : Component
        {
            var t = root.transform.Find(path);
            return t ? t.GetComponent<T>() : null;
        }

        public static Text Text(Transform parent, string name, Font font, int size, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.supportRichText = false;
            return text;
        }

        public static Image Image(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Centred anchors: <paramref name="center"/> relative to the parent's centre, in canvas units.</summary>
        public static RectTransform Place(Component c, float x, float y, float width, float height)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
            return rt;
        }

        public static void Select(GameObject go)
        {
            try { RewiredInputHelper.SetCurrentSelectedGameObject(go); }
            catch (Exception) { UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(go); }
        }

        public static GameObject Selected
        {
            get
            {
                try { return RewiredInputHelper.GetCurrentSelectedObject(); }
                catch (Exception) { return UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject; }
            }
        }
    }
}
