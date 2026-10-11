using System;
using System.IO;
using System.Linq;
using HelloThere.Modules.AudioModules;
using UnityEngine;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>The InvectorModKit Discord: its link (a setting), its logo, the glow behind it.</summary>
    internal static class Community
    {
        /// <summary>The invite link; empty hides every Discord button.</summary>
        public static string DiscordUrl = "";

        public static bool HasDiscord => DiscordUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        /// <summary>Discord's purple, a touch closer to the game's accent.</summary>
        public static readonly Color Purple = new Color32(0x7B, 0x5C, 0xFF, 0xFF);

        private static Sprite _logo, _glow;

        /// <summary>The Discord logo, white (Simple Icons, CC0), embedded in this plugin.</summary>
        public static Sprite Logo
        {
            get
            {
                if (_logo) return _logo;
                using (var stream = typeof(Community).Assembly.GetManifestResourceStream("discord.png"))
                {
                    if (stream == null) return null;
                    var bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "InvectorModKit Discord", wrapMode = TextureWrapMode.Clamp };
                    texture.LoadImage(bytes);
                    texture.hideFlags = HideFlags.HideAndDontSave;
                    _logo = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    _logo.hideFlags = HideFlags.HideAndDontSave;
                }
                return _logo;
            }
        }

        /// <summary>The game's own soft glow sprite, or a drawn one before the game has loaded it.</summary>
        public static Sprite Glow
        {
            get
            {
                if (_glow) return _glow;
                _glow = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == "Glow");
                if (_glow) return _glow;
                const int size = 128;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                        texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d)));
                    }
                texture.Apply();
                var drawn = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                drawn.hideFlags = HideFlags.HideAndDontSave;
                return drawn; // not kept: the game's own glow is looked for again next time
            }
        }

        public static void OpenDiscord()
        {
            if (!HasDiscord) return;
            AudioModule.AudioProvider.Play(AudioShortcut.SFX_MENU_CONFIRM);
            Application.OpenURL(DiscordUrl);
        }
    }
}
