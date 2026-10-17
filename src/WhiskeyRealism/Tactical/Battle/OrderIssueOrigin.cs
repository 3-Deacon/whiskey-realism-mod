using System;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// Which producer's intent the executor is carrying out right now (pure; one flag and one source, battle-scoped). The executor sets it around the
    /// vanilla calls of one winner (<c>OrderExecutorRuntime.Execute</c>); the tactical observer's <c>AddToOrderQueue</c> row reads it, so an order
    /// the executor queued shows <c>origin=ReserveRelease</c> (the intent's <see cref="IntentSource"/>) and not a bare "ai-chain" that looks like
    /// vanilla's own. Outside an executor write it reads null (a vanilla routine, the player's own order). The battle is single-threaded: the scope
    /// is not nested across winners, and a nested enter restores the outer source on exit.
    /// </summary>
    internal static class OrderIssueOrigin
    {
        private static readonly string[] Names = Enum.GetNames(typeof(IntentSource));
        private static IntentSource _source;
        private static int _depth;

        static OrderIssueOrigin()
        {
            BattleScope.Register("order-issue-origin", () => _depth = 0);
        }

        /// <summary>The source of the intent being carried out, or null when no executor write is in progress.</summary>
        public static string Current => _depth > 0 ? NameOf(_source) : null;

        /// <summary>Starts an executor write for <paramref name="source"/>; returns the source to restore on <see cref="Exit"/>.</summary>
        public static IntentSource Enter(IntentSource source)
        {
            IntentSource outer = _source;
            _source = source;
            _depth++;
            return outer;
        }

        public static void Exit(IntentSource restore)
        {
            if (_depth > 0) _depth--;
            _source = restore;
        }

        public static string NameOf(IntentSource source)
        {
            int i = (int)source;
            return i >= 0 && i < Names.Length ? Names[i] : "Unknown";
        }
    }
}
