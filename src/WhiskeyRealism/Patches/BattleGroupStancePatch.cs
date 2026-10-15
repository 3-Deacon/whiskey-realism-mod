using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B5 group sector stance scorer. This is deliberately limited to stance
    // pressure; it does not issue movement, reserve, artillery, fallback, or
    // charge orders.
    [HarmonyPatch(typeof(AIBattle), "AdjustGroupAIStance")]
    internal static class BattleGroupStancePatch
    {
        private static readonly Dictionary<string, float> _lastLoggedAt = new Dictionary<string, float>();
        private static FieldInfo _macroAiField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _bunitsField;
        private static FieldInfo _unitsUsedField;
        private static FieldInfo _orderedStanceField;

        // Compiled-expression getter for the per-tick-per-group
        // Regiment.ai_stanceordered read. The legacy SafeIntField path uses
        // FieldInfo.GetValue which boxes + reflects on every call; this
        // delegate compiles down to a direct field load. Initialized lazily
        // on first use; degrades to the legacy SafeIntField path on
        // compile failure.
        private static Func<Regiment, int> _aiStanceOrderedGetter;
        private static bool _aiStanceOrderedGetterTried;

        // Per-AIBattle wallclock CPU throttle (2026-05-21). At high time compression
        // (20x / 50x), vanilla calls AdjustGroupAIStance N× more often per
        // wallclock — our Postfix walks unitsused on every call even when
        // ~99% of fires produce no write. Smoke at 20× compression showed
        // 9 Postfix fires per wallclock-second per side cumulating to ~22ms/sec
        // of main-thread cost, the dominant source of sustained frame hitching.
        // Cooldown gates Apply() body so it runs at most once per
        // BattleGroupStanceMinIntervalSeconds (default 1.5s, configurable).
        // Behavior preserved at low compression (1x-4x) because vanilla cadence
        // naturally fires Postfix less often than the cooldown.
        private static readonly Dictionary<int, float> _lastApplyRealtimeSeconds = new Dictionary<int, float>();

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.battle-group-stance", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (!Enabled() || __instance == null) return;

                try
                {
                    Apply(__instance);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-group-stance:failed", "Tactical group sector stance failed: " + ex.Message);
                }
            }
        }

        private static void Apply(AIBattle battle)
        {
            // The W&L feud instance (isplayeraiorfeud 2) duplicates side 0's
            // macro cycle; battle-level writers skip it.
            if (TacticalWriteGuard.IsFeudInstance(battle)) return;

            // Per-AIBattle CPU throttle (real time is allowed for CPU budgets):
            // at high compression vanilla calls AdjustGroupAIStance many times
            // per real second. Keyed by AIBattle instance so the W&L feud
            // instance (aibattle[2], same sideofai as side 0) does not share
            // side 0's slot (BR-M6).
            int battleKey = SafeInstanceId(battle);
            float minInterval = SafeMinIntervalSeconds();
            if (minInterval > 0f)
            {
                float now = SafeRealtimeSeconds();
                if (_lastApplyRealtimeSeconds.TryGetValue(battleKey, out float last) && now - last >= 0f && now - last < minInterval)
                    return;
                _lastApplyRealtimeSeconds[battleKey] = now;
            }

            // Artillery all-stance audit (2026-05-21). Walks each artillery
            // sub-unit (standalone batteries + brigade-attached batteries) for
            // this alliance and runs TacticalArtilleryMicroDoctrine. Fills the
            // gap left by vanilla's CheckAIBombardment which only runs for
            // ai_stance 3/4. Per-unit hold-time prevents thrash.
            try { ApplyArtilleryAudit(battle); }
            catch (Exception aex)
            {
                OnceLog.Warning("tactical-artillery-audit:apply-failed",
                    "ApplyArtilleryAudit threw: " + aex.GetType().Name + " " + aex.Message);
            }

            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            var units = SafeList(battle, ref _unitsUsedField, "unitsused");
            if (side < 0 || macro < 0 || bunits == null || units == null) return;
            if (macro == 3)
            {
                // Vanilla army retreat: release Whiskey's stance holds.
                TacticalStanceWriter.ReleaseForRetreat(battle);
                return;
            }
            int allianceId = SafeAllianceForSide(bunits, side);

            // Per group: the coordinator's resolved decision (populated at
            // heavy-gate fire, TacticalBattleCoordinatorRuntime
            // .PopulateResolvedDecisionsForSide) when present, otherwise the
            // full decide-and-write path.
            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i] as Regiment;
                if (group == null || !TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(group.unittyp)) continue;

                if (allianceId >= 0 && allianceId < 2 &&
                    TacticalBattleCoordinator.TryGetResolvedGroupDecision(allianceId, SafeInstanceId(group), out TacticalResolvedGroupDecision resolved))
                {
                    ApplyResolvedDecisionFast(bunits, side, macro, group, resolved);
                    continue;
                }

                ApplyGroup(bunits, side, macro, group, i);
            }
        }

        // Resets between battles. Called from TacticalBattleCoordinatorRuntime.OnBattleEnd.
        internal static void ResetRegimentalControlForBattleEnd()
        {
            _artilleryLastActionHours.Clear();
            _lastApplyRealtimeSeconds.Clear();
        }

        // Artillery audit state (2026-05-21). Per-instance battle time
        // (GameVars.currenttimefromstart, hours) of the last accepted
        // artillery action. Reused across BGS Apply calls for
        // the same battle (cleared on OnBattleEnd via
        // ResetRegimentalControlForBattleEnd). Min-hold gating prevents
        // thrash when doctrine output oscillates near thresholds (e.g.
        // Retreat ↔ Fire when enemy is at panic boundary).
        private static readonly Dictionary<int, float> _artilleryLastActionHours = new Dictionary<int, float>();

        // All-stance artillery audit. Walks completeunitlist filtered by alliance
        // + unittyp==2, evaluates TacticalArtilleryMicroDoctrine per unit,
        // applies the action through TacticalArtilleryApplier. Honors per-unit
        // hold-time + standard player-protection / routed gates.
        //
        // Cost: one full completeunitlist walk per Apply call (gated by BGS's
        // 1.5s cooldown). Per-artillery doctrine call is O(1) + a small
        // friendlies count loop.
        private static void ApplyArtilleryAudit(AIBattle battle)
        {
            if (Plugin.EnableArtilleryAllStanceAudit == null
                || !Plugin.EnableArtilleryAllStanceAudit.Value) return;
            if (battle == null) return;

            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            if (bunits == null) return;

            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            if (side < 0) return;
            int allianceId = SafeAllianceForSide(bunits, side);
            if (allianceId < 0 || allianceId >= 2) return;

            var allUnits = BattleUnits.completeunitlist as System.Collections.IList;
            if (allUnits == null) return;

            // Hold on battle time; the config value is seconds at 1x.
            float nowHours = GameVars.currenttimefromstart;
            float minHoldHours = TacticalBattleClock.RealSecondsAtOneXToHours(
                Plugin.ArtilleryAuditMinHoldSeconds != null
                    ? Plugin.ArtilleryAuditMinHoldSeconds.Value
                    : 3.0f);

            for (int i = 0; i < allUnits.Count; i++)
            {
                var unit = allUnits[i] as Regiment;
                if (unit == null) continue;
                if (unit.alliance != allianceId) continue;
                if (unit.unittyp != 2) continue;
                if (unit.guns <= 0) continue;
                if (unit.isrouted || unit.markedforrout || unit.permanentlydetached) continue;

                int instanceId;
                try { instanceId = unit.GetInstanceID(); }
                catch { continue; }
                if (instanceId == 0) continue;

                // Per-unit hold gate. If we acted on this unit < minHold ago,
                // don't act again. Lets the prior vanilla write settle. A clock
                // going backwards (reload) ends the hold.
                float lastActionAt;
                if (_artilleryLastActionHours.TryGetValue(instanceId, out lastActionAt)
                    && nowHours - lastActionAt >= 0f && nowHours - lastActionAt < minHoldHours)
                {
                    continue;
                }

                // Player-owned units, the W&L player's command and chain
                // (dlcw_isundercommander) and DLC-taken-over units: never override.
                if (TacticalWriteGuard.IsPlayerProtected(unit)) continue;

                // Resolve current state and run doctrine.
                Regiment closestEnemy;
                try { closestEnemy = TacticalFogOfWarContact.ClosestVisibleEnemy(unit); }
                catch { closestEnemy = null; }

                bool hasEnemy = closestEnemy != null;
                float enemyDistance = 0f;
                bool enemyRouted = false;
                if (hasEnemy)
                {
                    try
                    {
                        var op = unit.transform.position;
                        var ep = closestEnemy.transform.position;
                        float dx = ep.x - op.x;
                        float dz = ep.z - op.z;
                        enemyDistance = (float)System.Math.Sqrt(dx * dx + dz * dz);
                        enemyRouted = closestEnemy.isrouted
                            || closestEnemy.onretreat
                            || closestEnemy.markedforrout;
                    }
                    catch { hasEnemy = false; }
                }

                int supportCount = CountFriendlyInfantryCavInterposed(unit, closestEnemy, allUnits, allianceId, enemyDistance, instanceId);

                float maxRange = 0f;
                try { maxRange = unit.firerange; } catch { maxRange = 0f; }
                float ammoRatio = TacticalArtilleryInputAdapter.ArtilleryAmmoRatio01(unit.ammo);
                // Panic distance: Scourge's DistArtyPanic is per-unit-type runtime,
                // GT vanilla has no analog. 100m default matches musket effective
                // range — once enemy infantry is closer, unsupported artillery is
                // in real danger.
                float panicDistance = 100f;
                bool moraleBonus = SafeMoraleBonus(unit);
                bool canWheel = unit.regimentpaths == 0 && unit.mounted == 0;
                var limberState = TacticalArtilleryApplier.LimberStateFor(unit);

                var input = new TacticalArtilleryMicroInput(
                    hasEnemy: hasEnemy,
                    enemyCloseDistance: enemyDistance,
                    panicDistance: panicDistance,
                    supportCount: supportCount,
                    moraleBonus: moraleBonus,
                    enemyRoutedOrRetreating: enemyRouted,
                    limberState: limberState,
                    ammoRatio01: ammoRatio,
                    targetDistance: enemyDistance,
                    maxRange: maxRange,
                    frontalTargetInRange: hasEnemy && enemyDistance > 0f && enemyDistance <= maxRange,
                    canWheel: canWheel,
                    currentQuadrantThreat: hasEnemy ? 1f : 0f,
                    bestQuadrantThreat: hasEnemy ? 1f : 0f);

                var decision = TacticalArtilleryMicroDoctrine.Decide(input);

                // Apply the action through the shared applier. Emit telemetry
                // even when the doctrine returns Fire/Preserve so the smoke can
                // see what artillery the doctrine is examining.
                EmitArtilleryDecisionTelemetry(side, unit, decision, enemyDistance, supportCount, limberState);

                int currentBehavior = 0;
                try { currentBehavior = unit.combatbehaviorordered; } catch { }

                bool wrote = TacticalArtilleryApplier.ApplyMicroDecision(
                    bunits, unit, currentBehavior, decision, "art-audit");
                if (wrote)
                {
                    _artilleryLastActionHours[instanceId] = nowHours;
                }
            }
        }

        private static int CountFriendlyInfantryCavInterposed(
            Regiment unit,
            Regiment closestEnemy,
            System.Collections.IList allUnits,
            int allianceId,
            float enemyDistance,
            int selfInstanceId)
        {
            // Revised 2026-05-22 from prior CountFriendlyInfantryCavWithinEnemyDistance.
            // Original prior bug: counted friendlies within enemyDistance of
            // artillery in ANY direction (including BEHIND artillery), which
            // produced support counts of 14-27 when smoke showed artillery
            // at 0-50m from enemy infantry. Friendlies behind artillery don't
            // block enemy fire from in front.
            //
            // New rule: friendly counts as interposed (real support) when:
            //   1. friend is on the enemy-side of artillery (projection of
            //      (friend - artillery) onto (enemy - artillery) is positive)
            //   2. friend is closer to artillery than enemy is (projection
            //      length < distance to enemy)
            //   3. friend is reasonably "on the axis" — lateral offset from
            //      the artillery→enemy line is at most ~100m (matches
            //      regiment frontage — wider misses still get hit by enemy fire)
            //
            // Fallback: if closestEnemy is null or enemyDistance is invalid,
            // use the prior naive radius check at 200m so the doctrine still
            // has a sane support estimate.
            int count = 0;
            try
            {
                Vector3 own = unit.transform.position;

                // Fallback path when interposition geometry isn't available.
                if (closestEnemy == null || enemyDistance <= 1f)
                {
                    float fallbackRangeSq = 200f * 200f;
                    for (int i = 0; i < allUnits.Count; i++)
                    {
                        var f = allUnits[i] as Regiment;
                        if (f == null) continue;
                        if (f.alliance != allianceId) continue;
                        int fid;
                        try { fid = f.GetInstanceID(); } catch { continue; }
                        if (fid == 0 || fid == selfInstanceId) continue;
                        if (f.isrouted || f.markedforrout || f.permanentlydetached) continue;
                        if (f.unittyp != 0 && f.unittyp != 1 && f.unittyp != 4) continue;
                        var fp = f.transform.position;
                        float dx = fp.x - own.x;
                        float dz = fp.z - own.z;
                        if (dx * dx + dz * dz <= fallbackRangeSq) count++;
                    }
                    return count;
                }

                // Geometric interposition path.
                Vector3 enemyPos = closestEnemy.transform.position;
                float toEnemyX = enemyPos.x - own.x;
                float toEnemyZ = enemyPos.z - own.z;
                float toEnemyLenSq = toEnemyX * toEnemyX + toEnemyZ * toEnemyZ;
                if (toEnemyLenSq < 1f) return 0;
                float toEnemyLen = (float)System.Math.Sqrt(toEnemyLenSq);

                for (int i = 0; i < allUnits.Count; i++)
                {
                    var f = allUnits[i] as Regiment;
                    if (f == null) continue;
                    if (f.alliance != allianceId) continue;
                    int fid;
                    try { fid = f.GetInstanceID(); } catch { continue; }
                    if (fid == 0 || fid == selfInstanceId) continue;
                    if (f.isrouted || f.markedforrout || f.permanentlydetached) continue;
                    if (f.unittyp != 0 && f.unittyp != 1 && f.unittyp != 4) continue;

                    var fp = f.transform.position;
                    float toFriendX = fp.x - own.x;
                    float toFriendZ = fp.z - own.z;

                    // Project (friend - artillery) onto (enemy - artillery) axis.
                    // dot = |toFriend| * |toEnemy| * cos(theta). Positive means
                    // friend is on the enemy-side; negative means behind artillery.
                    float dot = toFriendX * toEnemyX + toFriendZ * toEnemyZ;
                    if (dot <= 0f) continue;  // friend is behind artillery — not interposed

                    // Project length = component of friend offset along axis.
                    float projLen = dot / toEnemyLen;
                    if (projLen >= toEnemyLen) continue;  // friend is at or past enemy — not interposed

                    // Lateral distance from artillery→enemy axis.
                    float friendDistSq = toFriendX * toFriendX + toFriendZ * toFriendZ;
                    float lateralSq = friendDistSq - projLen * projLen;
                    if (lateralSq < 0f) lateralSq = 0f;
                    if (lateralSq > 100f * 100f) continue;  // too far off-axis to actually shield

                    count++;
                }
            }
            catch { /* count stays 0 */ }
            return count;
        }

        private static bool SafeMoraleBonus(Regiment unit)
        {
            try { return unit.groupmorale >= 0.6f; }
            catch { return false; }
        }

        private static void EmitArtilleryDecisionTelemetry(
            int side,
            Regiment unit,
            TacticalArtilleryMicroDecision decision,
            float enemyDistance,
            int supportCount,
            TacticalArtilleryLimberState limberState)
        {
            try
            {
                int instanceId;
                try { instanceId = unit.GetInstanceID(); } catch { instanceId = 0; }
                // One row per battery per distinct decision per 30 s: the audit
                // re-evaluates every battery each stance pass.
                string dedupe = side + "|" + instanceId + "|" + decision.Action + "|" + decision.AmmoMission + "|" + decision.Reason + "|" + limberState;
                if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "artillery-audit-decision", dedupe, Time.realtimeSinceStartup, 30f, false))
                    return;
                string unitName = SafeRegimentName(unit);
                string sig = "TacticalArtilleryDecision|side=" + side
                    + "|unit=" + unitName
                    + "|id=" + instanceId
                    + "|action=" + decision.Action
                    + "|ammo=" + decision.AmmoMission
                    + "|reason=" + decision.Reason
                    + "|enemyDist=" + enemyDistance.ToString("0")
                    + "|support=" + supportCount
                    + "|limber=" + limberState;
                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Decision,
                    "TacticalArtilleryDecision",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(side)
                        .WithUnit(unitName)
                        .WithDecision(decision.Action.ToString(), decision.Reason, sig)
                        .WithField("instanceId", instanceId)
                        .WithField("action", decision.Action.ToString())
                        .WithField("ammoMission", decision.AmmoMission.ToString())
                        .WithField("reason", decision.Reason)
                        .WithField("enemyDistance", enemyDistance)
                        .WithField("supportCount", supportCount)
                        .WithField("limberState", limberState.ToString()));
            }
            catch { /* never throw out of audit */ }
        }

        private static string SafeRegimentName(Regiment reg)
        {
            try { return ((UnityEngine.Object)((UnityEngine.Component)reg).gameObject).name ?? "<noname>"; }
            catch { return "<error>"; }
        }

        /// <summary>
        /// "Cheap per-group" apply path (2026-05-20). Hot path: dict lookup
        /// already happened; here we only enforce the per-tick safety gates
        /// that depend on live Regiment state, then dispatch the cached
        /// action. No resolver walks, no sector rebuild, no
        /// DecideGroupStance recompute — those ran once at the
        /// heavy-gate-fire boundary in
        /// <c>TacticalBattleCoordinatorRuntime.PopulateResolvedDecisionsForSide</c>.
        ///
        /// Perf reorder (2026-05-20): the cached FinalStance matches the
        /// live vanilla ordered stance ~99.98% of the time (smoke: 2 writes
        /// in 13,050 group-iterations). The old structure paid the full
        /// WlAllowsControl + OrderFrictionAllowsChange safety-gate cost
        /// (~250us/group, dominated by WL iterating group.allattachedunits)
        /// before checking whether a write was needed at all. We now run
        /// the cheap value-compares first so the no-write fast path skips
        /// the safety gates entirely. Gates still run on the (rare) write
        /// path — same behavior, just deferred until a write is actually
        /// in play.
        /// </summary>
        private static void ApplyResolvedDecisionFast(
            BattleUnits bunits,
            int side,
            int macro,
            Regiment group,
            TacticalResolvedGroupDecision resolved)
        {
            // Cheap read — direct field access via compiled delegate.
            int vanillaOrdered = ReadAiStanceOrdered(group);

            // EARLY-OUT FAST PATH (no-write case, ~99.98% of iterations).
            //
            // When this isn't a charge override AND vanilla isn't currently
            // racing into charge, the only way a write happens is through
            // the non-charge branch — and that branch already screens for
            // (Kind != Apply | FinalStance invalid | FinalStance ==
            // vanillaOrdered | macro == 3). All of those are pure value
            // compares over `resolved` + `vanillaOrdered` + `macro`. Hoist
            // them here so we can bail before paying the safety-gate cost
            // (WlAllowsControl iterates ~50-100 attached units per group;
            // OrderFrictionAllowsChange touches a handful of live fields).
            //
            // Correctness: these are the exact same predicates the original
            // non-charge branch enforces below. If they would have caused a
            // bail there, they cause an equivalent bail here — the safety
            // gates were never going to write anything anyway. We do NOT
            // include AllowsVanillaWrites() / activeInHierarchy / gameObject
            // null checks here because those need to happen AFTER the
            // safety gates to keep MonitorOnly telemetry counting attempts
            // gated by WL/order-friction (the original ordering).
            if (!resolved.IsChargeOverride && vanillaOrdered != 4)
            {
                if (resolved.Kind != TacticalDoctrineDecisionKind.Apply) return;
                if (resolved.FinalStance < 1 || resolved.FinalStance > 3) return;
                if (macro == 3) return;
                if (resolved.FinalStance == vanillaOrdered)
                {
                    // Agreement: the writer re-stamps the single-owner hold so
                    // vanilla's AdjustGroupAIStance does not flip the group next
                    // cycle — unless vanilla's per-group defend/screen applies
                    // (weak group), in which case it yields and releases the hold.
                    TacticalStanceWriter.EnsureStance(bunits, group, resolved.FinalStance, null, StanceWriteSource.Scorer);
                    return;
                }
                // Falls through to the safety gates + non-charge write.
            }
            // Else: charge-override path, vanilla-raced-into-charge path,
            // or non-charge write — all still need the safety gates.

            // Per-tick safety gates. WL/order-friction live on the apply
            // path because they consume Regiment state that can change
            // every vanilla tick (player control toggle, fresh queued
            // orders, path interrupts). Reached only when a write or
            // charge-preserved telemetry log is actually likely.
            if (!WlAllowsControl(group)) return;
            if (!OrderFrictionAllowsChange(group)) return;

            // Charge-override branch. Cache decided WHETHER to demote at
            // gate-fire time; live reaction context is consulted here only
            // for the demote telemetry detail. If vanilla raced from 4 to
            // something else, fall through to FinalStance (the cached
            // charge override no longer applies).
            if (resolved.IsChargeOverride && vanillaOrdered == 4)
            {
                switch (resolved.ChargeAction)
                {
                    case ChargeOverrideAction.PreserveVanilla:
                    case ChargeOverrideAction.DenyExplicit:
                    case ChargeOverrideAction.None:
                        LogChargePreserved(side, group, resolved.Reason);
                        return;
                    case ChargeOverrideAction.DemoteToHold:
                        if (!AllowsVanillaWrites())
                        {
                            LogChargePreserved(side, group, "mode-monitor-only");
                            return;
                        }
                        var reaction = TacticalReactionContext.Shared.GetReaction(SafeInstanceId(group));
                        DemoteCharge(bunits, group, side, reaction);
                        return;
                }
                return;
            }

            // If vanilla raced INTO charge (vanillaOrdered == 4) but the
            // cache didn't flag this group as a charge override (because
            // it wasn't at 4 at gate-fire time), preserve vanilla's
            // charge. The cached FinalStance would write a non-charge
            // value over the fresh vanilla write, which is wrong.
            if (vanillaOrdered == 4)
            {
                LogChargePreserved(side, group, "vanilla-charge-preserved");
                return;
            }

            // Non-charge write branch. When we entered via the early-out
            // fast path (the common case), Kind/FinalStance/macro were
            // already screened — these re-checks are cheap no-ops. When we
            // entered via the IsChargeOverride && vanillaOrdered != 4
            // fall-through (cache flagged charge override but vanilla
            // raced out of charge), the early-out block was SKIPPED, so
            // we must screen the same predicates the original non-charge
            // branch enforced before writing the cached FinalStance.
            if (resolved.Kind != TacticalDoctrineDecisionKind.Apply) return;
            if (resolved.FinalStance < 1 || resolved.FinalStance > 3) return;
            if (macro == 3) return;

            if (TacticalStanceWriter.EnsureStance(bunits, group, resolved.FinalStance, null, StanceWriteSource.Scorer))
                LogResolvedDecision(side, group, resolved);
        }

        private static int ReadAiStanceOrdered(Regiment group)
        {
            if (group == null) return 0;

            if (!_aiStanceOrderedGetterTried)
            {
                _aiStanceOrderedGetterTried = true;
                try
                {
                    var field = AccessTools.Field(typeof(Regiment), "ai_stanceordered");
                    if (field != null)
                    {
                        var p = Expression.Parameter(typeof(Regiment), "r");
                        var body = Expression.Field(p, field);
                        _aiStanceOrderedGetter = Expression.Lambda<Func<Regiment, int>>(body, p).Compile();
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("[BattleGroupStancePatch] ai_stanceordered getter compile failed: "
                        + e.GetType().Name + " " + e.Message);
                }
            }

            var getter = _aiStanceOrderedGetter;
            if (getter != null)
            {
                try { return getter(group); }
                catch { /* fall through to reflection fallback */ }
            }

            return SafeIntField(group, ref _orderedStanceField, "ai_" + "stanceordered", group.ai_stanceordered);
        }

        private static int SafeAllianceForSide(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.alliance == null) return -1;
                if (side < 0 || side >= bunits.alliance.Length) return -1;
                return bunits.alliance[side];
            }
            catch
            {
                return -1;
            }
        }

        private static void ApplyGroup(BattleUnits bunits, int side, int macro, Regiment group, int index)
        {
            if (!WlAllowsControl(group)) return;
            if (!OrderFrictionAllowsChange(group)) return;

            int vanillaOrdered = SafeIntField(group, ref _orderedStanceField, "ai_" + "stanceordered", group.ai_stanceordered);
            var sector = BuildGroupSector(group, index);

            if (TryApplyDoctrineStance(bunits, side, macro, group, sector))
                return;

            if (vanillaOrdered == 4)
            {
                if (!Plugin.Instance.EnableTacticalChargeDenial.Value || !LocalReactionProducerEnabled())
                {
                    LogChargePreserved(side, group, "vanilla-charge-preserved");
                    return;
                }

                var reaction = TacticalReactionContext.Shared.GetReaction(SafeInstanceId(group));
                if (IsExplicitChargeDenial(reaction))
                {
                    if (!AllowsVanillaWrites())
                    {
                        LogChargePreserved(side, group, "mode-monitor-only");
                        return;
                    }

                    DemoteCharge(bunits, group, side, reaction);
                    return;
                }

                LogChargePreserved(side, group, "vanilla-charge-preserved");
                return;
            }

            if (TryApplyLedgerTaskStance(bunits, side, macro, group, sector))
                return;

            if (!Plugin.Instance.EnableTacticalGroupSectorStance.Value) return;

            var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
                vanillaOrdered,
                macro,
                sector,
                true,
                true));

            if (decision.Kind != TacticalDoctrineDecisionKind.Apply) return;
            if (decision.GroupStance < 1 || decision.GroupStance > 3) return;

            if (TacticalStanceWriter.EnsureStance(bunits, group, decision.GroupStance, null, StanceWriteSource.Scorer))
                LogDecision(side, group, sector, decision);
        }

        private static bool TryApplyDoctrineStance(
            BattleUnits bunits,
            int side,
            int macro,
            Regiment group,
            TacticalSectorAssessment sector)
        {
            if (macro == 3) return false;
            if (!TacticalDoctrineOrderResolver.TryResolve(group, out CommandDoctrineOrder order, out BattlefieldPictureSnapshot picture)) return false;

            DoctrineStanceDecision decision = DoctrineConsumerDecisions.DecideStance(
                order,
                DoctrineConsumerDecisions.EnemyMainLineExposed(order, picture),
                sector.Odds);
            if (decision.Action == DoctrineConsumerAction.Observe) return false;

            int stance = decision.Action == DoctrineConsumerAction.Allow ? 3 : 2;
            if (TacticalStanceWriter.EnsureStance(bunits, group, stance, null, StanceWriteSource.Scorer))
                LogDoctrineStanceDecision(side, group, order, decision, stance);
            return true;
        }

        private static bool TryApplyLedgerTaskStance(BattleUnits bunits, int side, int macro, Regiment group, TacticalSectorAssessment sector)
        {
            if (macro == 3) return true;
            if (!TacticalLedgerStateResolver.TryResolve(group, out CommandNodeOperationalState state)) return false;

            // Task → stance from the shared mapper so #45 and #61 agree.
            int stance = TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(
                state.Task,
                TacticalDoctrineScorer.ShouldCommitVisibleWeakPoint(sector));
            if (stance < 0) return true;
            if (TacticalStanceWriter.EnsureStance(bunits, group, stance, null, StanceWriteSource.Scorer))
                LogLedgerTaskDecision(side, group, state, stance);
            return true;
        }

        private static void LogDoctrineStanceDecision(
            int side,
            Regiment group,
            CommandDoctrineOrder order,
            DoctrineStanceDecision decision,
            int stance)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + order.Task + "|" +
                decision.Action + "|" + stance + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "group-doctrine-consumer", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalGroupDecision] side=" + side +
                " group=" + SafeInstanceId(group) +
                " stance=" + stance +
                " doctrineTask=" + order.Task +
                " action=" + decision.Action +
                " reason=" + decision.Reason, TelemetryLayer.Tactical);
        }

        private static void LogLedgerTaskDecision(int side, Regiment group, CommandNodeOperationalState state, int stance)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + state.Task + "|" + stance;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "group-ledger-task", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalGroupDecision] side=" + side +
                " group=" + SafeInstanceId(group) +
                " stance=" + stance +
                " ledgerTask=" + state.Task +
                " ledgerRole=" + state.Role +
                " reason=operations-ledger-task", TelemetryLayer.Tactical);
        }

        private static bool IsExplicitChargeDenial(TacticalLocalReactionDecision reaction)
        {
            return reaction.Reason != "no-decision" &&
                reaction.Reaction == LocalReaction.DenyCharge;
        }

        private static void DemoteCharge(
            BattleUnits bunits,
            Regiment group,
            int side,
            TacticalLocalReactionDecision reaction)
        {
            if (!TacticalStanceWriter.EnsureStance(bunits, group, 3, null, StanceWriteSource.ChargeDemotion)) return;

            string signature = side + "|" + SafeInstanceId(group) + "|" + reaction.Reaction + "|" + reaction.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "b6c-charge-deny-stance", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalChargeDeny] surface=stance side=" + side +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group) +
                " reaction=" + reaction.Reaction +
                " reason=" + reaction.Reason, TelemetryLayer.Tactical);
        }

        private static void LogChargePreserved(int side, Regiment group, string reason)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "b6c-charge-preserved", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalChargePreserved] surface=stance side=" + side +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group) +
                " reason=" + reason, TelemetryLayer.Tactical);
        }

        private static TacticalSectorAssessment BuildGroupSector(Regiment group, int index)
        {
            Regiment closest = TacticalFogOfWarContact.ClosestVisibleEnemy(group);
            return TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
                index,
                group != null ? Math.Max(group.groupowninrange, group.groupstrengthaigroup) : 0f,
                group != null ? group.groupenemiesinrange : 0f,
                SumEnemyStrengthWithinAngle(group),
                closest != null ? closest.strength : 0f,
                closest != null ? closest.unittyp : -1,
                closest != null ? SafeName(closest) : string.Empty,
                closest != null && closest.isrouted,
                closest != null && closest.permanentlydetached,
                group != null && (group.flanksthreated > 0f || group.outflanked > 0),
                group != null && (group.covervalue > 0.5f || group.fortinrange)));
        }

        private static bool WlAllowsControl(Regiment group)
        {
            var decision = TacticalWlActionGuard.Decide(
                true,
                DLC_WL.dlc_scenarioactive,
                TacticalWlGuardAction.FeudMovement,
                group != null && group.dlcw_isundercommander,
                group != null && group.dlcw_isundercommander,
                AttachedUnitUnderPlayerCommander(group));
            return decision.Allow;
        }

        private static bool AttachedUnitUnderPlayerCommander(Regiment group)
        {
            if (group == null || group.allattachedunits == null) return false;
            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                var unit = group.allattachedunits[i];
                if (unit != null && unit.dlcw_isundercommander) return true;
            }

            return false;
        }

        private static bool OrderFrictionAllowsChange(Regiment group)
        {
            return TacticalOrderSettlementGate.Evaluate(new TacticalOrderSettlementGate.Input
            {
                OrderQueueCount = SafeOrderQueueCount(group),
                OrderState = group != null ? group.orderstate : -1,
                RegimentPaths = group != null ? group.regimentpaths : 0,
                PathInterrupted = group != null && group.pathinterrupted,
                MovementMode = group != null ? group.movementmode : -1,
                ActiveMove = HasActiveMoveOrder(group)
            }).AllowChange;
        }

        private static float SumEnemyStrengthWithinAngle(Regiment group)
        {
            try
            {
                if (group == null ||
                    group.unitrange == null ||
                    group.unitrange.enemystrengthwithinangle == null ||
                    !TacticalFogOfWarContact.HasVisibleEnemy(group))
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

        private static bool HasActiveMoveOrder(Regiment unit)
        {
            try
            {
                return unit != null &&
                    unit.ordertypeactive != null &&
                    unit.ordertypeactive.Length > 1 &&
                    (unit.ordertypeactive[0] || unit.ordertypeactive[1]);
            }
            catch
            {
                return false;
            }
        }

        private static int SafeOrderQueueCount(Regiment group)
        {
            try { return group != null && group.orderqueue != null ? group.orderqueue.Count : 0; }
            catch { return 1; }
        }

        private static void LogResolvedDecision(int side, Regiment group, TacticalResolvedGroupDecision resolved)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + resolved.FinalStance + "|" + resolved.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "group-resolved", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalGroupDecision] side=" + side +
                " group=" + SafeInstanceId(group) +
                " stance=" + resolved.FinalStance +
                " reason=" + resolved.Reason +
                " doctrine=" + resolved.FromDoctrine +
                " ledger=" + resolved.FromLedger, TelemetryLayer.Tactical);
        }

        private static void LogDecision(
            int side,
            Regiment group,
            TacticalSectorAssessment sector,
            TacticalGroupStanceDecision decision)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + sector.SectorId + "|" +
                decision.GroupStance + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "group-decision", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalGroupDecision] side=" + side +
                " group=" + SafeInstanceId(group) +
                " sector=" + sector.SectorId +
                " stance=" + decision.GroupStance +
                " mission=" + sector.Mission +
                " reason=" + decision.Reason +
                " odds=" + sector.Odds.ToString("0.00") +
                " confidence=" + sector.Confidence.ToString("0.00"), TelemetryLayer.Tactical);
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                (Plugin.Instance.EnableTacticalGroupSectorStance.Value ||
                    Plugin.Instance.TacticalOperationsLedgerAllowsWrites);
        }

        // Wallclock seconds since process start. Wraps UnityEngine.Time.realtimeSinceStartup
        // with a try/catch so a Unity API stall (rare) never throws out of the Postfix.
        private static float SafeRealtimeSeconds()
        {
            try { return UnityEngine.Time.realtimeSinceStartup; }
            catch { return 0f; }
        }

        // Cooldown interval (seconds) between AdjustGroupAIStance Postfix bodies per side.
        // Default 1.5s — measured at 20× compression where vanilla fires the patch ~9 times
        // per wallclock-second per side; cooldown drops effective fires to ~0.67/sec which
        // is plenty for tactical AI responsiveness while removing the sustained ~22ms/sec
        // main-thread cost. 0 disables the cooldown (preserves pre-2026-05-21 behavior).
        private static float SafeMinIntervalSeconds()
        {
            try { return Plugin.BattleGroupStanceMinIntervalSeconds?.Value ?? 1.5f; }
            catch { return 1.5f; }
        }

        private static bool AllowsVanillaWrites()
        {
            try
            {
                return Plugin.Instance != null &&
                    TacticalCommanderModePolicy.AllowsWrites(Plugin.Instance.TacticalCommanderModeValue);
            }
            catch
            {
                return false;
            }
        }

        private static bool LocalReactionProducerEnabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.EnableTacticalObserver.Value &&
                Plugin.Instance.EnableTacticalCommanderIntentDoctrine.Value &&
                Plugin.Instance.EnableTacticalLocalReactionDoctrine.Value;
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

        private static T SafeField<T>(object instance, ref FieldInfo cache, string name) where T : class
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as T : null;
            }
            catch
            {
                return null;
            }
        }

        private static IList SafeList(object instance, ref FieldInfo cache, string name)
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as IList : null;
            }
            catch
            {
                return null;
            }
        }

        private static string SafeName(Regiment group)
        {
            try
            {
                return group != null ? ((Component)group).gameObject.name : "<null>";
            }
            catch
            {
                return "<err>";
            }
        }

        private static int SafeInstanceId(UnityEngine.Object obj)
        {
            try
            {
                return obj != null ? obj.GetInstanceID() : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
