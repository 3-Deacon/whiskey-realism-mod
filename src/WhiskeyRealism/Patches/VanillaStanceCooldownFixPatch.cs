using System;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.BugFixes;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #119 VanillaStanceCooldownFixPatch (BUG-TAC-027) - Vanilla AIBattle.AdjustGroupAIStance (4221-4278) re-decides a pool group's
    // stance when lastaistancechangetime < GameVars.currenttimefromstart + GamePrefs.updatetimeforresettingofaistance (4264), a test
    // every past stamp passes, so a group's stance can flip every side-stat cycle although battleprefs (804-805) says to wait T
    // (1 game-hour) after a change. The comparison is left as it is (Whiskey's own stance hold depends on its form,
    // TacticalStanceOwnership); instead a re-stamp steer, for the groups vanilla itself changed in a call:
    //   - a non-skipping Prefix records each pool group's ai_stanceordered (and, on the first call with macroai 3, pulls back the
    //     stamps this fix wrote so vanilla's retreat block 4258 acts in that call);
    //   - a Postfix finds the groups whose ai_stanceordered vanilla changed in this call (stamp equal to the raw clock) and sets
    //     lastaistancechangetime = now + 2 x T, so vanilla skips them until now + T. No re-stamp while macroai is 3.
    // It acts on AI-run instances only (the W&L feud instance duplicates side 0's cycle and its stances are W&L's), in every
    // Commander Mode: a correction of a vanilla field, not an order. A Whiskey TacticalStanceWriter write after the Postfix (the
    // arbiter runs later in the macro cycle) overwrites the stamp, so Whiskey's own hold governs a Whiskey-owned group.
    // Effect: vanilla's weak-group defend and screen valves now wait up to T after vanilla's own previous change of a group.
    [HarmonyPatch(typeof(AIBattle), "AdjustGroupAIStance")]
    internal static class VanillaStanceCooldownFixPatch
    {
        private const double RowIntervalHours = 0.1;

        private static readonly StanceRestampBook[] Books = { new StanceRestampBook(), new StanceRestampBook() };
        private static readonly double[] NextRow = { double.NegativeInfinity, double.NegativeInfinity };
        private static readonly PoolStamps Stamps = new PoolStamps();

        static VanillaStanceCooldownFixPatch()
        {
            BattleScope.Register("vanilla-stance-cooldown-fix", Reset);
        }

        [HarmonyPrefix]
        internal static void Prefix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.stance-cooldown.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.stance-cooldown.prefix");
                try
                {
                    if (!Applies(__instance, out StanceRestampBook book, out int side)) return;
                    float now = GameVars.currenttimefromstart;
                    bool retreat = __instance.macroai == 3;
                    StanceCooldownPlan plan = StanceCooldownPlan.Decide(FixOn(), retreat);
                    if (plan.ReleaseOnRetreat)
                    {
                        Stamps.Bind(__instance);
                        int released = book.ReleaseOnRetreat(now, Stamps);
                        if (released > 0)
                            OnceLog.Info("tactical-battle-core:stance-cooldown-retreat", "[StanceCooldown] army retreat: released " + released + " restamped group(s) so vanilla's retreat block acts");
                    }
                    else if (!retreat) book.NoteNotRetreating();
                    var pool = __instance.unitsused;
                    if (pool == null) return;
                    BattleUnits bunits = BattleCoreTick.ResolveBattleUnits();
                    double doctrine = TacticalDoctrineTime.NowBattleHours;
                    float reset = GamePrefs.updatetimeforresettingofaistance;
                    for (int i = 0; i < pool.Count; i++)
                    {
                        Regiment group = pool[i];
                        if ((UnityEngine.Object)group == null) continue;
                        int id = group.GetInstanceID();
                        book.RecordBefore(id, group.ai_stanceordered);
                        if (!plan.Restamp) continue;
                        // A group whose re-stamp holds it: would vanilla's own chain change it now? That wait is the cost of the fix (smoke item 11).
                        if (book.TryWritten(id, out float written) && group.lastaistancechangetime == written &&
                            StanceCooldownRestamp.VanillaSkips(written, now, reset) && (UnityEngine.Object)bunits != null)
                        {
                            int wants = TacticalStanceWriter.VanillaGroupStance(__instance, bunits, group, __instance.macroai);
                            if ((wants == 1 || wants == 2) && wants != group.ai_stanceordered) book.NoteWant(id, doctrine);
                            else book.ClearWant(id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-battle-core:stance-cooldown-prefix-failed",
                        "[StanceCooldown] prefix failed; vanilla runs unchanged: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.stance-cooldown.postfix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.stance-cooldown.postfix");
                try
                {
                    if (!Applies(__instance, out StanceRestampBook book, out int side)) return;
                    StanceCooldownPlan plan = StanceCooldownPlan.Decide(FixOn(), __instance.macroai == 3);
                    var pool = __instance.unitsused;
                    if (pool == null) return;
                    float now = GameVars.currenttimefromstart;
                    float reset = GamePrefs.updatetimeforresettingofaistance;
                    double doctrine = TacticalDoctrineTime.NowBattleHours;
                    for (int i = 0; i < pool.Count; i++)
                    {
                        Regiment group = pool[i];
                        if ((UnityEngine.Object)group == null) continue;
                        int id = group.GetInstanceID();
                        if (!book.TryBefore(id, out int before) ||
                            !StanceCooldownRestamp.VanillaChanged(before, group.ai_stanceordered, group.lastaistancechangetime, now))
                            continue;
                        book.NoteVanillaChange(id, doctrine);
                        if (!plan.Restamp) continue;
                        book.ResolveWant(id, doctrine);
                        float stamp = StanceCooldownRestamp.Restamp(now, reset);
                        group.lastaistancechangetime = stamp;
                        book.NoteRestamp(id, stamp);
                        OnceLog.Info("tactical-battle-core:stance-cooldown-first", "[StanceCooldown] first vanilla stance hold restored");
                    }
                    EmitRowIfDue(side, book, doctrine, plan);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-battle-core:stance-cooldown-postfix-failed",
                        "[StanceCooldown] postfix failed; no re-stamp this call: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        // The patch serves AI-run instances only: not the W&L feud instance. The instance is counted (vanilla's own stance changes)
        // whatever the flag and the macro stance; only the re-stamp itself needs the flag on and no army retreat, so smoke item 2.50
        // can compare the change rate with the fix on and off.
        private static bool Applies(AIBattle battle, out StanceRestampBook book, out int side)
        {
            book = null;
            side = -1;
            if ((UnityEngine.Object)battle == null) return false;
            if (!TacticalBattleCoordinator.DrivesOrchestrator(battle.isplayeraiorfeud) || TacticalWriteGuard.IsFeudInstance(battle)) return false;
            side = battle.sideofai;
            if (side < 0 || side >= Books.Length) return false;
            book = Books[side];
            return true;
        }

        // A vanilla bug fix: its flag, in every Commander Mode (BattleFixGate).
        private static bool FixOn()
        {
            return BattleFixGate.Runs(BattleFixKind.StanceCooldown, Plugin.MasterEnabled(), Plugin.VanillaStanceCooldownFixEnabled(),
                TacticalWriteGuard.ModeAllowsWrites());
        }

        private static void EmitRowIfDue(int side, StanceRestampBook book, double doctrine, StanceCooldownPlan plan)
        {
            if (doctrine < NextRow[side]) return;
            NextRow[side] = doctrine + RowIntervalHours;
            if (!TelemetryRouter.ShouldEmit(TelemetryRouter.CurrentProfile, TelemetryLayer.Tactical, TelemetryCategory.State)) return;
            bool fix = plan.FixActing;
            string reason = plan.Reason;
            int changes = book.VanillaChanges, restamped = book.Restamped, maxPerHour = book.MaxChangesPerGroupHour, released = book.Released, held = book.RestampedCount;
            int d0 = book.DelayUnder025, d1 = book.Delay025To05, d2 = book.Delay05To1, d3 = book.DelayOver1;
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.State, "TacticalStanceCooldown", TelemetrySeverity.Info,
                ev => ev
                    .WithSide(side)
                    .WithField("fix", fix)
                    .WithField("reason", reason)
                    .WithField("vanillaChanges", changes)
                    .WithField("restamped", restamped)
                    .WithField("restampedHeld", held)
                    .WithField("maxChangesPerGroupHour", maxPerHour)
                    .WithField("released", released)
                    .WithField("delayUnder025", d0).WithField("delay025to05", d1).WithField("delay05to1", d2).WithField("delayOver1", d3)
                    .WithField("doctrineHours", doctrine));
        }

        private static void Reset()
        {
            for (int i = 0; i < Books.Length; i++)
            {
                Books[i].Clear();
                NextRow[i] = double.NegativeInfinity;
            }
            Stamps.Bind(null);
        }

        // lastaistancechangetime of the instance's pool groups, by instance id.
        private sealed class PoolStamps : IStanceStampAccess
        {
            private AIBattle _battle;

            public void Bind(AIBattle battle) => _battle = battle;

            public float Get(int groupId)
            {
                Regiment group = Find(groupId);
                return (UnityEngine.Object)group != null ? group.lastaistancechangetime : float.NaN;
            }

            public void Set(int groupId, float stamp)
            {
                Regiment group = Find(groupId);
                if ((UnityEngine.Object)group != null) group.lastaistancechangetime = stamp;
            }

            private Regiment Find(int groupId)
            {
                var pool = _battle != null ? _battle.unitsused : null;
                if (pool == null) return null;
                for (int i = 0; i < pool.Count; i++)
                    if ((UnityEngine.Object)pool[i] != null && pool[i].GetInstanceID() == groupId) return pool[i];
                return null;
            }
        }
    }
}
