using System;
using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// What one <see cref="ArmyTickCycle"/> step did about the replan triggers (BC14): the trigger it evaluated, whether it replanned or
    /// kept the plan (the hysteresis of a broken maneuver assumption), and, when a broken assumption was supplied and did not replan,
    /// the named suppression for the TacticalManeuverBody row.
    /// </summary>
    public readonly struct ReplanOutcome
    {
        public const string SuppressedByCooldown = "cooldown", SuppressedByMargin = "margin", SuppressedByPreemption = "preempted", SuppressedByCommitment = "committed",
            SuppressedByPlanHold = "plan-hold";
        // plan-hold: the trigger fired inside ArmyReplanTriggers.PlanCommitmentBattleSeconds and is no crisis. (committed is the maneuver monitor's
        // committed wings, a different thing.)

        public ReplanOutcome(ReplanTrigger trigger, bool replanned, bool kept, string suppression)
        {
            Trigger = trigger;
            Replanned = replanned;
            Kept = kept;
            Suppression = suppression;
        }

        /// <summary>The trigger that fired this step, None when none did.</summary>
        public ReplanTrigger Trigger { get; }
        /// <summary>The plan was re-picked.</summary>
        public bool Replanned { get; }
        /// <summary>The hysteresis kept the current plan: it stays as it is and the cooldown is stamped.</summary>
        public bool Kept { get; }
        /// <summary>A static literal: why a supplied broken assumption did not replan the maneuver (cooldown, margin) or was pre-empted; null otherwise.</summary>
        public string Suppression { get; }

        public static ReplanOutcome None => default(ReplanOutcome);
    }

    /// <summary>
    /// Pure army-orchestrator tick driver. Runtime callers own telemetry and
    /// battle-object adaptation; this class only advances state and replans.
    /// </summary>
    public static class ArmyTickCycle
    {
        static ArmyTickCycle()
        {
            BattleScope.Register("army-tick-cycle", Reset);
        }

        private static readonly Dictionary<int, float> _lastReplanByAlliance = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _clockByAlliance = new Dictionary<int, float>();
        private static readonly Dictionary<int, double> _lastDoctrineNowByAlliance = new Dictionary<int, double>();

        public static void Reset()
        {
            _lastReplanByAlliance.Clear();
            _clockByAlliance.Clear();
            _lastDoctrineNowByAlliance.Clear();
        }

        /// <summary>
        /// Stamps a replan made outside <see cref="MaybeReplan(ArmyOrchestrator, float, ArmyEvidence, EnemyVisibleState, float, float, float, float, int, float, float, float, float, float, double)"/>
        /// (the coordinator's new-battle-day replan) so it counts against
        /// "Min Replan Seconds" like any other. Uses the doctrine time.
        /// </summary>
        public static void RecordReplan(int allianceId) => RecordReplan(allianceId, TacticalDoctrineTime.NowBattleSeconds);

        internal static void RecordReplan(int allianceId, double doctrineNowSeconds)
        {
            if (double.IsNaN(doctrineNowSeconds) || double.IsInfinity(doctrineNowSeconds)) return;
            _clockByAlliance[allianceId] = (float)doctrineNowSeconds;
            _lastReplanByAlliance[allianceId] = (float)doctrineNowSeconds;
            _lastDoctrineNowByAlliance[allianceId] = doctrineNowSeconds;
        }

        /// <summary>
        /// Legacy overload that defaults readiness fields to neutral values
        /// (PushReady decision). Existing call sites stay binary-compatible
        /// until DriveTickCycle wires through the new fatigue/ammo data.
        /// </summary>
        public static ReplanTrigger MaybeReplan(
            ArmyOrchestrator orchestrator,
            float deltaSeconds,
            ArmyEvidence ownEvidence,
            EnemyVisibleState enemyVisible,
            float ownActiveForce,
            float ownArmyMorale,
            float ownReservesCommittedFraction,
            float ownReinforcementsArriving,
            int minReplanSeconds) =>
            MaybeReplan(
                orchestrator, deltaSeconds, ownEvidence, enemyVisible,
                ownActiveForce, ownArmyMorale, ownReservesCommittedFraction,
                ownReinforcementsArriving, minReplanSeconds,
                ownAvgFatigue01: 0.2f,
                ownAvgAmmo01: 0.9f,
                nearestReinforcementHours: 0f,
                nearestReinforcementStrength: 0f);

        public static ReplanTrigger MaybeReplan(
            ArmyOrchestrator orchestrator,
            float deltaSeconds,
            ArmyEvidence ownEvidence,
            EnemyVisibleState enemyVisible,
            float ownActiveForce,
            float ownArmyMorale,
            float ownReservesCommittedFraction,
            float ownReinforcementsArriving,
            int minReplanSeconds,
            float ownAvgFatigue01,
            float ownAvgAmmo01,
            float nearestReinforcementHours,
            float nearestReinforcementStrength,
            float battleDeltaSeconds = -1f,
            double doctrineNowSeconds = double.NaN)
        {
            ReplanOutcome outcome = MaybeReplan(
                orchestrator, deltaSeconds, ownEvidence, enemyVisible, ownActiveForce, ownArmyMorale, ownReservesCommittedFraction,
                ownReinforcementsArriving, minReplanSeconds, ownAvgFatigue01, ownAvgAmmo01, nearestReinforcementHours,
                nearestReinforcementStrength, battleDeltaSeconds, doctrineNowSeconds, assumptionReason: null);
            return outcome.Replanned || outcome.Kept ? outcome.Trigger : ReplanTrigger.None;
        }

        /// <summary>
        /// The tick step with BC14's broken-assumption input: <paramref name="assumptionReason"/> (a static literal from the feasibility
        /// monitor, null none) raises <see cref="ReplanTrigger.ManeuverAssumptionBroken"/> when no concrete trigger fires first. That trigger
        /// replans with hysteresis (the current plan is kept unless it is gone, impossible, or beaten by the score margin), and a kept plan
        /// stamps the cooldown and the replan baseline like a replan does.
        /// </summary>
        public static ReplanOutcome MaybeReplan(
            ArmyOrchestrator orchestrator,
            float deltaSeconds,
            ArmyEvidence ownEvidence,
            EnemyVisibleState enemyVisible,
            float ownActiveForce,
            float ownArmyMorale,
            float ownReservesCommittedFraction,
            float ownReinforcementsArriving,
            int minReplanSeconds,
            float ownAvgFatigue01,
            float ownAvgAmmo01,
            float nearestReinforcementHours,
            float nearestReinforcementStrength,
            float battleDeltaSeconds,
            double doctrineNowSeconds,
            string assumptionReason)
        {
            if (orchestrator == null) return ReplanOutcome.None;

            var allianceId = orchestrator.AllianceId;
            // Doctrine clock (T8): plan age, phase age and the replan cooldown
            // all run on BATTLE time when the caller supplies it. Wall-clock
            // deltas drove the 180 s plan deadline before, so at 20x
            // compression a plan "expired" after ~10 battle-minutes and at 1x
            // every 3 wall-clock minutes. Callers pass
            // TacticalDoctrineTime.NowBattleSeconds, the coordinator's doctrine
            // clock, which is frozen overnight; a frozen value yields a zero
            // delta. Legacy callers without a battle clock keep the
            // single-clock behaviour.
            bool doctrineClock = !double.IsNaN(doctrineNowSeconds) && !double.IsInfinity(doctrineNowSeconds);
            if (doctrineClock)
            {
                double delta = 0.0;
                if (_lastDoctrineNowByAlliance.TryGetValue(allianceId, out var lastDoctrineNow) && doctrineNowSeconds > lastDoctrineNow)
                    delta = doctrineNowSeconds - lastDoctrineNow;
                _lastDoctrineNowByAlliance[allianceId] = doctrineNowSeconds;
                battleDeltaSeconds = (float)delta;
            }
            bool battleClock = battleDeltaSeconds >= 0f;
            float clockDelta = battleClock ? battleDeltaSeconds : deltaSeconds;
            // The replan cooldown runs on doctrine time itself when supplied,
            // the same clock RecordReplan stamps; otherwise on summed deltas.
            if (doctrineClock)
            {
                _clockByAlliance[allianceId] = (float)doctrineNowSeconds;
            }
            else if (clockDelta > 0f && !float.IsNaN(clockDelta) && !float.IsInfinity(clockDelta))
            {
                float currentClock;
                _clockByAlliance.TryGetValue(allianceId, out currentClock);
                _clockByAlliance[allianceId] = currentClock + clockDelta;
            }
            orchestrator.AdvancePlanAge(clockDelta);

            orchestrator.ObserveFrontGeometry(enemyVisible);
            orchestrator.ObserveGlobalOdds(ownEvidence.CurrentOdds);
            if (!orchestrator.HasPlan) return ReplanOutcome.None;

            var intent = ArmyIntentInference.Build(enemyVisible);
            orchestrator.ObserveIntent(intent);

            // ---- Sector readiness decision (fresh-troops gate) ----
            //
            // Computes whether the commander has effective force to push, OR
            // should hold for reinforcements, OR push despite fatigue, OR
            // preserve the force. Army-wide averages for now (per-sector
            // aggregation is a future refinement). Defaults make the decision
            // a no-op (PushReady) when the caller passes neutral data.
            float aggression01 = (orchestrator.CommanderPersonality.Aggression + 1f) * 0.5f;
            if (aggression01 < 0f) aggression01 = 0f;
            if (aggression01 > 1f) aggression01 = 1f;
            // Odds are the blend from "unknown" (1.0) toward the observed ratio by the intel confidence, and own / 1.0 is an enemy as strong as the
            // army. Priced against that phantom the army's effective strength (fatigue x ammo x morale, below 1) never reaches the required ratio,
            // so a cautious commander "held fatigued" at the end of the probe budget and consolidated a fresh army that had not yet met the enemy
            // (smoke 2026-10-17, the Union at 12.6 h). An enemy not yet found has no strength to price against: readiness then reads the army's own
            // health alone (the doctrine's "enemy gone, push freely" sentinel is enemy strength 0).
            bool enemyKnown = enemyVisible.IntelConfidence01 >= TacticalPhaseProgressionDoctrine.ProbeCommitMinIntelConfidence;
            float enemyStrengthForReadiness = !enemyKnown
                ? 0f
                : ownActiveForce > 0f && ownEvidence.CurrentOdds > 0.01f
                    ? ownActiveForce / ownEvidence.CurrentOdds
                    : ownActiveForce;
            var readinessInput = new TacticalSectorReadinessDoctrine.Input(
                ownRawStrength: ownActiveForce,
                avgFatigue01: ownAvgFatigue01,
                avgAmmo01: ownAvgAmmo01,
                avgMorale01: ownArmyMorale,
                enemyStrength: enemyStrengthForReadiness,
                reinforcementHours: nearestReinforcementHours,
                reinforcementStrength: nearestReinforcementStrength,
                commanderAggression01: aggression01);
            var readiness = TacticalSectorReadinessDoctrine.Decide(readinessInput);

            // ---- Sector-level evidence for phase progression + main-effort shift ----
            //
            // EnemyVisibleState.Sectors already carries per-sector own/enemy
            // strength (built from BattleUnits + vision per tick). We use it
            // to compute:
            //   currentMainEffortOdds       - odds in the plan's main effort sector
            //   decisiveSectorId            - sector with the best opportunity right now
            //   bestNonMainEffortOdds       - best odds outside the current main effort
            // These feed the phase progression doctrine, the offensive replan
            // triggers, and ConsiderMainEffortShift. Sector index here is the
            // EnemyVisibleSector.SectorId, which matches plan.MainEffortSector
            // (both originate from the same group-sector estimator).
            // Pass 1: identify the decisive sector AND the current main effort
            // sector's odds in one walk. Both feed the hysteresis check in
            // ConsiderMainEffortShift below — without the current sector's
            // odds, the shift can't tell whether the candidate is materially
            // better or just marginally better.
            int preShiftMainEffortSector = orchestrator.CurrentPlan.MainEffortSector;
            int decisiveSectorId = -1;
            float decisiveSectorOdds = 0f;
            float preShiftMainEffortOdds = 0f;
            if (enemyVisible.Sectors != null)
            {
                for (int i = 0; i < enemyVisible.Sectors.Length; i++)
                {
                    var s = enemyVisible.Sectors[i];
                    // Bands with no enemy carry no odds signal. Band odds are
                    // capped and blended toward unknown by intel confidence
                    // (EnemyVisibleState.SectorOdds), so a fully observed weak
                    // point is a decisive-sector candidate and a picket seen
                    // under low coverage is not.
                    if (!s.HasEnemy) continue;
                    float odds = enemyVisible.SectorOdds(s);
                    if (s.SectorId == preShiftMainEffortSector)
                        preShiftMainEffortOdds = odds;
                    if (odds > decisiveSectorOdds)
                    {
                        decisiveSectorOdds = odds;
                        decisiveSectorId = s.SectorId;
                    }
                }
            }

            // ---- Main effort shift (with hysteresis) ----
            //
            // Hysteresis-aware: candidate must beat current by 25% in odds.
            // Critical at high time compression (20x) where sector odds
            // bounce in real-time terms — without the margin, main effort
            // could oscillate every wallclock second. Internal guards still
            // block Consolidate/Withdraw and same-sector calls.
            // BC14O: with a main-effort objective the plan's main effort follows it (the objective's own latch holds the margin) and the odds-based
            // shift is not asked; with none, the shift runs as before.
            if (orchestrator.TryObjectiveSector(out int objectiveSector))
            {
                orchestrator.SetObjectiveMainEffort(objectiveSector);
            }
            else if (decisiveSectorId >= 0)
            {
                orchestrator.ConsiderMainEffortShift(
                    decisiveSectorId, decisiveSectorOdds, preShiftMainEffortOdds);
            }

            // Pass 2 (post-shift): compute the offensive-trigger inputs
            // against the POST-SHIFT main effort sector. Otherwise the
            // BreakthroughOpportunity trigger would fire spuriously right
            // after shifting (the just-shifted-to sector would still appear
            // as "off-axis best" from the pre-shift snapshot).
            int currentMainEffortSector = orchestrator.CurrentPlan.MainEffortSector;
            float currentMainEffortOdds = ownEvidence.CurrentOdds; // fallback if sector not seen
            float bestNonMainEffortOdds = 0f;
            if (enemyVisible.Sectors != null)
            {
                for (int i = 0; i < enemyVisible.Sectors.Length; i++)
                {
                    var s = enemyVisible.Sectors[i];
                    if (!s.HasEnemy) continue;
                    float odds = enemyVisible.SectorOdds(s);
                    if (s.SectorId == currentMainEffortSector)
                        currentMainEffortOdds = odds;
                    else if (odds > bestNonMainEffortOdds)
                        bestNonMainEffortOdds = odds;
                }
            }

            // ---- Main-effort band history (T7) ----
            //
            // The main band's own strength and odds at the last replan (or at
            // first sighting of this band) are the history the sector-loss and
            // breakthrough checks compare against. Previously both "histories"
            // were the army-wide total / global odds, so MainEffortSectorLoss
            // could never fire and the 1.35x breakthrough ratio divided sector
            // odds by global odds.
            float mainBandOwn = 0f;
            float mainBandOdds = 0f;
            bool mainBandSeen = false;
            if (enemyVisible.Sectors != null)
            {
                for (int i = 0; i < enemyVisible.Sectors.Length; i++)
                {
                    var s = enemyVisible.Sectors[i];
                    if (s.SectorId != currentMainEffortSector) continue;
                    mainBandSeen = true;
                    mainBandOwn = s.OwnStrength;
                    if (s.HasEnemy) mainBandOdds = enemyVisible.SectorOdds(s);
                }
            }
            var baseline = orchestrator.LastReplanBaseline;
            if (mainBandSeen && mainBandOwn > 0f && !baseline.HasMainEffortSnapshot(currentMainEffortSector))
            {
                baseline = baseline.WithMainEffort(currentMainEffortSector, mainBandOwn, mainBandOdds, ownActiveForce);
                orchestrator.RecordReplanBaseline(baseline);
            }
            else if (baseline.MainEffortSector == currentMainEffortSector && baseline.MainEffortOdds <= 0f && mainBandOdds > 0f)
            {
                baseline = baseline.WithMainEffortOdds(mainBandOdds);
                orchestrator.RecordReplanBaseline(baseline);
            }
            float mainOwnCurrent = mainBandSeen && mainBandOwn > 0f ? mainBandOwn : ownActiveForce;
            float mainOwnHistory = baseline.HasMainEffortSnapshot(currentMainEffortSector)
                ? baseline.MainEffortOwnStrength
                : mainOwnCurrent;
            // MainEffortSectorLoss is men lost. The band is a lateral band of an axis refitted every tick, so its strength also falls when a wing
            // marches into the next band, and the old reading called that a collapse (smoke 2026-10-17: 7 of 20 replans, casualtyPressure flat). Losses
            // cannot exceed the army's own decline since the band was taken, so the band's fall counts only up to it.
            float mainOwnForLoss = MainBandStrengthLost(mainOwnCurrent, mainOwnHistory, baseline.OwnArmyStrength, ownActiveForce);
            float mainOddsHistory = baseline.MainEffortSector == currentMainEffortSector && baseline.MainEffortOdds > 0f
                ? baseline.MainEffortOdds
                : currentMainEffortOdds;
            float reinforcementShare = ownReinforcementsArriving / Math.Max(1f, ownActiveForce);

            // ---- Phase progression evaluator ----
            //
            // Drives Probe -> MainEffort -> Exploit -> Consolidate -> Withdraw
            // each tick. AdvancePhase resets phase age (NOT plan age) on
            // actual transitions so each phase has its own budget while the
            // overarching plan deadline (ArmyReplanTriggers.PlanDeadlineBattleSeconds)
            // still backstops via the PhaseDeadline replan trigger.
            orchestrator.EvaluateAndAdvancePhase(
                globalOddsCurrent: ownEvidence.CurrentOdds,
                mainEffortOddsCurrent: currentMainEffortOdds,
                mainEffortOddsHistory: mainOddsHistory,
                armyMoraleCurrent: ownArmyMorale,
                armyMoraleFloor: 0.4f,
                reservesCommittedFraction: ownReservesCommittedFraction,
                mainEffortReadiness: readiness.Result,
                intelConfidence01: enemyVisible.IntelConfidence01);

            var input = new ReplanTriggerInput(
                planAgeSeconds: orchestrator.PlanAgeSeconds,
                currentPhase: orchestrator.CurrentPlan.Phase,
                mainEffortOwnStrength: mainOwnForLoss,
                mainEffortHistoryOwnStrength: mainOwnHistory,
                globalOddsCurrent: ownEvidence.CurrentOdds,
                globalOddsHistory: orchestrator.HistoryGlobalOdds,
                armyMoraleCurrent: ownArmyMorale,
                armyMoraleFloor: 0.4f,
                reservesCommittedFraction: ownReservesCommittedFraction,
                ownReinforcementsArriving: ownReinforcementsArriving,
                enemyMainEffortShiftConfidenceWeighted: ConfidenceWeightedShift(intent),
                mainEffortLocalOdds: currentMainEffortOdds,
                mainEffortLocalOddsHistory: mainOddsHistory,
                bestNonMainEffortSectorOdds: bestNonMainEffortOdds,
                currentMainEffortOdds: currentMainEffortOdds,
                // Opportunity-change trigger inputs (2026-05-21). The orchestrator
                // refreshes _currentReinforcementOpportunity every tick via
                // UpdateReinforcementOpportunity; _lastReplanOpportunity is
                // snapshotted in Replan(). Differing values fire OpportunityChange.
                currentOpportunity: orchestrator.CurrentReinforcementOpportunity.Opportunity,
                lastReplanOpportunity: orchestrator.LastReplanOpportunity,
                armyMoraleAtLastReplan: baseline.Morale,
                reservesCommittedAtLastReplan: baseline.ReservesCommitted,
                ownStrength: ownActiveForce,
                reinforcementShareAtLastReplan: baseline.ReinforcementShare,
                currentEnemyIntent: intent.PrimaryIntent,
                enemyIntentAtLastReplan: baseline.EnemyIntent,
                assumptionReason: assumptionReason,
                flankCrisis: enemyVisible.MaxFlankThreat >= TacticalFrontAxis.FlankThreatSevere);

            var trigger = orchestrator.CheckReplanTriggers(input);
            if (trigger == ReplanTrigger.None) return ReplanOutcome.None;

            // A side holds the plan it adopted for ArmyReplanTriggers.PlanCommitmentBattleSeconds: inside it only a crisis re-picks. The held trigger
            // is level-like (an opportunity category that has not reverted fires every tick) and sits ahead of the broken-assumption trigger in
            // Evaluate's order, so while it is held the assumption, itself a crisis, is raised in its place rather than shadowed for the hour.
            bool crisis = ArmyReplanTriggers.IsCrisis(trigger, input);
            if (!crisis && orchestrator.PlanAgeSeconds < ArmyReplanTriggers.PlanCommitmentBattleSeconds)
            {
                if (assumptionReason == null) return new ReplanOutcome(trigger, false, false, ReplanOutcome.SuppressedByPlanHold);
                trigger = ReplanTrigger.ManeuverAssumptionBroken;
            }

            float now;
            _clockByAlliance.TryGetValue(allianceId, out now);
            // "Min Replan Seconds" is measured on the same clock as plan age:
            // battle seconds when the caller supplies the battle clock.
            float cooldown = minReplanSeconds;
            // A stamp later than now means the clock ran backwards (reload
            // to an earlier save): treat the last replan as long ago.
            if (_lastReplanByAlliance.TryGetValue(allianceId, out var lastReplan) &&
                lastReplan <= now && now - lastReplan < cooldown)
            {
                return new ReplanOutcome(trigger, false, false, assumptionReason != null ? ReplanOutcome.SuppressedByCooldown : null);
            }

            bool brokenAssumption = trigger == ReplanTrigger.ManeuverAssumptionBroken;
            // The challenger must beat the plan held by the score margin unless this is a crisis (a broken assumption has its own margin).
            bool kept = orchestrator.Replan(ownEvidence, intent, hysteresis: brokenAssumption || !crisis, trigger.ToString(), restartAgeIfKept: !brokenAssumption);
            // The band's baseline is the band's own strength: an empty main band records none (the next sighting seeds it), never the army total.
            orchestrator.RecordReplanBaseline(new ReplanBaseline(
                ownArmyMorale,
                ownReservesCommittedFraction,
                reinforcementShare,
                intent.PrimaryIntent,
                orchestrator.CurrentPlan.MainEffortSector,
                mainBandSeen && mainBandOwn > 0f ? mainBandOwn : 0f,
                mainBandOdds,
                ownActiveForce));
            _lastReplanByAlliance[allianceId] = now;
            string suppression = null;
            if (assumptionReason != null) suppression = brokenAssumption ? (kept ? ReplanOutcome.SuppressedByMargin : null) : ReplanOutcome.SuppressedByPreemption;
            return new ReplanOutcome(trigger, !kept, kept, suppression);
        }

        /// <summary>
        /// The main band's strength with its positional fall removed: the band fell from <paramref name="history"/> to <paramref name="current"/>, but the
        /// men it lost cannot exceed what the whole army lost since the band was taken (<paramref name="armyThen"/> to <paramref name="armyNow"/>).
        /// With no army baseline, or an army that has not shrunk, nothing was lost and the band reads as it stood. Reinforcements arriving hide losses
        /// (the safe side: fewer replans).
        /// </summary>
        internal static float MainBandStrengthLost(float current, float history, float armyThen, float armyNow)
        {
            if (history <= 0f || current >= history) return current;
            float armyDrop = Math.Max(0f, armyThen - armyNow);
            float bandDrop = history - current;
            return history - Math.Min(bandDrop, armyDrop);
        }

        private static float ConfidenceWeightedShift(TacticalIntentModel intent)
        {
            if (intent.PrimaryIntent == InferredIntent.Unknown) return 0f;
            if (intent.Confidence01 < ArmyIntentInference.ConfidenceFloor) return 0f;

            float baseWeight;
            switch (intent.PrimaryIntent)
            {
                case InferredIntent.Attack:
                    baseWeight = 1.0f;
                    break;
                case InferredIntent.Defend:
                case InferredIntent.Refuse:
                    baseWeight = 0.5f;
                    break;
                case InferredIntent.Withdraw:
                    baseWeight = 0.7f;
                    break;
                case InferredIntent.Probe:
                    baseWeight = 0.3f;
                    break;
                default:
                    baseWeight = 0f;
                    break;
            }

            return Math.Min(1f, baseWeight * intent.Confidence01);
        }
    }
}
