using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace MpHideDebugLine.Patches
{
    /// <summary>
    /// Hooks status messages and forwards them as toasts.
    /// </summary>
    [HarmonyPatch(typeof(KrokoshaScavMultiplayer), nameof(KrokoshaScavMultiplayer.DoMultiplayerStatusMessageLog))]
    public static class StatusLogPatch
    {
        private static void Prefix(string msg)
        {
            Plugin.Instance?.EnqueueStatusMessage(msg, false);
        }
    }

    [HarmonyPatch(typeof(KrokoshaScavMultiplayer), nameof(KrokoshaScavMultiplayer.DoMultiplayerStatusMessageError))]
    public static class StatusErrorPatch
    {
        private static void Prefix(string msg)
        {
            Plugin.Instance?.EnqueueStatusMessage(msg, true);
        }
    }
}