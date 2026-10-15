using System;
using WhiskeyRealism.Strategic;

// WS C2a — campaign military write surfaces (2026-10-14 remediation plan).
internal static class CampaignMilitaryFixTests
{
    public static void Run()
    {
        SeaInvasionStrengthResolvesInstanceOverload();
        SeaInvasionLandedSignalUsesEnemyBalance();
        WlCampaignContextSourcesAreRealVanillaFields();
        WlCampaignContextKeyDistinguishesCareersAndSaves();
        ListSnapshotRestoreUndoesVanillaMutation();
        ListSnapshotRestoreIsIdempotent();
        TransferRequirementIsLocalNotTheaterWide();
        TransferSteersOnlyWhenPlanShortfallIsWorse();
        ImportanceRedirectHonoursVanillaCandidateGates();
        ImportanceRedirectStaysInPlanTheater();
        ArmyGroupManagementRepeatsVanillaGuard();
        CommanderNameMatchDisambiguatesHistoricalSurnames();
        CommanderNameMatchFallsBackToExactSurname();
        ArmyGroupCommanderEligibilityMirrorsVanilla();
        ArmyGroupAcceptsUnitsMirrorsGetArmyGroupInRange();
        SuccessionRoleResolvesTheaterArmyGroup();
        SuccessionRoleNeverTargetsPlayerOrWrongFront();
        SuccessionGeneralInChiefIsNotAnArmyGroup();
        StalePreviousCommandIsRefilledNotOrphaned();
        CapitalNeedMatchesVanillaFormula();
        CapitalPrePledgeOnlyWhenVanillaAlsoNeedsDefenders();
        CapitalPersonalityNeedDoesNotDoubleCountPledgedDefenders();
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

    // C-DeadRefl-scene: SceneManagement.currentcampaign / currentsave do not
    // exist; the context key collapsed to "campaign:a:f::" for every career.
    private static void WlCampaignContextSourcesAreRealVanillaFields()
    {
        var sources = WlStrategicOrderBridge.CampaignContextSources;
        TestHarness.AssertEqual(4, sources.Length, "scenario, level, sublevel, save folder");
        foreach (var source in sources)
        {
            var field = source.Owner.GetField(source.Field,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            TestHarness.AssertTrue(field != null, source.Owner.Name + "." + source.Field + " exists");
            TestHarness.AssertEqual(typeof(string), field.FieldType, source.Field + " is a string");
        }
        LogPass("c2a wl campaign context key reads real vanilla fields");
    }

    private static void WlCampaignContextKeyDistinguishesCareersAndSaves()
    {
        string a = WlStrategicOrderBridge.BuildCampaignContextKey(0, 1, "002", "002", "A", "Save");
        string sameAgain = WlStrategicOrderBridge.BuildCampaignContextKey(0, 1, "002", "002", "A", "Save");
        string otherCareer = WlStrategicOrderBridge.BuildCampaignContextKey(0, 1, "002", "002", "B", "Save");
        string otherSave = WlStrategicOrderBridge.BuildCampaignContextKey(0, 1, "002", "002", "A", "Save3");
        string otherScenario = WlStrategicOrderBridge.BuildCampaignContextKey(0, 1, "001", "001", "A", "Save");
        TestHarness.AssertEqual(a, sameAgain, "stable for same context");
        TestHarness.AssertTrue(a != otherCareer, "career sublevel changes key");
        TestHarness.AssertTrue(a != otherSave, "save folder changes key");
        TestHarness.AssertTrue(a != otherScenario, "scenario changes key");
        LogPass("c2a wl campaign context key distinguishes careers and saves");
    }

    // B-46-campaign: the ownunits restore moves into a [HarmonyFinalizer] so it
    // runs even when vanilla CheckForDefensiveOperations throws. The restore
    // itself must be exact and safe to run twice (Postfix + Finalizer paths).
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
    // when army groups are disabled, the reform is not researched, or the W&L
    // player is commander-in-chief. The Postfix created groups regardless.
    private static void ArmyGroupManagementRepeatsVanillaGuard()
    {
        TestHarness.AssertTrue(ArmyGroupDoctrine.ArmyGroupsAllowed(true, true, false, false), "allowed");
        TestHarness.AssertFalse(ArmyGroupDoctrine.ArmyGroupsAllowed(false, true, false, false), "game option off");
        TestHarness.AssertFalse(ArmyGroupDoctrine.ArmyGroupsAllowed(true, false, false, false), "reform (project 88/89) not researched");
        TestHarness.AssertFalse(ArmyGroupDoctrine.ArmyGroupsAllowed(true, true, true, true), "W&L player is CIC");
        TestHarness.AssertTrue(ArmyGroupDoctrine.ArmyGroupsAllowed(true, true, true, false), "W&L player not CIC");
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

    // vanilla GetArmyGroupInRange (75631) skips the W&L player's group and
    // groups that cannot take more units (CanAddFurtherUnits 75770).
    private static void ArmyGroupAcceptsUnitsMirrorsGetArmyGroupInRange()
    {
        TestHarness.AssertTrue(ArmyGroupDoctrine.ArmyGroupAcceptsUnits(false, 12, 5, true), "AI group with room");
        TestHarness.AssertFalse(ArmyGroupDoctrine.ArmyGroupAcceptsUnits(true, 5, 5, true), "W&L player's army group");
        TestHarness.AssertTrue(ArmyGroupDoctrine.ArmyGroupAcceptsUnits(false, 5, 5, true), "chosen commander id irrelevant outside W&L");
        TestHarness.AssertFalse(ArmyGroupDoctrine.ArmyGroupAcceptsUnits(false, 12, 5, false), "group full");
        LogPass("c2a army group accepts units mirrors GetArmyGroupInRange");
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
}
