using System.Collections.Generic;

namespace WhiskeyRealism.Util
{
    internal static class OnceLog
    {
        private static readonly HashSet<string> _fired = new HashSet<string>();

        internal static void Info(string key, string message)
        {
            if (_fired.Contains(key)) return;
            _fired.Add(key);
            Plugin.Log.LogInfo("[once:" + key + "] " + message);
        }

        internal static void Reset()
        {
            _fired.Clear();
        }
    }
}
