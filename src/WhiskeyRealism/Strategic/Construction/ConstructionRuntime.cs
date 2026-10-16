using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Construction
{
    public static class ConstructionRuntime
    {
        public static ConstructionInput BuildInput(
            int alliance,
            FiscalOutput fiscal,
            FrontSectorLedger front,
            FormationDirectiveLedger formation)
        {
            var input = NewInput(alliance, fiscal);
            input.ActiveRailroadStarts = CountActiveRailroads(alliance);

            FillPressure(input, front, formation);
            AddPrivateBuildingCandidates(input);
            AddRailroadCandidates(input);
            AddFortSiteObservationCandidates(input);
            AddDepotTaskCandidates(input);
            return input;
        }

        // The depot task executor's own evaluation (plan G5c): the fiscal and pressure facts and one SupplyDepot candidate per plan
        // depot task, today's, so the ledger's verdict and its TopSupplyDepot name today's tasks (the full BuildInput above runs
        // weekly and lags the plan). The private-building, railroad and fort scans are not repeated.
        // The ledger's depot rules read the fiscal posture, the bond floor and the pressures, never the era stage, so none is passed
        // (the planner reads no era stage, plan decision 7).
        internal static ConstructionInput BuildDepotInput(
            int alliance,
            FiscalOutput fiscal,
            FrontSectorLedger front,
            FormationDirectiveLedger formation)
        {
            var input = NewInput(alliance, fiscal);
            FillPressure(input, front, formation);
            AddDepotTaskCandidates(input);
            return input;
        }

        private static ConstructionInput NewInput(int alliance, FiscalOutput fiscal)
        {
            return new ConstructionInput
            {
                AllianceId = alliance,
                CurrentYear = SafeYear(),
                FiscalPosture = fiscal != null ? fiscal.Posture : FiscalPosture.BalancedWar,
                CurrentRating = SafeRating(alliance),
                BondFloorRating = SafeBondFloor(),
                SupplyProtection = fiscal != null && fiscal.SupplyProtection,
                LogisticsExpansion = fiscal != null && fiscal.LogisticsExpansion,
                TopSupplyTheater = fiscal != null ? fiscal.TheaterSupplyPriority ?? "" : ""
            };
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
                    input.FormationCount = formation.Assignments != null ? formation.Assignments.Count : 0;
                    // Area key (PlannerAreaKeys), kept separate from the fiscal Theater name.
                    if (!string.IsNullOrEmpty(formation.Pressure.TopSupplyAreaKey))
                        input.TopSupplyAreaKey = formation.Pressure.TopSupplyAreaKey;
                }

                if (front == null || front.Sectors == null) return;

                int count = 0;
                float supply = 0f;
                foreach (var sector in front.Sectors)
                {
                    if (sector == null) continue;
                    count++;
                    supply += 1f - Clamp01(sector.AverageSupply);
                }

                ConstructionThreat.Apply(input, front);
                if (count <= 0) return;
                input.SupplyPressure = Clamp01(supply / count);
                input.AmmoPressure = ConstructionIntentLedger.HasLowFormationShare(input.LowAmmoFormationCount, input.FormationCount, null)
                    ? Clamp01(input.SupplyPressure + 0.15f)
                    : Clamp01(input.SupplyPressure * 0.5f);
                input.TransportPressure = input.LogisticsExpansion ? Math.Max(input.SupplyPressure, 0.35f) : input.SupplyPressure;
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
                        SiteIndex = i,
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
                    var theater = TheaterFromPosition(position);
                    float siteThreat = ConstructionThreat.ForTheater(input.SectorThreats, theater);
                    input.Candidates.Add(new ConstructionCandidate
                    {
                        Kind = ConstructionCandidateKind.Fort,
                        SiteIndex = i,
                        Name = "fort-site-" + i,
                        Theater = theater,
                        VanillaValid = true,
                        CriticalDefense = siteThreat > 0.35f,
                        CapitalThreat = siteThreat
                    });
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("construction-runtime:fort-sites", "[ConstructionRuntime] fort site read failed: " + ex.Message);
            }
        }

        // The planner's depot tasks as SupplyDepot candidates (plan G5c): the ledger's depot kind was never fed before. The plan is the
        // published one (CampaignPlanStore), so a patch-side caller and the review read the same day's tasks.
        private static void AddDepotTaskCandidates(ConstructionInput input)
        {
            try
            {
                if (!CampaignPlanStore.TryGetPlan(input.AllianceId, out var plan)) return;
                int alliance = input.AllianceId;
                DepotCandidates.Add(input, plan,
                    (x, z) => TheaterFromPosition(new Vector3(x, 0f, z)),
                    (x, z) => NearestOwnIip(alliance, x, z) != null);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("construction-runtime:depot-candidates", "[ConstructionRuntime] depot candidate read failed: " + ex.Message);
            }
        }

        // The IIP a depot wish at (x, z) calls (AddConstructionWish needs one, 97488; vanilla's job 19 passes the unit's
        // closestiipforsupply, 14770): the nearest visible IIP the alliance owns that has no building under construction (a wish
        // at an IIP with one returns that building and places nothing, 97481-97484). Null when none.
        internal static IIP NearestOwnIip(int alliance, float x, float z)
        {
            var iips = BattleUnits.iips;
            if (iips == null) return null;
            IIP best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < iips.Count; i++)
            {
                var iip = iips[i];
                if ((UnityEngine.Object)iip == null || iip.allianceowner != alliance || iip.IIPType == IIP.id_invisible) continue;
                if ((UnityEngine.Object)iip.currentlyunderconstruction != null) continue;
                Vector3 p = ((Component)iip).transform.position;
                float dx = p.x - x, dz = p.z - z;
                float sq = dx * dx + dz * dz;
                if (sq >= bestSq) continue;
                bestSq = sq;
                best = iip;
            }
            return best;
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
                ConstructionThreat.ForTheater(input.SectorThreats, TheaterFromPosition(position)) > 0.35f ||
                TheaterMatchesTopSupply(position, input);
        }

        private static bool TheaterMatchesTopSupply(Vector3 position, ConstructionInput input)
        {
            if (string.IsNullOrEmpty(input.TopSupplyTheater) && string.IsNullOrEmpty(input.TopSupplyAreaKey)) return false;
            return ConstructionSupplyCorridor.Matches(
                input.TopSupplyTheater,
                input.TopSupplyAreaKey,
                TheaterFromPosition(position),
                PlannerAreaKeys.At(input.AllianceId, position.x, position.z));
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
