using UnityEngine;

namespace InvectorModKit.Internal
{
    /// <summary>Small corner label so players always know whether the session is ranked.</summary>
    internal sealed class StatusOverlay : MonoBehaviour
    {
        public static bool Visible = true;
        /// <summary>A plugin draws the corner itself (in the game's style); this plain label then stays hidden.</summary>
        public static bool HandedOver;
        private GUIStyle _style;

        private void OnGUI()
        {
            if (!Visible || HandedOver) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(11, Screen.height / 70), alignment = TextAnchor.LowerLeft };
                _style.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
            }
            string text = Integrity.IsModded
                ? $"{ModKit.Name} {ModKit.Version} · modded: official scores paused"
                : $"{ModKit.Name} {ModKit.Version}";
            float h = _style.fontSize * 1.6f;
            GUI.Label(new Rect(8, Screen.height - h - 4, Screen.width / 2f, h), text, _style);
        }
    }
}
