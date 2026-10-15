using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // B-61state (2026-10-14): orchestrator inputs that #61
    // (BattleCommandPostureExecutorPatch) consumes, refreshed on the
    // coordinator tick instead of from the Harmony Postfix (read-only contract
    // for patches). Call Refresh once per side per coordinator tick, before
    // the next AIBattle.UpdateAITasks for that side:
    //   - leaf brigade map: ArmyOrchestrator.UpdateLeafBrigadeMap(tree) from the
    //     alliance's extended probe tree (cached per roster signature, not per
    //     frame);
    //   - live commander initiative: UpdateLiveCommanderInitiative;
    //   - reinforcement opportunity: UpdateReinforcementOpportunity from
    //     ArmyEvidenceBuilder.BuildForceBalance (gated by
    //     Enable Tactical Reinforcement Opportunity Doctrine).
    public static class ArmyPostureInputsRuntime
    {
        private const float TelemetrySeconds = 30f;
        private static readonly Dictionary<string, float> _lastTelemetryAt = new Dictionary<string, float>();
        private static readonly Dictionary<int, CachedProbes> _probeCache = new Dictionary<int, CachedProbes>();

        private readonly struct CachedProbes
        {
            public CachedProbes(TacticalRosterSignature signature, IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> probes)
            {
                Signature = signature;
                Probes = probes;
            }

            public TacticalRosterSignature Signature { get; }
            public IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> Probes { get; }
        }

        public static void Refresh(AIBattle battle, BattleUnits bunits, int side, ArmyOrchestrator army)
        {
            try
            {
                if (battle == null || bunits == null || army == null || bunits.alliance == null) return;
                if (side < 0 || side >= bunits.alliance.Length) return;
                int allianceId = bunits.alliance[side];
                if (allianceId < 0 || allianceId >= 2) return;

                var probes = ExtendedProbes(bunits, allianceId);
                if (probes != null && probes.Count > 0)
                    army.UpdateLeafBrigadeMap(TacticalCommandTreeProbe.BuildTree(probes, 0));

                if (Plugin.EnableTacticalReinforcementOpportunityDoctrine == null ||
                    !Plugin.EnableTacticalReinforcementOpportunityDoctrine.Value)
                    return;

                army.UpdateLiveCommanderInitiative(ReadLiveCommanderInitiative01(bunits, side));

                // Aggression is the PersonalityVector baseline mapped from
                // [-1, 1] to [0, 1]; live initiative is the vanilla reading.
                float commanderAggression01 = (army.CommanderPersonality.Aggression + 1f) * 0.5f;
                if (commanderAggression01 < 0f) commanderAggression01 = 0f;
                if (commanderAggression01 > 1f) commanderAggression01 = 1f;

                var forceBalance = ArmyEvidenceBuilder.BuildForceBalance(
                    battle, allianceId, army.LiveCommanderInitiative01, commanderAggression01);
                if (forceBalance.HasValue)
                {
                    army.UpdateReinforcementOpportunity(forceBalance.Value);
                    EmitReinforcementOpportunityTelemetry(side, army.CurrentReinforcementOpportunity);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("army-posture-inputs:refresh-failed",
                    "ArmyPostureInputsRuntime.Refresh failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // Called on battle teardown (coordinator OnBattleEnd).
        public static void ResetForBattleEnd()
        {
            _probeCache.Clear();
            _lastTelemetryAt.Clear();
        }

        // The W&L player's command subtree is left out of the probe tree, so
        // the role cascade / leaf map never assigns it a role and no AI
        // brigade waits on it as support (PlayerCommandProbeFilter).
        private static IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> ExtendedProbes(BattleUnits bunits, int allianceId)
        {
            var playerCommand = new HashSet<int>();
            TacticalRosterSignature signature = RosterSignature(bunits, allianceId, playerCommand);
            if (_probeCache.TryGetValue(allianceId, out CachedProbes cached) &&
                cached.Probes != null &&
                cached.Signature.Equals(signature))
                return cached.Probes;

            var probes = PlayerCommandProbeFilter.Without(
                DirectChildDiscovery.BuildExtendedProbesForAlliance(allianceId),
                playerCommand);
            _probeCache[allianceId] = new CachedProbes(signature, probes);
            return probes;
        }

        // Counts only units outside the player's command, so a change in the
        // W&L command set also invalidates the probe cache.
        private static TacticalRosterSignature RosterSignature(BattleUnits bunits, int allianceId, ISet<int> playerCommand)
        {
            int count = 0;
            int routed = 0;
            bool wl = DLC_WL.dlc_scenarioactive;
            var units = BattleUnits.completeunitlist as IList;
            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    var unit = units[i] as Regiment;
                    if (unit == null || unit.alliance != allianceId) continue;
                    if (wl && unit.dlcw_isundercommander)
                    {
                        playerCommand.Add(((Component)unit).gameObject.GetInstanceID());
                        continue;
                    }
                    count++;
                    if (unit.isrouted || unit.markedforrout) routed++;
                }
            }

            return new TacticalRosterSignature(allianceId, count, routed, bunits.GetInstanceID());
        }

        /// <summary>
        /// Live commander initiative for the given side, read defensively from
        /// vanilla. Mirrors the proven BattleMacroStrategyPatch.CommanderAggression01
        /// pattern: bunits.GetCommandingOfficerFromSide(side) → GameVars.commander[id]
        /// .GetCommanderInitiative(), with NaN/range guards. Returns 0.5 mid-band
        /// on any failure so the doctrine degrades gracefully rather than locking
        /// to a misleading 0 or NaN.
        /// </summary>
        private static float ReadLiveCommanderInitiative01(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.alliance == null) return 0.5f;
                if (side < 0 || side >= bunits.alliance.Length) return 0.5f;
                int commanderId = bunits.GetCommandingOfficerFromSide(side);
                if (GameVars.commander == null || commanderId < 0 || commanderId >= GameVars.commander.Count) return 0.5f;
                var commander = GameVars.commander[commanderId];
                if (commander == null) return 0.5f;
                float init = commander.GetCommanderInitiative();
                if (float.IsNaN(init) || float.IsInfinity(init)) return 0.5f;
                if (init < 0f) return 0f;
                if (init > 1f) return 1f;
                return init;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-reinforcement-opportunity:init-read-failed",
                    "ReadLiveCommanderInitiative01 threw: " + ex.GetType().Name + " " + ex.Message);
                return 0.5f;
            }
        }

        private static void EmitReinforcementOpportunityTelemetry(int side, ReinforcementOpportunityDecision decision)
        {
            try
            {
                string sig = "TacticalReinforcementOpportunity|side=" + side
                    + "|outcome=" + decision.Opportunity
                    + "|reason=" + decision.Reason
                    + "|ratio=" + decision.CurrentRatio.ToString("0.00")
                    + "|enemyParityHrs=" + (decision.ParityHoursForEnemy >= 999f ? "never" : decision.ParityHoursForEnemy.ToString("0.0"))
                    + "|ownParityHrs=" + (decision.ParityHoursForOwn >= 999f ? "never" : decision.ParityHoursForOwn.ToString("0.0"))
                    + "|threshold=" + decision.AttackThreshold.ToString("0.00")
                    + "|window=" + decision.AttackWindowHours.ToString("0.0");
                string key = "tactical-reinforcement-opportunity:" + side;
                if (!TacticalTelemetry.ShouldEmit(_lastTelemetryAt, key, sig, Time.realtimeSinceStartup, TelemetrySeconds, false))
                    return;

                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Decision,
                    "TacticalReinforcementOpportunity",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(side)
                        .WithDecision("TacticalReinforcementOpportunity", decision.Opportunity.ToString(), sig)
                        .WithField("outcome", decision.Opportunity.ToString())
                        .WithField("reason", decision.Reason)
                        .WithField("currentRatio", decision.CurrentRatio)
                        .WithField("enemyParityHours", decision.ParityHoursForEnemy >= 999f ? -1f : decision.ParityHoursForEnemy)
                        .WithField("ownParityHours", decision.ParityHoursForOwn >= 999f ? -1f : decision.ParityHoursForOwn)
                        .WithField("attackThreshold", decision.AttackThreshold)
                        .WithField("attackWindowHours", decision.AttackWindowHours));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-reinforcement-opportunity:telemetry-failed",
                    "EmitReinforcementOpportunityTelemetry threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }
    }
}
