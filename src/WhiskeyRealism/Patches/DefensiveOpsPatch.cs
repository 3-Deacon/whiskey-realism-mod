using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // v0.2.2 concrete steering for vanilla AICampaign.AssignUnitToDefendCapital
    // (decompile 11668). Vanilla computes num2 (capital defense need), drops
    // invalid defenders, removes the FARTHEST defender when num2 <= 0 (~11778),
    // adds the closest eligible unit when num2 > 0, then moves every
    // groupstodefendcapital entry toward the capital (~11829).
    //
    // 2026-10-14 (C-DefOps): this used to be a Postfix that added a candidate
    // AFTER vanilla's move loop, whenever the personality gate was stricter than
    // vanilla's. The unit never moved, vanilla stripped it as "farthest" the
    // next call (num2 <= 0), the Postfix re-added it and stripped it from its
    // other operations again: permanent churn. Now a Prefix pre-pledges the
    // personality-chosen candidate only when vanilla's own num2 > 0 as well
    // (CampaignMilitaryGates.ShouldPrePledgeCapitalDefender), so vanilla keeps
    // it and its move loop moves it this call. Units the coastal-defense runner
    // has custom-ordered are never pledged.
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

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.defensive-ops", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
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

                    var faction = AICampaignReflect.GetFaction(_aifaction);
                    if (faction == null) return;

                    var ownUnits = ReadList(faction, "ownunits", "defensiveops:field:ownunits");
                    var enemyUnits = ReadList(faction, "enemyunits", "defensiveops:field:enemyunits");
                    var capitalDefenders = ReadList(faction, "groupstodefendcapital", "defensiveops:field:capitaldefenders");
                    if (ownUnits == null || enemyUnits == null || capitalDefenders == null) return;

                    float vanillaNeed = CalculateRequiredDefense(ownUnits, enemyUnits, null, capitalPosition, default(PersonalityVector), null, vanillaOnly: true);
                    float required = CalculateRequiredDefense(ownUnits, enemyUnits, capitalDefenders, capitalPosition, personality, capitalTown, vanillaOnly: false);

                    // Scale the personality need by the director's CapitalDefenseBudgetModifier.
                    // A positive modifier (e.g., TooFastCollapse → +0.10) raises the need. It can
                    // only add a defender alongside vanilla's own (vanillaNeed > 0), never against it.
                    // Default preserved when coordinator or posture is absent.
                    try
                    {
                        var directorMemories = StrategicCoordinator.Instance?.DirectorMemories;
                        if (directorMemories != null && allianceId < directorMemories.Length)
                        {
                            float capitalMod = directorMemories[allianceId]?.LastPosture?.CapitalDefenseBudgetModifier ?? 0f;
                            float capitalFraction = Clamp(1f + capitalMod, 0.90f, 1.30f);
                            if (required > 0f) required *= capitalFraction;
                        }
                    }
                    catch { }

                    if (!CampaignMilitaryGates.ShouldPrePledgeCapitalDefender(vanillaNeed, required)) return;

                    var candidate = FindCandidate(
                        allianceId,
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

                    TelemetryRouter.LegacyInfo(
                        $"[DefenseIntent] alliance={allianceId} capital={capitalName} " +
                        $"pre-pledged=1 need={required:F0} vanillaNeed={vanillaNeed:F0} caution={personality.Caution:F2}",
                        TelemetryLayer.Campaign);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("defensiveops:prefix", "[Patch:DefensiveOps] prefix failed: " + ex.Message);
                }
            }
        }

        private static IList ReadList(object faction, string fieldName, string warningKey)
        {
            try
            {
                var list = VanillaFieldCache.List(faction, fieldName);
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

        // Builds the CampaignMilitaryGates inputs from live lists. vanillaOnly
        // reproduces vanilla num2 exactly (11700-11740); otherwise the personality
        // multipliers and search radius apply and pledged defenders that vanilla
        // keeps (its own validity filter, 11745) count while still marching.
        private static float CalculateRequiredDefense(
            IList ownUnits,
            IList enemyUnits,
            IList capitalDefenders,
            Vector3 capitalPosition,
            PersonalityVector personality,
            CampaignMapTown capitalTown,
            bool vanillaOnly)
        {
            var enemies = new List<CapitalForce>(enemyUnits.Count);
            for (int i = 0; i < enemyUnits.Count; i++)
            {
                var enemy = enemyUnits[i] as Regiment;
                if (enemy == null) continue;
                enemies.Add(new CapitalForce(
                    Tools.GetXZDistance(((Component)enemy).transform.position, capitalPosition),
                    enemy.groupstrengthactive,
                    enemy.groupmorale));
            }

            var own = new List<CapitalForce>(ownUnits.Count);
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i] as Regiment;
                if (unit == null) continue;
                own.Add(new CapitalForce(
                    Tools.GetXZDistance(unit.GetLastWaypointPos(), capitalPosition),
                    unit.groupstrengthactive,
                    unit.groupmorale));
            }

            float baseMinimum = GamePrefs.aiminimumstrengthformovement;
            if (vanillaOnly)
            {
                return CampaignMilitaryGates.VanillaCapitalDefenseNeed(
                    baseMinimum, GamePrefs.aimaximumdistancetosearchforunitrelocations, enemies, own);
            }

            var pledged = new List<CapitalForce>();
            if (capitalDefenders != null)
            {
                for (int i = 0; i < capitalDefenders.Count; i++)
                {
                    var defender = capitalDefenders[i] as Regiment;
                    if (defender == null || !VanillaKeepsDefender(defender)) continue;
                    pledged.Add(new CapitalForce(
                        Tools.GetXZDistance(defender.GetLastWaypointPos(), capitalPosition),
                        defender.groupstrengthactive,
                        defender.groupmorale));
                }
            }

            return CampaignMilitaryGates.PersonalityCapitalDefenseNeed(
                baseMinimum,
                GamePrefs.aimaximumdistancetosearchforunitrelocations * SearchRadiusMultiplier(personality),
                enemies,
                own,
                pledged,
                StrengthGateMultiplier(personality),
                CapitalValueMultiplier(capitalTown));
        }

        // Vanilla drops a defender when groupstrength < min, morale < min,
        // onretreat, or moved by the W&L player (11745).
        private static bool VanillaKeepsDefender(Regiment defender)
        {
            if ((float)defender.groupstrength < GamePrefs.aiminimumstrengthformovement) return false;
            if (defender.groupmorale < GamePrefs.aiminimummoraleformovement) return false;
            if (defender.onretreat) return false;
            try { if (DLC_WL.IsMovedByPlayer(defender)) return false; }
            catch { }
            return true;
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
            int allianceId,
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
                if (CoastalDefenseCustomOrderRunner.IsCustomOrdered(allianceId, ((UnityEngine.Object)unit).GetInstanceID())) continue;

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
                VanillaFieldCache.List(faction, fieldName)?.Remove(unit);
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
