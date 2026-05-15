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
    // Vanilla AICampaign.CheckFortConstruction(int) scans static fort construction
    // sites for one faction/unit and starts a FortConstructionOrder when the site
    // clears vanilla checks. This Prefix temporarily filters saturated sites for
    // the current call, then restores the vanilla list in Postfix/Finalizer.
    [HarmonyPatch(typeof(AICampaign), "CheckFortConstruction")]
    internal static class FortConstructionGovernorPatch
    {
        private static FieldInfo _fortSitesField;
        private static FieldInfo _fortOrdersField;
        private static readonly HashSet<string> _loggedSuppressions = new HashSet<string>();

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction, ref List<Vector3> __state)
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

                var filtered = new List<Vector3>(original.Count);
                int suppressed = 0;

                for (int i = 0; i < original.Count; i++)
                {
                    var site = original[i];
                    var context = BuildContext(site, alliance, faction);
                    var decision = FortConstructionGovernor.Decide(context);
                    if (decision.Allow)
                    {
                        filtered.Add(site);
                        continue;
                    }

                    suppressed++;
                    LogSuppression(alliance, site, context, decision);
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
        }

        [HarmonyPostfix]
        internal static void Postfix(List<Vector3> __state)
        {
            RestoreSites(__state);
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, List<Vector3> __state)
        {
            RestoreSites(__state);
            return __exception;
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
                    var location = AccessTools.Field(order.GetType(), "location")?.GetValue(order);
                    if (location is Vector3 pos && Tools.GetXZDistance(pos, site) <= radius)
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
            try { return AccessTools.Field(faction.GetType(), fieldName)?.GetValue(faction) as IList; }
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
            FortConstructionSiteContext context,
            FortConstructionGovernorDecision decision)
        {
            if (_loggedSuppressions.Count >= 16) return;

            string key = alliance + ":" + decision.Reason + ":" +
                Mathf.RoundToInt(site.x / 5f) + ":" +
                Mathf.RoundToInt(site.z / 5f);
            if (!_loggedSuppressions.Add(key)) return;

            TelemetryRouter.LegacyInfo(
                $"[ConstructionTelemetry] alliance={alliance} reason={decision.Reason} " +
                $"forts={context.ExistingFortCount} orders={context.ActiveOrderCount} " +
                $"soft={decision.SoftCap} hard={decision.HardCap} " +
                $"threat={context.ThreatRatio:F2} nearCapital={context.NearCapital}",
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
