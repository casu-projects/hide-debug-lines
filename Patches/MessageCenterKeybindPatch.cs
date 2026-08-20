using HarmonyLib;
using UnityEngine;

namespace MpHideDebugLine.Patches
{
    /// <summary>
    /// Status Messages Bar keybind and its integration with the game settings (controls tab).
    /// </summary>
    public static class MpHideDebugLineKeybind
    {
        public const string Name = "mphidedebugline_toggle";

        // Returns the settings-bound key, or KeyCode.None if unbound/not loaded.
        public static KeyCode GetBoundKey()
        {
            try
            {
                return KeyBinds.GetBind(Name);
            }
            catch
            {
                return KeyCode.None;
            }
        }
    }

    /// <summary>
    /// Injects the keybind's row label into the game settings (controls tab).
    /// </summary>
    [HarmonyPatch(typeof(Locale), "LoadLanguage")]
    public static class MpHideDebugLineLocalePatch
    {
        private static void Postfix()
        {
            try
            {
                if (Locale.currentLang != null && !Locale.currentLang.other.ContainsKey("gameset" + MpHideDebugLineKeybind.Name))
                {
                    Locale.currentLang.other["gameset" + MpHideDebugLineKeybind.Name] = "Open Status Messages Bar";
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
