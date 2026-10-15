using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Perf M3. Vanilla AICampaign.UpdateUnitAI (DC:11304-11629) starts every pass with
    // `List<Regiment> allunits = BattleUnits.completeunitlist.ToList()` (DC:11310): a new
    // copy of every Regiment under Battlemap/Units, inactive ones included (8 bytes per
    // unit plus the list, every pass; the governor and fast-forward catch-up add passes).
    // allunits is read only by `new AIFaction(k, allunits)` (DC:11361), whose constructor
    // only forwards it to SeparateUnits (DC:10396-10400), and by
    // aifaction[currentfaction].SeparateUnits(allunits) (DC:11382), which only iterates it
    // to fill new lists (DC:10458-10514). Nothing keeps the reference, so one reused list
    // refilled each pass (Clear + AddRange: same units, same order) is equivalent.
    //
    // This narrow Transpiler (AllocationTrimIl) swaps that one ToList call for
    // CompleteUnitListCopy, which takes the same IEnumerable<Regiment>. The hook reads
    // the flag live: off, a null list, or any exception gives Enumerable.ToList itself
    // (vanilla). If the IL pattern is not found exactly once (a game update), the method
    // is left as vanilla and one warning is logged. Transpilers carry no TelemetryPerf
    // scope (AGENTS.md); the hook follows the scope-free hook precedent of #89/#90
    // (TrimLeftOperand, UpperOffset) and marks first fire so a smoke log shows the
    // rewritten IL ran. Both callers of UpdateUnitAI (vanilla AICampaign.Update and the
    // governor/catch-up passes run from its Prefix/Postfix) are sequential on the main
    // thread, so the reused list is never refilled while a pass still reads it.
    // User approval 2026-10-15 (perf review M3, transpiler): "Approve both (Recommended)".
    // Rollback: Performance / Reuse Campaign AI Unit List.
    [HarmonyPatch(typeof(AICampaign), "UpdateUnitAI")]
    public static class CampaignUnitListReusePatch
    {
        private static List<Regiment> _reused;

        static CampaignUnitListReusePatch()
        {
            // Drop the Regiment references between campaigns.
            CampaignScope.Register("campaign-unit-list-reuse", () => _reused = null);
        }

        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var vanilla = instructions.ToList();
            try
            {
                var hook = AccessTools.Method(typeof(CampaignUnitListReusePatch), nameof(CompleteUnitListCopy));
                var result = AllocationTrimIl.TranspileUnitListCopy(vanilla, AllocationTrimIl.ResolveCompleteUnitList(),
                    AllocationTrimIl.ResolveToListOfRegiment(), hook, out int matches);
                if (ReferenceEquals(result, vanilla))
                    OnceLog.Warning("campaign-unit-list-reuse:pattern", "[Patch:UnitListReuse] completeunitlist.ToList() found " + matches + " times (expected 1); UpdateUnitAI left as vanilla");
                else
                    OnceLog.Info("campaign-unit-list-reuse:applied", "[Patch:UnitListReuse] UpdateUnitAI now refills one reused unit list instead of copying completeunitlist each pass");
                return result;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-unit-list-reuse:transpiler", "[Patch:UnitListReuse] transpiler failed; UpdateUnitAI left as vanilla: " + ex.Message);
                return vanilla;
            }
        }

        // Called from the rewritten vanilla IL once per UpdateUnitAI pass, in place of ToList.
        public static List<Regiment> CompleteUnitListCopy(IEnumerable<Regiment> source)
        {
            PatchFirstFire.Mark("campaign.patch.unit-list-reuse.hook");
            if (source != null && Enabled())
            {
                try
                {
                    return AllocationTrimIl.CopyList(_reused ?? (_reused = new List<Regiment>()), source, true);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-unit-list-reuse:copy", "[Patch:UnitListReuse] reused list refill failed; vanilla copy used: " + ex.Message);
                }
            }
            return Enumerable.ToList(source);
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.ReuseCampaignAiUnitList != null
                    && Plugin.ReuseCampaignAiUnitList.Value;
            }
            catch { return false; }
        }
    }
}
