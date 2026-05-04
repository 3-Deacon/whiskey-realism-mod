using System;
using System.Collections.Generic;
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
        private const float PlacementOriginOffset = 0.12f;
        private const float PlacementSearchEpsilon = 0.5f;
        private static readonly Dictionary<CBuilding, PendingTelegraph> PendingTelegraphs = new Dictionary<CBuilding, PendingTelegraph>();

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

                string iipReason;
                if (!IipAvailable(candidate.Iip, out iipReason))
                {
                    LogNoStart(alliance, iipReason, CandidateDetails(candidate));
                    return false;
                }

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

                if (building.BuildingType != CBuilding.id_telegraphstation || building.Owner != alliance)
                {
                    OnceLog.Warning(
                        "telegraph-ai:wrong-start:" + alliance,
                        "[TelegraphAI] AddConstructionWish returned unexpected building type=" +
                        building.BuildingType + " owner=" + building.Owner);
                    return false;
                }

                CBuilding.AIPlacement placement;
                if (!TryFindPlacement(building, candidate.Iip, out placement))
                {
                    OnceLog.Warning(
                        "telegraph-ai:no-placement:" + alliance,
                        "[TelegraphAI] alliance=" + alliance + " action=start-rejected reason=no-ai-placement");
                    return false;
                }

                string reserveReason;
                if (!TryReserveIip(candidate.Iip, building, out reserveReason))
                {
                    RemovePlacement(placement);
                    RemoveBuilding(building);
                    OnceLog.Warning(
                        "telegraph-ai:start-rejected:" + alliance + ":" + reserveReason,
                        "[TelegraphAI] alliance=" + alliance + " action=start-rejected reason=" + reserveReason);
                    return false;
                }

                PendingTelegraphs[building] = new PendingTelegraph
                {
                    Alliance = alliance,
                    Placeholder = building,
                    SupportUnit = candidate.Unit,
                    Iip = candidate.Iip,
                    RequestedSite = candidate.Site,
                    TelegraphRange = candidate.TelegraphRange,
                    SupportRange = candidate.ActualSupportRange,
                    AnchorPosition = candidate.AnchorPosition,
                    AnchorToRequestedDistance = candidate.AnchorToSiteDistance,
                    SupportToRequestedDistance = candidate.UnitToSiteDistance,
                    EffectiveConnectionRange = candidate.EffectiveConnectionRange,
                    EffectiveSupportRange = candidate.EffectiveSupportRange
                };

                OnceLog.Info(
                    "telegraph-ai:start:" + alliance,
                    "[TelegraphAI] alliance=" + alliance +
                    " action=start site=" + candidate.Site.x.ToString("0") + "," + candidate.Site.z.ToString("0") +
                    " unit=" + SafeName(candidate.Unit) +
                    " margin=" + candidate.PlacementMargin.ToString("F1") +
                    " anchorDist=" + candidate.AnchorToSiteDistance.ToString("F1") +
                    " unitDist=" + candidate.UnitToSiteDistance.ToString("F1") +
                    " connectRange=" + candidate.EffectiveConnectionRange.ToString("F1") +
                    " supportRange=" + candidate.EffectiveSupportRange.ToString("F1") +
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
            if (units == null)
            {
                LogNoStart(alliance, "unit-list-null", null);
                return false;
            }

            float bestScore = 0f;
            string lastRejectReason = "no-eligible-unit";
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                string reason;
                if (!UnitEligible(unit, alliance, out reason))
                {
                    lastRejectReason = reason;
                    LogRejection(alliance, reason, UnitDetails(unit));
                    continue;
                }
                if (UnitAlreadyCovered(unit, telegraphRange))
                {
                    lastRejectReason = "already-covered";
                    LogRejection(alliance, lastRejectReason, UnitDetails(unit));
                    continue;
                }
                if (EnemyNearby(unit))
                {
                    lastRejectReason = "enemy-nearby";
                    LogRejection(alliance, lastRejectReason, UnitDetails(unit));
                    continue;
                }

                Candidate candidate;
                if (!TryBuildCandidate(alliance, unit, construction, telegraphRange, out candidate, out reason))
                {
                    lastRejectReason = reason;
                    LogRejection(alliance, reason, UnitDetails(unit));
                    continue;
                }

                float score = CandidateScore(candidate);
                if (score <= bestScore) continue;

                best = candidate;
                bestScore = score;
            }

            if (best.Unit == null)
                LogNoStart(alliance, lastRejectReason, null);
            return best.Unit != null;
        }

        private static bool TryBuildCandidate(
            int alliance,
            Regiment unit,
            ConstructionOutput construction,
            float telegraphRange,
            out Candidate candidate,
            out string rejectReason)
        {
            candidate = default(Candidate);
            rejectReason = "unknown";

            float margin = PlacementSearchMargin();
            float effectiveConnectionRange = (telegraphRange * ConnectionRangeFactor) - margin;
            float actualSupportRange = unit.buglerange;
            float effectiveSupportRange = actualSupportRange * SupportRangeFactor;
            if (effectiveConnectionRange <= 0f)
            {
                rejectReason = "connection-range-too-small";
                return false;
            }
            if (effectiveSupportRange <= 0f)
            {
                rejectReason = "support-range-too-small";
                return false;
            }

            Vector3 unitPosition = ((Component)unit).transform.position;
            Vector3 anchor;
            if (!TryClosestConnectedAnchor(alliance, unitPosition, out anchor))
            {
                rejectReason = "no-anchor";
                return false;
            }

            float anchorDistance = XzDistance(anchor, unitPosition);
            Vector3 site = SiteTowardUnit(anchor, unitPosition, effectiveConnectionRange, effectiveSupportRange);
            if (site == default(Vector3))
            {
                rejectReason = "no-bridging-site";
                return false;
            }
            if (!ConnectedToCapitalOrChain(alliance, site, effectiveConnectionRange))
            {
                rejectReason = "site-out-of-connection-range";
                return false;
            }
            if (!SiteSupportedByUnit(unit, site, effectiveSupportRange))
            {
                rejectReason = "site-out-of-support-range";
                return false;
            }
            if (!SafeRear(site, alliance))
            {
                rejectReason = "unsafe-rear";
                return false;
            }

            string iipReason;
            var iip = unit.closestiipforsupply;
            if (!IipAvailable(iip, out iipReason))
            {
                rejectReason = iipReason;
                return false;
            }

            candidate = new Candidate
            {
                Unit = unit,
                Iip = iip,
                Site = site,
                TelegraphRange = telegraphRange,
                ActualSupportRange = actualSupportRange,
                AnchorPosition = anchor,
                AnchorDistance = anchorDistance,
                AnchorToSiteDistance = XzDistance(anchor, site),
                UnitToSiteDistance = XzDistance(unitPosition, site),
                PlacementMargin = margin,
                EffectiveConnectionRange = effectiveConnectionRange,
                EffectiveSupportRange = effectiveSupportRange,
                CommandDelayPressure = CommandDelayPressure(anchorDistance, telegraphRange),
                FormationImportance = FormationImportance(unit, construction)
            };
            return true;
        }

        private static Vector3 SiteTowardUnit(Vector3 anchor, Vector3 unitPosition, float connectionRange, float supportRange)
        {
            if (connectionRange <= 0f || supportRange <= 0f) return default(Vector3);

            Vector3 delta = unitPosition - anchor;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance <= 0.1f) return unitPosition;

            float maxFromAnchor = connectionRange;
            float minFromAnchor = Math.Max(0f, distance - supportRange);
            if (minFromAnchor > maxFromAnchor) return default(Vector3);

            float fromAnchor = Math.Min(distance, maxFromAnchor);
            if (fromAnchor < minFromAnchor) fromAnchor = minFromAnchor;
            Vector3 site = anchor + (delta.normalized * fromAnchor);
            site.y = unitPosition.y;
            return site;
        }

        private static bool UnitEligible(Regiment unit, int alliance, out string rejectReason)
        {
            rejectReason = "unknown";
            if (unit == null)
            {
                rejectReason = "unit-null";
                return false;
            }
            if (unit.alliance != alliance)
            {
                rejectReason = "wrong-alliance";
                return false;
            }
            if (!unit.istopunit)
            {
                rejectReason = "not-top-unit";
                return false;
            }
            if (unit.unittyp < 14 || unit.unittyp > 16)
            {
                rejectReason = "not-campaign-command";
                return false;
            }
            if (!IsOwnCampaignGroup(unit, out rejectReason)) return false;
            if (!((Component)unit).gameObject.activeInHierarchy)
            {
                rejectReason = "inactive";
                return false;
            }
            if (unit.onretreat || unit.isrouted || unit.inbattle)
            {
                rejectReason = "not-available";
                return false;
            }
            if (unit.garrisonreference != null)
            {
                rejectReason = "garrison";
                return false;
            }
            if (unit.closestiipforsupply == null)
            {
                rejectReason = "iip-null";
                return false;
            }
            if (unit.buglerange <= 0f)
            {
                rejectReason = "bugle-range-zero";
                return false;
            }
            if (unit.groupstrengthdirect <= MinimumSupportingStrength)
            {
                rejectReason = "strength-too-low";
                return false;
            }
            return true;
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
            if (TryCapitalPosition(alliance, out capital) && XzDistance(capital, site) <= telegraphRange)
                return true;

            try
            {
                if (BattleUnits.telegraphstation == null) return false;

                for (int i = 0; i < BattleUnits.telegraphstation.Count; i++)
                {
                    var station = BattleUnits.telegraphstation[i];
                    if (!ConnectedStation(station, alliance)) continue;
                    if (XzDistance(((Component)station).transform.position, site) <= telegraphRange)
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

        private static bool SiteSupportedByUnit(Regiment unit, Vector3 site, float supportRange)
        {
            if (unit == null || supportRange <= 0f) return false;
            return XzDistance(((Component)unit).transform.position, site) <= supportRange;
        }

        private static bool SafeRear(Vector3 position, int alliance)
        {
            try
            {
                var battleUnits = GameObject.Find("GameController")?.GetComponent<BattleUnits>();
                if (battleUnits == null ||
                    battleUnits.frontline2 == null ||
                    battleUnits.frontline2.numberofupdates <= 0)
                    return false;

                return battleUnits.frontline2.GetSideOnPosition(position) == alliance;
            }
            catch
            {
                return false;
            }
        }

        private static bool EnemyNearPosition(Vector3 position, int alliance, float range)
        {
            try
            {
                if (range <= 0f) return true;
                var units = BattleUnits.campaignunitlist ?? BattleUnits.completeunitlist;
                if (units == null) return true;

                for (int i = 0; i < units.Count; i++)
                {
                    var unit = units[i];
                    if (unit == null || unit.alliance == alliance) continue;
                    if (!((Component)unit).gameObject.activeInHierarchy) continue;
                    if (unit.isrouted) continue;
                    if (XzDistance(((Component)unit).transform.position, position) <= range)
                        return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
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

        private static float PlacementSearchMargin()
        {
            try
            {
                float maxRadius = GamePrefs.buildingsitemaxradius;
                float step = GamePrefs.buildingsiteradiusstep;
                if (float.IsNaN(maxRadius) || float.IsInfinity(maxRadius) || maxRadius < 0f)
                    return float.MaxValue / 4f;
                if (float.IsNaN(step) || float.IsInfinity(step) || step < 0f)
                    step = 0f;
                return maxRadius + step + PlacementOriginOffset + PlacementSearchEpsilon;
            }
            catch
            {
                return float.MaxValue / 4f;
            }
        }

        private static bool IipAvailable(IIP iip, out string rejectReason)
        {
            rejectReason = "unknown";
            try
            {
                if (iip == null)
                {
                    rejectReason = "iip-null";
                    return false;
                }
                if (iip.currentlyunderconstruction != null)
                {
                    rejectReason = "iip-under-construction";
                    return false;
                }
                return true;
            }
            catch
            {
                rejectReason = "iip-read-failed";
                return false;
            }
        }

        private static bool TryReserveIip(IIP iip, CBuilding building, out string rejectReason)
        {
            rejectReason = "unknown";
            try
            {
                if (iip == null)
                {
                    rejectReason = "iip-null";
                    return false;
                }

                var current = iip.currentlyunderconstruction;
                if (current != null && current != building)
                {
                    rejectReason = "iip-reservation-changed";
                    return false;
                }

                iip.currentlyunderconstruction = building;
                return true;
            }
            catch
            {
                rejectReason = "iip-reservation-failed";
                return false;
            }
        }

        private static bool TryFindPlacement(CBuilding placeholder, IIP iip, out CBuilding.AIPlacement placement)
        {
            placement = null;
            try
            {
                if (placeholder == null || CBuilding.aiplacements == null) return false;

                for (int i = 0; i < CBuilding.aiplacements.Count; i++)
                {
                    var candidate = CBuilding.aiplacements[i];
                    if (candidate == null) continue;
                    if (candidate.buildingtype != CBuilding.id_telegraphstation) continue;
                    if (candidate.buildingref != placeholder) continue;
                    if (iip != null && candidate.IIPcalled != iip) continue;

                    placement = candidate;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool TryCurrentPendingPlacement(out CBuilding placeholder, out PendingTelegraph pending)
        {
            placeholder = null;
            pending = default(PendingTelegraph);
            try
            {
                if (CBuilding.aiplacements == null || CBuilding.aiplacements.Count <= 0)
                    return false;

                var placement = CBuilding.aiplacements[0];
                if (placement == null || placement.buildingtype != CBuilding.id_telegraphstation)
                    return false;

                placeholder = placement.buildingref;
                if (placeholder == null) return false;
                return PendingTelegraphs.TryGetValue(placeholder, out pending);
            }
            catch
            {
                return false;
            }
        }

        private static void RemovePlacement(CBuilding.AIPlacement placement)
        {
            try
            {
                if (placement == null || CBuilding.aiplacements == null) return;
                CBuilding.aiplacements.Remove(placement);
            }
            catch { }
        }

        private static void RemoveBuilding(CBuilding building)
        {
            try
            {
                if (building == null) return;
                building.RemoveBuilding();
                BattleUnits.CheckForEmptyBuildingReferences(building);
            }
            catch { }
        }

        private static void ClearIipReservation(PendingTelegraph pending, CBuilding finalBuilding)
        {
            try
            {
                if (pending.Iip == null) return;
                var current = pending.Iip.currentlyunderconstruction;
                if (current == pending.Placeholder || current == finalBuilding)
                    pending.Iip.currentlyunderconstruction = null;
            }
            catch { }
        }

        private static void KeepIipReservation(PendingTelegraph pending, CBuilding finalBuilding)
        {
            try
            {
                if (pending.Iip == null || finalBuilding == null) return;
                var current = pending.Iip.currentlyunderconstruction;
                if (current == null || current == pending.Placeholder || current == finalBuilding)
                {
                    pending.Iip.currentlyunderconstruction = finalBuilding;
                    return;
                }

                OnceLog.Warning(
                    "telegraph-ai:iip-final-reservation:" + pending.Alliance + ":iip-reservation-changed",
                    "[TelegraphAI] alliance=" + pending.Alliance +
                    " action=placed-warning reason=iip-reservation-changed");
            }
            catch { }
        }

        private static bool TryValidateFinalPlacement(
            CBuilding finalBuilding,
            PendingTelegraph pending,
            int type,
            int owner,
            out string reason,
            out PlacementDistances distances)
        {
            reason = "unknown";
            distances = default(PlacementDistances);
            try
            {
                if (finalBuilding == null)
                {
                    reason = "building-null";
                    return false;
                }
                if (type != CBuilding.id_telegraphstation ||
                    finalBuilding.BuildingType != CBuilding.id_telegraphstation)
                {
                    reason = "wrong-type";
                    return false;
                }
                if (owner != pending.Alliance || finalBuilding.Owner != pending.Alliance)
                {
                    reason = "wrong-owner";
                    return false;
                }

                Vector3 finalPosition = ((Component)finalBuilding).transform.position;
                distances.RequestedToFinal = XzDistance(pending.RequestedSite, finalPosition);
                distances.AnchorToFinal = XzDistance(pending.AnchorPosition, finalPosition);

                if (pending.TelegraphRange <= 0f)
                {
                    reason = "telegraph-range-zero";
                    return false;
                }
                if (!ConnectedToCapitalOrChain(pending.Alliance, finalPosition, pending.TelegraphRange))
                {
                    reason = "not-connected";
                    return false;
                }

                if (pending.SupportUnit == null || pending.SupportRange <= 0f)
                {
                    reason = "support-unit-missing";
                    return false;
                }

                Vector3 supportPosition = ((Component)pending.SupportUnit).transform.position;
                distances.SupportToFinal = XzDistance(supportPosition, finalPosition);
                if (distances.SupportToFinal > pending.SupportRange)
                {
                    reason = "support-out-of-range";
                    return false;
                }

                if (!SafeRear(finalPosition, pending.Alliance))
                {
                    reason = "unsafe-rear";
                    return false;
                }
                if (EnemyNearPosition(finalPosition, pending.Alliance, pending.SupportRange))
                {
                    reason = "enemy-nearby";
                    return false;
                }

                reason = "ok";
                return true;
            }
            catch
            {
                reason = "validation-failed";
                return false;
            }
        }

        private static void UpdateTelegraphConnectionsIfAvailable(CBuilding finalBuilding, int alliance)
        {
            try
            {
                if (BattleUnits.telegraphstation != null)
                {
                    for (int i = 0; i < BattleUnits.telegraphstation.Count; i++)
                    {
                        var station = BattleUnits.telegraphstation[i];
                        if (station == null || station.BuildingType != CBuilding.id_telegraphstation)
                            continue;
                        station.UpdateTelegraphConnections();
                    }
                }

                var method = AccessTools.Method(typeof(CBuilding), "CheckTelegraphConnection");
                if (method != null && finalBuilding != null)
                    method.Invoke(finalBuilding, null);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "telegraph-ai:connection-update:" + alliance,
                    "[TelegraphAI] alliance=" + alliance +
                    " action=placed-warning reason=connection-update-failed message=" + ex.Message);
            }
        }

        private static string FormatPosition(Vector3 position)
        {
            return position.x.ToString("0") + "," + position.z.ToString("0");
        }

        [HarmonyPatch(typeof(CBuilding), "Place")]
        private static class PlacePatch
        {
            [HarmonyPostfix]
            private static void Postfix(
                CBuilding __result,
                int type,
                int owner,
                bool newlycreated,
                bool pay)
            {
                if (type != CBuilding.id_telegraphstation || !newlycreated || pay)
                    return;

                try
                {
                    CBuilding placeholder;
                    PendingTelegraph pending;
                    if (!TryCurrentPendingPlacement(out placeholder, out pending))
                        return;

                    PlacementDistances distances;
                    string reason;
                    if (TryValidateFinalPlacement(__result, pending, type, owner, out reason, out distances))
                    {
                        UpdateTelegraphConnectionsIfAvailable(__result, pending.Alliance);
                        KeepIipReservation(pending, __result);
                        PendingTelegraphs.Remove(placeholder);

                        Vector3 finalPosition = ((Component)__result).transform.position;
                        string message = "[TelegraphAI] alliance=" + pending.Alliance +
                            " action=placed requested=" + FormatPosition(pending.RequestedSite) +
                            " final=" + FormatPosition(finalPosition) +
                            " requestedFinalDist=" + distances.RequestedToFinal.ToString("F1") +
                            " anchorFinalDist=" + distances.AnchorToFinal.ToString("F1") +
                            " supportFinalDist=" + distances.SupportToFinal.ToString("F1");
                        if (ConstructionVerboseLoggingEnabled())
                        {
                            message +=
                                " anchorRequestedDist=" + pending.AnchorToRequestedDistance.ToString("F1") +
                                " supportRequestedDist=" + pending.SupportToRequestedDistance.ToString("F1") +
                                " connectRange=" + pending.EffectiveConnectionRange.ToString("F1") +
                                " supportRange=" + pending.EffectiveSupportRange.ToString("F1");
                        }

                        OnceLog.Info("telegraph-ai:placed:" + pending.Alliance, message);
                        return;
                    }

                    string finalPositionText = __result != null
                        ? FormatPosition(((Component)__result).transform.position)
                        : "<none>";
                    RemoveBuilding(__result);
                    ClearIipReservation(pending, __result);
                    PendingTelegraphs.Remove(placeholder);
                    OnceLog.Warning(
                        "telegraph-ai:placed-rejected:" + pending.Alliance + ":" + reason,
                        "[TelegraphAI] alliance=" + pending.Alliance +
                        " action=placed-rejected reason=" + reason +
                        " requested=" + FormatPosition(pending.RequestedSite) +
                        " final=" + finalPositionText);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning(
                        "telegraph-ai:place-postfix",
                        "[TelegraphAI] action=placed-warning reason=postfix-failed message=" + ex.Message);
                }
            }
        }

        private static bool IsOwnCampaignGroup(Regiment unit, out string rejectReason)
        {
            rejectReason = "campaign-group-mismatch";
            try
            {
                var group = BattleUnits.GetCampaignGroup(unit);
                if ((UnityEngine.Object)(object)group != (UnityEngine.Object)(object)unit)
                    return false;
                return true;
            }
            catch (Exception ex)
            {
                rejectReason = "campaign-group-read-failed";
                OnceLog.Warning(
                    "telegraph-ai:campaign-group-read",
                    "[TelegraphAI] campaign group read failed: " + ex.Message);
                return false;
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

        private static string UnitDetails(Regiment unit)
        {
            if (unit == null) return "unit=<none>";
            try
            {
                return "unit=" + SafeName(unit) +
                    " type=" + unit.unittyp +
                    " top=" + unit.istopunit +
                    " strength=" + unit.groupstrengthdirect.ToString("F0") +
                    " bugle=" + unit.buglerange.ToString("F1");
            }
            catch
            {
                return "unit=" + SafeName(unit);
            }
        }

        private static string CandidateDetails(Candidate candidate)
        {
            return "site=" + candidate.Site.x.ToString("0") + "," + candidate.Site.z.ToString("0") +
                " unit=" + SafeName(candidate.Unit) +
                " margin=" + candidate.PlacementMargin.ToString("F1") +
                " anchorDist=" + candidate.AnchorToSiteDistance.ToString("F1") +
                " unitDist=" + candidate.UnitToSiteDistance.ToString("F1") +
                " connectRange=" + candidate.EffectiveConnectionRange.ToString("F1") +
                " supportRange=" + candidate.EffectiveSupportRange.ToString("F1");
        }

        private static void LogNoStart(int alliance, string reason, string details)
        {
            string message = "[TelegraphAI] alliance=" + alliance + " action=no-start reason=" + reason;
            if (ConstructionVerboseLoggingEnabled() && !string.IsNullOrEmpty(details))
                message += " " + details;
            OnceLog.Info("telegraph-ai:no-start:" + alliance + ":" + reason, message);
        }

        private static void LogRejection(int alliance, string reason, string details)
        {
            if (!ConstructionVerboseLoggingEnabled()) return;
            OnceLog.Info(
                "telegraph-ai:reject:" + alliance + ":" + reason,
                "[TelegraphAI] alliance=" + alliance + " action=reject reason=" + reason + " " + details);
        }

        private static bool ConstructionVerboseLoggingEnabled()
        {
            try
            {
                return Plugin.Instance != null &&
                    Plugin.Instance.ConstructionVerboseLogging != null &&
                    Plugin.Instance.ConstructionVerboseLogging.Value;
            }
            catch
            {
                return false;
            }
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
            public float TelegraphRange;
            public float ActualSupportRange;
            public Vector3 AnchorPosition;
            public float AnchorDistance;
            public float AnchorToSiteDistance;
            public float UnitToSiteDistance;
            public float PlacementMargin;
            public float EffectiveConnectionRange;
            public float EffectiveSupportRange;
            public float CommandDelayPressure;
            public float FormationImportance;
        }

        private struct PendingTelegraph
        {
            public int Alliance;
            public CBuilding Placeholder;
            public Regiment SupportUnit;
            public IIP Iip;
            public Vector3 RequestedSite;
            public float TelegraphRange;
            public float SupportRange;
            public Vector3 AnchorPosition;
            public float AnchorToRequestedDistance;
            public float SupportToRequestedDistance;
            public float EffectiveConnectionRange;
            public float EffectiveSupportRange;
        }

        private struct PlacementDistances
        {
            public float RequestedToFinal;
            public float AnchorToFinal;
            public float SupportToFinal;
        }
    }
}
