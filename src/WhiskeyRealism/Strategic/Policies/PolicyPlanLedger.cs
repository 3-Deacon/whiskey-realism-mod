using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Policies
{
    // One alliance's ranked policy candidates, rebuilt by the daily strategic review and
    // read by PolicySelectionPatch. The pick only changes when a challenger beats it by
    // SwitchMargin, when a need crosses EmergencyNeedThreshold upward, or when the pick left
    // the field (researched, ineligible). Ranked lists the pick first, then by score.
    // Held scores (irreversible acts below their need gate) never enter the ranking, so
    // Whiskey never starts one; with nothing else ranked the patch lets vanilla run.
    public sealed class PolicyPlanLedger
    {
        public const float SwitchMargin = 0.25f;
        public const float EmergencyNeedThreshold = 0.85f;

        private readonly List<PolicyScore> _ranked = new List<PolicyScore>();

        public int CurrentPick { get; private set; } = -1;

        public IReadOnlyList<PolicyScore> Ranked => _ranked;

        // Returns true when the pick changed.
        public bool Update(IReadOnlyList<PolicyScore> scored, NeedVector need, NeedVector previousNeed)
        {
            int before = CurrentPick;
            _ranked.Clear();
            if (scored != null)
                for (int i = 0; i < scored.Count; i++)
                    if (!scored[i].Held) _ranked.Add(scored[i]);
            _ranked.Sort((a, b) => b.Total != a.Total ? b.Total.CompareTo(a.Total) : a.PolicyId.CompareTo(b.PolicyId));

            if (_ranked.Count == 0)
            {
                CurrentPick = -1;
                return before != -1;
            }

            var top = _ranked[0];
            int currentIndex = IndexOf(CurrentPick);
            bool keep = currentIndex >= 0
                && !NeedCrossedEmergency(need, previousNeed)
                && top.Total < _ranked[currentIndex].Total + SwitchMargin;
            if (keep)
            {
                var current = _ranked[currentIndex];
                _ranked.RemoveAt(currentIndex);
                _ranked.Insert(0, current);
            }
            else
            {
                CurrentPick = top.PolicyId;
            }
            return CurrentPick != before;
        }

        public int NextEligible(System.Func<int, bool> stillEligible)
        {
            if (stillEligible == null) return -1;
            for (int i = 0; i < _ranked.Count; i++)
                if (stillEligible(_ranked[i].PolicyId))
                    return _ranked[i].PolicyId;
            return -1;
        }

        // Telemetry: "id:total(need/history/profile/lookahead/cost)" for the first count
        // ranked candidates, space-separated; "-" when empty.
        public string DescribeTop(int count)
        {
            if (_ranked.Count == 0 || count <= 0) return "-";
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _ranked.Count && i < count; i++)
            {
                var r = _ranked[i];
                if (i > 0) sb.Append(' ');
                sb.Append(r.PolicyId).Append(':').Append(r.Total.ToString("0.00", inv))
                  .Append('(').Append(r.Need.ToString("0.00", inv))
                  .Append('/').Append(r.Personality.ToString("0.00", inv))
                  .Append('/').Append(r.Profile.ToString("0.00", inv))
                  .Append('/').Append(r.LookAhead.ToString("0.00", inv))
                  .Append('/').Append((r.SideCost + r.SlotPressure + r.Duration).ToString("0.00", inv))
                  .Append(')');
            }
            return sb.ToString();
        }

        public void Restore(int currentPick)
        {
            CurrentPick = currentPick >= 0 ? currentPick : -1;
        }

        private int IndexOf(int policyId)
        {
            if (policyId < 0) return -1;
            for (int i = 0; i < _ranked.Count; i++)
                if (_ranked[i].PolicyId == policyId) return i;
            return -1;
        }

        private static bool NeedCrossedEmergency(NeedVector need, NeedVector previousNeed)
        {
            if (need == null || previousNeed == null) return false;
            for (int k = 0; k < PolicyEffectCatalog.NeedKindCount; k++)
            {
                var kind = (NeedKind)k;
                if (previousNeed[kind] < EmergencyNeedThreshold && need[kind] >= EmergencyNeedThreshold)
                    return true;
            }
            return false;
        }
    }
}
