using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Test-friendly partial — pure state machine, no BepInEx/OnceLog/Plugin.Log references.
    ///
    /// Owns two per-side TacticalBattleOrchestrator roots (side0 = CSA alliance 0,
    /// side1 = USA alliance 1). On battle start the player-CIC side is suppressed (null)
    /// so we never retask units the player is personally commanding.
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

        public static void OnBattleStartForTest(int playerCicAllianceId, IEnumerable<SyntheticCommanderInput> commanders)
        {
            if (active) return;
            var roster = TacticalCommanderRoster.BuildFromSynthetic(commanders);
            BuildAndActivate(playerCicAllianceId, roster);
        }

        public static void OnBattleEndForTest()
        {
            side0 = null;
            side1 = null;
            active = false;
        }

        // ---- Internal builder (used by both test and runtime paths) ----

        /// <summary>
        /// Instantiates per-side orchestrators, suppresses the player-CIC side, and sets
        /// active = true. No telemetry — callers in the runtime partial add logging on top.
        /// </summary>
        internal static void BuildAndActivate(int playerCicAllianceId, TacticalCommanderRoster roster)
        {
            side0 = (playerCicAllianceId == 0) ? null : new TacticalBattleOrchestrator(allianceId: 0, roster);
            side1 = (playerCicAllianceId == 1) ? null : new TacticalBattleOrchestrator(allianceId: 1, roster);
            active = true;
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
