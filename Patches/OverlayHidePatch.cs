using HarmonyLib;

namespace MpHideDebugLine.Patches
{
    /// <summary>
    /// Hides the mod's top-left debug overlay when HideOverlay is on.
    /// </summary>
    [HarmonyPatch(typeof(KrokoshaCasualtiesMP.KrokoshaScavMultiplayer), "_GUI_RenderMainGUI")]
    public static class OverlayHidePatch
    {
        private static bool Prefix()
        {
            return !Plugin.HideOverlay.Value;
        }
    }
}