namespace InvectorModKit.Marketplace.Client.Discord
{
    /// <summary>The activity's words and the buttons' links.</summary>
    public sealed class PresenceTexts
    {
        public const string ModPage = "https://github.com/theovil1/invectormodkit/releases/latest";

        public string Menu, SongSelect, Marketplace, Editor, Paused, Results;
        /// <summary>"{0} pts · combo {1}": score, combo.</summary>
        public string ScoreLine;
        /// <summary>"{0} note" / "{0} notes", chosen with <see cref="Plural.IsOne"/>.</summary>
        public string NotesOne, NotesMany;
        public string CustomSong, GetMod, JoinDiscord;
        /// <summary>"InvectorModKit 0.4.0": the large image's hover text.</summary>
        public string ModVersion;
        /// <summary>The language's code ("fr"), for plurals.</summary>
        public string LanguageCode;
        public string ModUrl, DiscordUrl;

        /// <summary>
        /// The texts the game and the level editor show: English for every player, whatever their language, so
        /// friends all read the same card (Theo's choice; an exception to the mod's 11 languages).
        /// </summary>
        public static PresenceTexts English(string modVersion, string discordUrl) => new PresenceTexts
        {
            Menu = "In the main menu",
            SongSelect = "Choosing a song",
            Marketplace = "Browsing the marketplace",
            Editor = "Making a level",
            Paused = "Paused",
            Results = "Looking at the results",
            ScoreLine = "{0} pts · combo {1}",
            NotesOne = "{0} note",
            NotesMany = "{0} notes",
            CustomSong = "Custom song",
            GetMod = "Get InvectorModKit",
            JoinDiscord = "Join the Discord",
            ModVersion = modVersion,
            LanguageCode = "en",
            ModUrl = ModPage,
            DiscordUrl = discordUrl,
        };
    }
}
