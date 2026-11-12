using System;
using System.IO;
using HelloThere.Modules.AudioModules;
using UnityEngine;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>The InvectorModKit Discord: its link (a setting) and its logo.</summary>
    internal static class Community
    {
        /// <summary>The invite link; empty hides every Discord button.</summary>
        public static string DiscordUrl = "";

        public static bool HasDiscord => DiscordUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        /// <summary>Discord's purple, a touch closer to the game's accent.</summary>
        public static readonly Color Purple = new Color32(0x7B, 0x5C, 0xFF, 0xFF);

        private static Sprite _logo;

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

        public static void OpenDiscord()
        {
            if (!HasDiscord) return;
            AudioModule.AudioProvider.Play(AudioShortcut.SFX_MENU_CONFIRM);
            Application.OpenURL(DiscordUrl);
        }
    }
}
