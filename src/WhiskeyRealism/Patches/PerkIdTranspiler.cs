using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Shared body of the perk-id transpilers (no [HarmonyPatch]): edit a clone of vanilla's IL
    // with one PerkIdIlRewrites function; on a pattern miss or any exception log once and hand
    // vanilla's IL back unchanged.
    internal static class PerkIdTranspiler
    {
        internal static IEnumerable<CodeInstruction> Apply(
            string target,
            IEnumerable<CodeInstruction> instructions,
            Func<List<CodeInstruction>, bool> rewrite)
        {
            var original = new List<CodeInstruction>(instructions);
            try
            {
                var code = original.ConvertAll(i => new CodeInstruction(i));
                if (rewrite(code))
                {
                    OnceLog.Info("perk-ids:il:" + target, "[Patch:PerkIds] " + target + ": perk-id sites rewritten");
                    return code;
                }
                OnceLog.Warning("perk-ids:il-miss:" + target, "[Patch:PerkIds] " + target + ": IL pattern not found (game update?); vanilla left as is");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("perk-ids:il-error:" + target, "[Patch:PerkIds] " + target + ": transpiler failed, vanilla left as is: " + ex.Message);
            }
            return original;
        }

        internal static MethodInfo Hook(string name)
        {
            return AccessTools.Method(typeof(PerkIdIlHooks), name);
        }
    }
}
