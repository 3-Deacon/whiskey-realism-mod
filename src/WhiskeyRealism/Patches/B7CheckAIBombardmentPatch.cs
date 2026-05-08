using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.CheckAIBombardment(Regiment) at decompile line 3869 evaluates
    // each artillery sub-unit and may set combatbehaviorordered to 8 (bombard) or 9
    // (counter-battery), or cancel an active bombardment. This Postfix reads the
    // post-vanilla state, runs TacticalArtilleryDoctrine.Score, and selectively
    // rewrites combatbehaviorordered when the doctrine output disagrees with vanilla.
    // Default-off behind Plugin.EnableTacticalArtilleryDoctrine.
    [HarmonyPatch(typeof(AIBattle), "CheckAIBombardment")]
    public static class B7CheckAIBombardmentPatch
    {
        private static FieldInfo _isPlayerAiOrFeudField;

        [HarmonyPostfix]
        public static void Postfix(AIBattle __instance, Regiment aigroup)
        {
            if (Plugin.EnableTacticalArtilleryDoctrine == null) return;
            if (!Plugin.EnableTacticalArtilleryDoctrine.Value) return;
            if (aigroup == null) return;

            try
            {
                OnceLog.Info("b7-check-ai-bombardment", "B7 CheckAIBombardment Postfix wired.");

                var allUnits = aigroup.allattachedunits;
                if (allUnits == null) return;

                int? isPlayerAiOrFeud = IsPlayerAiOrFeud(__instance);
                if (!isPlayerAiOrFeud.HasValue) return;

                for (int i = 0; i < allUnits.Length; i++)
                {
                    var unit = allUnits[i];
                    if (unit == null) continue;

                    var snapshot = BuildSnapshot(unit, aigroup, isPlayerAiOrFeud.Value);
                    if (!TacticalArtilleryInputAdapter.IsEligible(snapshot)) continue;

                    var screenInput = TacticalArtilleryInputAdapter.ToSupportScreenInput(snapshot);
                    var screenResult = TacticalSupportScreen.Score(screenInput);

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
                    };
                    var decision = TacticalArtilleryDoctrine.Score(doctrineInput);

                    ApplyDecision(unit, snapshot, decision);
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

        private static void ApplyDecision(Regiment unit, in TacticalArtilleryInputAdapter.Snapshot snapshot, TacticalArtilleryDoctrine.Decision decision)
        {
            switch (decision)
            {
                case TacticalArtilleryDoctrine.Decision.CancelBombard:
                    if (snapshot.CombatBehaviorOrdered == 8 || snapshot.CombatBehaviorOrdered == 9)
                    {
                        unit.combatbehaviorordered = 0;
                        OnceLog.Info("b7-cancel-bombard", "B7 cancel-bombard decision applied.");
                    }
                    break;
                case TacticalArtilleryDoctrine.Decision.CounterBattery:
                    if (snapshot.CombatBehaviorOrdered == 8)
                    {
                        unit.combatbehaviorordered = 9;
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
    }
}
