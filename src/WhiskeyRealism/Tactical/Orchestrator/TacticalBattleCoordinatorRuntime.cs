using System;
using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// BepInEx-coupled partial — excluded from the test assembly.
    ///
    /// Contains the real runtime entry points called from Harmony patches:
    ///   OnBattleStart  — resolves player CIC side, discovers commanders (O0 stub),
    ///                    calls BuildAndActivate, emits bootstrap telemetry.
    ///   OnBattleEnd    — clears inter-battle ledger state, tears down orchestrators.
    ///   Tick           — cascades to per-side orchestrators; emits first-tick once-marker.
    ///
    /// All methods are try/catch guarded — this code runs inside or adjacent to Harmony
    /// patches and must never throw.
    /// </summary>
    public static partial class TacticalBattleCoordinator
    {
        public static void OnBattleStart()
        {
            if (active) return;
            if (!Plugin.EnableTacticalBattleOrchestrator.Value) return;

            try
            {
                int playerCicAllianceId = ResolvePlayerCicAllianceId();
                var commanders = DiscoverCommandersFromVanilla();
                var roster = TacticalCommanderRoster.BuildFromSynthetic(commanders);
                BuildAndActivate(playerCicAllianceId, roster);

                int suppressed = (playerCicAllianceId == 0 || playerCicAllianceId == 1) ? 1 : 0;
                int activated = 2 - suppressed;
                Plugin.Log.LogInfo("[TacticalCommanderRoster] alliance=0 total=" + roster.GetSide(0).Count
                    + " matched=" + MatchedCount(roster, 0) + " unknown=" + UnknownCount(roster, 0));
                Plugin.Log.LogInfo("[TacticalCommanderRoster] alliance=1 total=" + roster.GetSide(1).Count
                    + " matched=" + MatchedCount(roster, 1) + " unknown=" + UnknownCount(roster, 1));
                foreach (var entry in roster.GetSide(0))
                    if (!entry.MatchedHistoricalRegistry)
                        Plugin.Log.LogInfo("[TacticalCommanderUnknown] echelon=" + entry.Echelon
                            + " name=" + (string.IsNullOrEmpty(entry.Name) ? "<null>" : entry.Name));
                foreach (var entry in roster.GetSide(1))
                    if (!entry.MatchedHistoricalRegistry)
                        Plugin.Log.LogInfo("[TacticalCommanderUnknown] echelon=" + entry.Echelon
                            + " name=" + (string.IsNullOrEmpty(entry.Name) ? "<null>" : entry.Name));
                OnceLog.Info("orch-bootstrap", "[TacticalOrchestrator] bootstrap sidesActive=" + activated
                    + " sidesSuppressed=" + suppressed);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] OnBattleStart skipped: "
                    + e.GetType().Name + " " + e.Message);
                ClearForFailure();
            }
        }

        public static void OnBattleEnd()
        {
            if (!active) return;
            try
            {
                ClearLedgersBetweenBattles();
                side0 = null;
                side1 = null;
                active = false;
                OnceLog.Info("orch-teardown", "[TacticalOrchestrator] teardown");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] OnBattleEnd partial: "
                    + e.GetType().Name + " " + e.Message);
                ClearForFailure();
            }
        }

        public static void Tick()
        {
            if (!active) return;
            try
            {
                OnceLog.Info("orch-coordinator", "[TacticalOrchestrator] coordinator first tick");
                side0?.Tick();
                side1?.Tick();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] Tick skipped: "
                    + e.GetType().Name + " " + e.Message);
            }
        }

        // ---- Private runtime helpers ----

        private static int ResolvePlayerCicAllianceId()
        {
            try
            {
                if (!DLC_WL.dlc_scenarioactive) return -1;
                if (!DLC_WL.IsCommanderInChief()) return -1;
                int chosen = DLC_WL.dlc_chosencommander;
                if (chosen < 0 || chosen >= GameVars.commander.Count) return -1;
                return GameVars.commander[chosen].alliance;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] ResolvePlayerCicAllianceId failed (W&L suppression gate disabled until next battle): " + e.GetType().Name + " " + e.Message);
                return -1;
            }
        }

        private static IEnumerable<SyntheticCommanderInput> DiscoverCommandersFromVanilla()
        {
            // O0 stub: real commander discovery ships in O1 when ArmyOrchestrator exists
            // and we know which tiers to bind. Empty roster still exercises the bootstrap
            // lifecycle and emits telemetry.
            return Array.Empty<SyntheticCommanderInput>();
        }

        private static void ClearLedgersBetweenBattles()
        {
            try { TacticalSectorLedger.ClearHelpRequests(); } catch { }
            try { Plugin.MoraleSnapshotLedger?.Clear(); } catch { }
        }

        private static void ClearForFailure()
        {
            side0 = null;
            side1 = null;
            active = false;
        }
    }
}
