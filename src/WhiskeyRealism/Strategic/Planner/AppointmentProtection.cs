using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The planner's appointees are shielded from vanilla's job-24 swap for ProtectionDays (plan
    // P13, spec C16). Day serials are ReviewCadenceCatchUp.DaySerial (calendar days, the review
    // clock; not the legacy year x 372 serial); a window is the appointment day plus the next 59
    // calendar days. Written
    // only by CommanderAppointmentRuntime inside the daily review; read by the #6 guard
    // (CommanderReplacementPatch). G6 persists ProtectedUntil in sidecar v3 and calls Restore.
    public sealed class AppointmentProtection
    {
        public const int ProtectionDays = 60;

        private readonly Dictionary<int, int> _lastProtectedDay = new Dictionary<int, int>();

        public void Protect(int commanderId, int daySerial)
        {
            if (commanderId < 0) return;
            _lastProtectedDay[commanderId] = daySerial + ProtectionDays - 1;
        }

        public bool IsProtected(int commanderId, int daySerial)
        {
            return _lastProtectedDay.TryGetValue(commanderId, out int last) && daySerial <= last;
        }

        // Ledger aging (plan P1: runs in bookkeeping reviews too).
        public void Prune(int daySerial)
        {
            List<int> expired = null;
            foreach (var entry in _lastProtectedDay)
                if (entry.Value < daySerial) (expired ?? (expired = new List<int>())).Add(entry.Key);
            if (expired == null) return;
            for (int i = 0; i < expired.Count; i++) _lastProtectedDay.Remove(expired[i]);
        }

        // commanderId -> last protected day serial, ordered by commander id (stable sidecar output).
        public IEnumerable<KeyValuePair<int, int>> ProtectedUntil
        {
            get
            {
                var ids = new List<int>(_lastProtectedDay.Keys);
                ids.Sort();
                for (int i = 0; i < ids.Count; i++)
                    yield return new KeyValuePair<int, int>(ids[i], _lastProtectedDay[ids[i]]);
            }
        }

        public void Restore(IEnumerable<KeyValuePair<int, int>> protectedUntil)
        {
            _lastProtectedDay.Clear();
            if (protectedUntil == null) return;
            foreach (var entry in protectedUntil)
                if (entry.Key >= 0) _lastProtectedDay[entry.Key] = entry.Value;
        }
    }

    // Both AI alliances' protection windows plus the day serial of each alliance's last review.
    // Age runs every review (bookkeeping included, P1); the #6 guard runs inside vanilla's job 24
    // between reviews, so a commander counts as protected as of that last review day, and never
    // before the first review of the campaign. Owned by CommanderAppointmentRuntime (CampaignScope).
    public sealed class AppointmentProtectionBook
    {
        private readonly AppointmentProtection[] _byAlliance = { new AppointmentProtection(), new AppointmentProtection() };
        private readonly int[] _lastReviewDay = { -1, -1 };

        public AppointmentProtection For(int alliance)
        {
            return alliance >= 0 && alliance < _byAlliance.Length ? _byAlliance[alliance] : null;
        }

        public int LastReviewDay(int alliance)
        {
            return alliance >= 0 && alliance < _lastReviewDay.Length ? _lastReviewDay[alliance] : -1;
        }

        public void Age(int alliance, int daySerial)
        {
            if (alliance < 0 || alliance >= _byAlliance.Length) return;
            _lastReviewDay[alliance] = daySerial;
            _byAlliance[alliance].Prune(daySerial);
        }

        public bool IsProtected(int alliance, int commanderId)
        {
            if (alliance < 0 || alliance >= _byAlliance.Length || _lastReviewDay[alliance] < 0) return false;
            return _byAlliance[alliance].IsProtected(commanderId, _lastReviewDay[alliance]);
        }
    }

    // The slots the #6 guard cleared in GameVars.alliance[a].worstcommanderidperlevel, with the
    // ids it took out of them.
    public sealed class AppointmentGuardState
    {
        internal readonly int[] Slots;
        internal readonly int[] Ids;

        public int Count => Slots.Length;
        public int SlotAt(int k) => Slots[k];
        public int IdAt(int k) => Ids[k];

        internal AppointmentGuardState(int[] slots, int[] ids)
        {
            Slots = slots;
            Ids = ids;
        }
    }

    // The pure halves of the #6 guard (plan P13). Vanilla's CheckAICommanderReplacements
    // (17008-17032) skips a rank slot whose worst id is -1 (17017), so clearing a protected
    // commander's slot keeps job 24 from replacing him; vanilla sets both ids of a slot it
    // consumed to -1 (17027-17028), which Restore never touches.
    public static class AppointmentGuard
    {
        // The #6 Prefix's whole decision: clear the slots of this alliance's protected appointees.
        public static AppointmentGuardState ClearProtected(int[] worst, AppointmentProtectionBook book, int alliance)
        {
            if (book == null) return null;
            return Clear(worst, id => book.IsProtected(alliance, id));
        }

        // Vanilla's own swap test for one rank slot (CheckAICommanderReplacements 17017-17020):
        // both ids set and different, the worst one commands a unit outside a garrison, the best
        // one is pickable and beats the worst by expdifferenceforverticalpromotion on
        // GetAverageAttributeValue. True for a cleared slot = the guard really blocked a swap.
        public static bool VanillaWouldReplace(int bestId, int worstId, bool worstHasCommand, bool worstInGarrison,
                                               bool bestPickable, float bestAverage, float worstAverage, float margin)
        {
            return worstId >= 0 && bestId >= 0 && worstId != bestId && worstHasCommand && bestPickable
                && !worstInGarrison && bestAverage > worstAverage + margin;
        }

        public static AppointmentGuardState Clear(int[] worst, Func<int, bool> isProtected)
        {
            if (worst == null || isProtected == null) return null;
            int count = 0;
            for (int i = 0; i < worst.Length; i++)
                if (worst[i] >= 0 && isProtected(worst[i])) count++;
            if (count == 0) return null;

            var slots = new int[count];
            var ids = new int[count];
            int k = 0;
            for (int i = 0; i < worst.Length && k < count; i++)
            {
                if (worst[i] < 0 || !isProtected(worst[i])) continue;
                slots[k] = i;
                ids[k] = worst[i];
                worst[i] = -1;
                k++;
            }
            return new AppointmentGuardState(slots, ids);
        }

        // Writes each saved id back into its own slot while that slot is still the -1 the guard
        // left there; returns how many slots it restored.
        public static int Restore(int[] worst, AppointmentGuardState state)
        {
            if (worst == null || state == null) return 0;
            int restored = 0;
            for (int k = 0; k < state.Slots.Length; k++)
            {
                int slot = state.Slots[k];
                if (slot < 0 || slot >= worst.Length || worst[slot] != -1) continue;
                worst[slot] = state.Ids[k];
                restored++;
            }
            return restored;
        }
    }
}
