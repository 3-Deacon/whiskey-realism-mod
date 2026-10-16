using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #112, step A: arrival re-path SIZING only.
    //
    // Vanilla BattleUnits.SetInitialPathForEntryPoint (decompile 87967-88003), called from
    // SetGroupArrival (87662) once per queued unit of a reinforcement group, sends the whole
    // group to the entry's trooptargetposition +-20 m in group formation 1 through
    // SetGroupFormation, which re-paths every non-detached, non-routed child (92076-92108).
    // Its only shield is ruleoutmanuallymoved (members with alreadymanuallymoved are
    // detached for the call), and only the player UI writes that field (138854-138859). So
    // orders given to members that already arrived are overwritten each time a later unit
    // of the same group arrives. Whether that reaches Whiskey's own orders in play is not
    // known; this step measures it (plan BC10 decision 10, smoke item 2.21).
    //
    // The Prefix and Finalizer never skip vanilla and write nothing into the battle: the
    // Prefix records, for the arriving group's active members, the last waypoint and
    // (ArrivalShield.Select over the side's latest snapshot) which of them an executor Move
    // is walking; the Finalizer counts the members whose waypoint moved more than
    // OrderArbiter.DedupeMeters and emits one TacticalArrivalRepath Decision row per call
    // (at most MaxRowsPerBattle per battle). Only a side the battle core drives has a
    // snapshot, so only its columns are measured. The snapshot is up to one tick old, so a
    // member that arrived after it is not counted as ordered (sizing, not exact).
    //
    // Step B (the guard that sets alreadymanuallymoved on the ordered members and restores
    // it in the Finalizer) is NOT built: it waits on smoke item 2.21 (plan decision 10).
    // Row fields: group, arrived, queued, repathed, repathedOrdered. Step B adds shielded.
    [HarmonyPatch(typeof(BattleUnits), "SetInitialPathForEntryPoint")]
    internal static class ReinforcementArrivalGuardPatch
    {
        private const int MaxRowsPerBattle = 64;
        private const int MaxOrderedMembers = 256;
        private static int _rows;

        static ReinforcementArrivalGuardPatch()
        {
            BattleScope.Register("arrival-guard", ResetRows);
        }

        // What the Prefix saw; the Finalizer compares against it.
        internal sealed class ArrivalSample
        {
            public int Alliance;
            public int GroupId;
            public Regiment[] Members;
            public int MemberCount;
            public float[] BeforeX, BeforeZ;
            public int[] OrderedIds;
            public int OrderedCount;
            public int Queued;
        }

        [HarmonyPrefix]
        internal static void Prefix(GameObject grp, bool ruleoutmanuallymoved, out ArrivalSample __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.arrival-guard.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.arrival-guard.prefix");
                __state = null;
                try
                {
                    if (!ruleoutmanuallymoved || (UnityEngine.Object)grp == null || !Enabled()) return;
                    __state = Capture(grp);
                }
                catch (Exception ex)
                {
                    __state = null;
                    OnceLog.Warning("tactical-battle-core:arrival-guard-failed",
                        "[BattleArrival] sizing prefix failed, the arrival is not measured: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(ArrivalSample __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.arrival-guard.finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.arrival-guard.finalizer");
                if (__state == null) return;
                try
                {
                    Report(__state);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-battle-core:arrival-guard-failed",
                        "[BattleArrival] sizing finalizer failed, the arrival is not measured: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null && Plugin.Instance.Enabled.Value && Plugin.Instance.EnableTacticalObserver.Value;
        }

        private static ArrivalSample Capture(GameObject grp)
        {
            Regiment top = grp.GetComponent<Regiment>();
            if ((UnityEngine.Object)top == null) return null;
            // Only a side the battle core drives has a snapshot (and executor orders to measure).
            if (!BattleCore.TryGetSnapshot(top.alliance, out BattleSnapshot snapshot)) return null;
            OrderExecutor executor = OrderExecutor.ForAlliance(top.alliance);
            if (executor == null) return null;

            Regiment[] all = grp.GetComponentsInChildren<Regiment>(true);
            var sample = new ArrivalSample
            {
                Alliance = top.alliance,
                GroupId = grp.GetInstanceID(),
                Members = new Regiment[all.Length],
                BeforeX = new float[all.Length],
                BeforeZ = new float[all.Length],
            };
            for (int i = 0; i < all.Length; i++)
            {
                Regiment unit = all[i];
                if ((UnityEngine.Object)unit == null || (UnityEngine.Object)unit == (UnityEngine.Object)top) continue;
                if (!unit.gameObject.activeInHierarchy)
                {
                    sample.Queued++;
                    continue;
                }
                Vector3 waypoint = unit.lastsetwaypointposition;
                sample.Members[sample.MemberCount] = unit;
                sample.BeforeX[sample.MemberCount] = waypoint.x;
                sample.BeforeZ[sample.MemberCount] = waypoint.z;
                sample.MemberCount++;
            }

            int topIndex = snapshot.IndexOfOwn(sample.GroupId);
            if (topIndex >= 0)
            {
                var ordered = new int[Math.Min(MaxOrderedMembers, snapshot.OwnCount)];
                int n = ArrivalShield.Select(snapshot, topIndex, executor, ordered);
                sample.OrderedIds = new int[n];
                for (int k = 0; k < n; k++) sample.OrderedIds[k] = snapshot.Own[ordered[k]].InstanceId;
                sample.OrderedCount = n;
            }
            return sample;
        }

        private static void Report(ArrivalSample sample)
        {
            float limit = OrderArbiter.DedupeMeters;
            int repathed = 0, repathedOrdered = 0;
            for (int i = 0; i < sample.MemberCount; i++)
            {
                Regiment unit = sample.Members[i];
                if ((UnityEngine.Object)unit == null) continue;
                Vector3 after = unit.lastsetwaypointposition;
                float dx = after.x - sample.BeforeX[i];
                float dz = after.z - sample.BeforeZ[i];
                if (dx * dx + dz * dz <= limit * limit) continue;
                repathed++;
                if (sample.OrderedCount > 0 && Array.IndexOf(sample.OrderedIds, unit.gameObject.GetInstanceID()) >= 0) repathedOrdered++;
            }

            // Multi-unit arrivals are the ones the decision needs (a lone unit is only
            // re-pathed by its own arrival); the cap keeps the sidecar bounded.
            if (sample.MemberCount < 2 || _rows >= MaxRowsPerBattle) return;
            _rows++;
            int arrived = sample.MemberCount;
            int queued = sample.Queued;
            int ordered = sample.OrderedCount;
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalArrivalRepath", TelemetrySeverity.Info,
                ev => ev
                    .WithSide(sample.Alliance)
                    .WithUnit("#" + sample.GroupId)
                    .WithDecision("ArrivalRepath", repathedOrdered > 0 ? "ordered-members-repathed" : "no-ordered-member-repathed",
                        sample.Alliance + "|" + sample.GroupId + "|" + arrived + "|" + queued)
                    .WithField("arrived", arrived)
                    .WithField("queued", queued)
                    .WithField("ordered", ordered)
                    .WithField("repathed", repathed)
                    .WithField("repathedOrdered", repathedOrdered));
            if (repathedOrdered > 0)
                OnceLog.Info("tactical-battle-core:arrival-repath-first",
                    "[BattleArrival] an entry re-path overwrote an executor-ordered member: ordered=" + ordered + " repathedOrdered=" + repathedOrdered);
        }

        private static void ResetRows()
        {
            _rows = 0;
        }
    }
}
