using HarmonyLib;

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
    /// </summary>
    [HarmonyPatch(typeof(AsyncInputManager), "Update")]
    internal static class AsyncInputUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            Plugin.Tick();
        }
    }

    [HarmonyPatch(typeof(AsyncInputManager), "OnGUI")]
    internal static class AsyncInputOnGuiPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            Plugin.Draw();
        }
    }
}
