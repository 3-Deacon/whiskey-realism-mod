using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B5 group sector stance scorer. This is deliberately limited to stance
    // pressure; it does not issue movement, reserve, artillery, fallback, or
    // charge orders.
    [HarmonyPatch(typeof(AIBattle), "AdjustGroupAIStance")]
    internal static class BattleGroupStancePatch
    {
        private static readonly Dictionary<string, float> _lastLoggedAt = new Dictionary<string, float>();
        private static FieldInfo _macroAiField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _bunitsField;
        private static FieldInfo _unitsUsedField;
        private static FieldInfo _orderedStanceField;

        // Compiled-expression getter for the per-tick-per-group
        // Regiment.ai_stanceordered read. The legacy SafeIntField path uses
        // FieldInfo.GetValue which boxes + reflects on every call; this
        // delegate compiles down to a direct field load. Initialized lazily
        // on first use; degrades to the legacy SafeIntField path on
        // compile failure.
        private static Func<Regiment, int> _aiStanceOrderedGetter;
        private static bool _aiStanceOrderedGetterTried;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.battle-group-stance", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (!Enabled() || __instance == null) return;

                try
                {
                    Apply(__instance);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-group-stance:failed", "Tactical group sector stance failed: " + ex.Message);
                }
            }
        }

        private static void Apply(AIBattle battle)
        {
            // Three-tier read path (see TryApplyFromDecisionSnapshot for
            // detail): "cheap per-group" resolved cache → worker snapshot →
            // legacy ApplyGroup. The cache is populated on every
            // heavy-gate fire by
            // TacticalBattleCoordinatorRuntime.PopulateResolvedDecisionsForSide,
            // so the dominant per-tick cost collapses to a dict lookup
            // plus the cheap safety gates that need live Regiment state.
            //
            // Worst case (no orchestrator state, worker off, gate hasn't
            // fired yet): the fallback below runs ApplyGroup for every
            // commandable Regiment — the legacy pre-refactor path. We
            // keep this fallback intact so suppressed-side / pre-bootstrap
            // / config-flipped sessions still behave identically to the
            // pre-refactor build.
            if (TryApplyFromDecisionSnapshot(battle)) return;

            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            var units = SafeList(battle, ref _unitsUsedField, "unitsused");
            if (side < 0 || macro < 0 || bunits == null || units == null) return;

            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i] as Regiment;
                if (group == null || !TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(group.unittyp)) continue;
                ApplyGroup(bunits, side, macro, group, i);
            }
        }

        // Pre-computed decision read path.
        //
        // Three-tier lookup per Regiment:
        //   1. _resolvedDecisionsBySide (TacticalBattleCoordinatorRuntime
        //      cache, populated at heavy-gate-fire) — full doctrine + ledger
        //      + charge + scorer resolution baked in. Apply path is O(1):
        //      dict lookup + a few cheap safety gates + one ChangeStance.
        //      This is the "cheap per-group" hot path.
        //   2. Worker snapshot (TacticalDecisionSnapshot.Current) — partial
        //      pre-compute from the worker thread. Still calls the doctrine
        //      and ledger resolvers on main at apply time, but skips the
        //      live BuildGroupSector + DecideGroupStance recompute.
        //   3. ApplyGroup (legacy) — full resolver + sector rebuild + scorer
        //      call. Runs on cache miss (worker disabled, pre-bootstrap,
        //      gate hasn't fired yet, suppressed side, etc).
        //
        // Returns true whenever we owned the iteration (touched at least
        // one group); the postfix then short-circuits because the caller
        // already iterated all groups via one of the three branches.
        private static bool TryApplyFromDecisionSnapshot(AIBattle battle)
        {
            try
            {
                int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
                int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
                var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
                var units = SafeList(battle, ref _unitsUsedField, "unitsused");
                if (side < 0 || macro < 0 || bunits == null || units == null) return false;

                int allianceId = SafeAllianceForSide(bunits, side);
                if (allianceId < 0 || allianceId >= 2) return false;

                bool workerOn = Plugin.EnableTacticalOrchestratorWorker != null
                    && Plugin.EnableTacticalOrchestratorWorker.Value;
                TacticalDecisionSnapshot snapshot = workerOn ? TacticalDecisionSnapshot.Current : null;
                bool snapshotHasData = snapshot != null && snapshot != TacticalDecisionSnapshot.Empty;

                bool touchedAny = false;
                for (int i = 0; i < units.Count; i++)
                {
                    var group = units[i] as Regiment;
                    if (group == null) continue;
                    if (!TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(group.unittyp)) continue;

                    int instanceId;
                    try { instanceId = group.GetInstanceID(); }
                    catch { continue; }

                    // Tier 1: cheap per-group cache. Populated on every
                    // heavy-gate fire in DriveTickCycle; covers every
                    // commandable Regiment for the firing side.
                    TacticalResolvedGroupDecision resolved;
                    if (TacticalBattleCoordinator.TryGetResolvedGroupDecision(allianceId, instanceId, out resolved))
                    {
                        ApplyResolvedDecisionFast(bunits, side, macro, group, resolved);
                        touchedAny = true;
                        continue;
                    }

                    // Tier 2: worker snapshot — partial pre-compute.
                    if (snapshotHasData)
                    {
                        TacticalGroupStanceDecision cachedDecision;
                        TacticalSectorAssessment cachedSector;
                        if (snapshot.TryGetGroupStance(allianceId, instanceId, out cachedDecision)
                            && snapshot.TryGetGroupSector(allianceId, instanceId, out cachedSector))
                        {
                            ApplyCachedDecision(bunits, side, macro, group, cachedDecision, cachedSector);
                            touchedAny = true;
                            continue;
                        }
                    }

                    // Tier 3: legacy decide-and-write path.
                    ApplyGroup(bunits, side, macro, group, i);
                    touchedAny = true;
                }

                return touchedAny;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// "Cheap per-group" apply path (2026-05-20). Hot path: dict lookup
        /// already happened; here we only enforce the per-tick safety gates
        /// that depend on live Regiment state, then dispatch the cached
        /// action. No resolver walks, no sector rebuild, no
        /// DecideGroupStance recompute — those ran once at the
        /// heavy-gate-fire boundary in
        /// <c>TacticalBattleCoordinatorRuntime.PopulateResolvedDecisionsForSide</c>.
        /// </summary>
        private static void ApplyResolvedDecisionFast(
            BattleUnits bunits,
            int side,
            int macro,
            Regiment group,
            TacticalResolvedGroupDecision resolved)
        {
            // Per-tick safety gates. WL/order-friction live on the apply
            // path because they consume Regiment state that can change
            // every vanilla tick (player control toggle, fresh queued
            // orders, path interrupts). All are cheap relative to the
            // resolver walks they replace.
            if (!WlAllowsControl(group)) return;
            if (!OrderFrictionAllowsChange(group)) return;

            int vanillaOrdered = ReadAiStanceOrdered(group);

            // Charge-override branch. Cache decided WHETHER to demote at
            // gate-fire time; live reaction context is consulted here only
            // for the demote telemetry detail. If vanilla raced from 4 to
            // something else, fall through to FinalStance (the cached
            // charge override no longer applies).
            if (resolved.IsChargeOverride && vanillaOrdered == 4)
            {
                switch (resolved.ChargeAction)
                {
                    case ChargeOverrideAction.PreserveVanilla:
                    case ChargeOverrideAction.DenyExplicit:
                    case ChargeOverrideAction.None:
                        LogChargePreserved(side, group, resolved.Reason);
                        return;
                    case ChargeOverrideAction.DemoteToHold:
                        if (!AllowsVanillaWrites())
                        {
                            LogChargePreserved(side, group, "mode-monitor-only");
                            return;
                        }
                        var reaction = TacticalReactionContext.Shared.GetReaction(SafeInstanceId(group));
                        DemoteCharge(bunits, group, side, reaction);
                        return;
                }
                return;
            }

            // If vanilla raced INTO charge (vanillaOrdered == 4) but the
            // cache didn't flag this group as a charge override (because
            // it wasn't at 4 at gate-fire time), preserve vanilla's
            // charge. The cached FinalStance would write a non-charge
            // value over the fresh vanilla write, which is wrong.
            if (vanillaOrdered == 4)
            {
                LogChargePreserved(side, group, "vanilla-charge-preserved");
                return;
            }

            // Vanilla-retreat live check: macro can flip to 3 between
            // gate fires when morale collapses. The pre-compute skips
            // doctrine and ledger when macro==3 (mirroring the legacy
            // path), so the cached FinalStance for a now-retreating side
            // is whatever the scorer would have written PRE-retreat. We
            // must not overwrite vanilla's retreat with that stale
            // decision. Charge override above is intentionally not
            // gated on this — charge demote/preserve telemetry still
            // matters during retreat.
            if (macro == 3) return;

            // Non-charge branch — write cached FinalStance.
            if (resolved.Kind != TacticalDoctrineDecisionKind.Apply) return;
            if (resolved.FinalStance < 0) return;
            if (resolved.FinalStance == vanillaOrdered) return;
            if (resolved.FinalStance == 4) return;
            if (resolved.FinalStance > 3) return;
            if (!AllowsVanillaWrites()) return;

            var gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return;

            bunits.ChangeStance(gameObject, resolved.FinalStance, immediate: false, overwriteaigroups: false);
            group.ai_stance = resolved.FinalStance;
            group.ai_stanceordered = resolved.FinalStance;
            group.lastaistancechangetime = GameVars.currenttimefromstart;
            LogResolvedDecision(side, group, resolved);
        }

        private static int ReadAiStanceOrdered(Regiment group)
        {
            if (group == null) return 0;

            if (!_aiStanceOrderedGetterTried)
            {
                _aiStanceOrderedGetterTried = true;
                try
                {
                    var field = AccessTools.Field(typeof(Regiment), "ai_stanceordered");
                    if (field != null)
                    {
                        var p = Expression.Parameter(typeof(Regiment), "r");
                        var body = Expression.Field(p, field);
                        _aiStanceOrderedGetter = Expression.Lambda<Func<Regiment, int>>(body, p).Compile();
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("[BattleGroupStancePatch] ai_stanceordered getter compile failed: "
                        + e.GetType().Name + " " + e.Message);
                }
            }

            var getter = _aiStanceOrderedGetter;
            if (getter != null)
            {
                try { return getter(group); }
                catch { /* fall through to reflection fallback */ }
            }

            return SafeIntField(group, ref _orderedStanceField, "ai_" + "stanceordered", group.ai_stanceordered);
        }

        /// <summary>
        /// Apply-cached fast path (gap-fill chunk 1). Replicates
        /// <see cref="ApplyGroup"/>'s sequence but reads the sector +
        /// default-scorer decision from the worker snapshot:
        /// 1. WlAllowsControl + OrderFrictionAllowsChange enforced on main
        ///    (require live Regiment state).
        /// 2. TryApplyDoctrineStance using cached sector.
        /// 3. Charge (vanillaOrdered == 4) branch — unchanged.
        /// 4. TryApplyLedgerTaskStance using cached sector.
        /// 5. Fallthrough: write cached default-scorer decision.
        /// </summary>
        private static void ApplyCachedDecision(
            BattleUnits bunits,
            int side,
            int macro,
            Regiment group,
            TacticalGroupStanceDecision decision,
            TacticalSectorAssessment sector)
        {
            if (!WlAllowsControl(group)) return;
            if (!OrderFrictionAllowsChange(group)) return;

            int vanillaOrdered = SafeIntField(group, ref _orderedStanceField, "ai_" + "stanceordered", group.ai_stanceordered);

            if (TryApplyDoctrineStance(bunits, side, macro, group, vanillaOrdered, sector))
                return;

            if (vanillaOrdered == 4)
            {
                if (!Plugin.Instance.EnableTacticalChargeDenial.Value || !LocalReactionProducerEnabled())
                {
                    LogChargePreserved(side, group, "vanilla-charge-preserved");
                    return;
                }

                var reaction = TacticalReactionContext.Shared.GetReaction(SafeInstanceId(group));
                if (IsExplicitChargeDenial(reaction))
                {
                    if (!AllowsVanillaWrites())
                    {
                        LogChargePreserved(side, group, "mode-monitor-only");
                        return;
                    }

                    DemoteCharge(bunits, group, side, reaction);
                    return;
                }

                LogChargePreserved(side, group, "vanilla-charge-preserved");
                return;
            }

            if (TryApplyLedgerTaskStance(bunits, side, macro, group, vanillaOrdered, sector))
                return;

            // Fallthrough — apply the worker's default-scorer decision.
            // We do NOT call DecideGroupStance here; the worker already
            // produced this decision on its thread.
            if (!Plugin.Instance.EnableTacticalGroupSectorStance.Value) return;
            if (decision.Kind != TacticalDoctrineDecisionKind.Apply) return;
            if (decision.GroupStance == vanillaOrdered) return;
            if (decision.GroupStance == 4) return;
            if (decision.GroupStance < 0 || decision.GroupStance > 3) return;
            if (!AllowsVanillaWrites()) return;

            var gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return;

            bunits.ChangeStance(gameObject, decision.GroupStance, immediate: false, overwriteaigroups: false);
            group.ai_stance = decision.GroupStance;
            group.ai_stanceordered = decision.GroupStance;
            group.lastaistancechangetime = GameVars.currenttimefromstart;
            LogDecision(side, group, sector, decision);
        }

        private static int SafeAllianceForSide(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.alliance == null) return -1;
                if (side < 0 || side >= bunits.alliance.Length) return -1;
                return bunits.alliance[side];
            }
            catch
            {
                return -1;
            }
        }

        private static void ApplyGroup(BattleUnits bunits, int side, int macro, Regiment group, int index)
        {
            if (!WlAllowsControl(group)) return;
            if (!OrderFrictionAllowsChange(group)) return;

            int vanillaOrdered = SafeIntField(group, ref _orderedStanceField, "ai_" + "stanceordered", group.ai_stanceordered);
            var sector = BuildGroupSector(group, index);

            if (TryApplyDoctrineStance(bunits, side, macro, group, vanillaOrdered, sector))
                return;

            if (vanillaOrdered == 4)
            {
                if (!Plugin.Instance.EnableTacticalChargeDenial.Value || !LocalReactionProducerEnabled())
                {
                    LogChargePreserved(side, group, "vanilla-charge-preserved");
                    return;
                }

                var reaction = TacticalReactionContext.Shared.GetReaction(SafeInstanceId(group));
                if (IsExplicitChargeDenial(reaction))
                {
                    if (!AllowsVanillaWrites())
                    {
                        LogChargePreserved(side, group, "mode-monitor-only");
                        return;
                    }

                    DemoteCharge(bunits, group, side, reaction);
                    return;
                }

                LogChargePreserved(side, group, "vanilla-charge-preserved");
                return;
            }

            if (TryApplyLedgerTaskStance(bunits, side, macro, group, vanillaOrdered, sector))
                return;

            if (!Plugin.Instance.EnableTacticalGroupSectorStance.Value) return;

            var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
                vanillaOrdered,
                macro,
                sector,
                true,
                true));

            if (decision.Kind != TacticalDoctrineDecisionKind.Apply) return;
            if (decision.GroupStance == vanillaOrdered) return;
            if (decision.GroupStance == 4) return;
            if (decision.GroupStance < 0 || decision.GroupStance > 3) return;
            if (!AllowsVanillaWrites()) return;

            var gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return;

            bunits.ChangeStance(gameObject, decision.GroupStance, immediate: false, overwriteaigroups: false);
            group.ai_stance = decision.GroupStance;
            group.ai_stanceordered = decision.GroupStance;
            group.lastaistancechangetime = GameVars.currenttimefromstart;
            LogDecision(side, group, sector, decision);
        }

        private static bool TryApplyDoctrineStance(
            BattleUnits bunits,
            int side,
            int macro,
            Regiment group,
            int vanillaOrdered,
            TacticalSectorAssessment sector)
        {
            if (macro == 3) return false;
            if (!TacticalDoctrineOrderResolver.TryResolve(group, out CommandDoctrineOrder order, out BattlefieldPictureSnapshot picture)) return false;

            DoctrineStanceDecision decision = DoctrineConsumerDecisions.DecideStance(
                order,
                DoctrineConsumerDecisions.EnemyMainLineExposed(order, picture),
                sector.Odds);
            if (decision.Action == DoctrineConsumerAction.Observe) return false;

            int stance = decision.Action == DoctrineConsumerAction.Allow ? 3 : 2;
            if (stance == vanillaOrdered) return true;
            if (!AllowsVanillaWrites()) return true;

            var gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return true;

            bunits.ChangeStance(gameObject, stance, immediate: false, overwriteaigroups: false);
            group.ai_stance = stance;
            group.ai_stanceordered = stance;
            group.lastaistancechangetime = GameVars.currenttimefromstart;
            LogDoctrineStanceDecision(side, group, order, decision, stance);
            return true;
        }

        private static bool TryApplyLedgerTaskStance(BattleUnits bunits, int side, int macro, Regiment group, int vanillaOrdered, TacticalSectorAssessment sector)
        {
            if (macro == 3) return true;
            if (!TacticalLedgerStateResolver.TryResolve(group, out CommandNodeOperationalState state)) return false;

            int stance = StanceForTask(state.Task, vanillaOrdered, sector);
            if (stance < 0) return true;
            if (stance == vanillaOrdered) return true;
            if (stance == 4 || stance < 0 || stance > 3) return true;
            if (!AllowsVanillaWrites()) return true;

            var gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return true;

            bunits.ChangeStance(gameObject, stance, immediate: false, overwriteaigroups: false);
            group.ai_stance = stance;
            group.ai_stanceordered = stance;
            group.lastaistancechangetime = GameVars.currenttimefromstart;
            LogLedgerTaskDecision(side, group, state, stance);
            return true;
        }

        private static int StanceForTask(CommandTaskType task, int vanillaOrdered, TacticalSectorAssessment sector)
        {
            if ((task == CommandTaskType.HoldObjective ||
                 task == CommandTaskType.HoldChoke ||
                 task == CommandTaskType.FixEnemy) &&
                TacticalDoctrineScorer.ShouldCommitVisibleWeakPoint(sector))
                return 3;

            switch (task)
            {
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                    return 3;
                case CommandTaskType.FixEnemy:
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                case CommandTaskType.ReserveWait:
                case CommandTaskType.FallBackToLine:
                    return 2;
                default:
                    return -1;
            }
        }

        private static void LogDoctrineStanceDecision(
            int side,
            Regiment group,
            CommandDoctrineOrder order,
            DoctrineStanceDecision decision,
            int stance)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + order.Task + "|" +
                decision.Action + "|" + stance + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "group-doctrine-consumer", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalGroupDecision] side=" + side +
                " group=" + SafeInstanceId(group) +
                " stance=" + stance +
                " doctrineTask=" + order.Task +
                " action=" + decision.Action +
                " reason=" + decision.Reason, TelemetryLayer.Tactical);
        }

        private static void LogLedgerTaskDecision(int side, Regiment group, CommandNodeOperationalState state, int stance)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + state.Task + "|" + stance;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "group-ledger-task", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalGroupDecision] side=" + side +
                " group=" + SafeInstanceId(group) +
                " stance=" + stance +
                " ledgerTask=" + state.Task +
                " ledgerRole=" + state.Role +
                " reason=operations-ledger-task", TelemetryLayer.Tactical);
        }

        private static bool IsExplicitChargeDenial(TacticalLocalReactionDecision reaction)
        {
            return reaction.Reason != "no-decision" &&
                reaction.Reaction == LocalReaction.DenyCharge;
        }

        private static void DemoteCharge(
            BattleUnits bunits,
            Regiment group,
            int side,
            TacticalLocalReactionDecision reaction)
        {
            var gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return;

            bunits.ChangeStance(gameObject, 3, immediate: false, overwriteaigroups: false);
            group.ai_stance = 3;
            group.ai_stanceordered = 3;
            group.lastaistancechangetime = GameVars.currenttimefromstart;

            string signature = side + "|" + SafeInstanceId(group) + "|" + reaction.Reaction + "|" + reaction.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "b6c-charge-deny-stance", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalChargeDeny] surface=stance side=" + side +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group) +
                " reaction=" + reaction.Reaction +
                " reason=" + reaction.Reason, TelemetryLayer.Tactical);
        }

        private static void LogChargePreserved(int side, Regiment group, string reason)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "b6c-charge-preserved", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalChargePreserved] surface=stance side=" + side +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group) +
                " reason=" + reason, TelemetryLayer.Tactical);
        }

        private static TacticalSectorAssessment BuildGroupSector(Regiment group, int index)
        {
            Regiment closest = TacticalFogOfWarContact.ClosestVisibleEnemy(group);
            return TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
                index,
                group != null ? Math.Max(group.groupowninrange, group.groupstrengthaigroup) : 0f,
                group != null ? group.groupenemiesinrange : 0f,
                SumEnemyStrengthWithinAngle(group),
                closest != null ? closest.strength : 0f,
                closest != null ? closest.unittyp : -1,
                closest != null ? SafeName(closest) : string.Empty,
                closest != null && closest.isrouted,
                closest != null && closest.permanentlydetached,
                group != null && (group.flanksthreated > 0f || group.outflanked > 0),
                group != null && (group.covervalue > 0.5f || group.fortinrange)));
        }

        private static bool WlAllowsControl(Regiment group)
        {
            var decision = TacticalWlActionGuard.Decide(
                true,
                DLC_WL.dlc_scenarioactive,
                TacticalWlGuardAction.FeudMovement,
                group != null && group.dlcw_isundercommander,
                group != null && group.dlcw_isundercommander,
                AttachedUnitUnderPlayerCommander(group));
            return decision.Allow;
        }

        private static bool AttachedUnitUnderPlayerCommander(Regiment group)
        {
            if (group == null || group.allattachedunits == null) return false;
            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                var unit = group.allattachedunits[i];
                if (unit != null && unit.dlcw_isundercommander) return true;
            }

            return false;
        }

        private static bool OrderFrictionAllowsChange(Regiment group)
        {
            return TacticalOrderSettlementGate.Evaluate(new TacticalOrderSettlementGate.Input
            {
                OrderQueueCount = SafeOrderQueueCount(group),
                OrderState = group != null ? group.orderstate : -1,
                RegimentPaths = group != null ? group.regimentpaths : 0,
                PathInterrupted = group != null && group.pathinterrupted,
                MovementMode = group != null ? group.movementmode : -1,
                ActiveMove = HasActiveMoveOrder(group)
            }).AllowChange;
        }

        private static float SumEnemyStrengthWithinAngle(Regiment group)
        {
            try
            {
                if (group == null ||
                    group.unitrange == null ||
                    group.unitrange.enemystrengthwithinangle == null ||
                    !TacticalFogOfWarContact.HasVisibleEnemy(group))
                    return 0f;

                float total = 0f;
                for (int i = 0; i < group.unitrange.enemystrengthwithinangle.Length; i++)
                    total += Math.Max(0f, group.unitrange.enemystrengthwithinangle[i]);
                return total;
            }
            catch
            {
                return 0f;
            }
        }

        private static bool HasActiveMoveOrder(Regiment unit)
        {
            try
            {
                return unit != null &&
                    unit.ordertypeactive != null &&
                    unit.ordertypeactive.Length > 1 &&
                    (unit.ordertypeactive[0] || unit.ordertypeactive[1]);
            }
            catch
            {
                return false;
            }
        }

        private static int SafeOrderQueueCount(Regiment group)
        {
            try { return group != null && group.orderqueue != null ? group.orderqueue.Count : 0; }
            catch { return 1; }
        }

        private static void LogResolvedDecision(int side, Regiment group, TacticalResolvedGroupDecision resolved)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + resolved.FinalStance + "|" + resolved.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "group-resolved", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalGroupDecision] side=" + side +
                " group=" + SafeInstanceId(group) +
                " stance=" + resolved.FinalStance +
                " reason=" + resolved.Reason +
                " doctrine=" + resolved.FromDoctrine +
                " ledger=" + resolved.FromLedger, TelemetryLayer.Tactical);
        }

        private static void LogDecision(
            int side,
            Regiment group,
            TacticalSectorAssessment sector,
            TacticalGroupStanceDecision decision)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + sector.SectorId + "|" +
                decision.GroupStance + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "group-decision", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalGroupDecision] side=" + side +
                " group=" + SafeInstanceId(group) +
                " sector=" + sector.SectorId +
                " stance=" + decision.GroupStance +
                " mission=" + sector.Mission +
                " reason=" + decision.Reason +
                " odds=" + sector.Odds.ToString("0.00") +
                " confidence=" + sector.Confidence.ToString("0.00"), TelemetryLayer.Tactical);
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                (Plugin.Instance.EnableTacticalGroupSectorStance.Value ||
                    Plugin.Instance.TacticalOperationsLedgerAllowsWrites);
        }

        private static bool AllowsVanillaWrites()
        {
            try
            {
                return Plugin.Instance != null &&
                    TacticalCommanderModePolicy.AllowsWrites(Plugin.Instance.TacticalCommanderModeValue);
            }
            catch
            {
                return false;
            }
        }

        private static bool LocalReactionProducerEnabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.EnableTacticalObserver.Value &&
                Plugin.Instance.EnableTacticalCommanderIntentDoctrine.Value &&
                Plugin.Instance.EnableTacticalLocalReactionDoctrine.Value;
        }

        private static int SafeIntField(object instance, ref FieldInfo cache, string name, int fallback)
        {
            try
            {
                if (instance == null) return fallback;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                if (cache == null) return fallback;
                return Convert.ToInt32(cache.GetValue(instance));
            }
            catch
            {
                return fallback;
            }
        }

        private static T SafeField<T>(object instance, ref FieldInfo cache, string name) where T : class
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as T : null;
            }
            catch
            {
                return null;
            }
        }

        private static IList SafeList(object instance, ref FieldInfo cache, string name)
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as IList : null;
            }
            catch
            {
                return null;
            }
        }

        private static GameObject UnityObject(Regiment unit)
        {
            try
            {
                return unit != null ? ((Component)unit).gameObject : null;
            }
            catch
            {
                return null;
            }
        }

        private static string SafeName(Regiment group)
        {
            try
            {
                return group != null ? ((Component)group).gameObject.name : "<null>";
            }
            catch
            {
                return "<err>";
            }
        }

        private static int SafeInstanceId(UnityEngine.Object obj)
        {
            try
            {
                return obj != null ? obj.GetInstanceID() : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
