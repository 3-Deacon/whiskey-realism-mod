using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Test-friendly partial — pure state machine, no BepInEx/OnceLog/Plugin.Log references.
    ///
    /// Owns two per-side TacticalBattleOrchestrator roots, keyed by allianceId
    /// 0 or 1. The alliance-id-to-nation mapping is NOT fixed: it is per-battle,
    /// populated by vanilla from the actual combatants
    /// (BattleUnits.alliance[side] = combatant alliance id, see decompile
    /// /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:19898 and adjacent).
    /// CSA can be side 0 OR side 1 depending on who deploys first / battle
    /// type / scenario. Historical NB: "Army of the Potomac" was Confederate
    /// (May 1861 – March 1862, Beauregard then Joe Johnston) before it was
    /// renamed the Army of Northern Virginia; the Union Army of the Potomac
    /// dates from August 1861. Both sides used the same name briefly in 1861.
    /// On battle start the player-CIC side is suppressed (null) so we never
    /// retask units the player is personally commanding.
    ///
    /// Runtime entry points (OnBattleStart / OnBattleEnd / Tick) live in the runtime
    /// partial: TacticalBattleCoordinatorRuntime.cs (excluded from the test assembly).
    /// </summary>
    internal enum TickReplanStep
    {
        None = 0,
        NightReplan = 1,
        Cadence = 2,
    }

    public static partial class TacticalBattleCoordinator
    {
        private static TacticalBattleOrchestrator side0;
        private static TacticalBattleOrchestrator side1;
        private static bool active;

        public static bool IsActive => active;

        // ---- Doctrine clock ----
        //
        // End-of-day handling: vanilla keeps all AI state across the night
        // (battlepasseddays++ at 86384 is mid-night, eodcycle 2->3). While eodcycle != 0
        // doctrine Ticks pause and the doctrine clock stops (the game clock races at
        // speed 1000 overnight); when fighting resumes (eodcycle back to 0 at 164881)
        // both sides rebuild their snapshot and replan, keeping their orchestrators.
        private static TacticalEndOfDayGate _endOfDayGate = new TacticalEndOfDayGate();
        private static TacticalDoctrineClock _doctrineClock = new TacticalDoctrineClock();
        private static bool _doctrineClockSampled;
        private static int _doctrineClockSampleFrame;
        private static int _doctrineClockBattleKey;

        /// <summary>One raw reading of the battle clock. BattleKey is the battle's
        /// BattleUnits instance id, 0 when no battle is loaded.</summary>
        internal readonly struct DoctrineClockSample
        {
            public DoctrineClockSample(int battleKey, double rawBattleHours, int eodcycle)
            {
                BattleKey = battleKey;
                RawBattleHours = rawBattleHours;
                EodCycle = eodcycle;
            }

            public int BattleKey { get; }
            public double RawBattleHours { get; }
            public int EodCycle { get; }
        }

        // Installed by the runtime partial (Unity frame counter and vanilla battle
        // state). Null in the test assembly unless a test installs a fake: the getter
        // then returns the clock as last observed.
        internal static Func<int> DoctrineClockFrameSource;
        internal static Func<DoctrineClockSample> DoctrineClockSampler;

        /// <summary>
        /// THE doctrine clock for every tactical doctrine timer (phase budgets, commit
        /// windows, replan cadence, hold/delay timers): battle hours that follow
        /// GameVars.currenttimefromstart forward while fighting, are frozen while
        /// BattleUnits.eodcycle != 0 (end-of-day sequence, CheckTimeIssues 86369-86470),
        /// never run backwards (midnight hour wrap before battlepasseddays++,
        /// 86382-86384 / SetTimeVariables 93545) and are 0 before the first observation
        /// of a battle and after battle end. Self-advancing: each read samples the
        /// vanilla clock at most once per frame, so readers do not depend on the
        /// orchestrator ticking. Do not read currenttimefromstart or realtimeSinceStartup
        /// for doctrine timing.
        /// </summary>
        public static double DoctrineBattleHours
        {
            get
            {
                SampleDoctrineClock();
                return _doctrineClock.Hours;
            }
        }

        /// <summary>Samples the vanilla battle clock through the installed sources, at
        /// most once per frame. Never throws.</summary>
        internal static void SampleDoctrineClock()
        {
            var frameSource = DoctrineClockFrameSource;
            var sampler = DoctrineClockSampler;
            if (frameSource == null || sampler == null) return;
            try
            {
                int frame = frameSource();
                if (_doctrineClockSampled && frame == _doctrineClockSampleFrame) return;
                ObserveDoctrineClockSample(frame, sampler());
            }
            catch
            {
                // A failed sample leaves the clock where it was.
            }
        }

        /// <summary>
        /// Pure clock step for one frame: a second sample in the same frame is ignored,
        /// no battle (key 0) leaves the clock alone, a different battle restarts it, and
        /// otherwise it follows the raw clock unless eodcycle != 0. The end-of-day gate
        /// is not touched here — its Resumed edge belongs to the coordinator Tick.
        /// </summary>
        internal static void ObserveDoctrineClockSample(int frame, DoctrineClockSample sample)
        {
            if (_doctrineClockSampled && frame == _doctrineClockSampleFrame) return;
            _doctrineClockSampled = true;
            _doctrineClockSampleFrame = frame;
            if (sample.BattleKey == 0) return;
            if (sample.BattleKey != _doctrineClockBattleKey)
            {
                _doctrineClock = new TacticalDoctrineClock();
                _doctrineClockBattleKey = sample.BattleKey;
            }
            _doctrineClock.Advance(sample.RawBattleHours, suspended: sample.EodCycle != 0);
        }

        /// <summary>Feeds one vanilla side-stat cycle's eodcycle into the end-of-day
        /// gate. Called once per coordinator Tick.</summary>
        internal static EndOfDayPhase ObserveEndOfDay(int eodcycle)
        {
            return _endOfDayGate.Observe(eodcycle);
        }

        /// <summary>
        /// Which replan path a side's tick takes. On the first fighting tick of a new
        /// battle day the night replan runs INSTEAD of the cadence path (MaybeReplan),
        /// so a side never replans twice on the same evidence in one tick.
        /// </summary>
        internal static TickReplanStep DecideTickReplanStep(bool nightReplanDue, bool hasPlan)
        {
            if (!hasPlan) return TickReplanStep.None;
            return nightReplanDue ? TickReplanStep.NightReplan : TickReplanStep.Cadence;
        }

        /// <summary>
        /// New battle day: re-infer the enemy's intent from the morning's evidence (AI
        /// groups may have been redeployed when the deployment panel closed,
        /// DoPlacementAIUnitsWithinDeploymentzoneNew 85524) and pick a plan on it. The
        /// orchestrator and its plan memory are kept.
        /// </summary>
        internal static void ReplanForNewBattleDay(ArmyOrchestrator army, ArmyEvidence ownEvidence, EnemyVisibleState enemyVisible)
        {
            if (army == null || !army.HasPlan) return;
            var intent = ArmyIntentInference.Build(enemyVisible);
            army.ObserveIntent(intent);
            army.Replan(ownEvidence, intent);
        }

        /// <summary>Battle end / failure: clock back to 0, no suspension.</summary>
        internal static void ResetDoctrineClock()
        {
            _endOfDayGate = new TacticalEndOfDayGate();
            _doctrineClock = new TacticalDoctrineClock();
            _doctrineClockSampled = false;
            _doctrineClockSampleFrame = 0;
            _doctrineClockBattleKey = 0;
        }

        /// <summary>
        /// Returns the per-side orchestrator for the given alliance id, or null if
        /// that side is suppressed (player-CIC) or not yet activated.
        /// </summary>
        public static TacticalBattleOrchestrator GetSideOrchestrator(int allianceId)
        {
            switch (allianceId)
            {
                case 0: return side0;
                case 1: return side1;
                default: return null;
            }
        }

        // ---- Test seams ----

        public static void ResetForTest()
        {
            side0 = null;
            side1 = null;
            active = false;
            ResetDoctrineClock();
        }

        public static void OnBattleStartForTest(int suppressedAllianceId, IEnumerable<SyntheticCommanderInput> commanders)
        {
            if (active) return;
            var roster = TacticalCommanderRoster.BuildFromSynthetic(commanders);
            BuildAndActivate(suppressedAllianceId, roster);
        }

        // ---- Internal builder (used by both test and runtime paths) ----

        /// <summary>
        /// Instantiates per-side orchestrators, suppresses one protected side, and sets
        /// active = true. No telemetry — callers in the runtime partial add logging on top.
        /// </summary>
        internal static void BuildAndActivate(int suppressedAllianceId, TacticalCommanderRoster roster)
        {
            side0 = (suppressedAllianceId == 0) ? null : new TacticalBattleOrchestrator(allianceId: 0, roster);
            side1 = (suppressedAllianceId == 1) ? null : new TacticalBattleOrchestrator(allianceId: 1, roster);
            active = true;
        }

        /// <summary>
        /// The feud instance (AIBattle.isplayeraiorfeud == 2, SetSide 3408) runs the macro
        /// cycle under ai_vs_ai but commands only feuding groups; it never drives the
        /// battle lifecycle or the orchestrator Tick.
        /// </summary>
        public static bool DrivesOrchestrator(int isPlayerAiOrFeud)
        {
            return isPlayerAiOrFeud != 2;
        }

        /// <summary>
        /// Alliance whose orchestrator is not built. W&amp;L forces ai_vs_ai (29789), so a
        /// W&amp;L Commander-in-Chief player (DLC_WL.IsCommanderInChief, 47475) owns every
        /// unit of their alliance and that alliance is suppressed. Outside W&amp;L the
        /// player's alliance is suppressed unless ai_vs_ai. A W&amp;L subordinate career
        /// suppresses nothing here (its units are protected per unit by the write guard).
        /// </summary>
        public static int ResolveSuppressedAllianceId(bool aiVsAi, int playerAllianceId, int playerCicAllianceId)
        {
            if (playerCicAllianceId == 0 || playerCicAllianceId == 1) return playerCicAllianceId;
            if (!aiVsAi && (playerAllianceId == 0 || playerAllianceId == 1)) return playerAllianceId;
            return -1;
        }

        public static bool ShouldRunTacticalCommanderForSide(int allianceId, int playerAllianceId, bool aiVsAi)
        {
            if (allianceId < 0 || allianceId > 1) return false;
            if (aiVsAi) return true;
            if (playerAllianceId < 0 || playerAllianceId > 1) return false;
            return allianceId != playerAllianceId;
        }

        /// <summary>
        /// Operations-ledger clock (T8): battle seconds from the doctrine clock
        /// (TacticalDoctrineClock over GameVars.currenttimefromstart = Hour +
        /// battlepasseddays*24, SetTimeVariables 93545; frozen overnight), so the
        /// director's commit windows (420/900/1200 s) span the same battle time at any
        /// time compression. Previously realtimeSinceStartup: 15 battle-minutes at 1x
        /// but ~5 battle-hours at 20x.
        /// </summary>
        internal static float LedgerClockSecondsFromBattleHours(float battleHours)
        {
            if (float.IsNaN(battleHours) || float.IsInfinity(battleHours) || battleHours < 0f) return 0f;
            return battleHours * 3600f;
        }

        /// <summary>Upper bound between operations-ledger ticks when the published
        /// snapshot has not changed, so time-based director transitions (commit-window
        /// expiry) still happen. Battle seconds.</summary>
        internal const float OperationsLedgerMaxIntervalBattleSeconds = 60f;

        /// <summary>
        /// The ledger (director + doctrine assignment + command-node build) only needs
        /// to run when its inputs changed: a new heavy snapshot, a Commander Mode change,
        /// or OperationsLedgerMaxIntervalBattleSeconds of battle time elapsed (or the
        /// battle clock went backwards).
        /// </summary>
        internal static bool ShouldTickOperationsLedger(
            bool snapshotChanged,
            bool modeChanged,
            float nowBattleSeconds,
            float lastTickBattleSeconds)
        {
            if (snapshotChanged || modeChanged) return true;
            float elapsed = nowBattleSeconds - lastTickBattleSeconds;
            return elapsed < 0f || elapsed >= OperationsLedgerMaxIntervalBattleSeconds;
        }

        /// <summary>
        /// Allocation-free structural hash of a command tree (root, counts, depth, and per
        /// node id / parent / raw unittyp / depth), replacing the per-tick StringBuilder
        /// signature used for "unchanged" checks. Live flags are excluded: the builder
        /// already drops routed / inactive command units, so a rout that matters changes
        /// the node set, while other flag changes do not reallocate intents.
        /// </summary>
        internal static int CommandTreeHash(CommandTreeSnapshot tree)
        {
            if (tree == null || !tree.HasNodes) return 0;
            unchecked
            {
                int h = 17;
                h = h * 31 + StableHash(tree.RootNodeId);
                h = h * 31 + tree.Nodes.Count;
                h = h * 31 + tree.MaxDepth;
                h = h * 31 + StableHash(tree.RawUnitTypDistribution);
                h = h * 31 + tree.MissingParentCount;
                for (int i = 0; i < tree.Nodes.Count; i++)
                {
                    var node = tree.Nodes[i];
                    h = h * 31 + StableHash(node.NodeId);
                    h = h * 31 + StableHash(node.ParentNodeId);
                    h = h * 31 + node.RawUnitTyp;
                    h = h * 31 + node.Depth;
                }
                return h;
            }
        }

        /// <summary>
        /// T14 operation strength filter for one unit of a tasked node's allattachedunits:
        /// combat units only (unittyp &lt;= MaxCombat), with vanilla GetAttachedUnitsReg's
        /// default filters (no dechained units, no skirmisher companies 3/4, no inactive or
        /// routed units), no marked-for-rout, no unittyp 5 (stripped from completeunitlist
        /// by UpdateUnitList 79984), and no player-owned units (dlcw_isundercommander: the
        /// W&amp;L player's command stays in its parent's attachment list).
        /// </summary>
        internal static bool CountsTowardOperationStrength(
            int unittyp,
            bool permanentlyDetached,
            bool routed,
            bool markedForRout,
            bool active,
            bool underPlayerCommand)
        {
            if (unittyp > TacticalUnitType.MaxCombat) return false;
            if (unittyp == 3 || unittyp == 4 || unittyp == 5) return false;
            if (permanentlyDetached || routed || markedForRout || !active) return false;
            return !underPlayerCommand;
        }

        /// <summary>
        /// Content hash of the command-node intents feeding the operations ledger
        /// (node, source, role, axis, sector, support priority, depth, aggression). The
        /// allocator returns a fresh list on every observe, so the ledger change gate
        /// compares this hash instead of references.
        /// </summary>
        internal static int CommandNodeIntentsHash(IReadOnlyList<CommandNodeIntent> intents)
        {
            if (intents == null) return 0;
            unchecked
            {
                int h = 17 + intents.Count;
                for (int i = 0; i < intents.Count; i++)
                {
                    var intent = intents[i];
                    h = h * 31 + StableHash(intent.NodeId);
                    h = h * 31 + StableHash(intent.SourceNodeId);
                    h = h * 31 + (int)intent.Role;
                    h = h * 31 + (int)intent.Axis;
                    h = h * 31 + intent.PrimarySector;
                    h = h * 31 + intent.SupportPriority;
                    h = h * 31 + intent.Depth;
                }
                return h;
            }
        }

        private static int StableHash(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            unchecked
            {
                int h = 5381;
                for (int i = 0; i < value.Length; i++) h = (h * 33) ^ value[i];
                return h;
            }
        }

        // ---- Pure helpers used by the runtime partial for telemetry ----

        internal static int MatchedCount(TacticalCommanderRoster roster, int alliance)
        {
            int n = 0;
            foreach (var e in roster.GetSide(alliance))
                if (e.MatchedHistoricalRegistry) n++;
            return n;
        }

        internal static int UnknownCount(TacticalCommanderRoster roster, int alliance)
        {
            int n = 0;
            foreach (var e in roster.GetSide(alliance))
                if (!e.MatchedHistoricalRegistry) n++;
            return n;
        }

        // ---- DirectChild ChildId parser ----
        //
        // Parses the trailing integer instanceId from "child-{id}" or "synth-army-{id}".
        // Unity GameObject InstanceIDs are routinely negative, so we strip the known
        // prefix (NOT LastIndexOf('-'), which would land on the negative sign).
        // Returns 0 on parse failure (which never matches a real GameObject InstanceID).
        internal const string DirectChildIdPrefix = "child-";
        internal const string DirectChildSynthArmyIdPrefix = "synth-army-";
        internal static int ParseInstanceIdFromChildId(string childId)
        {
            if (string.IsNullOrEmpty(childId)) return 0;
            if (childId.StartsWith(DirectChildSynthArmyIdPrefix, System.StringComparison.Ordinal))
                return ParseInt32Suffix(childId, DirectChildSynthArmyIdPrefix.Length);
            if (childId.StartsWith(DirectChildIdPrefix, System.StringComparison.Ordinal))
                return ParseInt32Suffix(childId, DirectChildIdPrefix.Length);
            return 0;
        }

        internal const string CommandNodeIdPrefix = "node-";

        /// <summary>
        /// GameObject InstanceID of a command node id: "node-{id}" (CommandTreeBuilder)
        /// or a direct-child id ("child-{id}" / "synth-army-{id}"). 0 when unparseable.
        /// Runs per tasked node every ledger tick, so it parses in place (no Substring).
        /// </summary>
        internal static int ParseCommandNodeInstanceId(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return 0;
            if (nodeId.StartsWith(CommandNodeIdPrefix, System.StringComparison.Ordinal))
                return ParseInt32Suffix(nodeId, CommandNodeIdPrefix.Length);
            return ParseInstanceIdFromChildId(nodeId);
        }

        // Optional '-' then decimal digits to the end of the string; 0 on any other
        // character, an empty suffix or Int32 overflow.
        private static int ParseInt32Suffix(string s, int start)
        {
            int i = start;
            bool negative = i < s.Length && s[i] == '-';
            if (negative) i++;
            if (i >= s.Length) return 0;
            long value = 0;
            for (; i < s.Length; i++)
            {
                int digit = s[i] - '0';
                if (digit < 0 || digit > 9) return 0;
                value = value * 10 + digit;
                if (value > 2147483648L) return 0;
            }
            if (negative) value = -value;
            if (value > int.MaxValue) return 0;
            return (int)value;
        }
    }
}
