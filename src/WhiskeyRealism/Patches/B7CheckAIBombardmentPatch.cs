using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.CheckAIBombardment(Regiment) at decompile line 3869 evaluates
    // each artillery sub-unit and may set combatbehaviorordered to 9 (bombardment),
    // or cancel an active bombardment. This Postfix reads the
    // post-vanilla state, runs TacticalArtilleryDoctrine.Score, and selectively
    // rewrites combatbehaviorordered when the doctrine output disagrees with vanilla.
    // Default-off behind Plugin.EnableTacticalArtilleryDoctrine.
    [HarmonyPatch(typeof(AIBattle), "CheckAIBombardment")]
    public static class B7CheckAIBombardmentPatch
    {
        private static FieldInfo _bunitsField;
        private static FieldInfo _isPlayerAiOrFeudField;
        private static MethodInfo _performAiActionDlcWlMethod;

        [HarmonyPostfix]
        public static void Postfix(AIBattle __instance, Regiment aigroup)
        {
            if (Plugin.EnableTacticalArtilleryDoctrine == null) return;
            if (!Plugin.EnableTacticalArtilleryDoctrine.Value) return;
            if (aigroup == null) return;
            if (aigroup.ai_stance != 3 && aigroup.ai_stance != 4) return;

            try
            {
                OnceLog.Info("b7-check-ai-bombardment", "B7 CheckAIBombardment Postfix wired.");

                var allUnits = aigroup.allattachedunits;
                if (allUnits == null) return;

                BattleUnits bunits = BattleUnits(__instance);
                if (bunits == null) return;

                int? isPlayerAiOrFeud = IsPlayerAiOrFeud(__instance);
                if (!isPlayerAiOrFeud.HasValue) return;
                if (!HasPerformAiActionDlcWl()) return;

                for (int i = 0; i < allUnits.Length; i++)
                {
                    var unit = allUnits[i];
                    if (!PassesVanillaWriteEnvelope(__instance, unit, aigroup, isPlayerAiOrFeud.Value)) continue;

                    var snapshot = BuildSnapshot(unit, aigroup, isPlayerAiOrFeud.Value);

                    var screenInput = TacticalArtilleryInputAdapter.ToSupportScreenInput(snapshot);
                    var screenResult = TacticalSupportScreen.Score(screenInput);

                    DoctrineArtilleryDecision doctrineDecision = default(DoctrineArtilleryDecision);
                    if (TryResolveDoctrineOrder(aigroup, out CommandDoctrineOrder doctrineOrder, out BattlefieldPictureSnapshot picture))
                    {
                        doctrineDecision = DoctrineConsumerDecisions.DecideArtillery(
                            doctrineOrder,
                            DoctrineConsumerDecisions.EnemyMainLineExposed(doctrineOrder, picture),
                            HasFriendlyCloseRangeRisk(unit, unit.firerange));
                    }

                    var doctrineInput = new TacticalArtilleryDoctrine.Input
                    {
                        ScreenResult = screenResult,
                        AmmoTotalRatio = snapshot.AmmoTotalRatio,
                        CanisterAmmo = snapshot.CanisterAmmo,
                        ClosestEnemyDistance = snapshot.ClosestEnemyDistance,
                        UnitFireRange = unit.firerange,
                        EnemyArtilleryVisible = HasEnemyArtilleryInFireRange(unit),
                        CombatBehaviorOrdered = snapshot.CombatBehaviorOrdered,
                        AiFeudStance = snapshot.AiFeudStance,
                        IsPlayerAiOrFeud = snapshot.IsPlayerAiOrFeud,
                        DoctrineDecision = doctrineDecision,
                    };
                    var decision = TacticalArtilleryDoctrine.Score(doctrineInput);

                    ApplyDecision(bunits, unit, snapshot, decision);
                }
            }
            catch (System.Exception ex)
            {
                OnceLog.Warning("b7-check-ai-bombardment-error", "[B7] CheckAIBombardment Postfix error: " + ex.Message);
            }
        }

        private static TacticalArtilleryInputAdapter.Snapshot BuildSnapshot(Regiment unit, Regiment aigroup, int isPlayerAiOrFeud)
        {
            float ammoTotal = 0f;
            float canister = 0f;
            int slots = 0;
            if (unit.ammo != null)
            {
                slots = unit.ammo.Length;
                for (int i = 0; i < slots; i++) ammoTotal += unit.ammo[i];
                if (slots > 2) canister = unit.ammo[2];
            }
            float ammoRatio = (slots > 0) ? ammoTotal / slots : 0f;

            float fallbackThreshold = 0.4f;
            try
            {
                if (GamePrefs.moraletriggerforfallbackifenemyclose != null
                    && aigroup.ai_stance >= 0
                    && aigroup.ai_stance < GamePrefs.moraletriggerforfallbackifenemyclose.Length)
                {
                    fallbackThreshold = GamePrefs.moraletriggerforfallbackifenemyclose[aigroup.ai_stance];
                }
            }
            catch { /* use default */ }

            float closestEnemy = 9999f;
            try
            {
                if (unit.unitrange != null && unit.unitrange.closestenemyunitfardistance > 0f)
                    closestEnemy = unit.unitrange.closestenemyunitfardistance;
            }
            catch { }

            int infCavScreen = CountInfCavScreen(unit);

            float dangerRadius = GamePrefs.artilleryfallbackenemyclosedist;

            float volleyDwell = System.Math.Max(0f,
                (unit.lastfiredshottime + GamePrefs.aritimetowaitbeforemovingcloser) - GameVars.currenttimefromstart);

            return new TacticalArtilleryInputAdapter.Snapshot
            {
                UnitTyp = unit.unittyp,
                Guns = unit.guns,
                IsRouted = unit.isrouted,
                MarkedForRout = unit.markedforrout,
                AmmoTotalRatio = ammoRatio,
                CanisterAmmo = canister,
                Morale = unit.morale,
                BattleStartMorale = unit.battlestartmorale,
                BattleStartMoraleInitialized = unit.battlestartmorale >= 0f,
                DangerRadius = dangerRadius,
                ClosestEnemyDistance = closestEnemy,
                InfCavScreenCount = infCavScreen,
                AiFeudStance = aigroup.ai_feudstance,
                IsPlayerAiOrFeud = isPlayerAiOrFeud,
                FallbackThreshold = fallbackThreshold,
                CombatBehaviorOrdered = unit.combatbehaviorordered,
                VolleyDwellRemaining = volleyDwell,
            };
        }

        private static int CountInfCavScreen(Regiment unit)
        {
            int count = 0;
            try
            {
                if (unit.unitrange == null || unit.unitrange.temp_owninrangeregs == null) return 0;
                for (int i = 0; i < unit.unitrange.temp_owninrangeregs.Count; i++)
                {
                    var friend = unit.unitrange.temp_owninrangeregs[i];
                    if (friend == null) continue;
                    if (friend.isrouted || friend.markedforrout) continue;
                    if (friend.unittyp != TacticalUnitType.Infantry
                        && friend.unittyp != TacticalUnitType.Cavalry) continue;
                    count++;
                }
            }
            catch { }
            return count;
        }

        private static bool HasEnemyArtilleryInFireRange(Regiment unit)
        {
            try
            {
                if (unit.unitrange == null || unit.unitrange.enemyinfirerangereg == null) return false;
                for (int i = 0; i < unit.unitrange.enemyinfirerangereg.Count; i++)
                {
                    var enemy = unit.unitrange.enemyinfirerangereg[i];
                    if (enemy == null) continue;
                    if (enemy.isrouted) continue;
                    if (enemy.unittyp == TacticalUnitType.Artillery) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool HasFriendlyCloseRangeRisk(Regiment unit, float fireRange)
        {
            try
            {
                if (unit == null || unit.unitrange == null || unit.unitrange.temp_owninrangeregs == null)
                    return false;

                float closeRange = Math.Max(80f, fireRange * 0.20f);
                for (int i = 0; i < unit.unitrange.temp_owninrangeregs.Count; i++)
                {
                    Regiment friend = unit.unitrange.temp_owninrangeregs[i];
                    if (friend == null) continue;
                    if (friend.isrouted || friend.markedforrout) continue;
                    if (friend.unittyp != TacticalUnitType.Infantry &&
                        friend.unittyp != TacticalUnitType.Cavalry)
                        continue;

                    float distance = UnityEngine.Vector3.Distance(
                        ((UnityEngine.Component)unit).transform.position,
                        ((UnityEngine.Component)friend).transform.position);
                    if (distance <= closeRange) return true;
                }
            }
            catch { }

            return false;
        }

        private static bool TryResolveDoctrineOrder(
            Regiment group,
            out CommandDoctrineOrder order,
            out BattlefieldPictureSnapshot picture)
        {
            order = default(CommandDoctrineOrder);
            picture = new BattlefieldPictureSnapshot(Array.Empty<BattlefieldObjectiveEstimate>());

            try
            {
                if (group == null) return false;
                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                ArmyOrchestrator army = side?.Army;
                var orders = army?.CurrentDoctrineOrders;
                if (orders == null || orders.Count == 0) return false;

                int componentInstanceId = TacticalPatchIds.ComponentInstanceId(group);
                int gameObjectInstanceId = TacticalPatchIds.GameObjectInstanceId(group);
                for (int i = 0; i < orders.Count; i++)
                {
                    CommandDoctrineOrder candidate = orders[i];
                    if (!TacticalPatchIds.NodeIdMatches(candidate.NodeId, gameObjectInstanceId, componentInstanceId))
                        continue;
                    if (!candidate.HasPurpose) return false;

                    order = candidate;
                    if (side?.OperationsLedger != null)
                        picture = side.OperationsLedger.CurrentBattlefieldPicture;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static void ApplyDecision(BattleUnits bunits, Regiment unit, in TacticalArtilleryInputAdapter.Snapshot snapshot, TacticalArtilleryDoctrine.Decision decision)
        {
            switch (decision)
            {
                case TacticalArtilleryDoctrine.Decision.CancelBombard:
                    if (snapshot.CombatBehaviorOrdered == 9)
                    {
                        bunits.ChangeCombatBehavior(((UnityEngine.Component)unit).gameObject, 7);
                        unit.bombardstarttime = 0f;
                        OnceLog.Info("b7-cancel-bombard", "B7 cancel-bombard decision applied.");
                    }
                    break;
                case TacticalArtilleryDoctrine.Decision.CounterBattery:
                    if (snapshot.CombatBehaviorOrdered == 9)
                    {
                        bunits.ChangeCombatBehavior(((UnityEngine.Component)unit).gameObject, 8);
                        OnceLog.Info("b7-counterbattery", "B7 counterbattery decision applied.");
                    }
                    break;
                case TacticalArtilleryDoctrine.Decision.PreserveFire:
                case TacticalArtilleryDoctrine.Decision.SuppressStrongpoint:
                case TacticalArtilleryDoctrine.Decision.DefensiveFallback:
                    // Telemetry only; vanilla owns these write paths.
                    break;
            }
        }

        private static bool PassesVanillaWriteEnvelope(AIBattle battle, Regiment unit, Regiment aigroup, int isPlayerAiOrFeud)
        {
            if (unit == null) return false;
            if (unit.unittyp != TacticalUnitType.Artillery) return false;
            if (unit.isrouted) return false;
            if (unit.combatbehaviorordered == 8) return false;
            if (unit.markedforrout) return false;
            if (unit.guns <= 0) return false;
            if (unit.permanentlydetached) return false;
            if (!(aigroup.ai_feudstance == -1 || isPlayerAiOrFeud == 2)) return false;
            return PerformAiActionDlcWl(battle, unit, aigroup);
        }

        private static BattleUnits BattleUnits(AIBattle battle)
        {
            try
            {
                if (battle == null) return null;

                if (_bunitsField == null)
                    _bunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
                if (_bunitsField == null)
                {
                    OnceLog.Warning("b7-check-ai-bombardment-missing-bunits", "[B7] Missing AIBattle.bunits; skipping artillery doctrine writes.");
                    return null;
                }

                BattleUnits bunits = _bunitsField.GetValue(battle) as BattleUnits;
                if (bunits == null)
                    OnceLog.Warning("b7-check-ai-bombardment-null-bunits", "[B7] AIBattle.bunits was null; skipping artillery doctrine writes.");
                return bunits;
            }
            catch (System.Exception ex)
            {
                OnceLog.Warning("b7-check-ai-bombardment-bunits-error", "[B7] Failed reading AIBattle.bunits: " + ex.Message);
                return null;
            }
        }

        private static int? IsPlayerAiOrFeud(AIBattle battle)
        {
            try
            {
                if (battle == null) return null;

                if (_isPlayerAiOrFeudField == null)
                    _isPlayerAiOrFeudField = AccessTools.Field(typeof(AIBattle), "isplayeraiorfeud");
                if (_isPlayerAiOrFeudField == null)
                {
                    OnceLog.Warning("b7-check-ai-bombardment-missing-isplayeraiorfeud", "[B7] Missing AIBattle.isplayeraiorfeud; skipping artillery doctrine write.");
                    return null;
                }

                object value = _isPlayerAiOrFeudField.GetValue(battle);
                if (value is int result) return result;

                OnceLog.Warning("b7-check-ai-bombardment-invalid-isplayeraiorfeud", "[B7] AIBattle.isplayeraiorfeud was not an int; skipping artillery doctrine write.");
                return null;
            }
            catch (System.Exception ex)
            {
                OnceLog.Warning("b7-check-ai-bombardment-isplayeraiorfeud-error", "[B7] Failed reading AIBattle.isplayeraiorfeud: " + ex.Message);
                return null;
            }
        }

        private static bool HasPerformAiActionDlcWl()
        {
            if (_performAiActionDlcWlMethod == null)
            {
                _performAiActionDlcWlMethod = AccessTools.Method(
                    typeof(AIBattle),
                    "PerformAIActionDLCWL",
                    new[] { typeof(Regiment), typeof(Regiment) });
            }
            if (_performAiActionDlcWlMethod != null) return true;

            OnceLog.Warning("b7-check-ai-bombardment-missing-perform-ai-action-dlcwl", "[B7] Missing AIBattle.PerformAIActionDLCWL(Regiment, Regiment); skipping artillery doctrine writes.");
            return false;
        }

        private static bool PerformAiActionDlcWl(AIBattle battle, Regiment unit, Regiment aigroup)
        {
            try
            {
                object value = _performAiActionDlcWlMethod.Invoke(null, new object[] { unit, aigroup });
                if (value is bool result) return result;

                OnceLog.Warning("b7-check-ai-bombardment-invalid-perform-ai-action-dlcwl", "[B7] AIBattle.PerformAIActionDLCWL did not return bool; skipping artillery doctrine write.");
                return false;
            }
            catch (System.Exception ex)
            {
                OnceLog.Warning("b7-check-ai-bombardment-perform-ai-action-dlcwl-error", "[B7] Failed invoking AIBattle.PerformAIActionDLCWL: " + ex.Message);
                return false;
            }
        }
    }
}
