using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum BattleLifecycleEvent
    {
        None = 0,
        BattleStart = 1,
        BattleEnd = 2,
    }

    public enum BattleLifecycleTransition
    {
        None = 0,
        Start = 1,
        End = 2,
        // End the current battle and immediately start the observed one.
        Restart = 3,
    }

    /// <summary>
    /// One per-cycle observation of the running battle, read on the main thread
    /// from vanilla:
    ///   BattleUnitsId   — GetInstanceID of the scene's BattleUnits. A new battle,
    ///                     a battle reload and a return to the main menu all
    ///                     rebuild the scene (BattleController quit flow 24095-24113,
    ///                     ImportExportUnitData.LoadBattle at frame 29, 24043), so a
    ///                     new id means a new battle.
    ///   SceneHash        — hash of GamePrefs.leveltoload/subleveltoload and
    ///                     SceneManagement.currentcampaignscenario.
    ///   CalendarDay      — monotonic calendar day index from the BattleUnits
    ///                     UniStorm clock (year/month/dayCounter).
    ///   BattleClockHours — GameVars.currenttimefromstart = Hour + battlepasseddays*24
    ///                     (SetTimeVariables 93545). battlepasseddays++ happens mid-night
    ///                     (CheckTimeIssues 86384, eodcycle 2->3) and is NOT a new battle:
    ///                     vanilla keeps every AI state across the night. If midnight
    ///                     passes while eodcycle != 2 the increment is skipped and the
    ///                     clock drops ~24h; the calendar day tells that apart from a
    ///                     reload.
    ///   UnitsInBattle    — BattleUnits.completeunitlist.Count (nulled by OnDestroy at 79219).
    /// </summary>
    public readonly struct BattleLifecycleObservation
    {
        public BattleLifecycleObservation(int battleUnitsId, int sceneHash, int calendarDay, float battleClockHours, int unitsInBattle)
        {
            BattleUnitsId = battleUnitsId;
            SceneHash = sceneHash;
            CalendarDay = calendarDay;
            BattleClockHours = float.IsNaN(battleClockHours) || float.IsInfinity(battleClockHours) ? 0f : battleClockHours;
            UnitsInBattle = unitsInBattle;
        }

        public int BattleUnitsId { get; }
        public int SceneHash { get; }
        public int CalendarDay { get; }
        public float BattleClockHours { get; }
        public int UnitsInBattle { get; }
    }

    public readonly struct BattleLifecycleDecision
    {
        public BattleLifecycleDecision(BattleLifecycleTransition transition, string reason)
        {
            Transition = transition;
            Reason = reason ?? string.Empty;
        }

        public BattleLifecycleTransition Transition { get; }
        public string Reason { get; }

        public static BattleLifecycleDecision None => new BattleLifecycleDecision(BattleLifecycleTransition.None, string.Empty);
    }

    /// <summary>
    /// Pure battle start/end detector. Independent of Unity / vanilla state — safe for
    /// harness testing.
    ///
    /// Primary end signal is <see cref="NotifyBattleUnitsDestroyed"/>, driven by a
    /// Postfix on BattleUnits.OnDestroy (79207): once vanilla nulls completeunitlist (79219),
    /// UpdateAITasks (called from BattleUnits.Update via CalculateSideStatsAndUpdateAITasks
    /// 84864) never runs again, so a "zero units" tick is never observed.
    ///
    /// Fallback on every observed AI cycle: the battle identity (BattleUnits id, scene)
    /// changing, or the battle clock moving backwards on the same calendar day, forces a
    /// Restart (teardown of the old battle followed by a fresh start).
    /// </summary>
    public sealed class TacticalBattleLifecycleDetector
    {
        // A rewind smaller than this is noise; the vanilla clock is monotonic within a
        // battle day and all three AIBattle instances see the same value per cycle.
        private const float ClockRewindToleranceHours = 0.001f;

        private bool inBattle;
        private int consecutiveZeroTicks;
        private bool hasIdentity;
        private int battleUnitsId;
        private int sceneHash;
        private int calendarDay;
        private float lastClockHours;

        public bool InBattle => inBattle;

        /// <summary>
        /// Count-only observation (legacy contract): BattleStart on the first tick with
        /// units after none, BattleEnd after two consecutive zero-unit ticks.
        /// </summary>
        public BattleLifecycleEvent Observe(int unitsInBattleThisTick)
        {
            if (unitsInBattleThisTick > 0)
            {
                consecutiveZeroTicks = 0;
                if (!inBattle)
                {
                    inBattle = true;
                    return BattleLifecycleEvent.BattleStart;
                }
                return BattleLifecycleEvent.None;
            }

            if (!inBattle) return BattleLifecycleEvent.None;
            consecutiveZeroTicks++;
            if (consecutiveZeroTicks >= 2)
            {
                EndBattle();
                return BattleLifecycleEvent.BattleEnd;
            }
            return BattleLifecycleEvent.None;
        }

        public BattleLifecycleDecision Observe(BattleLifecycleObservation observation)
        {
            if (observation.UnitsInBattle <= 0 || observation.BattleUnitsId == 0)
            {
                return Observe(0) == BattleLifecycleEvent.BattleEnd
                    ? new BattleLifecycleDecision(BattleLifecycleTransition.End, "no-units")
                    : BattleLifecycleDecision.None;
            }

            consecutiveZeroTicks = 0;
            if (!inBattle)
            {
                inBattle = true;
                Remember(observation);
                return new BattleLifecycleDecision(BattleLifecycleTransition.Start, "first-observation");
            }

            string restartReason = RestartReason(observation);
            Remember(observation);
            return restartReason == null
                ? BattleLifecycleDecision.None
                : new BattleLifecycleDecision(BattleLifecycleTransition.Restart, restartReason);
        }

        /// <summary>
        /// BattleUnits.OnDestroy for <paramref name="destroyedBattleUnitsId"/>. Ends the
        /// tracked battle only when the destroyed instance is the battle's own
        /// BattleUnits (the campaign-map BattleUnits is destroyed on battle load too).
        /// </summary>
        public BattleLifecycleDecision NotifyBattleUnitsDestroyed(int destroyedBattleUnitsId)
        {
            if (!inBattle) return BattleLifecycleDecision.None;
            if (hasIdentity && destroyedBattleUnitsId != battleUnitsId) return BattleLifecycleDecision.None;
            EndBattle();
            return new BattleLifecycleDecision(BattleLifecycleTransition.End, "battle-units-destroyed");
        }

        private string RestartReason(BattleLifecycleObservation observation)
        {
            if (!hasIdentity) return null;
            if (observation.BattleUnitsId != battleUnitsId) return "battle-units-changed";
            if (observation.SceneHash != sceneHash) return "scene-changed";
            if (observation.BattleClockHours < lastClockHours - ClockRewindToleranceHours
                && observation.CalendarDay <= calendarDay)
                return "battle-clock-rewound";
            return null;
        }

        private void Remember(BattleLifecycleObservation observation)
        {
            hasIdentity = true;
            battleUnitsId = observation.BattleUnitsId;
            sceneHash = observation.SceneHash;
            calendarDay = observation.CalendarDay;
            lastClockHours = observation.BattleClockHours;
        }

        private void EndBattle()
        {
            inBattle = false;
            consecutiveZeroTicks = 0;
            hasIdentity = false;
            lastClockHours = 0f;
        }
    }

    public enum EndOfDayPhase
    {
        Fighting = 0,
        // BattleUnits.eodcycle != 0: end-of-day sequence (provisions 1, night
        // fast-forward 2, after midnight 3, morning 4, deployment 5, cease-fire 6;
        // CheckTimeIssues 86369-86470).
        Suspended = 1,
        // First fighting cycle after the morning deployment panel closed
        // (SetActiveDeploymentPhase(false) sets eodcycle = 0, 164874-164882).
        Resumed = 2,
    }

    /// <summary>
    /// Pure end-of-day tracker. Vanilla keeps AI state across the night; the
    /// orchestrator pauses its doctrine ticks while eodcycle != 0 and, once fighting
    /// resumes, rebuilds its picture of the (possibly redeployed) field and replans
    /// with its plan memory intact.
    /// </summary>
    public sealed class TacticalEndOfDayGate
    {
        private bool suspended;

        public EndOfDayPhase Observe(int eodcycle)
        {
            if (eodcycle != 0)
            {
                suspended = true;
                return EndOfDayPhase.Suspended;
            }

            if (!suspended) return EndOfDayPhase.Fighting;
            suspended = false;
            return EndOfDayPhase.Resumed;
        }
    }

    /// <summary>
    /// Monotonic battle clock for doctrine timers (phase budgets, commit windows,
    /// replan cadence). Follows GameVars.currenttimefromstart (Hour + battlepasseddays*24,
    /// SetTimeVariables 93545) forward, but does not advance while suspended (overnight
    /// the clock races at game speed 1000) and ignores backward jumps (the hour wraps at
    /// midnight before battlepasseddays++, CheckTimeIssues 86382-86384).
    /// </summary>
    public sealed class TacticalDoctrineClock
    {
        private bool started;
        private double lastRawHours;
        private double doctrineHours;

        public double Hours => doctrineHours;

        public double Advance(double rawBattleHours, bool suspended)
        {
            if (double.IsNaN(rawBattleHours) || double.IsInfinity(rawBattleHours)) return doctrineHours;
            if (!started)
            {
                started = true;
                lastRawHours = rawBattleHours;
                doctrineHours = rawBattleHours;
                return doctrineHours;
            }

            double delta = rawBattleHours - lastRawHours;
            lastRawHours = rawBattleHours;
            if (!suspended && delta > 0.0) doctrineHours += delta;
            return doctrineHours;
        }
    }
}
