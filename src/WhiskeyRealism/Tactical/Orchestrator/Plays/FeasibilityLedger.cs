using System;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// The feasibility evaluation of one direct child in one candidate (requirement-bodies): which body it belongs to, its timing and
    /// its release. The ledger keeps these per candidate so the selected plan's per-child table (planned release, vias) is built from
    /// them without a second evaluation.
    /// </summary>
    public struct MemberEstimate
    {
        public string ChildId;
        public int Body;
        /// <summary><see cref="ManeuverFeasibility.MemberEnvelops"/>, <see cref="ManeuverFeasibility.MemberFix"/> or <see cref="ManeuverFeasibility.MemberOther"/>.</summary>
        public byte Kind;
        public bool InContact;
        public float TargetX, TargetZ;
        public float CourierHours, MarchHours, StrikeHours, DesiredStrikeHours, ReleaseHours;
        /// <summary>A release is published for the member (a fixing member in contact has none).</summary>
        public bool HasRelease;
        /// <summary>Link index of the first crossing of the march leg and of the inward leg, -1 none.</summary>
        public int MarchVia, AttackVia;
        /// <summary>The crossings on the member's march and inward legs, <c>CrossingStart..CrossingStart+CrossingCount</c> in the ledger's crossing array.</summary>
        public int CrossingStart, CrossingCount;
    }

    /// <summary>
    /// What BC11's maneuver reads of one direct child of the selected plan (requirement-runtime-wiring, "per-child values"): its planned
    /// release, time budget, vias and the regions they lead into, and the predicted hour it launches its attack.
    /// </summary>
    public struct ChildPlan
    {
        public string ChildId;
        public bool HasRelease;
        public double ReleaseHours;
        public bool HasBudget;
        public double TimeBudgetHours;
        public float ViaX, ViaZ, AttackViaX, AttackViaZ;
        public int ViaFarRegion, AttackViaFarRegion;
    }

    /// <summary>
    /// BC14's per-side feasibility ledger (spec § 8): every candidate's result as the last finished evaluation left it. The writer is the
    /// coordinator's heavy path only; a running evaluation fills the back buffers and <see cref="Publish"/> swaps them in whole, so a
    /// reader never sees a half-evaluated ledger.
    /// </summary>
    public sealed partial class FeasibilityLedger : IFeasibilityLookup
    {
        public const int MaxCandidates = 24;
        public const int MaxMembersPerCandidate = 8;
        public const int MaxCrossingsPerCandidate = 96;

        private sealed class Buffer
        {
            public readonly CandidateResult[] Results;
            public readonly BodyEstimate[] Bodies;
            public readonly MemberEstimate[] Members;
            public readonly RouteCrossing[] Crossings;
            public int ResultCount, BodyCount, MemberCount, CrossingCount;

            public Buffer(int candidates)
            {
                Results = new CandidateResult[candidates];
                Bodies = new BodyEstimate[candidates * ManeuverFeasibility.MaxBodies];
                Members = new MemberEstimate[candidates * MaxMembersPerCandidate];
                Crossings = new RouteCrossing[candidates * MaxCrossingsPerCandidate];
            }

            public void Clear()
            {
                ResultCount = 0;
                BodyCount = 0;
                MemberCount = 0;
                CrossingCount = 0;
            }

            /// <summary>Appends one candidate: its result with its bodies, members and crossings re-based into this buffer. False when full.</summary>
            public bool Append(in CandidateResult result, ReadOnlySpan<BodyEstimate> bodies, ReadOnlySpan<MemberEstimate> members,
                ReadOnlySpan<RouteCrossing> crossings)
            {
                if (ResultCount >= Results.Length) return false;
                CandidateResult r = result;
                r.BodyStart = BodyCount;
                r.BodyCount = Math.Min(bodies.Length, ManeuverFeasibility.MaxBodies);
                r.MemberStart = MemberCount;
                r.MemberCount = Math.Min(members.Length, MaxMembersPerCandidate);
                r.CrossingStart = CrossingCount;
                r.CrossingCount = Math.Min(crossings.Length, MaxCrossingsPerCandidate);
                for (int i = 0; i < r.BodyCount; i++) Bodies[BodyCount++] = bodies[i];
                for (int i = 0; i < r.MemberCount; i++) Members[MemberCount++] = members[i];
                for (int i = 0; i < r.CrossingCount; i++) Crossings[CrossingCount++] = crossings[i];
                Results[ResultCount++] = r;
                return true;
            }
        }

        private Buffer _front = new Buffer(MaxCandidates);
        private Buffer _back = new Buffer(MaxCandidates);
        // The selected candidate as it was evaluated when the army selected it (requirement-assumption-monitor compares against it).
        private readonly Buffer _selected = new Buffer(1);
        private readonly FeasibilityJob _job = new FeasibilityJob();

        // The selected plan's per-child table, rebuilt at each evaluation and at each capture (double-buffered with the candidates).
        private ChildPlan[] _childFront = new ChildPlan[ManeuverFeasibility.MaxChildren];
        private ChildPlan[] _childBack = new ChildPlan[ManeuverFeasibility.MaxChildren];
        private int _childFrontCount;

        /// <summary>The doctrine hour the published evaluation started at.</summary>
        public double EvaluatedHours { get; private set; }

        /// <summary>An evaluation has been published.</summary>
        public bool HasEvaluation { get; private set; }

        public int CandidateCount => _front.ResultCount;

        /// <summary>The plan and side the army selected (CaptureSelection); the evaluation scores that plan on that side.</summary>
        public BattlePlanId SelectedPlan { get; private set; }

        public bool SelectedMirrored { get; private set; }

        public bool HasSelection { get; private set; }

        /// <summary>
        /// The army selected a plan (after every pick and every kept plan): the ledger keeps its identity, scores it on this side from now on,
        /// snapshots its evaluation for the assumption monitor and builds the per-child table BC11 reads from its latest result.
        /// </summary>
        public void CaptureSelection(BattlePlanId plan, bool mirrored)
        {
            SelectedPlan = plan;
            SelectedMirrored = mirrored;
            HasSelection = plan != BattlePlanId.Unknown;
            _childFrontCount = HasSelection ? BuildChildTable(_front, _childFront) : 0;
            BeginMonitoring();
        }

        /// <summary>The ledger's own resumable job (one per side; the evaluation entry points drive it).</summary>
        internal FeasibilityJob Job => _job;

        public bool TryGet(BattlePlanId plan, out CandidateResult result)
        {
            Buffer b = _front;
            for (int i = 0; i < b.ResultCount; i++)
            {
                if (b.Results[i].Plan != plan) continue;
                result = b.Results[i];
                return true;
            }
            result = default(CandidateResult);
            return false;
        }

        public ReadOnlySpan<BodyEstimate> Bodies(in CandidateResult r)
            => new ReadOnlySpan<BodyEstimate>(_front.Bodies, r.BodyStart, r.BodyCount);

        public ReadOnlySpan<MemberEstimate> Members(in CandidateResult r)
            => new ReadOnlySpan<MemberEstimate>(_front.Members, r.MemberStart, r.MemberCount);

        /// <summary>The crossings on a member's march and inward legs, in the order the route meets them.</summary>
        public ReadOnlySpan<RouteCrossing> Crossings(in CandidateResult r, in MemberEstimate m)
            => new ReadOnlySpan<RouteCrossing>(_front.Crossings, r.CrossingStart + m.CrossingStart, m.CrossingCount);

        public bool TryPlannedRelease(string childId, out double releaseHours, out double timeBudgetHours, out float viaX, out float viaZ,
            out float attackViaX, out float attackViaZ)
        {
            releaseHours = 0.0;
            timeBudgetHours = 0.0;
            viaX = viaZ = attackViaX = attackViaZ = float.NaN;
            for (int i = 0; i < _childFrontCount; i++)
            {
                if (!string.Equals(_childFront[i].ChildId, childId, StringComparison.Ordinal)) continue;
                ChildPlan c = _childFront[i];
                if (!c.HasRelease) return false;
                releaseHours = c.ReleaseHours;
                timeBudgetHours = c.HasBudget ? c.TimeBudgetHours : 0.0;
                viaX = c.ViaX;
                viaZ = c.ViaZ;
                attackViaX = c.AttackViaX;
                attackViaZ = c.AttackViaZ;
                return true;
            }
            return false;
        }

        public bool TryViaRegions(string childId, out int viaFarRegion, out int attackViaFarRegion)
        {
            viaFarRegion = -1;
            attackViaFarRegion = -1;
            for (int i = 0; i < _childFrontCount; i++)
            {
                if (!string.Equals(_childFront[i].ChildId, childId, StringComparison.Ordinal)) continue;
                viaFarRegion = _childFront[i].ViaFarRegion;
                attackViaFarRegion = _childFront[i].AttackViaFarRegion;
                return true;
            }
            return false;
        }

        // One entry per enveloping or fixing member of the selected plan's result in the given buffer.
        private int BuildChildTable(Buffer source, ChildPlan[] into)
        {
            if (!HasSelection) return 0;
            int n = 0;
            for (int r = 0; r < source.ResultCount; r++)
            {
                if (source.Results[r].Plan != SelectedPlan) continue;
                CandidateResult result = source.Results[r];
                FeasibilityInput input = _job.Input;
                for (int m = 0; m < result.MemberCount && n < into.Length; m++)
                {
                    MemberEstimate member = source.Members[result.MemberStart + m];
                    if (member.Kind == ManeuverFeasibility.MemberOther) continue;
                    BodyEstimate body = source.Bodies[result.BodyStart + member.Body];
                    var c = new ChildPlan
                    {
                        ChildId = member.ChildId, HasRelease = member.HasRelease && member.Kind != ManeuverFeasibility.MemberOther,
                        ReleaseHours = ManeuverFeasibility.AbsoluteHours(EvaluatedHours, member.ReleaseHours),
                        ViaX = float.NaN, ViaZ = float.NaN, AttackViaX = float.NaN, AttackViaZ = float.NaN, ViaFarRegion = -1, AttackViaFarRegion = -1,
                    };
                    if (member.Kind == ManeuverFeasibility.MemberEnvelops)
                    {
                        c.HasBudget = true;
                        c.TimeBudgetHours = ManeuverFeasibility.TimeBudget(member.CourierHours, member.MarchHours, (float)input.DaylightHours);
                        if (member.MarchVia >= 0 && input.Regions != null)
                        {
                            input.Regions.LinkCrossing(member.MarchVia, out c.ViaX, out c.ViaZ, out _, out _);
                            c.ViaFarRegion = input.Regions.RegionAt(member.TargetX, member.TargetZ);
                        }
                        if (member.AttackVia >= 0 && input.Regions != null)
                        {
                            input.Regions.LinkCrossing(member.AttackVia, out c.AttackViaX, out c.AttackViaZ, out _, out _);
                            c.AttackViaFarRegion = input.Regions.RegionAt(body.StrikeX, body.StrikeZ);
                        }
                    }
                    into[n++] = c;
                }
                break;
            }
            return n;
        }

        // ---- writer side (the evaluation) ----------------------------------------------------------------------

        internal void BeginBack() => _back.Clear();

        /// <summary>Appends one candidate to the back buffer: its result, bodies, members and crossings. Returns false when the buffers are full.</summary>
        internal bool AddBack(in CandidateResult result, ReadOnlySpan<BodyEstimate> bodies, ReadOnlySpan<MemberEstimate> members,
            ReadOnlySpan<RouteCrossing> crossings)
            => _back.Append(in result, bodies, members, crossings);

        /// <summary>Swaps the finished back buffers in whole.</summary>
        internal void Publish(double evaluatedHours)
        {
            Buffer swap = _front;
            _front = _back;
            _back = swap;
            EvaluatedHours = evaluatedHours;
            HasEvaluation = true;
            // The per-child table follows the new results (the selected plan on its selected side).
            ChildPlan[] tmp = _childFront;
            _childFront = _childBack;
            _childBack = tmp;
            _childFrontCount = BuildChildTable(_front, _childFront);
        }
    }
}
