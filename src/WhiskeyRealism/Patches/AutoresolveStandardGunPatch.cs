using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-BAT-002 diagnostic. Vanilla Autocalc.StartLandBattle (22118) builds an autoresolved
    // land battle from both sides' top units. This Postfix counts, once per session, the
    // batteries on each side that carry weapon id 0 (the 6-pounder Field Gun), which deal no
    // autoresolve damage in vanilla (FightUnit 21085/21089 test weapon > 0). Read-only; sizes
    // the fix in smoke. Raids and skirmishes (StartRaiding 20462, StartSkirmishing 20598)
    // reach FightUnit too but are not counted here.
    [HarmonyPatch(typeof(Autocalc), nameof(Autocalc.StartLandBattle))]
    internal static class AutoresolveStandardGunPatch
    {
        private static bool _counted;

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
