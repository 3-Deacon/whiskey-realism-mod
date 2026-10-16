using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// BC13 task 5: the per-commander opportunity decision (spec § 6, requirement-deciding-commanders ... decision-contract).
internal static partial class CommanderOpportunityTests
{
    private static (string, Action)[] DecisionTests()
    {
        return new (string, Action)[]
        {
            ("bc13 OPP-LAUNCH the spec fixture launches on a roll below 0.7159 and misses above", OppLaunch),
            ("bc13 OPP-KINDS kind weights and conditions", OppKindWeights),
            ("bc13 OPP-CAV-STEADY cavalry is never chosen against formed steady infantry", OppCavSteady),
            ("bc13 OPP-CAV-MORALE a cavalry asset at morale 0.70 is ineligible", OppCavMorale),
            ("bc13 OPP-GAP-FIT a gap narrower than 0.6 x the asset's width is rejected", OppGapFit),
            ("bc13 OPP-REACH audacity scales reach between 450 and 750 m", OppReach),
            ("bc13 OPP-ODDS required odds scale with aggression; no enemy within 300 m passes", OppOdds),
            ("bc13 OPP-CONF the confidence threshold scales with caution; the opening ages out", OppConfidence),
            ("bc13 OPP-FLOOR the floor share and the n <= 2 rule", OppFloor),
            ("bc13 OPP-INTEGRITY removing a line brigade that leaves a gap fails", OppIntegrity),
            ("bc13 OPP-INTEGRITY-SIBLING the commitment ledger carries a sibling's launch", OppIntegritySibling),
            ("bc13 OPP-MAIN the main-effort rule and its in-sector exception", OppMain),
            ("bc13 OPP-RIVER a river on the segment fails", OppRiver),
            ("bc13 OPP-DEF a Defender launches only Penetration, and only with the flag on", OppDefender),
            ("bc13 OPP-CONC the side cap, the per-node cap and claimed openings", OppConcurrency),
            ("bc13 OPP-PHASE no exploit in the Withdraw phase or an army retreat", OppPhase),
            ("bc13 OPP-ORDER deepest first, ties by id", OppOrder),
            ("bc13 OPP-DUE the decision interval and the first-decision stagger", OppDue),
            ("bc13 OPP-ROLL the hash is deterministic and below one", OppRoll),
            ("bc13 PERS-DUTY the exploit chance carries the out-of-duty factor", OppDuty),
            ("bc13 OPP-ELIG one case per eligibility rule", OppEligibility),
            ("bc13 OPP-PLAYER a player-owned descendant excludes the asset and the player's node never decides", OppPlayer),
            ("bc13 OPP-FEUD a Feud asset is never eligible", OppFeud),
            ("bc13 OPP-ARMY-DECIDES with no corps or division the army node decides", OppArmyDecides),
            ("bc13 OPP-CONTRACT launch iff roll < chance; the last failing reason or no-pair", OppContract),
            ("bc13 OPP-HESITATE a hesitating commander skips an opening another may take", OppHesitation),
            ("bc13 COUNTER-ASSET a frontage-holding child is never the counterstroke asset, cavalry never", CounterAsset),
            ("bc13 COUNTER-FLAG with the flag off a Defender never launches", CounterFlag),
        };
    }

    // ------------------------------------------------------------------ fixtures

    // The plan's OPP-LAUNCH commander: initiative 0.55, cunning 0.40, leadership 0.60, administration 0.50, a veteran.
    private static OpportunityPersonality LaunchPersonality()
    {
        PersonalityVector v = CommanderPersonalityDerivation.Derive(Stats(0.55f, 0.40f, 0.60f, 0.50f, vet: true));
        return new OpportunityPersonality
        {
            Agg01 = CommanderPersonalityDerivation.To01(v.Aggression),
            Caution01 = CommanderPersonalityDerivation.To01(v.Caution),
            Audacity01 = CommanderPersonalityDerivation.To01(v.Audacity),
            CasualtyTolerance01 = CommanderPersonalityDerivation.To01(v.CasualtyTolerance),
            Duty = 1f,
        };
    }

    private static OpportunityPersonality Persona(float agg01 = 0.5f, float caution01 = 0.5f, float audacity01 = 0.5f, float tolerance01 = 0.5f, float duty = 1f)
        => new OpportunityPersonality { Agg01 = agg01, Caution01 = caution01, Audacity01 = audacity01, CasualtyTolerance01 = tolerance01, Duty = duty };

    private sealed class NoHesitation : IHesitationLookup
    {
        public bool IsHesitating(int commanderIndex, int openingId, double nowHours) => false;
    }

    // One deciding node's input with a single infantry asset at the origin and a confirmed 300 m gap 400 m ahead.
    private sealed class DecFx
    {
        public readonly List<ExploitAsset> Assets = new List<ExploitAsset>();
        public readonly List<Opening> Openings = new List<Opening>();
        public FnTerrain Terrain = new FnTerrain();
        public BattleSnapshot Snapshot;
        public IHesitationLookup Hesitation = new NoHesitation();
        public CommitmentLedger Ledger = new CommitmentLedger();
        public BattleStance Stance = BattleStance.Attacker;
        public BattlePhase Phase = BattlePhase.MainEffort;
        public int MacroAi = 1, MainEffortSector = 2, SideLive = 0, Children = 4, Uncommitted = 4;
        public bool NodeHasExploit, CounterstrokeOn = true;
        public int BattleKey = 7, Commander = 42, DecisionNumber = 3;
        public double Now = 10.0;

        public DecFx()
        {
            Assets.Add(Asset(1));
            Openings.Add(GapOpening(1, 0f, 400f));
        }

        public static ExploitAsset Asset(int id, float x = 0f, float z = 0f, bool cavalry = false, float strength = 1000f, float morale = 0.8f, float width = 200f)
            => new ExploitAsset
            {
                Id = id, OwnIndex = id, X = x, Z = z, Lateral = x, Strength = strength, GroupMorale = morale, GroupWidth = width,
                Cavalry = cavalry, Uncommitted = true, Task = CommandTaskType.ReserveWait,
            };

        public static Opening GapOpening(int id, float x, float z, float width = 300f, float confidence = 1f)
            => new Opening
            {
                Id = id, Kind = OpeningKind.Gap, AimX = x, AimZ = z, Width = width, Confidence01 = confidence, Sector = 1,
                TargetObservation = -1, FirstSeenHours = 9.9, LastSeenHours = 10.0, Confirmed = true,
            };

        public DecisionInput Build()
        {
            return new DecisionInput
            {
                NodeId = 100, CommanderIndex = Commander, CommanderStatus = 0, ChildCount = Children, UncommittedChildren = Uncommitted,
                Assets = Assets.ToArray(), AssetCount = Assets.Count, Openings = Openings.ToArray(), OpeningCount = Openings.Count,
                Stance = Stance, PlanPhase = Phase, MacroAi = MacroAi, MainEffortSector = MainEffortSector, SideLiveExploits = SideLive,
                NodeHasExploit = NodeHasExploit, CounterstrokeOn = CounterstrokeOn,
                BattleKey = BattleKey, DecisionNumber = DecisionNumber, NowHours = Now,
                Terrain = Terrain, Snapshot = Snapshot, Hesitation = Hesitation, Ledger = Ledger,
            };
        }

        public DecisionResult Decide(in OpportunityPersonality p) => OpportunityDoctrine.Decide(Build(), in p);
    }

    // A decision number whose roll lands below (launch) or above (miss) the chance.
    private static int NumberWithRoll(int battleKey, int commander, int opening, float chance, bool below)
    {
        for (int n = 0; n < 2000; n++)
        {
            float roll = OpportunityDoctrine.Roll(battleKey, commander, opening, n);
            if (below ? roll < chance : roll >= chance) return n;
        }
        throw new Exception("no decision number found");
    }

    // ------------------------------------------------------------------ tests

    private static void OppLaunch()
    {
        OpportunityPersonality p = LaunchPersonality();
        // The spec's worked numbers.
        AssertNear(622.5f, OpportunityDoctrine.Reach(false, p.Audacity01), 1e-2f, "infantry reach");
        AssertNear(0.94f, OpportunityDoctrine.RequiredOdds(OpeningKind.Gap, p.Agg01), 1e-4f, "required odds");
        AssertNear(0.4625f, OpportunityDoctrine.ConfidenceMin(p.Caution01), 1e-4f, "confidence minimum");
        AssertNear(0.26f, OpportunityDoctrine.FloorShare(p.Caution01, p.CasualtyTolerance01), 1e-4f, "floor share");
        AssertEqual(2, OpportunityDoctrine.FloorCount(4, OpportunityDoctrine.FloorShare(p.Caution01, p.CasualtyTolerance01)), "floor 2 of 4");
        float chance = OpportunityDoctrine.Chance(OpeningKind.Gap, in p);
        AssertNear(0.7159f, chance, 1e-3f, "chance");

        var fx = new DecFx();
        fx.DecisionNumber = NumberWithRoll(fx.BattleKey, fx.Commander, 1, chance, below: true);
        DecisionResult hit = fx.Decide(in p);
        AssertTrue(hit.Launch, "launch on a roll below the chance: " + hit.Reason);
        AssertEqual("launch", hit.Reason, "reason literal");
        AssertEqual(1, hit.AssetId, "asset");
        AssertEqual(1, hit.OpeningId, "opening");
        AssertNear(chance, hit.Chance, 1e-5f, "chance reported");
        AssertTrue(hit.Roll < chance, "roll reported");
        AssertNear(0.94f, hit.RequiredOdds, 1e-4f, "required odds reported");
        AssertNear(0.4625f, hit.ConfidenceMin, 1e-4f, "confidence minimum reported");

        fx.DecisionNumber = NumberWithRoll(fx.BattleKey, fx.Commander, 1, chance, below: false);
        DecisionResult miss = fx.Decide(in p);
        AssertFalse(miss.Launch, "no launch above the chance");
        AssertEqual("roll-miss", miss.Reason, "roll-miss");
        AssertEqual(1, miss.OpeningId, "the declined pair is reported");
    }

    private static void OppKindWeights()
    {
        AssertEqual(1.0f, OpportunityDoctrine.KindWeight(OpeningKind.Gap, false, 0.5f, false), "gap infantry");
        AssertEqual(0.3f, OpportunityDoctrine.KindWeight(OpeningKind.Gap, true, 0.5f, false), "gap cavalry");
        AssertEqual(0.8f, OpportunityDoctrine.KindWeight(OpeningKind.ShakenSector, false, 0.5f, false), "shaken infantry");
        AssertEqual(1.0f, OpportunityDoctrine.KindWeight(OpeningKind.ShakenSector, true, 0.5f, false), "shaken cavalry");
        AssertEqual(1.0f, OpportunityDoctrine.KindWeight(OpeningKind.FlankInAir, false, 0.5f, false), "flank infantry");
        AssertEqual(0.5f, OpportunityDoctrine.KindWeight(OpeningKind.FlankInAir, true, 0.6f, false), "flank cavalry at audacity 0.6");
        AssertEqual(0f, OpportunityDoctrine.KindWeight(OpeningKind.FlankInAir, true, 0.59f, false), "flank cavalry below audacity 0.6");
        AssertEqual(0.6f, OpportunityDoctrine.KindWeight(OpeningKind.UnsupportedBattery, false, 0.5f, false), "battery infantry");
        AssertEqual(1.0f, OpportunityDoctrine.KindWeight(OpeningKind.UnsupportedBattery, true, 0.5f, false), "battery cavalry");
        AssertEqual(1.0f, OpportunityDoctrine.KindWeight(OpeningKind.IsolatedCluster, false, 0.5f, false), "cluster infantry");
        AssertEqual(0.6f, OpportunityDoctrine.KindWeight(OpeningKind.IsolatedCluster, true, 0.5f, true), "shaken cluster cavalry");
        AssertEqual(0f, OpportunityDoctrine.KindWeight(OpeningKind.IsolatedCluster, true, 0.5f, false), "steady cluster cavalry");
        AssertEqual(1.0f, OpportunityDoctrine.KindWeight(OpeningKind.Penetration, false, 0.5f, false), "penetration infantry");
        AssertEqual(0f, OpportunityDoctrine.KindWeight(OpeningKind.Penetration, true, 0.5f, false), "penetration cavalry");
        AssertNear(1.0f, OpportunityDoctrine.RequiredOdds(OpeningKind.Gap, 0.5f), 1e-5f, "base odds Gap");
        AssertNear(0.8f, OpportunityDoctrine.RequiredOdds(OpeningKind.ShakenSector, 0.5f) / 1f, 1e-5f, "base odds Shaken at mid aggression");
    }

    private static void OppCavSteady()
    {
        var fx = new DecFx();
        fx.Assets.Clear();
        fx.Assets.Add(DecFx.Asset(1, cavalry: true));
        fx.Openings.Clear();
        Opening steady = DecFx.GapOpening(1, 0f, 400f);
        steady.Kind = OpeningKind.IsolatedCluster;
        steady.ClusterValue = 100f;
        steady.ClusterNearestMeters = 2000f;
        steady.Shaken = false;
        fx.Openings.Add(steady);
        OpportunityPersonality p = Persona();
        DecisionResult none = fx.Decide(in p);
        AssertFalse(none.Launch, "cavalry never against a steady cluster");
        AssertEqual("no-pair", none.Reason, "no pair is formed");
        fx.Openings.Clear();
        steady.Shaken = true;
        fx.Openings.Add(steady);
        fx.DecisionNumber = NumberWithRoll(fx.BattleKey, fx.Commander, 1, OpportunityDoctrine.Chance(OpeningKind.IsolatedCluster, in p), below: true);
        AssertTrue(fx.Decide(in p).Launch, "against a shaken cluster it may launch");
    }

    private static void OppCavMorale()
    {
        var ctx = EligibleContext();
        var group = new UnitRecord { Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup, GroupMorale = 0.70f };
        ctx.Cavalry = true;
        AssertFalse(OpportunityDoctrine.Eligible(in group, in ctx, out string reason), "cavalry at 0.70 is below the 0.72 bar");
        AssertEqual("cavalry-morale", reason, "reason");
        group.GroupMorale = 0.73f;
        AssertTrue(OpportunityDoctrine.Eligible(in group, in ctx, out reason), "at 0.73 it is eligible: " + reason);
        ctx.Cavalry = false;
        group.GroupMorale = 0.70f;
        AssertTrue(OpportunityDoctrine.Eligible(in group, in ctx, out reason), "infantry at 0.70 is (the bar is 0.6): " + reason);
        AssertNear(0.72f, OpportunityDoctrine.CavalryMoraleBar(0.6f), 1e-5f, "min(1, 0.6 x 1.2)");
        AssertEqual(1f, OpportunityDoctrine.CavalryMoraleBar(0.9f), "capped at 1");
    }

    private static void OppGapFit()
    {
        var fx = new DecFx();
        fx.Openings.Clear();
        fx.Openings.Add(DecFx.GapOpening(1, 0f, 400f, width: 100f));   // an asset 200 m wide needs 120 m
        OpportunityPersonality p = Persona();
        AssertEqual("no-pair", fx.Decide(in p).Reason, "a gap of 100 m does not fit a 200 m asset");
        fx.Openings.Clear();
        fx.Openings.Add(DecFx.GapOpening(1, 0f, 400f, width: 120f));
        AssertTrue(fx.Decide(in p).Reason != "no-pair", "120 m (0.6 x 200) fits");
    }

    private static void OppReach()
    {
        AssertNear(450f, OpportunityDoctrine.Reach(false, 0f), 1e-3f, "infantry reach at audacity 0");
        AssertNear(600f, OpportunityDoctrine.Reach(false, 0.5f), 1e-3f, "at 0.5");
        AssertNear(750f, OpportunityDoctrine.Reach(false, 1f), 1e-3f, "at 1");
        AssertNear(900f, OpportunityDoctrine.Reach(true, 0f), 1e-3f, "cavalry at 0");
        AssertNear(1500f, OpportunityDoctrine.Reach(true, 1f), 1e-3f, "cavalry at 1");
        var fx = new DecFx();
        fx.Openings.Clear();
        fx.Openings.Add(DecFx.GapOpening(1, 0f, 460f));
        AssertEqual("no-pair", fx.Decide(Persona(audacity01: 0f)).Reason, "460 m is beyond an unaudacious commander's 450 m");
        AssertTrue(fx.Decide(Persona(audacity01: 1f)).Reason != "no-pair", "but within an audacious one's 750 m");
    }

    private static void OppOdds()
    {
        AssertNear(1.2f, OpportunityDoctrine.RequiredOdds(OpeningKind.Gap, 0f), 1e-5f, "agg 0");
        AssertNear(1.0f, OpportunityDoctrine.RequiredOdds(OpeningKind.Gap, 0.5f), 1e-5f, "agg 0.5");
        AssertNear(0.8f, OpportunityDoctrine.RequiredOdds(OpeningKind.Gap, 1f), 1e-5f, "agg 1");
        AssertNear(0.5f * 1.0f, OpportunityDoctrine.RequiredOdds(OpeningKind.UnsupportedBattery, 0.5f), 1e-5f, "battery base 0.5");
        AssertNear(1f / 0.65f, OpportunityDoctrine.RequiredOdds(OpeningKind.IsolatedCluster, 0.5f), 1e-4f, "cluster base 1 / 0.65");
        // asset strength x morale = 1000 x 0.8 = 800; local enemy value 900 -> odds 0.889 < 1.0 at mid aggression
        var fx = new DecFx();
        Opening weak = fx.Openings[0];
        weak.LocalEnemyValue = 900f;
        fx.Openings[0] = weak;
        DecisionResult low = fx.Decide(Persona());
        AssertEqual("odds", low.Reason, "local odds 0.889 against 1.0 required");
        AssertNear(800f / 900f, low.Odds, 1e-4f, "odds reported");
        // an aggressive commander (required 0.8) takes it
        AssertTrue(fx.Decide(Persona(agg01: 1f)).Reason != "odds", "at aggression 1 the same odds pass");
        // no enemy within 300 m: odds pass and the score uses the cap of 10
        var open = new DecFx();
        DecisionResult none = open.Decide(Persona());
        AssertTrue(none.Reason != "odds", "an empty corridor passes the odds test");
        AssertNear(10f * 1.0f * 1.0f, none.Score, 1e-3f, "score = kind weight 1.0 x odds cap 10 x confidence 1.0");
    }

    private static void OppConfidence()
    {
        AssertNear(0.35f, OpportunityDoctrine.ConfidenceMin(0f), 1e-5f, "caution 0");
        AssertNear(0.50f, OpportunityDoctrine.ConfidenceMin(0.5f), 1e-5f, "caution 0.5");
        AssertNear(0.65f, OpportunityDoctrine.ConfidenceMin(1f), 1e-5f, "caution 1");
        var fx = new DecFx();
        fx.Openings.Clear();
        fx.Openings.Add(DecFx.GapOpening(1, 0f, 400f, confidence: 0.4f));
        AssertEqual("confidence", fx.Decide(Persona(caution01: 0.5f)).Reason, "0.4 below the 0.5 a mid commander needs");
        AssertTrue(fx.Decide(Persona(caution01: 0f)).Reason != "confidence", "a bold commander (0.35) acts on it");
        // age: 0.5 h
        var old = new DecFx();
        Opening o = old.Openings[0];
        o.FirstSeenHours = 9.4;
        old.Openings[0] = o;
        AssertEqual("age", old.Decide(Persona()).Reason, "an opening seen 0.6 h ago is stale");
        o.FirstSeenHours = 9.5;
        old.Openings[0] = o;
        AssertTrue(old.Decide(Persona()).Reason != "age", "0.5 h is still fresh (inclusive)");
        var unconfirmed = new DecFx();
        Opening u = unconfirmed.Openings[0];
        u.Confirmed = false;
        unconfirmed.Openings[0] = u;
        AssertEqual("no-pair", unconfirmed.Decide(Persona()).Reason, "an unconfirmed opening is never a pair");
    }

    private static void OppFloor()
    {
        AssertNear(0.30f, OpportunityDoctrine.FloorShare(0.5f, 0.5f), 1e-5f, "mid commander");
        AssertNear(0.30f + 0.2f * 0.5f, OpportunityDoctrine.FloorShare(1f, 0.5f), 1e-5f, "a cautious commander keeps more (caution 1: 0.40)");
        AssertNear(0.30f - 0.1f * 0.5f, OpportunityDoctrine.FloorShare(0.5f, 1f), 1e-5f, "a tolerant one keeps less (tolerance 1: 0.25)");
        AssertNear(0.15f, OpportunityDoctrine.FloorShare(0f, 1f), 1e-5f, "clamped at 0.15 (0.30 - 0.10 - 0.05 = 0.15)");
        AssertEqual(0.45f, OpportunityDoctrine.FloorShare(1f, -9f), "clamped at 0.45");
        AssertEqual(2, OpportunityDoctrine.FloorCount(5, 0.30f), "ceil(5 x 0.30) = 2");
        AssertEqual(0, OpportunityDoctrine.FloorCount(2, 0.45f), "n <= 2: no floor");
        AssertEqual(0, OpportunityDoctrine.FloorCount(1, 0.45f), "n = 1: no floor");
        AssertEqual(1, OpportunityDoctrine.FloorCount(3, 0.30f), "ceil(3 x 0.30) = 1");
        // The decision: five children, floor 2 -> three uncommitted after the asset leaves is fine, two is not... one asset leaving four -> 3
        var ok = new DecFx { Children = 5, Uncommitted = 3 };   // asset uncommitted: 2 left >= floor 2
        AssertTrue(ok.Decide(Persona()).Reason != "reserve-floor", "two uncommitted children remain: the floor holds");
        var short_ = new DecFx { Children = 5, Uncommitted = 2 };   // 1 left < 2
        AssertEqual("reserve-floor", short_.Decide(Persona()).Reason, "one uncommitted child left is below the floor");
        var small = new DecFx { Children = 2, Uncommitted = 1 };
        AssertTrue(small.Decide(Persona()).Reason != "reserve-floor", "a node with two children has no floor");
    }

    // A line of own groups for the integrity checks: groups at lateral 0, 130, 260, 390, each holding one regiment of
    // width 120 (a 10 m edge gap between neighbours); a perceived enemy 300 m ahead.
    private static BattleSnapshot LineSnapshot(out int[] groupIndex)
    {
        var fx = new DetFixture();
        groupIndex = new int[4];
        for (int i = 0; i < 4; i++)
        {
            groupIndex[i] = fx.OwnGroup();
            fx.OwnInf(i * 130f, 0f, 120f, groupIndex[i]);
        }
        fx.Inf(200f, 300f);
        return fx.Build();
    }

    private static DecFx LineAsset(BattleSnapshot s, int groupIndex, float lateral)
    {
        var fx = new DecFx();
        fx.Snapshot = s;
        fx.Assets.Clear();
        ExploitAsset a = DecFx.Asset(groupIndex, x: lateral, z: 0f);
        a.OwnIndex = groupIndex;
        a.HasFrontage = true;
        a.InContact = false;
        fx.Assets.Add(a);
        fx.Openings.Clear();
        fx.Openings.Add(DecFx.GapOpening(1, lateral, 400f));
        return fx;
    }

    private static void OppIntegrity()
    {
        // groups at 0, 250, 500: the middle one leaving opens a 380 m edge gap facing the enemy.
        var fxs = new DetFixture();
        int g0 = fxs.OwnGroup(); fxs.OwnInf(0f, 0f, 120f, g0);
        int g1 = fxs.OwnGroup(); fxs.OwnInf(250f, 0f, 120f, g1);
        int g2 = fxs.OwnGroup(); fxs.OwnInf(500f, 0f, 120f, g2);
        fxs.Inf(250f, 300f);
        BattleSnapshot s = fxs.Build();
        var fx = LineAsset(s, g1, 250f);
        DecisionResult r = fx.Decide(Persona());
        AssertEqual("line-integrity", r.Reason, "removing the middle brigade leaves a 380 m gap");
        // an asset in the line that leaves no gap is fine; one not in the line is never tested
        var edge = LineAsset(s, g2, 500f);
        AssertTrue(edge.Decide(Persona()).Reason != "line-integrity", "the end brigade leaving opens no gap between the others");
        var behind = LineAsset(s, g1, 250f);
        ExploitAsset a = behind.Assets[0];
        a.HasFrontage = false;
        behind.Assets[0] = a;
        AssertTrue(behind.Decide(Persona()).Reason != "line-integrity", "an asset standing outside the line is not tested");
    }

    private static void OppIntegritySibling()
    {
        BattleSnapshot s = LineSnapshot(out int[] g);
        // Alone, group 1 or group 2 leaves a 140 m gap (below 150): fine.
        var alone = LineAsset(s, g[2], 260f);
        AssertTrue(alone.Decide(Persona()).Reason != "line-integrity", "one brigade of the closed line leaving opens only 140 m");
        // The sibling (group 1) already launched this tick: the ledger holds it, and group 2 leaving would open 270 m.
        var sibling = LineAsset(s, g[2], 260f);
        sibling.Ledger.BeginTick();
        sibling.Ledger.Add(g[1]);
        AssertEqual("line-integrity", sibling.Decide(Persona()).Reason, "the commitment ledger carries the sibling's launch");
        // The same holds when the first exploit is live from an earlier tick: the producer seeds the ledger with it.
        var earlier = LineAsset(s, g[2], 260f);
        earlier.Ledger.BeginTick();
        earlier.Ledger.Add(g[1]);
        earlier.Ledger.Add(g[1]);
        AssertEqual(1, earlier.Ledger.Roots.Length, "adding the same root twice keeps one");
        AssertEqual("line-integrity", earlier.Decide(Persona()).Reason, "a live exploit whose asset still stands in its slot counts");
    }

    private static void OppMain()
    {
        var fx = new DecFx();
        ExploitAsset a = fx.Assets[0];
        a.InMainEffort = true;
        fx.Assets[0] = a;
        AssertEqual("main-effort", fx.Decide(Persona()).Reason, "the main effort does not go chasing openings");
        // unless the opening's aim lies in the main-effort sector
        Opening o = fx.Openings[0];
        o.Sector = fx.MainEffortSector;
        fx.Openings[0] = o;
        AssertTrue(fx.Decide(Persona()).Reason != "main-effort", "an opening in the main-effort sector pushes its own direction");
    }

    private static void OppRiver()
    {
        var fx = new DecFx();
        fx.Terrain.SegmentRiver = true;
        AssertEqual("recovery-path", fx.Decide(Persona()).Reason, "an open river on the segment");
        fx.Terrain.SegmentRiver = false;
        AssertTrue(fx.Decide(Persona()).Reason != "recovery-path", "a ford or a bridge (the terrain reports no river) passes");
    }

    private static void OppDefender()
    {
        var gap = new DecFx { Stance = BattleStance.Defender };
        AssertEqual("stance", gap.Decide(Persona()).Reason, "a Defender never launches a Gap");
        var pen = new DecFx { Stance = BattleStance.Defender };
        Opening o = pen.Openings[0];
        o.Kind = OpeningKind.Penetration;
        o.TargetObservation = 900;
        pen.Openings[0] = o;
        AssertTrue(pen.Decide(Persona()).Reason != "stance", "a Defender may counterstroke a Penetration");
    }

    private static void OppConcurrency()
    {
        var capped = new DecFx { SideLive = OpportunityDoctrine.MaxConcurrentExploits };
        AssertEqual("concurrency", capped.Decide(Persona()).Reason, "three live exploits on the side");
        var node = new DecFx { NodeHasExploit = true };
        AssertEqual("concurrency", node.Decide(Persona()).Reason, "the node already has one");
        var claimed = new DecFx();
        Opening o = claimed.Openings[0];
        o.Claimed = true;
        claimed.Openings[0] = o;
        AssertEqual("concurrency", claimed.Decide(Persona()).Reason, "the opening is claimed by another exploit");
        var two = new DecFx { SideLive = OpportunityDoctrine.MaxConcurrentExploits - 1 };
        AssertTrue(two.Decide(Persona()).Reason != "concurrency", "two live exploits leave room for a third");
    }

    private static void OppPhase()
    {
        var withdraw = new DecFx { Phase = BattlePhase.Withdraw };
        AssertEqual("phase", withdraw.Decide(Persona()).Reason, "no exploit in the Withdraw phase");
        var retreat = new DecFx { MacroAi = 3 };
        AssertEqual("phase", retreat.Decide(Persona()).Reason, "no exploit in an army retreat");
    }

    private static void OppOrder()
    {
        int[] depth = { 1, 3, 3, 2 };
        int[] id = { 40, 30, 20, 10 };
        var order = new int[4];
        OpportunityDoctrine.DecisionOrder(depth, id, 4, order);
        AssertEqual(2, order[0], "deepest first, the lower id first among equals (id 20)");
        AssertEqual(1, order[1], "then id 30");
        AssertEqual(3, order[2], "then depth 2");
        AssertEqual(0, order[3], "the shallowest last");
    }

    private static void OppDue()
    {
        const double cycle = 0.015;
        // First decision staggered by (commander mod 4) x the side-stat cycle after first sight.
        AssertTrue(OpportunityDoctrine.Due(10.0, 10.0, double.NegativeInfinity, 4, cycle), "commander 4 mod 4 = 0: at once");
        AssertFalse(OpportunityDoctrine.Due(10.0, 10.0, double.NegativeInfinity, 7, cycle), "commander 7 mod 4 = 3: 0.045 h later");
        AssertTrue(OpportunityDoctrine.Due(10.046, 10.0, double.NegativeInfinity, 7, cycle), "the stagger has passed");
        // Then every DecisionIntervalHours.
        AssertFalse(OpportunityDoctrine.Due(10.04, 10.0, 10.0, 0, cycle), "0.04 h after the last decision");
        AssertTrue(OpportunityDoctrine.Due(10.05, 10.0, 10.0, 0, cycle), "0.05 h after");
    }

    private static void OppRoll()
    {
        float a = OpportunityDoctrine.Roll(7, 42, 1, 3);
        AssertEqual(a, OpportunityDoctrine.Roll(7, 42, 1, 3), "deterministic");
        AssertTrue(a >= 0f && a < 1f, "in [0, 1)");
        AssertTrue(OpportunityDoctrine.Roll(7, 42, 1, 4) != a || OpportunityDoctrine.Roll(7, 43, 1, 3) != a, "the inputs matter");
        AssertTrue(OpportunityDoctrine.Scale(0xFFFFFFFFu) < 1f, "the largest hash stays below 1");
        AssertEqual(0f, OpportunityDoctrine.Scale(0u), "and the smallest is 0");
        // The FNV-1a vector, computed independently over the four little-endian ints.
        uint h = 2166136261u;
        foreach (int v in new[] { 7, 42, 1, 3 })
            for (int i = 0; i < 4; i++) { h ^= (uint)((v >> (8 * i)) & 0xFF); h *= 16777619u; }
        AssertEqual((h >> 8) * (1f / 16777216f), a, "FNV-1a, top 24 bits scaled by 2^-24");
        // roughly uniform
        int below = 0;
        for (int n = 0; n < 4000; n++) if (OpportunityDoctrine.Roll(1, 2, 3, n) < 0.5f) below++;
        AssertTrue(below > 1800 && below < 2200, "uniform enough: " + below);
    }

    private static void OppDuty()
    {
        OpportunityPersonality inDuty = Persona(duty: CommanderPersonalityDerivation.Duty(0, 0.5f));
        OpportunityPersonality hit = Persona(duty: CommanderPersonalityDerivation.Duty(3, 0.5f));
        float full = OpportunityDoctrine.Chance(OpeningKind.Gap, in inDuty);
        float half = OpportunityDoctrine.Chance(OpeningKind.Gap, in hit);
        AssertNear(full * 0.5f, half, 1e-5f, "status != 0 halves the chance");
        AssertEqual(0.95f, OpportunityDoctrine.Chance(OpeningKind.Gap, Persona(agg01: 1f, audacity01: 1f)), "capped at 0.95");
        AssertTrue(OpportunityDoctrine.Chance(OpeningKind.FlankInAir, in inDuty) < OpportunityDoctrine.Chance(OpeningKind.Gap, in inDuty), "flank 0.4 < gap 0.6");
    }

    private delegate void TweakContext(ref AssetContext c);

    private static AssetContext EligibleContext() => new AssetContext { DrivenByPosture = true, LossFraction = 0f, CavalryMoraleBar = 0.72f };

    private static void OppEligibility()
    {
        var group = new UnitRecord { Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup, GroupMorale = 0.8f };
        Func<TweakContext, string> reasonOf = tweak =>
        {
            AssetContext c = EligibleContext();
            tweak(ref c);
            return OpportunityDoctrine.Eligible(in group, in c, out string r) ? "ok" : r;
        };
        AssertEqual("ok", reasonOf((ref AssetContext c) => { }), "the baseline asset is eligible");
        AssertEqual("not-driven", reasonOf((ref AssetContext c) => { c.DrivenByPosture = false; }), "driven by the posture producer");
        AssertEqual("losses", reasonOf((ref AssetContext c) => { c.LossFraction = 0.31f; }), "loss fraction above 0.3");
        AssertEqual("ok", reasonOf((ref AssetContext c) => { c.LossFraction = 0.3f; }), "0.3 is the limit (inclusive)");
        AssertEqual("moving", reasonOf((ref AssetContext c) => { c.ActiveMove = true; }), "settled: not moving");
        AssertEqual("pending-order", reasonOf((ref AssetContext c) => { c.PendingOrder = true; }), "no pending order");
        AssertEqual("recent-order", reasonOf((ref AssetContext c) => { c.RecentExecutorMove = true; }), "no executor Move within the recent-order window");
        AssertEqual("maneuver", reasonOf((ref AssetContext c) => { c.UnderManeuver = true; }), "no BC11 maneuver");
        AssertEqual("recon", reasonOf((ref AssetContext c) => { c.UnderRecon = true; }), "no BC9 recon mission");
        AssertEqual("probe-hold", reasonOf((ref AssetContext c) => { c.UnderProbeHold = true; }), "no probe hold");
        AssertEqual("arrival-hold", reasonOf((ref AssetContext c) => { c.UnderArrivalHold = true; }), "no main-arrival hold");
        AssertEqual("exploit", reasonOf((ref AssetContext c) => { c.ExploitInSubtreeOrAncestor = true; }), "no live exploit on it, an ancestor or a descendant");
        AssertEqual("safety", reasonOf((ref AssetContext c) => { c.SafetyClaim = true; }), "no Safety claim");
        AssertEqual("cooldown", reasonOf((ref AssetContext c) => { c.InCooldown = true; }), "not in its post-exploit cooldown");

        var routed = group;
        routed.Flags |= UnitFlags.Routed;
        AssetContext ctx = EligibleContext();
        AssertFalse(OpportunityDoctrine.Eligible(in routed, in ctx, out string r1), "routed");
        AssertEqual("routed", r1, "routed reason");
        var melee = group;
        melee.Flags |= UnitFlags.InMelee;
        AssertFalse(OpportunityDoctrine.Eligible(in melee, in ctx, out r1), "in melee");
        AssertEqual("in-melee", r1, "melee reason");
        var shaky = group;
        shaky.GroupMorale = 0.59f;
        AssertFalse(OpportunityDoctrine.Eligible(in shaky, in ctx, out r1), "group morale below 0.6");
        AssertEqual("morale", r1, "morale reason");
        shaky.GroupMorale = 0.6f;
        AssertTrue(OpportunityDoctrine.Eligible(in shaky, in ctx, out r1), "0.6 is the bar (inclusive)");
    }

    private static void OppPlayer()
    {
        var group = new UnitRecord { Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup, GroupMorale = 0.8f };
        AssetContext ctx = EligibleContext();
        var owned = group;
        owned.Flags |= UnitFlags.PlayerOwned;
        AssertFalse(OpportunityDoctrine.Eligible(in owned, in ctx, out string reason), "a player-owned asset");
        AssertEqual("player", reason, "reason");
        var ancestor = group;
        ancestor.Flags |= UnitFlags.AncestorOfPlayerCommand;
        AssertFalse(OpportunityDoctrine.Eligible(in ancestor, in ctx, out reason), "an ancestor of the player's command");
        ctx.DescendantPlayerOwned = true;
        AssertFalse(OpportunityDoctrine.Eligible(in group, in ctx, out reason), "a player-owned descendant");
        AssertEqual("player", reason, "descendant reason");

        // The player's command node never decides; an AI division above it does.
        var fx = new DetFixture();
        int army = fx.OwnGroup();
        int division = fx.OwnGroup(army);
        int mine = fx.OwnGroup(division);
        var s = fx.Build();
        s.Own[army].UnitTyp = TacticalUnitType.BattleGroupArmy;
        s.Own[division].UnitTyp = TacticalUnitType.BattleGroupDivision;
        s.Own[mine].UnitTyp = TacticalUnitType.BattleGroupDivision;
        s.Own[mine].Flags |= UnitFlags.PlayerCommand | UnitFlags.PlayerOwned;
        s.Own[division].Flags |= UnitFlags.AncestorOfPlayerCommand;
        var nodes = new int[8];
        var depths = new int[8];
        int n = OpportunityDoctrine.CollectDecidingNodes(s, nodes, depths);
        AssertEqual(1, n, "one deciding node");
        AssertEqual(division, nodes[0], "the AI division above the player decides");
    }

    private static void OppFeud()
    {
        var group = new UnitRecord { Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup | UnitFlags.Feud, GroupMorale = 0.8f };
        AssetContext ctx = EligibleContext();
        AssertFalse(OpportunityDoctrine.Eligible(in group, in ctx, out string reason), "a feud asset");
        AssertEqual("feud", reason, "reason");
        group.Flags &= ~UnitFlags.Feud;
        ctx.DescendantFeud = true;
        AssertFalse(OpportunityDoctrine.Eligible(in group, in ctx, out reason), "a feud descendant");
        AssertEqual("feud", reason, "descendant reason");
    }

    private static void OppArmyDecides()
    {
        var fx = new DetFixture();
        int army = fx.OwnGroup();
        int b1 = fx.OwnGroup(army);
        int b2 = fx.OwnGroup(army);
        var s = fx.Build();
        s.Own[army].UnitTyp = TacticalUnitType.BattleGroupArmy;
        s.Own[b1].UnitTyp = TacticalUnitType.BattleGroupBrigade;
        s.Own[b2].UnitTyp = TacticalUnitType.BattleGroupBrigade;
        var nodes = new int[8];
        var depths = new int[8];
        int n = OpportunityDoctrine.CollectDecidingNodes(s, nodes, depths);
        AssertEqual(1, n, "with no corps or division the army node decides");
        AssertEqual(army, nodes[0], "the army");

        // With a division present the army does not decide.
        var fx2 = new DetFixture();
        int a2 = fx2.OwnGroup();
        int d2 = fx2.OwnGroup(a2);
        fx2.OwnGroup(d2);
        var s2 = fx2.Build();
        s2.Own[a2].UnitTyp = TacticalUnitType.BattleGroupArmy;
        s2.Own[d2].UnitTyp = TacticalUnitType.BattleGroupDivision;
        s2.Own[2].UnitTyp = TacticalUnitType.BattleGroupBrigade;
        int n2 = OpportunityDoctrine.CollectDecidingNodes(s2, nodes, depths);
        AssertEqual(1, n2, "the division decides");
        AssertEqual(d2, nodes[0], "not the army");
        AssertEqual(1, depths[0], "depth of the division");
    }

    private static void OppContract()
    {
        OpportunityPersonality p = Persona();
        float chance = OpportunityDoctrine.Chance(OpeningKind.Gap, in p);
        var fx = new DecFx();
        // launch iff roll < chance
        for (int pass = 0; pass < 2; pass++)
        {
            fx.DecisionNumber = NumberWithRoll(fx.BattleKey, fx.Commander, 1, chance, below: pass == 0);
            DecisionResult r = fx.Decide(in p);
            AssertEqual(pass == 0, r.Launch, "launch iff roll < chance (pass " + pass + ")");
            AssertEqual(pass == 0 ? "launch" : "roll-miss", r.Reason, "reason (pass " + pass + ")");
        }
        // no pair: the last failing reason; none: no-pair
        var none = new DecFx();
        none.Openings.Clear();
        AssertEqual("no-pair", none.Decide(in p).Reason, "no opening: no-pair");
        var odds = new DecFx();
        Opening o = odds.Openings[0];
        o.LocalEnemyValue = 5000f;
        odds.Openings[0] = o;
        AssertEqual("odds", odds.Decide(in p).Reason, "a pair that fails reports its reason");
        // several pairs: the best by score is tried first; on a failure the next
        var two = new DecFx();
        two.Openings.Clear();
        Opening strong = DecFx.GapOpening(1, 0f, 400f);
        strong.LocalEnemyValue = 5000f;       // fails the odds test
        Opening ok = DecFx.GapOpening(2, 100f, 400f);
        ok.Confidence01 = 0.9f;               // a lower score but it passes
        two.Openings.Add(strong);
        two.Openings.Add(ok);
        two.DecisionNumber = NumberWithRoll(two.BattleKey, two.Commander, 2, chance, below: true);
        DecisionResult chosen = two.Decide(in p);
        AssertTrue(chosen.Launch, "the second pair launches after the first fails: " + chosen.Reason);
        AssertEqual(2, chosen.OpeningId, "the passing opening");
        // Decide is pure: it takes the lookup read-only and never marks anything.
        AssertEqual(typeof(IHesitationLookup).GetMethods().Length, 1, "the lookup only reads");
    }

    private sealed class MarkedHesitation : IHesitationLookup
    {
        public int Commander, Opening;
        public bool IsHesitating(int commanderIndex, int openingId, double nowHours) => commanderIndex == Commander && openingId == Opening;
    }

    private static void OppHesitation()
    {
        var fx = new DecFx { Hesitation = new MarkedHesitation { Commander = 42, Opening = 1 } };
        AssertEqual("no-pair", fx.Decide(Persona()).Reason, "the commander passed this opening over");
        var other = new DecFx { Commander = 43, Hesitation = new MarkedHesitation { Commander = 42, Opening = 1 } };
        AssertTrue(other.Decide(Persona()).Reason != "no-pair", "another commander may take it");
        var store = new HesitationStore();
        store.Mark(42, 1, 10.0);
        AssertTrue(store.IsHesitating(42, 1, 10.05), "marked for 0.1 h");
        AssertFalse(store.IsHesitating(42, 1, 10.11), "free again after HesitationHours");
        AssertFalse(store.IsHesitating(43, 1, 10.05), "per commander");
        AssertFalse(store.IsHesitating(42, 2, 10.05), "per opening");
        store.Clear();
        AssertFalse(store.IsHesitating(42, 1, 10.05), "cleared");
    }

    private static void CounterAsset()
    {
        Func<bool, bool, string> run = (frontage, cavalry) =>
        {
            var fx = new DecFx { Stance = BattleStance.Defender };
            ExploitAsset a = DecFx.Asset(1, cavalry: cavalry);
            a.HasFrontage = frontage;
            fx.Assets.Clear();
            fx.Assets.Add(a);
            Opening o = fx.Openings[0];
            o.Kind = OpeningKind.Penetration;
            o.TargetObservation = 900;
            o.Width = 0f;
            fx.Openings[0] = o;
            return fx.Decide(Persona()).Reason;
        };
        AssertEqual("no-pair", run(true, false), "a frontage-holding child is never the counterstroke asset");
        AssertTrue(run(false, false) != "no-pair", "a reserve or second-line child is");
        AssertEqual("no-pair", run(false, true), "cavalry never is");
    }

    private static void CounterFlag()
    {
        var fx = new DecFx { Stance = BattleStance.Defender, CounterstrokeOn = false };
        Opening o = fx.Openings[0];
        o.Kind = OpeningKind.Penetration;
        o.TargetObservation = 900;
        o.Width = 0f;
        fx.Openings[0] = o;
        AssertEqual("stance", fx.Decide(Persona()).Reason, "with the flag off a Defender never launches");
        fx.CounterstrokeOn = true;
        AssertTrue(fx.Decide(Persona()).Reason != "stance", "with it on it may");
    }
}
