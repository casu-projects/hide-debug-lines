using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace MpHideDebugLine.Patches
{
    /// <summary>
    /// Merges MpSettings into the mod's User Interface settings tab fields.
    /// </summary>
    [HarmonyPatch(typeof(UIMainMenu), nameof(UIMainMenu.GetSettingsFromStaticClass))]
    public static class SettingsDiscoveryPatch
    {
        private static void Postfix(Type type, ref List<FieldInfo> __result)
        {
            try
            {
                if (type != typeof(UIBullshit))
                    return;

                // Cache our setting fields via the mod's infra (idempotent).
                List<FieldInfo> ours = UIMainMenu.GetSettingsFromStaticClass(typeof(MpSettings));
                if (ours == null || ours.Count == 0)
                    return;

                // Merge into a new list so the mod's cache is untouched (no duplicates).
                __result = __result.Concat(ours).ToList();
            }
            catch
            {
            }
        }
    }
}
