using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // B-61state (2026-10-14): orchestrator inputs the posture producers
    // (Tactical/Battle/Producers, plan BC5a) read, refreshed on the coordinator
    // tick (CheckGlobalAIStrategy, 5866) before the same UpdateAITasks reaches the
    // arbiter (AdjustGroupFormations, 5870). Two calls per side per coordinator
    // tick (BC14): RefreshPickInputs first, and before the initial plan pick;
    // RefreshPostureCaches after the direct-child cycle, where the old single
    // Refresh ran.
    //   RefreshPickInputs (everything a plan pick reads):
    //   - the battle stance: ArmyEvidenceBuilder.ReadBattleStance (GetBattleType
    //     86095, objective points held) into ArmyOrchestrator.UpdateBattleStance,
    //     ungated: the side's one stance reader (BC9; the readiness tie-break the
    //     division play used to read is gone);
    //   - live commander initiative: UpdateLiveCommanderInitiative;
    //   - reinforcement opportunity: UpdateReinforcementOpportunity from
    //     ArmyEvidenceBuilder.BuildForceBalance (gated by
    //     Enable Tactical Reinforcement Opportunity Doctrine).
    //   RefreshPostureCaches (they read this tick's intents):
    //   - leaf brigade map: ArmyOrchestrator.UpdateLeafBrigadeMap(tree) from the
    //     alliance's extended probe tree, cached per snapshot roster signature,
    //     without the snapshot's PlayerOwned units (PostureProbeFilter);
    //   - the AI pool: the side's AIBattle.unitsused (85227-85262, the groups
    //     vanilla's own AI orders), which splits #61's group pass from its
    //     leaf-brigade pass.
    public static class ArmyPostureInputsRuntime
    {
        static ArmyPostureInputsRuntime()
        {
            BattleScope.Register("posture-inputs", ResetForBattleEnd);
        }

        private const float TelemetrySeconds = 30f;
        private static readonly Dictionary<string, float> _lastTelemetryAt = new Dictionary<string, float>();
        private static readonly Dictionary<int, CachedProbes> _probeCache = new Dictionary<int, CachedProbes>();

        private readonly struct CachedProbes
        {
            public CachedProbes(ProbeRosterSignature signature, IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> probes)
            {
                Signature = signature;
                Probes = probes;
            }

            public ProbeRosterSignature Signature { get; }
            public IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> Probes { get; }
        }

        // The pick inputs: everything a plan pick reads, refreshed before every pick (BC14 requirement-stance-before-pick:
        // the first pick used to run at the default Meeting stance). Runs first in the tick and before the initial pick.
        public static void RefreshPickInputs(AIBattle battle, BattleUnits bunits, int side, ArmyOrchestrator army)
        {
            try
            {
                if (battle == null || bunits == null || army == null || bunits.alliance == null) return;
                if (side < 0 || side >= bunits.alliance.Length) return;
                int allianceId = bunits.alliance[side];
                if (allianceId < 0 || allianceId >= 2) return;

                // BC9: the side's one battle stance (the director, node and doctrine
                // assignment, the posture guard, the probe gate, the division play and
                // #45's ledger cap read it), whatever the opportunity doctrine flag below.
                army.UpdateBattleStance(ArmyEvidenceBuilder.ReadBattleStance(bunits, allianceId));

                if (Plugin.EnableTacticalReinforcementOpportunityDoctrine == null ||
                    !Plugin.EnableTacticalReinforcementOpportunityDoctrine.Value)
                    return;

                army.UpdateLiveCommanderInitiative(ReadLiveCommanderInitiative01(bunits, side));

                // Aggression is the PersonalityVector baseline mapped from
                // [-1, 1] to [0, 1]; live initiative is the vanilla reading.
                float commanderAggression01 = (army.CommanderPersonality.Aggression + 1f) * 0.5f;
                if (commanderAggression01 < 0f) commanderAggression01 = 0f;
                if (commanderAggression01 > 1f) commanderAggression01 = 1f;

                // The casualty escalation threshold reads the commander's casualty tolerance (BC13).
                float commanderCasualtyTolerance01 = Battle.CommanderPersonalityDerivation.To01(army.CommanderPersonality.CasualtyTolerance);
                var forceBalance = ArmyEvidenceBuilder.BuildForceBalance(
                    battle, allianceId, army.LiveCommanderInitiative01, commanderAggression01, commanderCasualtyTolerance01);
                if (forceBalance.HasValue)
                {
                    army.UpdateReinforcementOpportunity(forceBalance.Value);
                    EmitReinforcementOpportunityTelemetry(side, army.CurrentReinforcementOpportunity, forceBalance.Value.QualityFactor);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("army-posture-inputs:refresh-failed",
                    "ArmyPostureInputsRuntime.RefreshPickInputs failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // The posture caches, which read this tick's intents: the AI pool and the leaf brigade map. Stays where Refresh ran, after
        // the direct-child cycle.
        public static void RefreshPostureCaches(AIBattle battle, BattleUnits bunits, int side, ArmyOrchestrator army)
        {
            try
            {
                if (battle == null || bunits == null || army == null || bunits.alliance == null) return;
                if (side < 0 || side >= bunits.alliance.Length) return;
                int allianceId = bunits.alliance[side];
                if (allianceId < 0 || allianceId >= 2) return;

                RefreshAiPool(battle, allianceId);
                var probes = ExtendedProbes(bunits, allianceId);
                if (probes != null && probes.Count > 0)
                    army.UpdateLeafBrigadeMap(TacticalCommandTreeProbe.BuildTree(probes, 0));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("army-posture-inputs:caches-failed",
                    "ArmyPostureInputsRuntime.RefreshPostureCaches failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        /// <summary>True when the group is in the side's AI pool (AIBattle.unitsused) this tick.</summary>
        public static bool InAiPool(int allianceId, int instanceId)
        {
            return allianceId >= 0 && allianceId < _aiPool.Length && _aiPool[allianceId].Contains(instanceId);
        }

        // Battle end (BattleScope).
        public static void ResetForBattleEnd()
        {
            _probeCache.Clear();
            _lastTelemetryAt.Clear();
            for (int a = 0; a < _aiPool.Length; a++) _aiPool[a].Clear();
        }

        private static readonly HashSet<int>[] _aiPool = { new HashSet<int>(), new HashSet<int>() };

        // UnitRecord.InstanceId is the GameObject instance id (BattleSnapshotBuilder).
        private static void RefreshAiPool(AIBattle battle, int allianceId)
        {
            HashSet<int> pool = _aiPool[allianceId];
            pool.Clear();
            var units = battle.unitsused;
            if (units == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                Regiment group = units[i];
                if ((UnityEngine.Object)group == null) continue;
                GameObject go = ((Component)group).gameObject;
                if (go != null) pool.Add(go.GetInstanceID());
            }
        }

        // Player-owned units (the W&L player's command and descendants,
        // take-overs, the player side outside W&L) are left out of the probe tree
        // by the snapshot's PlayerOwned flag, so the role cascade / leaf map never
        // assigns them a role and no AI brigade waits on them as support. The
        // player's AI ancestors stay.
        private static IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> ExtendedProbes(BattleUnits bunits, int allianceId)
        {
            if (!BattleCore.TryGetSnapshot(allianceId, out BattleSnapshot snapshot)) return null;
            ProbeRosterSignature signature = RosterSignature(snapshot, bunits.GetInstanceID());
            if (_probeCache.TryGetValue(allianceId, out CachedProbes cached) &&
                cached.Probes != null &&
                cached.Signature.Equals(signature))
                return cached.Probes;

            var probes = PostureProbeFilter.WithoutPlayerOwned(
                DirectChildDiscovery.BuildExtendedProbesForAlliance(allianceId),
                snapshot);
            // An empty list means no snapshot was built for the side yet (or the
            // build degraded); cached, it would stand until the roster changed.
            if (probes != null && probes.Count > 0) _probeCache[allianceId] = new CachedProbes(signature, probes);
            return probes;
        }

        // The snapshot roster without player-owned units, so a change in the
        // W&L command set also invalidates the probe cache.
        private static ProbeRosterSignature RosterSignature(BattleSnapshot snapshot, int battleKey)
        {
            int count = 0;
            int routed = 0;
            UnitRecord[] own = snapshot.Own;
            for (int i = 0; i < snapshot.OwnCount; i++)
            {
                if ((own[i].Flags & UnitFlags.PlayerOwned) != 0) continue;
                count++;
                if (PostureSense.Routed(own[i])) routed++;
            }

            return new ProbeRosterSignature(snapshot.Alliance, count, routed, battleKey);
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

        // qualityFactor: the BC8 force-balance quality factor in CurrentRatio (84862), 1 when
        // either side's quality is unknown or the weighting is off.
        private static void EmitReinforcementOpportunityTelemetry(int side, ReinforcementOpportunityDecision decision, float qualityFactor)
        {
            try
            {
                string sig = "TacticalReinforcementOpportunity|side=" + side
                    + "|outcome=" + decision.Opportunity
                    + "|reason=" + decision.Reason
                    + "|ratio=" + decision.CurrentRatio.ToString("0.00")
                    + "|quality=" + qualityFactor.ToString("0.00")
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
                        .WithField("qualityFactor", qualityFactor)
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
