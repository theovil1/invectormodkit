using System;

namespace InvectorModKit
{
    /// <summary>
    /// Declares how a BepInEx plugin affects the game. Put it next to <c>[BepInPlugin]</c>.
    /// Plugins without it are treated as gameplay-affecting, which turns off Steam leaderboards,
    /// achievements and stats for the session (see <see cref="ModKit.IsModdedSession"/>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ModKitModAttribute : Attribute
    {
        /// <summary>
        /// True if the mod can change scores, judging, charts, songs or anything else a leaderboard or
        /// achievement depends on. Purely visual mods set this to false and keep the session ranked.
        /// </summary>
        public bool AffectsGameplay { get; set; } = true;
    }
}
