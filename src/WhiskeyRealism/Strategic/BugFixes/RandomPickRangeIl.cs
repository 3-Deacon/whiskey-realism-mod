using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WhiskeyRealism.Strategic.BugFixes
{
    // BUG-LOG-011. UnityEngine.Random.Range(int, int) excludes its upper bound, but these
    // AICampaign candidate picks pass list.Count - 1, so the last candidate is never
    // chosen (with two candidates the first always wins). Vanilla's other picks in the
    // same class use Range(0, Count) (RaidForce 9070, 9174), and every list here is a
    // plain or weighted candidate list (weights by repetition at 12235-12238, and in
    // GrabOwnTopUnitsInArea 12890-12895 that feeds 13040), so the intent is a pick over
    // all of it. The bound is an inline
    // expression, so the fix is a narrow IL rewrite: the "ldc.i4.1" of "Count - 1" is
    // replaced by a hook call returning 0 (fix on) or 1 (fix off: vanilla).
    //
    // Matched IL per site: callvirt List`1::get_Count(); ldc.i4.1; sub;
    //                      call UnityEngine.Random::Range(int32, int32)
    internal static class RandomPickRangeIl
    {
        internal struct Site
        {
            internal string Method;
            internal int Expected;
            internal string Anchor;
        }

        // Decompile anchors (build 14703111).
        internal static readonly Site[] Sites =
        {
            new Site { Method = "RaiseFleet", Expected = 1, Anchor = "12146 port to raise a new fleet at" },
            new Site { Method = "GrabFleet", Expected = 1, Anchor = "12241 fleet weighted by free ship slots" },
            new Site { Method = "GrabShipType", Expected = 1, Anchor = "12302 ship type to build" },
            new Site { Method = "GrabExistingGroup", Expected = 2, Anchor = "12793 division / 12801 corps for a recruit" },
            new Site { Method = "GetClosestIIPPositionOfZone", Expected = 1, Anchor = "12928 IIP of a recruiting zone" },
            new Site { Method = "RaiseNewCampaignGroup", Expected = 1, Anchor = "13040 army weighted for a new group" },
            new Site { Method = "CheckCreationOfRaidForces", Expected = 1, Anchor = "17611 frontline town for a raid force" },
        };

        internal static int UpperOffset(bool fixEnabled)
        {
            return fixEnabled ? 0 : 1;
        }

        internal static MethodInfo ResolveRandomRangeInt()
        {
            var random = AccessTools.TypeByName("UnityEngine.Random");
            return random == null ? null : AccessTools.Method(random, "Range", new[] { typeof(int), typeof(int) });
        }

        // Returns the number of matching sites. The code is changed only when exactly
        // `expected` sites match; any other count leaves it untouched (fail safe).
        internal static int Rewrite(List<CodeInstruction> code, MethodInfo randomRangeInt, MethodInfo upperOffsetHook, int expected)
        {
            if (code == null || randomRangeInt == null || upperOffsetHook == null) return 0;
            var sites = new List<int>();
            for (int i = 0; i + 3 < code.Count; i++)
            {
                if (IsCountGetter(code[i])
                    && code[i + 1].opcode == OpCodes.Ldc_I4_1
                    && code[i + 2].opcode == OpCodes.Sub
                    && code[i + 3].Calls(randomRangeInt))
                {
                    sites.Add(i + 1);
                }
            }
            if (sites.Count != expected) return sites.Count;
            foreach (int at in sites)
            {
                code[at].opcode = OpCodes.Call;
                code[at].operand = upperOffsetHook;
            }
            return sites.Count;
        }

        private static bool IsCountGetter(CodeInstruction ci)
        {
            return (ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call)
                && ci.operand is MethodInfo m
                && m.Name == "get_Count";
        }
    }
}
