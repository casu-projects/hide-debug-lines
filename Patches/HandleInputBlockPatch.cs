using HarmonyLib;

namespace MpHideDebugLine.Patches
{
    /// <summary>
    /// Blocks player movement/use input while the Notification Center is open.
    /// </summary>
    [HarmonyPatch(typeof(PlayerCamera), "HandleInput")]
    public static class HandleInputBlockPatch
    {
        private static bool Prefix()
        {
            if (!Plugin.CenterOpen)
                return true;

            return false;
        }
    }
}
