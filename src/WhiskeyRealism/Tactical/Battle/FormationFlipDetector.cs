using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// BC16 decision 2, step 0 (record only): spots a group whose <c>groupformation</c> changes twice within <see cref="WindowHours"/> battle-hours,
    /// the signature of vanilla's own formation pass (AdjustGroupFormations 5875-5935) and the arbiter each ordering their own value. Fed with the
    /// group's formation after each group order; keeps one small record per group, no allocation once a group has been seen.
    /// </summary>
    public sealed class FormationFlipDetector
    {
        /// <summary>Two changes this close (battle-hours, inclusive) are a flip.</summary>
        public const double WindowHours = 0.1;

        /// <summary>Rows a battle may write. The running <see cref="TotalFlips"/> carries on past it, so the count per battle-hour survives a full budget.</summary>
        public const int MaxRows = 256;

        // Float-free hour arithmetic: 1.1 - 1.0 is 0.10000000000000009, which must still be "0.1 hours later".
        private const double Slack = 1e-9;

        private struct Seen
        {
            public int Formation;
            public double LastChangeHours; // NaN: no counted change yet
        }

        private readonly Dictionary<int, Seen> _groups = new Dictionary<int, Seen>();

        public int TotalFlips { get; private set; }

        public int RowsTaken { get; private set; }

        /// <summary>
        /// Notes the formation a group stands in after an order. The first sighting is a baseline. A change is a flip when the group's previous
        /// counted change was within the window. A change that is not <paramref name="countable"/> (an immediate placement: deployment, a re-lay
        /// in place) is not an order war; it moves the baseline and forgets the group's last counted change.
        /// </summary>
        public bool Observe(int groupId, int formation, double nowHours, bool countable, out int from, out double sinceHours)
        {
            from = formation;
            sinceHours = 0.0;
            if (!_groups.TryGetValue(groupId, out Seen seen))
            {
                _groups[groupId] = new Seen { Formation = formation, LastChangeHours = double.NaN };
                return false;
            }
            if (seen.Formation == formation) return false;

            from = seen.Formation;
            bool flip = false;
            if (countable)
            {
                double since = nowHours - seen.LastChangeHours;
                if (!double.IsNaN(seen.LastChangeHours) && since >= 0.0 && since <= WindowHours + Slack)
                {
                    flip = true;
                    sinceHours = since;
                    TotalFlips++;
                }
            }
            _groups[groupId] = new Seen { Formation = formation, LastChangeHours = countable ? nowHours : double.NaN };
            return flip;
        }

        /// <summary>Takes one row from the battle's budget; false when it is spent.</summary>
        public bool TryTakeRow()
        {
            if (RowsTaken >= MaxRows) return false;
            RowsTaken++;
            return true;
        }

        public void Clear()
        {
            _groups.Clear();
            TotalFlips = 0;
            RowsTaken = 0;
        }
    }
}
