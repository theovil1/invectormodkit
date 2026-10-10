using HarmonyLib;

namespace InvectorModKit.Internal
{
    /// <summary>
    /// The game drops its render scale (down to 75%) whenever three frames in a row run late, then climbs
    /// back slowly: the picture goes blurry after any hitch. Optionally keep it at 100%. Re-applied every
    /// frame because the pause menu resets the game's own override.
    /// </summary>
    internal static class GraphicsTweaks
    {
        public static bool KeepFullResolution = true;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(DynamicRenderScale), "Update"),
                prefix: new HarmonyMethod(typeof(GraphicsTweaks), nameof(BeforeRenderScaleUpdate)));
        }

        private static void BeforeRenderScaleUpdate()
        {
            if (!KeepFullResolution) return;
            DynamicRenderScale.forceMax = true;
            DynamicRenderScale.RenderScale = 1f;
        }
    }
}
