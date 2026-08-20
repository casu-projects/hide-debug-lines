using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace MpHideDebugLine.Patches
{
    /// <summary>
    /// Prevents the Mod Menu open/close state from changing while the Center is open.
    /// </summary>
    [HarmonyPatch(typeof(UIMainMenu), "SetOpen")]
    public static class ModMenuSetOpenBlockPatch
    {
        private static bool Prefix()
        {
            return !Plugin.CenterOpen;
        }
    }
}
