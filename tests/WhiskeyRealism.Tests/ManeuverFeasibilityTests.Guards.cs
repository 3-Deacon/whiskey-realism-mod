using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static BattleMovementHonestyTests;
using static ManeuverFeasibilityFixtures;
using static CommanderOpportunityTests;

// BC14 verify round 1: the guards of the crossing release and the Defender's fog rules that had no test failing without them (F2 to F4, F6, F5).
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] GuardTests() => new (string, Action)[]
    {
        ("bc14 RELEASE-HOLD-PLAYER a bound guard whose subtree gains player-owned strength is released from its crossing", ReleaseHoldPlayer),
        ("bc14 RELEASE-HOLD-EXPLOIT a bound guard that becomes an exploit asset is released from its crossing", ReleaseHoldExploit),
        ("bc14 RELEASE-ONE-PER-CROSSING a crossing already guarded is not given a second guard", ReleaseOnePerCrossing),
        ("bc14 THREAT-KEYED-BY-ID the threat matrix is read by child id, not by position or count", ThreatKeyedById),
        ("bc14 DEF-NO-SAMPLES with no enemy perceived the assumed point counts crossings for the penalty and publishes none", DefNoSamples),
        ("bc14 ENEMY-SAMPLE-FILTER the enemy picture counts what EnemySamples.Accepts counts and nothing else", EnemySampleFilter),
        ("bc14 MARCH-HORSE-ARTILLERY a child with horse artillery marches at vanilla's pace (slowest base x Min(that, 1)), horse artillery at the limbered base times 2.5", MarchHorseArtillery),
        ("bc14 LOOK-DEF-FRONTAL a Defender also asks about an Unknown front crossing the perceived enemy reaches before dark, which the threat list does not carry", LookDefenderFrontal),
        ("bc14 LOOK-LIST-ORDER the Defender's look list is the crossings the enemy reaches before dark, earliest first, at most eight", LookListOrder),
        ("bc14 DEF-WORK-UNITS the Defender's picture is built in work units: no slice runs more than its budget, no unit costs more than one body's rides, the result equals a forced run", DefWorkUnits),
        ("bc14 DEF-TIME informational: a 60-crossing Defender field's whole picture and its largest work unit, in milliseconds", DefTime),
        ("bc14 FEAS-TICK-ORDER with both pending the tick runs the monitor first, so a wide Defender evaluation never starves it", FeasTickOrder),
        ("bc14 XING-OWN-COVER an own regiment covers a crossing by its facing and fire range; a routed, grouped, artillery or off-line one does not", XingOwnCover),
    };

    // c0 main, c1 and c2 reserves; c1 is bound to link 5 at its own shift 0.2 (the hold bound is 0.2 + 0.25 + 0.25 = 0.7) and the enemy is due at 0.6.
    static ReserveReleaseState BoundC1()
    {
        var state = new ReserveReleaseState();
        state.BindCrossing(new CrossingBinding { ChildId = "c1", Link = 5, OwnShiftAtRelease = 0.2, ReleasedAtHours = 9.9 });
        return state;
    }

    static void ReleaseHoldPlayer()
    {
        ReserveReleaseState.ResetAll();
        DirectChildEvidence[] ev = ResEvidence(1, 3);
        var threats = new[] { Crossing(5, true, 0.6f) };
        // Control: the guard stays while nothing changed.
        ReserveReleaseState held = BoundC1();
        IReadOnlyList<DirectChildIntent> intents = CrossAllocate(ev, held, 10.0, threats, Row(9f, 0.2f, 0.5f));
        AssertEqual(DirectChildRole.RefuseLeft, intents[1].Role, "control: the guard stays on its crossing");
        AssertTrue(held.TryCrossingOf("c1", out _), "control: and stays bound");
        // c1's subtree gained player-owned strength (its time is NaN): the binding ends and it is a reserve again.
        ReserveReleaseState player = BoundC1();
        intents = CrossAllocate(ev, player, 10.0, threats, Row(9f, float.NaN, 0.5f));
        AssertEqual(DirectChildRole.Reserve, intents[1].Role, "a child holding player-owned strength stops guarding");
        AssertFalse(player.TryCrossingOf("c1", out _), "and its binding ends");
    }

    static void ReleaseHoldExploit()
    {
        ReserveReleaseState.ResetAll();
        DirectChildEvidence[] ev = ResEvidence(1, 3);
        var threats = new[] { Crossing(5, true, 0.6f) };
        ReserveReleaseState exploit = BoundC1();
        IReadOnlyList<DirectChildIntent> intents = CrossAllocate(ev, exploit, 10.0, threats, Row(9f, 0.2f, 0.5f), exploiting: new[] { false, true, false });
        AssertTrue(intents[1].Role != DirectChildRole.RefuseLeft, "a guard that became an exploit asset leaves its crossing");
        AssertFalse(exploit.TryCrossingOf("c1", out _), "and its binding ends");
    }

    static void ReleaseOnePerCrossing()
    {
        ReserveReleaseState.ResetAll();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4);
        float[] shifts = Row(9f, 0.2f, 0.3f, 0.4f);
        var threats = new[] { Crossing(5, true, 0.4f) };
        var state = new ReserveReleaseState();
        IReadOnlyList<DirectChildIntent> intents = CrossAllocate(ev, state, 10.0, threats, shifts);
        AssertEqual(DirectChildRole.RefuseLeft, intents[1].Role, "the soonest reserve guards the crossing");
        AssertEqual(1, state.BoundCount, "one binding");
        // Allocations after it, with the guard holding: nobody else is sent to the same crossing.
        for (int run = 1; run <= 3; run++)
        {
            intents = CrossAllocate(ev, state, 10.0 + run * 0.05, threats, shifts);
            AssertEqual(DirectChildRole.RefuseLeft, intents[1].Role, "run " + run + ": the guard holds");
            AssertEqual(DirectChildRole.Reserve, intents[2].Role, "run " + run + ": the second reserve is not sent to the guarded crossing");
            AssertEqual(DirectChildRole.Reserve, intents[3].Role, "run " + run + ": nor the third");
        }
        AssertEqual(1, state.BoundCount, "still one binding");
    }

    sealed class MatrixLookup : IFeasibilityLookup
    {
        public string[] Columns;
        public float[] Times;
        public CrossingThreat Threat = Crossing(5, true, 0.4f);

        public BattlePlanId SelectedPlan => BattlePlanId.Unknown;
        public string BrokenAssumption => null;
        public bool TryFocalPoint(out FocalPoint focal) { focal = default(FocalPoint); return false; }
        public bool WorksHold(out string reason) { reason = null; return false; }
        public bool AssaultsWorks(string childId) => false;
        public bool FacesWorks(string childId) => false;
        public int WorksHoldHash => 0;
        public float Willingness => -1f;
        public IWorksQuery HeldWorks => null;
        public bool TryStation(out StationPoint station, float[] ownShiftByChild, out int childCount) { station = default(StationPoint); childCount = 0; return false; }
        public string StationChildId(int column) => null;

        // BC14O: no objective table.
        public WhiskeyRealism.Tactical.Battle.ObjectivePressure Pressure => default(WhiskeyRealism.Tactical.Battle.ObjectivePressure);
        public int ObjectiveCount => 0;
        public bool TryObjective(int index, out WhiskeyRealism.Tactical.Battle.ObjectiveEstimate estimate) { estimate = default(WhiskeyRealism.Tactical.Battle.ObjectiveEstimate); return false; }
        public int ThreatCount => 1;
        public int LookRequests(Span<float> xs, Span<float> zs) => 0;
        public double EvaluatedHours => 0.0;
        public bool HasEvaluation => false;
        public bool TryPredictedArrival(string childId, out double arrivalHours, out double marchHours) { arrivalHours = marchHours = 0.0; return false; }
        public bool TryThreat(int index, out CrossingThreat threat, float[] ownShiftByChild, out int childCount)
        {
            threat = Threat;
            childCount = Columns.Length;
            for (int c = 0; c < Columns.Length; c++) ownShiftByChild[c] = Times[c];
            return index == 0;
        }
        public string ThreatChildId(int column) => column >= 0 && column < Columns.Length ? Columns[column] : null;
        public void CaptureSelection(BattlePlanId plan, bool mirrored) { }
        public bool TryPlannedRelease(string childId, out double releaseHours, out double timeBudgetHours, out float viaX, out float viaZ, out float attackViaX, out float attackViaZ)
        {
            releaseHours = timeBudgetHours = 0.0;
            viaX = viaZ = attackViaX = attackViaZ = float.NaN;
            return false;
        }
        public bool TryViaRegions(string childId, out int viaFarRegion, out int attackViaFarRegion) { viaFarRegion = attackViaFarRegion = -1; return false; }
        public bool TryGet(BattlePlanId plan, out CandidateResult result) { result = default(CandidateResult); return false; }
    }

    static void ThreatKeyedById()
    {
        // The ledger timed c1, c2 and c3 (c2 is the one the player holds: NaN). The allocation's children now are c3, c1, c4, c2: the same
        // number of columns, a different set and order.
        var lookup = new MatrixLookup { Columns = new[] { "c1", "c2", "c3" }, Times = new[] { 0.2f, float.NaN, 0.7f } };
        DirectChildSnapshot[] snapshots = ResChildren(4);                 // c0..c3
        var renamed = new DirectChildSnapshot[4];
        string[] ids = { "c3", "c1", "c4", "c2" };
        for (int i = 0; i < 4; i++) renamed[i] = WithId(snapshots[i], ids[i]);
        var buffer = new CrossingThreat[ManeuverFeasibility.MaxThreatCrossings];
        var shifts = new float[ManeuverFeasibility.MaxThreatCrossings * ManeuverFeasibility.MaxChildren];
        var row = new float[ManeuverFeasibility.MaxChildren];
        int threats = ArmyOrchestrator.FillThreatMatrix(lookup, renamed, buffer, shifts, row, out int children);
        AssertEqual(1, threats, "one threat");
        AssertEqual(4, children, "a column per allocation child");
        AssertNear(0.7f, shifts[0], 1e-6f, "c3 takes the ledger's c3 time");
        AssertNear(0.2f, shifts[1], 1e-6f, "c1 takes the ledger's c1 time");
        AssertTrue(float.IsNaN(shifts[2]), "c4 is new to the ledger: no time, never released or bound");
        AssertTrue(float.IsNaN(shifts[3]), "c2 keeps the NaN of the player-owned strength the ledger saw");
        // The same children in the ledger's order read straight across.
        for (int i = 0; i < 4; i++) renamed[i] = WithId(snapshots[i], new[] { "c1", "c2", "c3", "cx" }[i]);
        threats = ArmyOrchestrator.FillThreatMatrix(lookup, renamed, buffer, shifts, row, out children);
        AssertNear(0.2f, shifts[0], 1e-6f, "c1");
        AssertTrue(float.IsNaN(shifts[1]), "c2");
        AssertNear(0.7f, shifts[2], 1e-6f, "c3");
        AssertTrue(float.IsNaN(shifts[3]), "cx is not in the ledger");
        // The real ledger publishes the id of each column, in the evaluation's child order.
        FeasField field = DefenderField();
        FeasibilityLedger ledger = DefenderLedger(field);
        AssertTrue(ledger.ThreatCount > 0, "the Defender ledger has a threat");
        for (int k = 0; k < field.ChildIds.Count; k++)
            AssertEqual(field.ChildIds[k], ledger.ThreatChildId(k), "column " + k + " is child " + field.ChildIds[k]);
        AssertTrue(ledger.ThreatChildId(field.ChildIds.Count) == null, "and no column past the last child");
        // More children than the ledger's cap: no matrix at all.
        var many = new DirectChildSnapshot[ManeuverFeasibility.MaxChildren + 1];
        for (int i = 0; i < many.Length; i++) many[i] = WithId(snapshots[0], "m" + i);
        AssertEqual(0, ArmyOrchestrator.FillThreatMatrix(lookup, many, buffer, shifts, row, out children), "over the cap: no threats");
    }

    static DirectChildSnapshot WithId(DirectChildSnapshot s, string id) => new DirectChildSnapshot(id, s.ParentArmyId, s.RawUnitTyp, s.CommandHierarchyShift, s.DisplayName, s.Active);

    static void DefNoSamples()
    {
        // Control: with the enemy perceived at the flank ford the Defender counts and publishes a threat there.
        FeasField seen = DefenderField(f => { f.Enemies.Clear(); f.AddEnemy(-900f, 556f, 11000f); });
        FeasibilityLedger seenLedger = DefenderLedger(seen);
        AssertTrue(seenLedger.Job.Defender.Perceived, "control: perceived");
        AssertTrue(seenLedger.ThreatCount > 0, "control: the ford is a published threat, " + seenLedger.ThreatCount);

        // No sample, only BC10's assumed point at the same place: the crossings are counted (the penalty reads the body) but none is published.
        FeasField none = DefenderField(f => { f.Enemies.Clear(); f.HasAssumedEnemy = true; f.AssumedEnemyLateral = -900f; f.AssumedEnemyDepth = 556f; });
        FeasibilityLedger noneLedger = DefenderLedger(none);
        AssertFalse(noneLedger.Job.Defender.Perceived, "not perceived");
        AssertTrue(noneLedger.Job.Defender.ThreatCount > 0, "the assumed point still counts the ford, " + noneLedger.Job.Defender.ThreatCount);
        AssertEqual(0, noneLedger.ThreatCount, "but the assumed point never drives a guard: nothing is published");
        // A binding that stood on the last tick's threat ends when the enemy is no longer perceived.
        ReserveReleaseState.ResetAll();
        var state = BoundC1();
        IReadOnlyList<DirectChildIntent> intents = CrossAllocate(ResEvidence(1, 3), state, 10.0, new CrossingThreat[0], Row(9f, 0.2f, 0.5f));
        AssertEqual(DirectChildRole.Reserve, intents[1].Role, "an empty threat list reverts the guard");
        AssertFalse(state.TryCrossingOf("c1", out _), "and ends its binding");
    }

    static void EnemySampleFilter()
    {
        FeasField f = DefenderField(g => g.Enemies.Clear());
        f.AddEnemy(-400f, 556f, 1000f);                                                                       // counted in full
        f.AddEnemy(0f, 556f, 1000f, state: ObservationState.Remembered, confidence: 0.6f);                    // counted at 0.6
        f.AddEnemy(400f, 556f, 1000f, state: ObservationState.Remembered, confidence: 0.4f);                  // below the Remembered floor
        f.AddEnemy(800f, 556f, 1000f, unitTyp: TacticalUnitType.BattleGroupDivision);                          // a command group, not a combat unit
        f.AddEnemy(1200f, 556f, 1000f);
        EnemyObservation reinforcement = f.Enemies[f.Enemies.Count - 1];
        reinforcement.IsReinforcement = true;                                                                 // a column that has not arrived
        f.Enemies[f.Enemies.Count - 1] = reinforcement;
        FeasibilityLedger ledger = DefenderLedger(f);
        EnemyPicture enemy = ledger.Job.Enemy;
        AssertEqual(2, enemy.Count, "two samples: the visible one and the confident Remembered one");
        AssertNear(1000f + 600f, enemy.P, 1e-3f, "perceived strength 1,000 + 0.6 x 1,000");
        AssertNear(FeasField.X(-250f), enemy.MassX, 1f, "the mass is the weighted centre of the two counted samples (lateral -250), not pulled by the excluded ones");
    }

    static void MarchHorseArtillery()
    {
        // Vanilla's pace of a group is the slowest regiment's base times Min(groupspeed, 1), not a cap at 1 (133353-133358, 133986, 91708-91733).
        // The shipped data: infantry 1, limbered artillery 1, mounted cavalry 2, horse artillery the limbered base times 2.5.
        float Speed(MarchRules rules, bool infantry, int cavalry, int horse, int foot)
        {
            FeasField f = DefenderField(g => { g.Enemies.Clear(); g.AddEnemy(0f, 556f, 5000f); });
            f.Rules = rules;
            if (infantry) f.AddCorps(-600f, 100f, 3000f);
            else f.AddBatteryCorps(-600f, 100f, 3000f, horse: horse > 0, batteries: horse + foot, cavalry: cavalry);
            FeasibilityLedger ledger = DefenderLedger(f);
            return ledger.Job.Children[ledger.Job.SlotOfChild(f.ChildIds[f.ChildIds.Count - 1])].BaseSpeed;
        }
        MarchRules shipped = ShippedRules();
        AssertNear(2.5f, shipped.HorseArtilleryFactor, 1e-6f, "the fixture carries the shipped factor");
        AssertNear(2f, Speed(shipped, false, cavalry: 2, horse: 2, foot: 0), 1e-6f, "a cavalry corps with horse batteries moves at the cavalry's 2: not capped at 1, not slowed to 2.5");
        AssertNear(2.5f, Speed(shipped, false, cavalry: 0, horse: 2, foot: 0), 1e-6f, "horse artillery alone moves at the limbered base times 2.5");
        AssertNear(1f, Speed(shipped, false, cavalry: 2, horse: 0, foot: 2), 1e-6f, "cavalry with foot guns moves at the guns' 1");
        AssertNear(2f, Speed(shipped, false, cavalry: 2, horse: 0, foot: 0), 1e-6f, "cavalry alone moves at 2");
        AssertNear(1f, Speed(shipped, true, 0, 0, 0), 1e-6f, "infantry moves at 1");
        // Only a base under 1 squares (s x Min(s, 1)); no shipped type has one.
        MarchRules slow = ShippedRules();
        slow.HorseArtilleryFactor = 0.6f;
        AssertNear(0.36f, Speed(slow, false, cavalry: 0, horse: 2, foot: 0), 1e-6f, "a base of 0.6 gives 0.6 x Min(0.6, 1)");
        // The snapshot builder sets the flag from the regiment's own ishorseartillery (a runtime file: a source pin).
        FlankResponseTests.AssertPinned(FlankResponseTests.SourceText("Tactical", "Battle", "BattleSnapshotBuilder.cs"),
            "if (reg.ishorseartillery) flags |= UnitFlags.HorseArtillery;", "the snapshot marks horse artillery");
    }

    static void XingOwnCover()
    {
        UnitRecord Reg(UnitFlags flags, int unitTyp = TacticalUnitType.Infantry, float rotation = 90f)
            => new UnitRecord { UnitTyp = unitTyp, X = 0f, Z = 0f, RotationY = rotation, FireRange = 100f, Flags = flags };
        const UnitFlags live = UnitFlags.Alive | UnitFlags.Active;
        // A crossing 100 units east (bearing 90): inside 1.5 x 100 and on the facing line.
        AssertTrue(CoveredCrossings.Covers(Reg(live), 100f, 0f), "control: a live infantry regiment facing the crossing covers it");
        AssertFalse(CoveredCrossings.Covers(Reg(live | UnitFlags.Routed), 100f, 0f), "a routed regiment does not");
        AssertFalse(CoveredCrossings.Covers(Reg(live | UnitFlags.IsGroup), 100f, 0f), "a command group does not");
        AssertFalse(CoveredCrossings.Covers(Reg(UnitFlags.Alive), 100f, 0f), "an inactive one does not");
        AssertFalse(CoveredCrossings.Covers(Reg(live, TacticalUnitType.Artillery), 100f, 0f), "artillery does not");
        AssertFalse(CoveredCrossings.Covers(Reg(live, rotation: 150f), 100f, 0f), "facing 60 degrees off does not");
        AssertFalse(CoveredCrossings.Covers(Reg(live), 200f, 0f), "beyond 1.5 x its fire range does not");
    }

    static void LookDefenderFrontal()
    {
        // The bridge at lateral 0 is in front of the line's middle half (not a flank crossing: no threat); the ford at -900 is the flank's.
        FeasField f = DefenderField(g => g.Perceived = (x, z) => false);       // nothing of the ground is perceived: every crossing is Unknown
        // Two defensive plans whose routes cross nothing, so no plan route asks about a crossing: only the enemy's reach can.
        BattlePlanId[] holding = { BattlePlanId.PreparedDefense, BattlePlanId.GenericMethodical };
        f.Legs = StraightLegs(f, holding);
        var ledger = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(f.Build(holding), ledger);
        ledger.CaptureSelection(BattlePlanId.PreparedDefense, false);
        ManeuverFeasibility.Evaluate(f.Build(holding), ledger);
        Span<float> xs = stackalloc float[ManeuverFeasibility.MaxLookRequests], zs = stackalloc float[ManeuverFeasibility.MaxLookRequests];
        int n = ledger.LookRequests(xs, zs);
        AssertEqual(2, n, "both Unknown crossings are asked about");
        bool bridge = false, ford = false;
        for (int i = 0; i < n; i++)
        {
            if (Math.Abs(xs[i] - FeasField.X(0f)) < 5f && Math.Abs(zs[i] - FeasField.Z(250f)) < 5f) bridge = true;
            if (Math.Abs(xs[i] - FeasField.X(-900f)) < 5f && Math.Abs(zs[i] - FeasField.Z(250f)) < 5f) ford = true;
        }
        AssertTrue(ford, "the flank ford is asked about");
        AssertTrue(bridge, "and the frontal bridge, which the threat list leaves out");
        for (int t = 0; t < ledger.ThreatCount; t++)
        {
            ledger.TryThreat(t, out CrossingThreat threat, new float[ManeuverFeasibility.MaxChildren], out _);
            AssertTrue(Math.Abs(threat.X - FeasField.X(0f)) > 5f, "the threat list does not carry the bridge");
        }
        // With no enemy perceived (the assumed point only) there is no reach to speak of: nothing is asked about for the enemy's sake.
        FeasField none = DefenderField(g => { g.Enemies.Clear(); g.HasAssumedEnemy = true; g.AssumedEnemyLateral = 0f; g.AssumedEnemyDepth = 556f; g.Perceived = (x, z) => false; });
        FeasibilityLedger noneLedger = DefenderLedger(none);
        AssertTrue(ledger.Job.Defender.LookCount >= 2, "control: the perceived enemy reaches both crossings, " + ledger.Job.Defender.LookCount);
        AssertEqual(0, noneLedger.Job.Defender.LookCount, "no sample, no crossing in the enemy's reach for a look");
    }

    // A Defender field over 60 fords with n enemy regiments 200 apart (one cluster each), the shape the frame-budget tests use.
    static FeasField WideDefender(int enemies, Action<FeasField> more = null, int fords = 60)
    {
        FeasField f = ManyFords(fords);
        f.Stance = BattleStance.Defender;
        f.Enemies.Clear();
        for (int e = 0; e < enemies; e++) f.AddEnemy(-1100f + 200f * (e % 12), 420f + 200f * (e / 12), 800f);
        more?.Invoke(f);
        f.Legs = StraightLegs(f, DefensivePlans);
        return f;
    }

    static void LookListOrder()
    {
        FeasField f = WideDefender(24, g => g.Perceived = (x, z) => false);
        FeasibilityLedger probe = new FeasibilityLedger();
        FeasibilityInput input = f.Build(DefensivePlans);
        ManeuverFeasibility.Evaluate(in input, probe);
        DefenderPicture d = probe.Job.Defender;
        int routed = probe.Job.Input.Regions.RoutedCount;
        // Daylight cut at the median arrival: some links are reached before dark and some are not.
        var arrivals = new List<float>();
        for (int l = 0; l < routed; l++) if (d.LinkBody[l] >= 0) arrivals.Add(d.LinkEarliest[l]);
        arrivals.Sort();
        AssertTrue(arrivals.Count > 20, "the enemy reaches many fords: " + arrivals.Count);
        float cut = arrivals[arrivals.Count / 2];
        f.DaylightHours = cut;
        var ledger = new FeasibilityLedger();
        FeasibilityInput cutInput = f.Build(DefensivePlans);
        ManeuverFeasibility.Evaluate(in cutInput, ledger);
        d = ledger.Job.Defender;
        AssertEqual(ManeuverFeasibility.MaxThreatCrossings, d.LookCount, "more than eight fords are reached before dark: the list holds eight");
        var reached = new List<float>();
        for (int l = 0; l < routed; l++) if (d.LinkBody[l] >= 0 && d.LinkEarliest[l] < cut) reached.Add(d.LinkEarliest[l]);
        reached.Sort();
        AssertTrue(reached.Count > ManeuverFeasibility.MaxThreatCrossings, "and there are more than eight to choose from: " + reached.Count);
        for (int i = 0; i < d.LookCount; i++)
        {
            float at = d.LinkEarliest[d.LookLinks[i]];
            AssertTrue(at < cut, "look " + i + " is reached before dark (" + at + " < " + cut + ")");
            AssertNear(reached[i], at, 1e-6f, "look " + i + " is the " + i + "-th earliest of the crossings reached before dark");
        }

        // Dusk falls before the eighth: only the crossings reached before it are in the list, not the eight earliest regardless.
        int few = 4;
        AssertTrue(arrivals[few] > arrivals[few - 1], "the fixture's arrivals separate");
        float dusk = (arrivals[few - 1] + arrivals[few]) * 0.5f;
        f.DaylightHours = dusk;
        var late = new FeasibilityLedger();
        FeasibilityInput duskInput = f.Build(DefensivePlans);
        ManeuverFeasibility.Evaluate(in duskInput, late);
        AssertEqual(few, late.Job.Defender.LookCount, "only the crossings reached before dusk are listed");
        for (int i = 0; i < late.Job.Defender.LookCount; i++)
            AssertTrue(late.Job.Defender.LinkEarliest[late.Job.Defender.LookLinks[i]] < dusk, "look " + i + " is before dusk");
    }

    static void DefWorkUnits()
    {
        const int budget = 4;
        FeasField f = WideDefender(24);
        FeasibilityInput input = f.Build(DefensivePlans);
        var forced = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(in input, forced);
        int totalUnits = forced.Job.WorkUnits;
        AssertTrue(totalUnits > 120, "the picture of 60 fords and 16 bodies is many units: " + totalUnits);
        AssertTrue(forced.Job.Defender.Rides <= forced.Job.Input.Regions.RoutedCount * forced.Job.Defender.BodyCount,
            "each link's earliest arrival is priced once, not again in the flank loop: " + forced.Job.Defender.Rides + " rides over "
            + forced.Job.Input.Regions.RoutedCount + " links and " + forced.Job.Defender.BodyCount + " bodies");
        AssertTrue(forced.Job.MaxUnitCost <= ManeuverFeasibility.MaxEnemyBodies, "no unit costs more than one body's courier rides or one path search: " + forced.Job.MaxUnitCost);

        // The same evaluation under a budget of `budget` units a call (the clock advances one tick per check): resumable, every call bounded.
        var sliced = new FeasibilityLedger();
        long ticks = 0;
        Func<long> clock = () => ticks++;
        int calls = 0, mostUnits = 0, previous = 0;
        bool done = false;
        while (!done)
        {
            long deadline = ticks + budget;
            done = ManeuverFeasibility.EvaluateSlice(in input, sliced.Job, sliced, deadline, clock);
            calls++;
            int units = sliced.Job.WorkUnits - previous;
            previous = sliced.Job.WorkUnits;
            if (units > mostUnits) mostUnits = units;
            AssertTrue(calls < 2000, "the evaluation finishes");
        }
        AssertTrue(mostUnits <= budget + 1, "no call ran more than its budget of units (+1 for the guaranteed one): " + mostUnits);
        AssertTrue(calls > totalUnits / (budget + 1), "the work was spread over many calls: " + calls);
        AssertEqual(totalUnits, sliced.Job.WorkUnits, "the same units in the end");
        AssertEqual(forced.ThreatCount, sliced.ThreatCount, "the same threats");
        AssertEqual(forced.CandidateCount, sliced.CandidateCount, "the same candidates");
        for (int c = 0; c < forced.CandidateCount; c++)
        {
            forced.TryGetAt(c, out CandidateResult a);
            sliced.TryGetAt(c, out CandidateResult b);
            AssertEqual(a.Plan, b.Plan, "candidate " + c + " plan");
            AssertNear(a.Penalty, b.Penalty, 1e-6f, "candidate " + c + " penalty");
        }

        // An Attacker has no picture to build.
        FeasField attacker = WideDefender(24, g => g.Stance = BattleStance.Meeting);
        FeasibilityInput attackInput = attacker.Build(DefensivePlans);
        var attackLedger = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(in attackInput, attackLedger);
        AssertEqual(0, attackLedger.Job.WorkUnits, "an Attacker's evaluation has no Defender work units");

        // The monitor's run is the same work in slices, builds no look list, and finishes with the monitor ledger fresh.
        forced.CaptureSelection(BattlePlanId.PreparedDefense, false);
        ManeuverFeasibility.Evaluate(in input, forced);
        ticks = 0;
        int monitorCalls = 0;
        while (!ManeuverFeasibility.EvaluateSelectedSlice(in input, forced, ticks + budget, clock))
        {
            monitorCalls++;
            AssertTrue(forced.MonitorRunning, "the monitor run is part-way between calls");
            AssertTrue(monitorCalls < 2000, "the monitor finishes");
        }
        AssertTrue(monitorCalls > 5, "the monitor's picture also took several calls: " + monitorCalls);
        AssertEqual(0, forced.MonitorLedger.Job.Defender.LookCount, "the monitor builds no look list");
        AssertFalse(forced.MonitorRunning, "and is done");
        // A new selection drops a monitor run of the old one.
        ticks = 0;
        AssertFalse(ManeuverFeasibility.EvaluateSelectedSlice(in input, forced, ticks + 2, clock), "a tight budget leaves the monitor part-way");
        AssertTrue(forced.MonitorRunning, "part-way");
        forced.CaptureSelection(BattlePlanId.GenericMethodical, false);
        AssertFalse(forced.MonitorRunning, "a new selection ends the old selection's monitor run");
    }

    static void DefTime()
    {
        // Informational (never asserted): the harness machine is not the game's. The binding measurement is the in-game frame budget (smoke item 6).
        // The same field and counts the verifier timed: ManyFords(n) as a Defender with 24 samples, the minimum over 200 runs.
        foreach (int fords in new[] { 10, 20, 60 })
        {
            FeasField f = WideDefender(24, null, fords);
            FeasibilityInput input = f.Build(DefensivePlans);
            var ledger = new FeasibilityLedger();
            ManeuverFeasibility.Evaluate(in input, ledger);            // warm-up
            double bestTotal = double.MaxValue, bestLargest = double.MaxValue;
            int units = 0;
            for (int run = 0; run < 200; run++)
            {
                ledger.Job.Abort();
                ledger.Job.Begin(in input, ledger);
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp(), last = t0;
                double largest = 0.0;
                units = 0;
                while (!ledger.Job.Defender.IsDone)
                {
                    ManeuverFeasibility.StepDefender(ledger.Job);
                    units++;
                    long now = System.Diagnostics.Stopwatch.GetTimestamp();
                    double ms = (now - last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    if (ms > largest) largest = ms;
                    last = now;
                }
                double total = (last - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (total < bestTotal) { bestTotal = total; bestLargest = largest; }
            }
            ledger.Job.Abort();
            Console.WriteLine("INFO DEF-TIME " + fords + " fords, 24 samples: the whole picture " + bestTotal.ToString("F3") + " ms in " + units + " work units, its largest unit "
                + bestLargest.ToString("F3") + " ms");
            AssertTrue(ledger.Job.Defender.IsDone && units > 0, "the picture was built in units");
        }
    }

    struct RecordingTick : IFeasibilityTickWork
    {
        public List<string> Log;
        public bool Pending;
        public bool MonitorPending => Pending;
        public void RunMonitor() => Log.Add("monitor");
        public void RunEvaluation() => Log.Add("evaluate");
    }

    // Both jobs of a 60-ford Defender field on the real ledgers, one shared budget a tick, the clock one tick a check.
    struct SharedBudgetTick : IFeasibilityTickWork
    {
        public FeasibilityLedger Ledger;
        public FeasibilityInput Full, Monitored;
        public long Deadline;
        public Func<long> Clock;
        public bool MonitorWanted;
        public bool MonitorPending => MonitorWanted;
        public void RunMonitor() { if (ManeuverFeasibility.EvaluateSelectedSlice(in Monitored, Ledger, Deadline, Clock)) MonitorWanted = false; }
        public void RunEvaluation() => ManeuverFeasibility.EvaluateSlice(in Full, Ledger.Job, Ledger, Deadline, Clock);
    }

    static void FeasTickOrder()
    {
        var log = new List<string>();
        var both = new RecordingTick { Log = log, Pending = true };
        FeasibilityTick.Run(ref both);
        AssertEqual("monitor,evaluate", string.Join(",", log), "with both pending the monitor runs first");
        log.Clear();
        var only = new RecordingTick { Log = log, Pending = false };
        FeasibilityTick.Run(ref only);
        AssertEqual("evaluate", string.Join(",", log), "with no monitor pending only the evaluation runs");

        // The property it buys, on the real jobs: how many ticks the monitor needs while a wide evaluation is also running, under one budget.
        const int budget = 6;
        FeasField f = WideDefender(24);
        FeasibilityInput full = f.Build(DefensivePlans);
        var probe = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(in full, probe);
        probe.CaptureSelection(BattlePlanId.PreparedDefense, false);
        ManeuverFeasibility.Evaluate(in full, probe);
        EvaluateSelectedFully(in full, probe);
        int monitorUnits = probe.MonitorLedger.Job.WorkUnits + 1;       // the picture and the one candidate
        AssertTrue(monitorUnits > 100, "the monitor's run is many units: " + monitorUnits);

        int Ticks()
        {
            // A running full evaluation (restarted when it finishes, as the interval does) and a monitor run wanted from tick 0.
            var ledger = new FeasibilityLedger();
            ManeuverFeasibility.Evaluate(in full, ledger);
            ledger.CaptureSelection(BattlePlanId.PreparedDefense, false);
            long ticks = 0;
            Func<long> clock = () => ticks++;
            var work = new SharedBudgetTick { Ledger = ledger, Full = full, Monitored = full, Clock = clock, MonitorWanted = true };
            int n = 0;
            while (work.MonitorWanted && n < 1000)
            {
                work.Deadline = ticks + budget;
                FeasibilityTick.Run(ref work);
                n++;
            }
            return n;
        }
        int monitorFirst = Ticks();
        AssertTrue(monitorFirst <= (monitorUnits + budget - 1) / (budget - 1) + 1, "the monitor finishes in about units / budget ticks: " + monitorFirst + " for " + monitorUnits + " units at " + budget);
        AssertTrue(monitorFirst < 1000, "and it finishes");
    }
}
