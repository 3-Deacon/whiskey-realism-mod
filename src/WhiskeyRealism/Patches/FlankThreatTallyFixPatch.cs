using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Patch #113 (BUG-TAC-023): the flanksthreated tally. Vanilla Regiment.UpdateGroupStrength (DC:120135) tallies, for every group, how
    // many weight points of flank threat its subordinates carry: inside the loop over allattachedunits
    // (DC:120148, counted subordinates filtered at DC:120284) it adds 1 / 2 / 3 / 4 points by `outflanked`
    // (DC:120566-120580), but reads the GROUP's own `outflanked` field, not the subordinate's
    // (array[m].outflanked). A group's own code is the headquarters', which stands behind its line, so the
    // tally is usually 0. Its only live reader, GetGroupStrength(includeflanksthreated) (DC:6047-6050,
    // strength / (1 + flanksthreated / flankinginfluenceonaidecisions)), feeds CheckFlankMoves (6269-6270),
    // the flank-unit cancel (5962) and CalculateFlankData (6518, 6547 -> AssignReserves 7059-7087).
    //
    // The Postfix sets flanksthreated to the sum, over the subordinates vanilla's loop counted, of the
    // weight of each subordinate's own outflanked code (FlankThreatTally), only when vanilla reached its
    // loop (the call is not the early return of DC:120140-120143: no battle UI, the selected unit is this
    // group, or overridecalc). A result correction of a vanilla field: no Prefix, no Transpiler, not an
    // order, so it does not go through the battle-core executor (precedent: BattleHqConditionFixPatch).
    //
    // Effect (recorded, not hidden): an inner line group whose regiments are outflanked reads weaker, so
    // CheckFlankMoves' "inner neighbour below 3" test passes more often and VANILLA launches more
    // FlankMoves (lastaiorder 4 / 5), on a Defender side too (CheckFlankMoves returns early only for
    // macroai < 0 or 3, DC:6253). These are vanilla's own counter-flank moves; Whiskey's own paths keep
    // the Defender rule. Vanilla's AI stays omniscient here (it reads its own range lists).
    // Measurement: UnitRecord.LastAiOrder and the TacticalVanillaFlankMove rows (BattleCoreTick).
    // Rollback: Tactical / Enable Vanilla Flank Threat Tally Fix.
    [HarmonyPatch(typeof(Regiment), "UpdateGroupStrength")]
    internal static class FlankThreatTallyFixPatch
    {
        // The first-correction message is formatted once per battle, not per call.
        private static bool _logged;
        private static Func<object, BattleUI> _bui;
        private static bool _buiResolved;

        static FlankThreatTallyFixPatch()
        {
            BattleScope.Register("flank-threat-tally-log", () => _logged = false);
        }

        [HarmonyPostfix]
        internal static void Postfix(Regiment __instance, bool overridecalc, bool ignoreactivestate, Regiment ignoreactivestateonlyforthisunit)
        {
            using (TelemetryPerf.Scope("tactical.patch.flank-threat-tally", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.flank-threat-tally");
                try
                {
                    if (!Enabled() || (UnityEngine.Object)__instance == null) return;
                    // Battle groups only: the campaign map runs the same method for its own groups.
                    if (__instance.calledfromcampaign || __instance.unittyp <= 13) return;

                    // Without the compiled getter the early return cannot be told apart: keep vanilla's tally.
                    if (!BuiReadable()) return;
                    BattleUI bui = _bui(__instance);
                    bool buiMissing = (UnityEngine.Object)bui == null;
                    bool selectedIsThis = !buiMissing && (UnityEngine.Object)bui.selectedunit == (UnityEngine.Object)__instance.gameObject;
                    if (!FlankThreatTally.VanillaReachedLoop(buiMissing, selectedIsThis, overridecalc)) return;

                    Regiment[] units = __instance.allattachedunits;
                    if (units == null) return;
                    // `flag` of the loop: the single exception only applies when it is this group and it is inactive.
                    bool exceptionIsThisAndInactive = (UnityEngine.Object)ignoreactivestateonlyforthisunit == (UnityEngine.Object)__instance
                        && !ignoreactivestateonlyforthisunit.gameObject.activeInHierarchy;
                    int points = 0;
                    for (int i = 0; i < units.Length; i++)
                    {
                        Regiment unit = units[i];
                        if ((UnityEngine.Object)unit == null) continue;
                        if (!FlankThreatTally.Counts(unit.unittyp, unit.gameObject.activeInHierarchy, ignoreactivestate, exceptionIsThisAndInactive, unit.surrenderstate > 0))
                            continue;
                        points += FlankThreatTally.Weight(unit.outflanked);
                    }

                    float vanilla = __instance.flanksthreated;
                    if (vanilla == points) return;
                    __instance.flanksthreated = points;
                    if (_logged) return;
                    _logged = true;
                    OnceLog.Info("tactical-flank-threat:first-correction",
                        "[FlankThreatTally] group flanks threatened re-tallied per subordinate: vanilla=" + vanilla + " corrected=" + points);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-flank-threat:failed", "[FlankThreatTally] UpdateGroupStrength postfix failed, vanilla tally kept: " + ex.Message);
                }
            }
        }

        // Regiment.bui is private (DC:111450); read through a compiled getter, resolved once.
        private static bool BuiReadable()
        {
            if (!_buiResolved)
            {
                _buiResolved = true;
                FieldInfo field = AccessTools.Field(typeof(Regiment), "bui");
                _bui = CompiledFieldGetter.Build<BattleUI>(field);
                if (_bui == null)
                    OnceLog.Warning("tactical-flank-threat:bui-missing", "[FlankThreatTally] Regiment.bui not found; vanilla tally kept");
            }
            return _bui != null;
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableVanillaFlankThreatTallyFix != null && Plugin.EnableVanillaFlankThreatTallyFix.Value;
            }
            catch { return false; }
        }
    }
}
