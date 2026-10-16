using System;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // The ledger's half of the assumption monitor (requirement-assumption-monitor): the snapshot of the selected candidate as it was
    // evaluated when the army selected it, the monitor's own re-evaluation of the selected plan, and the comparison between the two.
    public sealed partial class FeasibilityLedger
    {
        private FeasibilityLedger _monitor;
        private readonly BattlePlanId[] _oneCandidate = new BattlePlanId[1];
        private MonitorFacts _facts = MonitorFacts.Create();
        private bool _hasSnapshot, _selectedWhileUnknown, _mapKnownFired, _monitorFresh;
        private readonly string[] _abandoned = new string[FeasibilityLedger.MaxMembersPerCandidate];
        private int _abandonedCount;

        /// <summary>The doctrine hour of the evaluation the plan was selected on.</summary>
        public double SelectedAtHours { get; private set; }

        /// <inheritdoc />
        public string BrokenAssumption { get; private set; }

        /// <summary>The monitor's re-evaluation of the selected plan (created on first use).</summary>
        internal FeasibilityLedger MonitorLedger => _monitor ?? (_monitor = new FeasibilityLedger());

        internal BattlePlanId[] OneCandidate(BattlePlanId plan)
        {
            _oneCandidate[0] = plan;
            return _oneCandidate;
        }

        /// <summary>The monitor ledger judges the plan on the side the army executes it, as the full evaluation does.</summary>
        internal void SetSelectedSide(BattlePlanId plan, bool mirrored)
        {
            SelectedPlan = plan;
            SelectedMirrored = mirrored;
            HasSelection = plan != BattlePlanId.Unknown;
        }

        internal void MarkMonitorFresh() => _monitorFresh = true;

        // A new selection: its evaluation becomes the snapshot the monitor compares against, and nothing is broken yet.
        private void BeginMonitoring()
        {
            BrokenAssumption = null;
            _mapKnownFired = false;
            _abandonedCount = 0;
            _monitorFresh = false;
            _selected.Clear();
            _hasSnapshot = false;
            SelectedAtHours = EvaluatedHours;
            if (HasSelection)
            {
                for (int r = 0; r < _front.ResultCount; r++)
                {
                    if (_front.Results[r].Plan != SelectedPlan) continue;
                    CandidateResult result = _front.Results[r];
                    _hasSnapshot = _selected.Append(in result,
                        new ReadOnlySpan<BodyEstimate>(_front.Bodies, result.BodyStart, result.BodyCount),
                        new ReadOnlySpan<MemberEstimate>(_front.Members, result.MemberStart, result.MemberCount),
                        new ReadOnlySpan<RouteCrossing>(_front.Crossings, result.CrossingStart, result.CrossingCount));
                    break;
                }
            }
            _selectedWhileUnknown = !_hasSnapshot || _selected.Results[0].Verdict == FeasibilityVerdict.Unknown;
        }

        /// <summary>
        /// Compares the monitor's latest re-evaluation of the selected plan with the snapshot it was selected on. Returns true and sets
        /// <see cref="BrokenAssumption"/> when an assumption is broken; <c>map-known</c> stays set until the next selection, so a cooldown that
        /// blocks the replan once does not lose it.
        /// </summary>
        public bool Assess(IMonitorWorld world, double nowHours)
        {
            BrokenAssumption = null;
            if (!HasSelection || !_monitorFresh || _monitor == null || !_monitor.TryGet(SelectedPlan, out CandidateResult now)) return false;
            FillFacts(world, nowHours, in now);
            CandidateResult before = _hasSnapshot ? _selected.Results[0] : default(CandidateResult);
            ReadOnlySpan<BodyEstimate> bodiesBefore = _hasSnapshot ? new ReadOnlySpan<BodyEstimate>(_selected.Bodies, 0, before.BodyCount) : default(ReadOnlySpan<BodyEstimate>);
            if (ManeuverFeasibility.TryBrokenAssumption(in before, in now, bodiesBefore, _monitor.Bodies(in now), in _facts, out string reason))
            {
                BrokenAssumption = reason;
                if (string.Equals(reason, ManeuverFeasibility.ReasonMapKnown, StringComparison.Ordinal)) _mapKnownFired = true;
                return true;
            }
            if (_mapKnownFired)
            {
                BrokenAssumption = ManeuverFeasibility.ReasonMapKnown;
                return true;
            }
            return false;
        }

        private void FillFacts(IMonitorWorld world, double nowHours, in CandidateResult now)
        {
            ref MonitorFacts f = ref _facts;
            f.SelectedAtHours = SelectedAtHours;
            f.NowHours = nowHours;
            f.SelectedWhileUnknown = _selectedWhileUnknown;
            f.MapKnownFired = _mapKnownFired;
            if (world != null) _abandonedCount = world.DrainAbandoned(_abandoned, _abandonedCount);
            f.AbandonedChildIds = _abandoned;
            f.AbandonedCount = _abandonedCount;
            f.MemberCount = 0;
            if (!_hasSnapshot) return;

            CandidateResult selected = _selected.Results[0];
            ReadOnlySpan<MemberEstimate> afterMembers = _monitor.Members(in now);
            int crossings = 0;
            for (int m = 0; m < selected.MemberCount; m++)
            {
                MemberEstimate member = _selected.Members[selected.MemberStart + m];
                int i = f.MemberCount++;
                f.ChildId[i] = member.ChildId;
                f.Kind[i] = member.Kind;
                f.Body[i] = member.Body;
                f.Phase[i] = world != null ? world.PhaseOf(member.ChildId) : ManeuverPhase.None;
                f.HasRelease[i] = TryPlannedRelease(member.ChildId, out double release, out _, out _, out _, out _, out _);
                f.PlannedReleaseHours[i] = release;
                f.DesiredBefore[i] = member.DesiredStrikeHours;
                f.StrikeAfter[i] = float.NaN;
                for (int k = 0; k < afterMembers.Length; k++)
                    if (string.Equals(afterMembers[k].ChildId, member.ChildId, StringComparison.Ordinal)) f.StrikeAfter[i] = afterMembers[k].StrikeHours;

                int region = world != null ? world.RegionOf(member.ChildId) : -1;
                f.CrossingStart[i] = crossings;
                for (int c = 0; c < member.CrossingCount && crossings < f.CrossingPassed.Length; c++)
                {
                    RouteCrossing crossing = _selected.Crossings[selected.CrossingStart + member.CrossingStart + c];
                    f.CrossingPassed[crossings] = crossing.FarRegion >= 0 && region == crossing.FarRegion;
                    f.CrossingHeld[crossings] = world != null && !crossing.HeldAtEvaluation && world.HeldNow(crossing.Link);
                    crossings++;
                }
                f.CrossingCount[i] = crossings - f.CrossingStart[i];
            }
        }
    }
}
