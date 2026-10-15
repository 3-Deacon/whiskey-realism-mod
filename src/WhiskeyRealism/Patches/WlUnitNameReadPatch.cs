using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Perf (review §5, F3). Vanilla BattleUnits.IsUnitUnderCommander (DC:91168-91222), past
    // its W&L early return, does `_ = ((Object)unit).name == "1st Delaware Sharpshooters"`
    // (DC:91186): a native name read that allocates a string, compared and discarded.
    // It runs per descendant from UpdateGroupStrength (DC:120281) and per unit from the
    // campaign slow cycle (DC:112240), about 90-100 calls per frame in a W&L career. The
    // read cannot throw where it sits: the Unity null check (DC:91178, true for a
    // destroyed object) returns first, and every caller is main-thread (LoadBattle,
    // Load, AllocateUnitsAndObjectivesToAI, ChangeStance, UpdateUnitUnderCommanderAll,
    // Regiment.Update/Initialize, UpdateGroupStrength; the one thread-pool job,
    // UpdateUnitRangeFast DC:112191, never calls it), so skipping it changes no result.
    //
    // This narrow Transpiler (AllocationTrimIl) replaces `callvirt Object::get_name;
    // ldstr; call string::op_Equality; pop` with one call to UnitNameRead, which takes
    // the unit already on the stack (the ldarg before it keeps its branch label). The hook
    // reads the flag live: off, it repeats vanilla's read and compare. If the IL pattern is
    // not found exactly once (a game update), the method is left as vanilla and one warning
    // is logged. Transpilers carry no TelemetryPerf scope (AGENTS.md); the hook follows the
    // scope-free hook precedent of #89/#90 and marks first fire.
    // User approval 2026-10-15 (perf review, transpiler): "Approve both (Recommended)".
    // Rollback: Performance / Skip Dead W&L Unit Name Read.
    [HarmonyPatch(typeof(BattleUnits), "IsUnitUnderCommander")]
    public static class WlUnitNameReadPatch
    {
        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var vanilla = instructions.ToList();
            try
            {
                var hook = AccessTools.Method(typeof(WlUnitNameReadPatch), nameof(UnitNameRead));
                var result = AllocationTrimIl.TranspileNameRead(vanilla, AllocationTrimIl.ResolveObjectNameGetter(),
                    AllocationTrimIl.ResolveStringEquality(), hook, out int matches);
                if (ReferenceEquals(result, vanilla))
                    OnceLog.Warning("wl-unit-name-read:pattern", "[Patch:WlUnitNameRead] dead name read found " + matches + " times (expected 1); IsUnitUnderCommander left as vanilla");
                else
                    OnceLog.Info("wl-unit-name-read:applied", "[Patch:WlUnitNameRead] IsUnitUnderCommander no longer reads the unit name");
                return result;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-unit-name-read:transpiler", "[Patch:WlUnitNameRead] transpiler failed; IsUnitUnderCommander left as vanilla: " + ex.Message);
                return vanilla;
            }
        }

        // Called from the rewritten vanilla IL in place of the dead name compare.
        public static void UnitNameRead(UnityEngine.Object unit)
        {
            PatchFirstFire.Mark("campaign.patch.wl-unit-name-read.hook");
            if (!Enabled())
                _ = unit.name == AllocationTrimIl.DeadNameLiteral;
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.SkipDeadWlUnitNameRead != null
                    && Plugin.SkipDeadWlUnitNameRead.Value;
            }
            catch { return false; }
        }
    }
}
