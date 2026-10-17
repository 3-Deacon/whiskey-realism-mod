using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14O requirement-telemetry: TacticalObjectivePressure (State) and TacticalMainEffort (Decision), through the TelemetryRouter tuning-log sink.
    // Both are bounded by their own clocks: the pressure row by 0.1 doctrine-h, the main-effort row by a change of the objective, a chain contest,
    // or the heartbeat below.
    internal static partial class FeasibilityTelemetry
    {
        private const double PressureRowSpacingHours = 0.1, MainEffortHeartbeatHours = 0.25;

        private static readonly double[] PressureAt = { double.NegativeInfinity, double.NegativeInfinity };
        private static readonly double[] MainEffortAt = { double.NegativeInfinity, double.NegativeInfinity };
        private static readonly string[] MainEffortId = new string[2];
        // Chain contests counted since the last TacticalMainEffort row (the row reads and clears them).
        private static readonly int[] PendingContests = new int[2];

        private static void ResetObjectives(int side)
        {
            PressureAt[side] = double.NegativeInfinity;
            MainEffortAt[side] = double.NegativeInfinity;
            MainEffortId[side] = null;
            PendingContests[side] = 0;
        }

        /// <summary>The pressure row is due every 0.1 doctrine-h (the time is taken when it is).</summary>
        internal static bool PressureRowDue(int side, double nowHours)
        {
            if (nowHours - PressureAt[side] < PressureRowSpacingHours) return false;
            PressureAt[side] = nowHours;
            return true;
        }

        /// <summary>
        /// The main-effort row is due when the pick's objective changes (a first pick, a new objective, or a pick that lapsed and came back), when the
        /// chain contest re-issued a move since the last row, and every 0.25 doctrine-h while an objective is picked: the director re-selects on every
        /// heavy tick, so the heartbeat is the row "at each selection" at a bounded rate. The state is taken when it is, and the contests the row reports
        /// (<paramref name="contests"/>: all since the last row) are cleared with it.
        /// </summary>
        internal static bool MainEffortRowDue(int side, string objectiveId, int newContests, double nowHours, out int contests)
        {
            contests = 0;
            PendingContests[side] += newContests;
            bool changed = !string.Equals(objectiveId, MainEffortId[side], StringComparison.Ordinal);
            MainEffortId[side] = objectiveId;
            if (objectiveId == null) return false;
            if (!changed && PendingContests[side] == 0 && nowHours - MainEffortAt[side] < MainEffortHeartbeatHours) return false;
            MainEffortAt[side] = nowHours;
            contests = PendingContests[side];
            PendingContests[side] = 0;
            return true;
        }

        /// <summary>The side's victory clock and the pressure it puts on the commander, every 0.1 doctrine-h while it has live objectives.</summary>
        public static void ObjectivePressure(int alliance, FeasibilityLedger ledger, double nowHours)
        {
            try
            {
                FeasibilityInput input = ledger.Job.Input;
                if (!input.HasClock && ledger.ObjectiveCount == 0) return;
                if (!PressureRowDue(Side(alliance), nowHours) || !Wanted(TelemetryCategory.State)) return;
                VictoryClock clock = input.Clock;
                ObjectivePressure pressure = ledger.Pressure;
                int dueChains = input.DueChainCount;
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.State, "TacticalObjectivePressure", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(alliance)
                        .WithField("sOwn", clock.ShareOwn)
                        .WithField("sEnemy", clock.ShareEnemy)
                        .WithField("bar", clock.Bar)
                        .WithField("daylightHours", clock.DaylightHours)
                        .WithField("behind", pressure.Behind)
                        .WithField("ahead", pressure.Ahead)
                        .WithField("late", pressure.Late)
                        .WithField("U", pressure.U)
                        .WithField("H", pressure.H)
                        .WithField("dueChains", dueChains)
                        .WithField("doctrineHours", nowHours));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:feasibility-telemetry", "[Feasibility] objective-pressure row threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        /// <summary>
        /// The main effort: the objective picked, the numbers it was picked on (the table's row, and the pick's total with the director's terms), the
        /// best feasible objective by value and the nearest one the side does not hold, the plan's band, the screens drawn, and the chain moves
        /// contested since the last row (read and cleared here).
        /// </summary>
        public static void MainEffort(int alliance, FeasibilityLedger ledger, in ObjectivePick pick, int band, int secondaryScreens, double nowHours)
        {
            try
            {
                int side = Side(alliance);
                MoveContestState contest = MoveContestState.ForAlliance(alliance);
                int contested = contest != null ? contest.TakeChainReissues() : 0;
                if (!MainEffortRowDue(side, pick.Has ? pick.Objective.ObjectiveId : null, contested, nowHours, out int chainContests) || !Wanted(TelemetryCategory.Decision)) return;
                string objective = pick.Objective.ObjectiveId;
                ObjectiveEstimate row = pick.Row;
                float value = pick.HasRow ? row.Value : pick.Objective.Value;
                float points = PointsOf(in ledger.Job.Input, objective);
                ObjectiveContrast contrast = ObjectiveScoring.Contrast(ledger, ledger.Job.ArmyX, ledger.Job.ArmyZ);
                ObjectivePressure pressure = ledger.Pressure;
                string runnerUp = pick.RunnerUpId ?? string.Empty, maxId = contrast.MaxFeasibleId ?? string.Empty, nearestId = contrast.NearestId ?? string.Empty;
                float runnerUpScore = pick.RunnerUpId != null ? pick.RunnerUpTotal : 0f;
                float risk = pick.HasRow ? row.Penalty : 0f, prior = pick.HasRow ? row.Prior : 0f, hold = pick.HasRow ? row.Hold : 0f, score = pick.Total;
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalMainEffort", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(alliance)
                        .WithDecision("TacticalMainEffort", objective, "side=" + alliance + "|objective=" + objective)
                        .WithField("objective", objective)
                        .WithField("points", points)
                        .WithField("value", value)
                        .WithField("r", risk)
                        .WithField("prior", prior)
                        .WithField("hold", hold)
                        .WithField("U", pressure.U)
                        .WithField("H", pressure.H)
                        .WithField("score", score)
                        .WithField("runnerUp", runnerUp)
                        .WithField("runnerUpScore", runnerUpScore)
                        .WithField("maxFeasibleValueObjective", maxId)
                        .WithField("maxFeasibleValue", contrast.MaxFeasibleValue)
                        .WithField("nearestObjective", nearestId)
                        .WithField("band", band)
                        .WithField("secondaryScreens", secondaryScreens)
                        .WithField("vanillaChainContest", chainContests)
                        .WithField("doctrineHours", nowHours));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:feasibility-telemetry", "[Feasibility] main-effort row threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static float PointsOf(in FeasibilityInput input, string id)
        {
            for (int i = 0; i < input.ObjectiveCount && input.Objectives != null && i < input.Objectives.Length; i++)
                if (string.Equals(input.Objectives[i].Id, id, StringComparison.Ordinal)) return input.Objectives[i].Points;
            return 0f;
        }
    }
}
