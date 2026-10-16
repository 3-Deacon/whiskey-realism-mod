using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Battle.BugFixes
{
    // BC13 (spec § 7, requirement-fix-stance-cooldown): the pure side of VanillaStanceCooldownFixPatch (#119, BUG-TAC-027).
    //
    // Vanilla AdjustGroupAIStance (4264) re-decides a group when lastaistancechangetime < now + updatetimeforresettingofaistance (T,
    // 1 game-hour), a test that is true for every past stamp, so a group's stance can change every side-stat cycle (0.015 h)
    // although battleprefs says to wait T after a change. The comparison is not changed (Whiskey's own stance hold depends on its form,
    // TacticalStanceOwnership.VanillaHoldStamp). Instead, after vanilla itself changes a group's stance in a call the group is
    // re-stamped to now + 2 x T, so vanilla skips it until now + T (the inverted test skips while stamp >= now' + T).
    //
    // Army retreat (macroai 3): nothing is re-stamped, and on the first call with macroai 3 the stamps this fix wrote are pulled back
    // (to ReleaseStamp) before vanilla runs, so vanilla's retreat block (4258) acts in that call. A group whose stamp is no longer the
    // value the fix wrote has been stamped by Whiskey's writer since; it is dropped from the set without a release.
    public static class StanceCooldownRestamp
    {
        /// <summary>The stamp that makes vanilla skip the group until rawNow + T: now + 2 x T.</summary>
        public static float Restamp(float rawNowHours, float resetHours)
        {
            float t = resetHours > 0f ? resetHours : 0f;
            return rawNowHours + 2f * t;
        }

        /// <summary>
        /// Vanilla changed the group's stance in this call: its ordered stance differs from the one the Prefix recorded, and vanilla
        /// stamped it with the raw clock (4275). Whiskey's writes are made outside the call, so they never match.
        /// </summary>
        public static bool VanillaChanged(int orderedBefore, int orderedAfter, float stamp, float rawNowHours)
        {
            return orderedAfter != orderedBefore && stamp == rawNowHours;
        }

        /// <summary>Vanilla's own skip: it re-decides only when stamp &lt; now + T.</summary>
        public static bool VanillaSkips(float stamp, float rawNowHours, float resetHours)
        {
            return !(stamp < rawNowHours + resetHours);
        }
    }

    /// <summary>Reads and writes lastaistancechangetime by group id (the runtime patch adapts the pool; the harness fakes it).</summary>
    public interface IStanceStampAccess
    {
        float Get(int groupId);
        void Set(int groupId, float stamp);
    }

    /// <summary>
    /// One AI instance's memory for the fix: the stances recorded before a call, the stamps the fix wrote, the groups vanilla would have
    /// re-decided while a re-stamp holds them, and the counters of the TacticalStanceCooldown row (cumulative for the battle).
    /// Per side, BattleScope (the patch owns it); bounded by the pool size.
    /// </summary>
    public sealed class StanceRestampBook
    {
        public const int ChangeHistory = 16;

        private readonly Dictionary<int, int> _before = new Dictionary<int, int>(64);
        private readonly Dictionary<int, float> _written = new Dictionary<int, float>(64);
        private readonly Dictionary<int, double> _wantSince = new Dictionary<int, double>(64);
        private readonly Dictionary<int, double[]> _changes = new Dictionary<int, double[]>(64);
        private readonly List<int> _scratch = new List<int>(64);

        private bool _retreatReleased;

        public int VanillaChanges { get; private set; }
        public int Restamped { get; private set; }
        public int Released { get; private set; }
        public int MaxChangesPerGroupHour { get; private set; }
        public int DelayUnder025 { get; private set; }
        public int Delay025To05 { get; private set; }
        public int Delay05To1 { get; private set; }
        public int DelayOver1 { get; private set; }

        public int RestampedCount => _written.Count;

        public void RecordBefore(int groupId, int orderedStance) => _before[groupId] = orderedStance;

        public bool TryBefore(int groupId, out int orderedStance) => _before.TryGetValue(groupId, out orderedStance);

        /// <summary>A vanilla stance change of a group: counted, and the most any group changed within one game-hour kept.</summary>
        public void NoteVanillaChange(int groupId, double doctrineHours)
        {
            VanillaChanges++;
            if (!_changes.TryGetValue(groupId, out double[] times))
            {
                times = new double[ChangeHistory];
                for (int i = 0; i < times.Length; i++) times[i] = double.NegativeInfinity;
                _changes[groupId] = times;
            }
            int oldest = 0;
            for (int i = 1; i < times.Length; i++) if (times[i] < times[oldest]) oldest = i;
            times[oldest] = doctrineHours;
            int withinHour = 0;
            for (int i = 0; i < times.Length; i++) if (doctrineHours - times[i] < 1.0) withinHour++;
            if (withinHour > MaxChangesPerGroupHour) MaxChangesPerGroupHour = withinHour;
        }

        public void NoteRestamp(int groupId, float written)
        {
            _written[groupId] = written;
            Restamped++;
        }

        public bool TryWritten(int groupId, out float written) => _written.TryGetValue(groupId, out written);

        /// <summary>The group wants a defend or screen change vanilla's chain would make now, but the re-stamp holds it; the first hour is kept.</summary>
        public void NoteWant(int groupId, double doctrineHours)
        {
            if (!_wantSince.ContainsKey(groupId)) _wantSince[groupId] = doctrineHours;
        }

        public void ClearWant(int groupId) => _wantSince.Remove(groupId);

        /// <summary>Vanilla changed the group: the time it waited past when it would have fired goes into the delay histogram.</summary>
        public void ResolveWant(int groupId, double doctrineHours)
        {
            if (!_wantSince.TryGetValue(groupId, out double since)) return;
            _wantSince.Remove(groupId);
            double delay = doctrineHours - since;
            if (delay < 0.25) DelayUnder025++;
            else if (delay < 0.5) Delay025To05++;
            else if (delay < 1.0) Delay05To1++;
            else DelayOver1++;
        }

        /// <summary>
        /// The first call with macroai 3 pulls back the stamps the fix wrote and still sees unchanged (to ReleaseStamp, as
        /// TacticalStanceWriter.ReleaseForRetreat does for Whiskey's own), drops the rest without a release (Whiskey's writer has
        /// stamped them since), and clears the set. Returns how many were released; later calls with macroai 3 do nothing.
        /// </summary>
        public int ReleaseOnRetreat(float rawNowHours, IStanceStampAccess stamps)
        {
            if (_retreatReleased) return 0;
            _retreatReleased = true;
            float release = TacticalStanceOwnership.ReleaseStamp(rawNowHours);
            _scratch.Clear();
            foreach (KeyValuePair<int, float> pair in _written) _scratch.Add(pair.Key);
            int released = 0;
            for (int i = 0; i < _scratch.Count; i++)
            {
                int id = _scratch[i];
                if (_written.TryGetValue(id, out float written) && stamps.Get(id) == written)
                {
                    stamps.Set(id, release);
                    released++;
                }
            }
            _written.Clear();
            _wantSince.Clear();
            Released += released;
            return released;
        }

        /// <summary>A call with macroai below 3: a later retreat releases again.</summary>
        public void NoteNotRetreating() => _retreatReleased = false;

        public void Clear()
        {
            _before.Clear();
            _written.Clear();
            _wantSince.Clear();
            _changes.Clear();
            _retreatReleased = false;
            VanillaChanges = Restamped = Released = MaxChangesPerGroupHour = 0;
            DelayUnder025 = Delay025To05 = Delay05To1 = DelayOver1 = 0;
        }
    }
}
