using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // v0.2.2 concrete steering for vanilla AICampaign.AssignUnitToDefendCapital.
    // Vanilla computes whether the capital is under-defended, picks one eligible
    // unit, and then moves all capital-defense groups. We run after vanilla and
    // add one extra eligible group only when the active CIC personality makes
    // the capital-defense strength gate stricter. Vanilla owns the later move.
    [HarmonyPatch(typeof(AICampaign), "AssignUnitToDefendCapital")]
    internal static class DefensiveOpsPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            OnceLog.Info("defensiveops", "DefensiveOpsPatch wired (capital-defense strength gate)");

            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (StrategicCoordinator.Instance == null) return;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0 || allianceId >= StrategicCoordinator.Instance.CICs.Length) return;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                var cic = StrategicCoordinator.Instance.CICs[allianceId];
                if (cic == null) return;

                var era = StrategicCoordinator.Instance.Eras[allianceId] ?? new EraStageManager { Stage = EraStage.Amateur1861 };
                var personality = cic.Effective(era);

                var faction = GetFaction(_aifaction);
                if (faction == null) return;

                if (!TryResolveCapital(allianceId, out var capitalPosition, out var capitalName)) return;

                var ownUnits = ReadList(faction, "ownunits", "defensiveops:field:ownunits");
                var enemyUnits = ReadList(faction, "enemyunits", "defensiveops:field:enemyunits");
                var capitalDefenders = ReadList(faction, "groupstodefendcapital", "defensiveops:field:capitaldefenders");
                if (ownUnits == null || enemyUnits == null || capitalDefenders == null) return;

                float required = CalculateRequiredDefense(ownUnits, enemyUnits, capitalDefenders, capitalPosition, personality);
                if (required <= 0f) return;

                var candidate = FindCandidate(
                    _aifaction,
                    ownUnits,
                    capitalDefenders,
                    ReadList(faction, "unitsindefensiveoperations", "defensiveops:field:defensiveops"),
                    ReadList(faction, "unitsinoffensiveoperations", "defensiveops:field:offensiveops"),
                    ReadList(faction, "unitsconstructingsupplydepots", "defensiveops:field:supplyops"),
                    capitalPosition,
                    personality);
                if (candidate == null) return;

                capitalDefenders.Add(candidate);
                RemoveFromList(faction, "unitsindefensiveoperations", candidate);
                RemoveFromDefensiveOperation(candidate);
                RemoveFromList(faction, "unitsinoffensiveoperations", candidate);
                RemoveFromList(faction, "unitsconstructingsupplydepots", candidate);

                Plugin.Log.LogInfo(
                    $"[Patch:DefensiveOps] alliance={allianceId} capital={capitalName} " +
                    $"assigned=1 need={required:F0} caution={personality.Caution:F2}");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defensiveops:postfix", "[Patch:DefensiveOps] postfix failed: " + ex.Message);
            }
        }

        private static object GetFaction(int aifactionIndex)
        {
            var aicType = AccessTools.TypeByName("AICampaign");
            var list = AccessTools.Field(aicType, "aifaction")?.GetValue(null) as IList;
            if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return null;
            return list[aifactionIndex];
        }

        private static IList ReadList(object faction, string fieldName, string warningKey)
        {
            try
            {
                var list = AccessTools.Field(faction.GetType(), fieldName)?.GetValue(faction) as IList;
                if (list == null)
                    OnceLog.Warning(warningKey, $"DefensiveOpsPatch missing AIFaction.{fieldName} list");
                return list;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(warningKey, $"DefensiveOpsPatch failed reading AIFaction.{fieldName}: {ex.Message}");
                return null;
            }
        }

        private static bool TryResolveCapital(int allianceId, out Vector3 position, out string cityName)
        {
            position = default(Vector3);
            cityName = "<capital>";

            try
            {
                if (GameVars.alliance == null || allianceId < 0 || allianceId >= GameVars.alliance.Length) return false;
                var capital = GameVars.alliance[allianceId].capital;
                if (capital == null) return false;
                position = ((Component)capital).transform.position;
                cityName = capital.CityName ?? cityName;
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defensiveops:capital", "[Patch:DefensiveOps] capital resolve failed: " + ex.Message);
                return false;
            }
        }

        private static float CalculateRequiredDefense(
            IList ownUnits,
            IList enemyUnits,
            IList capitalDefenders,
            Vector3 capitalPosition,
            PersonalityVector personality)
        {
            float closestEnemyDistance = 9999f;
            float searchRadius = GamePrefs.aimaximumdistancetosearchforunitrelocations * SearchRadiusMultiplier(personality);
            float required = GamePrefs.aiminimumstrengthformovement * StrengthGateMultiplier(personality);

            for (int i = 0; i < enemyUnits.Count; i++)
            {
                var enemy = enemyUnits[i] as Regiment;
                if (enemy == null) continue;
                float distance = Mathf.Max(1f, Tools.GetXZDistance(((Component)enemy).transform.position, capitalPosition));
                if (distance < closestEnemyDistance) closestEnemyDistance = distance;
                if (distance < searchRadius)
                {
                    float pressure = Mathf.Min((float)enemy.groupstrengthactive, (float)enemy.groupstrengthactive * enemy.groupmorale);
                    required += pressure / Mathf.Pow(Mathf.Max(10f, distance), 0.3f) * 0.5f * StrengthGateMultiplier(personality);
                }
            }

            for (int i = 0; i < ownUnits.Count; i++)
            {
                var own = ownUnits[i] as Regiment;
                if (own == null) continue;
                if (Tools.GetXZDistance(own.GetLastWaypointPos(), capitalPosition) < closestEnemyDistance)
                    required -= (float)own.groupstrengthactive * own.groupmorale;
            }

            for (int i = 0; i < capitalDefenders.Count; i++)
            {
                var pledged = capitalDefenders[i] as Regiment;
                if (pledged == null) continue;
                required -= (float)pledged.groupstrengthactive * Mathf.Max(0.25f, pledged.groupmorale) * 0.75f;
            }

            return required;
        }

        private static Regiment FindCandidate(
            int aifactionIndex,
            IList ownUnits,
            IList capitalDefenders,
            IList defensiveOps,
            IList offensiveOps,
            IList supplyOps,
            Vector3 capitalPosition,
            PersonalityVector personality)
        {
            float maxDistance = GamePrefs.maxdistancedefensiveoperations * SearchRadiusMultiplier(personality);
            float bestDistance = float.MaxValue;
            Regiment best = null;

            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i] as Regiment;
                if (!EligibleForCapitalDefense(aifactionIndex, unit, capitalDefenders, defensiveOps, supplyOps, capitalPosition, maxDistance)) continue;

                float distance = Tools.GetXZDistance(((Component)unit).transform.position, capitalPosition);
                float score = distance - (float)unit.groupstrengthactive * 0.01f * Math.Max(0f, personality.Caution);
                if (offensiveOps != null && offensiveOps.Contains(unit))
                    score += 75f * (1f + Math.Max(0f, personality.Aggression));

                if (score < bestDistance)
                {
                    bestDistance = score;
                    best = unit;
                }
            }

            return best;
        }

        private static bool EligibleForCapitalDefense(
            int aifactionIndex,
            Regiment unit,
            IList capitalDefenders,
            IList defensiveOps,
            IList supplyOps,
            Vector3 capitalPosition,
            float maxDistance)
        {
            if (unit == null) return false;
            if (capitalDefenders != null && capitalDefenders.Contains(unit)) return false;
            if (Tools.GetXZDistance(((Component)unit).transform.position, capitalPosition) >= maxDistance) return false;
            if ((float)unit.groupstrength < GamePrefs.aiminimumstrengthformovement) return false;
            if (unit.groupmorale <= GamePrefs.aiminimummoraleformovement) return false;
            if (unit.onretreat || unit.inbattle) return false;
            if (supplyOps != null && supplyOps.Contains(unit)) return false;
            if (UnitAlreadyConstructing(unit)) return false;
            if (SeaInvasionForceReference(unit) != null) return false;
            if (!AICampaign.UnitIsFightingForce(unit)) return false;
            if (!AICampaign.IsWithinOperationsTheater(unit, capitalPosition)) return false;
            if (IsRaidUnit(unit)) return false;
            if (DLC_WL.IsMovedByPlayer(unit)) return false;
            if (IsUnitTakingTown(unit)) return false;

            if (defensiveOps != null && defensiveOps.Contains(unit))
            {
                float waypointDistance = Tools.GetXZDistance(capitalPosition, unit.GetLastWaypointPos());
                if (waypointDistance >= GamePrefs.maxdistancedefensiveoperations) return false;
            }

            return true;
        }

        private static bool UnitAlreadyConstructing(Regiment unit)
        {
            try
            {
                var type = AccessTools.TypeByName("FortConstructionOrder");
                var method = type != null ? AccessTools.Method(type, "UnitAlreadyConstructing", new[] { typeof(Regiment) }) : null;
                if (method == null)
                {
                    OnceLog.Warning("defensiveops:method:fortconstruct", "DefensiveOpsPatch missing FortConstructionOrder.UnitAlreadyConstructing(Regiment)");
                    return true;
                }
                return (bool)method.Invoke(null, new object[] { unit });
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defensiveops:method:fortconstruct", "DefensiveOpsPatch FortConstructionOrder.UnitAlreadyConstructing failed: " + ex.Message);
                return true;
            }
        }

        private static object SeaInvasionForceReference(Regiment unit)
        {
            try
            {
                var type = AccessTools.TypeByName("SeaInvasionForce");
                var method = type != null ? AccessTools.Method(type, "GetSeaInvasionForceReference", new[] { typeof(Regiment) }) : null;
                if (method == null)
                {
                    OnceLog.Warning("defensiveops:method:seainvasion", "DefensiveOpsPatch missing SeaInvasionForce.GetSeaInvasionForceReference(Regiment)");
                    return unit;
                }
                return method.Invoke(null, new object[] { unit });
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defensiveops:method:seainvasion", "DefensiveOpsPatch SeaInvasionForce.GetSeaInvasionForceReference failed: " + ex.Message);
                return unit;
            }
        }

        private static bool IsRaidUnit(Regiment unit)
        {
            try
            {
                var type = AccessTools.TypeByName("RaidForce");
                var method = type != null ? AccessTools.Method(type, "IsRaidUnit", new[] { typeof(Regiment) }) : null;
                if (method == null)
                {
                    OnceLog.Warning("defensiveops:method:raid", "DefensiveOpsPatch missing RaidForce.IsRaidUnit(Regiment)");
                    return true;
                }
                return (bool)method.Invoke(null, new object[] { unit });
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defensiveops:method:raid", "DefensiveOpsPatch RaidForce.IsRaidUnit failed: " + ex.Message);
                return true;
            }
        }

        private static bool IsUnitTakingTown(Regiment unit)
        {
            try
            {
                var method = AccessTools.Method(typeof(AICampaign), "IsUnitTakingTown", new[] { typeof(Regiment) });
                if (method == null)
                {
                    OnceLog.Warning("defensiveops:method:takingtown", "DefensiveOpsPatch missing AICampaign.IsUnitTakingTown(Regiment)");
                    return true;
                }
                return (bool)method.Invoke(null, new object[] { unit });
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defensiveops:method:takingtown", "DefensiveOpsPatch IsUnitTakingTown failed: " + ex.Message);
                return true;
            }
        }

        private static void RemoveFromList(object faction, string fieldName, Regiment unit)
        {
            try
            {
                var list = AccessTools.Field(faction.GetType(), fieldName)?.GetValue(faction) as IList;
                list?.Remove(unit);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defensiveops:remove:" + fieldName, $"DefensiveOpsPatch failed removing from {fieldName}: {ex.Message}");
            }
        }

        private static void RemoveFromDefensiveOperation(Regiment unit)
        {
            try
            {
                var nested = AccessTools.Inner(typeof(AICampaign), "DefensiveOperation");
                var method = nested != null ? AccessTools.Method(nested, "RemoveUnit", new[] { typeof(Regiment) }) : null;
                if (method == null)
                {
                    OnceLog.Warning("defensiveops:method:removeunit", "DefensiveOpsPatch missing DefensiveOperation.RemoveUnit(Regiment)");
                    return;
                }
                method.Invoke(null, new object[] { unit });
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defensiveops:method:removeunit", "DefensiveOpsPatch DefensiveOperation.RemoveUnit failed: " + ex.Message);
            }
        }

        private static float StrengthGateMultiplier(PersonalityVector personality)
        {
            return Clamp(1f + 0.35f * personality.Caution - 0.20f * personality.Aggression, 0.70f, 1.45f);
        }

        private static float SearchRadiusMultiplier(PersonalityVector personality)
        {
            return Clamp(1f + 0.25f * personality.Caution - 0.10f * personality.Aggression, 0.80f, 1.30f);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
