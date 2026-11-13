namespace InvectorModKit.Marketplace.Client.Discord
{
    /// <summary>Where the player is. <see cref="Hidden"/> clears the activity (the editor shows its own).</summary>
    public enum PresenceScreen { Menu, SongSelect, Marketplace, Editor, Playing, Paused, Results, Hidden }

    /// <summary>What the game or the editor is doing right now, as the Discord activity shows it.</summary>
    public sealed class PresenceSnapshot
    {
        public PresenceScreen Screen;
        public string Title, Artist;
        public bool CustomSong;
        /// <summary>"Casual", "Normal", "Hard" or "Ultra".</summary>
        public string Difficulty;
        public int Score, Combo;
        public double RemainingSeconds;
        /// <summary>Editor: the note count of the difficulty being edited.</summary>
        public int Notes;

        public PresenceSnapshot Copy() => (PresenceSnapshot)MemberwiseClone();
    }
}
