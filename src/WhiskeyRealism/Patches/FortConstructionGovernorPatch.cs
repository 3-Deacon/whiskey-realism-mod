using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic.Construction;
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

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction, ref List<Vector3> __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.fort-governor.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
            __state = null;

            try
            {
                if (!Enabled()) return;

                var field = FortSitesField();
                var original = field?.GetValue(null) as List<Vector3>;
                if (original == null || original.Count == 0) return;

                int alliance = AICampaignReflect.GetAllianceId(_aifaction);
                if (alliance < 0 || alliance > 1) return;

                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;

                int runthrough = Runthrough();
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

                var cache = _siteCaches[alliance];
                cache.BeginCall(runthrough == 0, FortSiteDecisionCache.Signature(original.Count, FortCount(), OrderCount()));

                Vector3 unitPosition = ((Component)unit).transform.position;
                float range = GamePrefs.rangefortconstructionsite;
                var filtered = new List<Vector3>(original.Count);
                int suppressed = 0;

                for (int i = 0; i < original.Count; i++)
                {
                    var site = original[i];
                    // Out of this unit's range vanilla rejects the site itself (16389).
                    if (Tools.GetXZDistance(unitPosition, site) > range)
                    {
                        filtered.Add(site);
                        continue;
                    }

                    if (!cache.TryGet(i, out var decision))
                    {
                        decision = FortConstructionGovernor.Decide(BuildContext(site, alliance, faction));
                        cache.Store(i, decision);
                    }

                    if (decision.Allow)
                    {
                        filtered.Add(site);
                        continue;
                    }

                    suppressed++;
                    LogSuppression(alliance, site, decision);
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
                RestoreSites(__state);
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, List<Vector3> __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.fort-governor.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                RestoreSites(__state);
                return __exception;
            }
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
                ThreatRatio = LocalThreatRatio(site, radius * 1.5f, faction)
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

        private static float LocalThreatRatio(Vector3 site, float radius, object faction)
        {
            try
            {
                float own = SumStrength(ReadList(faction, "ownunits"), site, radius);
                float enemy = SumStrength(ReadList(faction, "enemyunits"), site, radius);
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
