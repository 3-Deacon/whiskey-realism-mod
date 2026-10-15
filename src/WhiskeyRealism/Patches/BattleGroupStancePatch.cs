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

        // Per-side wallclock cooldown (2026-05-21). At high time compression
        // (20x / 50x), vanilla calls AdjustGroupAIStance N× more often per
        // wallclock — our Postfix walks unitsused on every call even when
        // ~99% of fires produce no write. Smoke at 20× compression showed
        // 9 Postfix fires per wallclock-second per side cumulating to ~22ms/sec
        // of main-thread cost, the dominant source of sustained frame hitching.
        // Cooldown gates Apply() body so it runs at most once per
        // BattleGroupStanceMinIntervalSeconds (default 1.5s, configurable).
        // Behavior preserved at low compression (1x-4x) because vanilla cadence
        // naturally fires Postfix less often than the cooldown.
        private static readonly float[] _lastApplyRealtimeSeconds = { 0f, 0f };

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
            // Three-tier read path (see TryApplyFromDecisionSnapshot for
            // detail): "cheap per-group" resolved cache → worker snapshot →
            // legacy ApplyGroup. The cache is populated on every
            // heavy-gate fire by
            // TacticalBattleCoordinatorRuntime.PopulateResolvedDecisionsForSide,
            // so the dominant per-tick cost collapses to a dict lookup
            // plus the cheap safety gates that need live Regiment state.
            //
            // Worst case (no orchestrator state, worker off, gate hasn't
            // fired yet): the fallback below runs ApplyGroup for every
            // commandable Regiment — the legacy pre-refactor path. We
            // keep this fallback intact so suppressed-side / pre-bootstrap
            // / config-flipped sessions still behave identically to the
            // pre-refactor build.
            // Per-side wallclock cooldown — see _lastApplyRealtimeSeconds field comment.
            // Resolve side EARLY so we can gate the rest of Apply on it. We resolve here
            // (not in Postfix entry) so the SafeIntField lookup is the only work paid on
            // an early-return cooldown hit. Side resolution itself is a cached FieldInfo
            // read — cheap (~1 µs).
            int sideForCooldown = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            if (sideForCooldown >= 0 && sideForCooldown < _lastApplyRealtimeSeconds.Length)
            {
                float minInterval = SafeMinIntervalSeconds();
                if (minInterval > 0f)
                {
                    float now = SafeRealtimeSeconds();
                    float last = _lastApplyRealtimeSeconds[sideForCooldown];
                    if (last > 0f && now - last < minInterval)
                    {
                        // Cooldown active for this side — skip the walk entirely.
                        // Patches that wrote stance on the prior fire stay in place
                        // (vanilla preserves them across cycles). No telemetry emit
                        // on the cooldown branch; the Postfix scope still measures
                        // the (now-cheap) call.
                        return;
                    }
                    _lastApplyRealtimeSeconds[sideForCooldown] = now;
                }
            }

            // Regimental intelligence pass (2026-05-21). Reads worker-published
            // per-regiment decisions and either emits telemetry (write-disabled mode)
            // or applies SetWaypoint / SetGroupFormation orders. Runs after the
            // brigade-stance path so the regimental overrides have access to the
            // most recent brigade-level state. Cheap: dict lookup per regiment
            // walked.
            try { ApplyRegimentalIntelligence(battle); }
            catch (Exception ree)
            {
                OnceLog.Warning("tactical-regimental-intelligence:apply-failed",
                    "ApplyRegimentalIntelligence threw: " + ree.GetType().Name + " " + ree.Message);
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

            if (TryApplyFromDecisionSnapshot(battle)) return;

            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            var units = SafeList(battle, ref _unitsUsedField, "unitsused");
            if (side < 0 || macro < 0 || bunits == null || units == null) return;

            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i] as Regiment;
                if (group == null || !TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(group.unittyp)) continue;
                ApplyGroup(bunits, side, macro, group, i);
            }
        }

        // Regimental intelligence apply path (2026-05-21). Walks completeunitlist
        // filtered to this AIBattle's alliance, reads worker decisions per
        // regiment, emits telemetry, and optionally writes vanilla orders.
        //
        // Always-on: telemetry emission so smoke can verify the doctrine is
        // making the right calls (which regiments get FallBack, which get
        // RetreatToBrigade) before the user flips the write flag.
        //
        // Gated by Plugin.EnableRegimentalIntelligenceWrites: actual SetWaypoint
        // / SetGroupFormation calls. Default OFF until decisions are verified.
        // Scope B refactor (2026-05-22). The prior _whiskeyControlledRegiments
        // HashSet + PerformAIActionDLCWLPatch Prefix architecture has been
        // removed. New approach plugs into vanilla's W&L canonical pipeline:
        //
        //   doctrine decides → bunits.ChangeStance(reg, mapped_stance) +
        //                       bunits.SetWaypoint(reg, target)
        //
        // ChangeStance updates `ai_stanceordered`, which vanilla's micro-AI
        // respects natively (it's the same field the player's W&L UI writes).
        // No need to gate vanilla AI via Prefix patch — vanilla AI executes
        // ACCORDING to the stance we wrote, exactly as it would for a
        // player-issued order.
        //
        // The hold-time gate is preserved at the brigade-task layer
        // (BattleCommandPostureExecutorPatch.ShouldSuppressTaskRefresh) which
        // suppresses re-writes for the same task within Plugin.BrigadeTaskHoldSeconds.
        // Vanilla's "no stance write if already correct" semantics handle the
        // regimental-level case (EnsureStanceMatchesTask checks ai_stance ==
        // targetStance and skips if matching).

        // Resets between battles. Called from TacticalBattleCoordinatorRuntime.OnBattleEnd.
        internal static void ResetRegimentalControlForBattleEnd()
        {
            _artilleryLastActionRealtimeSeconds.Clear();
        }

        private static void ApplyRegimentalIntelligence(AIBattle battle)
        {
            if (battle == null) return;
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            if (bunits == null) return;

            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            if (side < 0) return;
            int allianceId = SafeAllianceForSide(bunits, side);
            if (allianceId < 0 || allianceId >= 2) return;

            var snapshot = TacticalDecisionSnapshot.Current;
            if (snapshot == null || snapshot == TacticalDecisionSnapshot.Empty) return;
            if (snapshot.BattleSequence != TacticalBattleCoordinator.BattleSequenceForParity) return;

            var allUnits = BattleUnits.completeunitlist as System.Collections.IList;
            if (allUnits == null) return;

            bool writesOn = Plugin.EnableRegimentalIntelligenceWrites != null
                && Plugin.EnableRegimentalIntelligenceWrites.Value;

            float nowReal = SafeRealtimeSeconds();

            // Friendly-fire detection telemetry (2026-05-21). Builds one
            // friendly snapshot for the alliance, then per-regiment checks
            // whether a friendly is in the fire corridor toward the regiment's
            // in-range enemy. Telemetry only — emits TacticalFriendlyFireBlocked.
            // Single walk before the per-regiment loop so we avoid N² cost.
            bool fireDetectionOn = Plugin.EnableFriendlyFireDetection != null
                && Plugin.EnableFriendlyFireDetection.Value;
            List<WhiskeyRealism.Tactical.TacticalFriendlyPathBlockingGate.FriendlySnapshot> friendlyFireSnapshot = null;
            if (fireDetectionOn)
            {
                friendlyFireSnapshot = BuildFriendlySnapshotForFireDetection(allUnits, allianceId);
            }

            for (int i = 0; i < allUnits.Count; i++)
            {
                var reg = allUnits[i] as Regiment;
                if (reg == null) continue;
                if (reg.alliance != allianceId) continue;

                int instanceId;
                try { instanceId = reg.GetInstanceID(); }
                catch { continue; }
                if (instanceId == 0) continue;

                bool isDeadOrRouted = false;
                try { isDeadOrRouted = reg.isrouted || reg.markedforrout || reg.permanentlydetached; }
                catch { isDeadOrRouted = true; }

                // Friendly-fire detection runs for every non-routed own regiment,
                // independent of the regimental decision pipeline.
                if (fireDetectionOn && friendlyFireSnapshot != null && !isDeadOrRouted)
                {
                    TryEmitFriendlyFireBlocked(reg, side, instanceId, friendlyFireSnapshot);
                }

                if (isDeadOrRouted) continue;

                TacticalRegimentalDecision decision;
                bool hasDecision = snapshot.TryGetRegimentalDecision(allianceId, instanceId, out decision);
                if (!hasDecision || decision.Action == RegimentalAction.NoAction) continue;

                // Respect player W&L command — never override. Emit telemetry
                // for diagnostic visibility but don't write.
                bool playerCommanded = false;
                try { playerCommanded = reg.dlcw_isundercommander; } catch { playerCommanded = true; }
                if (playerCommanded)
                {
                    EmitRegimentalIntelTelemetry(side, reg, decision);
                    continue;
                }

                EmitRegimentalIntelTelemetry(side, reg, decision);

                if (!writesOn) continue;

                // Scope B refactor (2026-05-22): write via the canonical
                // vanilla/W&L pipeline. ApplyRegimentalAction now calls
                // ChangeStance(reg, defend) BEFORE SetWaypoint so vanilla
                // micro-AI executes the withdrawal CORRECTLY. No claim-set
                // tracking — stance write IS the standing order, vanilla
                // ai_stanceordered carries the intent across cycles.
                try
                {
                    ApplyRegimentalAction(bunits, reg, decision);
                    EmitRegimentalControlEvent(side, reg, instanceId,
                        "applied", decision.Action.ToString(), decision.Reason);
                }
                catch (Exception aex)
                {
                    OnceLog.Warning(
                        "tactical-regimental-intelligence:write-failed:" + instanceId,
                        "ApplyRegimentalAction failed for " + SafeRegimentName(reg)
                        + ": " + aex.GetType().Name + " " + aex.Message);
                }
            }
        }

        // Emits a TacticalRegimentalControl event per claim/release transition.
        // NOT routed through OnceLog — recurring transitions for the same regiment
        // are valuable diagnostic signal (thrashing detection, etc). Telemetry
        // dedup is keyed on (transition, reason) at the sink layer so a
        // genuinely-different action/reason will surface.
        private static void EmitRegimentalControlEvent(int side, Regiment reg, int instanceId, string transition, string action, string reason)
        {
            try
            {
                string unit = SafeRegimentName(reg);
                string sig = "TacticalRegimentalControl|side=" + side
                    + "|unit=" + unit
                    + "|id=" + instanceId
                    + "|transition=" + transition
                    + "|action=" + action
                    + "|reason=" + reason;
                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Decision,
                    "TacticalRegimentalControl",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(side)
                        .WithUnit(unit)
                        .WithDecision(transition, reason, sig)
                        .WithField("instanceId", instanceId)
                        .WithField("transition", transition)
                        .WithField("action", action)
                        .WithField("reason", reason));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-regimental-control:telemetry-failed",
                    "EmitRegimentalControlEvent threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // Executes the regimental-intelligence write for a single regiment.
        // FallBack → SetWaypoint 100m behind the regiment along the front bearing.
        // RetreatToBrigade → SetWaypoint 200m behind the front bearing.
        // WheelToLeftFlank / WheelToRightFlank / WheelToRear → SetGroupFormation
        // with manualfinalrotation set to face the threat direction.
        //
        // Targets are derived from the threat assessment's FrontBearingDegrees so
        // we don't need to look up the brigade rally point (which would require
        // walking command hierarchy). Falls-back targets behind the enemy bearing
        // are a reasonable approximation of "toward friendly lines" — enemies
        // are in front, friendlies are behind.
        private static void ApplyRegimentalAction(
            BattleUnits bunits,
            Regiment reg,
            TacticalRegimentalDecision decision)
        {
            if (bunits == null || reg == null) return;
            var threats = decision.Threats;
            float bearingDeg = threats.FrontBearingDegrees;

            switch (decision.Action)
            {
                case RegimentalAction.FallBack:
                case RegimentalAction.RetreatToBrigade:
                    {
                        // Move directly away from the enemy centroid.
                        float fallbackMeters = decision.Action == RegimentalAction.FallBack ? 100f : 200f;
                        UnityEngine.Vector3 target = OffsetByBearing(
                            reg.transform.position,
                            bearingDeg + 180f,  // reverse bearing = away from enemy
                            fallbackMeters);
                        // Friendly-path blocking gate (2026-05-21). Same Scourge-ported
                        // gate the brigade-posture SetWaypoint uses — skip the write
                        // if the proposed fallback target would cross/overlap a
                        // friendly's path. Gate fail-open via try/catch; flag default OFF.
                        if (Plugin.EnableFriendlyPathBlockingGate != null
                            && Plugin.EnableFriendlyPathBlockingGate.Value
                            && IsRegimentalWaypointBlocked(reg, target))
                        {
                            OnceLog.Info(
                                "tactical-regimental-intelligence:path-blocked:" + reg.GetInstanceID(),
                                "[TacticalRegimentalIntelligence] friendly-path-blocked: "
                                + decision.Action + " waypoint refused for "
                                + SafeRegimentName(reg) + " (unit holds; vanilla AI may resume)");
                            return;
                        }
                        // Scope B refactor (2026-05-22): set defensive stance
                        // BEFORE the waypoint so vanilla micro-AI knows this is
                        // a withdrawal, not an advance. Stance 2 = defend.
                        WhiskeyRealism.Util.TacticalStanceWriter.EnsureStance(
                            bunits, reg, 2, contextTag: "regimental-fallback:" + SafeRegimentName(reg));
                        bunits.SetWaypoint(
                            reg,
                            target,
                            newpath: true,
                            doublequick: decision.Action == RegimentalAction.RetreatToBrigade,
                            manualfinalrotation: -1f,
                            modifylastwaypoint: false,
                            useorderdelay: false,
                            timetomove: -1f,
                            direction: -1,
                            showmovementoptions: false,
                            ignorebattlemonuments: false,
                            groupmoveonly: false,
                            ignoredisabledships: false,
                            checkforreadiness: true,
                            clearinterruptionpaths: true);
                    }
                    break;
                case RegimentalAction.WheelToLeftFlank:
                case RegimentalAction.WheelToRightFlank:
                case RegimentalAction.WheelToRear:
                    {
                        float rotationDeg = bearingDeg;
                        if (decision.Action == RegimentalAction.WheelToLeftFlank) rotationDeg = bearingDeg - 90f;
                        else if (decision.Action == RegimentalAction.WheelToRightFlank) rotationDeg = bearingDeg + 90f;
                        else if (decision.Action == RegimentalAction.WheelToRear) rotationDeg = bearingDeg + 180f;
                        while (rotationDeg < 0f) rotationDeg += 360f;
                        while (rotationDeg >= 360f) rotationDeg -= 360f;
                        bunits.SetGroupFormation(
                            reg,
                            reg.formationordered,
                            manualfinalrotation: rotationDeg,
                            targetpos: default(UnityEngine.Vector3),
                            immediateplacement: false,
                            newpath: true,
                            modifylastwaypoint: false,
                            newstate: 2,
                            refuseflank: -1);
                    }
                    break;
            }
        }

        // Artillery audit state (2026-05-21). Per-instance timestamp of the
        // last accepted artillery action. Reused across BGS Apply calls for
        // the same battle (cleared on OnBattleEnd via
        // ResetRegimentalControlForBattleEnd). Min-hold gating prevents
        // thrash when doctrine output oscillates near thresholds (e.g.
        // Retreat ↔ Fire when enemy is at panic boundary).
        private static readonly Dictionary<int, float> _artilleryLastActionRealtimeSeconds = new Dictionary<int, float>();

        // All-stance artillery audit. Walks completeunitlist filtered by alliance
        // + unittyp==2, evaluates TacticalArtilleryMicroDoctrine per unit,
        // applies the action through TacticalArtilleryApplier. Honors per-unit
        // hold-time + standard player-protection / routed gates.
        //
        // Cost: one full completeunitlist walk per Apply call (gated by BGS's
        // 1.5s cooldown). Per-artillery doctrine call is O(1) + a small
        // friendlies count loop. Comparable to ApplyRegimentalIntelligence.
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

            float nowReal = SafeRealtimeSeconds();
            float minHold = Plugin.ArtilleryAuditMinHoldSeconds != null
                ? Plugin.ArtilleryAuditMinHoldSeconds.Value
                : 3.0f;

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

                // Per-unit hold gate. If we acted on this unit < minHold seconds
                // ago, don't act again. Lets the prior vanilla write settle.
                float lastActionAt;
                if (_artilleryLastActionRealtimeSeconds.TryGetValue(instanceId, out lastActionAt)
                    && nowReal - lastActionAt < minHold)
                {
                    continue;
                }

                // W&L player command: never override. Same contract as the
                // regimental intelligence layer.
                bool playerCommanded;
                try { playerCommanded = unit.dlcw_isundercommander; }
                catch { playerCommanded = true; }
                if (playerCommanded) continue;

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
                float ammoRatio = 1f;
                try { ammoRatio = SafeArtilleryAmmoRatio(unit); } catch { ammoRatio = 1f; }
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
                    _artilleryLastActionRealtimeSeconds[instanceId] = nowReal;
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

        private static float SafeArtilleryAmmoRatio(Regiment unit)
        {
            // Vanilla GT exposes `Regiment.ammo` as a float[] (one entry per
            // ammo type). Sum all entries and treat the result as the
            // remaining-rounds count. Without a known maximum, we approximate
            // ratio against a per-gun nominal load (vanilla artillery typically
            // ships with 120-200 rounds per gun); we use `guns * 200` as the
            // denominator floor. Ratio is clamped to [0, 1].
            try
            {
                if (unit.ammo == null || unit.ammo.Length == 0) return 1f;
                float total = 0f;
                for (int i = 0; i < unit.ammo.Length; i++) total += unit.ammo[i];
                float nominalMax = unit.guns > 0 ? unit.guns * 200f : 800f;
                float ratio = total / nominalMax;
                if (ratio < 0f) ratio = 0f;
                if (ratio > 1f) ratio = 1f;
                return ratio;
            }
            catch { return 1f; }
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

        // Friendly snapshot built once per ApplyRegimentalIntelligence call
        // for the fire-detection pass. Walks the already-acquired completeunitlist
        // (no extra Unity API beyond what BGS already does) filtered to this
        // alliance. Captures position only — fire detection doesn't need
        // waypoints or formation.
        private static List<WhiskeyRealism.Tactical.TacticalFriendlyPathBlockingGate.FriendlySnapshot> BuildFriendlySnapshotForFireDetection(
            System.Collections.IList allUnits,
            int allianceId)
        {
            var list = new List<WhiskeyRealism.Tactical.TacticalFriendlyPathBlockingGate.FriendlySnapshot>(64);
            for (int i = 0; i < allUnits.Count; i++)
            {
                var reg = allUnits[i] as Regiment;
                if (reg == null) continue;
                if (reg.alliance != allianceId) continue;
                if (reg.isrouted || reg.markedforrout || reg.permanentlydetached) continue;
                int id;
                try { id = reg.GetInstanceID(); } catch { continue; }
                if (id == 0) continue;
                var pos = reg.transform.position;
                list.Add(new WhiskeyRealism.Tactical.TacticalFriendlyPathBlockingGate.FriendlySnapshot(
                    id, pos.x, pos.z, 0f, 0f, hasWaypoint: false, isLineFormation: false));
            }
            return list;
        }

        // Per-regiment fire-block telemetry emit. Reads the regiment's nearest
        // in-fire-range enemy via vanilla's unitrange.enemyinfirerangereg (the
        // same field vanilla uses for its own fire decisions). Excludes self
        // from the friendly snapshot via the snapshot's instanceId filter.
        // Bounded by OnceLog dedup keyed on (instanceId, blockerId) so the
        // same fire-block doesn't spam during a sustained engagement.
        private static void TryEmitFriendlyFireBlocked(
            Regiment reg,
            int side,
            int instanceId,
            List<WhiskeyRealism.Tactical.TacticalFriendlyPathBlockingGate.FriendlySnapshot> friendlies)
        {
            try
            {
                Regiment enemy = null;
                try
                {
                    var enemyList = reg.unitrange?.enemyinfirerangereg;
                    if (enemyList != null)
                    {
                        // First valid entry — vanilla doesn't expose a "closest"
                        // index, but the list is small (typically 1-3 entries).
                        for (int i = 0; i < enemyList.Count; i++)
                        {
                            var candidate = enemyList[i];
                            if (candidate == null) continue;
                            if (candidate.isrouted || candidate.permanentlydetached) continue;
                            enemy = candidate;
                            break;
                        }
                    }
                }
                catch { enemy = null; }
                if (enemy == null) return;

                var myPos = reg.transform.position;
                var enemyPos = enemy.transform.position;

                // Build a filtered view that excludes self — pure scorer doesn't
                // know which entry is "me." Copy + filter is cheap (< 50 entries
                // typically).
                List<WhiskeyRealism.Tactical.TacticalFriendlyPathBlockingGate.FriendlySnapshot> filtered = friendlies;
                bool needsFilter = false;
                for (int i = 0; i < friendlies.Count; i++)
                {
                    if (friendlies[i].InstanceId == instanceId) { needsFilter = true; break; }
                }
                if (needsFilter)
                {
                    filtered = new List<WhiskeyRealism.Tactical.TacticalFriendlyPathBlockingGate.FriendlySnapshot>(friendlies.Count - 1);
                    for (int i = 0; i < friendlies.Count; i++)
                    {
                        if (friendlies[i].InstanceId != instanceId) filtered.Add(friendlies[i]);
                    }
                }

                var result = WhiskeyRealism.Tactical.TacticalFriendlyFireGate.Evaluate(
                    myPos.x, myPos.z,
                    enemyPos.x, enemyPos.z,
                    filtered);
                if (!result.Blocked) return;

                string unit = SafeRegimentName(reg);
                string enemyName;
                try { enemyName = ((UnityEngine.Object)((UnityEngine.Component)enemy).gameObject).name ?? "<noname>"; }
                catch { enemyName = "<error>"; }
                string sig = "TacticalFriendlyFireBlocked|side=" + side
                    + "|unit=" + unit
                    + "|id=" + instanceId
                    + "|enemy=" + enemyName
                    + "|blocker=" + result.BlockerInstanceId;

                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Gate,
                    "TacticalFriendlyFireBlocked",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(side)
                        .WithUnit(unit)
                        .WithDecision("Blocked", result.Reason, sig)
                        .WithField("instanceId", instanceId)
                        .WithField("enemyName", enemyName)
                        .WithField("blockerInstanceId", result.BlockerInstanceId)
                        .WithField("blockerDistanceFromLine", result.BlockerDistanceFromLine)
                        .WithField("reason", result.Reason));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-friendly-fire-detection:exception",
                    "[TacticalFriendlyFireGate] TryEmitFriendlyFireBlocked threw: "
                    + ex.GetType().Name + " " + ex.Message);
            }
        }

        // Regimental-side wrapper around the friendly-path blocking gate. Walks
        // completeunitlist filtered by alliance, builds a FriendlySnapshot list,
        // calls the pure gate scorer. Used by ApplyRegimentalAction's
        // FallBack/Retreat paths so regimental waypoints honor the same Scourge
        // crossing-avoidance contract as brigade-posture writes.
        private static bool IsRegimentalWaypointBlocked(Regiment reg, UnityEngine.Vector3 target)
        {
            try
            {
                if (reg == null) return false;
                int allianceId = reg.alliance;
                if (allianceId < 0) return false;
                int selfId = 0;
                try { selfId = reg.GetInstanceID(); } catch { selfId = 0; }

                var units = BattleUnits.completeunitlist as System.Collections.IList;
                if (units == null) return false;
                var friendlies = new List<WhiskeyRealism.Tactical.TacticalFriendlyPathBlockingGate.FriendlySnapshot>(64);
                for (int i = 0; i < units.Count; i++)
                {
                    var other = units[i] as Regiment;
                    if (other == null) continue;
                    if (other.alliance != allianceId) continue;
                    if (other.isrouted || other.markedforrout || other.permanentlydetached) continue;
                    int oid;
                    try { oid = other.GetInstanceID(); } catch { continue; }
                    if (oid == 0 || oid == selfId) continue;
                    var op = other.transform.position;
                    UnityEngine.Vector3 wp = default(UnityEngine.Vector3);
                    bool hasWp = false;
                    try
                    {
                        wp = other.lastsetwaypointposition;
                        hasWp = (wp.x != 0f || wp.z != 0f);
                    }
                    catch { hasWp = false; }
                    int otherParentId = 0;
                    try { var p = other.groupaiobjectreg; if (p != null) otherParentId = p.GetInstanceID(); }
                    catch { otherParentId = 0; }
                    friendlies.Add(new WhiskeyRealism.Tactical.TacticalFriendlyPathBlockingGate.FriendlySnapshot(
                        oid, op.x, op.z, wp.x, wp.z, hasWp,
                        isLineFormation: false,
                        parentBrigadeInstanceId: otherParentId));
                }
                var myPos = reg.transform.position;
                int myParentId = 0;
                try { var p = reg.groupaiobjectreg; if (p != null) myParentId = p.GetInstanceID(); }
                catch { myParentId = 0; }
                var result = WhiskeyRealism.Tactical.TacticalFriendlyPathBlockingGate.Evaluate(
                    myPos.x, myPos.z,
                    target.x, target.z,
                    myIsLineFormation: false,
                    friendlies,
                    myParentBrigadeInstanceId: myParentId);
                return result.Blocked;
            }
            catch
            {
                return false;  // fail-open
            }
        }

        private static UnityEngine.Vector3 OffsetByBearing(UnityEngine.Vector3 from, float bearingDegrees, float distance)
        {
            float radians = bearingDegrees * 0.01745329252f; // PI / 180
            float dx = (float)System.Math.Sin(radians) * distance;
            float dz = (float)System.Math.Cos(radians) * distance;
            return new UnityEngine.Vector3(from.x + dx, from.y, from.z + dz);
        }

        // Player-protected check for the regimental path. Conservative — defaults
        // to "protected" on any exception so we never overwrite a player order.
        // W&L's dlcw_isundercommander marks units the player is actively commanding
        // through the W&L feud system; same field BattleCommandPostureExecutorPatch
        // checks at the brigade level.
        private static bool IsPlayerProtectedForRegiment(Regiment reg)
        {
            try
            {
                if (reg == null) return true;
                if (reg.dlcw_isundercommander) return true;
            }
            catch { return true; }
            return false;
        }

        private static string SafeRegimentName(Regiment reg)
        {
            try { return ((UnityEngine.Object)((UnityEngine.Component)reg).gameObject).name ?? "<noname>"; }
            catch { return "<error>"; }
        }

        // Telemetry per non-NoAction decision. Bounded by once-log dedup keyed on
        // (instanceId, action) so the same unit doesn't spam if it sits in the
        // same flanked-state across many cycles.
        private static void EmitRegimentalIntelTelemetry(int side, Regiment reg, TacticalRegimentalDecision decision)
        {
            try
            {
                int instanceId;
                try { instanceId = reg.GetInstanceID(); } catch { instanceId = 0; }
                string unit = SafeRegimentName(reg);
                var threats = decision.Threats;
                string sig = "TacticalRegimentalIntel|side=" + side
                    + "|unit=" + unit
                    + "|id=" + instanceId
                    + "|action=" + decision.Action
                    + "|reason=" + decision.Reason
                    + "|front=" + threats.FrontThreat.ToString("0")
                    + "|left=" + threats.LeftFlankThreat.ToString("0")
                    + "|right=" + threats.RightFlankThreat.ToString("0")
                    + "|rear=" + threats.RearThreat.ToString("0")
                    + "|pressed=" + threats.PressedCount
                    + "|flanked=" + threats.IsFlanked
                    + "|isolated=" + threats.IsIsolated;

                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Decision,
                    "TacticalRegimentalIntel",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(side)
                        .WithUnit(unit)
                        .WithDecision(decision.Action.ToString(), decision.Reason, sig)
                        .WithField("instanceId", instanceId)
                        .WithField("frontThreat", threats.FrontThreat)
                        .WithField("leftFlankThreat", threats.LeftFlankThreat)
                        .WithField("rightFlankThreat", threats.RightFlankThreat)
                        .WithField("rearThreat", threats.RearThreat)
                        .WithField("pressedCount", threats.PressedCount)
                        .WithField("isFlanked", threats.IsFlanked)
                        .WithField("isIsolated", threats.IsIsolated)
                        .WithField("totalEnemiesInRange", threats.TotalEnemiesInRange)
                        .WithField("frontBearingDegrees", threats.FrontBearingDegrees));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-regimental-intelligence:telemetry-failed",
                    "EmitRegimentalIntelTelemetry threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // Pre-computed decision read path.
        //
        // Three-tier lookup per Regiment:
        //   1. _resolvedDecisionsBySide (TacticalBattleCoordinatorRuntime
        //      cache, populated at heavy-gate-fire) — full doctrine + ledger
        //      + charge + scorer resolution baked in. Apply path is O(1):
        //      dict lookup + a few cheap safety gates + one ChangeStance.
        //      This is the "cheap per-group" hot path.
        //   2. Worker snapshot (TacticalDecisionSnapshot.Current) — partial
        //      pre-compute from the worker thread. Still calls the doctrine
        //      and ledger resolvers on main at apply time, but skips the
        //      live BuildGroupSector + DecideGroupStance recompute.
        //   3. ApplyGroup (legacy) — full resolver + sector rebuild + scorer
        //      call. Runs on cache miss (worker disabled, pre-bootstrap,
        //      gate hasn't fired yet, suppressed side, etc).
        //
        // Returns true whenever we owned the iteration (touched at least
        // one group); the postfix then short-circuits because the caller
        // already iterated all groups via one of the three branches.
        private static bool TryApplyFromDecisionSnapshot(AIBattle battle)
        {
            try
            {
                int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
                int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
                var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
                var units = SafeList(battle, ref _unitsUsedField, "unitsused");
                if (side < 0 || macro < 0 || bunits == null || units == null) return false;

                int allianceId = SafeAllianceForSide(bunits, side);
                if (allianceId < 0 || allianceId >= 2) return false;

                bool workerOn = Plugin.EnableTacticalOrchestratorWorker != null
                    && Plugin.EnableTacticalOrchestratorWorker.Value;
                TacticalDecisionSnapshot snapshot = workerOn ? TacticalDecisionSnapshot.Current : null;
                bool snapshotHasData = snapshot != null && snapshot != TacticalDecisionSnapshot.Empty;

                bool touchedAny = false;
                for (int i = 0; i < units.Count; i++)
                {
                    var group = units[i] as Regiment;
                    if (group == null) continue;
                    if (!TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(group.unittyp)) continue;

                    int instanceId;
                    try { instanceId = group.GetInstanceID(); }
                    catch { continue; }

                    // Tier 1: cheap per-group cache. Populated on every
                    // heavy-gate fire in DriveTickCycle; covers every
                    // commandable Regiment for the firing side.
                    TacticalResolvedGroupDecision resolved;
                    if (TacticalBattleCoordinator.TryGetResolvedGroupDecision(allianceId, instanceId, out resolved))
                    {
                        ApplyResolvedDecisionFast(bunits, side, macro, group, resolved);
                        touchedAny = true;
                        continue;
                    }

                    // Tier 2: worker snapshot — partial pre-compute.
                    if (snapshotHasData)
                    {
                        TacticalGroupStanceDecision cachedDecision;
                        TacticalSectorAssessment cachedSector;
                        if (snapshot.TryGetGroupStance(allianceId, instanceId, out cachedDecision)
                            && snapshot.TryGetGroupSector(allianceId, instanceId, out cachedSector))
                        {
                            ApplyCachedDecision(bunits, side, macro, group, cachedDecision, cachedSector);
                            touchedAny = true;
                            continue;
                        }
                    }

                    // Tier 3: legacy decide-and-write path.
                    ApplyGroup(bunits, side, macro, group, i);
                    touchedAny = true;
                }

                return touchedAny;
            }
            catch
            {
                return false;
            }
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
                    // Agreement: refresh the single-owner hold so vanilla's
                    // AdjustGroupAIStance does not flip the group next cycle.
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

        /// <summary>
        /// Apply-cached fast path (gap-fill chunk 1). Replicates
        /// <see cref="ApplyGroup"/>'s sequence but reads the sector +
        /// default-scorer decision from the worker snapshot:
        /// 1. WlAllowsControl + OrderFrictionAllowsChange enforced on main
        ///    (require live Regiment state).
        /// 2. TryApplyDoctrineStance using cached sector.
        /// 3. Charge (vanillaOrdered == 4) branch — unchanged.
        /// 4. TryApplyLedgerTaskStance using cached sector.
        /// 5. Fallthrough: write cached default-scorer decision.
        /// </summary>
        private static void ApplyCachedDecision(
            BattleUnits bunits,
            int side,
            int macro,
            Regiment group,
            TacticalGroupStanceDecision decision,
            TacticalSectorAssessment sector)
        {
            if (!WlAllowsControl(group)) return;
            if (!OrderFrictionAllowsChange(group)) return;

            int vanillaOrdered = SafeIntField(group, ref _orderedStanceField, "ai_" + "stanceordered", group.ai_stanceordered);

            if (TryApplyDoctrineStance(bunits, side, macro, group, vanillaOrdered, sector))
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

            if (TryApplyLedgerTaskStance(bunits, side, macro, group, vanillaOrdered, sector))
                return;

            // Fallthrough — apply the worker's default-scorer decision.
            // We do NOT call DecideGroupStance here; the worker already
            // produced this decision on its thread.
            if (!Plugin.Instance.EnableTacticalGroupSectorStance.Value) return;
            if (decision.Kind != TacticalDoctrineDecisionKind.Apply) return;
            if (decision.GroupStance < 1 || decision.GroupStance > 3) return;

            if (TacticalStanceWriter.EnsureStance(bunits, group, decision.GroupStance, null, StanceWriteSource.Scorer))
                LogDecision(side, group, sector, decision);
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

            if (TryApplyDoctrineStance(bunits, side, macro, group, vanillaOrdered, sector))
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

            if (TryApplyLedgerTaskStance(bunits, side, macro, group, vanillaOrdered, sector))
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
            int vanillaOrdered,
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

        private static bool TryApplyLedgerTaskStance(BattleUnits bunits, int side, int macro, Regiment group, int vanillaOrdered, TacticalSectorAssessment sector)
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
            if (!TacticalStanceWriter.EnsureStance(bunits, group, 3, null, StanceWriteSource.Scorer)) return;

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

        private static GameObject UnityObject(Regiment unit)
        {
            try
            {
                return unit != null ? ((Component)unit).gameObject : null;
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
