using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Strategic.Planner
{
    // The [Reserves] row (D11 requirement-telemetry, foresight F1): once per review, what the planner holds against what it needs, and what
    // vanilla's own defence did with the formations the planner withheld. Every later foresight criterion counts review days on this row
    // ("a review day is a Full-mode review, a [Reserves] row of the alliance"), and the smoke reads the covering force's and the
    // departments' shortfalls from it. Pure over the review's snapshot, plan and allocation; the runtime writes it (CampaignPlannerRuntime).
    //
    //   capital=<held>/<floor>   the CapitalGuard formations' strength against the plan's capital floor
    //   covering=<held>/<need>   the covering force's counted strength against the strength it is sized to (0/0 with no force; foresight F6b-2)
    //   hold=<sector:held/need>  at most MaxHoldSectors sectors, largest need first (the lower id on a tie): the hold each has by the roles
    //                            assigned against max(HoldNeed, the garrison minimum), the allocator's own sums
    //   holdReserve=<unit+...>   the formations the allocation gave Hold hold-reserve, at most MaxHoldReserve names in snapshot order, quoted as one
    //                            value ("-" with none): the hold reserves the covering force's members are chosen from (F6b-2)
    //   committed=<n>/<men>      the formations committed this review (a Mass claim with the committed reason, a Committed claim) and their
    //                            HoldStrength (foresight F7): a push's corps kept through a readiness dip, and an ended push's corps waiting
    //   endedListed=<n>          yesterday's CommitmentEnded formations that are neither a MainEffort nor a Support today and are in vanilla's
    //                            offensive list again: reported, not bounded (vanilla's job 4 may list a corps again, a legitimate act)
    //   endedMarching=<n>        yesterday's CommitmentEnded formations that yesterday's allocation held as committed-wait (the ones the stop was
    //                            for), that are a Hold today and that still show a path or a waiting order: 0 when the stop works
    //   dept=<theater:have,...>  each department's HoldStrength over its formations that count toward coverage; "none" when none do
    //   enemyOnOwnSoil           live land beliefs (EnemyTraits.OnOwnSoil, not afloat) the side holds today
    //   vanillaDefActive         own formations in a vanilla defensive operation today
    //   vanillaDefWithheld       formations now in a vanilla defensive operation that yesterday's allocation withheld as Reserve, CapitalGuard,
    //                            DepotBuilder, Recon or Recover (a violation of the hold the planner keeps)
    //   vanillaDefHoldOrCovering the same for a withheld Hold (vanilla's defence may take it: the same-sector exception) and, from foresight
    //                            F6b, a covering-force member
    //   massInVanillaDef         members of yesterday's mass now in a vanilla defensive operation
    // The last three read YESTERDAY's allocation and plan, which the runtime captures before today's Publish overwrites the working copy
    // (as AgeRecon reads it); on the first review they read 0. enemyOnOwnSoil= and vanillaDefActive= (foresight F2) are today's picture:
    // criterion-interception-restored counts the review days on which both are above 0.
    internal static class ReservesRow
    {
        internal const int MaxHoldSectors = 6, MaxHoldReserve = 12;

        internal static string Build(CampaignSnapshot s, SectorModel sectors, TheaterPlan plan, Allocation a, PlannerState state, Allocation previous, TheaterPlan previousPlan)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("[Reserves] alliance=").Append(s.Alliance).Append(" date=").Append(ReviewCadenceCatchUp.FormatDate(s.DaySerial))
              .Append(" capital=").Append(ForceAllocator.RoleStrength(s, a, FormationRole.CapitalGuard).ToString("0", inv))
              .Append('/').Append(plan.CapitalFloorStrength.ToString("0", inv))
              .Append(" covering=").Append(plan.Covering.Held.ToString("0", inv)).Append('/').Append(plan.Covering.Need.ToString("0", inv))
              .Append(" hold=").Append(HoldShares(s, sectors, plan, a, inv))
              .Append(" holdReserve=").Append(HoldReserve(s, a));
            CountCommitment(s, plan, a, previous, previousPlan, out int committed, out float men, out int endedListed, out int endedMarching);
            sb.Append(" committed=").Append(committed).Append('/').Append(men.ToString("0", inv))
              .Append(" endedListed=").Append(endedListed)
              .Append(" endedMarching=").Append(endedMarching)
              .Append(" dept=").Append(DepartmentStrengths(s, sectors, state, inv))
              .Append(" enemyOnOwnSoil=").Append(EnemiesOnOwnSoil(s))
              .Append(" vanillaDefActive=").Append(InVanillaDefence(s));
            CountVanillaDefence(s, previous, previousPlan, out int withheld, out int holdOrCovering, out int mass);
            sb.Append(" vanillaDefWithheld=").Append(withheld)
              .Append(" vanillaDefHoldOrCovering=").Append(holdOrCovering)
              .Append(" massInVanillaDef=").Append(mass);
            return sb.ToString();
        }

        // The commitment counts of the row. committed is read from the published table: a continuing push's committed corps are Mass claims with
        // the committed reason, an ended push's waiting corps Committed claims (a corps of an ended push that the new mass took is an ordinary member
        // and is not counted). The two ended counts read YESTERDAY's CommitmentEnded, allocation and today's snapshot.
        private static void CountCommitment(CampaignSnapshot s, TheaterPlan plan, Allocation a, Allocation previous, TheaterPlan previousPlan,
                                            out int committed, out float men, out int endedListed, out int endedMarching)
        {
            committed = endedListed = endedMarching = 0;
            men = 0f;
            for (int k = 0; k < plan.Claims.Count; k++)
            {
                var claim = plan.Claims[k];
                bool isCommitted = claim.Claimant == Claimant.Committed
                    || (claim.Claimant == Claimant.Mass && string.Equals(claim.Reason, ClaimTable.ReasonCommitted, StringComparison.Ordinal));
                int i = isCommitted ? s.IndexOfOwn(claim.InstanceId) : -1;
                if (i < 0) continue;
                committed++;
                men += ReserveRules.HoldStrength(s.Own[i]);
            }
            if (previousPlan == null) return;
            for (int k = 0; k < previousPlan.CommitmentEnded.Count; k++)
            {
                int id = previousPlan.CommitmentEnded[k];
                int i = s.IndexOfOwn(id);
                if (i < 0 || !a.TryGet(id, out var today)) continue;
                var flags = s.Own[i].Flags;
                if (today.Role != FormationRole.MainEffort && today.Role != FormationRole.Support && (flags & FormationFlags.InOffensiveOps) != 0) endedListed++;
                bool waited = previous != null && previous.TryGet(id, out var was) && was.Role == FormationRole.Hold
                    && string.Equals(was.Reason, ForceAllocator.ReasonCommittedWait, StringComparison.Ordinal);
                if (waited && today.Role == FormationRole.Hold && (flags & (FormationFlags.HasPath | FormationFlags.MoveQueued)) != 0) endedMarching++;
            }
        }

        // "sector:held/need,..." over the sectors with the largest need, the lower id first among equals. The need is the allocator's
        // Target: max(the plan's HoldNeed, the garrison minimum), so every sector has one.
        private static string HoldShares(CampaignSnapshot s, SectorModel sectors, TheaterPlan plan, Allocation a, CultureInfo inv)
        {
            var need = new float[sectors.Count];
            for (int k = 0; k < need.Length; k++) need[k] = plan.MinGarrisonStrength;
            for (int k = 0; k < plan.Sectors.Count; k++)
            {
                var sp = plan.Sectors[k];
                if (sp != null && sp.SectorId >= 0 && sp.SectorId < need.Length) need[sp.SectorId] = Math.Max(need[sp.SectorId], sp.HoldNeed);
            }
            var order = new List<int>(need.Length);
            for (int k = 0; k < need.Length; k++) order.Add(k);
            order.Sort((x, y) => need[y] != need[x] ? need[y].CompareTo(need[x]) : x.CompareTo(y));
            var sb = new StringBuilder();
            for (int k = 0; k < order.Count && k < MaxHoldSectors; k++)
            {
                int sector = order[k];
                if (sb.Length > 0) sb.Append(',');
                sb.Append(sector).Append(':').Append(ForceAllocator.CountedHold(s, sectors, a, sector).ToString("0", inv)).Append('/').Append(need[sector].ToString("0", inv));
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        // The unit keys of the formations with the role Hold and the reason hold-reserve, snapshot order, the first MaxHoldReserve, joined by '+' and
        // quoted as one value. The same keys the [CampaignMoverIssue] and [CoveringForce] rows name.
        private static string HoldReserve(CampaignSnapshot s, Allocation a)
        {
            var sb = new StringBuilder();
            int count = 0;
            for (int i = 0; i < s.Own.Count && count < MaxHoldReserve; i++)
            {
                if (!a.TryGet(s.Own[i].InstanceId, out var x) || x.Role != FormationRole.Hold
                    || !string.Equals(x.Reason, ForceAllocator.ReasonHoldReserve, StringComparison.Ordinal)) continue;
                if (sb.Length > 0) sb.Append('+');
                sb.Append(s.Own[i].UnitKey ?? s.Own[i].InstanceId.ToString(CultureInfo.InvariantCulture));
                count++;
            }
            return TelemetryLegacyParser.Quote(sb.ToString());
        }

        // Each department's HoldStrength over its formations that count toward coverage (the player's own command and a CIC side count
        // nothing): the shipped D6 "have" (TheaterPlanner.DepartmentHoldMet), by home department, read for question-capital-share's
        // comparison against the covering need. A formation of no known department is where it stands.
        private static string DepartmentStrengths(CampaignSnapshot s, SectorModel sectors, PlannerState state, CultureInfo inv)
        {
            var have = new float[6];
            var any = new bool[6];
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (!WlChainRules.CountsTowardCoverage(f.Chain)) continue;
                var department = state != null ? state.DepartmentOf(f.UnitKey) : Theater.Unknown;
                if (department == Theater.Unknown) department = sectors.TheaterOf(ReserveRules.HoldSectorOf(sectors, f));
                int d = (int)department;
                if (department == Theater.Unknown || d < 0 || d >= have.Length) continue;
                have[d] += ReserveRules.HoldStrength(f);
                any[d] = true;
            }
            var sb = new StringBuilder();
            for (int d = 0; d < have.Length; d++)
            {
                if (!any[d]) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append((Theater)d).Append(':').Append(have[d].ToString("0", inv));
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        // Live land beliefs standing on the side's own soil (the test 13601-13603 uses for an enemy vanilla's defence may answer).
        private static int EnemiesOnOwnSoil(CampaignSnapshot s)
        {
            int count = 0;
            for (int i = 0; i < s.Beliefs.Count; i++)
            {
                var b = s.Beliefs[i];
                if (ReserveRules.Live(b) > 0f && !ReserveRules.Afloat(b) && (b.Traits & EnemyTraits.OnOwnSoil) != 0) count++;
            }
            return count;
        }

        private static int InVanillaDefence(CampaignSnapshot s)
        {
            int count = 0;
            for (int i = 0; i < s.Own.Count; i++)
                if ((s.Own[i].Flags & FormationFlags.InDefensiveOps) != 0) count++;
            return count;
        }

        // Today's formations in a vanilla defensive operation, judged by what yesterday's allocation made of them.
        private static void CountVanillaDefence(CampaignSnapshot s, Allocation previous, TheaterPlan previousPlan, out int withheld, out int holdOrCovering, out int mass)
        {
            withheld = holdOrCovering = mass = 0;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if ((f.Flags & FormationFlags.InDefensiveOps) == 0) continue;
                if (previous != null && previous.TryGet(f.InstanceId, out var was))
                {
                    if (WithheldAsReserve(was.Role)) withheld++;
                    else if ((was.Role == FormationRole.Hold && was.Withheld) || ForceAllocator.IsCoveringMember(in was)) holdOrCovering++;
                }
                if (previousPlan != null && previousPlan.MassInstanceIds.Contains(f.InstanceId)) mass++;
            }
        }

        // The roles whose withhold is the planner's claim that vanilla's defence must not spend the formation.
        private static bool WithheldAsReserve(FormationRole role)
        {
            return role == FormationRole.Reserve || role == FormationRole.CapitalGuard || role == FormationRole.DepotBuilder
                || role == FormationRole.Recon || role == FormationRole.Recover;
        }
    }
}
