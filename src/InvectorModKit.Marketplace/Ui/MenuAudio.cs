using System;
using HelloThere.Avicii;
using HelloThere.Modules.MenuModules;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// The menu's music goes quiet while the marketplace is open, and comes back as it was: the main menu's music
    /// pauses and resumes where it was, song select's preview fades out and the selected song's comes back.
    /// </summary>
    internal static class MenuAudio
    {
        private static bool _pausedMenu, _fadedPreview;

        public static void Silence()
        {
            var state = ModKit.CurrentState;
            try
            {
                if (state is StateMainMenu)
                {
                    StateMainMenu.PauseMusic();
                    _pausedMenu = true;
                }
                else if (state is StateSelectSongSingleplayer || state is StateSelectSongMultiplayer)
                {
                    _fadedPreview = StateMenuModuleBase.menuSceneRoot != null && StateMenuModuleBase.menuSceneRoot.TransitionOutPreviewMusic(0.5f);
                }
            }
            catch (Exception e) { MarketplacePlugin.Log.LogWarning("Menu music: " + e.Message); }
        }

        /// <summary>Undoes only what <see cref="Silence"/> did.</summary>
        public static void Restore()
        {
            try
            {
                if (_pausedMenu && ModKit.CurrentState is StateMainMenu) StateMainMenu.UnpauseMusic();
                if (_fadedPreview && StateMenuModuleBase.menuSceneRoot != null) StateMenuModuleBase.menuSceneRoot.PlayLastPreviewMusic(0.5f);
            }
            catch (Exception e) { MarketplacePlugin.Log.LogWarning("Menu music: " + e.Message); }
            finally { _pausedMenu = _fadedPreview = false; }
        }
    }
}
