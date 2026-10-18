using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BC16 step 0, record only: what a group order really laid out.
    //
    // Vanilla BattleUnits.SetGroupFormation (decompile 91822-92125) sets the ordered group's groupformation, builds each group's layout
    // bottom-up and then places or paths every regiment three levels down from its offsets: the first two levels at -offset.x (92086, 92126),
    // the third at +offset.x (92176). The Postfix reads the finished call and writes nothing; it never skips vanilla.
    //
    //  * TacticalLayoutMirror (plan decision 9, smoke item 2.42): for every brigade the call laid out, whether its regiments stand in the order of
    //    their layout columns ("agree") or against it ("reversed"), with the ordered group's depth above the regiments. A corps-level order is
    //    depth 3. Positions are GetPosition() after an immediate placement (deployment: SetNewPosition does not write lastsetwaypointposition) and
    //    lastsetwaypointposition after a path (RegimentSetPath writes it for a regiment, 131253-131262). The facing is the group's own
    //    lastsetwaypointrotation, which SetGroupFormation stored after resolving a -1 argument (92067-92068). One row per depth the call touched.
    //  * TacticalFormationFlip (plan decision 2, smoke item 2.37): a group whose groupformation changed twice within 0.1 battle-hours. Only the
    //    ordered group is followed; groups re-formed beneath it by a parent order are a blind spot. An immediate placement (deployment) is only a
    //    baseline.
    //  * [FormationProbe] (plan finding 4): once per battle, at the first order that is not a deployment placement, how many groups still carry
    //    formation 3 (expected: all of them) and how formationordered and groupformation are spread.
    //
    // Known limits, stated in the plan's As built: a regiment whose path array is full keeps its old lastsetwaypointposition (RegimentSetPath
    // returns early, 130972), so one stale regiment can mislabel a brigade; a brigade of single-unit rows says nothing (it is not counted).
    [HarmonyPatch(typeof(BattleUnits), "SetGroupFormation", new[]
    {
        typeof(Regiment), typeof(int), typeof(float), typeof(Vector3), typeof(bool), typeof(bool), typeof(bool),
        typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
    })]
    internal static class LayoutObserverPatch
    {
        // Formations are 0-4; anything else lands in the last bucket of the probe's histograms.
        private const int FormationBuckets = 6;

        private static readonly LayoutMirrorCall Call = new LayoutMirrorCall();
        private static readonly LayoutMirrorBudget MirrorRows = new LayoutMirrorBudget();
        private static readonly FormationFlipDetector Flips = new FormationFlipDetector();
        private static int[] _row = new int[32];
        private static int[] _column = new int[32];
        private static float[] _lateral = new float[32];
        private static bool _probed;

        static LayoutObserverPatch()
        {
            BattleScope.Register("layout-observer", Reset);
        }

        [HarmonyPostfix]
        internal static void Postfix(BattleUnits __instance, Regiment grpreg, bool immediateplacement)
        {
            using (TelemetryPerf.Scope("tactical.patch.layout-observer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.layout-observer");
                try
                {
                    if (!Enabled() || (UnityEngine.Object)__instance == null || __instance.IsCampaign) return;
                    if ((UnityEngine.Object)grpreg == null || grpreg.unittyp <= TacticalUnitType.MaxCombat) return;

                    if (!_probed && !immediateplacement)
                    {
                        _probed = true;
                        ProbeFormationFields();
                    }

                    double hours = TacticalDoctrineTime.NowBattleHours;
                    ObserveFlip(grpreg, immediateplacement, hours);
                    ObserveMirror(grpreg, immediateplacement, hours);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-battle-core:layout-observer-failed",
                        "[LayoutObserver] a group order was not observed: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null && Plugin.Instance.Enabled.Value && Plugin.Instance.EnableTacticalObserver.Value;
        }

        // ---- the flip row ---------------------------------------------------------------------------------------------

        private static void ObserveFlip(Regiment group, bool immediate, double hours)
        {
            int to = group.groupformation;
            bool flip = Flips.Observe(group.gameObject.GetInstanceID(), to, hours, countable: !immediate, out int from, out double since);
            if (flip && Flips.TryTakeRow())
                EmitFlip(group, from, to, since, hours, summary: false);
            else if (Flips.TrySummary(hours))
                EmitFlip(group, to, to, 0.0, hours, summary: true);
        }

        private static void EmitFlip(Regiment group, int from, int to, double since, double hours, bool summary)
        {
            int alliance = group.alliance;
            string unit = "#" + group.gameObject.GetInstanceID();
            int stance = group.ai_stance, ordered = group.formationordered, unittyp = group.unittyp, total = Flips.TotalFlips;
            double sinceRounded = Math.Round(since, 4), hourRounded = Math.Round(hours, 4);
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.State, "TacticalFormationFlip", TelemetrySeverity.Info,
                ev => ev
                    .WithSide(alliance)
                    .WithUnit(unit)
                    .WithField("summary", summary)
                    .WithField("from", from)
                    .WithField("to", to)
                    .WithField("sinceHours", sinceRounded)
                    .WithField("stance", stance)
                    .WithField("ordered", ordered)
                    .WithField("unittyp", unittyp)
                    .WithField("flips", total)
                    .WithField("hour", hourRounded));
        }

        // ---- the mirror row -------------------------------------------------------------------------------------------

        private static void ObserveMirror(Regiment ordered, bool immediate, double hours)
        {
            Call.Clear();
            float facing = ordered.lastsetwaypointrotation;
            Walk(ordered, 0, facing, immediate);
            for (int depth = 1; depth <= LayoutMirrorCall.MaxDepth; depth++)
            {
                if (Call.Brigades(depth) == 0 || !MirrorRows.TryTake(depth)) continue;
                EmitMirror(ordered, depth, immediate, hours);
            }
        }

        // The ordered group is level 0; a brigade at level L has its regiments at level L + 1, which is the depth of the order above them.
        private static void Walk(Regiment node, int level, float facing, bool immediate)
        {
            BattleUnits.GroupPosition layout = node.groupposition;
            if (layout == null || layout.attachedunitreg == null) return;
            if (node.unittyp == TacticalUnitType.BattleGroupBrigade)
            {
                Classify(node, layout, level + 1, facing, immediate);
                return;
            }
            if (level >= LayoutMirrorCall.MaxDepth - 1) return;
            int count = Math.Min(layout.attachedunits, layout.attachedunitreg.Length);
            for (int k = 0; k < count; k++)
            {
                Regiment child = layout.attachedunitreg[k];
                if ((UnityEngine.Object)child == null || child.unittyp <= TacticalUnitType.MaxCombat || child.permanentlydetached || child.isrouted) continue;
                Walk(child, level + 1, facing, immediate);
            }
        }

        private static void Classify(Regiment brigade, BattleUnits.GroupPosition layout, int depth, float facing, bool immediate)
        {
            Regiment[] units = layout.attachedunitreg;
            int[] rows = layout.row, columns = layout.column;
            if (rows == null || columns == null) return;
            int count = Math.Min(Math.Min(layout.attachedunits, units.Length), Math.Min(rows.Length, columns.Length));
            if (count > _row.Length)
            {
                _row = new int[count];
                _column = new int[count];
                _lateral = new float[count];
            }

            int placed = 0;
            for (int k = 0; k < count; k++)
            {
                Regiment unit = units[k];
                // Vanilla places neither a detached nor a routed unit (92101-92108), and a sub-group has no position of its own to compare.
                if ((UnityEngine.Object)unit == null || unit.unittyp > TacticalUnitType.MaxCombat || unit.permanentlydetached || unit.isrouted) continue;
                Vector3 at = immediate ? unit.GetPosition() : unit.lastsetwaypointposition;
                _row[placed] = rows[k];
                _column[placed] = columns[k];
                _lateral[placed] = LayoutMirror.Lateral(at.x, at.z, facing);
                placed++;
            }
            Call.Add(depth, LayoutMirror.Classify(_row, _column, _lateral, placed));
        }

        private static void EmitMirror(Regiment ordered, int depth, bool immediate, double hours)
        {
            int alliance = ordered.alliance;
            string unit = "#" + ordered.gameObject.GetInstanceID();
            int brigades = Call.Brigades(depth), agree = Call.Agree(depth), reversed = Call.Reversed(depth);
            double hourRounded = Math.Round(hours, 4);
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.State, "TacticalLayoutMirror", TelemetrySeverity.Info,
                ev => ev
                    .WithSide(alliance)
                    .WithUnit(unit)
                    .WithField("depth", depth)
                    .WithField("brigades", brigades)
                    .WithField("agree", agree)
                    .WithField("reversed", reversed)
                    .WithField("immediate", immediate)
                    .WithField("hour", hourRounded));
        }

        // ---- the field probe ------------------------------------------------------------------------------------------

        private static void ProbeFormationFields()
        {
            var units = BattleUnits.completeunitlist;
            if (units == null) return;
            int groups = 0, formation3 = 0, formationOther = 0;
            var ordered = new int[FormationBuckets];
            var grouped = new int[FormationBuckets];
            for (int i = 0; i < units.Count; i++)
            {
                Regiment unit = units[i];
                // An AI group: a group the player's own command (permanently detached from the hierarchy) is not.
                if ((UnityEngine.Object)unit == null || unit.unittyp <= TacticalUnitType.MaxCombat || unit.permanentlydetached) continue;
                groups++;
                if (unit.formation == 3) formation3++;
                else formationOther++;
                ordered[Bucket(unit.formationordered)]++;
                grouped[Bucket(unit.groupformation)]++;
            }
            OnceLog.Info("tactical-battle-core:formation-field-probe",
                "[FormationProbe] groups=" + groups + " formation3=" + formation3 + " formationOther=" + formationOther
                + " formationordered(0-4,other)=" + string.Join("/", ordered) + " groupformation(0-4,other)=" + string.Join("/", grouped));
        }

        private static int Bucket(int formation) => formation >= 0 && formation < FormationBuckets - 1 ? formation : FormationBuckets - 1;

        private static void Reset()
        {
            Call.Clear();
            MirrorRows.Reset();
            Flips.Clear();
            _probed = false;
        }
    }
}
