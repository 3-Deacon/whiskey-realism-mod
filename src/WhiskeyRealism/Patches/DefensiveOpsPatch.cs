using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // v0.2.2 concrete steering for vanilla AICampaign.AssignUnitToDefendCapital
    // (decompile 11668). Vanilla computes num2 (capital defense need, 11712-11741), drops
    // invalid defenders, removes the FARTHEST defender when num2 <= 0 (~11778),
    // adds the closest eligible unit when num2 > 0 (11786-11817), then moves every
    // groupstodefendcapital entry toward the capital (~11829).
    //
    // 2026-10-14 (C-DefOps): this used to be a Postfix that added a candidate AFTER vanilla's
    // move loop; the unit never moved, vanilla stripped it as "farthest" the next call and the
    // Postfix re-added it: permanent churn. A Prefix pre-pledges only while vanilla's own num2 > 0
    // so vanilla keeps the pledge and its move loop moves it this call.
    //
    // 2026-10-15 (stage G, G5b): the pick comes from the live campaign plan. The need (num2) is
    // CapitalFloorPolicy.VanillaNeed: the 11712-11741 formula on the side's beliefs, not on
    // vanilla's omniscient list of every enemy. The pledged unit is the allocation's CapitalGuard nearest the
    // capital among the units vanilla's own filters (11791-11817) admit (CapitalFloorPolicy.PickPledge);
    // the excess floor stays CapitalGuard, held by the planner's mover and withheld from vanilla's
    // offensive pool, never pledged. The CIC personality, era and director modifiers are gone with the
    // CIC input. Units the coastal-defense runner has custom-ordered are never pledged.
    //
    // W&L (stage F, C4): vanilla's own pick excludes only DLC_WL.IsMovedByPlayer
    // (11791); a player-owned unit can be pledged (its move loop then skips the
    // move, 11860, and issues order 8, 11864) and the map group carrying a player
    // below map-group level is pledged and moved like any AI unit. Whiskey's own
    // pick follows WlChainRules.MayMove: the carrying group may be pledged (vanilla
    // moves it, #69 dispatches the march); the player's own command, his map group
    // (an unreliable executor is never pledged) and a CIC side are not. The need
    // counts every own group at its last waypoint, the player's map group included,
    // exactly as vanilla's num2 does, so a player standing at the capital already
    // satisfies it and nothing is pledged.
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

        // Pooled per call (main thread); cleared on entry.
        private static readonly List<FormationRecord> OwnPool = new List<FormationRecord>();
        private static readonly List<PledgeCandidate> CandidatePool = new List<PledgeCandidate>();
        private static readonly List<Regiment> CandidateUnits = new List<Regiment>();
        // The [CapitalFloor] row's last signature per alliance (rows on change).
        private static readonly string[] LastRowSignature = new string[2];

        static DefensiveOpsPatch()
        {
            CampaignScope.Register("defensive-ops-capital-floor-row", ResetRows);
        }

        private static void ResetRows()
        {
            LastRowSignature[0] = LastRowSignature[1] = null;
        }

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.defensive-ops", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.defensive-ops");
                OnceLog.Info("defensiveops", "DefensiveOpsPatch wired (capital floor pre-pledge)");

                try
                {
                    if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                    if (!FlagOn(Plugin.EnableCampaignPlanner) || !FlagOn(Plugin.EnableCampaignPlannerExecutors)) return;
                    if (StrategicCoordinator.Instance == null) return;

                    int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                    if (allianceId < 0 || allianceId >= LastRowSignature.Length) return;

                    int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                    if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                    // Capital is owned exclusively by #4 — short-circuit if no capital
                    // can be resolved so this patch never touches non-capital state.
                    // The defense intent ledger (Slice 2) owns all non-capital coastal
                    // assets; this guard makes the spec's coexistence invariant explicit.
                    if (!TryResolveCapital(allianceId, out var capitalPosition)) return;

                    if (!CampaignPlanStore.TryGetPlan(allianceId, out var plan) || !CampaignPlanStore.TryGetAllocation(allianceId, out var allocation)) return;

                    var faction = AICampaignReflect.GetFaction(_aifaction);
                    if (faction == null) return;

                    var ownUnits = ReadList(faction, "ownunits", "defensiveops:field:ownunits");
                    var capitalDefenders = ReadList(faction, "groupstodefendcapital", "defensiveops:field:capitaldefenders");
                    if (ownUnits == null || capitalDefenders == null) return;

                    LiveFormationReader.Read(ownUnits, allianceId, true, OwnPool);
                    float vanillaNeed = CapitalFloorPolicy.VanillaNeed(
                        GamePrefs.aiminimumstrengthformovement,
                        EnemyDetectionRuntime.BeliefsFor(allianceId),
                        1f,
                        capitalPosition.x,
                        capitalPosition.z,
                        GamePrefs.aimaximumdistancetosearchforunitrelocations,
                        OwnPool);

                    Regiment pledged = null;
                    if (vanillaNeed > 0f)
                    {
                        FillCandidates(
                            allianceId,
                            ownUnits,
                            capitalDefenders,
                            ReadList(faction, "unitsindefensiveoperations", "defensiveops:field:defensiveops"),
                            ReadList(faction, "unitsconstructingsupplydepots", "defensiveops:field:supplyops"),
                            capitalPosition);
                        int pick = CapitalFloorPolicy.PickPledge(allocation, vanillaNeed, CandidatePool);
                        if (pick >= 0) pledged = UnitOf(pick);
                    }

                    if (pledged != null)
                    {
                        capitalDefenders.Add(pledged);
                        RemoveFromList(faction, "unitsindefensiveoperations", pledged);
                        RemoveFromDefensiveOperation(pledged);
                        RemoveFromList(faction, "unitsinoffensiveoperations", pledged);
                        RemoveFromList(faction, "unitsconstructingsupplydepots", pledged);
                    }

                    EmitCapitalFloor(allianceId, vanillaNeed, plan.CapitalFloorStrength, plan.CapitalReach, pledged);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("defensiveops:prefix", "[Patch:DefensiveOps] prefix failed: " + ex.Message);
                }
            }
        }

        private static bool FlagOn(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
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

        private static bool TryResolveCapital(int allianceId, out Vector3 position)
        {
            position = default(Vector3);

            try
            {
                if (GameVars.alliance == null || allianceId < 0 || allianceId >= GameVars.alliance.Length) return false;
                var capital = GameVars.alliance[allianceId].capital;
                if (capital == null) return false;
                position = ((Component)capital).transform.position;
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defensiveops:capital", "[Patch:DefensiveOps] capital resolve failed: " + ex.Message);
                return false;
            }
        }

        // The pool CapitalFloorPolicy.PickPledge chooses from: every own unit with its chain class,
        // its distance to the capital, whether vanilla's own filters admit it (11791-11817) and
        // whether the coastal-defense runner has it on a custom order.
        private static void FillCandidates(
            int allianceId,
            IList ownUnits,
            IList capitalDefenders,
            IList defensiveOps,
            IList supplyOps,
            Vector3 capitalPosition)
        {
            CandidatePool.Clear();
            CandidateUnits.Clear();
            float maxDistance = GamePrefs.maxdistancedefensiveoperations;
            var chain = WlChainContext.Capture();

            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i] as Regiment;
                if (unit == null) continue;
                int instanceId = ((UnityEngine.Object)unit).GetInstanceID();
                float distance = Tools.GetXZDistance(((Component)unit).transform.position, capitalPosition);
                CandidatePool.Add(new PledgeCandidate
                {
                    InstanceId = instanceId,
                    DistanceToCapital = distance,
                    Chain = WlStrategicOrderBridge.ChainClassOf(in chain, allianceId, unit),
                    PassesVanillaFilters = EligibleForCapitalDefense(unit, capitalDefenders, defensiveOps, supplyOps, capitalPosition, maxDistance),
                    CustomOrdered = CoastalDefenseCustomOrderRunner.IsCustomOrdered(allianceId, instanceId)
                });
                CandidateUnits.Add(unit);
            }
        }

        private static Regiment UnitOf(int instanceId)
        {
            for (int i = 0; i < CandidatePool.Count; i++)
                if (CandidatePool[i].InstanceId == instanceId) return CandidateUnits[i];
            return null;
        }

        // [CapitalFloor] on change of (floor, pledged unit, whether vanilla's need is positive): the need
        // moves continuously, so the signature buckets it.
        private static void EmitCapitalFloor(int allianceId, float vanillaNeed, float floor, float reach, Regiment pledged)
        {
            string unit = pledged == null ? "none" : ((UnityEngine.Object)pledged).name;
            string signature = floor.ToString("0", CultureInfo.InvariantCulture) + "|" + unit + "|" + (vanillaNeed > 0f ? "short" : "met");
            if (string.Equals(LastRowSignature[allianceId], signature, StringComparison.Ordinal)) return;
            LastRowSignature[allianceId] = signature;
            TelemetryRouter.LegacyInfo(
                "[CapitalFloor] alliance=" + allianceId +
                " vanillaNeed=" + vanillaNeed.ToString("0", CultureInfo.InvariantCulture) +
                " floor=" + floor.ToString("0", CultureInfo.InvariantCulture) +
                " reach=" + reach.ToString("0", CultureInfo.InvariantCulture) +
                " pledged=" + unit,
                TelemetryLayer.Campaign);
        }

        private static bool EligibleForCapitalDefense(
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
    }
}
