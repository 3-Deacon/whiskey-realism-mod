using System;
using System.Collections;
using System.Reflection;
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
    [HarmonyPriority(Priority.High)]
    internal static class DefensiveOpsPatch
    {
        private static bool _unitAlreadyConstructingResolved;
        private static MethodInfo _unitAlreadyConstructingMethod;
        private static bool _sifReferenceResolved;
        private static MethodInfo _sifReferenceMethod;
        private static bool _raidIsRaidUnitResolved;
        private static MethodInfo _raidIsRaidUnitMethod;
        private static bool _isUnitTakingTownResolved;
        private static MethodInfo _isUnitTakingTownMethod;
        private static bool _removeUnitResolved;
        private static MethodInfo _removeUnitMethod;

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

                // Capital is owned exclusively by #4 — short-circuit if no capital
                // can be resolved so this patch never touches non-capital state.
                // The defense intent ledger (Slice 2) owns all non-capital coastal
                // assets; this guard makes the spec's coexistence invariant explicit.
                if (!TryResolveCapital(allianceId, out var capitalPosition, out var capitalName)) return;
                var capitalTown = ResolveMapTown(capitalName);

                var cic = StrategicCoordinator.Instance.CICs[allianceId];
                if (cic == null) return;

                var era = StrategicCoordinator.Instance.Eras[allianceId] ?? new EraStageManager { Stage = EraStage.Amateur1861 };
                var personality = cic.Effective(era);

                var faction = GetFaction(_aifaction);
                if (faction == null) return;

                var ownUnits = ReadList(faction, "ownunits", "defensiveops:field:ownunits");
                var enemyUnits = ReadList(faction, "enemyunits", "defensiveops:field:enemyunits");
                var capitalDefenders = ReadList(faction, "groupstodefendcapital", "defensiveops:field:capitaldefenders");
                if (ownUnits == null || enemyUnits == null || capitalDefenders == null) return;

                float required = CalculateRequiredDefense(ownUnits, enemyUnits, capitalDefenders, capitalPosition, personality, capitalTown);
                if (required <= 0f) return;

                // Scale the capital defense gate by the director's CapitalDefenseBudgetModifier.
                // A positive modifier (e.g., TooFastCollapse → +0.10) raises the effective strength
                // gate, causing the patch to add a defender when the vanilla threshold would not.
                // Default preserved when coordinator or posture is absent.
                try
                {
                    var directorMemories = StrategicCoordinator.Instance?.DirectorMemories;
                    if (directorMemories != null && allianceId < directorMemories.Length)
                    {
                        float capitalMod = directorMemories[allianceId]?.LastPosture?.CapitalDefenseBudgetModifier ?? 0f;
                        float capitalFraction = Clamp(1f + capitalMod, 0.90f, 1.30f);
                        required *= capitalFraction;
                    }
                }
                catch { }

                var candidate = FindCandidate(
                    _aifaction,
                    ownUnits,
                    capitalDefenders,
                    ReadList(faction, "unitsindefensiveoperations", "defensiveops:field:defensiveops"),
                    ReadList(faction, "unitsinoffensiveoperations", "defensiveops:field:offensiveops"),
                    ReadList(faction, "unitsconstructingsupplydepots", "defensiveops:field:supplyops"),
                    capitalPosition,
                    required,
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

        private static CampaignMapTown ResolveMapTown(string cityName)
        {
            try
            {
                var ledger = StrategicCoordinator.Instance?.CampaignMap;
                if (ledger != null && ledger.TryGetTown(cityName, out var town))
                    return town;
            }
            catch { }
            return null;
        }

        private static float CalculateRequiredDefense(
            IList ownUnits,
            IList enemyUnits,
            IList capitalDefenders,
            Vector3 capitalPosition,
            PersonalityVector personality,
            CampaignMapTown capitalTown)
        {
            float closestEnemyDistance = 9999f;
            float searchRadius = GamePrefs.aimaximumdistancetosearchforunitrelocations * SearchRadiusMultiplier(personality);
            float required = GamePrefs.aiminimumstrengthformovement *
                StrengthGateMultiplier(personality) *
                CapitalValueMultiplier(capitalTown);

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

        private static float CapitalValueMultiplier(CampaignMapTown capitalTown)
        {
            if (capitalTown == null) return 1f;

            float multiplier = 1f;
            if ((capitalTown.Roles & CampaignTownRole.MajorCity) != 0) multiplier += 0.08f;
            if ((capitalTown.Roles & CampaignTownRole.Workforce) != 0) multiplier += 0.04f;
            if ((capitalTown.Roles & CampaignTownRole.Economic) != 0) multiplier += 0.04f;
            multiplier += Mathf.Clamp01(capitalTown.CitySize) * 0.08f;
            return Clamp(multiplier, 1f, 1.25f);
        }

        private static Regiment FindCandidate(
            int aifactionIndex,
            IList ownUnits,
            IList capitalDefenders,
            IList defensiveOps,
            IList offensiveOps,
            IList supplyOps,
            Vector3 capitalPosition,
            float requiredStrength,
            PersonalityVector personality)
        {
            float maxDistance = GamePrefs.maxdistancedefensiveoperations * SearchRadiusMultiplier(personality);
            float desiredStrength = Math.Max(GamePrefs.aiminimumstrengthformovement, requiredStrength);
            float bestScore = float.MaxValue;
            Regiment best = null;

            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i] as Regiment;
                if (!EligibleForCapitalDefense(aifactionIndex, unit, capitalDefenders, defensiveOps, supplyOps, capitalPosition, maxDistance)) continue;

                float distance = Tools.GetXZDistance(((Component)unit).transform.position, capitalPosition);
                float readiness = ReadinessStep(unit);
                float score = DefenseForceSizer.ScoreCandidate(
                    unit.groupstrengthactive,
                    unit.groupmorale,
                    readiness,
                    distance,
                    desiredStrength,
                    offensiveOps != null && offensiveOps.Contains(unit),
                    personality.Caution,
                    personality.Aggression);

                if (score < bestScore)
                {
                    bestScore = score;
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
            if (ReadinessStep(unit) < 1f) return false;
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

        private static float ReadinessStep(Regiment unit)
        {
            try { return CampaignArmyPanel.GetReadinessStep(unit); }
            catch { return 2f; }
        }

        private static bool UnitAlreadyConstructing(Regiment unit)
        {
            try
            {
                var method = ResolveUnitAlreadyConstructingMethod();
                if (method == null)
                {
                    OnceLog.Warning("defensiveops:method:fortconstruct", "DefensiveOpsPatch missing AICampaign.FortConstructionOrder.UnitAlreadyConstructing(Regiment, FortConstructionOrder)");
                    return true;
                }
                return (bool)method.Invoke(null, new object[] { unit, null });
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
                var method = ResolveSeaInvasionForceReferenceMethod();
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
                var method = ResolveIsRaidUnitMethod();
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
                var method = ResolveIsUnitTakingTownMethod();
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
                var method = ResolveRemoveUnitMethod();
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

        private static MethodInfo ResolveUnitAlreadyConstructingMethod()
        {
            if (_unitAlreadyConstructingResolved) return _unitAlreadyConstructingMethod;
            _unitAlreadyConstructingResolved = true;

            var nested = ResolveAICampaignNestedType("FortConstructionOrder");
            if (nested == null) return null;

            _unitAlreadyConstructingMethod = AccessTools.Method(
                nested,
                "UnitAlreadyConstructing",
                new[] { typeof(Regiment), nested });
            return _unitAlreadyConstructingMethod;
        }

        private static MethodInfo ResolveSeaInvasionForceReferenceMethod()
        {
            if (_sifReferenceResolved) return _sifReferenceMethod;
            _sifReferenceResolved = true;

            var nested = ResolveAICampaignNestedType("SeaInvasionForce");
            if (nested == null) return null;

            _sifReferenceMethod = AccessTools.Method(
                nested,
                "GetSeaInvasionForceReference",
                new[] { typeof(Regiment) });
            return _sifReferenceMethod;
        }

        private static MethodInfo ResolveIsRaidUnitMethod()
        {
            if (_raidIsRaidUnitResolved) return _raidIsRaidUnitMethod;
            _raidIsRaidUnitResolved = true;

            var nested = ResolveAICampaignNestedType("RaidForce");
            if (nested == null) return null;

            _raidIsRaidUnitMethod = AccessTools.Method(
                nested,
                "IsRaidUnit",
                new[] { typeof(Regiment) });
            return _raidIsRaidUnitMethod;
        }

        private static MethodInfo ResolveIsUnitTakingTownMethod()
        {
            if (_isUnitTakingTownResolved) return _isUnitTakingTownMethod;
            _isUnitTakingTownResolved = true;

            _isUnitTakingTownMethod = AccessTools.Method(
                typeof(AICampaign),
                "IsUnitTakingTown",
                new[] { typeof(Regiment) });
            return _isUnitTakingTownMethod;
        }

        private static MethodInfo ResolveRemoveUnitMethod()
        {
            if (_removeUnitResolved) return _removeUnitMethod;
            _removeUnitResolved = true;

            var nested = ResolveAICampaignNestedType("DefensiveOperation");
            if (nested == null) return null;

            _removeUnitMethod = AccessTools.Method(
                nested,
                "RemoveUnit",
                new[] { typeof(Regiment) });
            return _removeUnitMethod;
        }

        private static Type ResolveAICampaignNestedType(string typeName)
        {
            return AccessTools.Inner(typeof(AICampaign), typeName)
                ?? AccessTools.TypeByName("AICampaign+" + typeName)
                ?? AccessTools.TypeByName(typeName);
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
