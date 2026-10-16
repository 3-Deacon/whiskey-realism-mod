using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AICampaign.CheckFortConstruction(int) (decompile 16347-16405) is job 21 of
    // UpdateUnitAI (11520-11527), called once per own unit per pass. It returns before
    // reading fortconstructionsites when bfs/bunits are null (16360-16367), when the
    // faction already has a fort order (16368; IsFactionAlreadyConstructingFort 9694), or
    // when the visited unit fails the idle/strength/op gates (16373-16381); per site it
    // requires the unit within GamePrefs.rangefortconstructionsite (16389).
    //
    // This Prefix temporarily filters saturated sites for the current call and restores
    // the vanilla list in Postfix/Finalizer. It mirrors those early returns before any
    // scoring, scores only sites inside the unit's construction range, and caches site
    // decisions for the faction's job-21 cycle.
    //
    // Campaign planner fort steering (plan G5c, Enable Fort Steering): vanilla orders the unit onto the FIRST site in list order
    // that passes its tests (16383-16392), so the list order is its only preference. When a site the planner's board marks as on a
    // MainEffort axis or in a Hold / Screen sector passes every test vanilla applies to this unit (other forts, frontline, the
    // unit's ability, the capital defender's own distance rule: asked of vanilla's own calls) and the governor, every other site in
    // the unit's range is hidden for the call, the one the construction ledger ranks highest first (FortSitePreference). Out-of-range
    // sites stay (vanilla rejects them itself); when no such site is eligible nothing extra is hidden. The one-order-per-alliance
    // rule, the range and the builder logic are vanilla's. The same snapshot-restore carries both filters.
    //
    // Depot builder hold (plan G5c, verify F6): a unit the campaign planner holds as the builder of a depot task
    // (CampaignWithholdView.HoldsDepotBuilder: the ledger's hold under the withhold view's plan-freshness and executors-allowed test) is not sent to build a fort for the call: every in-range site is hidden, so vanilla's job 21
    // (which does not read the availability Postfixes and excludes a unit only once its depot stands, 16376-16381) cannot walk the
    // builder off its site between its arrival and the wish. Out-of-range sites stay, vanilla rejects them itself.
    [HarmonyPatch(typeof(AICampaign), "CheckFortConstruction")]
    internal static class FortConstructionGovernorPatch
    {
        private static FieldInfo _fortSitesField;
        private static FieldInfo _fortOrdersField;
        private static FieldInfo _runthroughField;
        private static FieldInfo _bfsField;
        private static FieldInfo _bunitsField;
        private static readonly Dictionary<string, FieldInfo> _factionFields = new Dictionary<string, FieldInfo>();
        private static readonly FortSiteDecisionCache[] _siteCaches = { new FortSiteDecisionCache(), new FortSiteDecisionCache() };
        private static readonly HashSet<string> _loggedSuppressions = new HashSet<string>();
        private static readonly HashSet<string> _loggedPreferences = new HashSet<string>();

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction, ref List<Vector3> __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.fort-governor.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.fort-governor.prefix");
            __state = null;

            try
            {
                bool governor = Enabled();
                bool steering = SteeringEnabled();
                bool holdBuilders = DepotHoldEnabled();
                if (!governor && !steering && !holdBuilders) return;

                var field = FortSitesField();
                var original = field?.GetValue(null) as List<Vector3>;
                if (original == null || original.Count == 0) return;

                int alliance = AICampaignReflect.GetAllianceId(_aifaction);
                if (alliance < 0 || alliance > 1) return;

                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;

                int runthrough = Runthrough();
                var cache = _siteCaches[alliance];
                cache.ObserveRunthrough(runthrough);
                var unit = UnitAt(faction, runthrough);
                var gate = new FortConstructionCallGate
                {
                    HasBattlefieldSetup = StaticFieldPresent(ref _bfsField, "bfs"),
                    HasBattleUnits = StaticFieldPresent(ref _bunitsField, "bunits"),
                    FactionAlreadyConstructing = FactionAlreadyConstructing(alliance),
                    UnitPresent = unit != null,
                    UnitEligible = unit != null && UnitEligible(faction, unit, alliance)
                };
                if (!FortConstructionGovernor.ShouldEvaluateSites(gate)) return;

                cache.BeginCall(FortSiteDecisionCache.Signature(original.Count, FortCount(), OrderCount()));

                Vector3 unitPosition = ((Component)unit).transform.position;
                float range = GamePrefs.rangefortconstructionsite;
                bool heldBuilder = holdBuilders && CampaignWithholdView.HoldsDepotBuilder(alliance, unit.GetInstanceID());
                var inRange = new bool[original.Count];
                var allowed = new bool[original.Count];
                for (int i = 0; i < original.Count; i++)
                {
                    var site = original[i];
                    // Out of this unit's range vanilla rejects the site itself (16389).
                    inRange[i] = Tools.GetXZDistance(unitPosition, site) <= range;
                    allowed[i] = !heldBuilder;
                    if (!inRange[i] || !governor) continue;

                    if (!cache.TryGet(i, out var decision))
                    {
                        decision = FortConstructionGovernor.Decide(BuildContext(site, alliance, faction));
                        cache.Store(i, decision);
                    }

                    allowed[i] = !heldBuilder && decision.Allow;
                    if (!decision.Allow) LogSuppression(alliance, site, decision);
                }

                int preferredSite = steering ? PreferredSite(alliance, faction, unit, original, inRange, allowed) : -1;
                var filtered = new List<Vector3>(original.Count);
                int suppressed = 0;
                for (int i = 0; i < original.Count; i++)
                {
                    if (!inRange[i]) filtered.Add(original[i]);
                    else if (!allowed[i]) suppressed++;
                    else if (preferredSite >= 0 && i != preferredSite) suppressed++;
                    else filtered.Add(original[i]);
                }

                if (suppressed <= 0) return;

                __state = original;
                field.SetValue(null, filtered);
                OnceLog.Info("fort-governor:wired", "FortConstructionGovernorPatch wired (CheckFortConstruction site filter)");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("fort-governor:prefix", "[Patch:FortGovernor] prefix failed: " + ex.Message);
                RestoreSites(__state);
                __state = null;
            }
            } // end using TelemetryPerf.Scope
        }

        [HarmonyPostfix]
        internal static void Postfix(List<Vector3> __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.fort-governor.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.fort-governor.postfix");
                RestoreSites(__state);
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, List<Vector3> __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.fort-governor.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.fort-governor.finalizer");
                RestoreSites(__state);
                return __exception;
            }
        }

        // The site index (into the original list) of the preferred, eligible site to leave visible, -1 when steering has none: the
        // board must be fresh, and the site must be on an axis or in a threatened sector, in this unit's range, allowed by the governor
        // and one that vanilla's own tests accept for this unit. Only preferred sites are put through vanilla's tests (a handful per
        // call at most), and only when the board has any.
        private static int PreferredSite(int alliance, object faction, Regiment unit, List<Vector3> sites, bool[] inRange, bool[] allowed)
        {
            if (!ConstructionSteeringBoard.TryRead(alliance, out var board)) return -1;
            var preferred = new bool[sites.Count];
            var eligible = new bool[sites.Count];
            bool any = false;
            for (int i = 0; i < sites.Count; i++)
            {
                if (!inRange[i] || !allowed[i] || i >= board.FortPreferred.Length || !board.FortPreferred[i]) continue;
                preferred[i] = true;
                eligible[i] = VanillaWouldTake(faction, unit, sites[i], alliance);
                any |= eligible[i];
            }
            if (!any) return -1;
            int chosen = FortSitePreference.Choose(preferred, eligible, board.FortRank);
            if (chosen >= 0) LogPreference(alliance, chosen);
            return chosen;
        }

        // The per-site tests of CheckFortConstruction (16383-16392), asked of vanilla's own calls: no fort of any owner within
        // maxdistancetootherfort (x 0.6 for a capital defender), the site close to the frontline, the unit able to build there, and a
        // capital defender only on a site no nearer the capital than itself. The unit's range was tested by the caller.
        private static bool VanillaWouldTake(object faction, Regiment unit, Vector3 site, int alliance)
        {
            try
            {
                var bunits = StaticValue<BattleUnits>(ref _bunitsField, "bunits");
                var bfs = StaticValue<BattlefieldSetup>(ref _bfsField, "bfs");
                var capital = GameVars.alliance[alliance].capital;
                if ((UnityEngine.Object)bunits == null || (UnityEngine.Object)bfs == null || (UnityEngine.Object)capital == null) return false;
                bool capitalDefender = Contains(faction, "groupstodefendcapital", unit);
                Vector3 capitalPosition = ((Component)capital).transform.position;
                if (capitalDefender && Tools.GetXZDistance(((Component)unit).transform.position, capitalPosition) > Tools.GetXZDistance(site, capitalPosition))
                    return false;
                float factor = capitalDefender ? 0.6f : 1f;
                return bunits.GetClosestBuilding(site, CBuilding.id_fort, -1, 0f, GamePrefs.maxdistancetootherfort * factor) == null
                    && bfs.CheckIfPositionIsCloseToFrontline(site, alliance, GamePrefs.maxdistancefrontlinefortconstruction)
                    && AICampaign.FortConstructionOrder.IsUnitAbleToConstructFort(unit, site);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("fort-governor:preference", "[Patch:FortGovernor] a preferred site could not be tested (vanilla's list order stands): " + ex.Message);
                return false;
            }
        }

        private static T StaticValue<T>(ref FieldInfo cached, string name) where T : class
        {
            if (cached == null) cached = AccessTools.Field(typeof(AICampaign), name);
            return cached?.GetValue(null) as T;
        }

        private static bool DepotHoldEnabled()
        {
            try
            {
                return Plugin.Instance != null &&
                    Plugin.Instance.Enabled != null &&
                    Plugin.Instance.Enabled.Value &&
                    Plugin.EnableSupplyDepotSteering != null &&
                    Plugin.EnableSupplyDepotSteering.Value &&
                    CampaignPlannerRuntime.ExecutorsEnabled();
            }
            catch { return false; }
        }

        private static bool SteeringEnabled()
        {
            try
            {
                return Plugin.Instance != null &&
                    Plugin.Instance.Enabled != null &&
                    Plugin.Instance.Enabled.Value &&
                    Plugin.EnableFortSteering != null &&
                    Plugin.EnableFortSteering.Value &&
                    CampaignPlannerRuntime.ExecutorsEnabled();
            }
            catch { return false; }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null &&
                    Plugin.Instance.Enabled != null &&
                    Plugin.Instance.Enabled.Value &&
                    Plugin.Instance.FortConstructionGovernorEnabled != null &&
                    Plugin.Instance.FortConstructionGovernorEnabled.Value;
            }
            catch { return false; }
        }

        private static int Runthrough()
        {
            try
            {
                if (_runthroughField == null)
                    _runthroughField = AccessTools.Field(typeof(AICampaign), "currentfortconstructionunitrunthrough");
                return _runthroughField != null ? (int)_runthroughField.GetValue(null) : -1;
            }
            catch { return -1; }
        }

        private static bool StaticFieldPresent(ref FieldInfo cached, string name)
        {
            try
            {
                if (cached == null) cached = AccessTools.Field(typeof(AICampaign), name);
                return cached != null && cached.GetValue(null) is UnityEngine.Object obj && obj != null;
            }
            catch { return false; }
        }

        private static bool FactionAlreadyConstructing(int alliance)
        {
            try { return AICampaign.FortConstructionOrder.IsFactionAlreadyConstructingFort(alliance); }
            catch { return false; }
        }

        private static Regiment UnitAt(object faction, int index)
        {
            var units = ReadList(faction, "ownunits");
            if (units == null || index < 0 || index >= units.Count) return null;
            var unit = units[index] as Regiment;
            return unit != null ? unit : null; // Unity null check
        }

        // Cheap mirror of the vanilla unit gate (16376-16381). GetGroupSupplyState,
        // DefensiveMovingOrder.OrderRunning and RaidForce.IsRaidUnit are left to vanilla:
        // missing one only means a harmless extra filter pass.
        private static bool UnitEligible(object faction, Regiment unit, int alliance)
        {
            try
            {
                if (GameVars.alliance == null || alliance >= GameVars.alliance.Length) return false;
                var state = GameVars.alliance[alliance];
                if (state == null || state.capital == null) return false;
                bool capitalDefender = Contains(faction, "groupstodefendcapital", unit);
                if (!(unit.groupstrengthactive < state.troopsfielded * GamePrefs.maxstrengthunitsconstructfort || capitalDefender))
                    return false;
                return unit.regimentpaths <= 0 &&
                    !unit.onretreat &&
                    !unit.inbattle &&
                    unit.garrisonreference == null &&
                    !Contains(faction, "unitsinoffensiveoperations", unit) &&
                    !Contains(faction, "unitsindefensiveoperations", unit) &&
                    !Contains(faction, "unitsconstructingsupplydepots", unit);
            }
            catch { return true; }
        }

        private static bool Contains(object faction, string fieldName, Regiment unit)
        {
            var list = ReadList(faction, fieldName);
            return list != null && list.Contains(unit);
        }

        private static int FortCount()
        {
            try { return BattleUnits.fort != null ? BattleUnits.fort.Count : 0; }
            catch { return 0; }
        }

        private static int OrderCount()
        {
            try { return (FortOrdersField()?.GetValue(null) as IList)?.Count ?? 0; }
            catch { return 0; }
        }

        private static FortConstructionSiteContext BuildContext(Vector3 site, int alliance, object faction)
        {
            float radius = SaturationRadius();
            return new FortConstructionSiteContext
            {
                ExistingFortCount = CountExistingForts(site, radius),
                ActiveOrderCount = CountActiveOrders(site, radius),
                NearCapital = IsNearCapital(site, alliance),
                ThreatRatio = LocalThreatRatio(site, radius * 1.5f, faction, alliance)
            };
        }

        private static int CountExistingForts(Vector3 site, float radius)
        {
            try
            {
                int count = 0;
                if (BattleUnits.fort == null) return 0;

                for (int i = 0; i < BattleUnits.fort.Count; i++)
                {
                    var fort = BattleUnits.fort[i];
                    if (fort == null) continue;
                    if (Tools.GetXZDistance(((Component)fort).transform.position, site) <= radius)
                        count++;
                }

                return count;
            }
            catch { return 0; }
        }

        private static int CountActiveOrders(Vector3 site, float radius)
        {
            try
            {
                var orders = FortOrdersField()?.GetValue(null) as IList;
                if (orders == null) return 0;

                int count = 0;
                for (int i = 0; i < orders.Count; i++)
                {
                    var order = orders[i];
                    if (order == null) continue;
                    if (order is AICampaign.FortConstructionOrder fortOrder &&
                        Tools.GetXZDistance(fortOrder.location, site) <= radius)
                        count++;
                }

                return count;
            }
            catch { return 0; }
        }

        private static bool IsNearCapital(Vector3 site, int alliance)
        {
            try
            {
                if (GameVars.alliance == null || alliance < 0 || alliance >= GameVars.alliance.Length)
                    return false;
                var capital = GameVars.alliance[alliance].capital;
                if (capital == null) return false;
                float radius = Math.Max(GamePrefs.maxdistancedefensiveoperations, SaturationRadius() * 2f);
                return Tools.GetXZDistance(((Component)capital).transform.position, site) <= radius;
            }
            catch { return false; }
        }

        // Believed enemy (G1: fog-honest, confidence-weighted; a sighting shows no morale) over own
        // strength x morale near the site. The beliefs are read-only mod state.
        private static float LocalThreatRatio(Vector3 site, float radius, object faction, int alliance)
        {
            try
            {
                float own = SumStrength(ReadList(faction, "ownunits"), site, radius);
                float enemy = EnemyBeliefQuery.StrengthWithin(EnemyDetectionRuntime.BeliefsFor(alliance), site.x, site.z, radius);
                if (enemy <= 0f) return 0f;
                return enemy / Math.Max(1f, own);
            }
            catch { return 0f; }
        }

        private static float SumStrength(IList units, Vector3 site, float radius)
        {
            if (units == null) return 0f;

            float strength = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (unit == null) continue;
                if (Tools.GetXZDistance(((Component)unit).transform.position, site) <= radius)
                    strength += Math.Max(0f, unit.groupstrengthactive) * Math.Max(0.25f, unit.groupmorale);
            }

            return strength;
        }

        private static IList ReadList(object faction, string fieldName)
        {
            try
            {
                if (!_factionFields.TryGetValue(fieldName, out var field))
                {
                    field = AccessTools.Field(faction.GetType(), fieldName);
                    _factionFields[fieldName] = field;
                }
                return field?.GetValue(faction) as IList;
            }
            catch { return null; }
        }

        private static float SaturationRadius()
        {
            try { return Math.Max(2f, GamePrefs.maxdistancetootherfort * 3f); }
            catch { return 12f; }
        }

        private static void RestoreSites(List<Vector3> original)
        {
            try
            {
                if (original != null)
                    FortSitesField()?.SetValue(null, original);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("fort-governor:restore", "[Patch:FortGovernor] restore failed: " + ex.Message);
            }
        }

        private static void LogSuppression(
            int alliance,
            Vector3 site,
            FortConstructionGovernorDecision decision)
        {
            if (_loggedSuppressions.Count >= 16) return;

            string key = alliance + ":" + decision.Reason + ":" +
                Mathf.RoundToInt(site.x / 5f) + ":" +
                Mathf.RoundToInt(site.z / 5f);
            if (!_loggedSuppressions.Add(key)) return;

            TelemetryRouter.LegacyInfo(
                $"[ConstructionTelemetry] alliance={alliance} reason={decision.Reason} " +
                $"current={decision.CurrentCount} soft={decision.SoftCap} hard={decision.HardCap}",
                TelemetryLayer.Campaign);
        }

        // At most 16 distinct rows per session, as the suppression rows (a set of their own, so neither starves the other).
        private static void LogPreference(int alliance, int site)
        {
            if (_loggedPreferences.Count >= 16) return;
            if (!_loggedPreferences.Add(alliance + ":" + site)) return;
            TelemetryRouter.LegacyInfo(
                $"[ConstructionTelemetry] alliance={alliance} reason=planner-axis-preference site={site}",
                TelemetryLayer.Campaign);
        }

        private static FieldInfo FortSitesField()
        {
            return _fortSitesField ?? (_fortSitesField = AccessTools.Field(typeof(AICampaign), "fortconstructionsites"));
        }

        private static FieldInfo FortOrdersField()
        {
            return _fortOrdersField ?? (_fortOrdersField = AccessTools.Field(typeof(AICampaign), "fortconstructionorders"));
        }
    }
}
