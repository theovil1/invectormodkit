using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.Marketplace.Client.Discord
{
    /// <summary>Turns a snapshot of the game or the editor into Discord's activity object (SET_ACTIVITY's "activity").</summary>
    public static class ActivityBuilder
    {
        private const int TextMax = 128, LabelMax = 32;

        /// <summary>The activity; null for <see cref="PresenceScreen.Hidden"/> (clears it).</summary>
        public static JObject Build(PresenceSnapshot s, PresenceTexts t, DateTime sessionStartUtc, DateTime nowUtc)
        {
            if (s.Screen == PresenceScreen.Hidden) return null;
            var a = new JObject { ["type"] = 0 };
            var assets = new JObject { ["large_image"] = "logo", ["large_text"] = Cut(t.ModVersion, TextMax) };
            bool level = s.Screen == PresenceScreen.Playing || s.Screen == PresenceScreen.Paused;
            if (level)
            {
                a["details"] = Cut(string.IsNullOrEmpty(s.Artist) ? s.Title : s.Title + " · " + s.Artist, TextMax);
                a["state"] = Cut(s.Screen == PresenceScreen.Paused
                    ? t.Paused
                    : s.Difficulty + " · " + string.Format(CultureInfo.InvariantCulture, t.ScoreLine, s.Score, s.Combo), TextMax);
                if (s.Screen == PresenceScreen.Playing)
                    a["timestamps"] = new JObject { ["end"] = Ms(nowUtc) + (long)Math.Round(Math.Max(0, s.RemainingSeconds) * 1000) };
                if (s.CustomSong) assets["large_text"] = Cut(t.ModVersion + " · " + t.CustomSong, TextMax);
                AddDifficulty(assets, s.Difficulty);
            }
            else
            {
                a["details"] = Cut(ScreenText(s.Screen, t), TextMax);
                if (s.Screen == PresenceScreen.Editor && !string.IsNullOrEmpty(s.Title))
                {
                    string notes = string.Format(CultureInfo.InvariantCulture, Plural.IsOne(s.Notes, t.LanguageCode) ? t.NotesOne : t.NotesMany, s.Notes);
                    a["state"] = Cut(s.Title + " · " + s.Difficulty + " · " + notes, TextMax);
                    AddDifficulty(assets, s.Difficulty);
                }
                a["timestamps"] = new JObject { ["start"] = Ms(sessionStartUtc) };
            }
            a["assets"] = assets;
            var buttons = new JArray { Button(t.GetMod, t.ModUrl) };
            if (t.DiscordUrl != null && t.DiscordUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                buttons.Add(Button(t.JoinDiscord, t.DiscordUrl));
            a["buttons"] = buttons;
            return a;
        }

        private static string ScreenText(PresenceScreen screen, PresenceTexts t)
        {
            switch (screen)
            {
                case PresenceScreen.SongSelect: return t.SongSelect;
                case PresenceScreen.Marketplace: return t.Marketplace;
                case PresenceScreen.Editor: return t.Editor;
                case PresenceScreen.Results: return t.Results;
                default: return t.Menu;
            }
        }

        private static void AddDifficulty(JObject assets, string difficulty)
        {
            if (string.IsNullOrEmpty(difficulty)) return;
            assets["small_image"] = difficulty.ToLowerInvariant();
            assets["small_text"] = difficulty;
        }

        private static JObject Button(string label, string url) => new JObject { ["label"] = Cut(label, LabelMax), ["url"] = url };

        /// <summary>At most <paramref name="max"/> chars, never cutting an emoji (surrogate pair) in half.</summary>
        private static string Cut(string text, int max)
        {
            if (text == null || text.Length <= max) return text;
            int n = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
            return text.Substring(0, n);
        }

        private static long Ms(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    }
}
