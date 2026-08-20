using System;
using System.Collections.Generic;

namespace MpHideDebugLine
{
    /// <summary>
    /// Message history store for the Notification Center.
    /// </summary>
    public static class NotificationLog
    {
        public sealed class Entry
        {
            public string msg;
            public bool isError;
            public DateTime time;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        public static IReadOnlyList<Entry> Entries => _entries;

        public static void Append(string msg, bool isError)
        {
            if (string.IsNullOrEmpty(msg))
                return;

            _entries.Add(new Entry
            {
                msg = msg,
                isError = isError,
                time = DateTime.Now
            });

            int max = 200;
            try { max = Math.Max(10, Plugin.MaxHistory.Value); }
            catch { } // cfg not loaded yet; use default

            while (_entries.Count > max)
                _entries.RemoveAt(0);
        }

        // Clears the accumulated entries.
        public static void Clear()
        {
            _entries.Clear();
        }
    }
}
