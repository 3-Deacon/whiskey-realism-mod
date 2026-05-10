using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.CheckUseOfReserves directly issues RegimentSetPath for a
    // reserve support unit at decompile line 6170. This patch snapshots
    // attached-unit path state before vanilla, then rolls back only newly-added
    // paths when the command-node role says the calling command group should
    // remain in Reserve.
    [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
    internal static class BattleReserveCommitGatePatch
    {
        private static FieldInfo _lastDrawnPathCornerField;
        private static FieldInfo _firstWpAdjustmentMadeField;
        private static bool _lastDrawnPathCornerFieldMissing;
        private static bool _firstWpAdjustmentMadeFieldMissing;

        internal sealed class ReserveCommitState
        {
            public UnitState[] Units = Array.Empty<UnitState>();
        }

        internal readonly struct UnitState
        {
            public UnitState(
                Regiment unit,
                int paths,
                int queueCount,
                bool doubleQuick,
                bool inEngagement,
                int pathsBeforeRotation,
                Regiment unitWhoGaveLastMovingOrder,
                float coverValue,
                int coverObject,
                float coverValueTemp,
                int coverObjectTemp,
                Vector3[] lastWaypointPosition,
                float[] lastWaypointRotation,
                Vector3 lastSetWaypointPosition,
                float lastSetWaypointRotation,
                float lastMovingUpdate,
                bool lastPathSetOutOfSafetyZone,
                bool lastPathSetOutOfManualMove,
                int priorMountingState,
                bool hasLastDrawnPathCorner,
                int lastDrawnPathCorner,
                bool hasFirstWpAdjustmentMade,
                bool firstWpAdjustmentMade)
            {
                Unit = unit;
                Paths = Math.Max(0, paths);
                QueueCount = Math.Max(0, queueCount);
                DoubleQuick = doubleQuick;
                InEngagement = inEngagement;
                PathsBeforeRotation = Math.Max(0, pathsBeforeRotation);
                UnitWhoGaveLastMovingOrder = unitWhoGaveLastMovingOrder;
                CoverValue = coverValue;
                CoverObject = coverObject;
                CoverValueTemp = coverValueTemp;
                CoverObjectTemp = coverObjectTemp;
                LastWaypointPosition = Clone(lastWaypointPosition);
                LastWaypointRotation = Clone(lastWaypointRotation);
                LastSetWaypointPosition = lastSetWaypointPosition;
                LastSetWaypointRotation = lastSetWaypointRotation;
                LastMovingUpdate = lastMovingUpdate;
                LastPathSetOutOfSafetyZone = lastPathSetOutOfSafetyZone;
                LastPathSetOutOfManualMove = lastPathSetOutOfManualMove;
                PriorMountingState = priorMountingState;
                HasLastDrawnPathCorner = hasLastDrawnPathCorner;
                LastDrawnPathCorner = lastDrawnPathCorner;
                HasFirstWpAdjustmentMade = hasFirstWpAdjustmentMade;
                FirstWpAdjustmentMade = firstWpAdjustmentMade;
            }

            public Regiment Unit { get; }
            public int Paths { get; }
            public int QueueCount { get; }
            public bool DoubleQuick { get; }
            public bool InEngagement { get; }
            public int PathsBeforeRotation { get; }
            public Regiment UnitWhoGaveLastMovingOrder { get; }
            public float CoverValue { get; }
            public int CoverObject { get; }
            public float CoverValueTemp { get; }
            public int CoverObjectTemp { get; }
            public Vector3[] LastWaypointPosition { get; }
            public float[] LastWaypointRotation { get; }
            public Vector3 LastSetWaypointPosition { get; }
            public float LastSetWaypointRotation { get; }
            public float LastMovingUpdate { get; }
            public bool LastPathSetOutOfSafetyZone { get; }
            public bool LastPathSetOutOfManualMove { get; }
            public int PriorMountingState { get; }
            public bool HasLastDrawnPathCorner { get; }
            public int LastDrawnPathCorner { get; }
            public bool HasFirstWpAdjustmentMade { get; }
            public bool FirstWpAdjustmentMade { get; }

            private static Vector3[] Clone(Vector3[] source)
            {
                if (source == null) return null;
                var clone = new Vector3[source.Length];
                Array.Copy(source, clone, source.Length);
                return clone;
            }

            private static float[] Clone(float[] source)
            {
                if (source == null) return null;
                var clone = new float[source.Length];
                Array.Copy(source, clone, source.Length);
                return clone;
            }
        }

        [HarmonyPrefix]
        internal static void Prefix(Regiment aigroup, out ReserveCommitState __state)
        {
            __state = null;
            try
            {
                if (Enabled())
                    __state = Snapshot(aigroup);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:prefix",
                    "[TacticalReserveCommitGate] Prefix failed; vanilla reserve movement remains active: " + ex.Message);
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        internal static void Postfix(Regiment aigroup, ReserveCommitState __state)
        {
            try
            {
                if (!Enabled()) return;
                if (__state == null || __state.Units == null || aigroup == null) return;

                UnitState[] changed = FindChangedUnits(__state);
                if (changed.Length == 0)
                {
                    Log(aigroup, TacticalReserveCommitGate.Action.Observe, DirectChildRole.Unknown, "no-vanilla-commit", 0);
                    return;
                }

                CommandIntentResolution resolution = ResolveIntent(aigroup);
                var input = new TacticalReserveCommitGate.Input(
                    vanillaCommitted: true,
                    resolution: resolution,
                    playerControlled: HasPlayerOwnership(aigroup),
                    committedUnitAlreadyEngaged: AnyAlreadyEngaged(changed),
                    ownStrengthRatio: OwnStrengthRatio(aigroup),
                    localOdds: LocalOdds(aigroup));

                TacticalReserveCommitGate.Decision decision = TacticalReserveCommitGate.Decide(input);
                if (decision.Action == TacticalReserveCommitGate.Action.Deny)
                    RollBackChangedUnits(changed);

                Log(aigroup, decision.Action, decision.Role, decision.Reason, changed.Length);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:postfix",
                    "[TacticalReserveCommitGate] Postfix failed; vanilla reserve movement remains active: " + ex.Message);
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null
                    && Plugin.Instance.Enabled != null
                    && Plugin.Instance.Enabled.Value
                    && Plugin.EnableTacticalBattleOrchestrator != null
                    && Plugin.EnableTacticalBattleOrchestrator.Value
                    && Plugin.Instance.EnableTacticalOrchestratorReserveCommitGate != null
                    && Plugin.Instance.EnableTacticalOrchestratorReserveCommitGate.Value;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:enabled",
                    "[TacticalReserveCommitGate] Enable check failed; patch disabled: " + ex.Message);
                return false;
            }
        }

        private static ReserveCommitState Snapshot(Regiment group)
        {
            try
            {
                if (group == null || group.allattachedunits == null)
                    return new ReserveCommitState();

                UnitState[] units = new UnitState[group.allattachedunits.Length];
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    units[i] = SnapshotUnit(unit);
                }

                return new ReserveCommitState { Units = units };
            }
            catch
            {
                return new ReserveCommitState();
            }
        }

        private static UnitState SnapshotUnit(Regiment unit)
        {
            if (unit == null)
                return new UnitState(null, 0, 0, false, false, 0, null, 0f, 0, 0f, 0, null, null, default(Vector3), 0f, 0f, false, false, 0, false, 0, false, false);

            int lastDrawnPathCorner = 0;
            bool hasLastDrawnPathCorner = TryGetPrivateInt(unit, ref _lastDrawnPathCornerField, "lastdrawnpathcorner", ref _lastDrawnPathCornerFieldMissing, out lastDrawnPathCorner);
            bool firstWpAdjustmentMade = false;
            bool hasFirstWpAdjustmentMade = TryGetPrivateBool(unit, ref _firstWpAdjustmentMadeField, "firstwpadjustmentmade", ref _firstWpAdjustmentMadeFieldMissing, out firstWpAdjustmentMade);

            return new UnitState(
                unit,
                SafePathCount(unit),
                SafeQueueCount(unit),
                SafeDoubleQuick(unit),
                SafeInEngagement(unit),
                SafeInt(() => unit.regimentpathsbeforerotation),
                SafeRegiment(() => unit.unitwhogavelastmovingorder),
                SafeFloat(() => unit.covervalue),
                SafeInt(() => unit.coverobject),
                SafeFloat(() => unit.covervaluetemp),
                SafeInt(() => unit.coverobjecttemp),
                SafeVectorArray(() => unit.lastwaypointposition),
                SafeFloatArray(() => unit.lastwaypointrotation),
                SafeVector(() => unit.lastsetwaypointposition),
                SafeFloat(() => unit.lastsetwaypointrotation),
                SafeFloat(() => unit.lastmovingupdate),
                SafeBool(() => unit.lastpathsetoutofsafetyzone),
                SafeBool(() => unit.lastpathsetoutofmanualmove),
                SafeInt(() => unit.priormountingstate),
                hasLastDrawnPathCorner,
                lastDrawnPathCorner,
                hasFirstWpAdjustmentMade,
                firstWpAdjustmentMade);
        }

        private static UnitState[] FindChangedUnits(ReserveCommitState state)
        {
            var changed = new List<UnitState>();
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitState before = state.Units[i];
                Regiment unit = before.Unit;
                if (unit == null) continue;

                int afterPaths = SafePathCount(unit);
                int afterQueue = SafeQueueCount(unit);
                if (afterPaths > before.Paths && afterQueue <= before.QueueCount)
                    changed.Add(before);
            }

            return changed.ToArray();
        }

        private static void RollBackChangedUnits(UnitState[] changed)
        {
            for (int i = 0; i < changed.Length; i++)
            {
                UnitState before = changed[i];
                Regiment unit = before.Unit;
                if (unit == null) continue;

                RemoveAddedPaths(unit, before.Paths, SafePathCount(unit));
                RestoreMovementState(unit, before);
            }
        }

        private static void RestoreMovementState(Regiment unit, UnitState before)
        {
            try { unit.regimentpathsbeforerotation = before.PathsBeforeRotation; } catch { }
            try { unit.unitwhogavelastmovingorder = before.UnitWhoGaveLastMovingOrder; } catch { }
            try { unit.covervalue = before.CoverValue; } catch { }
            try { unit.coverobject = before.CoverObject; } catch { }
            try { unit.covervaluetemp = before.CoverValueTemp; } catch { }
            try { unit.coverobjecttemp = before.CoverObjectTemp; } catch { }
            try { RestoreVectorArray(ref unit.lastwaypointposition, before.LastWaypointPosition); } catch { }
            try { RestoreFloatArray(ref unit.lastwaypointrotation, before.LastWaypointRotation); } catch { }
            try { unit.lastsetwaypointposition = before.LastSetWaypointPosition; } catch { }
            try { unit.lastsetwaypointrotation = before.LastSetWaypointRotation; } catch { }
            try { unit.lastmovingupdate = before.LastMovingUpdate; } catch { }
            try { unit.lastpathsetoutofsafetyzone = before.LastPathSetOutOfSafetyZone; } catch { }
            try { unit.lastpathsetoutofmanualmove = before.LastPathSetOutOfManualMove; } catch { }
            try { unit.priormountingstate = before.PriorMountingState; } catch { }
            try { unit.doublequick = before.DoubleQuick; } catch { }
            if (before.HasLastDrawnPathCorner)
                TrySetPrivateInt(unit, ref _lastDrawnPathCornerField, "lastdrawnpathcorner", ref _lastDrawnPathCornerFieldMissing, before.LastDrawnPathCorner);
            if (before.HasFirstWpAdjustmentMade)
                TrySetPrivateBool(unit, ref _firstWpAdjustmentMadeField, "firstwpadjustmentmade", ref _firstWpAdjustmentMadeFieldMissing, before.FirstWpAdjustmentMade);
        }

        private static void RestoreVectorArray(ref Vector3[] target, Vector3[] snapshot)
        {
            if (snapshot == null)
            {
                target = null;
                return;
            }

            if (target == null || target.Length != snapshot.Length)
                target = new Vector3[snapshot.Length];

            int max = Math.Min(target.Length, snapshot.Length);
            for (int i = 0; i < max; i++)
                target[i] = snapshot[i];
        }

        private static void RestoreFloatArray(ref float[] target, float[] snapshot)
        {
            if (snapshot == null)
            {
                target = null;
                return;
            }

            if (target == null || target.Length != snapshot.Length)
                target = new float[snapshot.Length];

            int max = Math.Min(target.Length, snapshot.Length);
            for (int i = 0; i < max; i++)
                target[i] = snapshot[i];
        }

        private static CommandIntentResolution ResolveIntent(Regiment group)
        {
            try
            {
                if (group == null)
                    return new CommandIntentResolution(false, default, "no-group");

                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                if (side == null || side.Army == null)
                    return new CommandIntentResolution(false, default, "no-side-orchestrator");

                return side.Army.ResolveCommandIntentForGroup(group.GetInstanceID());
            }
            catch (Exception ex)
            {
                return new CommandIntentResolution(false, default, "resolve-error:" + ex.GetType().Name);
            }
        }

        private static bool HasPlayerOwnership(Regiment group)
        {
            try
            {
                if (group == null) return true;
                if (!SafeAiVsAi() && group.alliance == SafePlayerAlliance()) return true;
                if (group.dlcw_isundercommander) return true;
                if (group.allattachedunits == null) return false;

                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit != null && unit.dlcw_isundercommander) return true;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool AnyAlreadyEngaged(UnitState[] changed)
        {
            for (int i = 0; i < changed.Length; i++)
            {
                Regiment unit = changed[i].Unit;
                if (unit != null && (changed[i].InEngagement || SafeInEngagement(unit)))
                    return true;
            }

            return false;
        }

        private static float OwnStrengthRatio(Regiment group)
        {
            try
            {
                if (group == null) return 1f;
                float active = Sanitize(group.groupstrengthactive);
                float total = Math.Max(1f, Sanitize(group.groupstrength));
                return active / total;
            }
            catch
            {
                return 1f;
            }
        }

        private static float LocalOdds(Regiment group)
        {
            try
            {
                if (group == null) return 1f;
                float own = Math.Max(Sanitize(group.groupowninrange), Sanitize(group.groupstrengthaigroup));
                float enemy = Math.Max(Sanitize(group.groupenemiesinrange), SumEnemyStrengthWithinAngle(group));
                return enemy <= 0f ? 1f : own / Math.Max(1f, enemy);
            }
            catch
            {
                return 1f;
            }
        }

        private static float SumEnemyStrengthWithinAngle(Regiment group)
        {
            try
            {
                if (group == null || group.unitrange == null || group.unitrange.enemystrengthwithinangle == null)
                    return 0f;

                float total = 0f;
                for (int i = 0; i < group.unitrange.enemystrengthwithinangle.Length; i++)
                    total += Math.Max(0f, group.unitrange.enemystrengthwithinangle[i]);
                return total;
            }
            catch
            {
                return 0f;
            }
        }

        private static void RemoveAddedPaths(Regiment unit, int before, int after)
        {
            int safeBefore = Math.Max(0, before);
            int safeAfter = Math.Max(safeBefore, after);
            if (unit.regimentpath != null)
            {
                int max = Math.Min(safeAfter, unit.regimentpath.Length);
                for (int i = safeBefore; i < max; i++)
                    unit.regimentpath[i] = new NavMeshPath();
            }

            if (unit.pathstatus != null)
            {
                int max = Math.Min(safeAfter, unit.pathstatus.Length);
                for (int i = safeBefore; i < max; i++)
                    unit.pathstatus[i] = 0;
            }

            unit.regimentpaths = safeBefore;
        }

        private static int SafePathCount(Regiment unit)
        {
            try { return unit != null ? Math.Max(0, unit.regimentpaths) : 0; }
            catch { return 0; }
        }

        private static int SafeQueueCount(Regiment unit)
        {
            try { return unit != null && unit.orderqueue != null ? unit.orderqueue.Count : 0; }
            catch { return 0; }
        }

        private static bool SafeDoubleQuick(Regiment unit)
        {
            try { return unit != null && unit.doublequick; }
            catch { return false; }
        }

        private static bool SafeInEngagement(Regiment unit)
        {
            try { return unit != null && unit.inengagement; }
            catch { return false; }
        }

        private static int SafePlayerAlliance()
        {
            try { return GameVars.playeralliance; }
            catch { return -99; }
        }

        private static bool SafeAiVsAi()
        {
            try { return GameVars.ai_vs_ai; }
            catch { return false; }
        }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }

        private static int SafeInt(Func<int> read)
        {
            try { return read != null ? read() : 0; }
            catch { return 0; }
        }

        private static float SafeFloat(Func<float> read)
        {
            try { return read != null ? read() : 0f; }
            catch { return 0f; }
        }

        private static bool SafeBool(Func<bool> read)
        {
            try { return read != null && read(); }
            catch { return false; }
        }

        private static Regiment SafeRegiment(Func<Regiment> read)
        {
            try { return read != null ? read() : null; }
            catch { return null; }
        }

        private static Vector3 SafeVector(Func<Vector3> read)
        {
            try { return read != null ? read() : default(Vector3); }
            catch { return default(Vector3); }
        }

        private static Vector3[] SafeVectorArray(Func<Vector3[]> read)
        {
            try { return read != null ? read() : null; }
            catch { return null; }
        }

        private static float[] SafeFloatArray(Func<float[]> read)
        {
            try { return read != null ? read() : null; }
            catch { return null; }
        }

        private static bool TryGetPrivateInt(
            Regiment unit,
            ref FieldInfo field,
            string fieldName,
            ref bool missingLogged,
            out int value)
        {
            value = 0;
            try
            {
                field = ResolvePrivateField(ref field, fieldName, ref missingLogged);
                if (field == null) return false;
                object raw = field.GetValue(unit);
                if (raw is int intValue)
                {
                    value = intValue;
                    return true;
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:read-private:" + fieldName,
                    "[TacticalReserveCommitGate] failed reading Regiment." + fieldName + ": " + ex.Message);
            }

            return false;
        }

        private static bool TryGetPrivateBool(
            Regiment unit,
            ref FieldInfo field,
            string fieldName,
            ref bool missingLogged,
            out bool value)
        {
            value = false;
            try
            {
                field = ResolvePrivateField(ref field, fieldName, ref missingLogged);
                if (field == null) return false;
                object raw = field.GetValue(unit);
                if (raw is bool boolValue)
                {
                    value = boolValue;
                    return true;
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:read-private:" + fieldName,
                    "[TacticalReserveCommitGate] failed reading Regiment." + fieldName + ": " + ex.Message);
            }

            return false;
        }

        private static void TrySetPrivateInt(
            Regiment unit,
            ref FieldInfo field,
            string fieldName,
            ref bool missingLogged,
            int value)
        {
            try
            {
                field = ResolvePrivateField(ref field, fieldName, ref missingLogged);
                if (field != null) field.SetValue(unit, value);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:write-private:" + fieldName,
                    "[TacticalReserveCommitGate] failed restoring Regiment." + fieldName + ": " + ex.Message);
            }
        }

        private static void TrySetPrivateBool(
            Regiment unit,
            ref FieldInfo field,
            string fieldName,
            ref bool missingLogged,
            bool value)
        {
            try
            {
                field = ResolvePrivateField(ref field, fieldName, ref missingLogged);
                if (field != null) field.SetValue(unit, value);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:write-private:" + fieldName,
                    "[TacticalReserveCommitGate] failed restoring Regiment." + fieldName + ": " + ex.Message);
            }
        }

        private static FieldInfo ResolvePrivateField(ref FieldInfo field, string fieldName, ref bool missingLogged)
        {
            if (field != null) return field;
            if (missingLogged) return null;

            field = AccessTools.Field(typeof(Regiment), fieldName);
            if (field == null)
            {
                missingLogged = true;
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:missing-private:" + fieldName,
                    "[TacticalReserveCommitGate] missing Regiment." + fieldName + " anchor; restore will skip that field.");
            }

            return field;
        }

        private static void Log(
            Regiment group,
            TacticalReserveCommitGate.Action action,
            DirectChildRole role,
            string reason,
            int changedUnits)
        {
            try
            {
                string groupName = SafeGroupName(group);
                string safeReason = TacticalCurrentOrderSignature.Safe(reason);
                string key = "tactical-reserve-commit-gate:"
                    + (group != null ? group.GetInstanceID().ToString() : "null")
                    + ":" + action
                    + ":" + role
                    + ":" + safeReason
                    + ":" + Math.Max(0, changedUnits);
                OnceLog.Info(
                    key,
                    "[TacticalReserveCommitGate] group=" + groupName
                    + " action=" + action
                    + " role=" + role
                    + " reason=" + safeReason
                    + " changedUnits=" + Math.Max(0, changedUnits));
            }
            catch { }
        }

        private static string SafeGroupName(Regiment group)
        {
            try { return group != null ? TacticalCurrentOrderSignature.Safe(group.name) : "-"; }
            catch { return "-"; }
        }
    }
}
