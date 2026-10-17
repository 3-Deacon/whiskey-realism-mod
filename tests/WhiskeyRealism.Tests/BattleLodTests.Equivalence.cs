using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using static TestHarness;

// BC15 decision 5: the tiered producers' decision for a group is a function of the inputs the tier pass covers, so a run with the tiers and a run
// without them execute the same orders at the same ticks. The reference producer below is exactly such a function (a move to the order's target
// or the layout slot unless the path corridor is blocked, a facing toward the closest visible enemy, a hold stance, an order formation); it
// stands where the posture, facing-pulse and stance producers stand, and the real arbiter, the real tier pass and the real executor plan run
// around it in the order BattleCoreTick runs them. One input changes per scripted case. The stance channel is never a duplicate (it is a
// hold refresh), so it is compared as the sequence of values, not by count; DistantIntervalKeepsTheStanceHoldAlive covers its cadence.
internal static partial class BattleLodTests
{
    private static (string, Action)[] EquivalenceTests()
    {
        return new (string, Action)[]
        {
            ("bc15 EQUIV SteadyDistantGroupExecutesSameOrdersAsFullRate", () => AssertEquivalent(null, "steady", expectMoves: 0)),
            ("bc15 EQUIV (1) the leaf brigade's parent division moves 300 m", () => AssertEquivalent(s => s.MoveDivision(300f), "parent", expectMoves: 3)),
            ("bc15 EQUIV (2) the group's order target moves 200 m with the same task", () => AssertEquivalent(s => s.MoveTarget(101, 200f), "target", expectMoves: 1)),
            ("bc15 EQUIV (3) a visible enemy column far away crosses laterally", () => AssertEquivalent(s => s.EnemyCrosses(), "bearing", expectFacing: true)),
            ("bc15 EQUIV (4) a friendly brigade leaves the group's path corridor", () => AssertEquivalent(s => s.ClearCorridor(), "corridor", expectMoves: 1)),
            ("bc15 EQUIV (5) the operation changes its primary objective under an unchanged order", () => AssertEquivalent(s => s.MoveObjective(300f), "objective", expectMoves: 1)),
            ("bc15 EQUIV (6) a contact at tick 17", () => AssertEquivalent(s => s.Contact(), "contact", expectFacing: true, scriptTick: 17)),
            ("bc15 EQUIV an exploit launch and a planned release promote on the tick they change the target", ExploitAndReleaseCases),
            ("bc15 EQUIV the tiers save evaluations: producer work with and without them", SavedEvaluations),
        };
    }

    private sealed class EquivSim
    {
        public readonly LodWorld W = new LodWorld();
        public readonly OrderArbiter Arbiter = new OrderArbiter();
        public readonly bool Lod;
        public readonly List<string> Calls = new List<string>();
        public readonly List<string> Stances = new List<string>();
        public int Evaluations, Tick;
        private readonly BattleOrderIntent[] _winners = new BattleOrderIntent[512];
        private readonly BattleOrderIntent[] _carried = new BattleOrderIntent[512];
        private readonly OrderOutcome[] _outcomes = new OrderOutcome[64];
        private readonly Dictionary<int, string> _lastStance = new Dictionary<int, string>();
        private readonly List<(int id, float x, float z, int arrive)> _travel = new List<(int, float, float, int)>();

        public EquivSim(bool lod)
        {
            Lod = lod;
            W.Executor.Bind(new OpenGuard(), null);
            W.Executor.SetMapSize(20000f, 20000f);
            // division 50 with four pool brigades (100-103) and two leaf brigades (104, 105) that stand in slots behind it.
            W.Division(50, 0, 5000f, 1000f);
            for (int b = 0; b < 4; b++)
            {
                W.QuietBrigade(100 + b, 3600f + b * 500f, 1000f, 50);
                W.Tasks.Target[100 + b] = (3600f + b * 500f, 1500f, -1f, -1);   // each marches 500 m to its hold point, then holds
            }
            W.Tasks.Target[50] = (5000f, 1000f, -1f, -1);
            for (int b = 4; b < 6; b++)
            {
                W.QuietBrigade(100 + b, 3000f + (b - 4) * 3800f, 700f, 50);
                W.Tasks.Slot[100 + b] = (3000f + (b - 4) * 3800f, 700f, 0f);
            }
            // Brigade 102 would march on, but a friendly brigade stands in its path; brigade 103 holds the operation's objective, its order carries no target.
            // (The brigades' own regiments are not corridor traffic in this fixture: they stand at the end of their own corridor.)
            W.Corridor.Friendly.Clear();
            W.Corridor.Friendly.Add((7001, 4600f, 1250f));
            W.Tasks.Target.Remove(103);
            W.Tasks.HasObjective = true;
            W.Tasks.ObjectiveGroupId = 103;
            W.Tasks.ObjectiveX = 5100f;
            W.Tasks.ObjectiveZ = 1000f;
            W.Unit(102).LastWaypointX = 0f;
        }

        public void MoveDivision(float meters)
        {
            W.Tasks.Target[50] = (5000f, 1000f + meters, -1f, -1);
            for (int b = 4; b < 6; b++)
            {
                var slot = W.Tasks.Slot[100 + b];
                W.Tasks.Slot[100 + b] = (slot.x, slot.z + meters, slot.heading);
            }
        }

        public void MoveTarget(int group, float meters)
        {
            var t = W.Tasks.Target[group];
            W.Tasks.Target[group] = (t.x, t.z + meters, t.heading, t.formation);
        }

        public void EnemyCrosses()
        {
            if (W.IndexOfEnemy(9001) < 0) W.SeeEnemy(9001, 4900f, 4600f);
            W.MoveEnemy(9001, 6300f, 4600f);
        }

        public void ClearCorridor() => W.Corridor.Friendly.RemoveAll(f => f.id == 7001);

        public void MoveObjective(float meters) => W.Tasks.ObjectiveZ += meters;

        public void Contact()
        {
            W.SeeEnemy(9002, 3600f, 2700f);   // 1,200 m in front of brigade 100's hold point
        }

        public void Step(Action<EquivSim> script, int scriptTick)
        {
            W.Hours += W.CycleHours;
            if (script != null && Tick == scriptTick) script(this);
            W.Build();
            Arbiter.BeginTick();
            if (Lod)
            {
                W.Classify();
                for (int i = 0; i < W.S.OwnCount; i++)
                    if (!W.State.IsDue(i)) Arbiter.CarryForward(W.S.Own[i].InstanceId);
            }
            for (int i = 0; i < W.S.OwnCount; i++)
            {
                if ((W.S.Own[i].Flags & UnitFlags.IsGroup) == 0) continue;
                if (Lod && !W.State.IsDue(i)) continue;
                Reference(i);
            }
            int winners = Arbiter.Resolve(_winners);
            int carried = Arbiter.CarriedWinners(_carried);
            W.Executor.BeginTickClaims(W.S, _winners, winners, _carried, carried);
            W.Executor.ObserveOutcomes(W.S, W.Hours, _outcomes, out _);
            for (int w = 0; w < winners; w++)
            {
                ExecutionPlan plan = W.Executor.Plan(W.S, _winners[w], W.Hours);
                if (!plan.Execute)
                {
                    W.Executor.NoteRefusal(_winners[w].GroupId, plan.Skip);
                    continue;
                }
                Execute(in _winners[w]);
            }
            W.Executor.EndTickClaims();
            Arrive();
            Tick++;
        }

        private void Reference(int i)
        {
            ref UnitRecord rec = ref W.S.Own[i];
            int id = rec.InstanceId;
            Evaluations++;
            if (W.Tasks.TryTarget(id, out float tx, out float tz, out _, out int formation))
            {
                if (PostureSense.Distance(rec.X, rec.Z, tx, tz) > BattleLod.TargetMoveMeters)
                {
                    W.Corridor.Signature(id, rec.X, rec.Z, tx, tz, BattleLod.CorridorWidthMeters, out int blockers);
                    if (blockers == 0) Arbiter.Submit(Intent(id, OrderChannel.Move, IntentSource.PostureExecutor, IntentPriority.Executor, tx, tz, 0, -1f));
                }
                if (formation >= 0 && rec.Formation != formation)
                    Arbiter.Submit(Intent(id, OrderChannel.Formation, IntentSource.PostureExecutor, IntentPriority.Executor, 0f, 0f, formation, -1f));
            }
            else if (W.Tasks.TrySlot(i, out float sx, out float sz, out _) && PostureSense.Distance(rec.X, rec.Z, sx, sz) > BattleLod.TargetMoveMeters)
            {
                Arbiter.Submit(Intent(id, OrderChannel.Move, IntentSource.PostureExecutor, IntentPriority.Executor, sx, sz, 0, -1f));
            }
            int k = W.S.ClosestVisibleEnemy(i, out _);
            if (k >= 0)
            {
                float bearing = PostureSense.HeadingTo(rec.X, rec.Z, W.S.Observations[k].X, W.S.Observations[k].Z);
                float turn = Math.Abs(rec.RotationY - bearing) % 360f;
                if ((turn > 180f ? 360f - turn : turn) > BattleLod.TurnDegrees)
                    Arbiter.Submit(Intent(id, OrderChannel.Facing, IntentSource.FacingPulse, IntentPriority.Scorer, 0f, 0f, 0, bearing));
            }
            // The hold stance is a refresh the executor never dedupes.
            Arbiter.Submit(Intent(id, OrderChannel.Stance, IntentSource.PostureExecutor, IntentPriority.Executor, 0f, 0f, 2, -1f));
        }

        private static BattleOrderIntent Intent(int id, OrderChannel channel, IntentSource source, IntentPriority priority, float x, float z, int value, float heading)
        {
            return new BattleOrderIntent
            {
                GroupId = id, Channel = channel, Source = source, Priority = priority, TargetX = x, TargetZ = z, Value = value, Heading = heading,
                Task = CommandTaskType.HoldObjective, Reason = "reference",
            };
        }

        private void Execute(in BattleOrderIntent intent)
        {
            LodUnit u = W.Unit(intent.GroupId);
            int paths = 0;
            switch (intent.Channel)
            {
                case OrderChannel.Move:
                    Calls.Add(Tick + "|" + intent.GroupId + "|move|" + Math.Round(intent.TargetX) + "|" + Math.Round(intent.TargetZ));
                    u.LastWaypointX = intent.TargetX;
                    u.LastWaypointZ = intent.TargetZ;
                    SetPaths(u, 1);
                    _travel.Add((u.Id, intent.TargetX, intent.TargetZ, Tick + 3));
                    paths = 1;
                    break;
                case OrderChannel.Facing:
                    Calls.Add(Tick + "|" + intent.GroupId + "|facing|" + Math.Round(intent.Heading));
                    u.Rotation = intent.Heading;
                    break;
                case OrderChannel.Formation:
                    Calls.Add(Tick + "|" + intent.GroupId + "|formation|" + intent.Value);
                    u.Formation = intent.Value;
                    break;
                case OrderChannel.Stance:
                    string value = intent.Value.ToString();
                    if (!_lastStance.TryGetValue(intent.GroupId, out string last) || last != value) Stances.Add(Tick + "|" + intent.GroupId + "|stance|" + value);
                    _lastStance[intent.GroupId] = value;
                    u.AiStanceOrdered = intent.Value;   // vanilla's ChangeStance sets ai_stanceordered, which makes later intents a refresh
                    break;
            }
            W.Executor.RecordIssued(in intent, W.Hours, null, 0, null, 0, 0, paths);
        }

        private void SetPaths(LodUnit group, int paths)
        {
            group.RegimentPaths = paths;
            foreach (LodUnit child in W.Units) if (child.ParentId == group.Id && !child.IsGroup) child.RegimentPaths = paths;
        }

        private void Arrive()
        {
            for (int t = _travel.Count - 1; t >= 0; t--)
            {
                if (_travel[t].arrive != Tick) continue;
                LodUnit g = W.Unit(_travel[t].id);
                g.X = _travel[t].x;
                g.Z = _travel[t].z;
                int side = -1;
                foreach (LodUnit child in W.Units)
                {
                    if (child.ParentId != g.Id || child.IsGroup) continue;
                    child.X = g.X + side * 30f;
                    child.Z = g.Z;
                    side = 1;
                    int at = W.Corridor.Friendly.FindIndex(f => f.id == child.Id);
                    if (at >= 0) W.Corridor.Friendly[at] = (child.Id, child.X, child.Z);
                }
                SetPaths(g, 0);
                _travel.RemoveAt(t);
            }
        }
    }

    private static EquivSim Run(bool lod, Action<EquivSim> script, int scriptTick, int ticks = 40)
    {
        var sim = new EquivSim(lod);
        for (int t = 0; t < ticks; t++) sim.Step(script, scriptTick);
        return sim;
    }

    // The scripted input changes on tick scriptTick; a Distant group is due on one tick in four, so the case runs on four consecutive ticks
    // and a missing trigger shows on the phase where the group is asleep.
    private static void AssertEquivalent(Action<EquivSim> script, string label, int expectMoves = 0, bool expectFacing = false, int scriptTick = 20)
    {
        int phases = script == null ? 1 : BattleLod.IntervalTicks;
        for (int phase = 0; phase < phases; phase++)
        {
            int at = scriptTick + phase;
            string where = label + " (scripted at tick " + at + ")";
            EquivSim off = Run(false, script, at);
            EquivSim on = Run(true, script, at);
            AssertEqual(string.Join("\n", off.Calls), string.Join("\n", on.Calls), where + ": executed move, facing and formation calls, with and without the tiers");
            AssertEqual(string.Join("\n", off.Stances), string.Join("\n", on.Stances), where + ": the sequence of stance values");
            int movesAfter = 0, facingAfter = 0;
            foreach (string call in on.Calls)
            {
                string[] part = call.Split('|');
                if (int.Parse(part[0]) < at) continue;
                if (part[2] == "move") movesAfter++;
                if (part[2] == "facing") facingAfter++;
            }
            AssertTrue(movesAfter >= expectMoves, where + ": the scripted change produced at least " + expectMoves + " move(s) (" + movesAfter + ")");
            if (expectFacing) AssertTrue(facingAfter >= 1, where + ": the scripted change produced a facing order");
            AssertTrue(on.Evaluations < off.Evaluations, where + ": the tiers evaluated fewer groups (" + on.Evaluations + " against " + off.Evaluations + ")");
        }
    }

    private static void ExploitAndReleaseCases()
    {
        // An exploit launch (HasLatchOrManeuver turns true) and a planned release (the leaf task changes from ReserveWait to a hold elsewhere) on a
        // group that was Distant the tick before: both change what the producers read, so the group is Full that tick.
        foreach (string how in new[] { "exploit", "release" })
        {
            LodWorld w = DistantNotDueWorld();
            if (how == "exploit") w.Tasks.Latched.Add(100);
            else w.Tasks.Task[100] = CommandTaskType.ReleaseReserve;
            w.Tick();
            AssertEqual(LodTier.Full, w.Tier(100), how + ": the group is Full on the tick the target changes");
            AssertTrue(w.Due(100) && w.Due(1001), how + ": and its producers run, its regiments' too");
        }
        // The late wake-up: the opportunity producer launches the exploit after the tier pass, in the same tick.
        LodWorld late = DistantNotDueWorld();
        late.Tick();
        while (late.Due(100)) late.Tick();
        int index = late.S.IndexOfOwn(100);
        AssertFalse(late.State.IsDue(index), "precondition: not due");
        AssertTrue(late.State.PromoteNow(late.S, index, LodPromotion.Target, late.Hours), "the late promotion takes");
        AssertTrue(late.State.IsDue(index), "now due");
        AssertEqual(LodTier.Full, late.State.TierOf(index), "and Full");
        AssertFalse(late.State.PromoteNow(late.S, index, LodPromotion.Target, late.Hours), "a group that is already due is not promoted twice");
        late.Tick();
        AssertEqual(LodTier.Full, late.Tier(100), "it stays Full for the minimum hold");
    }

    private static void SavedEvaluations()
    {
        EquivSim off = Run(false, null, 99);
        EquivSim on = Run(true, null, 99);
        // 7 groups (a division and six brigades), 40 ticks. Groups are Full while they march and for the minimum hold after, then one tick in four.
        AssertEqual(7 * 40, off.Evaluations, "without the tiers every group is evaluated every tick");
        AssertTrue(on.Evaluations * 2 < off.Evaluations, "with them less than half as many (" + on.Evaluations + " against " + off.Evaluations + ")");
        Console.WriteLine("INFO bc15 EQUIV group evaluations over 40 ticks: without the tiers " + off.Evaluations + ", with them " + on.Evaluations
            + " (" + (100 * on.Evaluations / off.Evaluations) + " %)");
    }
}
