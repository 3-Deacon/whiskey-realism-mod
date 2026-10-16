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
    }

    /// <summary>
    /// BC14's per-side feasibility ledger (spec § 8): every candidate's result as the last finished evaluation left it. The writer is the
    /// coordinator's heavy path only; a running evaluation fills the back buffers and <see cref="Publish"/> swaps them in whole, so a
    /// reader never sees a half-evaluated ledger.
    /// </summary>
    public sealed class FeasibilityLedger : IFeasibilityLookup
    {
        public const int MaxCandidates = 24;
        public const int MaxMembersPerCandidate = 8;

        private sealed class Buffer
        {
            public readonly CandidateResult[] Results = new CandidateResult[MaxCandidates];
            public readonly BodyEstimate[] Bodies = new BodyEstimate[MaxCandidates * ManeuverFeasibility.MaxBodies];
            public readonly MemberEstimate[] Members = new MemberEstimate[MaxCandidates * MaxMembersPerCandidate];
            public int ResultCount, BodyCount, MemberCount;

            public void Clear()
            {
                ResultCount = 0;
                BodyCount = 0;
                MemberCount = 0;
            }
        }

        private Buffer _front = new Buffer();
        private Buffer _back = new Buffer();
        private readonly FeasibilityJob _job = new FeasibilityJob();

        /// <summary>The doctrine hour the published evaluation started at.</summary>
        public double EvaluatedHours { get; private set; }

        /// <summary>An evaluation has been published.</summary>
        public bool HasEvaluation { get; private set; }

        public int CandidateCount => _front.ResultCount;

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

        // ---- writer side (the evaluation) ----------------------------------------------------------------------

        internal void BeginBack() => _back.Clear();

        /// <summary>Appends one candidate to the back buffer: its result, bodies and members. Returns false when the buffers are full.</summary>
        internal bool AddBack(in CandidateResult result, ReadOnlySpan<BodyEstimate> bodies, ReadOnlySpan<MemberEstimate> members)
        {
            Buffer b = _back;
            if (b.ResultCount >= MaxCandidates) return false;
            CandidateResult r = result;
            r.BodyStart = b.BodyCount;
            r.BodyCount = Math.Min(bodies.Length, ManeuverFeasibility.MaxBodies);
            r.MemberStart = b.MemberCount;
            r.MemberCount = Math.Min(members.Length, MaxMembersPerCandidate);
            for (int i = 0; i < r.BodyCount; i++) b.Bodies[b.BodyCount++] = bodies[i];
            for (int i = 0; i < r.MemberCount; i++) b.Members[b.MemberCount++] = members[i];
            b.Results[b.ResultCount++] = r;
            return true;
        }

        /// <summary>Swaps the finished back buffers in whole.</summary>
        internal void Publish(double evaluatedHours)
        {
            Buffer swap = _front;
            _front = _back;
            _back = swap;
            EvaluatedHours = evaluatedHours;
            HasEvaluation = true;
        }
    }
}
