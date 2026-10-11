using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// Keeps the Discord logo alive: a halo that breathes behind it, a slow float, and on hover the logo grows and
    /// lights up. <see cref="Hovered"/> is set by whoever owns the button (the corner, or the Marketplace window).
    /// </summary>
    internal sealed class DiscordPulse : MonoBehaviour
    {
        public RectTransform Logo;
        public Image Halo, LogoImage;
        public float Float = 5f;
        public bool Hovered;
        /// <summary>The logo's colour when hovered (white by default; the window's white buttons need another).</summary>
        public Color HoverColor = Color.white;

        private float _scale = 1f, _glow;
        private Vector2 _home;
        private bool _homeSet;

        private void Update()
        {
            if (!Logo || !Halo) return;
            if (!_homeSet) { _home = Logo.anchoredPosition; _homeSet = true; }
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            float breath = 0.5f + 0.5f * Mathf.Sin(t * 2.2f);
            _scale = Mathf.Lerp(_scale, Hovered ? 1.18f : 1f, 1 - Mathf.Exp(-dt * 14f));
            _glow = Mathf.Lerp(_glow, Hovered ? 1f : 0f, 1 - Mathf.Exp(-dt * 10f));

            Logo.anchoredPosition = _home + new Vector2(0, Mathf.Sin(t * 1.6f) * Float * (1 - _glow * 0.6f));
            Logo.localScale = Vector3.one * _scale;
            if (LogoImage) LogoImage.color = Color.Lerp(new Color(0.9f, 0.9f, 1f, 0.92f), HoverColor, _glow);

            var c = Color.Lerp(Community.Purple, Color.white, 0.25f + 0.2f * _glow); // a light violet that shows on the game's violet skies
            c.a = Mathf.Lerp(0.55f + 0.35f * breath, 1f, _glow);
            Halo.color = c;
            Halo.rectTransform.localScale = Vector3.one * (0.9f + 0.18f * breath + 0.35f * _glow);
        }
    }
}
