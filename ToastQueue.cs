using System;
using KrokoshaCasualtiesMP;
using KrokoshaCasualtiesUtils;

namespace MpHideDebugLine
{
    /// <summary>
    /// Routes "Last Status Message" messages to the renderer and records them in history.
    /// </summary>
    public class ToastQueue
    {
        // Forwards a status message to the renderer as a fresh toast.
        public void Show(string msg, bool isError)
        {
            if (string.IsNullOrEmpty(msg))
                return;

            // Strip Unity rich-text tags so they don't show as literal text in toasts/history.
            msg = StripRichText(msg);

            // Record every message in history so past messages can be reviewed later.
            NotificationLog.Append(msg, isError);

            try
            {
                // "Hide Message Toasts" = true: hide toasts, keep list accumulating.
                if (MpSettings.HIDE_MESSAGE_TOASTS)
                    return;

                if (Con.IsConsoleOpen())
                    return;

                bool inWorld = Util.IsInWorld();
                if (inWorld && !Plugin.ShowInGame.Value)
                    return;
                if (!inWorld && !Plugin.ShowInMenus.Value)
                    return;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"ToastQueue gate check failed: {ex}");
            }

            ToastRenderer renderer = Plugin.Instance?.GetRenderer();
            if (renderer == null)
                return;

            renderer.Show(msg, isError);
        }

        // Removes Unity rich-text tags (e.g. <color=...>, <b>) via the mod's sanitizer.
        private static string StripRichText(string msg)
        {
            try
            {
                return KrokoshaScavMultiplayer.SanitizeRichText(msg);
            }
            catch
            {
                return msg; // keep the original if sanitizing fails
            }
        }
    }
}