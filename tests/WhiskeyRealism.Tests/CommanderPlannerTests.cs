using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// Stage G slice G2 (docs/superpowers/plans/2026-10-15-campaign-planner.md § G2): commander
// profiles, the competence-plus-prior appointment planner, the appointment protection window,
// the #6 job-24 guard and the succession prior.
internal static class CommanderPlannerTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("ProfileHighInitiativeLowersRequiredOdds", ProfileHighInitiativeLowersRequiredOdds),
            ("ProfileNeutralForUnknownCommander", ProfileNeutralForUnknownCommander),
            ("AppointmentNeedsMinGain", AppointmentNeedsMinGain),
            ("SuccessionPriorTipsANearTie", SuccessionPriorTipsANearTie),
            ("SuccessionPriorCannotBeatALargeCompetenceGap", SuccessionPriorCannotBeatALargeCompetenceGap),
            ("SittingSuccessionFigureKeepsHisBonus", SittingSuccessionFigureKeepsHisBonus),
            ("NeverAppointsPlayerOrPlayerSubordinate", NeverAppointsPlayerOrPlayerSubordinate),
            ("NeverTargetsPlayerOwnedCommand", NeverTargetsPlayerOwnedCommand),
            ("OnlyVanillasAppointmentPoolIsCandidate", OnlyVanillasAppointmentPoolIsCandidate),
            ("NeverDisplacesThePlayer", NeverDisplacesThePlayer),
            ("ProtectedCommanderNotDisplaced", ProtectedCommanderNotDisplaced),
            ("ProtectedCandidateNotMovedAgain", ProtectedCandidateNotMovedAgain),
            ("ProtectionExpiresAfterSixtyDays", ProtectionExpiresAfterSixtyDays),
            ("ProtectionRestoreRoundTrips", ProtectionRestoreRoundTrips),
            ("GuardRestoresOnlyTheSlotsItCleared", GuardRestoresOnlyTheSlotsItCleared),
            ("OneAppointmentPerReview", OneAppointmentPerReview),
            ("ArmyGroupTargetFirstWithItsRankBand", ArmyGroupTargetFirstWithItsRankBand),
            ("RankBandMinimumOnlyForArmyGroups", RankBandMinimumOnlyForArmyGroups),
            ("VanillaRefusalTriesTheNextCandidate", VanillaRefusalTriesTheNextCandidate),
            ("MainEffortCommanderNotPulledSideways", MainEffortCommanderNotPulledSideways),
            ("OnlyMainEffortCommandsAreTargets", OnlyMainEffortCommandsAreTargets),
            ("NoMainEffortNoAppointment", NoMainEffortNoAppointment),
            ("TargetInBattleSkipped", TargetInBattleSkipped),
            ("CommanderAlliancesTwoToFourIgnored", AlliancesTwoToFourIgnored),
            ("NoVerticalSwapOnPlayerSideInCareer", NoVerticalSwapOnPlayerSideInCareer),
            ("RoleFollowsTheaterAndPrimacy", RoleFollowsTheaterAndPrimacy),
            ("ValleyRoleNeedsAShenandoahCommand", ValleyRoleNeedsAShenandoahCommand),
            ("SuccessionPriorOnlyForFiredNotApplied", SuccessionPriorOnlyForFiredNotApplied),
            ("SuccessionReseatMovesAFigureOffHisArmyGroup", SuccessionReseatMovesAFigureOffHisArmyGroup),
            ("SuccessionReseatDefersWithoutIdleSuccessor", SuccessionReseatDefersWithoutIdleSuccessor),
            ("SuccessionReseatNeverOnThePlayersSideOrGroup", SuccessionReseatNeverOnThePlayersSideOrGroup),
            ("VanillaChecksCappedAtEight", VanillaChecksCappedAtEight),
            ("ProtectionBookCountsFromTheLastReview", ProtectionBookCountsFromTheLastReview),
            ("GuardClearsThisAlliancesProtectedOnly", GuardClearsThisAlliancesProtectedOnly),
            ("VanillaWouldReplaceMirrorsJob24", VanillaWouldReplaceMirrorsJob24),
            ("GuardPatchNeverSkipsVanilla", GuardPatchNeverSkipsVanilla),
            ("AppointmentStepWiredIntoTheReview", AppointmentStepWiredIntoTheReview),
            ("ExecutorMirrorsVanillaRecipe", ExecutorMirrorsVanillaRecipe),
        });
    }

    private const int Day = 1000;

    // ---- CommanderProfiles (spec component 3, C2) ----

    private static void ProfileHighInitiativeLowersRequiredOdds()
    {
        var bold = CommanderProfiles.From(Cmd(1, 8, 0.6f, initiative: 0.9f, leadership: 0.5f, administration: 0.8f));
        var timid = CommanderProfiles.From(Cmd(2, 8, 0.6f, initiative: 0.1f, leadership: 0.5f, administration: 0.2f));
        AssertNear(-0.2f, bold.RequiredOddsShift, 1e-5f, "(0.9 - 0.5) * -0.5");
        AssertNear(0.2f, timid.RequiredOddsShift, 1e-5f, "(0.1 - 0.5) * -0.5");
        AssertTrue(bold.RequiredOddsShift < timid.RequiredOddsShift, "a bold commander accepts worse odds");
        AssertNear(0.65f, bold.AttackBias, 1e-5f, "initiative - leadership * 0.5");
        AssertNear(-0.15f, timid.AttackBias, 1e-5f, "attack bias can be negative");
        AssertNear(0.2f, bold.SupplySlack, 1e-5f, "administration * 0.25");
        var extreme = CommanderProfiles.From(Cmd(3, 8, 0.6f, initiative: 3f, leadership: -4f, administration: 2f));
        AssertNear(1f, extreme.AttackBias, 1e-5f, "attack bias clamped to [-1, 1]");
    }

    private static void ProfileNeutralForUnknownCommander()
    {
        var list = new List<CommanderRecord> { Cmd(7, 8, 0.6f, initiative: 0.8f, leadership: 0.4f, administration: 0.4f) };
        var neutral = CommanderProfiles.Neutral;
        AssertEqual(0f, neutral.RequiredOddsShift, "neutral odds");
        AssertEqual(0f, neutral.AttackBias, "neutral bias");
        AssertEqual(0f, neutral.SupplySlack, "neutral slack");
        AssertEqual(neutral, CommanderProfiles.ForSector(list, 99), "unknown id -> neutral");
        AssertEqual(neutral, CommanderProfiles.ForSector(list, -1), "no commander -> neutral");
        AssertEqual(neutral, CommanderProfiles.ForSector(null, 7), "no list -> neutral");
        AssertEqual(CommanderProfiles.From(list[0]), CommanderProfiles.ForSector(list, 7), "known id -> its profile");
    }

    // ---- CommanderAppointmentPlanner ----

    private static void AppointmentNeedsMinGain()
    {
        var f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.40f + 0.09f));
        AssertFalse(f.Plan(out _), "a gain below MinGain (0.10) appoints nobody");

        f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.40f + 0.11f));
        AssertTrue(f.Plan(out var intent), "a gain above MinGain appoints");
        AssertEqual(20, intent.CommanderId, "the better commander");
        AssertEqual(Fixture.Army, intent.TargetInstanceId, "onto the main-effort army");
        AssertEqual(1, intent.DisplacedId, "displacing the sitting commander");
        AssertFalse(intent.TargetIsArmyGroup, "an army HQ, not an army group");
        AssertEqual("competence", intent.Reason, "competence reason");
        AssertEqual(-1, intent.SuccessionEventId, "no succession event");
        AssertEqual(0.10f, CommanderAppointmentPlanner.MinGain, "MinGain = vanilla expdifferenceforverticalpromotion");
    }

    private static void SuccessionPriorTipsANearTie()
    {
        var f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.56f, name: "Joseph E. Johnston"));
        f.Snapshot.Commanders.Add(Cmd(21, 8, 0.53f, name: "Robert E. Lee"));
        f.Prior = PriorFor(21, "ANV", 1);
        AssertTrue(f.Plan(out var intent), "appointment");
        AssertEqual(21, intent.CommanderId, "the succession figure wins the near tie");
        AssertEqual("succession:1", intent.Reason, "succession reason");
        AssertEqual(1, intent.SuccessionEventId, "event id for MarkApplied");

        f.Prior = null;
        AssertTrue(f.Plan(out intent), "appointment without a prior");
        AssertEqual(20, intent.CommanderId, "competence alone picks the better commander");
    }

    private static void SuccessionPriorCannotBeatALargeCompetenceGap()
    {
        var f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.80f));
        f.Snapshot.Commanders.Add(Cmd(21, 8, 0.50f));
        f.Prior = PriorFor(21, "ANV", 1);
        AssertTrue(f.Plan(out var intent), "appointment");
        AssertEqual(20, intent.CommanderId, "a 0.30 competence gap beats the 0.25 prior");

        f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(21, 8, 0.40f - 0.20f));
        f.Prior = PriorFor(21, "ANV", 1);
        AssertFalse(f.Plan(out _), "prior 0.25 over a commander 0.20 worse is a gain of 0.05 < MinGain");
    }

    private static void SittingSuccessionFigureKeepsHisBonus()
    {
        var f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.40f + 0.14f));
        f.Prior = PriorFor(1, "ANV", 1);
        AssertFalse(f.Plan(out _), "the sitting historical commander keeps his prior against a slightly better challenger");
    }

    private static void NeverAppointsPlayerOrPlayerSubordinate()
    {
        var f = new Fixture();
        var player = Cmd(20, 8, 0.90f);
        player.IsPlayer = true;
        var staff = Cmd(21, 8, 0.85f);
        staff.ServingUnderPlayer = true;
        f.Snapshot.Commanders.Add(player);
        f.Snapshot.Commanders.Add(staff);
        AssertFalse(f.Plan(out _), "neither the W&L player nor a commander serving under him is appointed");

        f.Snapshot.Commanders.Add(Cmd(22, 8, 0.60f));
        AssertTrue(f.Plan(out var intent), "the next commander is");
        AssertEqual(22, intent.CommanderId, "an ordinary commander");
    }

    private static void NeverTargetsPlayerOwnedCommand()
    {
        // The player's brigade sits in the main-effort corps (its carrying group): neither the
        // corps nor the army above it may get a new commander, so the target falls to nothing.
        var f = new Fixture(PlayerCommandClass.BelowMapLevel);
        f.SetChain(Fixture.Corps, WlChainClass.CarryingGroup);
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.90f));
        AssertFalse(f.Plan(out _), "the carrying corps and its army are never targets");

        // A map-group player's own corps marks its ancestors too.
        f = new Fixture(PlayerCommandClass.MapGroup);
        f.SetChain(Fixture.Corps, WlChainClass.PlayerMapGroup);
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.90f));
        AssertFalse(f.Plan(out _), "the player's map group and its army are never targets");

        // An army group holding a formation that contains the player is never a target either.
        f = new Fixture(PlayerCommandClass.BelowMapLevel);
        f.AddArmyGroup();
        f.SetChain(Fixture.Corps, WlChainClass.CarryingGroup);
        f.Snapshot.Commanders.Add(Cmd(20, 9, 0.95f));
        AssertFalse(f.Plan(out _), "the army group above the player's corps is never a target");

        // The player's own command found only by PlayerCommandInstanceId.
        f = new Fixture(PlayerCommandClass.MapGroup, playerCommand: Fixture.Corps);
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.90f));
        AssertFalse(f.Plan(out _), "PlayerCommandInstanceId and its ancestors are never targets");
    }

    // CanBePickedForAIReplacement (60748-60779) rejects the CIC, staff officers and sitting
    // army-group commanders; the planner never proposes them (or the day's one appointment
    // would be refused forever).
    private static void OnlyVanillasAppointmentPoolIsCandidate()
    {
        var f = new Fixture();
        var cic = Cmd(20, 9, 0.95f);
        cic.Pickable = false;
        f.Snapshot.Commanders.Add(cic);
        AssertFalse(f.Plan(out _), "a commander outside vanilla's AI pool is never a candidate");
        f.Snapshot.Commanders.Add(Cmd(21, 8, 0.60f));
        AssertTrue(f.Plan(out var intent), "a pickable one is");
        AssertEqual(21, intent.CommanderId, "vanilla's pool");
    }

    private static void NeverDisplacesThePlayer()
    {
        var f = new Fixture();
        f.ReplaceCommander(1, c => { c.IsPlayer = true; return c; });
        f.Snapshot.Commanders.Add(Cmd(20, 9, 0.90f)); // a full general fits the army, not the corps
        AssertFalse(f.Plan(out _), "the W&L player in command is never displaced");

        f = new Fixture();
        f.ReplaceCommander(1, c => { c.ServingUnderPlayer = true; return c; });
        f.Snapshot.Commanders.Add(Cmd(20, 9, 0.90f));
        AssertFalse(f.Plan(out _), "a commander serving under the player is never displaced");
    }

    private static void ProtectedCommanderNotDisplaced()
    {
        var f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(20, 9, 0.90f)); // fits the army only
        f.Protection.Protect(1, Day - 10);
        AssertFalse(f.Plan(out _), "a protected army commander is not displaced");

        f.Protection.Protect(1, Day - AppointmentProtection.ProtectionDays);
        AssertTrue(f.Plan(out var intent), "after the window he can be displaced");
        AssertEqual(Fixture.Army, intent.TargetInstanceId, "the army again");
    }

    private static void ProtectedCandidateNotMovedAgain()
    {
        var f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.90f));
        f.Protection.Protect(20, Day - 5);
        AssertFalse(f.Plan(out _), "a commander appointed within the window is not moved again");
    }

    private static void ProtectionExpiresAfterSixtyDays()
    {
        var p = new AppointmentProtection();
        p.Protect(5, 100);
        AssertTrue(p.IsProtected(5, 100), "protected on the appointment day");
        AssertTrue(p.IsProtected(5, 159), "protected on day 60");
        AssertFalse(p.IsProtected(5, 160), "expired after 60 days");
        AssertFalse(p.IsProtected(6, 100), "unknown commander");
        AssertEqual(60, AppointmentProtection.ProtectionDays, "ProtectionDays");
        p.Prune(159);
        AssertTrue(p.IsProtected(5, 159), "prune keeps an entry on its last protected day");
        p.Prune(160);
        foreach (var _ in p.ProtectedUntil) throw new Exception("prune drops expired entries");
    }

    private static void ProtectionRestoreRoundTrips()
    {
        var p = new AppointmentProtection();
        p.Protect(5, 100);
        p.Protect(9, 120);
        var copy = new AppointmentProtection();
        copy.Restore(p.ProtectedUntil);
        AssertTrue(copy.IsProtected(5, 159) && !copy.IsProtected(5, 160), "restored window 5");
        AssertTrue(copy.IsProtected(9, 179) && !copy.IsProtected(9, 180), "restored window 9");
        copy.Restore(new KeyValuePair<int, int>[0]);
        AssertFalse(copy.IsProtected(5, 100), "restore replaces");
    }

    // P13: the Prefix clears exactly the protected worst slots; the Finalizer writes back only
    // those, so a slot vanilla consumed (both ids -1, 17027-17028) stays as vanilla left it.
    private static void GuardRestoresOnlyTheSlotsItCleared()
    {
        var worst = new[] { 3, 5, 7, -1, 5 };
        var state = AppointmentGuard.Clear(worst, id => id == 5 || id == 7);
        AssertEqual("3,-1,-1,-1,-1", string.Join(",", worst), "protected slots cleared");
        worst[0] = -1; // vanilla consumed slot 0 during the call
        AssertEqual(3, AppointmentGuard.Restore(worst, state), "three slots restored");
        AssertEqual("-1,5,7,-1,5", string.Join(",", worst), "only the cleared slots come back");

        AssertTrue(AppointmentGuard.Clear(worst, id => false) == null, "nothing protected -> no state");
        AssertEqual(0, AppointmentGuard.Restore(worst, null), "null state restores nothing");
        AssertTrue(AppointmentGuard.Clear(null, id => true) == null, "no array -> no state");
    }

    private static void OneAppointmentPerReview()
    {
        var f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.90f));
        f.Snapshot.Commanders.Add(Cmd(21, 7, 0.90f));
        AssertTrue(f.Plan(out var intent), "one intent");
        AssertEqual(Fixture.Army, intent.TargetInstanceId, "the army comes before the corps");
        AssertEqual(20, intent.CommanderId, "the best commander in band");
    }

    private static void ArmyGroupTargetFirstWithItsRankBand()
    {
        var f = new Fixture();
        f.AddArmyGroup();
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.95f)); // LtG: below the army-group minimum (9)
        f.Snapshot.Commanders.Add(Cmd(21, 9, 0.70f));
        AssertTrue(f.Plan(out var intent), "army-group appointment");
        AssertTrue(intent.TargetIsArmyGroup, "army group first");
        AssertEqual(Fixture.Group, intent.TargetInstanceId, "the main-effort army group");
        AssertEqual(21, intent.CommanderId, "only a full general fits the army-group band");
        AssertEqual(8, intent.DisplacedId, "displacing the army-group commander");
    }

    // CheckSuitableRank (60577-60590): the maximum rank applies to every unit type, the minimum
    // only to army groups (DoCommanderPromotion -> UpdateRankToFit raises a junior appointee).
    private static void RankBandMinimumOnlyForArmyGroups()
    {
        var f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(20, 7, 0.90f)); // MG onto an army (band 8..9)
        AssertTrue(f.Plan(out var intent), "a junior commander may take an army");
        AssertEqual(20, intent.CommanderId, "promotion by appointment");
        AssertEqual(Fixture.Army, intent.TargetInstanceId, "the army, below its minimum rank");

        f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(20, 10, 0.90f)); // above every band
        AssertFalse(f.Plan(out _), "above the maximum rank never fits");
    }

    private static void VanillaRefusalTriesTheNextCandidate()
    {
        var f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.90f));
        f.Snapshot.Commanders.Add(Cmd(21, 8, 0.80f));
        f.Allows = i => i.CommanderId != 20;
        AssertTrue(f.Plan(out var intent), "appointment");
        AssertEqual(21, intent.CommanderId, "vanilla's live rules refused the best; the next is taken");

        f.Allows = i => false;
        AssertFalse(f.Plan(out _), "vanilla refuses everyone -> no appointment");
    }

    private static void MainEffortCommanderNotPulledSideways()
    {
        var f = new Fixture();
        f.AddFormation(Fixture.SecondArmy, 16, Fixture.EastArea, 40000f, commander: 30, parent: -1);
        f.Snapshot.Commanders.Add(CmdWithCommand(30, 8, 0.90f, Fixture.SecondArmy, 16));
        AssertFalse(f.Plan(out var none) && none.CommanderId == 30, "a main-effort army commander is not moved to another army");

        // A main-effort corps commander may go up to the army (vertical, enemy side).
        f = new Fixture();
        f.ReplaceCommander(3, c => { c.Average = 0.90f; return c; });
        AssertTrue(f.Plan(out var intent), "vertical promotion on the enemy side");
        AssertEqual(3, intent.CommanderId, "the corps commander takes the army");
        AssertEqual(Fixture.Army, intent.TargetInstanceId, "onto the army");
    }

    private static void OnlyMainEffortCommandsAreTargets()
    {
        var f = new Fixture();
        f.ReplaceCommander(1, c => { c.Average = 0.95f; return c; });
        f.ReplaceCommander(3, c => { c.Average = 0.95f; return c; });
        f.Snapshot.Commanders.Add(Cmd(20, 7, 0.90f));
        AssertFalse(f.Plan(out _), "the weak western corps commander is not the main effort's concern");
    }

    private static void NoMainEffortNoAppointment()
    {
        var f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.95f));
        f.TheaterPlan.MainSectorId = -1;
        AssertFalse(f.Plan(out _), "no main effort -> nothing (the G0 empty plan)");
        AssertFalse(CommanderAppointmentPlanner.TryPlan(null, f.Snapshot, f.Sectors, null, f.Protection, null, out _), "no plan");
    }

    private static void TargetInBattleSkipped()
    {
        var f = new Fixture();
        f.SetFlags(Fixture.Army, FormationFlags.InBattle);
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.95f));
        AssertTrue(f.Plan(out var intent), "the corps still gets one");
        AssertEqual(Fixture.Corps, intent.TargetInstanceId, "an army in battle keeps its commander");
    }

    private static void AlliancesTwoToFourIgnored()
    {
        for (int alliance = 2; alliance <= 4; alliance++)
        {
            var f = new Fixture(alliance: alliance);
            f.Snapshot.Commanders.Add(Cmd(20, 8, 0.95f));
            AssertFalse(f.Plan(out _), "alliance " + alliance + " never appoints");
        }
    }

    // P16 / C19 (BUG-WL-014, user 2026-10-15: leave as vanilla): vanilla's vertical replacement
    // never runs for playeralliance (17664); on that side in a career the planner appoints only
    // idle commanders, as vanilla's job 24 does (84467-84473: the best per rank must be
    // pickable without a command).
    private static void NoVerticalSwapOnPlayerSideInCareer()
    {
        var f = new Fixture(PlayerCommandClass.BelowMapLevel);
        f.AddPlayerElsewhere();
        f.ReplaceCommander(3, c => { c.Average = 0.90f; return c; });
        AssertFalse(f.Plan(out var none) && none.CommanderId == 3, "player's side: the corps commander does not replace his army commander");
        f.Snapshot.Commanders.Add(Cmd(20, 8, 0.80f));
        AssertTrue(f.Plan(out var idle), "player's side: an idle commander is still appointed");
        AssertEqual(20, idle.CommanderId, "job-24 style idle appointment");

        f = new Fixture(PlayerCommandClass.BelowMapLevel);
        f.ReplaceCommander(3, c => { c.Average = 0.90f; return c; });
        AssertTrue(f.Plan(out var enemy), "enemy side of a career: vertical promotion allowed");
        AssertEqual(3, enemy.CommanderId, "enemy side");

        f = new Fixture();
        f.ReplaceCommander(3, c => { c.Average = 0.90f; return c; });
        AssertTrue(f.Plan(out var normal), "outside careers: vertical promotion allowed");
        AssertEqual(3, normal.CommanderId, "normal campaign");
    }

    // Main's ResolveRoleTargetGroup (dbc3a6f): ANV/AoP -> the principal eastern command outside
    // the Shenandoah Valley, Valley -> a Valley command, Western -> the principal western one.
    private static void RoleFollowsTheaterAndPrimacy()
    {
        AssertEqual("ANV", CommanderAppointmentPlanner.RoleOf(1, Theater.East, true, false), "CSA principal eastern command");
        AssertEqual("AoP", CommanderAppointmentPlanner.RoleOf(0, Theater.East, true, false), "Union principal eastern command");
        AssertEqual("Valley", CommanderAppointmentPlanner.RoleOf(0, Theater.East, false, true), "an eastern command in the Valley");
        AssertEqual(null, CommanderAppointmentPlanner.RoleOf(0, Theater.East, false, false), "a secondary eastern command outside the Valley has no role");
        AssertEqual("Western", CommanderAppointmentPlanner.RoleOf(1, Theater.West, true, false), "principal western command");
        AssertEqual(null, CommanderAppointmentPlanner.RoleOf(1, Theater.West, false, false), "secondary western command has no role");
        AssertEqual(null, CommanderAppointmentPlanner.RoleOf(0, Theater.Coast, true, false), "no role elsewhere");
        AssertTrue(CommanderAppointmentPlanner.InShenandoahValley(1107f, -555f), "the Shenandoah area anchor is in the box");
        AssertFalse(CommanderAppointmentPlanner.InShenandoahValley(1263f, -1010f), "the Virginia capital corridor is not");

        // The army (principal) carries ANV; the corps outside the Valley carries nothing.
        var f = new Fixture();
        f.Snapshot.Commanders.Add(Cmd(20, 7, 0.45f));
        var roles = new List<string>();
        f.Prior = (int id, string role, out int ev) => { roles.Add(role); ev = -1; return 0f; };
        f.Plan(out _);
        AssertTrue(roles.Contains("ANV") && !roles.Contains("Valley"), "roles passed to the prior: " + string.Join(",", roles));
    }

    // Main's "no valley group -> defer": a Valley event needs a command in the Valley.
    private static void ValleyRoleNeedsAShenandoahCommand()
    {
        var f = new Fixture(alliance: 0);
        f.Snapshot.Commanders.Add(Cmd(20, 7, 0.40f, name: "Philip H. Sheridan"));
        f.Prior = PriorFor(20, "Valley", 11);
        AssertFalse(f.Plan(out _), "no Valley command -> the Valley event defers");

        f = new Fixture(alliance: 0);
        f.AddFormation(Fixture.ValleyCorps, 15, Fixture.EastArea, 10000f, commander: 31, parent: -1, x: 1107f, z: -555f);
        f.Snapshot.Commanders.Add(CmdWithCommand(31, 7, 0.40f, Fixture.ValleyCorps, 15));
        f.Snapshot.Commanders.Add(Cmd(20, 7, 0.40f, name: "Philip H. Sheridan"));
        f.Prior = PriorFor(20, "Valley", 11);
        AssertTrue(f.Plan(out var intent), "a Valley command takes the Valley figure");
        AssertEqual(Fixture.ValleyCorps, intent.TargetInstanceId, "the Valley corps");
        AssertEqual("succession:11", intent.Reason, "the Valley event");
    }

    private static void SuccessionPriorOnlyForFiredNotApplied()
    {
        var s = new SuccessionScheduler();
        AssertEqual(0f, s.PriorFor(1, "Robert E. Lee", "ANV", out int ev), "not fired");
        AssertEqual(-1, ev, "no event");
        s.FiredEventIds.Add(1);
        AssertEqual(CommanderAppointmentPlanner.SuccessionPriorBonus, s.PriorFor(1, "Robert E. Lee", "ANV", out ev), "fired, not applied");
        AssertEqual(1, ev, "event 1 (Lee -> ANV)");
        AssertEqual(0f, s.PriorFor(1, "Fitzhugh Lee", "ANV", out _), "exact historical figure only");
        AssertEqual(0f, s.PriorFor(1, "Robert E. Lee", "Western", out _), "role must match");
        AssertEqual(0f, s.PriorFor(0, "Robert E. Lee", "ANV", out _), "alliance must match");
        s.MarkApplied(1);
        AssertEqual(0f, s.PriorFor(1, "Robert E. Lee", "ANV", out _), "applied events give nothing");
        s.FiredEventIds.Add(12); // Lee -> General-in-Chief CSA (C18: deferred)
        AssertEqual(0f, s.PriorFor(1, "Robert E. Lee", "ANV", out _), "GeneralInChief events never match a command role");
        AssertEqual(0.25f, CommanderAppointmentPlanner.SuccessionPriorBonus, "bonus constant");
    }

    // ---- succession reseat (spec component 7; main's C-CmdrReplace ReseatThenAppoint) ----

    // Lee sits on an auto-created western army group (not pickable, 60770); the fired ANV event
    // may still bring him to the main-effort army, his group reseated first.
    private static void SuccessionReseatMovesAFigureOffHisArmyGroup()
    {
        var f = new Fixture();
        f.AddFigureOnWesternArmyGroup();
        f.ReplaceCommander(40, c => { c.Average = 0.80f; return c; });
        AssertFalse(f.Plan(out _), "without the event even a far better army-group commander stays outside vanilla's pool");
        f.ReplaceCommander(40, c => { c.Average = 0.30f; return c; });

        f.Prior = PriorFor(40, "ANV", 1);
        f.Allows = i => i.ReseatGroupInstanceId < 0
            || CommanderAssignmentGuard.ResolveReplacementAppointment(false, true, true, successorAvailable: true) == ReplacementAppointment.ReseatThenAppoint;
        AssertTrue(f.Plan(out var intent), "the named figure moves");
        AssertEqual(40, intent.CommanderId, "Lee");
        AssertEqual(Fixture.WestGroup, intent.ReseatGroupInstanceId, "his old group is reseated first");
        AssertEqual(Fixture.Army, intent.TargetInstanceId, "onto the ANV command");
        AssertEqual("succession:1", intent.Reason, "succession reason");

        var plain = new Fixture();
        plain.Snapshot.Commanders.Add(Cmd(20, 8, 0.90f));
        AssertTrue(plain.Plan(out var direct) && direct.ReseatGroupInstanceId == -1, "a pickable appointee reseats nothing");
    }

    // "No successor -> never leave one man on two groups": the live check defers.
    private static void SuccessionReseatDefersWithoutIdleSuccessor()
    {
        var f = new Fixture();
        f.AddFigureOnWesternArmyGroup();
        f.Prior = PriorFor(40, "ANV", 1);
        f.Allows = i => i.ReseatGroupInstanceId < 0
            || CommanderAssignmentGuard.ResolveReplacementAppointment(false, true, true, successorAvailable: false) == ReplacementAppointment.ReseatThenAppoint;
        AssertFalse(f.Plan(out _), "no idle successor for his group -> the event defers");

        f.ReplaceCommander(40, c => { c.FreeApartFromArmyGroup = false; return c; });
        f.Allows = i => true;
        AssertFalse(f.Plan(out _), "a figure not free apart from his group (wounded, staff, CIC, the player's officer) stays put");
    }

    private static void SuccessionReseatNeverOnThePlayersSideOrGroup()
    {
        var f = new Fixture(PlayerCommandClass.BelowMapLevel);
        f.AddPlayerElsewhere();
        f.AddFigureOnWesternArmyGroup();
        f.Prior = PriorFor(40, "ANV", 1);
        AssertFalse(f.Plan(out _), "player's alliance: idle commanders only, no reseat");

        f = new Fixture(PlayerCommandClass.BelowMapLevel);
        f.AddFigureOnWesternArmyGroup();
        f.SetChain(Fixture.WestCorps, WlChainClass.CarryingGroup);
        f.Snapshot.Commanders.RemoveAll(c => c.IsPlayer); // not the player-side rule: the group rule alone
        f.Prior = PriorFor(40, "ANV", 1);
        AssertFalse(f.Plan(out _), "a group holding the player's command is never reseated");

        // His group is itself a main-effort command (secondary to army group 300, the ANV
        // command): he is not pulled off it.
        f = new Fixture();
        f.AddArmyGroup();
        f.AddFigureOnWesternArmyGroup();
        f.MoveWestGroupToMainEffort();
        f.Prior = PriorFor(40, "ANV", 1);
        AssertFalse(f.Plan(out var none) && none.CommanderId == 40, "a main-effort army group's commander is not pulled off it");
    }

    private static void VanillaChecksCappedAtEight()
    {
        var f = new Fixture();
        for (int i = 0; i < 12; i++) f.Snapshot.Commanders.Add(Cmd(60 + i, 9, 0.90f - i * 0.01f));
        int calls = 0;
        f.Allows = i => { calls++; return i.CommanderId == 69; };
        AssertFalse(f.Plan(out _), "the tenth candidate is beyond the cap");
        AssertEqual(CommanderAppointmentPlanner.MaxVanillaChecks, calls, "at most MaxVanillaChecks live checks per review");
        AssertEqual(8, CommanderAppointmentPlanner.MaxVanillaChecks, "cap");

        calls = 0;
        f.Allows = i => { calls++; return i.CommanderId == 67; };
        AssertTrue(f.Plan(out var intent) && intent.CommanderId == 67, "the eighth candidate is still checked");
    }

    // P13: the guard tests protection as of each alliance's last review (Age), never before the
    // first review, never another alliance's.
    private static void ProtectionBookCountsFromTheLastReview()
    {
        var book = new AppointmentProtectionBook();
        book.For(1).Protect(7, 100);
        AssertFalse(book.IsProtected(1, 7), "before the first review nothing is protected");
        book.Age(1, 120);
        AssertEqual(120, book.LastReviewDay(1), "Age records the review day");
        AssertTrue(book.IsProtected(1, 7), "protected as of the last review");
        AssertFalse(book.IsProtected(0, 7), "the other alliance's book");
        book.Age(1, 160);
        AssertFalse(book.IsProtected(1, 7), "expired by the review of day 160");
        AssertFalse(book.IsProtected(2, 7), "alliance 2 has no book");
        AssertTrue(book.For(2) == null && book.LastReviewDay(2) == -1, "alliance bounds");
        book.Age(2, 100); // no-op, no throw
    }

    private static void GuardClearsThisAlliancesProtectedOnly()
    {
        var book = new AppointmentProtectionBook();
        book.For(1).Protect(5, 100);
        book.For(0).Protect(7, 100);
        book.Age(0, 100);
        book.Age(1, 100);
        var worst = new[] { 5, 7, 9 };
        var state = AppointmentGuard.ClearProtected(worst, book, 1);
        AssertEqual("-1,7,9", string.Join(",", worst), "only alliance 1's protected appointee is cleared");
        AssertEqual(1, state.Count, "one slot");
        AssertEqual(0, state.SlotAt(0), "slot 0");
        AssertEqual(5, state.IdAt(0), "id 5");
        AssertTrue(AppointmentGuard.ClearProtected(worst, null, 1) == null, "no book -> nothing");
    }

    // CheckAICommanderReplacements 17017-17020.
    private static void VanillaWouldReplaceMirrorsJob24()
    {
        AssertTrue(AppointmentGuard.VanillaWouldReplace(2, 1, true, false, true, 0.6f, 0.4f, 0.1f), "vanilla swaps");
        AssertFalse(AppointmentGuard.VanillaWouldReplace(2, 1, true, false, true, 0.5f, 0.4f, 0.1f), "not above the margin");
        AssertFalse(AppointmentGuard.VanillaWouldReplace(-1, 1, true, false, true, 0.9f, 0.4f, 0.1f), "no best");
        AssertFalse(AppointmentGuard.VanillaWouldReplace(1, 1, true, false, true, 0.9f, 0.4f, 0.1f), "same commander");
        AssertFalse(AppointmentGuard.VanillaWouldReplace(2, 1, false, false, true, 0.9f, 0.4f, 0.1f), "worst has no command");
        AssertFalse(AppointmentGuard.VanillaWouldReplace(2, 1, true, true, true, 0.9f, 0.4f, 0.1f), "garrison command");
        AssertFalse(AppointmentGuard.VanillaWouldReplace(2, 1, true, false, false, 0.9f, 0.4f, 0.1f), "best not pickable");
    }

    // ---- source pins (runtime wiring the harness cannot run) ----

    private static void GuardPatchNeverSkipsVanilla()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Patches/CommanderReplacementPatch.cs");
        AssertContains(src, "[HarmonyPatch(typeof(AICampaign), \"CheckAICommanderReplacements\")]", "patch target");
        AssertContains(src, "[HarmonyFinalizer]", "the restore runs in a Finalizer");
        AssertContains(src, "PatchFirstFire.Mark(\"campaign.patch.commander-replacement\")", "Prefix first-fire id kept");
        AssertContains(src, "PatchFirstFire.Mark(\"campaign.patch.commander-replacement.finalizer\")", "Finalizer first-fire id");
        AssertContains(src, "__state = AppointmentGuard.ClearProtected(alliance.worstcommanderidperlevel, CommanderAppointmentRuntime.Book, allianceId);",
            "clears this alliance's protected appointees through the pure guard and the runtime's book");
        AssertContains(src, "OnceLog.Info(\"replace:blocked\"", "a real job-24 block is logged");
        AssertContains(src, "AppointmentGuard.VanillaWouldReplace(", "with vanilla's own swap test");
        AssertContains(src, "AppointmentGuard.Restore(", "restores through the pure guard");
        AssertContains(src, "CommanderAppointmentRuntime.Enabled()", "gated on the appointment flags");
        foreach (string write in new[] { "MarkApplied(", ".Protect(", "AppointCommander(", "AssignCommando(", "ReplaceCommanderOfUnit(" })
            AssertFalse(src.Contains(write), "the patch writes no mod state and appoints nobody: " + write);
        string finalizer = MethodBody(src, "internal static Exception Finalizer(");
        AssertContains(finalizer, "return __exception;", "vanilla's exception passes through");
        AssertFalse(finalizer.Contains("return null"), "the Finalizer never swallows vanilla's exception");
        string prefix = MethodBody(src, "internal static void Prefix(");
        AssertFalse(prefix.Contains("return false"), "the Prefix is void and never skips vanilla's job 24");
        AssertContains(prefix, "allianceId < 0 || allianceId > 1", "alliance-2 bound");
    }

    private static void AppointmentStepWiredIntoTheReview()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string plan = MethodBody(src, "internal static void ReviewPlan(");
        int age = plan.IndexOf("CommanderAppointmentRuntime.Age(", StringComparison.Ordinal);
        int bookkeeping = plan.IndexOf("mode == PlannerReviewMode.Bookkeeping) return;", StringComparison.Ordinal);
        AssertTrue(age >= 0 && age < bookkeeping, "protection windows age in bookkeeping too");

        string execute = MethodBody(src, "internal static void ReviewExecute(");
        int full = execute.IndexOf("!= PlannerReviewMode.Full) return;", StringComparison.Ordinal);
        int appoint = execute.IndexOf("CommanderAppointmentRuntime.Execute(", StringComparison.Ordinal);
        AssertTrue(full >= 0 && appoint > full, "appointments run only in a full review, after its gates");

        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CommanderAppointmentRuntime.cs");
        string enabled = MethodBody(runtime, "internal static bool Enabled(");
        foreach (string flag in new[] { "Plugin.EnableCampaignPlanner", "Plugin.EnableCampaignPlannerExecutors", "Plugin.EnableCommanderAppointmentPlanner" })
            AssertContains(enabled, flag, "appointments gated on " + flag);
        AssertContains(MethodBody(runtime, "internal static void Age("), "Book.Age(alliance, daySerial);", "Age records the review day in the book");
        string execBody = MethodBody(runtime, "internal static void Execute(");
        AssertContains(execBody, "if (!Enabled()) return;", "Execute checks the gate first");
        AssertContains(execBody, "plan.DaySerial != s.DaySerial", "never on a stale plan");
    }

    private static void ExecutorMirrorsVanillaRecipe()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CommanderAppointmentRuntime.cs");
        string allows = MethodBody(src, "private static bool VanillaAllows(");
        AssertContains(allows, "CanBePickedForAIReplacement(alliance, includeofficerswithcommand: true)", "vanilla's AI pool (60748)");
        AssertContains(allows, "CheckSuitableRank(", "vanilla's rank rule (60556)");
        AssertContains(allows, "refreshcommand", "vanilla's refresh rule");
        AssertContains(allows, "inbattle", "never mid-battle");
        AssertContains(allows, "if (DLC_WL.dlc_scenarioactive && intent.CommanderId == DLC_WL.dlc_chosencommander) return false;", "never the W&L player, live");
        AssertContains(allows, "ReseatDecision(alliance, intent, c)", "the reseat path is decided live");
        string reseat = MethodBody(src, "private static ReplacementAppointment ReseatDecision(");
        AssertContains(reseat, "CommanderAssignmentGuard.ResolveReplacementAppointment(", "main's reseat rule");
        AssertContains(reseat, "!GroupHoldsPlayer(group)", "never the player's group");
        string successor = MethodBody(src, "private static int PickSuccessor(");
        AssertContains(successor, "ArmyGroup.GetAvailableArmyGroupCommanders(alliance)", "vanilla's idle army-group pool (75546)");
        AssertContains(successor, "candidate.CanBePickedForAIReplacement(alliance)", "successor in vanilla's AI pool");
        AssertContains(successor, "id == DLC_WL.dlc_chosencommander) continue;", "never the player as successor");
        string execute0 = MethodBody(src, "internal static void Execute(");
        AssertContains(execute0, "(intent.ReseatGroupInstanceId < 0 || Reseat(alliance, intent, out reseated)) &&", "reseat before appointing");
        string reseatBody = MethodBody(src, "private static bool Reseat(");
        int appointed = reseatBody.IndexOf("ArmyGroup.AppointCommander(group, successor,", StringComparison.Ordinal);
        int verifiedReseat = reseatBody.IndexOf("if (group.GetCommanderID() != successor) return false;", StringComparison.Ordinal);
        AssertTrue(appointed >= 0 && verifiedReseat > appointed, "the reseat is verified before the figure is appointed");
        AssertContains(execute0, "if (reseated != null) EmitAppointment(s, alliance, intent, unitName, reseated, \"appoint-failed-after-reseat\");",
            "a reseat followed by a failed appointment is recorded");
        AssertContains(execute0, "EmitAppointment(s, alliance, intent, unitName, reseated, \"appointed\");", "a verified appointment is recorded");

        string unit = MethodBody(src, "private static bool AppointToUnit(");
        int replace = unit.IndexOf("BattleUnits.ReplaceCommanderOfUnit(", StringComparison.Ordinal);
        int assign = unit.IndexOf(".AssignCommando(", StringComparison.Ordinal);
        int promote = unit.IndexOf("BattleUnits.DoCommanderPromotion(", StringComparison.Ordinal);
        int refresh = unit.IndexOf("refreshcommand = true", StringComparison.Ordinal);
        AssertTrue(replace >= 0 && replace < assign && assign < promote && promote < refresh, "job-24 recipe order (17024-17026) after refilling the old command");

        string group = MethodBody(src, "private static bool AppointToArmyGroup(");
        AssertContains(group, "ArmyGroup.AppointCommander(", "vanilla army-group appointment (75559)");
        AssertContains(group, "usefeudmechanism: false", "no W&L feud mechanism");

        string execute = MethodBody(src, "internal static void Execute(");
        int verified = execute.IndexOf("if (!done)", StringComparison.Ordinal);
        int protect = execute.IndexOf(".Protect(", StringComparison.Ordinal);
        int applied = execute.IndexOf("MarkApplied(", StringComparison.Ordinal);
        AssertTrue(verified >= 0 && protect > verified && applied > verified, "protect and mark applied only after the verified result");
    }

    // From the signature to its matching closing brace.
    private static string MethodBody(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        AssertTrue(start >= 0, "method present: " + signature);
        int open = src.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start, i - start + 1);
        }
        throw new Exception("unbalanced body: " + signature);
    }

    // ---- fixtures ----

    private static CommanderRecord Cmd(int id, int rank, float average, float initiative = 0.5f, float leadership = 0.5f,
                                       float administration = 0.5f, string name = null)
    {
        return new CommanderRecord
        {
            Id = id, Rank = rank, Average = average, Initiative = initiative, Leadership = leadership, Administration = administration,
            Available = true, Pickable = true, CommandInstanceId = -1, CommandUnitTyp = -1, Name = name ?? ("Commander " + id)
        };
    }

    private static CommanderRecord CmdWithCommand(int id, int rank, float average, int command, int commandUnitTyp)
    {
        var c = Cmd(id, rank, average);
        c.CommandInstanceId = command;
        c.CommandUnitTyp = commandUnitTyp;
        return c;
    }

    private static SuccessionPrior PriorFor(int commanderId, string role, int eventId)
    {
        return (int id, string r, out int ev) =>
        {
            bool hit = id == commanderId && r == role;
            ev = hit ? eventId : -1;
            return hit ? CommanderAppointmentPlanner.SuccessionPriorBonus : 0f;
        };
    }

    // CSA (alliance 1) with an eastern main-effort sector holding army 100 (commander 1, LtG
    // 0.40) and its corps 101 (commander 3, MG 0.45), and a western sector with corps 200
    // (commander 4, MG 0.10).
    private sealed class Fixture
    {
        internal const int Army = 100, Corps = 101, SecondArmy = 102, ValleyCorps = 103, WestCorps = 200, Group = 300, WestGroup = 301;
        internal const int EastArea = 0, WestArea = 1;

        internal readonly CampaignSnapshot Snapshot = new CampaignSnapshot();
        internal readonly TheaterPlan TheaterPlan;
        internal readonly AppointmentProtection Protection = new AppointmentProtection();
        internal SuccessionPrior Prior;
        internal Func<AppointmentIntent, bool> Allows = i => true;
        private SectorModel _sectors;

        internal Fixture(PlayerCommandClass player = PlayerCommandClass.NotCareer, int alliance = 1, int playerCommand = -1)
        {
            Snapshot.Reset(alliance, alliance, Day, player, playerCommand, null);
            Snapshot.RelocationRange = 30f;
            Snapshot.Areas.Add(new AreaRecord { Index = EastArea, X = 1263f, Z = -1010f, EdgeX = new[] { 1263f }, EdgeZ = new[] { -1010f }, SupplyCapacity = 100f });
            Snapshot.Areas.Add(new AreaRecord { Index = WestArea, X = 0f, Z = 0f, EdgeX = new[] { 0f }, EdgeZ = new[] { 0f }, SupplyCapacity = 100f });
            Snapshot.Objectives.Add(new ObjectiveRecord { ObjectiveId = 1, X = 1263f, Z = -1010f });
            Snapshot.Objectives.Add(new ObjectiveRecord { ObjectiveId = 2, X = 0f, Z = 0f });
            Snapshot.RankBands.Add(new RankBand { UnitTyp = 15, MinRank = 7, MaxRank = 8 });
            Snapshot.RankBands.Add(new RankBand { UnitTyp = 16, MinRank = 8, MaxRank = 9 });
            Snapshot.RankBands.Add(new RankBand { UnitTyp = 18, MinRank = 9, MaxRank = 9 });

            AddFormation(Army, 16, EastArea, 50000f, commander: 1, parent: -1);
            AddFormation(Corps, 15, EastArea, 20000f, commander: 3, parent: Army);
            AddFormation(WestCorps, 15, WestArea, 15000f, commander: 4, parent: -1);
            Snapshot.Commanders.Add(CmdWithCommand(1, 8, 0.40f, Army, 16));
            Snapshot.Commanders.Add(CmdWithCommand(3, 7, 0.45f, Corps, 15));
            Snapshot.Commanders.Add(CmdWithCommand(4, 7, 0.10f, WestCorps, 15));

            TheaterPlan = new TheaterPlan { Alliance = alliance, DaySerial = Day, MainSectorId = Sectors.SectorOfArea(EastArea) };
        }

        internal SectorModel Sectors => _sectors ?? (_sectors = SectorModel.Build(Snapshot));

        internal bool Plan(out AppointmentIntent intent)
        {
            return CommanderAppointmentPlanner.TryPlan(TheaterPlan, Snapshot, Sectors, Prior, Protection, Allows, out intent);
        }

        internal void AddFormation(int id, int unitTyp, int area, float strength, int commander, int parent, float? x = null, float? z = null)
        {
            float px = x ?? (area == EastArea ? 1263f : 0f);
            float pz = z ?? (area == EastArea ? -1010f : 0f);
            Snapshot.Own.Add(new FormationRecord
            {
                InstanceId = id, UnitKey = "u" + id + ":" + commander, UnitTyp = unitTyp, X = px, Z = pz, Strength = strength,
                CommanderId = commander, AreaIndex = area, ParentInstanceId = parent, Name = "unit " + id, Flags = FormationFlags.FightingForce
            });
        }

        // Army group 300 (commander 8, full general 0.40) over the eastern army.
        internal void AddArmyGroup()
        {
            Snapshot.ArmyGroups.Add(new ArmyGroupRecord { InstanceId = Group, CommanderId = 8, X = 1263f, Z = -1010f, Strength = 50000f, MemberInstanceIds = new[] { Army } });
            Snapshot.Commanders.Add(Cmd(8, 9, 0.40f));
            ReplaceCommander(8, c => { c.Pickable = false; return c; }); // vanilla: an army-group commander is not pickable
        }

        // Robert E. Lee (40, full general 0.30) on western army group 301 over the western corps:
        // not pickable (an army-group commander), free apart from it.
        internal void AddFigureOnWesternArmyGroup()
        {
            Snapshot.ArmyGroups.Add(new ArmyGroupRecord { InstanceId = WestGroup, CommanderId = 40, X = 0f, Z = 0f, Strength = 15000f, MemberInstanceIds = new[] { WestCorps } });
            var lee = Cmd(40, 9, 0.30f, name: "Robert E. Lee");
            lee.Pickable = false;
            lee.FreeApartFromArmyGroup = true;
            Snapshot.Commanders.Add(lee);
        }

        internal void MoveWestGroupToMainEffort()
        {
            for (int i = 0; i < Snapshot.ArmyGroups.Count; i++)
            {
                if (Snapshot.ArmyGroups[i].InstanceId != WestGroup) continue;
                var g = Snapshot.ArmyGroups[i];
                g.X = 1263f;
                g.Z = -1010f;
                Snapshot.ArmyGroups[i] = g;
            }
        }

        // The W&L player commands a brigade somewhere on this side (makes this the player's alliance).
        internal void AddPlayerElsewhere()
        {
            var player = Cmd(50, 6, 0.50f);
            player.IsPlayer = true;
            player.Pickable = false;
            player.CommandInstanceId = 999;
            Snapshot.Commanders.Add(player);
        }

        internal void SetChain(int instanceId, WlChainClass chain)
        {
            int i = Snapshot.IndexOfOwn(instanceId);
            var f = Snapshot.Own[i];
            f.Chain = chain;
            Snapshot.Own[i] = f;
            AddPlayerElsewhere();
        }

        internal void SetFlags(int instanceId, FormationFlags flags)
        {
            int i = Snapshot.IndexOfOwn(instanceId);
            var f = Snapshot.Own[i];
            f.Flags |= flags;
            Snapshot.Own[i] = f;
        }

        internal void ReplaceCommander(int id, Func<CommanderRecord, CommanderRecord> change)
        {
            for (int i = 0; i < Snapshot.Commanders.Count; i++)
                if (Snapshot.Commanders[i].Id == id) Snapshot.Commanders[i] = change(Snapshot.Commanders[i]);
        }
    }
}
