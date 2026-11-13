namespace InvectorModKit.Marketplace.Client.Discord
{
    /// <summary>The activity's words, already in the player's language, and the buttons' links.</summary>
    public sealed class PresenceTexts
    {
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
    }
}
