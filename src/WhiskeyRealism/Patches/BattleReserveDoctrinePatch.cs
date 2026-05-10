using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B6c default-off reserve-list bias. Runs as Postfix on AIBattle.AssignReserves
    // (decompile 7017) and only reorders objectivechain[i].reservegroups when the
    // current TacticalReserveIntent allows runtime mutation. Every touched list is
    // snapshotted first and restored on any failure.
    [HarmonyPatch(typeof(AIBattle), "AssignReserves")]
    internal static class BattleReserveDoctrinePatch
    {
        private static FieldInfo _objectiveChainField;
        private static FieldInfo _reserveGroupsField;
        private static FieldInfo _sideOfAiField;
        private static bool _missingAnchorLogged;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.LowerThanNormal)]
        internal static void Postfix(AIBattle __instance)
        {
            if (!Enabled() || __instance == null) return;

            int side = SafeIntField(__instance, ref _sideOfAiField, "sideofai", -1);
            if (side < 0) return;

            if (!BattleCommanderIntentObserverPatch.RefreshRuntimeState(__instance, emitTelemetry: false))
                return;

            IList chain = ObjectiveChain(__instance);
            if (chain == null || chain.Count == 0) return;
            LogProtectedReserveDrift(side, chain);

            TacticalReserveIntentDecision intent = TacticalReactionContext.Shared.GetReserveIntent(side);
            if (!intent.AllowsRuntimeMutation || intent.Intent == TacticalReserveIntent.None) return;

            List<ReserveSnapshot> snapshot = SnapshotReserves(chain);
            if (snapshot.Count == 0) return;

            try
            {
                int changed = ApplyBias(chain, intent);
                EnforcePostconditions(chain, snapshot);
                if (changed > 0) LogMutation(side, intent, changed);
            }
            catch (Exception ex)
            {
                RestoreReserves(chain, snapshot);
                OnceLog.Warning(
                    "tactical-reserve-doctrine:failed",
                    "BattleReserveDoctrinePatch failed; restored snapshot: " + ex.Message);
            }
        }

        private struct ReserveSnapshot
        {
            public int Index;
            public List<object> Members;
        }

        private static List<ReserveSnapshot> SnapshotReserves(IList chain)
        {
            var snapshots = new List<ReserveSnapshot>();
            for (int i = 0; i < chain.Count; i++)
            {
                IList reserves = ReserveGroups(chain[i]);
                if (reserves == null) return new List<ReserveSnapshot>();

                var members = new List<object>(reserves.Count);
                for (int j = 0; j < reserves.Count; j++)
                    members.Add(reserves[j]);

                snapshots.Add(new ReserveSnapshot { Index = i, Members = members });
            }

            return snapshots;
        }

        private static void RestoreReserves(IList chain, List<ReserveSnapshot> snapshot)
        {
            try
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    int index = snapshot[i].Index;
                    if (index < 0 || index >= chain.Count) continue;

                    IList reserves = ReserveGroups(chain[index]);
                    if (reserves == null) continue;

                    reserves.Clear();
                    for (int j = 0; j < snapshot[i].Members.Count; j++)
                    {
                        reserves.Add(snapshot[i].Members[j]);
                    }
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-doctrine:restore",
                    "BattleReserveDoctrinePatch restore failed: " + ex.Message);
            }
        }

        private static int ApplyBias(IList chain, TacticalReserveIntentDecision intent)
        {
            if (intent.Intent != TacticalReserveIntent.RelieveBatteredLine &&
                intent.Intent != TacticalReserveIntent.ExploitWeakPoint)
                return 0;

            int changed = 0;
            for (int i = 0; i < chain.Count; i++)
            {
                IList reserves = ReserveGroups(chain[i]);
                if (reserves == null || reserves.Count <= 1) continue;

                int strongest = StrongestValidReserveIndex(reserves);
                if (strongest <= 0) continue;

                object first = reserves[0];
                reserves[0] = reserves[strongest];
                reserves[strongest] = first;
                changed++;
            }

            return changed;
        }

        private static int StrongestValidReserveIndex(IList reserves)
        {
            int strongest = -1;
            float best = float.MinValue;

            for (int i = 0; i < reserves.Count; i++)
            {
                Regiment group = reserves[i] as Regiment;
                if (!ValidReserveForRanking(group)) continue;
                if (IsLedgerProtectedReserve(group)) continue;
                if (!TacticalReserveCommitGate.PermitReserveListBias(ResolveCommandIntent(group))) continue;

                float score = ReserveStrengthScore(group);
                if (score > best)
                {
                    best = score;
                    strongest = i;
                }
            }

            return strongest;
        }

        private static bool ValidReserveForRanking(Regiment group)
        {
            return group != null && !group.dlcw_isundercommander && !HasPlayerCommandedAttachedUnit(group);
        }

        private static CommandIntentResolution ResolveCommandIntent(Regiment group)
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

        private static void LogProtectedReserveDrift(int side, IList chain)
        {
            try
            {
                for (int i = 0; i < chain.Count; i++)
                {
                    IList reserves = ReserveGroups(chain[i]);
                    if (reserves == null || reserves.Count == 0) continue;

                    Regiment first = reserves[0] as Regiment;
                    if (!TryResolveLedgerState(first, out CommandNodeOperationalState state)) continue;
                    if (!IsProtectedReserve(state)) continue;

                    OnceLog.Info(
                        "tactical-reserve-drift:assign-reserves:" + side + ":" + i + ":" + SafeInstanceId(first),
                        "[TacticalReserveDrift] surface=AssignReserves side=" + side +
                        " chain=" + i +
                        " group=" + SafeName(first) + "#" + SafeInstanceId(first) +
                        " ledgerRole=" + state.Role +
                        " ledgerTask=" + state.Task +
                        " reason=protected-reserve-in-next-commit-slot");
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-drift:assign-reserves:failed",
                    "[TacticalReserveDrift] AssignReserves drift inspection failed: " + ex.Message);
            }
        }

        private static bool IsLedgerProtectedReserve(Regiment group)
        {
            return TryResolveLedgerState(group, out CommandNodeOperationalState state) && IsProtectedReserve(state);
        }

        private static bool IsProtectedReserve(CommandNodeOperationalState state)
        {
            return state.Role == CommandNodeRole.Reserve || state.Task == CommandTaskType.ReserveWait;
        }

        private static bool TryResolveLedgerState(Regiment group, out CommandNodeOperationalState state)
        {
            state = default;
            try
            {
                if (group == null) return false;
                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                var operations = side?.Army?.CurrentCommandOperations;
                if (operations == null || operations.Count == 0) return false;

                string nodeId = "node-" + group.GetInstanceID();
                for (int i = 0; i < operations.Count; i++)
                {
                    if (string.Equals(operations[i].NodeId, nodeId, StringComparison.Ordinal))
                    {
                        state = operations[i];
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static int SafeInstanceId(Regiment group)
        {
            try { return group != null ? group.GetInstanceID() : 0; }
            catch { return 0; }
        }

        private static string SafeName(Regiment group)
        {
            try { return group != null ? TacticalCurrentOrderSignature.Safe(group.name) : "-"; }
            catch { return "-"; }
        }

        private static float ReserveStrengthScore(Regiment group)
        {
            float strength = Math.Max(Sanitize(group.groupstrength), Sanitize(group.groupowninrange));
            return Math.Max(0f, strength - Sanitize(group.grouplosses));
        }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }

        private static void EnforcePostconditions(IList chain, List<ReserveSnapshot> snapshot)
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                int index = snapshot[i].Index;
                if (index < 0 || index >= chain.Count)
                    throw new InvalidOperationException("objectivechain index disappeared: " + index);

                IList reserves = ReserveGroups(chain[index]);
                if (reserves == null)
                    throw new InvalidOperationException("reservegroups disappeared at chain " + index);

                List<object> before = snapshot[i].Members;
                if (reserves.Count != before.Count)
                    throw new InvalidOperationException("reservegroups count changed at chain " + index);

                for (int j = 0; j < reserves.Count; j++)
                {
                    object current = reserves[j];
                    if (current == null)
                        throw new InvalidOperationException("null reserve member at chain " + index + " index " + j);
                    if (!(current is Regiment group))
                        throw new InvalidOperationException("non-Regiment reserve member at chain " + index + " index " + j);
                    if (group.dlcw_isundercommander)
                        throw new InvalidOperationException("player-commanded reserve group at chain " + index + " index " + j);
                    if (HasPlayerCommandedAttachedUnit(group))
                        throw new InvalidOperationException("player-commanded attached reserve unit at chain " + index + " index " + j);
                    if (!ContainsReference(before, current))
                        throw new InvalidOperationException("new reserve member at chain " + index + " index " + j);

                    for (int k = j + 1; k < reserves.Count; k++)
                    {
                        if (ReferenceEquals(current, reserves[k]))
                            throw new InvalidOperationException("duplicate reserve member at chain " + index);
                    }
                }
            }
        }

        private static bool ContainsReference(List<object> members, object candidate)
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (ReferenceEquals(members[i], candidate)) return true;
            }
            return false;
        }

        private static bool HasPlayerCommandedAttachedUnit(Regiment group)
        {
            if (group == null || group.allattachedunits == null) return false;

            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                Regiment unit = group.allattachedunits[i];
                if (unit != null && unit.dlcw_isundercommander) return true;
            }

            return false;
        }

        private static IList ObjectiveChain(AIBattle battle)
        {
            if (_objectiveChainField == null)
                _objectiveChainField = AccessTools.Field(typeof(AIBattle), "objective" + "chain");
            if (_objectiveChainField == null)
            {
                LogMissingAnchor("objectivechain");
                return null;
            }

            IList chain = _objectiveChainField.GetValue(battle) as IList;
            if (chain == null) LogMissingAnchor("objectivechain:value");
            return chain;
        }

        private static IList ReserveGroups(object objectiveChainEntry)
        {
            if (objectiveChainEntry == null)
            {
                LogMissingAnchor("objectivechain:entry");
                return null;
            }

            if (_reserveGroupsField == null)
                _reserveGroupsField = AccessTools.Field(objectiveChainEntry.GetType(), "reservegroups");
            if (_reserveGroupsField == null)
            {
                LogMissingAnchor("reservegroups");
                return null;
            }

            IList reserves = _reserveGroupsField.GetValue(objectiveChainEntry) as IList;
            if (reserves == null) LogMissingAnchor("reservegroups:value");
            return reserves;
        }

        private static int SafeIntField(object instance, ref FieldInfo cache, string name, int fallback)
        {
            try
            {
                if (instance == null) return fallback;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                if (cache == null) return fallback;
                return Convert.ToInt32(cache.GetValue(instance));
            }
            catch
            {
                return fallback;
            }
        }

        private static void LogMutation(int side, TacticalReserveIntentDecision intent, int changed)
        {
            OnceLog.Info("tactical-reserve-doctrine:wired", "BattleReserveDoctrinePatch wired");
            OnceLog.Info(
                "tactical-reserve-doctrine:mutation:" + side + ":" + intent.Intent + ":" + intent.Reason,
                "[TacticalReserveMutation] side=" + side +
                " intent=" + intent.Intent +
                " changedLists=" + changed +
                " reason=" + intent.Reason);
        }

        private static void LogMissingAnchor(string anchor)
        {
            if (_missingAnchorLogged) return;
            _missingAnchorLogged = true;
            OnceLog.Warning(
                "tactical-reserve-doctrine:missing-anchor:" + anchor,
                "BattleReserveDoctrinePatch missing required anchor " + anchor + "; running inert");
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalObserver.Value &&
                Plugin.Instance.EnableTacticalLocalReactionDoctrine.Value &&
                Plugin.Instance.EnableTacticalReserveListMutation.Value;
        }
    }
}
