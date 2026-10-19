using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The covering force's input to the hold pass and its output (D11 requirement-covering-members, foresight F6b-2). CoveringForce.Prepare fills
    // the input from the review's snapshot, sectors and state: the post the members are ordered by (the stored verdict's), the forward post the
    // raw verdict is counted at, the axis sectors and their shares, the strength the force is sized to, the incumbents (by snapshot index: the
    // sector their recorded area maps to today) and the department of every formation. ReserveRules.Hold takes the members and writes them
    // here, CoveringForce.Settle publishes the verdict. A fresh object per pass: the terrain-filling review plans twice and keeps the second.
    internal sealed class CoveringInput
    {
        // A formation that stands at the post and cannot be massed (the busy, the unready, the map-group player): it counts toward the need
        // and no sector's cap. Strength is HoldStrength, X and Z the hold point.
        internal struct Standing
        {
            public float Strength, X, Z;
            public bool MapGroupPlayer;
        }

        // ---- the input ----
        public float OrderX, OrderZ;        // the stored verdict's post (the forward post while none is stored): members are ordered by distance to it
        public float ForwardX, ForwardZ;    // the forward post (the only post when there is one): the raw verdict counts there
        public float Radius;                // the relocation range
        public float Need;                  // CoveringNeed
        public float[] AxisShare = new float[0];
        public int[] IncumbentSector = new int[0];   // per own formation: the sector its recorded area maps to today, -1 for no incumbent
        public int[] IncumbentArea = new int[0];     // per own formation: the area it was recorded in, -1 none
        public Theater[] Department = new Theater[0];
        public Theater PostTheater = Theater.Unknown;
        public IReadOnlyList<GaveUpMove> GaveUp;
        public bool OnePost;
        public string PostKey = "none|none";
        public int ForwardPlace = -1, RearPlace = -1;   // indexes into CampaignSnapshot.Places, -1 none

        // ---- the output of the hold pass ----
        public readonly List<int> Members = new List<int>();      // snapshot indexes, in the order they were taken
        public readonly List<int> Retained = new List<int>();     // incumbents only briefly unavailable: they keep their entry this review
        public readonly List<Standing> NonMassable = new List<Standing>();
        public float[] Seeded = new float[0];   // per sector: the strength the incumbents gave it before its own reserve was chosen
        public float[] Given = new float[0];    // per sector: the strength its members gave the force

        // ---- the output of Settle ----
        public bool PublishedForward;
        public CoveringForce.VerdictStep Step;
        public int PostTown = -1;               // the published post, an index into CampaignSnapshot.Places
        public float PostX, PostZ;
        public float MemberStrength, Held;

        // The strength of the non-massable formations whose hold point lies within `radius` of (x, z).
        public float StandingNear(float x, float z, float radius)
        {
            float sum = 0f;
            float reachSq = radius * radius;
            for (int k = 0; k < NonMassable.Count; k++)
                if (ReserveRules.DistanceSq(NonMassable[k].X, NonMassable[k].Z, x, z) <= reachSq) sum += NonMassable[k].Strength;
            return sum;
        }
    }

    // What a per-row producer of the ownership table reads, built once by ClaimTable.Build: the review's snapshot, sectors and sector facts
    // (null in a hand-built harness world), the planner state, the moves the tracker gave up on and the depot ledger's held builders.
    internal readonly struct ClaimContext
    {
        public readonly CampaignSnapshot Snapshot;
        public readonly SectorModel Sectors;
        public readonly TheaterPlanner.SectorFacts Facts;
        public readonly PlannerState State;
        public readonly IReadOnlyList<GaveUpMove> GaveUp;
        public readonly IReadOnlyList<HeldBuilder> Held;
        // The formations the mover has sent toward an objective (MoveTracker.CollectDispatched), null when the harness gives none: row 4 reads it.
        public readonly IReadOnlyList<DispatchedMove> Dispatched;

        public ClaimContext(CampaignSnapshot snapshot, SectorModel sectors, TheaterPlanner.SectorFacts facts, PlannerState state,
                            IReadOnlyList<GaveUpMove> gaveUp, IReadOnlyList<HeldBuilder> held, IReadOnlyList<DispatchedMove> dispatched = null)
        {
            Dispatched = dispatched;
            Snapshot = snapshot;
            Sectors = sectors;
            Facts = facts;
            State = state;
            GaveUp = gaveUp;
            Held = held;
        }
    }

    // The producer of one pre-candidate row that a later slice fills: row 4 (commitment, F7) and row 5 (department moves, F8). It claims only
    // formations its row takes from (ClaimTable.Takes), which ApplySource enforces.
    internal interface IClaimSource
    {
        int Row { get; }
        void Produce(in ClaimContext ctx, Claim[] claims);
    }

    // The ownership table of a review (D11 requirement-claims): who owns each own formation, decided once and read by the plan's candidates
    // (G3) and by the allocator's roles (G4) alike, so the two cannot disagree on which formation a reserve holds. One Claim per formation, in
    // snapshot order. The rows run in this order and a row takes only the claimants its "Takes from" set lists:
    //   1 PinnedBuilder  the Wished depot builders the planner may move               from None
    //   2 Immobile       the busy, the non-combat, the player's own command           from None, PinnedBuilder
    //   3 Recover        the formations leaving for supply                             from None
    //   4 Committed      F7's source                                                   from None
    //   5 DepartmentMove F8's source                                                   from None
    //   6 Capital        ReserveRules.Capital                                          from None, Committed, DepartmentMove
    //   7 Hold           ReserveRules.Hold                                             from None
    //   8 Mass           TakeMass, after the plan has chosen its mass                  from None, Hold, Committed
    // (Row 9, D8's garrison pledge, is not a claimant; row 10, F8's pick, is a source of the department-move claimant.)
    internal static class ClaimTable
    {
        internal const int RowPinnedBuilder = 1, RowImmobile = 2, RowRecover = 3, RowCommitted = 4, RowDepartmentMove = 5, RowCapital = 6, RowHold = 7, RowMass = 8, RowDepartmentReinforcement = 10;

        // The producers of rows 4 and 5: F7's commitment (row 4); F8 adds the department move's (row 5).
        internal static readonly IReadOnlyList<IClaimSource> RowSources = new IClaimSource[] { Commitment.Source };

        // The reason a Mass claim carries when it was taken from a formation committed to the objective the push is still on: the reserve split
        // never splits one of those off (the excess comes from the others).
        internal const string ReasonCommitted = "committed";

        // The claimants a row may take a formation from; every other claim stands. The one definition of the "Takes from" column.
        internal static bool Takes(int row, Claimant current)
        {
            switch (row)
            {
                case RowPinnedBuilder:
                case RowRecover:
                case RowCommitted:
                case RowDepartmentMove:
                case RowHold:
                case RowDepartmentReinforcement:
                    return current == Claimant.None;
                case RowImmobile:
                    return current == Claimant.None || current == Claimant.PinnedBuilder;
                case RowCapital:
                    return current == Claimant.None || current == Claimant.Committed || current == Claimant.DepartmentMove;
                case RowMass:
                    return current == Claimant.None || current == Claimant.Hold || current == Claimant.Committed;
                default:
                    return false;
            }
        }

        // One unclaimed claim per own formation, sent nowhere: its target is where it stands, it is counted in no sector.
        internal static Claim[] Fresh(CampaignSnapshot s)
        {
            var claims = new Claim[s.Own.Count];
            for (int i = 0; i < claims.Length; i++)
                claims[i] = new Claim { InstanceId = s.Own[i].InstanceId, Claimant = Claimant.None, SectorId = -1, TargetX = s.Own[i].X, TargetZ = s.Own[i].Z };
            return claims;
        }

        // Rows 1 to 7: the table before the plan has chosen a mass. holdNeed, nearX and nearZ are per sector (the hold need and the army it
        // faces); sectorHeld receives, per sector with a need, the sum the hold reserve was sized on (D9's measure, TheaterPlan.SectorHeld).
        // facts is the review's SectorFacts, null when a harness builds the table for a hand-made plan.
        internal static Claim[] Build(CampaignSnapshot s, SectorModel sectors, float[] holdNeed, float[] nearX, float[] nearZ, float capitalFloor,
                                      IReadOnlyList<HeldBuilder> held, IReadOnlyList<GaveUpMove> gaveUp, PlannerState state, CoveringInput covering,
                                      IReadOnlyList<IClaimSource> rowSources, TheaterPlanner.SectorFacts facts, out float[] sectorHeld,
                                      IReadOnlyList<DispatchedMove> dispatched = null)
        {
            var claims = Fresh(s);
            sectorHeld = new float[sectors.Count];
            ClaimPinned(s, claims, held);
            ClaimImmobile(s, claims);
            ClaimRecover(s, claims);
            var ctx = new ClaimContext(s, sectors, facts, state, gaveUp, held, dispatched);
            ApplySources(rowSources, RowCommitted, ctx, claims);
            ApplySources(rowSources, RowDepartmentMove, ctx, claims);
            ReserveRules.Capital(s, capitalFloor, claims);
            ReserveRules.Hold(s, sectors, holdNeed, nearX, nearZ, claims, covering, held, sectorHeld);
            return claims;
        }

        // Row 1: the builder of a Wished depot task (ReserveRules.WishedBuilders: the ledger's Wished builders the planner may move). It is a
        // DepotBuilder at the pin's position, whatever the plan does with the task; the hold pass may still choose it as a reserve body.
        internal static void ClaimPinned(CampaignSnapshot s, Claim[] claims, IReadOnlyList<HeldBuilder> held)
        {
            bool[] mask = ReserveRules.WishedBuilders(s, held);
            if (mask == null) return;
            for (int k = 0; k < held.Count; k++)
            {
                int i = s.IndexOfOwn(held[k].InstanceId);
                if (i < 0 || !mask[i] || !Takes(RowPinnedBuilder, claims[i].Claimant)) continue;
                claims[i].Claimant = Claimant.PinnedBuilder;
                claims[i].TargetX = held[k].TargetX;
                claims[i].TargetZ = held[k].TargetZ;
            }
        }

        // Row 2: a formation that cannot be ordered (busy, not a combat force, the player's own command or a CIC side) stays where it is.
        internal static void ClaimImmobile(CampaignSnapshot s, Claim[] claims)
        {
            for (int i = 0; i < claims.Length; i++)
                if (!TheaterPlanner.Movable(s.Own[i]) && Takes(RowImmobile, claims[i].Claimant)) claims[i].Claimant = Claimant.Immobile;
        }

        // Row 3: a formation below the supply or morale floor is walked toward the nearest supply source (the spot it stands on with none).
        internal static void ClaimRecover(CampaignSnapshot s, Claim[] claims)
        {
            for (int i = 0; i < claims.Length; i++)
            {
                var f = s.Own[i];
                if (!ReserveRules.Recovers(f, s.MinMoraleForMovement) || !Takes(RowRecover, claims[i].Claimant)) continue;
                claims[i].Claimant = Claimant.Recover;
                if (!LogisticsGate.NearestSource(s, f.X, f.Z, out claims[i].TargetX, out claims[i].TargetZ))
                {
                    claims[i].TargetX = f.X;
                    claims[i].TargetZ = f.Z;
                }
            }
        }

        // Runs one row's producers and refuses (InvalidOperationException) a claim it may not make: a source takes only the formations its row
        // lists, and only to its own claimant.
        internal static void ApplySource(IClaimSource source, in ClaimContext ctx, Claim[] claims)
        {
            var before = new Claimant[claims.Length];
            for (int i = 0; i < claims.Length; i++) before[i] = claims[i].Claimant;
            source.Produce(in ctx, claims);
            Claimant produced = source.Row == RowCommitted ? Claimant.Committed : Claimant.DepartmentMove;
            for (int i = 0; i < claims.Length; i++)
            {
                if (claims[i].Claimant == before[i]) continue;
                if (claims[i].Claimant != produced || !Takes(source.Row, before[i]))
                    throw new InvalidOperationException("row " + source.Row + " may not take a " + before[i] + " formation as " + claims[i].Claimant);
            }
        }

        private static void ApplySources(IReadOnlyList<IClaimSource> sources, int row, in ClaimContext ctx, Claim[] claims)
        {
            if (sources == null) return;
            for (int k = 0; k < sources.Count; k++)
                if (sources[k].Row == row) ApplySource(sources[k], in ctx, claims);
        }

        // Row 8: the plan's mass (TheaterPlan.MassInstanceIds) takes the formations it may; a member another owner holds (a guard, a pinned
        // builder, a formation that cannot move) stays that owner's. A Mass claim keeps the sector and share the hold pass counted it with.
        // commitmentContinues: the push is still on the objective the last mass was on, so a formation committed to it is taken as such (reason
        // ReasonCommitted); when the commitment ended this review a formation it held that the new mass takes is an ordinary member.
        internal static void TakeMass(Claim[] claims, CampaignSnapshot s, IReadOnlyList<int> massInstanceIds, bool commitmentContinues = false)
        {
            for (int k = 0; k < massInstanceIds.Count; k++)
            {
                int i = s.IndexOfOwn(massInstanceIds[k]);
                if (i < 0 || !Takes(RowMass, claims[i].Claimant)) continue;
                bool committed = commitmentContinues && claims[i].Claimant == Claimant.Committed;
                claims[i].Claimant = Claimant.Mass;
                claims[i].Reason = committed ? ReasonCommitted : null;   // a covering member the push on the enemy capital takes is the mass's, not the force's
            }
        }

        // Whether the tracker gave up on sending the formation toward the objective (P8): the one test the mass and row 4 share.
        internal static bool GaveUpOn(IReadOnlyList<GaveUpMove> gaveUp, int instanceId, int objectiveId)
        {
            if (gaveUp == null) return false;
            for (int k = 0; k < gaveUp.Count; k++)
                if (gaveUp[k].InstanceId == instanceId && gaveUp[k].ObjectiveId == objectiveId) return true;
            return false;
        }

        // A formation the hold pass chose as a sector's reserve body: a Hold claim, or a PinnedBuilder the mass could use, counted in a sector.
        // The formations that hold a sector anyway (the unready, the busy, the map-group player) are not.
        internal static bool IsReserveBody(in Claim c, in FormationRecord f, float minMorale)
        {
            return c.SectorId >= 0
                && (c.Claimant == Claimant.Hold || (c.Claimant == Claimant.PinnedBuilder && TheaterPlanner.MassEligible(f, minMorale)));
        }

        // What D8's garrison post credits as the hold already kept against the army that threatens its town: a reserve body, and a capital
        // guard standing in a sector it was counted in (it really holds that sector; integrator decision 2026-10-18 on phase 1 gap 18).
        internal static bool HoldsItsSector(in Claim c, in FormationRecord f, float minMorale)
        {
            return IsReserveBody(c, f, minMorale) || (c.Claimant == Claimant.Capital && c.SectorId >= 0);
        }
    }
}
