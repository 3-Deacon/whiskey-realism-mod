using System;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14 requirement-assumption-monitor: the selected plan is re-evaluated on every new heavy snapshot and compared with the evaluation
    // it was selected on. A broken assumption is a reason (a static literal) the army replans on; the replan itself lives in
    // ArmyTickCycle.MaybeReplan (cooldown, hysteresis, named suppression).

    /// <summary>What the monitor reads of the world besides the two evaluations: BC11's phases, where the children stand, the crossings' states, the abandoned wings.</summary>
    public interface IMonitorWorld
    {
        /// <summary>BC11's phase for the direct child; <see cref="ManeuverPhase.None"/> before its maneuver starts.</summary>
        ManeuverPhase PhaseOf(string childId);

        /// <summary>The river region the child stands in (<see cref="RiverRegions.RegionAt"/>), -1 when unknown.</summary>
        int RegionOf(string childId);

        /// <summary>The crossing is Held now (the side's crossing watch).</summary>
        bool HeldNow(int link);

        /// <summary>
        /// Appends the direct children BC11 abandoned since the last call to <paramref name="into"/> (after <paramref name="count"/> entries)
        /// and returns the new count.
        /// </summary>
        int DrainAbandoned(string[] into, int count);
    }

    /// <summary>
    /// The facts the monitor compares besides the two evaluations (spec § 8), one entry per member of the selected candidate and, in
    /// <see cref="CrossingStart"/> order, one per crossing on a member's route. Pooled arrays, owned by the ledger.
    /// </summary>
    public struct MonitorFacts
    {
        public double SelectedAtHours, NowHours;
        public bool SelectedWhileUnknown, MapKnownFired;
        public int MemberCount;
        public string[] ChildId;
        /// <summary><see cref="ManeuverFeasibility.MemberEnvelops"/>, <see cref="ManeuverFeasibility.MemberFix"/> or <see cref="ManeuverFeasibility.MemberOther"/>.</summary>
        public byte[] Kind;
        /// <summary>The member's body in the selected candidate.</summary>
        public int[] Body;
        public ManeuverPhase[] Phase;
        /// <summary>The ledger's planned release for the child (absolute doctrine hours); false for a fix in contact or a child with no entry.</summary>
        public bool[] HasRelease;
        public double[] PlannedReleaseHours;
        /// <summary>The member's desired strike S* when selected (hours from <see cref="SelectedAtHours"/>) and its strike time in the current evaluation (hours from now, NaN when the member is not in it).</summary>
        public float[] DesiredBefore, StrikeAfter;
        public int[] CrossingStart, CrossingCount;
        /// <summary>Per crossing: the child stands in its far region, and it turned Held since the route was priced.</summary>
        public bool[] CrossingPassed, CrossingHeld;
        public string[] AbandonedChildIds;
        public int AbandonedCount;

        public static MonitorFacts Create() => new MonitorFacts
        {
            ChildId = new string[FeasibilityLedger.MaxMembersPerCandidate],
            Kind = new byte[FeasibilityLedger.MaxMembersPerCandidate],
            Body = new int[FeasibilityLedger.MaxMembersPerCandidate],
            Phase = new ManeuverPhase[FeasibilityLedger.MaxMembersPerCandidate],
            HasRelease = new bool[FeasibilityLedger.MaxMembersPerCandidate],
            PlannedReleaseHours = new double[FeasibilityLedger.MaxMembersPerCandidate],
            DesiredBefore = new float[FeasibilityLedger.MaxMembersPerCandidate],
            StrikeAfter = new float[FeasibilityLedger.MaxMembersPerCandidate],
            CrossingStart = new int[FeasibilityLedger.MaxMembersPerCandidate],
            CrossingCount = new int[FeasibilityLedger.MaxMembersPerCandidate],
            CrossingPassed = new bool[FeasibilityLedger.MaxCrossingsPerCandidate],
            CrossingHeld = new bool[FeasibilityLedger.MaxCrossingsPerCandidate],
            AbandonedChildIds = new string[FeasibilityLedger.MaxMembersPerCandidate],
        };
    }

    public static partial class ManeuverFeasibility
    {
        /// <summary>A body whose strike estimate is later than its desired strike by more than this has broken the plan's timing.</summary>
        public const float LateHours = 1.0f;
        /// <summary>A body's detail penalty must rise this far above its value at selection (and past <see cref="RiskyPenalty"/>) to break the plan.</summary>
        public const float ReplanPenaltyMargin = 0.3f;
        /// <summary>The best candidate must beat the plan being kept by this much score to replace it on a broken assumption.</summary>
        public const float ReplanScoreMargin = 0.1f;

        public const string ReasonCrossingHeld = "crossing-held", ReasonRouteMissing = "route-missing", ReasonLate = "late", ReasonDetail = "detail",
            ReasonWingAbandoned = "wing-abandoned", ReasonMapKnown = "map-known";

        /// <summary>
        /// The monitor's evaluation: only the selected plan, on the side the army selected it on, into the ledger's monitor ledger (never the
        /// candidate table the selection reads). Runs to completion; it is one candidate.
        /// </summary>
        public static void EvaluateSelected(in FeasibilityInput input, FeasibilityLedger into)
        {
            if (!into.HasSelection) return;
            FeasibilityLedger monitor = into.MonitorLedger;
            monitor.SetSelectedSide(into.SelectedPlan, into.SelectedMirrored);
            FeasibilityInput one = input;
            one.Candidates = into.OneCandidate(into.SelectedPlan);
            one.CandidateCount = 1;
            // The monitor job keeps its own leg cache: Begin clears the one it uses, and a shared cache would lose the legs of a full evaluation that is mid-run.
            one.Cache = null;
            Evaluate(in one, monitor);
            into.MarkMonitorFresh();
        }

        // The first approach reason in the spec's order, or null: crossing-held, route-missing, late, detail, wing-abandoned.
        private static string ApproachReason(in CandidateResult atSelection, in CandidateResult now, ReadOnlySpan<BodyEstimate> before,
            ReadOnlySpan<BodyEstimate> after, in MonitorFacts facts)
        {
            // crossing-held: a crossing on a still-to-launch member's route turned Held and the member has not passed it.
            for (int i = 0; i < facts.MemberCount; i++)
            {
                if (!Pending(in facts, i)) continue;
                for (int c = 0; c < facts.CrossingCount[i]; c++)
                {
                    int at = facts.CrossingStart[i] + c;
                    if (facts.CrossingHeld[at] && !facts.CrossingPassed[at]) return ReasonCrossingHeld;
                }
            }

            // route-missing: a leg of the plan has no path (the NavMesh refused it and no other crossing serves).
            if (now.Verdict == FeasibilityVerdict.Impossible && string.Equals(now.Reason, "no-route", StringComparison.Ordinal)
                && !(atSelection.Verdict == FeasibilityVerdict.Impossible && string.Equals(atSelection.Reason, "no-route", StringComparison.Ordinal)))
                return ReasonRouteMissing;

            // late: a member's strike estimate is past the desired strike recorded at selection. A held member is measured from its
            // planned release (its order has not gone out), a marching one from now.
            for (int i = 0; i < facts.MemberCount; i++)
            {
                if (!Pending(in facts, i) || float.IsNaN(facts.StrikeAfter[i])) continue;
                bool held = (facts.Phase[i] == ManeuverPhase.None || facts.Phase[i] == ManeuverPhase.Hold) && facts.HasRelease[i];
                double estimate = (held ? facts.PlannedReleaseHours[i] : facts.NowHours) + facts.StrikeAfter[i];
                double desired = facts.SelectedAtHours + facts.DesiredBefore[i];
                if (estimate - desired > LateHours) return ReasonLate;
            }

            // detail: a still-to-launch body's detail penalty rose past Risky and well above its value at selection.
            for (int b = 0; b < before.Length; b++)
            {
                bool pending = false;
                for (int i = 0; i < facts.MemberCount; i++) if (facts.Body[i] == b && Pending(in facts, i)) pending = true;
                if (!pending) continue;
                for (int a = 0; a < after.Length; a++)
                {
                    if (!string.Equals(after[a].ChildId, before[b].ChildId, StringComparison.Ordinal)) continue;
                    if (after[a].Detail > RiskyPenalty && after[a].Detail - before[b].Detail >= ReplanPenaltyMargin) return ReasonDetail;
                }
            }

            // wing-abandoned: BC11 gave up an enveloping child while another enveloping child, or a fix still to go in, depends on it.
            for (int i = 0; i < facts.MemberCount; i++)
            {
                if (facts.Kind[i] != MemberEnvelops || !Abandoned(in facts, facts.ChildId[i])) continue;
                for (int j = 0; j < facts.MemberCount; j++)
                {
                    if (j == i) continue;
                    bool otherWing = facts.Kind[j] == MemberEnvelops && !Abandoned(in facts, facts.ChildId[j]);
                    bool fixWaiting = facts.Kind[j] == MemberFix && Pending(in facts, j);
                    if (otherWing || fixWaiting) return ReasonWingAbandoned;
                }
            }
            return null;
        }

        // A member still to launch: an enveloping child before its Turn (an abandoned one is no longer waited on), a fix still holding or whose release is ahead.
        private static bool Pending(in MonitorFacts f, int i)
        {
            switch (f.Kind[i])
            {
                case MemberEnvelops:
                    return f.Phase[i] == ManeuverPhase.None || f.Phase[i] == ManeuverPhase.Hold || f.Phase[i] == ManeuverPhase.March;
                case MemberFix:
                    return f.Phase[i] == ManeuverPhase.Hold
                        || (f.Phase[i] == ManeuverPhase.None && f.HasRelease[i] && f.PlannedReleaseHours[i] > f.NowHours);
                default:
                    return false;
            }
        }

        private static bool Abandoned(in MonitorFacts f, string childId)
        {
            for (int a = 0; a < f.AbandonedCount; a++)
                if (string.Equals(f.AbandonedChildIds[a], childId, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Whether the plan's assumptions still hold (requirement-assumption-monitor). <paramref name="atSelection"/> and
        /// <paramref name="before"/> are the evaluation the plan was selected on, <paramref name="now"/> and <paramref name="after"/> the
        /// current one; the reasons come in the spec's order and the first wins. The approach reasons apply while the plan has an enveloping
        /// body that is not yet committed (every enveloping child in its Turn or Attack ends them); <c>map-known</c> applies to every plan.
        /// </summary>
        public static bool TryBrokenAssumption(in CandidateResult atSelection, in CandidateResult now, ReadOnlySpan<BodyEstimate> before,
            ReadOnlySpan<BodyEstimate> after, in MonitorFacts facts, out string reason)
            => TryBrokenAssumption(in atSelection, in now, before, after, in facts, out reason, out _);

        /// <summary>
        /// <see cref="TryBrokenAssumption(in CandidateResult, in CandidateResult, ReadOnlySpan{BodyEstimate}, ReadOnlySpan{BodyEstimate}, in MonitorFacts, out string)"/>
        /// that also reports <paramref name="committedReason"/>: the approach reason that would have broken the plan had a wing not already been
        /// committed (the telemetry row names the suppression <c>committed</c>); null when none, or when the plan is broken.
        /// </summary>
        public static bool TryBrokenAssumption(in CandidateResult atSelection, in CandidateResult now, ReadOnlySpan<BodyEstimate> before,
            ReadOnlySpan<BodyEstimate> after, in MonitorFacts facts, out string reason, out string committedReason)
        {
            reason = null;
            committedReason = null;
            bool envelops = false;
            for (int b = 0; b < before.Length; b++) if (before[b].Envelops) envelops = true;
            bool committed = true;
            for (int i = 0; i < facts.MemberCount; i++)
                if (facts.Kind[i] == MemberEnvelops && facts.Phase[i] != ManeuverPhase.Turn && facts.Phase[i] != ManeuverPhase.Attack) committed = false;

            string approach = envelops ? ApproachReason(in atSelection, in now, before, after, in facts) : null;
            if (approach != null)
            {
                if (!committed)
                {
                    reason = approach;
                    return true;
                }
                committedReason = approach;
            }

            // map-known: the plan was selected while its verdict was Unknown and a known verdict has appeared; once per selection.
            if (facts.SelectedWhileUnknown && !facts.MapKnownFired && now.Verdict != FeasibilityVerdict.Unknown)
            {
                reason = ReasonMapKnown;
                return true;
            }
            return false;
        }
    }
}
