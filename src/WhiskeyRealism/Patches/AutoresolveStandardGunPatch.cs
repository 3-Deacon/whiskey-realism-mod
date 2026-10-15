using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-BAT-002. Vanilla Autocalc.FightUnit (21050) gives a unit its casualty weapon factor
    // only when `unit0.weapon > 0` (21085, 21089), so a battery armed with weapon id 0 (the
    // 6-pounder Field Gun, every campaign's standard artillery weapon) inflicts no casualties
    // in autoresolved land battles, raids and skirmishes. The sibling GetFortWeaponStrength
    // tests `>= 0` (18482). The test is inline, so this narrow Transpiler
    // (AutoresolveWeaponGateIl, user approval 2026-10-15) replaces the 0 of both tests with a
    // call to WeaponFloor, which returns -1 while the fix is on and 0 while it is off
    // (vanilla), so the flag is a live rollback. If the pattern is not found exactly twice (a
    // game update) FightUnit stays vanilla with one warning. It composes with the perk-id
    // Transpiler on the same method (AutocalcPerkIdsPatch), which edits other instructions.
    // Transpilers carry no TelemetryPerf scope (AGENTS.md); the hook marks first fire.
    // Rollback: Campaign Bug Fixes / Enable Autoresolve Standard Gun Fix.
    [HarmonyPatch(typeof(Autocalc), "FightUnit")]
    public static class AutoresolveStandardGunPatch
    {
        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var vanilla = instructions.ToList();
            try
            {
                var hook = AccessTools.Method(typeof(AutoresolveStandardGunPatch), nameof(WeaponFloor));
                var result = AutoresolveWeaponGateIl.Transpile(vanilla, AutoresolveWeaponGateIl.ResolveWeaponField(), hook, out int matches);
                if (ReferenceEquals(result, vanilla))
                    OnceLog.Warning("arms:autoresolve-gun:pattern", "[Patch:AutoresolveGun] FightUnit weapon test found " + matches + " times (expected " + AutoresolveWeaponGateIl.ExpectedSites + "); left as vanilla");
                else
                    OnceLog.Info("arms:autoresolve-gun:applied", "[Patch:AutoresolveGun] FightUnit now counts weapon id 0 (6-pounder Field Gun) as armed");
                return result;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("arms:autoresolve-gun:transpiler", "[Patch:AutoresolveGun] transpiler failed; FightUnit left as vanilla: " + ex.Message);
                return vanilla;
            }
        }

        // Called from the rewritten vanilla IL in place of the 0 of `weapon > 0`.
        public static int WeaponFloor()
        {
            PatchFirstFire.Mark("campaign.patch.autoresolve-standard-gun.hook");
            return AutoresolveWeaponGateIl.WeaponFloor(Enabled());
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableAutoresolveStandardGunFix != null
                    && Plugin.EnableAutoresolveStandardGunFix.Value;
            }
            catch { return false; }
        }
    }

    // BUG-BAT-002 diagnostic. Vanilla Autocalc.StartLandBattle (22118) builds an autoresolved
    // land battle from both sides' top units. This Postfix counts, once per campaign, the
    // batteries on each side that carry weapon id 0, the ones vanilla's FightUnit leaves
    // without damage. Read-only; sizes the fix in smoke. Raids and skirmishes (StartRaiding
    // 20462, StartSkirmishing 20598) reach FightUnit too but are not counted here.
    [HarmonyPatch(typeof(Autocalc), nameof(Autocalc.StartLandBattle))]
    internal static class AutoresolveStandardGunCountPatch
    {
        private static bool _counted;

        static AutoresolveStandardGunCountPatch()
        {
            CampaignScope.Register("autoresolve-standard-gun-count", () => _counted = false);
        }

        [HarmonyPostfix]
        internal static void Postfix(List<Regiment> envolvedunitsside0, List<Regiment> envolvedunitsside1)
        {
            using (TelemetryPerf.Scope("campaign.patch.autoresolve-standard-gun.count", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.autoresolve-standard-gun.count");
                if (_counted) return;
                _counted = true;
                try
                {
                    string side0 = Describe(envolvedunitsside0);
                    string side1 = Describe(envolvedunitsside1);
                    OnceLog.Info("arms:autoresolve-id0", "[Patch:AutoresolveGun] first autoresolved land battle: side0 " + side0 + "; side1 " + side1);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("arms:autoresolve-id0:count", "[Patch:AutoresolveGun] battery count failed: " + ex.Message);
                }
            }
        }

        // "alliance=A batteries=N weapon0=M" over the side's basic units (top units and every
        // unit attached below them).
        private static string Describe(List<Regiment> topUnits)
        {
            int alliance = -1, batteries = 0, standardGun = 0;
            var seen = new HashSet<Regiment>();
            if (topUnits != null)
            {
                foreach (var top in topUnits)
                {
                    if ((UnityEngine.Object)top == null) continue;
                    alliance = top.alliance;
                    Count(top, seen, ref batteries, ref standardGun);
                    if (top.allattachedunits == null) continue;
                    foreach (var unit in top.allattachedunits)
                        if ((UnityEngine.Object)unit != null) Count(unit, seen, ref batteries, ref standardGun);
                }
            }
            return "alliance=" + alliance + " batteries=" + batteries + " weapon0=" + standardGun;
        }

        private static void Count(Regiment unit, HashSet<Regiment> seen, ref int batteries, ref int standardGun)
        {
            if (unit.unittyp != 2 || !seen.Add(unit)) return;
            batteries++;
            if (unit.weapon == 0) standardGun++;
        }
    }
}
