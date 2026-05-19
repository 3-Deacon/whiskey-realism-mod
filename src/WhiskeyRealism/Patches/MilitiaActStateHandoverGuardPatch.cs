using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    internal static class MilitiaActStateHandoverScope
    {
        private static int _depth;

        internal static bool Active => _depth > 0;

        internal static void Enter()
        {
            _depth++;
        }

        internal static void Exit()
        {
            if (_depth > 0) _depth--;
        }
    }

    // Vanilla Policies.LaunchPolicyEffect for policy 36 hands every USA/CSA
    // state to whichever side has a simple support majority. This scope lets the
    // SetNationToNewAlliance patch block only that militia-act handover path.
    [HarmonyPatch(typeof(Policies), "LaunchPolicyEffect")]
    internal static class MilitiaActStateHandoverScopePatch
    {
        [HarmonyPrefix]
        internal static void Prefix(Policy policy, int alliance, ref bool __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.militia-act-handover-scope.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                __state = false;
                try
                {
                    if (!Enabled() || !ShouldScope(policy, alliance)) return;
                    MilitiaActStateHandoverScope.Enter();
                    __state = true;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("state-handover:scope-prefix", "[Patch:StateHandover] scope prefix failed: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, bool __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.militia-act-handover-scope.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                if (__state) MilitiaActStateHandoverScope.Exit();
                return __exception;
            }
        }

        private static bool ShouldScope(Policy policy, int alliance)
        {
            if (policy == null || policy.UniquePolicyID != 36) return false;
            if (Policy.CurrentChapter < 0 || Policy.CurrentChapter > 1) return false;
            if (GameVars.alliance == null || alliance < 0 || alliance >= GameVars.alliance.Length) return false;
            return !GameVars.alliance[alliance].alreadyactivatedacts.Contains(policy);
        }

        private static bool Enabled()
        {
            try { return Plugin.Instance != null && Plugin.Instance.Enabled.Value; }
            catch { return false; }
        }
    }

    [HarmonyPatch(typeof(GameVars), "SetNationToNewAlliance")]
    internal static class MilitiaActStateHandoverGuardPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(int state, int newalliance)
        {
            using (TelemetryPerf.Scope("campaign.patch.militia-act-handover-guard", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
            try
            {
                if (!MilitiaActStateHandoverScope.Active) return true;
                if (GameVars.nation == null || state < 0 || state >= GameVars.nation.Length) return true;
                if (newalliance < 0 || newalliance > 1) return true;

                var nation = GameVars.nation[state];
                if (nation == null || nation.alliance == newalliance) return true;
                if (nation.alliance < 0 || nation.alliance > 1) return true;

                float unionSupport = Support(nation, 0);
                float csaSupport = Support(nation, 1);
                if (StateHandoverGuard.AllowsHandover(newalliance, unionSupport, csaSupport))
                    return true;

                OnceLog.Info(
                    "state-handover:blocked",
                    "[Patch:StateHandover] blocked policy-36 state transfer without decisive support");
                return false;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("state-handover:prefix", "[Patch:StateHandover] prefix failed: " + ex.Message);
                return true;
            }
            } // end using TelemetryPerf.Scope
        }

        private static float Support(GameVars.Nation nation, int alliance)
        {
            if (nation.support == null || alliance < 0 || alliance >= nation.support.Length)
                return 0f;
            return nation.support[alliance];
        }
    }
}
