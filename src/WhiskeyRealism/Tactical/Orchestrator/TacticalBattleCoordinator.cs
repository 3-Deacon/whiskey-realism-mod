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
    public static partial class TacticalBattleCoordinator
    {
        private static TacticalBattleOrchestrator side0;
        private static TacticalBattleOrchestrator side1;
        private static bool active;

        public static bool IsActive => active;

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
        }

        public static void OnBattleStartForTest(int suppressedAllianceId, IEnumerable<SyntheticCommanderInput> commanders)
        {
            if (active) return;
            var roster = TacticalCommanderRoster.BuildFromSynthetic(commanders);
            BuildAndActivate(suppressedAllianceId, roster);
        }

        public static void OnBattleEndForTest()
        {
            side0 = null;
            side1 = null;
            active = false;
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
                    h = h * 31 + intent.AggressionBias01.GetHashCode();
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
            string suffix;
            if (childId.StartsWith(DirectChildSynthArmyIdPrefix, System.StringComparison.Ordinal))
                suffix = childId.Substring(DirectChildSynthArmyIdPrefix.Length);
            else if (childId.StartsWith(DirectChildIdPrefix, System.StringComparison.Ordinal))
                suffix = childId.Substring(DirectChildIdPrefix.Length);
            else
                return 0;
            return int.TryParse(suffix, out int id) ? id : 0;
        }
    }
}
