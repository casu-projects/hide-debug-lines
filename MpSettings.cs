using KrokoshaCasualtiesMP;

namespace MpHideDebugLine
{
    /// <summary>
    /// Settings edited in the mod's Multiplayer Mod Menu > Settings > User Interface tab.
    /// </summary>
    public static class MpSettings
    {
        // If true, hide toasts and only accumulate in the Center list. Default false.
        [SettingDeclarerThingyBool("setting_ui_hidemessagetoasts")]
        public static bool HIDE_MESSAGE_TOASTS = false;
    }
}
