using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// Keeps the Discord logo alive, without any glow: a slow float, and on hover the logo grows and turns brighter.
    /// <see cref="Hovered"/> is set by whoever owns the button (the corner, or the Marketplace window).
    /// </summary>
    internal sealed class DiscordPulse : MonoBehaviour
    {
        public RectTransform Logo;
        public Image LogoImage;
        public float Float = 5f;
        public bool Hovered;
        /// <summary>The logo's colour when hovered (white by default; the window's white buttons need another).</summary>
        public Color HoverColor = Color.white;

        private float _scale = 1f, _hover;
        private Vector2 _home;
        private bool _homeSet;

        private void Update()
        {
            if (!Logo) return;
            if (!_homeSet) { _home = Logo.anchoredPosition; _homeSet = true; }
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            _scale = Mathf.Lerp(_scale, Hovered ? 1.18f : 1f, 1 - Mathf.Exp(-dt * 14f));
            _hover = Mathf.Lerp(_hover, Hovered ? 1f : 0f, 1 - Mathf.Exp(-dt * 10f));

            Logo.anchoredPosition = _home + new Vector2(0, Mathf.Sin(t * 1.6f) * Float * (1 - _hover * 0.6f));
            Logo.localScale = Vector3.one * _scale;
            if (LogoImage) LogoImage.color = Color.Lerp(new Color(0.9f, 0.9f, 1f, 0.92f), HoverColor, _hover);
        }
    }
}
