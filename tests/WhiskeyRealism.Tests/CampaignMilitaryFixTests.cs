using System;
using WhiskeyRealism.Strategic;

// WS C2a — campaign military write surfaces (2026-10-14 remediation plan).
internal static class CampaignMilitaryFixTests
{
    public static void Run()
    {
        SeaInvasionStrengthResolvesInstanceOverload();
        SeaInvasionLandedSignalUsesEnemyBalance();
        ListSnapshotRestoreUndoesVanillaMutation();
        ListSnapshotRestoreIsIdempotent();
        ForbiddenDefensiveCommitRevertMatchesHowVanillaMoved();
        TransferRequirementIsLocalNotTheaterWide();
        TransferSteersOnlyWhenPlanShortfallIsWorse();
        TransferStrengthReadsVanillaHeatmapGrids();
        ImportanceRedirectHonoursVanillaCandidateGates();
        ImportanceRedirectStaysInPlanTheater();
        ArmyGroupManagementRepeatsVanillaGuard();
        CommanderNameMatchDisambiguatesHistoricalSurnames();
        CommanderNameMatchFallsBackToExactSurname();
        ArmyGroupCommanderEligibilityMirrorsVanilla();
        ArmyGroupAcceptsUnitsMirrorsGetArmyGroupInRange();
        ArmyGroupAppointsOnlyOnGroupHoldingPlanUnits();
        SuccessionRoleResolvesTheaterArmyGroup();
        SuccessionRoleNeverTargetsPlayerOrWrongFront();
        SuccessionGeneralInChiefIsNotAnArmyGroup();
        StalePreviousCommandIsRefilledNotOrphaned();
        SuccessionReseatsReplacementsOldArmyGroup();
        CapitalNeedMatchesVanillaFormula();
        CapitalPrePledgeOnlyWhenVanillaAlsoNeedsDefenders();
        CapitalPersonalityNeedDoesNotDoubleCountPledgedDefenders();
        AreaOrderSkipsEveryVanillaBusyAssignment();
        CoastalInterceptKeepsEnemyInsideBugleRange();
        CoastalInterceptHoldsAnchorWhenEnemyAlreadyClose();
        CoastalAssignmentsCarryForwardWhileHeld();
        CoordinatedSingleLeadNeedsReinforceRatio();
        CoordinatedOffensiveMirrorsVanillaEarlyOuts();
        CoordinatedOffensiveTempoGateMatchesVanilla();
        CoordinatedTempoErrorLeavesVanillaUntouched();
        CoordinatedPackageLockExpiresWithPath();
    }

    private static void LogPass(string message)
    {
        Console.WriteLine("PASS " + message);
    }

    // C-DefIntent: vanilla SeaInvasion has two GetUnitStrengthInRange overloads
    // (decompile 141061 instance, 141066 static). An untyped AccessTools lookup
    // throws AmbiguousMatchException; the typed lookup must hit the instance one.
    private static void SeaInvasionStrengthResolvesInstanceOverload()
    {
        var method = CampaignMilitaryGates.ResolveSeaInvasionStrengthMethod();
        TestHarness.AssertTrue(method != null, "instance overload resolved");
        TestHarness.AssertFalse(method.IsStatic, "resolved overload is the instance method");
        var parameters = method.GetParameters();
        TestHarness.AssertEqual(3, parameters.Length, "parameter count");
        TestHarness.AssertEqual(typeof(int), parameters[0].ParameterType, "allianceid type");
        TestHarness.AssertEqual(typeof(bool), parameters[1].ParameterType, "returnbalance type");
        TestHarness.AssertEqual(typeof(int), parameters[2].ParameterType, "shipweight type");
        LogPass("c2a sea invasion strength resolves typed instance overload");
    }

    // C-DefIntent: with returnbalance=false vanilla sums OWN units too (141066+),
    // so own garrisons at the spot would read as a landing. Vanilla's own callers
    // (9238, 9433) always use returnbalance=true: enemy minus friendly.
    private static void SeaInvasionLandedSignalUsesEnemyBalance()
    {
        var args = CampaignMilitaryGates.SeaInvasionBalanceArgs(1);
        TestHarness.AssertEqual(1, (int)args[0], "defender alliance passed");
        TestHarness.AssertEqual(true, (bool)args[1], "returnbalance must be true");
        TestHarness.AssertEqual(0, (int)args[2], "ships weigh nothing for a land landing");
        TestHarness.AssertTrue(CampaignMilitaryGates.IsLandedByStrengthBalance(1), "enemy outweighs us -> landed");
        TestHarness.AssertFalse(CampaignMilitaryGates.IsLandedByStrengthBalance(0), "balanced -> not landed");
        TestHarness.AssertFalse(CampaignMilitaryGates.IsLandedByStrengthBalance(-500), "we outweigh -> not landed");
        LogPass("c2a sea invasion landed signal uses enemy-minus-friendly balance");
    }

    private static void ListSnapshotRestoreUndoesVanillaMutation()
    {
        var live = new System.Collections.ArrayList { "a", "c" };
        var snapshot = new System.Collections.Generic.List<object> { "a", "b", "c" };
        TestHarness.AssertTrue(CampaignMilitaryGates.RestoreListSnapshot(live, snapshot), "restore reports success");
        TestHarness.AssertEqual(3, live.Count, "restored count");
        TestHarness.AssertEqual("b", (string)live[1], "restored original order");
        LogPass("c2a list snapshot restore undoes vanilla mutation");
    }

    private static void ListSnapshotRestoreIsIdempotent()
    {
        var live = new System.Collections.ArrayList { "x" };
        var snapshot = new System.Collections.Generic.List<object> { "a", "b" };
        CampaignMilitaryGates.RestoreListSnapshot(live, snapshot);
        CampaignMilitaryGates.RestoreListSnapshot(live, snapshot);
        TestHarness.AssertEqual(2, live.Count, "second restore keeps exact contents");
        TestHarness.AssertFalse(CampaignMilitaryGates.RestoreListSnapshot(null, snapshot), "null live list is a no-op");
        TestHarness.AssertFalse(CampaignMilitaryGates.RestoreListSnapshot(live, null), "missing snapshot is a no-op");
        TestHarness.AssertEqual(2, live.Count, "missing snapshot leaves list alone");
        LogPass("c2a list snapshot restore is idempotent and null-safe");
    }

    // C-Transfer: the plan requirement used totalOwn * ForceFractionRequired
    // (up to 0.95 of the whole army), so the plan target always "needed"
    // more than any vanilla deficit and the threatened zone never got troops.
    private static void TransferRequirementIsLocalNotTheaterWide()
    {
        float undefended = CampaignMilitaryGates.PlanTransferRequiredAtTarget(totalOwn: 200000f, forceFraction: 0.8f, enemyNear: 0f);
        TestHarness.AssertEqual(2500f, undefended, "no enemy near target -> token presence");
        float contested = CampaignMilitaryGates.PlanTransferRequiredAtTarget(200000f, 0.8f, 20000f);
        TestHarness.AssertEqual(25000f, contested, "local superiority over enemy near target");
        float capped = CampaignMilitaryGates.PlanTransferRequiredAtTarget(30000f, 0.5f, 40000f);
        TestHarness.AssertEqual(15000f, capped, "never more than the plan's commitment share");
        LogPass("c2a transfer requirement is local, capped by commitment share");
    }

    private static void TransferSteersOnlyWhenPlanShortfallIsWorse()
    {
        // vanilla CheckTransferOfUnits severity = deficit.enemystrength - deficit.ownstrength (17275)
        TestHarness.AssertTrue(CampaignMilitaryGates.ShouldSteerTransfer(false, 0f, 0f, 5000f), "no vanilla deficit -> steer");
        TestHarness.AssertTrue(CampaignMilitaryGates.ShouldSteerTransfer(true, 1000f, 3000f, 5000f), "vanilla deficit not a shortfall -> steer");
        TestHarness.AssertTrue(CampaignMilitaryGates.ShouldSteerTransfer(true, 12000f, 8000f, 5000f), "plan shortfall worse -> steer");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldSteerTransfer(true, 20000f, 5000f, 5000f), "threatened zone worse -> vanilla keeps its deficit");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldSteerTransfer(false, 0f, 0f, 0f), "no plan shortfall -> nothing to steer");
        LogPass("c2a transfer steers only when plan shortfall exceeds vanilla deficit");
    }

    // R7: ImportanceValuesPatch overwrote mostvalueableaiareaclose for EVERY
    // source area, bypassing vanilla's candidate gates in
    // CalculateMostValueableAIZones (decompile 10964-10987).
    private static void ImportanceRedirectHonoursVanillaCandidateGates()
    {
        // baseline: same theater, in range, supply ok, vanilla value clause ok
        TestHarness.AssertTrue(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, Theater.East, Theater.East, 200f, 300f, 10f, 50f, true), "eligible target redirects");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            true, Theater.East, Theater.East, 0f, 300f, 10f, 50f, true), "aiarea[i] != this");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, Theater.East, Theater.East, 301f, 300f, 10f, 50f, true), "distance gate (relocation range x offensive range factor)");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, Theater.East, Theater.East, 200f, 300f, 60f, 50f, true), "supply gate ownpoints <= supplycapacity");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, Theater.East, Theater.East, 200f, 300f, 10f, 50f, false), "vanilla points/distance/importance clause");
        LogPass("c2a importance redirect honours vanilla candidate gates");
    }

    private static void ImportanceRedirectStaysInPlanTheater()
    {
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, Theater.West, Theater.East, 200f, 300f, 10f, 50f, true), "other front keeps vanilla's local target");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, Theater.Unknown, Theater.East, 200f, 300f, 10f, 50f, true), "unclassified source keeps vanilla target");
        LogPass("c2a importance redirect stays inside the plan theater");
    }

    // C-ArmyGroup: vanilla CheckArmyGroupManagement (decompile 17705) returns
    // when army groups are disabled, the reform is not researched, or its CIC
    // early return applies (17723; with the stage F guard #70 only for the W&L
    // CIC player's own alliance, CicFactionAiGuard.VanillaGuardBlocks). The
    // Postfix created groups regardless.
    private static void ArmyGroupManagementRepeatsVanillaGuard()
    {
        TestHarness.AssertTrue(ArmyGroupDoctrine.ArmyGroupsAllowed(true, true, false), "allowed");
        TestHarness.AssertFalse(ArmyGroupDoctrine.ArmyGroupsAllowed(false, true, false), "game option off");
        TestHarness.AssertFalse(ArmyGroupDoctrine.ArmyGroupsAllowed(true, false, false), "reform (project 88/89) not researched");
        TestHarness.AssertFalse(ArmyGroupDoctrine.ArmyGroupsAllowed(true, true, true), "vanilla CIC guard blocks this faction");
        LogPass("c2a army group management repeats vanilla guard");
    }

    // Config/commanders.txt has 6 Lees, 4 Johnstons, 3 Shermans, 2 Grants.
    private static void CommanderNameMatchDisambiguatesHistoricalSurnames()
    {
        TestHarness.AssertTrue(ArmyGroupDoctrine.CommanderNameMatches(1, "lee", "Robert E. Lee"), "R.E. Lee");
        TestHarness.AssertFalse(ArmyGroupDoctrine.CommanderNameMatches(1, "Lee", "Fitzhugh Lee"), "not Fitzhugh Lee");
        TestHarness.AssertFalse(ArmyGroupDoctrine.CommanderNameMatches(1, "lee", "Stephen D. Lee"), "not S.D. Lee");
        TestHarness.AssertFalse(ArmyGroupDoctrine.CommanderNameMatches(1, "lee", "William H.F. Lee"), "not Rooney Lee");
        TestHarness.AssertTrue(ArmyGroupDoctrine.CommanderNameMatches(1, "johnston", "Joseph E. Johnston"), "J.E. Johnston");
        TestHarness.AssertFalse(ArmyGroupDoctrine.CommanderNameMatches(1, "Johnston", "Albert S. Johnston"), "not A.S. Johnston");
        TestHarness.AssertTrue(ArmyGroupDoctrine.CommanderNameMatches(0, "sherman", "William T. Sherman"), "W.T. Sherman");
        TestHarness.AssertFalse(ArmyGroupDoctrine.CommanderNameMatches(0, "sherman", "Thomas W. Sherman"), "not T.W. Sherman");
        TestHarness.AssertTrue(ArmyGroupDoctrine.CommanderNameMatches(0, "grant", "Ulysses S. Grant"), "U.S. Grant");
        TestHarness.AssertFalse(ArmyGroupDoctrine.CommanderNameMatches(0, "grant", "Lewis A. Grant"), "not L.A. Grant");
        TestHarness.AssertTrue(ArmyGroupDoctrine.CommanderNameMatches(1, "Beauregard", "P.G.T. Beauregard"), "dotted initials");
        TestHarness.AssertTrue(ArmyGroupDoctrine.CommanderNameMatches(1, "Garnett", "Robert S. Garnett"), "R.S. Garnett (NW Virginia 1861)");
        TestHarness.AssertFalse(ArmyGroupDoctrine.CommanderNameMatches(1, "Garnett", "Richard B. Garnett"), "not R.B. Garnett");
        LogPass("c2a commander name match disambiguates historical surnames");
    }

    private static void CommanderNameMatchFallsBackToExactSurname()
    {
        TestHarness.AssertTrue(ArmyGroupDoctrine.CommanderNameMatches(1, "smith", "Edmund Kirby Smith"), "uncatalogued surname -> last token");
        TestHarness.AssertFalse(ArmyGroupDoctrine.CommanderNameMatches(1, "smith", "John Smithson"), "no substring match");
        TestHarness.AssertFalse(ArmyGroupDoctrine.CommanderNameMatches(1, "lee", "Leonidas Polk"), "no substring match on first name");
        TestHarness.AssertFalse(ArmyGroupDoctrine.CommanderNameMatches(1, "lee", null), "null name");
        LogPass("c2a commander name match falls back to exact surname token");
    }

    // FindPreferredCommander must use vanilla Commander.CanBePickedForAIReplacement
    // (decompile 60748: alliance, IsAvailable, not serving under player, not
    // staff, not already army-group commander, not CIC) and skip the W&L avatar.
    private static void ArmyGroupCommanderEligibilityMirrorsVanilla()
    {
        TestHarness.AssertTrue(ArmyGroupDoctrine.IsEligibleArmyGroupCommander(true, false, true), "vanilla-pickable, commands a member unit");
        TestHarness.AssertFalse(ArmyGroupDoctrine.IsEligibleArmyGroupCommander(false, false, true), "vanilla says not pickable");
        TestHarness.AssertFalse(ArmyGroupDoctrine.IsEligibleArmyGroupCommander(true, true, true), "W&L chosen commander");
        TestHarness.AssertFalse(ArmyGroupDoctrine.IsEligibleArmyGroupCommander(true, false, false), "commands an unrelated formation");
        LogPass("c2a army group commander eligibility mirrors vanilla replacement filter");
    }

    // vanilla GetArmyGroupInRange (75631) skips the W&L player's group; capacity
    // (CanAddFurtherUnits 75770) gates attaching only.
    private static void ArmyGroupAcceptsUnitsMirrorsGetArmyGroupInRange()
    {
        TestHarness.AssertTrue(ArmyGroupDoctrine.IsPlayerArmyGroup(true, 5, 5), "W&L player's army group");
        TestHarness.AssertFalse(ArmyGroupDoctrine.IsPlayerArmyGroup(false, 5, 5), "chosen commander id irrelevant outside W&L");
        TestHarness.AssertFalse(ArmyGroupDoctrine.IsPlayerArmyGroup(true, 12, 5), "AI group");
        LogPass("c2a army group player exclusion mirrors GetArmyGroupInRange");
    }

    // M3: the group the plan's units attach to and the group whose commander is
    // changed are different questions. A full own group must not divert the
    // appointment onto an unrelated group found in range.
    private static void ArmyGroupAppointsOnlyOnGroupHoldingPlanUnits()
    {
        TestHarness.AssertEqual(ArmyGroupPlanAction.AppointOwnOnly,
            ArmyGroupDoctrine.ResolveArmyGroupPlanAction(ownGroupFound: true, ownGroupHasRoom: false, areaGroupFound: true),
            "full own group + unrelated group in range -> appoint on own group only");
        TestHarness.AssertEqual(ArmyGroupPlanAction.AttachToOwn,
            ArmyGroupDoctrine.ResolveArmyGroupPlanAction(true, true, true), "own group with room");
        TestHarness.AssertEqual(ArmyGroupPlanAction.AttachToArea,
            ArmyGroupDoctrine.ResolveArmyGroupPlanAction(false, false, true), "no own group -> join area group");
        TestHarness.AssertEqual(ArmyGroupPlanAction.CreateNew,
            ArmyGroupDoctrine.ResolveArmyGroupPlanAction(false, false, false), "nothing in range -> create");
        TestHarness.AssertFalse(ArmyGroupDoctrine.ShouldAppointAfter(ArmyGroupPlanAction.AttachToArea, 0), "area group with none of our units attached -> no appointment");
        TestHarness.AssertTrue(ArmyGroupDoctrine.ShouldAppointAfter(ArmyGroupPlanAction.AttachToArea, 1), "area group now holds a plan unit");
        TestHarness.AssertTrue(ArmyGroupDoctrine.ShouldAppointAfter(ArmyGroupPlanAction.AppointOwnOnly, 0), "own group holds plan units");
        TestHarness.AssertFalse(ArmyGroupDoctrine.ShouldAppointAfter(ArmyGroupPlanAction.None, 0), "no action");
        LogPass("c2a army group appoints only on a group holding plan units");
    }

    // C-CmdrReplace: the displaced commander was "first army-group commander by
    // index", so "Bragg -> Western" could unseat Lee. Resolve by role + theater.
    private static System.Collections.Generic.List<ArmyGroupRoleCandidate> SampleGroups()
    {
        return new System.Collections.Generic.List<ArmyGroupRoleCandidate>
        {
            new ArmyGroupRoleCandidate(0, Theater.East, "VirginiaCapitalCorridor", 60000f, false),
            new ArmyGroupRoleCandidate(1, Theater.West, "Unassigned", 45000f, false),
            new ArmyGroupRoleCandidate(2, Theater.East, "ShenandoahValley", 15000f, false),
            new ArmyGroupRoleCandidate(3, Theater.West, "Unassigned", 20000f, false),
        };
    }

    private static void SuccessionRoleResolvesTheaterArmyGroup()
    {
        var groups = SampleGroups();
        TestHarness.AssertEqual(0, CommanderAssignmentGuard.ResolveRoleTargetGroup("ANV", groups), "ANV -> main eastern group");
        TestHarness.AssertEqual(0, CommanderAssignmentGuard.ResolveRoleTargetGroup("AoP", groups), "AoP -> main eastern group");
        TestHarness.AssertEqual(1, CommanderAssignmentGuard.ResolveRoleTargetGroup("Western", groups), "Western -> largest western group");
        TestHarness.AssertEqual(2, CommanderAssignmentGuard.ResolveRoleTargetGroup("Valley", groups), "Valley -> Shenandoah group");
        LogPass("c2a succession role resolves the theater army group");
    }

    private static void SuccessionRoleNeverTargetsPlayerOrWrongFront()
    {
        var westOnly = new System.Collections.Generic.List<ArmyGroupRoleCandidate>
        {
            new ArmyGroupRoleCandidate(0, Theater.West, "Unassigned", 50000f, false),
        };
        TestHarness.AssertEqual(-1, CommanderAssignmentGuard.ResolveRoleTargetGroup("ANV", westOnly), "no eastern group -> defer, never unseat the west");
        TestHarness.AssertEqual(-1, CommanderAssignmentGuard.ResolveRoleTargetGroup("Valley", SampleGroups().GetRange(0, 2)), "no valley group -> defer");
        var playerEast = new System.Collections.Generic.List<ArmyGroupRoleCandidate>
        {
            new ArmyGroupRoleCandidate(0, Theater.East, "VirginiaCapitalCorridor", 60000f, true),
            new ArmyGroupRoleCandidate(1, Theater.East, "WashingtonDefenses", 10000f, false),
        };
        TestHarness.AssertEqual(1, CommanderAssignmentGuard.ResolveRoleTargetGroup("AoP", playerEast), "W&L player's group is never displaced");
        LogPass("c2a succession role never targets player group or another front");
    }

    private static void SuccessionGeneralInChiefIsNotAnArmyGroup()
    {
        TestHarness.AssertFalse(CommanderAssignmentGuard.RoleIsArmyGroupCommand("GeneralInChief"), "GiC is GameVars.alliance.commanderinchief, not an army group");
        TestHarness.AssertTrue(CommanderAssignmentGuard.RoleIsArmyGroupCommand("Western"), "Western is an army group");
        TestHarness.AssertEqual(-1, CommanderAssignmentGuard.ResolveRoleTargetGroup("GeneralInChief", SampleGroups()), "GiC resolves no group");
        TestHarness.AssertEqual(-1, CommanderAssignmentGuard.ResolveRoleTargetGroup(null, SampleGroups()), "null role");
        LogPass("c2a succession general-in-chief is not an army-group swap");
    }

    // CommanderAssignmentPreviousCommandPatch set prior.commander = -1, leaving a
    // commanderless unit that vanilla indexes as GameVars.commander[-1]. Vanilla's
    // own fix for a vacated command is BattleUnits.ReplaceCommanderOfUnit (81389),
    // exactly what ArmyGroup.AppointCommander does first (75575).
    private static void StalePreviousCommandIsRefilledNotOrphaned()
    {
        TestHarness.AssertEqual(PreviousCommandAction.ReplaceViaVanilla,
            CommanderAssignmentGuard.ResolvePreviousCommandAction(staleReference: true, replacementInProgress: false),
            "stale previous unit gets a vanilla replacement commander");
        TestHarness.AssertEqual(PreviousCommandAction.None,
            CommanderAssignmentGuard.ResolvePreviousCommandAction(staleReference: true, replacementInProgress: true),
            "re-entrant AssignCommando from our own ReplaceCommanderOfUnit call is left alone");
        TestHarness.AssertEqual(PreviousCommandAction.None,
            CommanderAssignmentGuard.ResolvePreviousCommandAction(staleReference: false, replacementInProgress: false),
            "nothing stale -> nothing to do");
        LogPass("c2a stale previous command is refilled by vanilla, never orphaned");
    }

    // C-DefOps: vanilla AssignUnitToDefendCapital (decompile 11668) computes
    // num2 = aiminimumstrengthformovement + enemy pressure - own units whose
    // last waypoint is closer than the closest enemy; when num2 <= 0 it removes
    // the FARTHEST defender every call (~11778). A pledge vanilla does not also
    // need is therefore stripped next call: the old Postfix churned forever.
    private static void CapitalNeedMatchesVanillaFormula()
    {
        var enemies = new[] { new CapitalForce(100f, 10000f, 0.8f), new CapitalForce(700f, 5000f, 1f) };
        var own = new[] { new CapitalForce(50f, 2000f, 0.5f), new CapitalForce(150f, 9000f, 1f) };
        float need = CampaignMilitaryGates.VanillaCapitalDefenseNeed(500f, 650f, enemies, own);
        // closest enemy 100; only the first enemy is inside 650; pressure = min(10000, 8000)/100^0.3*0.5
        float pressure = 8000f / (float)Math.Pow(100f, 0.3f) * 0.5f;
        // own: waypoint 50 < 100 subtracts 2000*0.5; waypoint 150 does not
        float expected = 500f + pressure - 1000f;
        TestHarness.AssertTrue(Math.Abs(expected - need) < 0.01f, "vanilla num2 reproduced: expected " + expected + " got " + need);
        float noEnemies = CampaignMilitaryGates.VanillaCapitalDefenseNeed(500f, 650f, new CapitalForce[0], own);
        // closest enemy distance stays 9999, so every own unit with a waypoint counts
        TestHarness.AssertTrue(Math.Abs((500f - 1000f - 9000f) - noEnemies) < 0.01f, "no enemy -> all own waypoints count");
        LogPass("c2a capital need matches vanilla num2 formula");
    }

    private static void CapitalPrePledgeOnlyWhenVanillaAlsoNeedsDefenders()
    {
        TestHarness.AssertTrue(CampaignMilitaryGates.ShouldPrePledgeCapitalDefender(800f, 1200f), "both need -> pledge before vanilla's move loop");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldPrePledgeCapitalDefender(-50f, 1200f), "vanilla sufficient -> it would strip the pledge next call");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldPrePledgeCapitalDefender(800f, -10f), "personality satisfied -> leave it to vanilla");
        LogPass("c2a capital pre-pledge only when vanilla also needs defenders");
    }

    private static void CapitalPersonalityNeedDoesNotDoubleCountPledgedDefenders()
    {
        var enemies = new[] { new CapitalForce(100f, 10000f, 1f) };
        var own = new[] { new CapitalForce(50f, 4000f, 1f) };
        // the same unit is a pledged defender: it already counted via its waypoint
        var pledgedArrived = new[] { new CapitalForce(50f, 4000f, 1f) };
        var pledgedMarching = new[] { new CapitalForce(400f, 4000f, 1f) };
        float baseNeed = CampaignMilitaryGates.PersonalityCapitalDefenseNeed(500f, 650f, enemies, own, new CapitalForce[0], 1f, 1f);
        float arrived = CampaignMilitaryGates.PersonalityCapitalDefenseNeed(500f, 650f, enemies, own, pledgedArrived, 1f, 1f);
        float marching = CampaignMilitaryGates.PersonalityCapitalDefenseNeed(500f, 650f, enemies, own, pledgedMarching, 1f, 1f);
        TestHarness.AssertTrue(Math.Abs(baseNeed - arrived) < 0.01f, "arrived defender not subtracted twice");
        TestHarness.AssertTrue(Math.Abs((baseNeed - 3000f) - marching) < 0.01f, "marching defender counts at 0.75 strength");
        LogPass("c2a capital personality need does not double count pledged defenders");
    }

    // C-ArmyArea: IsIdleForAreaOrder only excluded battle/retreat/paths and the
    // two operation lists, so capital defenders, depot/fort builders, raid and
    // sea-invasion forces were sent "home" and vanilla sent them back: ping-pong.
    // The busy set mirrors vanilla's own exclusions (IsUnitAvailableForOffensive
    // Operations 14080, AssignUnitToDefendCapital 11800).
    private static void AreaOrderSkipsEveryVanillaBusyAssignment()
    {
        TestHarness.AssertTrue(CampaignMilitaryGates.IsIdleForAreaOrder(new AreaOrderBusyFlags()), "idle unit");
        string[] names =
        {
            "InBattle", "OnRetreat", "HasPaths", "InOffensiveOperation", "InDefensiveOperation",
            "DefendingCapital", "BuildingSupplyDepot", "BuildingFort", "RaidForce", "SeaInvasionForce",
            "DefensiveOrderQueued", "TakingTown"
        };
        foreach (var name in names)
        {
            object flags = new AreaOrderBusyFlags();
            var field = typeof(AreaOrderBusyFlags).GetField(name);
            TestHarness.AssertTrue(field != null, "flag " + name + " exists");
            field.SetValue(flags, true);
            TestHarness.AssertFalse(CampaignMilitaryGates.IsIdleForAreaOrder((AreaOrderBusyFlags)flags), name + " blocks area orders");
        }
        LogPass("c2a area order skips every vanilla busy assignment");
    }

    // C-Coastal: vanilla CheckForEndOfDefensiveOperations (decompile 13836) stops
    // and drops a defensive-op unit once no enemy is within its buglerange of its
    // last path position. Ordering coastal guards onto the asset itself meant a
    // daily stop/go; the destination must keep the threat within bugle range.
    private static void CoastalInterceptKeepsEnemyInsideBugleRange()
    {
        CampaignMilitaryGates.CoastalInterceptPoint(0f, 0f, 100f, 0f, 20f, out float x, out float z);
        float toEnemy = (float)Math.Sqrt((x - 100f) * (x - 100f) + z * z);
        TestHarness.AssertTrue(toEnemy < 20f, "destination within bugle range of the enemy, got " + toEnemy);
        TestHarness.AssertTrue(x > 0f && x < 100f && Math.Abs(z) < 0.001f, "destination between asset and enemy");
        LogPass("c2a coastal intercept keeps enemy inside bugle range");
    }

    private static void CoastalInterceptHoldsAnchorWhenEnemyAlreadyClose()
    {
        CampaignMilitaryGates.CoastalInterceptPoint(10f, 5f, 15f, 5f, 20f, out float x, out float z);
        TestHarness.AssertEqual(10f, x, "anchor x kept");
        TestHarness.AssertEqual(5f, z, "anchor z kept");
        CampaignMilitaryGates.CoastalInterceptPoint(10f, 5f, 10f, 5f, 0f, out x, out z);
        TestHarness.AssertEqual(10f, x, "degenerate bugle range keeps anchor");
        LogPass("c2a coastal intercept holds the anchor when enemy already close");
    }

    // C-CoordOff -------------------------------------------------------------
    private static CoordinatedOperationCandidate Candidate(int id, float strength)
    {
        return new CoordinatedOperationCandidate
        {
            StableUnitId = id,
            DisplayUnitKey = "unit-" + id,
            AllianceId = 1,
            Level = FormationLevel.Corps,
            Directive = FormationDirective.Counterstroke,
            AreaKey = "VirginiaCapitalCorridor",
            SectorKey = "VirginiaCapitalCorridor",
            CombatAvailability = strength,
            Readiness = 0.8f,
            Morale = 0.8f,
            Ammo = 0.8f,
            Supply = 0.8f,
            OffensiveAllowed = true,
            DefensiveAllowed = true,
            DirectMovementAllowed = true,
            CommitMode = CoordinatedCommitMode.DirectMovement
        };
    }

    // Any eligible lead became SingleLead at any ratio ("single-committable-lead").
    private static void CoordinatedSingleLeadNeedsReinforceRatio()
    {
        var input = new CoordinatedOperationInput
        {
            AllianceId = 1,
            Intent = CoordinatedOperationIntent.Attack,
            TargetAreaKey = "VirginiaCapitalCorridor",
            TargetSectorKey = "VirginiaCapitalCorridor",
            TargetEnemyStrength = 20000f,
            Options = CoordinatedOperationOptions.StableDefaults(20000f),
            Candidates = new System.Collections.Generic.List<CoordinatedOperationCandidate> { Candidate(10, 5000f) }
        };
        var weak = CoordinatedOperationPackageLedger.Build(input);
        TestHarness.AssertEqual(CoordinatedOperationDecision.Delay, weak.Decision, "0.25 ratio lone lead must not attack");

        input.Candidates = new System.Collections.Generic.List<CoordinatedOperationCandidate> { Candidate(10, 18000f) };
        var adequate = CoordinatedOperationPackageLedger.Build(input);
        TestHarness.AssertEqual(CoordinatedOperationDecision.SingleLead, adequate.Decision, "0.9 ratio lone lead may commit");

        // OperationalProbeLedger probes are deliberately small contact-making
        // packages; the ratio gate is for attack/reinforce commitments only.
        input.Intent = CoordinatedOperationIntent.Probe;
        input.Candidates = new System.Collections.Generic.List<CoordinatedOperationCandidate> { Candidate(10, 5000f) };
        var probe = CoordinatedOperationPackageLedger.Build(input);
        TestHarness.AssertEqual(CoordinatedOperationDecision.SingleLead, probe.Decision, "probe lead commits below reinforce ratio");
        LogPass("c2a coordinated single lead needs the reinforce ratio");
    }

    // vanilla CheckOffensiveMovements early return (decompile 14200).
    private static void CoordinatedOffensiveMirrorsVanillaEarlyOuts()
    {
        TestHarness.AssertTrue(CoordinatedOffensiveVanillaGates.VanillaWouldEvaluate(5, 0.1f, true, true, true, false, false, false), "normal call");
        TestHarness.AssertFalse(CoordinatedOffensiveVanillaGates.VanillaWouldEvaluate(0, 0.1f, true, true, true, false, false, false), "no own units");
        TestHarness.AssertFalse(CoordinatedOffensiveVanillaGates.VanillaWouldEvaluate(5, 0f, true, true, true, false, false, false), "timediff <= 0");
        TestHarness.AssertFalse(CoordinatedOffensiveVanillaGates.VanillaWouldEvaluate(5, 0.1f, false, true, true, false, false, false), "null lead");
        TestHarness.AssertFalse(CoordinatedOffensiveVanillaGates.VanillaWouldEvaluate(5, 0.1f, true, false, true, false, false, false), "inactive lead");
        TestHarness.AssertFalse(CoordinatedOffensiveVanillaGates.VanillaWouldEvaluate(5, 0.1f, true, true, false, false, false, false), "recruitingzonesupdated false: one commit per job cycle");
        TestHarness.AssertFalse(CoordinatedOffensiveVanillaGates.VanillaWouldEvaluate(5, 0.1f, true, true, true, true, false, false), "lead already in offensive op");
        TestHarness.AssertFalse(CoordinatedOffensiveVanillaGates.VanillaWouldEvaluate(5, 0.1f, true, true, true, false, true, false), "lead in defensive op");
        TestHarness.AssertFalse(CoordinatedOffensiveVanillaGates.VanillaWouldEvaluate(5, 0.1f, true, true, true, false, false, true), "lead building depot");
        LogPass("c2a coordinated offensive mirrors vanilla early outs");
    }

    // vanilla tempo roll (14336-14350): tval = aiagressiveness[chapter] * timediff
    // * 10 * factionAggression * usedcampaignagressiveness * winterModifier
    // + clamp01(idleYears^3), times the W&L multiplier; go when roll <= tval.
    private static void CoordinatedOffensiveTempoGateMatchesVanilla()
    {
        float tval = CoordinatedOffensiveVanillaGates.TempoProbability(0.5f, 0.02f, 1f, 1f, 1f, 0f, false, 1.5f, false);
        TestHarness.AssertTrue(Math.Abs(0.1f - tval) < 1e-5f, "chapter x timediff x 10, got " + tval);
        float winter = CoordinatedOffensiveVanillaGates.TempoProbability(0.5f, 0.02f, 1f, 1f, 0.2f, 0f, false, 1.5f, false);
        TestHarness.AssertTrue(Math.Abs(0.02f - winter) < 1e-5f, "winter modifier scales the roll");
        float idle = CoordinatedOffensiveVanillaGates.TempoProbability(0f, 0.02f, 1f, 1f, 1f, 0.5f, false, 1.5f, false);
        TestHarness.AssertTrue(Math.Abs(0.125f - idle) < 1e-5f, "idle time adds clamp01(idle^3)");
        float wl = CoordinatedOffensiveVanillaGates.TempoProbability(0.5f, 0.02f, 1f, 1f, 1f, 0f, true, 1.5f, false);
        TestHarness.AssertTrue(Math.Abs(0.15f - wl) < 1e-5f, "W&L scenario multiplies once (14344)");
        float wlPlayer = CoordinatedOffensiveVanillaGates.TempoProbability(0.5f, 0.02f, 1f, 1f, 1f, 0f, true, 1.5f, true);
        TestHarness.AssertTrue(Math.Abs(0.225f - wlPlayer) < 1e-5f, "player's group in the package multiplies again (14346)");
        // num2 at the TARGET area (14230): enemy strength * avgtroopmorale * importance * weapon / national morale factor
        float num2 = CoordinatedOffensiveVanillaGates.TargetEnemyWeight(1000f, 0.8f, 2f, 0.5f, 2f);
        TestHarness.AssertTrue(Math.Abs(400f - num2) < 1e-3f, "vanilla num2 (1000*0.8*2*0.5/2), got " + num2);
        TestHarness.AssertEqual(0f, CoordinatedOffensiveVanillaGates.TargetEnemyWeight(0f, 0.8f, 2f, 0.5f, 2f), "no enemy -> no roll");
        TestHarness.AssertTrue(CoordinatedOffensiveVanillaGates.TempoPasses(0.05f, 0.1f, false), "roll under tval goes");
        TestHarness.AssertFalse(CoordinatedOffensiveVanillaGates.TempoPasses(0.5f, 0.1f, false), "roll over tval waits");
        TestHarness.AssertTrue(CoordinatedOffensiveVanillaGates.TempoPasses(0.99f, 0f, true), "scripted/debug forcing always goes");
        LogPass("c2a coordinated offensive tempo gate matches vanilla");
    }

    // Package locks protect only units placed in unitsinoffensiveoperations; a
    // W&L current-order commit never enters that list, so its lock never cleared.
    private static void CoordinatedPackageLockExpiresWithPath()
    {
        TestHarness.AssertTrue(CoordinatedOperationRuntime.PackageLockLive(3), "path still running keeps lock");
        TestHarness.AssertFalse(CoordinatedOperationRuntime.PackageLockLive(0), "path consumed expires lock");
        LogPass("c2a coordinated package lock expires once the path is consumed");
    }

    // Vanilla seats historical figures on auto-created army groups early
    // (CreateNewArmyGroup 75653 -> GetSuitableCommander includeofficerswithcommand),
    // and CanBePickedForAIReplacement (60748) rejects any sitting army-group
    // commander - so Lee/Johnston/Grant would never be "pickable". A sitting
    // AG commander who is otherwise free moves after his old group is reseated
    // from vanilla's own pool (GetAvailableArmyGroupCommanders 75546);
    // AppointCommander never clears the old group's commanderid.
    private static void SuccessionReseatsReplacementsOldArmyGroup()
    {
        TestHarness.AssertEqual(ReplacementAppointment.AppointDirect,
            CommanderAssignmentGuard.ResolveReplacementAppointment(vanillaPickable: true, leadsOtherArmyGroup: false, freeApartFromArmyGroup: true, successorAvailable: false),
            "pickable replacement appointed directly");
        TestHarness.AssertEqual(ReplacementAppointment.ReseatThenAppoint,
            CommanderAssignmentGuard.ResolveReplacementAppointment(false, true, true, true),
            "sitting AG commander moves after his old group is reseated");
        TestHarness.AssertEqual(ReplacementAppointment.Defer,
            CommanderAssignmentGuard.ResolveReplacementAppointment(false, true, true, false),
            "no successor -> never leave one man on two groups");
        TestHarness.AssertEqual(ReplacementAppointment.Defer,
            CommanderAssignmentGuard.ResolveReplacementAppointment(false, true, false, true),
            "wounded/captured/staff/CIC/player-subordinate stays put");
        TestHarness.AssertEqual(ReplacementAppointment.Defer,
            CommanderAssignmentGuard.ResolveReplacementAppointment(false, false, true, true),
            "not pickable for another reason -> defer");
        LogPass("c2a succession reseats the replacement's old army group");
    }

    // M1: a committed unit skips re-issue (already in unitsindefensiveoperations)
    // and fell out of the per-signature tracking on tick 2, so IsCustomOrdered
    // was only true on the commit tick and the release never saw it.
    private static void CoastalAssignmentsCarryForwardWhileHeld()
    {
        var prior = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<int>>
        {
            { "sig-a", new System.Collections.Generic.HashSet<int> { 1, 2 } },
            { "sig-gone", new System.Collections.Generic.HashSet<int> { 3 } },
        };
        var thisTick = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<int>>
        {
            { "sig-a", new System.Collections.Generic.HashSet<int>() },
        };
        CampaignMilitaryGates.CarryForwardCustomOrders(prior, thisTick, id => id != 2);
        TestHarness.AssertTrue(thisTick["sig-a"].Contains(1), "held unit stays tracked under its live signature");
        TestHarness.AssertFalse(thisTick["sig-a"].Contains(2), "unit no longer held (vanilla dropped it) is not carried");
        TestHarness.AssertFalse(thisTick.ContainsKey("sig-gone"), "vanished signature is not resurrected (release path handles it)");
        LogPass("c2a coastal assignments carry forward while held");
    }

    // L3: vanilla's first branch only QUEUES a DefensiveMovingOrder (13713); the
    // unit has not moved yet, so cancelling the order is the whole revert. The
    // attack branch (13771) calls MoveUnitTo directly, so that needs a move back.
    private static void ForbiddenDefensiveCommitRevertMatchesHowVanillaMoved()
    {
        TestHarness.AssertEqual(ForbiddenCommitRevert.CancelQueuedOrder,
            CampaignMilitaryGates.ResolveForbiddenCommitRevert(queuedOrderRunning: true, hasPriorTheaterPosition: true),
            "queued order -> cancel only, no move");
        TestHarness.AssertEqual(ForbiddenCommitRevert.MoveBack,
            CampaignMilitaryGates.ResolveForbiddenCommitRevert(false, true), "already moving -> move back");
        TestHarness.AssertEqual(ForbiddenCommitRevert.None,
            CampaignMilitaryGates.ResolveForbiddenCommitRevert(false, false), "no prior position -> leave the path alone");
        LogPass("c2a forbidden defensive commit revert matches how vanilla moved");
    }

    // L5 (re-review): vanilla's positiondeficit is read from the painted
    // recruitment heatmap cells (ZoneRecruiting 12474-12476); PaintObject
    // (34117-34197) samples ring points with angle rounding, so no closed-form
    // kernel reproduces it (offset (1,1) = 1.25 x strength). The plan side must
    // read the same painted grids via the public Heatmap.GetValueOnPos(Vector3,
    // bool usearrayvalues) (33840). This pins the vanilla API the patch reads.
    private static void TransferStrengthReadsVanillaHeatmapGrids()
    {
        var factionType = HarmonyLib.AccessTools.Inner(typeof(AICampaign), "AIFaction");
        TestHarness.AssertTrue(factionType != null, "AICampaign.AIFaction exists");
        TestHarness.AssertEqual(2, CampaignMilitaryGates.TransferHeatmapFields.Length, "own + enemy grids");
        foreach (var name in CampaignMilitaryGates.TransferHeatmapFields)
        {
            var field = factionType.GetField(name);
            TestHarness.AssertTrue(field != null, "AIFaction." + name + " is public");
            TestHarness.AssertEqual(typeof(Heatmap), field.FieldType, name + " is a vanilla Heatmap");
        }
        var read = typeof(Heatmap).GetMethod("GetValueOnPos", new[] { typeof(UnityEngine.Vector3), typeof(bool) });
        TestHarness.AssertTrue(read != null && read.IsPublic && !read.IsStatic, "public instance Heatmap.GetValueOnPos(Vector3, bool)");
        LogPass("c2a transfer strength reads vanilla heatmap grids");
    }

    // Re-review: only a real failed roll ends the call (ownunits emptied so
    // vanilla does not roll twice); a tempo-gate exception must let vanilla run
    // unchanged, and a pass proceeds to commit.
    private static void CoordinatedTempoErrorLeavesVanillaUntouched()
    {
        TestHarness.AssertTrue(CoordinatedOffensiveVanillaGates.SuppressVanillaThisCall(TempoVerdict.Wait), "failed roll ends the call");
        TestHarness.AssertFalse(CoordinatedOffensiveVanillaGates.SuppressVanillaThisCall(TempoVerdict.Error), "gate error: vanilla runs unchanged");
        TestHarness.AssertFalse(CoordinatedOffensiveVanillaGates.SuppressVanillaThisCall(TempoVerdict.Go), "pass proceeds to commit");
        LogPass("c2a coordinated tempo error leaves vanilla untouched");
    }
}
