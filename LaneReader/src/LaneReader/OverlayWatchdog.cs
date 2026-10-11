using UnityEngine;

namespace LaneReader
{
    /// <summary>
    /// Drives the overlay from a GameObject the mod owns, so the overlay cannot go missing because
    /// of somebody else's object.
    ///
    /// The overlay normally runs off a postfix on the game's AsyncInputManager.Update and .OnGUI
    /// (see <see cref="AsyncInputUpdatePatch"/>). That works here, but it rests on an assumption
    /// about an object the mod does not own: a MonoBehaviour only receives Update and OnGUI while
    /// its GameObject is active, so any build, setting or other mod that keeps that object
    /// inactive leaves this mod with a working settings panel and no overlay whatsoever - and,
    /// because every path below the hook returns early, not one line in the log.
    ///
    /// This component is ours, it is created once and marked DontDestroyOnLoad, so Unity always
    /// calls it. It stays passive while the game hooks are stamping frames - on a normal install
    /// it never does anything at all - and takes over once they have been quiet for
    /// <see cref="HookTimeout"/> seconds.
    ///
    /// Two details make that takeover safe:
    ///
    ///   * the tick runs in LateUpdate, i.e. after every Update in the frame, so the game's own
    ///     Update has always had its chance to stamp first. Calling Tick twice in one frame would
    ///     advance the note clock twice and skip the notes forward, and the frame stamp makes that
    ///     impossible rather than unlikely.
    ///   * drawing sets GUI.depth below the game's, so the overlay composites on top of the
    ///     game's own IMGUI instead of behind it.
    /// </summary>
    internal sealed class OverlayWatchdog : MonoBehaviour
    {
        /// <summary>How long the hooks may be silent before this takes over.</summary>
        private const float HookTimeout = 0.5f;

        /// <summary>Draw above the game's own IMGUI, which uses the default depth.</summary>
        private const int OverlayDepth = -1000;

        private static OverlayWatchdog _instance;
        private static GameObject _host;

        internal static void Install()
        {
            if (_instance != null)
                return;

            _host = new GameObject("LaneReader_OverlayDriver");
            Object.DontDestroyOnLoad(_host);
            _instance = _host.AddComponent<OverlayWatchdog>();
        }

        internal static void Remove()
        {
            if (_host != null)
                Object.Destroy(_host);
            _host = null;
            _instance = null;
        }

        private void LateUpdate()
        {
            if (Plugin.HookTickFrame == Time.frameCount)
                return;
            if (Time.unscaledTime - Plugin.HookTickTime < HookTimeout)
                return;

            LogTakeover();
            Plugin.Tick();
        }

        private void OnGUI()
        {
            if (Plugin.HookDrawFrame == Time.frameCount)
                return;
            if (Time.unscaledTime - Plugin.HookDrawTime < HookTimeout)
                return;

            LogTakeover();
            int saved = GUI.depth;
            GUI.depth = OverlayDepth;
            try
            {
                Plugin.Draw();
            }
            finally
            {
                GUI.depth = saved;
            }
        }

        /// <summary>
        /// One line saying which driver is drawing. Written on the first takeover only, because
        /// this is the answer to "the panel opens but nothing is drawn".
        /// </summary>
        private static void LogTakeover()
        {
            if (_takeoverLogged)
                return;
            _takeoverLogged = true;

            if (Plugin.HookTickTime < 0f && Plugin.HookDrawTime < 0f)
                Plugin.Log("driver: game hooks never fired - drawing from the mod's own GameObject");
            else
                Plugin.Log("driver: game hooks stopped firing - drawing from the mod's own GameObject");
        }

        private static bool _takeoverLogged;
    }
}
