using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace MpHideDebugLine.Patches
{
    /// <summary>
    /// Draws our IMGUI after the mod's, so it appears above the mod's UI.
    /// </summary>
    [HarmonyPatch(typeof(UIBullshit), "_GUI_DoTheFullGUI")]
    public static class NotificationCenterDrawPatch
    {
        private static void Postfix()
        {
            Plugin.Instance?.GetRenderer()?.DrawImmediateGUI();
        }
    }
}
