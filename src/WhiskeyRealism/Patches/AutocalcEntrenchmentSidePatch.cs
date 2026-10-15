using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-AI-016. Autocalc.FightUnit (21050), one exchange of fire in an autoresolved battle:
    // num2 is the side of unit1, the unit fired at (21061-21070). When that side defends
    // (usedstance[num2] == 2) incoming fire is scaled by
    // 1 - engineeringpoints[1] / 100 x militaryexperience of unit1's alliance (21075-21077), so
    // a defending side 0 is protected by side 1's - its attacker's - engineering points. Every
    // other per-side read in the method uses num / num2, and the experience term is already the
    // defender's. FightUnit reads engineeringpoints nowhere else (21077 is the only read in
    // Autocalc besides CalculateEngineeringPoints and the save import, 22560-22584, 22938).
    // When the defending side is 0 the Prefix points the field at a two-slot array holding side
    // 0's points for the call (LogisticsSwaps' FieldMask); the Finalizer restores the vanilla
    // array where the field still holds ours, even if vanilla throws. Rollback: Campaign / Fix
    // Autoresolve Entrenchment Side.
    [HarmonyPatch(typeof(Autocalc), "FightUnit")]
    internal static class AutocalcEntrenchmentSidePatch
    {
        private static readonly float[] DefenderPoints = new float[2];
        private static readonly Autocalc[] Target = new Autocalc[1];
        private static readonly FieldMask<Autocalc, float[]> Swap = new FieldMask<Autocalc, float[]>(
            a => a.engineeringpoints, (a, v) => a.engineeringpoints = v, DefenderPoints);

        private static bool _logged;

        static AutocalcEntrenchmentSidePatch()
        {
            CampaignScope.Register("autocalc-entrenchment-side-log", () => _logged = false);
        }

        internal static bool Enabled()
        {
            try { return Plugin.FixAutoresolveEntrenchmentSide != null && Plugin.FixAutoresolveEntrenchmentSide.Value; }
            catch { return false; }
        }

        [HarmonyPrefix]
        internal static void Prefix(Autocalc __instance, Regiment unit0, Regiment unit1)
        {
            using (TelemetryPerf.Scope("campaign.patch.autocalc-entrenchment-side.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.autocalc-entrenchment-side.prefix");
                try
                {
                    LogRestore(Swap.Restore(a => a == null));
                    if ((UnityEngine.Object)unit0 == null || (UnityEngine.Object)unit1 == null || __instance.alliance == null || __instance.alliance.Length < 1) return;
                    int defending = TerrainBugFixes.DefendingSide(unit0.alliance == __instance.alliance[0]);
                    if (!TerrainBugFixes.EntrenchmentReadNeedsFix(Enabled(), defending)) return;
                    float[] points = __instance.engineeringpoints;
                    if (points == null || points.Length < 2 || ReferenceEquals(points, DefenderPoints)) return;
                    DefenderPoints[0] = points[defending];
                    DefenderPoints[1] = points[defending];
                    Target[0] = __instance;
                    Swap.Mask(Target, a => true);
                    Target[0] = null;
                    if (_logged) return;
                    _logged = true;
                    OnceLog.Info("autocalc-entrenchment-side", "[Patch:AutocalcEntrenchment] defending side 0 is protected by its own engineering points");
                }
                catch (Exception ex)
                {
                    LogRestore(Swap.Restore(a => a == null));
                    OnceLog.Warning("autocalc-entrenchment-side:prefix", "[Patch:AutocalcEntrenchment] FightUnit prefix failed, vanilla left as is: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer()
        {
            using (TelemetryPerf.Scope("campaign.patch.autocalc-entrenchment-side.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.autocalc-entrenchment-side.finalizer");
                if (Swap.Active) LogRestore(Swap.Restore(a => a == null));
            }
        }

        private static void LogRestore(string failure)
        {
            if (failure != null)
                OnceLog.Warning("autocalc-entrenchment-side:restore", "[Patch:AutocalcEntrenchment] engineering points restore failed: " + failure);
        }
    }
}
