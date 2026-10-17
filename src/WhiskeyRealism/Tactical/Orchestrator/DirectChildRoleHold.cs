using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// A direct child keeps its role until the plan changes, <see cref="HoldHours"/> of the doctrine clock have passed, or a crisis forces a change. The
    /// allocator is a pure function of bucketed evidence (contact flag, flank exposure, the positional band) that flickers from tick to tick, and only Main
    /// had a hysteresis, so one child went Screen, Main, RefuseRight, Reserve, Screen in seven allocations under one plan (smoke 2026-10-17). A division
    /// that is given a new role is sent somewhere else, often behind it: "walking backwards toward the enemy".
    ///
    /// What passes at once, held or not: a fallback (either way: the evidence that sent a child back also ends it), a guard point or a screen target
    /// (a crossing guard, a station, a secondary objective's screen: each has a binding and hysteresis of its own), the escalations Fix, RefuseLeft and
    /// RefuseRight (contact, an outflanked wing: an enemy on the flank is a crisis), and the release of a Reserve child to SupportMain (the release
    /// hysteresis is the army's own). What waits: every de-escalation and every reshuffle among Main, SupportMain, Screen and Reserve.
    ///
    /// One instance per army orchestrator; the committed intents are the memory. Pure: the caller supplies the doctrine hours.
    /// </summary>
    public sealed class DirectChildRoleHold
    {
        /// <summary>The longest a role is held: the plan's own commitment (<see cref="ArmyReplanTriggers.PlanCommitmentBattleSeconds"/>) in doctrine hours.</summary>
        public const double HoldHours = ArmyReplanTriggers.PlanCommitmentBattleSeconds / 3600.0;

        /// <summary>
        /// A held Fix, RefuseLeft or RefuseRight is a response to contact or to an outflanked wing, not an organisation of the line: it is re-evaluated after
        /// this long (a quarter hour), so a flank guard does not outlive its threat by an hour. (Without it a wing marked outflanked for one tick
        /// guarded for the full hold, and a whole army's tasks drifted to GuardFlank.)
        /// </summary>
        public const double CrisisRoleHoldHours = 0.25;

        private DirectChildIntent[] _held = new DirectChildIntent[0];
        private double[] _since = new double[0];
        private int _count;
        private int _planKey = int.MinValue;

        public void Clear()
        {
            _count = 0;
            _planKey = int.MinValue;
        }

        /// <summary>
        /// The intents the army acts on this allocation: <paramref name="fresh"/> with every role the hold keeps replaced by the committed intent.
        /// <paramref name="planKey"/> names the plan the allocation was made for (its id, side and main effort band): a different key accepts all of
        /// <paramref name="fresh"/>.
        /// </summary>
        public IReadOnlyList<DirectChildIntent> Apply(IReadOnlyList<DirectChildIntent> fresh, int planKey, double nowHours)
        {
            if (fresh == null || fresh.Count == 0)
            {
                Clear();
                return fresh;
            }

            bool samePlan = planKey == _planKey;
            var result = new DirectChildIntent[fresh.Count];
            var nextHeld = new DirectChildIntent[fresh.Count];
            var nextSince = new double[fresh.Count];
            bool anyMain = false, freshMain = false;
            int freshMainAt = -1;
            for (int i = 0; i < fresh.Count; i++)
            {
                DirectChildIntent f = fresh[i];
                int at = samePlan ? IndexOf(f.ChildId) : -1;
                bool accept = at < 0;
                double since = nowHours;
                if (!accept)
                {
                    DirectChildIntent h = _held[at];
                    since = _since[at];
                    double hold = IsCrisisRole(h.Role) ? CrisisRoleHoldHours : HoldHours;
                    accept = f.Role == h.Role || Forced(h, f) || nowHours - since >= hold || nowHours < since;
                    if (accept && f.Role == h.Role) since = _since[at];
                    else if (accept) since = nowHours;
                }
                result[i] = accept ? f : _held[at];
                nextHeld[i] = result[i];
                nextSince[i] = since;
                if (result[i].Role == DirectChildRole.Main) anyMain = true;
                if (f.Role == DirectChildRole.Main && freshMainAt < 0) freshMainAt = i;
            }
            freshMain = freshMainAt >= 0;

            // The army is not left without a Main because the hold kept the old one's neighbours: when the old Main is gone (a fallback) and the fresh
            // allocation has one, that child takes it.
            if (!anyMain && freshMain)
            {
                result[freshMainAt] = fresh[freshMainAt];
                nextHeld[freshMainAt] = fresh[freshMainAt];
                nextSince[freshMainAt] = nowHours;
            }

            _held = nextHeld;
            _since = nextSince;
            _count = fresh.Count;
            _planKey = planKey;
            return result;
        }

        private int IndexOf(string childId)
        {
            for (int i = 0; i < _count; i++)
                if (string.Equals(_held[i].ChildId, childId, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static bool IsCrisisRole(DirectChildRole role)
            => role == DirectChildRole.Fix || role == DirectChildRole.RefuseLeft || role == DirectChildRole.RefuseRight;

        private static bool Forced(in DirectChildIntent held, in DirectChildIntent fresh)
        {
            if (fresh.Role == DirectChildRole.Fallback || held.Role == DirectChildRole.Fallback) return true;
            if (fresh.HasGuardPoint || held.HasGuardPoint || fresh.HasScreenTarget || held.HasScreenTarget) return true;
            if (fresh.Role == DirectChildRole.Fix || fresh.Role == DirectChildRole.RefuseLeft || fresh.Role == DirectChildRole.RefuseRight) return true;
            return held.Role == DirectChildRole.Reserve && fresh.Role == DirectChildRole.SupportMain;
        }
    }
}
