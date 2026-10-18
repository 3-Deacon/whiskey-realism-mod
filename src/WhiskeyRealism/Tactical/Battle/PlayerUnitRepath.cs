using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// The vanilla routine that is on the stack while a player-owned unit is re-pathed or changes movement mode (BUG-WL-024). The observer
    /// patches (<c>PlayerUnitRepathObserverPatch</c>) cannot read a stack per call, so each routine under suspicion sets a marker around itself
    /// and the <c>RegimentSetPath</c> / <c>SetMovementMode</c> Postfixes read it.
    /// </summary>
    public enum RepathRoutine : byte
    {
        /// <summary>No marker: another vanilla caller (the brigade-level AI in AIBattle, a stop, the link of a group unit) or one this build does not name.</summary>
        Unattributed = 0,
        /// <summary>The player's own click or order: <c>BattleUnits.SetWaypoint</c> with showmovementoptions true (decompile 166134, 166165).</summary>
        PlayerOrder = 1,
        /// <summary><c>Regiment.CheckFallbackDueToEnemyClose</c> (128924): a skirmisher falls back from an enemy within 25 m.</summary>
        Fallback = 2,
        /// <summary><c>Regiment.CheckNewTargetsOfSkirmishers</c> (128847): auto-target and relink to the parent.</summary>
        SkirmisherTarget = 3,
        /// <summary><c>Regiment.UpdateUnitLink</c> (124394): follow the link position of the parent.</summary>
        UnitLink = 4,
        /// <summary><c>Regiment.CheckForReattachmentOrder</c> (129520): walk back to the parent regiment.</summary>
        Reattach = 5,
        /// <summary><c>Regiment.CheckEnemyContact</c> (117140): the path interruption on contact.</summary>
        ContactInterruption = 6,
    }

    /// <summary>What a row records: a path set, a movement-mode change, or what a whole routine changed.</summary>
    public enum RepathKind : byte
    {
        Path = 0,
        Mode = 1,
        Routine = 2,
    }

    /// <summary>The marker a routine replaced, handed back to <see cref="RepathRoutineMarker.Leave"/>.</summary>
    public readonly struct RepathMarker
    {
        public readonly RepathRoutine Routine;
        public readonly int UnitId;

        public RepathMarker(RepathRoutine routine, int unitId)
        {
            Routine = routine;
            UnitId = unitId;
        }
    }

    /// <summary>
    /// One flag and one unit, battle-scoped (the battle is single-threaded). A routine enters before vanilla's body and leaves in a Finalizer, so a
    /// throw leaves nothing behind; a nested enter restores the outer marker on leave. Allocation-free.
    /// </summary>
    internal static class RepathRoutineMarker
    {
        /// <summary>The marker applies to every unit (a click orders a whole group, whose descendants are re-pathed beneath it).</summary>
        public const int AnyUnit = 0;

        private static readonly string[] Names =
        {
            "unattributed", "player-order", "fallback", "skirmisher-target", "unit-link", "reattach", "contact-interruption",
        };

        private static RepathRoutine _routine;
        private static int _unitId;

        static RepathRoutineMarker()
        {
            BattleScope.Register("player-unit-repath-marker", Reset);
        }

        public static RepathMarker Enter(RepathRoutine routine, int unitId)
        {
            var previous = new RepathMarker(_routine, _unitId);
            _routine = routine;
            _unitId = unitId;
            return previous;
        }

        public static void Leave(RepathMarker previous)
        {
            _routine = previous.Routine;
            _unitId = previous.UnitId;
        }

        /// <summary>The routine on the stack for this unit, or <see cref="RepathRoutine.Unattributed"/>.</summary>
        public static RepathRoutine For(int unitId)
        {
            if (_routine == RepathRoutine.Unattributed) return RepathRoutine.Unattributed;
            return _unitId == AnyUnit || _unitId == unitId ? _routine : RepathRoutine.Unattributed;
        }

        /// <summary>True while the player's own order is being issued (any unit).</summary>
        public static bool PlayerOrderActive => _routine == RepathRoutine.PlayerOrder;

        public static string Name(RepathRoutine routine)
        {
            int i = (int)routine;
            return i >= 0 && i < Names.Length ? Names[i] : "unknown";
        }

        public static void Reset()
        {
            _routine = RepathRoutine.Unattributed;
            _unitId = AnyUnit;
        }
    }

    /// <summary>
    /// Bounds the rows the observer writes. One row per (unit, routine, kind) at most every <see cref="KeyIntervalSeconds"/> real seconds, the
    /// suppressed repeats counted on the next row; <see cref="WindowRows"/> rows per <see cref="WindowSeconds"/> over all keys, the rows lost to it
    /// counted on the next row; <see cref="MaxKeys"/> keys per battle. No allocation once a key has been seen. Real time is for this telemetry
    /// throttle only (AGENTS.md, Clocks).
    /// </summary>
    internal sealed class RepathRowBudget
    {
        public const float KeyIntervalSeconds = 3f;
        public const int MaxKeys = 2048;
        public const int WindowRows = 24;
        public const float WindowSeconds = 10f;

        private struct Entry
        {
            public float LastAt;
            public int Suppressed;
        }

        private readonly Dictionary<long, Entry> _keys = new Dictionary<long, Entry>(256);
        private float _windowStart = float.NegativeInfinity;
        private int _windowRows;
        private int _dropped;

        /// <summary>Rows lost to the window or the key cap since the last row was taken.</summary>
        public int Dropped => _dropped;

        public int KeyCount => _keys.Count;

        public bool TryTake(int unitId, RepathRoutine routine, RepathKind kind, float now, out int suppressed, out int dropped)
        {
            suppressed = 0;
            dropped = 0;
            long key = ((long)unitId << 8) | ((long)routine << 4) | (long)kind;
            bool known = _keys.TryGetValue(key, out Entry entry);
            if (known && now >= entry.LastAt && now - entry.LastAt < KeyIntervalSeconds)
            {
                entry.Suppressed++;
                _keys[key] = entry;
                return false;
            }
            if (!known && _keys.Count >= MaxKeys)
            {
                _dropped++;
                return false;
            }
            if (now < _windowStart || now - _windowStart >= WindowSeconds)
            {
                _windowStart = now;
                _windowRows = 0;
            }
            if (_windowRows >= WindowRows)
            {
                _dropped++;
                return false;
            }

            _windowRows++;
            suppressed = known ? entry.Suppressed : 0;
            dropped = _dropped;
            _dropped = 0;
            _keys[key] = new Entry { LastAt = now, Suppressed = 0 };
            return true;
        }

        public void Reset()
        {
            _keys.Clear();
            _windowStart = float.NegativeInfinity;
            _windowRows = 0;
            _dropped = 0;
        }
    }
}
