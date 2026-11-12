namespace InvectorModKit.Marketplace.Client
{
    /// <summary>What the Moderation tab lists.</summary>
    public enum ModerationShown { Reported, Hidden, All, Banned }

    /// <summary>The Moderation tab's main buttons, and what a refused admin call means for the player.</summary>
    public static class ModerationState
    {
        /// <summary>A chart's sheet: hide it, or show a hidden one again (which also clears its reports).</summary>
        public static SheetButton ForChart(bool hidden, bool busy) =>
            new SheetButton { LabelKey = hidden ? "mod_show" : "mod_hide", Accent = true, Enabled = !busy };

        /// <summary>A banned player's sheet.</summary>
        public static SheetButton ForPlayer(bool busy) =>
            new SheetButton { LabelKey = "mod_lift_ban", Accent = true, Enabled = !busy };

        /// <summary>Whether to offer banning that player: never the admin themself (the server refuses it).</summary>
        public static bool CanBan(string steamId, string mySteamId) => !string.IsNullOrEmpty(steamId) && steamId != mySteamId;

        /// <summary>
        /// The Loc key for a refused admin call: 403, the account is not an admin any more; 401 or 404, a server from
        /// before moderation in the game (it wants its web page's cookie). Null: say the server's own error.
        /// </summary>
        public static string Refused(int status) =>
            status == 403 ? "mod_not_admin" : status == 401 || status == 404 ? "server_too_old" : null;
    }
}
