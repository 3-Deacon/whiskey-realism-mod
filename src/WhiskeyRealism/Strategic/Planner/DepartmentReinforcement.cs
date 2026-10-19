using System;
using System.Collections.Generic;
using System.Globalization;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Strategic.Planner
{
    // Department shortfall reinforcement (foresight F8, D11 requirement-department-reinforcement; decision 5). The West (Porterfield's 1,402
    // men) had no way to be reinforced: D6's path serves only the main effort and #125 routes recruits within 300 of the recruiting cell. With
    // no push, the shortest department now gets one formation that stands outside it, chosen with D6's own selection
    // (TheaterPlanner.ReinforcementPick), under a lock read from the formations in a department move.
    //   have      DepartmentHave: the HoldStrength of the own formations that STAND in the department (their HoldSectorOf sector lies in its
    //             theater) and count toward coverage, less those claimed Mass, DepartmentMove or Recover. A formation leaving for another
    //             department or for supply is in no have from the first review it is claimed so, while it still stands in the donor, so a
    //             second short department's pick never counts it again in its donor. One definition: it ranks the short departments, is the
    //             donor test of both paths and D6's DepartmentHoldMet have.
    //   rank      the departments whose have is under their need (TheaterPlanner.DepartmentNeed), by (need - have) / need, the lower Theater on a
    //             tie. Only the first unlocked one is tried: no fall-through.
    //   row 10    after Decide, when no main effort is chosen: the pool (claimed None, MassEligible, no held builder, no secession pledge, no
    //             retained G8 post incumbent, no active recon mission, standing outside the department, no give-up on the department's
    //             sectors) goes to ReinforcementPick for the department's neediest sector; the pick is claimed DepartmentMove.
    //   row 5     a formation of PlannerState.DepartmentMoves that is still claimable (None after rows 1 to 4), whose receiver still has a facing
    //             sector, that has no give-up on the receiver's sectors and does not yet stand in a facing sector of it, is claimed
    //             DepartmentMove, bound for the receiver's neediest facing sector (recomputed each review). A push does not stop it.
    //   state     written once per review after Decide (WriteState): a formation claimed DepartmentMove this review is kept or added; one that
    //             cannot be moved today (in battle, in a vanilla defensive operation, embarked, in transfer) is kept, unclaimed, so neither a
    //             battle on the march nor a load frees the lock; one standing in a facing sector of its receiver has arrived and its home
    //             department changes (PlannerState.Transfer; c.Department is not refreshed, so the new home applies from the next review); every
    //             other is dropped (a capital take, Recover, a give-up, P8, a receiver with no facing sector, the formation gone).
    // The lock is the record as the review found it: the arrival review still reads the department locked, and the next review may pick.
    // Enable Department Reinforcement is the rollback: nothing is created and the record is cleared.
    internal static class DepartmentReinforcement
    {
        internal const string StatusDonor = "donor", StatusNoDonor = "nodonor", StatusLocked = "locked", StatusUntried = "untried", StatusPush = "push";
        internal const string StatePicked = "Picked", StateArrived = "Arrived", StateGaveUp = "GaveUp";

        // The producer of row 5.
        internal static readonly IClaimSource Source = new DepartmentMoveSource();

        private sealed class DepartmentMoveSource : IClaimSource
        {
            public int Row => ClaimTable.RowDepartmentMove;

            public void Produce(in ClaimContext ctx, Claim[] claims)
            {
                var s = ctx.Snapshot;
                var state = ctx.State;
                if (!PlannerSettings.DepartmentReinforcement || s == null || state == null || ctx.Facts == null || ctx.Sectors == null) return;
                if (state.DepartmentMoves.Count == 0) return;
                for (int i = 0; i < claims.Length; i++)
                {
                    var f = s.Own[i];
                    Theater receiver = state.DepartmentMoveOf(f.UnitKey);
                    if (receiver == Theater.Unknown || !ClaimTable.Takes(ClaimTable.RowDepartmentMove, claims[i].Claimant)) continue;
                    if (!NeediestFacingSector(s, ctx.Sectors, ctx.Facts, claims, receiver, out int sector)) continue;   // the receiver faces nothing now
                    if (StandsInFacingSector(s, ctx.Sectors, ctx.Facts, i, receiver) || GaveUpIn(ctx.GaveUp, f.InstanceId, ctx.Sectors, receiver)) continue;
                    claims[i].Claimant = Claimant.DepartmentMove;
                    ctx.Sectors.Centroid(sector, out claims[i].TargetX, out claims[i].TargetZ);
                }
            }
        }

        // ---- have ----

        // The department a formation stands in: the theater of the sector it is counted in (where it stands; the map-group player's last
        // waypoint). Unknown for a formation that stands in no sector.
        internal static Theater StandingDepartment(CampaignSnapshot s, SectorModel sectors, int i)
        {
            return sectors.TheaterOf(ReserveRules.HoldSectorOf(sectors, s.Own[i]));
        }

        // Whether formation i adds to the hold of the sector or department it stands in: it counts toward coverage and is not leaving for a
        // push (Mass), another department (DepartmentMove) or supply (Recover). `claims` may be null or short (a bare Evaluate): then nobody is
        // claimed.
        private static bool CountsInHave(CampaignSnapshot s, Claim[] claims, int i)
        {
            if (!WlChainRules.CountsTowardCoverage(s.Own[i].Chain)) return false;
            if (claims == null || i >= claims.Length) return true;
            Claimant owner = claims[i].Claimant;
            return owner != Claimant.Mass && owner != Claimant.DepartmentMove && owner != Claimant.Recover;
        }

        // DepartmentHave(department) less formation `excluded` (-1: none).
        internal static float Have(CampaignSnapshot s, SectorModel sectors, Claim[] claims, Theater department, int excluded = -1)
        {
            float have = 0f;
            for (int i = 0; i < s.Own.Count; i++)
                if (i != excluded && StandingDepartment(s, sectors, i) == department && CountsInHave(s, claims, i)) have += ReserveRules.HoldStrength(s.Own[i]);
            return have;
        }

        // The same sum over one sector (the formations whose HoldSectorOf is the sector): the `held` of the neediest-sector test.
        private static float HeldIn(CampaignSnapshot s, SectorModel sectors, Claim[] claims, int sector)
        {
            float held = 0f;
            for (int i = 0; i < s.Own.Count; i++)
                if (ReserveRules.HoldSectorOf(sectors, s.Own[i]) == sector && CountsInHave(s, claims, i)) held += ReserveRules.HoldStrength(s.Own[i]);
            return held;
        }

        // ---- the receiver's sectors ----

        // The department's facing sector that needs the most: the largest Need - held, the lower sector id on a tie. False when it has none.
        internal static bool NeediestFacingSector(CampaignSnapshot s, SectorModel sectors, TheaterPlanner.SectorFacts facts, Claim[] claims, Theater department, out int sector)
        {
            sector = -1;
            float best = float.NegativeInfinity;
            for (int k = 0; k < sectors.Count; k++)
            {
                if (sectors.TheaterOf(k) != department || !facts.Facing[k]) continue;
                float gap = facts.Need[k] - HeldIn(s, sectors, claims, k);
                if (gap > best)
                {
                    best = gap;
                    sector = k;
                }
            }
            return sector >= 0;
        }

        // Whether formation i stands in a facing sector of the department: it has arrived.
        internal static bool StandsInFacingSector(CampaignSnapshot s, SectorModel sectors, TheaterPlanner.SectorFacts facts, int i, Theater department)
        {
            int sector = ReserveRules.HoldSectorOf(sectors, s.Own[i]);
            return sector >= 0 && sector < sectors.Count && sectors.TheaterOf(sector) == department && facts.Facing[sector];
        }

        // Whether the tracker gave up on sending the formation anywhere in the department's sectors.
        internal static bool GaveUpIn(IReadOnlyList<GaveUpMove> gaveUp, int instanceId, SectorModel sectors, Theater department)
        {
            if (gaveUp == null) return false;
            for (int k = 0; k < gaveUp.Count; k++)
            {
                if (gaveUp[k].InstanceId != instanceId) continue;
                int sector = sectors.SectorAt(gaveUp[k].TargetX, gaveUp[k].TargetZ);
                if (sector >= 0 && sectors.TheaterOf(sector) == department) return true;
            }
            return false;
        }

        // ---- row 10: the ranking and the pick ----

        // The short departments, shortest first, with the status of this review's pick; the pick itself (the formation, as an index into s.Own,
        // and the department it is bound for) comes back in `pick` and `to`, for WriteState. The ranking is computed before the pick and the
        // status after it. Runs after Decide and after the mass and the garrison pledges are known, before the plan copies the table.
        internal static void Reinforce(TheaterPlanner.Inputs c, TheaterPlan plan, out int pick, out Theater to)
        {
            pick = -1;
            to = Theater.Unknown;
            if (!PlannerSettings.DepartmentReinforcement || c.State == null || c.Claims == null || c.Facts == null) return;
            var ranked = Rank(c);
            bool push = plan.MainObjectiveId >= 0;
            bool tried = false;
            for (int k = 0; k < ranked.Count; k++)
            {
                var d = ranked[k];
                if (push) d.Status = StatusPush;
                else if (c.State.IsLockedToward(d.Theater)) d.Status = StatusLocked;
                else if (tried) d.Status = StatusUntried;
                else
                {
                    tried = true;
                    pick = PickFor(c, plan, d.Theater);
                    d.Status = pick >= 0 ? StatusDonor : StatusNoDonor;
                    if (pick >= 0) to = d.Theater;
                }
                plan.DepartmentShort.Add(d);
            }
        }

        // The departments whose have is under their need, shortest first: by (need - have) / need, the lower Theater on a tie. Unknown is none.
        private static List<DepartmentShortfall> Rank(TheaterPlanner.Inputs c)
        {
            var s = c.S;
            var list = new List<DepartmentShortfall>();
            var seen = new HashSet<Theater>();
            for (int k = 0; k < c.Sectors.Count; k++)
            {
                Theater department = c.Sectors.TheaterOf(k);
                if (department == Theater.Unknown || !seen.Add(department)) continue;
                float need = TheaterPlanner.DepartmentNeed(c, department);
                float have = Have(s, c.Sectors, c.Claims, department);
                if (need > 0f && have < need) list.Add(new DepartmentShortfall { Theater = department, Need = need, Have = have });
            }
            list.Sort((a, b) =>
            {
                int order = ((b.Need - b.Have) / b.Need).CompareTo((a.Need - a.Have) / a.Need);
                return order != 0 ? order : ((int)a.Theater).CompareTo((int)b.Theater);
            });
            return list;
        }

        // One formation for the department, or -1 (nodonor). The target is the department's neediest facing sector's centre.
        private static int PickFor(TheaterPlanner.Inputs c, TheaterPlan plan, Theater department)
        {
            var s = c.S;
            if (!NeediestFacingSector(s, c.Sectors, c.Facts, c.Claims, department, out int sector)) return -1;
            c.Sectors.Centroid(sector, out float tx, out float tz);
            var pool = new List<int>();
            for (int i = 0; i < s.Own.Count; i++)
                if (InPool(c, i, department)) pool.Add(i);
            int pick = TheaterPlanner.ReinforcementPick(c, tx, tz, pool, null, out float days);
            if (pick < 0) return -1;
            c.Claims[pick].Claimant = Claimant.DepartmentMove;
            c.Claims[pick].SectorId = -1;
            c.Claims[pick].Share = 0f;
            c.Claims[pick].TargetX = tx;
            c.Claims[pick].TargetZ = tz;
            c.Claims[pick].Reason = null;
            plan.DepartmentTransitions.Add(new DepartmentTransition
            {
                InstanceId = s.Own[pick].InstanceId, Name = s.Own[pick].Name, From = HomeOf(c, pick), To = department,
                TargetX = tx, TargetZ = tz, Days = days, State = StatePicked
            });
            return pick;
        }

        // Who may be given to a short department (requirement-claims, row 10's pool): claimed None, so never a reserve, the mass, the covering
        // force, the capital guard, a recovering or a busy formation; massable; no depot builder of either kind; none of the formations other
        // owners hold (a D8 secession pledge, a retained G8 post incumbent, a scout on an active mission); standing in a department, and not the
        // short one; and with no give-up on the short department's sectors.
        internal static bool InPool(TheaterPlanner.Inputs c, int i, Theater department)
        {
            var s = c.S;
            var f = s.Own[i];
            if (c.Claims[i].Claimant != Claimant.None || !TheaterPlanner.MassEligible(f, s.MinMoraleForMovement)) return false;
            if (ReserveRules.IsHeldBuilder(c.Held, f.InstanceId) || (c.GarrisonReserved != null && c.GarrisonReserved[i])) return false;
            if (Holds(c.GarrisonIncumbents, f.InstanceId) || Holds(c.ReconIncumbents, f.InstanceId)) return false;
            Theater standing = StandingDepartment(s, c.Sectors, i);
            return standing != Theater.Unknown && standing != department && !GaveUpIn(c.GaveUp, f.InstanceId, c.Sectors, department);
        }

        private static bool Holds(IReadOnlyCollection<int> ids, int instanceId)
        {
            if (ids == null) return false;
            foreach (int id in ids)
                if (id == instanceId) return true;
            return false;
        }

        // ---- the state ----

        // See the header. `picked` and `to` are the formation row 10 gave a department this review (-1 none). Appends the Arrived and GaveUp
        // transitions to the plan. A review that saw no formation of ours (a degraded read, or the units not loaded yet after a load) leaves the
        // record alone, or the load would free the lock.
        internal static void WriteState(TheaterPlanner.Inputs c, TheaterPlan plan, int picked, Theater to)
        {
            var state = c.State;
            if (state == null) return;
            if (!PlannerSettings.DepartmentReinforcement)
            {
                state.SetDepartmentMoves(null);
                return;
            }
            var s = c.S;
            if (s.Own.Count == 0) return;
            var next = new List<KeyValuePair<string, Theater>>();
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                Theater receiver = i == picked ? to : state.DepartmentMoveOf(f.UnitKey);
                if (receiver == Theater.Unknown) continue;
                if (c.Claims[i].Claimant == Claimant.DepartmentMove || !TheaterPlanner.Movable(f))
                {
                    next.Add(new KeyValuePair<string, Theater>(f.UnitKey, receiver));
                    continue;
                }
                if (GaveUpIn(c.GaveUp, f.InstanceId, c.Sectors, receiver)) plan.DepartmentTransitions.Add(Transition(c, i, receiver, StateGaveUp));
                else if (StandsInFacingSector(s, c.Sectors, c.Facts, i, receiver))
                {
                    plan.DepartmentTransitions.Add(Transition(c, i, receiver, StateArrived));
                    state.Transfer(f.UnitKey, receiver);
                }
            }
            state.SetDepartmentMoves(next);
        }

        private static DepartmentTransition Transition(TheaterPlanner.Inputs c, int i, Theater to, string state)
        {
            var f = c.S.Own[i];
            return new DepartmentTransition
            {
                InstanceId = f.InstanceId, Name = f.Name, From = HomeOf(c, i), To = to, TargetX = f.X, TargetZ = f.Z, Days = -1f, State = state
            };
        }

        // The department the formation is leaving: its home, which only an arrival changes (where it stands when the planner has none).
        private static Theater HomeOf(TheaterPlanner.Inputs c, int i)
        {
            Theater home = c.State.DepartmentOf(c.S.Own[i].UnitKey);
            return home != Theater.Unknown ? home : StandingDepartment(c.S, c.Sectors, i);
        }

        // ---- telemetry ----

        // The [DepartmentReinforcement] row of one transition: the formation (quoted), the departments it left and went to, the target and the
        // march days of the pick (- on the other steps), and the step.
        internal static string Row(DepartmentTransition t, int alliance)
        {
            var inv = CultureInfo.InvariantCulture;
            return "[DepartmentReinforcement] alliance=" + alliance
                + " unit=" + TelemetryLegacyParser.Quote(t.Name ?? t.InstanceId.ToString(inv))
                + " from=" + TelemetryLegacyParser.Quote(t.From.ToString()) + " to=" + TelemetryLegacyParser.Quote(t.To.ToString())
                + " target=" + t.TargetX.ToString("0", inv) + "," + t.TargetZ.ToString("0", inv)
                + " days=" + (t.Days >= 0f ? t.Days.ToString("0.0", inv) : "-")
                + " state=" + t.State;
        }

        // The `deptShort=` field of the [Reserves] row: "<Theater>:<need>/<have>:<status>" for each short department, shortest first, "none" with
        // none.
        internal static string ShortText(TheaterPlan plan)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            foreach (DepartmentShortfall d in plan.DepartmentShort)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(d.Theater).Append(':').Append(d.Need.ToString("0", inv)).Append('/').Append(d.Have.ToString("0", inv)).Append(':').Append(d.Status);
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }
    }
}
