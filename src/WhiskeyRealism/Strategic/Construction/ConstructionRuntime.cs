using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Construction
{
    public static class ConstructionRuntime
    {
        public static ConstructionInput BuildInput(
            int alliance,
            EraStage era,
            FiscalOutput fiscal,
            FrontSectorLedger front,
            FormationDirectiveLedger formation)
        {
            var input = new ConstructionInput
            {
                AllianceId = alliance,
                EraStage = era,
                CurrentChapter = SafePolicyChapter(),
                CurrentYear = SafeYear(),
                FiscalPosture = fiscal != null ? fiscal.Posture : FiscalPosture.BalancedWar,
                FiscalDefendedGate = fiscal != null ? fiscal.DefendedGate : FiscalGate.None,
                CurrentRating = SafeRating(alliance),
                BondFloorRating = SafeBondFloor(),
                SupplyProtection = fiscal != null && fiscal.SupplyProtection,
                LogisticsExpansion = fiscal != null && fiscal.LogisticsExpansion,
                ForceCapWarning = fiscal != null && fiscal.ForceCapWarning,
                TopSupplyTheater = fiscal != null ? fiscal.TheaterSupplyPriority ?? "" : "",
                ActiveRailroadStarts = CountActiveRailroads(alliance)
            };

            FillPressure(input, front, formation);
            AddPrivateBuildingCandidates(input);
            AddRailroadCandidates(input);
            AddFortSiteObservationCandidates(input);
            return input;
        }

        private static int SafePolicyChapter()
        {
            try { return Policy.CurrentChapter; }
            catch { return -1; }
        }

        private static int SafeYear()
        {
            try { return Mathf.FloorToInt(GameVars.GetCampaignFloatingDate()); }
            catch { return 1861; }
        }

        private static int SafeRating(int alliance)
        {
            try
            {
                if (GameVars.alliance == null || alliance < 0 || alliance >= GameVars.alliance.Length)
                    return 0;
                return GameVars.alliance[alliance].currentrating;
            }
            catch { return 0; }
        }

        private static int SafeBondFloor()
        {
            try { return GamePrefs.ratingnotches != null ? GamePrefs.ratingnotches.Length - 1 : 11; }
            catch { return 11; }
        }

        private static void FillPressure(ConstructionInput input, FrontSectorLedger front, FormationDirectiveLedger formation)
        {
            try
            {
                if (formation != null && formation.Pressure != null)
                {
                    input.LowSupplyFormationCount = formation.Pressure.LowSupplyCount;
                    input.LowAmmoFormationCount = formation.Pressure.LowAmmoCount;
                    if (!string.IsNullOrEmpty(formation.Pressure.TopSupplyAreaKey))
                        input.TopSupplyTheater = formation.Pressure.TopSupplyAreaKey;
                }

                if (front == null || front.Sectors == null) return;

                int count = 0;
                float supply = 0f;
                float threat = 0f;
                foreach (var sector in front.Sectors)
                {
                    if (sector == null) continue;
                    count++;
                    supply += 1f - Clamp01(sector.AverageSupply);
                    if (sector.IsCritical && sector.Posture == FrontPosture.Hold)
                        threat += Clamp01(sector.StrategicImportance);
                }

                if (count <= 0) return;
                input.SupplyPressure = Clamp01(supply / count);
                input.AmmoPressure = input.LowAmmoFormationCount > 0
                    ? Clamp01(input.SupplyPressure + 0.15f)
                    : Clamp01(input.SupplyPressure * 0.5f);
                input.TransportPressure = input.LogisticsExpansion ? Math.Max(input.SupplyPressure, 0.35f) : input.SupplyPressure;
                input.CapitalThreat = Clamp01(threat / count);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("construction-runtime:pressure", "[ConstructionRuntime] pressure read failed: " + ex.Message);
            }
        }

        private static void AddPrivateBuildingCandidates(ConstructionInput input)
        {
            try
            {
                if (GameVars.alliance == null || GameVars.buildingtypes == null) return;
                if (input.AllianceId < 0 || input.AllianceId >= GameVars.alliance.Length) return;

                var state = GameVars.alliance[input.AllianceId];
                if (state == null || state.bestiipplaces == null || state.bestiipplacesprob == null) return;

                int count = Math.Min(state.bestiipplaces.Length, GameVars.buildingtypes.Count);
                for (int typeId = 0; typeId < count && typeId < state.bestiipplacesprob.Length; typeId++)
                {
                    var place = state.bestiipplaces[typeId];
                    if (place == null || state.bestiipplacesprob[typeId] <= 0f) continue;

                    var buildingType = GameVars.buildingtypes[typeId];
                    if (buildingType == null) continue;

                    Vector3 position = ((Component)place).transform.position;
                    input.Candidates.Add(new ConstructionCandidate
                    {
                        Kind = ConstructionCandidateKind.PrivateBuilding,
                        BuildingTypeId = typeId,
                        Name = buildingType.name ?? "building-" + typeId,
                        Theater = TheaterFromPosition(position),
                        SupplyPressure = input.SupplyPressure,
                        AmmoPressure = input.AmmoPressure,
                        TransportPressure = Clamp01(place.transportbottlenecks / 10f),
                        WoundedPressure = NearbyWoundedPressure(place, input.AllianceId),
                        VanillaValid = place.allianceowner == input.AllianceId && buildingType.aiplacement,
                        SafeRear = SafeRear(position, input.AllianceId),
                        ArmsIndustry = IsArmsIndustry(typeId, buildingType.name),
                        SupportsActiveArmyCorridor = SupportsActiveCorridor(position, input)
                    });
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("construction-runtime:private-candidates", "[ConstructionRuntime] private candidate read failed: " + ex.Message);
            }
        }

        private static void AddRailroadCandidates(ConstructionInput input)
        {
            try
            {
                var railroads = AccessTools.Field(typeof(BattleUnits), "railroad")?.GetValue(null) as IList;
                if (railroads == null) return;

                for (int i = 0; i < railroads.Count; i++)
                {
                    var railroad = railroads[i];
                    if (railroad == null) continue;

                    float progress = ReadFloat(railroad, "constructionprogress");
                    if (progress > 0f) continue;

                    Vector3 position = ReadVector3(railroad, "averageposition") ?? default(Vector3);
                    input.Candidates.Add(new ConstructionCandidate
                    {
                        Kind = ConstructionCandidateKind.Railroad,
                        Name = RailroadName(railroad, i),
                        Theater = TheaterFromPosition(position),
                        VanillaValid = RailroadVanillaValid(railroad, input.AllianceId),
                        SupplyPressure = input.SupplyPressure,
                        TransportPressure = input.TransportPressure,
                        SupportsActiveArmyCorridor = input.LogisticsExpansion ||
                            input.SupplyProtection ||
                            TheaterMatchesTopSupply(position, input)
                    });
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("construction-runtime:rail-candidates", "[ConstructionRuntime] railroad candidate read failed: " + ex.Message);
            }
        }

        private static void AddFortSiteObservationCandidates(ConstructionInput input)
        {
            try
            {
                var sites = AccessTools.Field(typeof(AICampaign), "fortconstructionsites")?.GetValue(null) as IList;
                if (sites == null) return;

                for (int i = 0; i < sites.Count; i++)
                {
                    if (!(sites[i] is Vector3 position)) continue;
                    input.Candidates.Add(new ConstructionCandidate
                    {
                        Kind = ConstructionCandidateKind.Fort,
                        Name = "fort-site-" + i,
                        Theater = TheaterFromPosition(position),
                        VanillaValid = true,
                        SafeRear = SafeRear(position, input.AllianceId),
                        CriticalDefense = input.CapitalThreat > 0.35f,
                        CapitalThreat = input.CapitalThreat
                    });
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("construction-runtime:fort-sites", "[ConstructionRuntime] fort site read failed: " + ex.Message);
            }
        }

        private static int CountActiveRailroads(int alliance)
        {
            try { return Mathf.FloorToInt(BattleUnits.Railroad.GetConstructedRailroads(alliance)); }
            catch
            {
                try
                {
                    int count = 0;
                    var railroads = AccessTools.Field(typeof(BattleUnits), "railroad")?.GetValue(null) as IList;
                    if (railroads == null) return 0;
                    for (int i = 0; i < railroads.Count; i++)
                    {
                        var railroad = railroads[i];
                        if (railroad == null) continue;
                        float progress = ReadFloat(railroad, "constructionprogress");
                        int constructingAlliance = ReadInt(railroad, "alliancetoconstruct", -1);
                        if (progress > 0f && progress < 1f && constructingAlliance == alliance)
                            count++;
                    }
                    return count;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("construction-runtime:active-railroads", "[ConstructionRuntime] active railroad read failed: " + ex.Message);
                    return 0;
                }
            }
        }

        private static float NearbyWoundedPressure(IIP place, int alliance)
        {
            try
            {
                if (place == null || place.unitsinrange == null) return 0f;
                float wounded = 0f;
                for (int i = 0; i < place.unitsinrange.Count; i++)
                {
                    var unit = place.unitsinrange[i];
                    if (unit != null && unit.alliance == alliance)
                        wounded += unit.groupwounded;
                }
                return Clamp01(wounded / 10000f);
            }
            catch { return 0f; }
        }

        private static bool SafeRear(Vector3 position, int alliance)
        {
            try
            {
                var battleUnits = GameObject.Find("GameController")?.GetComponent<BattleUnits>();
                if (battleUnits == null || battleUnits.frontline2 == null || battleUnits.frontline2.numberofupdates <= 0)
                    return true;
                return battleUnits.frontline2.GetSideOnPosition(position) == alliance;
            }
            catch { return true; }
        }

        private static bool SupportsActiveCorridor(Vector3 position, ConstructionInput input)
        {
            return input.SupplyProtection ||
                input.LogisticsExpansion ||
                input.CapitalThreat > 0.35f ||
                TheaterMatchesTopSupply(position, input);
        }

        private static bool TheaterMatchesTopSupply(Vector3 position, ConstructionInput input)
        {
            if (string.IsNullOrEmpty(input.TopSupplyTheater)) return false;
            return string.Equals(
                TheaterFromPosition(position).ToString(),
                input.TopSupplyTheater,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool RailroadVanillaValid(object railroad, int alliance)
        {
            try
            {
                if (ReadFloat(railroad, "constructionprogress") > 0f) return false;
                var scriptRef = AccessTools.Field(railroad.GetType(), "scriptref")?.GetValue(railroad);
                if (scriptRef != null && !ReadBool(scriptRef, "IsPermitted", true)) return false;

                var method = AccessTools.Method(railroad.GetType(), "AllianceOwnsAllIIPs");
                if (method == null) return true;
                return Convert.ToBoolean(method.Invoke(railroad, new object[] { alliance }));
            }
            catch { return false; }
        }

        private static string RailroadName(object railroad, int index)
        {
            try
            {
                var scriptRef = AccessTools.Field(railroad.GetType(), "scriptref")?.GetValue(railroad);
                var name = scriptRef != null
                    ? AccessTools.Field(scriptRef.GetType(), "RailroadName")?.GetValue(scriptRef) as string
                    : null;
                return string.IsNullOrEmpty(name) ? "railroad-" + index : name;
            }
            catch { return "railroad-" + index; }
        }

        private static bool IsArmsIndustry(int buildingTypeId, string name)
        {
            string lower = (name ?? "").ToLowerInvariant();
            return buildingTypeId == 8 ||
                buildingTypeId == 10 ||
                buildingTypeId == 12 ||
                lower.Contains("foundr") ||
                lower.Contains("iron") ||
                lower.Contains("factor");
        }

        private static float ReadFloat(object target, string fieldName)
        {
            try
            {
                var value = AccessTools.Field(target.GetType(), fieldName)?.GetValue(target);
                return value == null ? 0f : Convert.ToSingle(value);
            }
            catch { return 0f; }
        }

        private static int ReadInt(object target, string fieldName, int fallback)
        {
            try
            {
                var value = AccessTools.Field(target.GetType(), fieldName)?.GetValue(target);
                return value == null ? fallback : Convert.ToInt32(value);
            }
            catch { return fallback; }
        }

        private static bool ReadBool(object target, string fieldName, bool fallback)
        {
            try
            {
                var value = AccessTools.Field(target.GetType(), fieldName)?.GetValue(target);
                return value == null ? fallback : Convert.ToBoolean(value);
            }
            catch { return fallback; }
        }

        private static Vector3? ReadVector3(object target, string fieldName)
        {
            try
            {
                var value = AccessTools.Field(target.GetType(), fieldName)?.GetValue(target);
                return value is Vector3 position ? position : (Vector3?)null;
            }
            catch { return null; }
        }

        private static Theater TheaterFromPosition(Vector3 position)
        {
            return CampaignMapTheaterRuntime.FromPosition(position);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
