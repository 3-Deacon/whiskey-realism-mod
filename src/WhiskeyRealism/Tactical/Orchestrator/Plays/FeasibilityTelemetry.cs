using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14 requirement-telemetry (core rows), through the TelemetryRouter tuning-log sink: TacticalPlanSelection, TacticalManeuverFeasibility,
    // TacticalManeuverBody, TacticalReplan, TacticalCrossingWatch, and the OnceLogs of the feasibility layer. Every row builds its strings only
    // after the router has said the profile wants it, and the per-candidate and per-body rows are throttled by what changed, so the stream
    // is bounded by the evaluation's own state changes.
    internal static class FeasibilityTelemetry
    {
        private const double CandidateRowSpacingHours = 0.05, CrossingRowSpacingHours = 0.1;
        private const float PenaltyBucket = 0.1f;

        private static readonly double[][] CandidateAt = { NewHours(), NewHours() };
        private static readonly int[][] CandidateState = { NewStates(), NewStates() };
        private static readonly int[][] BodyState = { new int[ManeuverFeasibility.PlanSlots * ManeuverFeasibility.MaxBodies], new int[ManeuverFeasibility.PlanSlots * ManeuverFeasibility.MaxBodies] };
        private static readonly double[] CrossingAt = { double.NegativeInfinity, double.NegativeInfinity };

        static FeasibilityTelemetry()
        {
            BattleScope.Register("feasibility-telemetry", Reset);
        }

        private static double[] NewHours()
        {
            var hours = new double[ManeuverFeasibility.PlanSlots];
            for (int i = 0; i < hours.Length; i++) hours[i] = double.NegativeInfinity;
            return hours;
        }

        private static int[] NewStates()
        {
            var states = new int[ManeuverFeasibility.PlanSlots];
            for (int i = 0; i < states.Length; i++) states[i] = -1;
            return states;
        }

        private static void Reset()
        {
            for (int a = 0; a < 2; a++)
            {
                CandidateAt[a] = NewHours();
                CandidateState[a] = NewStates();
                Array.Clear(BodyState[a], 0, BodyState[a].Length);
                CrossingAt[a] = double.NegativeInfinity;
            }
        }

        private static bool Wanted(TelemetryCategory category)
            => TelemetryRouter.ShouldEmit(TelemetryRouter.CurrentProfile, TelemetryLayer.Tactical, category);

        private static int Side(int alliance) => alliance == 1 ? 1 : 0;

        // ---- TacticalPlanSelection ----------------------------------------------------------------------------

        /// <summary>One row per pick or kept plan, with the flag off too (so an on and an off run compare): who was offered, who won and why.</summary>
        public static void PlanSelection(int alliance, string trigger, bool kept, BattleStance stance, float odds, SelectionReport report, BattlePlanId winner,
            bool mirrored, double ledgerAgeHours, double nowHours)
        {
            try
            {
                if (report == null || !Wanted(TelemetryCategory.Decision)) return;
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalPlanSelection", TelemetrySeverity.Info,
                    ev =>
                    {
                        ev.WithSide(alliance)
                            .WithDecision("TacticalPlanSelection", winner.ToString(), "side=" + alliance + "|trigger=" + trigger + "|winner=" + winner)
                            .WithField("trigger", trigger ?? "unknown")
                            .WithField("kept", kept)
                            .WithField("stance", stance.ToString())
                            .WithField("odds", odds)
                            .WithField("winner", winner.ToString())
                            .WithField("mirrored", mirrored)
                            .WithField("candidates", report.Count)
                            .WithField("preconditionFiltered", report.PreconditionFiltered)
                            .WithField("stanceFiltered", report.StanceFiltered)
                            .WithField("crestFiltered", report.CrestFiltered)
                            .WithField("feasibilityFiltered", report.FeasibilityFiltered)
                            .WithField("allImpossible", report.AllImpossible)
                            .WithField("ledgerAgeHours", double.IsInfinity(ledgerAgeHours) || double.IsNaN(ledgerAgeHours) ? -1.0 : ledgerAgeHours)
                            .WithField("doctrineHours", nowHours);
                        for (int rank = 0; rank < 3 && rank < report.Count; rank++)
                        {
                            ref readonly SelectionReport.Entry e = ref report[rank];
                            string tag = "top" + (rank + 1);
                            ev.WithField(tag + "Plan", e.Plan.ToString())
                                .WithField(tag + "Score", e.Score)
                                .WithField(tag + "FeasibilityTerm", e.Term)
                                .WithField(tag + "Verdict", e.Verdict.ToString())
                                .WithField(tag + "Reason", e.Reason ?? string.Empty);
                        }
                    });
                if (report.FeasibilityFiltered > 0 && !OnceLog.AlreadyLogged("tactical-battle-core:feasibility-filtered"))
                {
                    string plan = "unknown", reason = "unknown";
                    for (int i = 0; i < report.Count; i++)
                    {
                        if (!report[i].Skipped) continue;
                        plan = report[i].Plan.ToString();
                        reason = report[i].Reason ?? "unknown";
                        break;
                    }
                    OnceLog.Info("tactical-battle-core:feasibility-filtered", "[Feasibility] first plan filtered: plan=" + plan + " reason=" + reason);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:feasibility-telemetry", "[Feasibility] plan-selection row threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // ---- TacticalManeuverFeasibility ----------------------------------------------------------------------

        /// <summary>
        /// The rows of a published evaluation: one per candidate whose verdict or penalty bucket changed (at most one per 0.05 doctrine-h each), the
        /// bodies of the selected plan and of the best-scored others whose penalty bucket or route source changed, and one <c>plan=all</c> summary.
        /// </summary>
        public static void Published(int alliance, FeasibilityLedger ledger, int slices, bool forced, int searches, int legsSampled, int legOverflow, int confirmQueued,
            double nowHours)
        {
            try
            {
                int side = Side(alliance);
                int impossible = 0, risky = 0;
                for (int rank = 0; rank < ledger.CandidateCount; rank++)
                {
                    if (!ledger.TryGetAt(rank, out CandidateResult r)) break;
                    if (r.Verdict == FeasibilityVerdict.Impossible) impossible++;
                    else if (r.Verdict == FeasibilityVerdict.Risky) risky++;
                    EmitCandidate(side, ledger, in r, nowHours);
                    bool top = rank < ManeuverFeasibility.ConfirmCandidates || (ledger.HasSelection && r.Plan == ledger.SelectedPlan);
                    if (top) EmitBodies(side, ledger, in r, null, null, nowHours);
                }
                if (!OnceLog.AlreadyLogged("tactical-battle-core:feasibility-first"))
                    OnceLog.Info("tactical-battle-core:feasibility-first", "[Feasibility] first evaluation: candidates=" + ledger.CandidateCount + " impossible=" + impossible + " risky=" + risky);

                if (!Wanted(TelemetryCategory.Decision)) return;
                int candidates = ledger.CandidateCount;
                bool selected = ledger.HasSelection;
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalManeuverFeasibility", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(alliance)
                        .WithDecision("TacticalManeuverFeasibility", "all", "side=" + alliance + "|summary")
                        .WithField("plan", "all")
                        .WithField("candidates", candidates)
                        .WithField("impossible", impossible)
                        .WithField("risky", risky)
                        .WithField("selected", selected)
                        .WithField("slices", slices)
                        .WithField("forced", forced)
                        .WithField("queries", searches)
                        .WithField("legsSampled", legsSampled)
                        .WithField("legOverflow", legOverflow)
                        .WithField("confirmQueued", confirmQueued)
                        .WithField("doctrineHours", nowHours));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:feasibility-telemetry", "[Feasibility] evaluation row threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void EmitCandidate(int side, FeasibilityLedger ledger, in CandidateResult r, double nowHours)
        {
            int slot = (int)r.Plan;
            if (slot < 0 || slot >= ManeuverFeasibility.PlanSlots) return;
            int state = (int)r.Verdict * 100 + (int)Math.Floor(r.Penalty / PenaltyBucket);
            bool first = CandidateState[side][slot] < 0;
            if (!first && (state == CandidateState[side][slot] || nowHours - CandidateAt[side][slot] < CandidateRowSpacingHours)) return;
            CandidateState[side][slot] = state;
            CandidateAt[side][slot] = nowHours;
            if (!Wanted(TelemetryCategory.Decision)) return;
            BattlePlanId plan = r.Plan;
            bool mirrored = r.Mirrored;
            string verdict = r.Verdict.ToString(), reason = r.Reason ?? string.Empty;
            float penalty = r.Penalty;
            int bodies = r.BodyCount;
            bool selected = ledger.HasSelection && plan == ledger.SelectedPlan;
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalManeuverFeasibility", TelemetrySeverity.Info,
                ev => ev
                    .WithSide(side)
                    .WithDecision("TacticalManeuverFeasibility", verdict, "side=" + side + "|plan=" + plan + "|verdict=" + verdict)
                    .WithField("plan", plan.ToString())
                    .WithField("mirrored", mirrored)
                    .WithField("verdict", verdict)
                    .WithField("penalty", penalty)
                    .WithField("reason", reason)
                    .WithField("bodies", bodies)
                    .WithField("selected", selected)
                    .WithField("doctrineHours", nowHours));
        }

        // ---- TacticalManeuverBody -----------------------------------------------------------------------------

        /// <summary>
        /// The bodies of one candidate: one row per body whose penalty bucket or route source changed since its last row, or all of them when a
        /// monitor <paramref name="assumption"/> or <paramref name="suppression"/> is named (the row of a broken or suppressed assumption).
        /// </summary>
        public static void Bodies(int alliance, FeasibilityLedger ledger, in CandidateResult r, string assumption, string suppression, double nowHours)
            => EmitBodies(Side(alliance), ledger, in r, assumption, suppression, nowHours);

        private static void EmitBodies(int side, FeasibilityLedger ledger, in CandidateResult r, string assumption, string suppression, double nowHours)
        {
            int slot = (int)r.Plan;
            if (slot < 0 || slot >= ManeuverFeasibility.PlanSlots || !Wanted(TelemetryCategory.Decision)) return;
            ReadOnlySpan<BodyEstimate> bodies = ledger.Bodies(in r);
            ReadOnlySpan<MemberEstimate> members = ledger.Members(in r);
            float bar = ManeuverFeasibility.Bar(in ledger.Job.Input.Army);
            for (int b = 0; b < bodies.Length && b < ManeuverFeasibility.MaxBodies; b++)
            {
                BodyEstimate body = bodies[b];
                int state = 1 + (int)Math.Floor(body.Penalty / PenaltyBucket) * 4 + (body.RouteConfirmed ? 2 : 0) + (body.RouteEstimated ? 1 : 0);
                int at = slot * ManeuverFeasibility.MaxBodies + b;
                bool named = assumption != null || suppression != null;
                if (!named && BodyState[side][at] == state) continue;
                BodyState[side][at] = state;
                float releaseHours = 0f;
                for (int m = 0; m < members.Length; m++)
                    if (members[m].Body == b && string.Equals(members[m].ChildId, body.ChildId, StringComparison.Ordinal)) releaseHours = members[m].ReleaseHours;
                BattlePlanId plan = r.Plan;
                string childId = body.ChildId ?? string.Empty, kind = body.Envelops ? "envelops" : (body.Main ? "main" : body.Kind.ToString());
                string source = body.RouteConfirmed ? "navmesh" : "estimate";
                string why = assumption ?? string.Empty, suppressed = suppression ?? "none";
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalManeuverBody", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(side)
                        .WithDecision("TacticalManeuverBody", suppressed, "side=" + side + "|plan=" + plan + "|body=" + body.Body)
                        .WithField("plan", plan.ToString())
                        .WithField("body", body.Body)
                        .WithField("child", childId)
                        .WithField("kind", kind)
                        .WithField("strength", body.Strength)
                        .WithField("routeUnits", body.RouteCost)
                        .WithField("routeSource", source)
                        .WithField("marchVia", body.MarchVia)
                        .WithField("attackVia", body.AttackVia)
                        .WithField("marchViaState", body.MarchCrossing.ToString())
                        .WithField("attackViaState", body.AttackCrossing.ToString())
                        .WithField("strikeContested", body.StrikeContested)
                        .WithField("courierHours", body.CourierHours)
                        .WithField("marchHours", body.MarchHours)
                        .WithField("inwardSurplusHours", body.InwardSurplusHours)
                        .WithField("strikeHours", body.StrikeHours)
                        .WithField("desiredStrikeHours", body.DesiredStrikeHours)
                        .WithField("releaseHours", releaseHours)
                        .WithField("enemyShiftHours", body.EnemyShiftHours)
                        .WithField("windowHours", body.ExposureWindowHours)
                        .WithField("pin", body.Pin)
                        .WithField("enemyAgainst", body.EnemyAgainst)
                        .WithField("isolatedOdds", body.IsolatedOdds)
                        .WithField("bar", bar)
                        .WithField("ownRatio", body.OwnRatio)
                        .WithField("daylight", body.Daylight)
                        .WithField("detail", body.Detail)
                        .WithField("contested", body.Contested)
                        .WithField("approach", body.Approach)
                        .WithField("exposure", body.Exposure)
                        .WithField("turn", body.Turn)
                        .WithField("penalty", body.Penalty)
                        .WithField("assumption", why)
                        .WithField("suppression", suppressed)
                        .WithField("doctrineHours", nowHours));
            }
        }

        // ---- TacticalReplan -----------------------------------------------------------------------------------

        /// <summary>A replan or a kept plan, structured and never deduped (today's deduped [TacticalReplan] log line stays for the main log).</summary>
        public static void Replan(int alliance, string trigger, string assumption, bool kept, BattlePlanId newPlan, bool mirrored, double nowHours)
        {
            try
            {
                if (string.Equals(trigger, "ManeuverAssumptionBroken", StringComparison.Ordinal) && !OnceLog.AlreadyLogged("tactical-battle-core:feasibility-replan"))
                    OnceLog.Info("tactical-battle-core:feasibility-replan", "[Feasibility] first assumption replan: assumption=" + (assumption ?? "unknown"));
                if (!Wanted(TelemetryCategory.Decision)) return;
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalReplan", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(alliance)
                        .WithDecision("TacticalReplan", trigger ?? "unknown", "side=" + alliance + "|trigger=" + trigger + "|plan=" + newPlan + "|at=" + nowHours.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture))
                        .WithField("trigger", trigger ?? "unknown")
                        .WithField("assumption", assumption ?? string.Empty)
                        .WithField("kept", kept)
                        .WithField("newPlan", newPlan.ToString())
                        .WithField("mirrored", mirrored)
                        .WithField("doctrineHours", nowHours));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:feasibility-telemetry", "[Feasibility] replan row threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // ---- TacticalCrossingWatch ----------------------------------------------------------------------------

        /// <summary>The side's crossing picture, every 0.1 doctrine-h.</summary>
        public static void CrossingWatchState(int alliance, CrossingWatch watch, RiverRegions regions, int lookRequests, double nowHours)
        {
            try
            {
                int side = Side(alliance);
                if (watch == null || regions == null || nowHours - CrossingAt[side] < CrossingRowSpacingHours) return;
                CrossingAt[side] = nowHours;
                if (!Wanted(TelemetryCategory.State)) return;
                int held = watch.CountBy(CrossingState.Held), watched = watch.CountBy(CrossingState.Watched);
                int clear = watch.CountBy(CrossingState.Clear), unknown = watch.CountBy(CrossingState.Unknown);
                int probes = watch.ProbesLastTick, regionCount = regions.RegionCount, links = regions.LinkCount;
                bool regionsTruncated = regions.RegionsTruncated, linksTruncated = regions.LinksTruncated;
                // How stale the picture is: the age of the oldest perceived crossing, and how many were never perceived.
                double oldestAge = 0.0;
                int neverPerceived = 0;
                for (int link = 0; link < links; link++)
                {
                    double at = watch.LastPerceivedHours(link);
                    if (double.IsNegativeInfinity(at)) neverPerceived++;
                    else if (nowHours - at > oldestAge) oldestAge = nowHours - at;
                }
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.State, "TacticalCrossingWatch", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(alliance)
                        .WithField("held", held)
                        .WithField("watched", watched)
                        .WithField("clear", clear)
                        .WithField("unknown", unknown)
                        .WithField("probes", probes)
                        .WithField("lookRequests", lookRequests)
                        .WithField("regions", regionCount)
                        .WithField("links", links)
                        .WithField("regionsTruncated", regionsTruncated)
                        .WithField("linksTruncated", linksTruncated)
                        .WithField("oldestPerceivedAgeHours", oldestAge)
                        .WithField("neverPerceived", neverPerceived)
                        .WithField("doctrineHours", nowHours));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:feasibility-telemetry", "[Feasibility] crossing-watch row threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        /// <summary>The first wing routed through a chosen crossing (BC11 carries the ledger's via on its March order).</summary>
        public static void FirstVia(CrossingState state)
        {
            if (OnceLog.AlreadyLogged("tactical-battle-core:feasibility-via")) return;
            OnceLog.Info("tactical-battle-core:feasibility-via", "[Feasibility] first wing routed through a chosen crossing: state=" + state);
        }
    }
}
