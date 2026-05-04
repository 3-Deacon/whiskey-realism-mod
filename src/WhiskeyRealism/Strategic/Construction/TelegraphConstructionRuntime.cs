using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Construction
{
    public static class TelegraphConstructionRuntime
    {
        private const float MinimumSupportingStrength = 1000f;
        private const float ConnectionRangeFactor = 0.85f;
        private const float SupportRangeFactor = 0.85f;

        public static bool TryStartTelegraph(int alliance, ConstructionOutput construction)
        {
            try
            {
                if (construction == null) return false;
                if (construction.Posture == ConstructionPosture.EmergencyHold) return false;
                if (!ValidAlliance(alliance)) return false;
                if (CompanyFoundingsAtCap()) return false;
                if (CountActiveTelegraphs(alliance) >= MaxActiveTelegraphs()) return false;

                Candidate candidate;
                if (!TryFindCandidate(alliance, construction, out candidate)) return false;

                var facts = new TelegraphCandidateFacts
                {
                    ConnectedToCapitalOrChain = true,
                    SupportingUnitEligible = true,
                    SupportsActiveCommandCorridor = true,
                    SafeRear = true,
                    AlreadyCoveredByTelegraph = false,
                    CommandDelayPressure = candidate.CommandDelayPressure,
                    FormationImportance = candidate.FormationImportance
                };

                var decision = TelegraphIntentScorer.Score(facts, construction.Posture);
                if (!decision.ShouldBuild) return false;

                var building = CBuilding.AddConstructionWish(
                    CBuilding.id_telegraphstation,
                    candidate.Site,
                    candidate.Iip,
                    alliance,
                    overridealreadyconstructing: false);

                if (building == null)
                {
                    OnceLog.Warning(
                        "telegraph-ai:null-start:" + alliance,
                        "[TelegraphAI] AddConstructionWish returned null");
                    return false;
                }

                OnceLog.Info(
                    "telegraph-ai:start:" + alliance,
                    "[TelegraphAI] alliance=" + alliance +
                    " action=start site=" + candidate.Site.x.ToString("0") + "," + candidate.Site.z.ToString("0") +
                    " unit=" + SafeName(candidate.Unit) +
                    " reason=" + decision.Reason +
                    " score=" + decision.Score.ToString("F2"));
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "telegraph-ai:failed:" + alliance,
                    "[TelegraphAI] start failed: " + ex.Message);
                return false;
            }
        }

        private static bool TryFindCandidate(int alliance, ConstructionOutput construction, out Candidate best)
        {
            best = default(Candidate);
            float telegraphRange = TelegraphRange();
            if (telegraphRange <= 0f) return false;

            var units = BattleUnits.campaignunitlist ?? BattleUnits.completeunitlist;
            if (units == null) return false;

            float bestScore = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (!UnitEligible(unit, alliance)) continue;
                if (UnitAlreadyCovered(unit, telegraphRange)) continue;
                if (EnemyNearby(unit)) continue;

                Candidate candidate;
                if (!TryBuildCandidate(alliance, unit, construction, telegraphRange, out candidate)) continue;

                float score = CandidateScore(candidate);
                if (score <= bestScore) continue;

                best = candidate;
                bestScore = score;
            }

            return best.Unit != null;
        }

        private static bool TryBuildCandidate(
            int alliance,
            Regiment unit,
            ConstructionOutput construction,
            float telegraphRange,
            out Candidate candidate)
        {
            candidate = default(Candidate);

            Vector3 unitPosition = ((Component)unit).transform.position;
            Vector3 anchor;
            if (!TryClosestConnectedAnchor(alliance, unitPosition, out anchor)) return false;

            float anchorDistance = XzDistance(anchor, unitPosition);
            Vector3 site = SiteTowardUnit(anchor, unitPosition, telegraphRange, unit.buglerange);
            if (site == default(Vector3)) return false;
            if (!ConnectedToCapitalOrChain(alliance, site, telegraphRange)) return false;
            if (!SiteSupportedByUnit(unit, site)) return false;
            if (!SafeRear(site, alliance)) return false;

            candidate = new Candidate
            {
                Unit = unit,
                Iip = unit.closestiipforsupply,
                Site = site,
                AnchorDistance = anchorDistance,
                CommandDelayPressure = CommandDelayPressure(anchorDistance, telegraphRange),
                FormationImportance = FormationImportance(unit, construction)
            };
            return true;
        }

        private static Vector3 SiteTowardUnit(Vector3 anchor, Vector3 unitPosition, float telegraphRange, float bugleRange)
        {
            if (telegraphRange <= 0f || bugleRange <= 0f) return default(Vector3);

            Vector3 delta = unitPosition - anchor;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance <= 0.1f) return unitPosition;

            float maxFromAnchor = telegraphRange * ConnectionRangeFactor;
            float minFromAnchor = Math.Max(0f, distance - (bugleRange * SupportRangeFactor));
            if (minFromAnchor > maxFromAnchor) return default(Vector3);

            float fromAnchor = Math.Min(distance, maxFromAnchor);
            if (fromAnchor < minFromAnchor) fromAnchor = minFromAnchor;
            Vector3 site = anchor + (delta.normalized * fromAnchor);
            site.y = unitPosition.y;
            return site;
        }

        private static bool UnitEligible(Regiment unit, int alliance)
        {
            if (unit == null) return false;
            if (unit.alliance != alliance) return false;
            if (unit.unittyp < 0 || unit.unittyp > 13) return false;
            if (!((Component)unit).gameObject.activeInHierarchy) return false;
            if (unit.onretreat || unit.isrouted || unit.inbattle) return false;
            if (unit.garrisonreference != null) return false;
            if (unit.closestiipforsupply == null) return false;
            if (unit.buglerange <= 0f) return false;
            return unit.groupstrengthdirect > MinimumSupportingStrength;
        }

        private static bool UnitAlreadyCovered(Regiment unit, float telegraphRange)
        {
            if (unit == null) return true;
            if (unit.hastelegraphconnection != null) return true;

            try
            {
                if (BattleUnits.telegraphstation == null) return false;
                Vector3 position = ((Component)unit).transform.position;
                for (int i = 0; i < BattleUnits.telegraphstation.Count; i++)
                {
                    var station = BattleUnits.telegraphstation[i];
                    if (!ConnectedStation(station, unit.alliance)) continue;
                    if (XzDistance(position, ((Component)station).transform.position) <= telegraphRange)
                        return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private static bool EnemyNearby(Regiment unit)
        {
            try
            {
                if (unit == null || unit.buglerange <= 0f) return true;
                return unit.GetClosestEnemyUnitReg(unit.buglerange) != null;
            }
            catch
            {
                return true;
            }
        }

        private static bool TryClosestConnectedAnchor(int alliance, Vector3 target, out Vector3 anchor)
        {
            anchor = default(Vector3);
            if (!TryCapitalPosition(alliance, out anchor)) return false;

            float bestDistance = XzDistance(anchor, target);
            try
            {
                if (BattleUnits.telegraphstation == null) return true;

                for (int i = 0; i < BattleUnits.telegraphstation.Count; i++)
                {
                    var station = BattleUnits.telegraphstation[i];
                    if (!ConnectedStation(station, alliance)) continue;

                    Vector3 position = ((Component)station).transform.position;
                    float distance = XzDistance(position, target);
                    if (distance >= bestDistance) continue;

                    anchor = position;
                    bestDistance = distance;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private static bool ConnectedToCapitalOrChain(int alliance, Vector3 site, float telegraphRange)
        {
            if (telegraphRange <= 0f) return false;

            Vector3 capital;
            if (TryCapitalPosition(alliance, out capital) && XzDistance(capital, site) < telegraphRange)
                return true;

            try
            {
                if (BattleUnits.telegraphstation == null) return false;

                for (int i = 0; i < BattleUnits.telegraphstation.Count; i++)
                {
                    var station = BattleUnits.telegraphstation[i];
                    if (!ConnectedStation(station, alliance)) continue;
                    if (XzDistance(((Component)station).transform.position, site) < telegraphRange)
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool ConnectedStation(CBuilding station, int alliance)
        {
            return station != null &&
                station.Owner == alliance &&
                station.BuildingType == CBuilding.id_telegraphstation &&
                station.Condition >= 1f &&
                station.isconnected;
        }

        private static bool SiteSupportedByUnit(Regiment unit, Vector3 site)
        {
            if (unit == null || unit.buglerange <= 0f) return false;
            return XzDistance(((Component)unit).transform.position, site) <= unit.buglerange * SupportRangeFactor;
        }

        private static bool SafeRear(Vector3 position, int alliance)
        {
            try
            {
                var battleUnits = GameObject.Find("GameController")?.GetComponent<BattleUnits>();
                if (battleUnits == null ||
                    battleUnits.frontline2 == null ||
                    battleUnits.frontline2.numberofupdates <= 0)
                    return true;

                return battleUnits.frontline2.GetSideOnPosition(position) == alliance;
            }
            catch
            {
                return true;
            }
        }

        private static int CountActiveTelegraphs(int alliance)
        {
            try
            {
                int count = 0;

                if (BattleUnits.telegraphstation != null)
                {
                    for (int i = 0; i < BattleUnits.telegraphstation.Count; i++)
                    {
                        var station = BattleUnits.telegraphstation[i];
                        if (station == null) continue;
                        if (station.Owner == alliance &&
                            station.BuildingType == CBuilding.id_telegraphstation &&
                            (station.constructiontimer > 0f || station.Condition < 1f))
                            count++;
                    }
                }

                if (CBuilding.aiplacements != null)
                {
                    for (int i = 0; i < CBuilding.aiplacements.Count; i++)
                    {
                        var placement = CBuilding.aiplacements[i];
                        if (placement == null || placement.buildingtype != CBuilding.id_telegraphstation)
                            continue;

                        int owner = placement.buildingref != null
                            ? placement.buildingref.Owner
                            : (placement.IIPcalled != null ? placement.IIPcalled.allianceowner : -1);
                        if (owner == alliance)
                            count++;
                    }
                }

                return count;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "telegraph-ai:active-count",
                    "[TelegraphAI] active construction count failed: " + ex.Message);
                return MaxActiveTelegraphs();
            }
        }

        private static int MaxActiveTelegraphs()
        {
            try
            {
                if (Plugin.Instance != null &&
                    Plugin.Instance.MaxActiveTelegraphConstructionsPerFaction != null &&
                    Plugin.Instance.MaxActiveTelegraphConstructionsPerFaction.Value >= 0)
                    return Plugin.Instance.MaxActiveTelegraphConstructionsPerFaction.Value;
            }
            catch { }

            return 1;
        }

        private static bool CompanyFoundingsAtCap()
        {
            try
            {
                int cap = GameVars.debug_maxcompanyfoundings;
                if (cap <= 0) return true;

                var field = AccessTools.Field(typeof(CBuilding), "companyfoundings");
                if (field == null)
                {
                    OnceLog.Warning(
                        "telegraph-ai:companyfoundings-field",
                        "[TelegraphAI] companyfoundings field not found");
                    return true;
                }

                int current = Convert.ToInt32(field.GetValue(null));
                return current >= cap;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "telegraph-ai:companyfoundings-read",
                    "[TelegraphAI] companyfoundings read failed: " + ex.Message);
                return true;
            }
        }

        private static bool ValidAlliance(int alliance)
        {
            try
            {
                return alliance >= 0 &&
                    alliance < 2 &&
                    GameVars.alliance != null &&
                    alliance < GameVars.alliance.Length &&
                    GameVars.alliance[alliance] != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCapitalPosition(int alliance, out Vector3 position)
        {
            position = default(Vector3);
            try
            {
                if (!ValidAlliance(alliance)) return false;
                var capital = GameVars.alliance[alliance].capital;
                if (capital == null) return false;

                position = ((Component)capital).transform.position;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static float TelegraphRange()
        {
            try
            {
                float range = GamePrefs.standardtelegraphrange;
                if (float.IsNaN(range) || float.IsInfinity(range) || range <= 0f)
                    return 0f;
                return range;
            }
            catch
            {
                return 0f;
            }
        }

        private static float CandidateScore(Candidate candidate)
        {
            return candidate.CommandDelayPressure +
                candidate.FormationImportance +
                Clamp01(candidate.Unit.groupstrengthdirect / 30000f);
        }

        private static float CommandDelayPressure(float anchorDistance, float telegraphRange)
        {
            if (telegraphRange <= 0f) return 0f;
            return Clamp01(anchorDistance / telegraphRange);
        }

        private static float FormationImportance(Regiment unit, ConstructionOutput construction)
        {
            float fromStrength = unit != null ? Clamp01(unit.groupstrengthdirect / 15000f) : 0f;
            if (construction != null &&
                construction.TopTelegraph.Kind == ConstructionCandidateKind.Telegraph &&
                construction.TopTelegraph.Score > 0f)
                return Math.Max(fromStrength, Clamp01(construction.TopTelegraph.Score));
            return fromStrength;
        }

        private static string SafeName(Regiment unit)
        {
            try { return unit != null ? ((UnityEngine.Object)unit).name : "<none>"; }
            catch { return "<unknown>"; }
        }

        private static float XzDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private struct Candidate
        {
            public Regiment Unit;
            public IIP Iip;
            public Vector3 Site;
            public float AnchorDistance;
            public float CommandDelayPressure;
            public float FormationImportance;
        }
    }
}
