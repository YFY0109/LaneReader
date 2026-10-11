using HarmonyLib;
using UnityEngine;

namespace LaneReader
{
    /// <summary>
    /// Hooks the game's own per-frame entry points.
    ///
    /// AsyncInputManager is a MonoBehaviour singleton the game keeps alive for the whole
    /// session, and it already implements both <c>Update</c> and <c>OnGUI</c>. Patching those
    /// two gives a per-frame tick and a full-screen IMGUI context without:
    ///
    ///   * UnityModManager's OnGUI callback, which only runs while its settings window is open;
    ///   * creating our own GameObject, which would need a DontDestroyOnLoad dance and would
    ///     still not get a GUI context the game composites.
    ///
    /// Everything here is a postfix that does nothing but delegate, so a failure inside the
    /// overlay cannot disturb the game's own input handling.
    ///
    /// Each postfix also stamps the frame and time it ran at. That is not decoration:
    /// <see cref="OverlayWatchdog"/> drives the overlay itself when these stop firing, and the
    /// stamps are how it knows. A player reported "the settings panel opens but nothing is ever
    /// drawn", and their log contained no overlay line at all - which is exactly what a hook that
    /// never fires looks like from the outside. The stamps cost nothing and make the fallback
    /// automatic, rather than a bet on someone else's GameObject staying active.
    /// </summary>
    [HarmonyPatch(typeof(AsyncInputManager), "Update")]
    internal static class AsyncInputUpdatePatch
    {
        private static bool _logged;

        [HarmonyPostfix]
        private static void Postfix()
        {
            Plugin.HookTickFrame = Time.frameCount;
            Plugin.HookTickTime = Time.unscaledTime;
            if (!_logged)
            {
                _logged = true;
                Plugin.Log("driver: game hooks alive (AsyncInputManager.Update)");
            }
            Plugin.Tick();
        }
    }

    [HarmonyPatch(typeof(AsyncInputManager), "OnGUI")]
    internal static class AsyncInputOnGuiPatch
    {
        private static bool _logged;

        [HarmonyPostfix]
        private static void Postfix()
        {
            Plugin.HookDrawFrame = Time.frameCount;
            Plugin.HookDrawTime = Time.unscaledTime;
            if (!_logged)
            {
                _logged = true;
                Plugin.Log("driver: game hooks alive (AsyncInputManager.OnGUI)");
            }
            Plugin.Draw();
        }
    }
}
