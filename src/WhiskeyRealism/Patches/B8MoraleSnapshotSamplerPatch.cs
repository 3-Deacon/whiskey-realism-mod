using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.MicroAICheckForCharges(Regiment, int) at decompile line 4905
    // is the existing 5-tick microai cycle. Postfix uses this cadence to sample
    // morale into TacticalMoraleSnapshotLedger so B8 doctrine has a prior-morale
    // comparator. RecordSampleIfNew dedupes by vanilla lastmoraleupdate.
    [HarmonyPatch(typeof(AIBattle), "MicroAICheckForCharges")]
    public static class B8MoraleSnapshotSamplerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Regiment aigroup, int restrictunittypes)
        {
            using (TelemetryPerf.Scope("tactical.patch.b8-morale-snapshot", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (Plugin.MoraleSnapshotLedger == null) return;
                if (aigroup == null) return;

                try
                {
                    OnceLog.Info("b8-morale-snapshot-sampler", "");

                    var allUnits = aigroup.allattachedunits;
                    if (allUnits == null) return;

                    for (int i = 0; i < allUnits.Length; i++)
                    {
                        var unit = allUnits[i];
                        if (unit == null) continue;
                        if (unit.unittyp > TacticalUnitType.MaxCombat) continue;
                        if (unit.unittyp == TacticalUnitType.Excluded) continue;
                        if (unit.permanentlydetached) continue;

                        var key = new TacticalMoraleSnapshotLedger.Key(
                            unit.GetInstanceID(),
                            ((UnityEngine.Object)unit).name);

                        if (unit.isrouted)
                        {
                            Plugin.MoraleSnapshotLedger.PruneRouted(key);
                            continue;
                        }

                        Plugin.MoraleSnapshotLedger.RecordSampleIfNew(
                            key,
                            morale: unit.morale,
                            timeFromStart: GameVars.currenttimefromstart,
                            vanillaLastMoraleUpdate: unit.lastmoraleupdate);
                    }
                }
                catch (System.Exception ex)
                {
                    OnceLog.Warning("b8-morale-snapshot-error",
                        "[B8] morale snapshot sampler error: " + ex.Message);
                }
            }
        }
    }
}
