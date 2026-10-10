using HarmonyLib;
using HelloThere.Applications;
using HelloThere.Avicii;

namespace InvectorModKit.Internal
{
    /// <summary>Feeds <see cref="GameEvents"/> from the game's state machine.</summary>
    internal static class LifecyclePatches
    {
        public static ApplicationState Current { get; private set; }

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ApplicationStateMachine), "ChangeState"),
                prefix: new HarmonyMethod(typeof(LifecyclePatches), nameof(ChangeStatePrefix)));
        }

        private static void ChangeStatePrefix(ApplicationState nextState)
        {
            var from = Current;
            Current = nextState;
            ModKit.Log.LogDebug($"State {from?.GetType().Name ?? "-"} -> {nextState?.GetType().Name ?? "-"}");
            GameEvents.RaiseStateChanged(from, nextState);
            if (nextState is StateGame game) GameEvents.RaiseSongStarting(game.gameSession);
            else if (nextState is StateGameResult result) GameEvents.RaiseSongEnded(result.gameSession);
        }
    }
}
