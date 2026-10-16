using System;
using WhiskeyRealism.Strategic;

// WS C2a — campaign military write surfaces (2026-10-14 remediation plan).
internal static class CampaignMilitaryFixTests
{
    public static void Run()
    {
        ListSnapshotRestoreUndoesVanillaMutation();
        ListSnapshotRestoreIsIdempotent();
        ForbiddenDefensiveCommitRevertMatchesHowVanillaMoved();
        ImportanceRedirectHonoursVanillaCandidateGates();
        ImportanceRedirectStaysInSteeredSectors();
        ArmyGroupManagementRepeatsVanillaGuard();
        CommanderNameMatchDisambiguatesHistoricalSurnames();
        CommanderNameMatchFallsBackToExactSurname();
        ArmyGroupAcceptsUnitsMirrorsGetArmyGroupInRange();
        ArmyGroupFullOwnGroupTakesNoMore();
        StalePreviousCommandIsRefilledNotOrphaned();
        SuccessionReseatsReplacementsOldArmyGroup();
        CapitalNeedMatchesVanillaFormula();
        CoastalInterceptKeepsEnemyInsideBugleRange();
        CoastalInterceptHoldsAnchorWhenEnemyAlreadyClose();
        CoastalAssignmentsCarryForwardWhileHeld();
    }

    private static void LogPass(string message)
    {
        Console.WriteLine("PASS " + message);
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

    // R7: ImportanceValuesPatch overwrote mostvalueableaiareaclose for EVERY
    // source area, bypassing vanilla's candidate gates in
    // CalculateMostValueableAIZones (decompile 10964-10987).
    private static void ImportanceRedirectHonoursVanillaCandidateGates()
    {
        // baseline: the source area is in a sector the plan steers, in range, supply ok, vanilla value clause ok
        TestHarness.AssertTrue(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, true, 200f, 300f, 10f, 50f, true), "eligible target redirects");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            true, true, 0f, 300f, 10f, 50f, true), "aiarea[i] != this");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, true, 301f, 300f, 10f, 50f, true), "distance gate (relocation range x offensive range factor)");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, true, 200f, 300f, 60f, 50f, true), "supply gate ownpoints <= supplycapacity");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, true, 200f, 300f, 10f, 50f, false), "vanilla points/distance/importance clause");
        LogPass("c2a importance redirect honours vanilla candidate gates");
    }

    // G5a re-keyed the theater clause on the plan: only a source area in a MainEffort or Support sector is steered.
    private static void ImportanceRedirectStaysInSteeredSectors()
    {
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, false, 200f, 300f, 10f, 50f, true), "another sector keeps vanilla's local target");
        LogPass("c2a importance redirect stays inside the steered sectors");
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

    // vanilla GetArmyGroupInRange (75631) skips the W&L player's group; capacity
    // (CanAddFurtherUnits 75770) gates attaching only.
    private static void ArmyGroupAcceptsUnitsMirrorsGetArmyGroupInRange()
    {
        TestHarness.AssertTrue(ArmyGroupDoctrine.IsPlayerArmyGroup(true, 5, 5), "W&L player's army group");
        TestHarness.AssertFalse(ArmyGroupDoctrine.IsPlayerArmyGroup(false, 5, 5), "chosen commander id irrelevant outside W&L");
        TestHarness.AssertFalse(ArmyGroupDoctrine.IsPlayerArmyGroup(true, 12, 5), "AI group");
        LogPass("c2a army group player exclusion mirrors GetArmyGroupInRange");
    }

    // The sector's formations join the army group already holding one of them while it has room; a full own
    // group takes no more and nothing else is done (the planner's appointment executor owns commanders), and a
    // group in range of the sector never takes over from a full own group.
    private static void ArmyGroupFullOwnGroupTakesNoMore()
    {
        TestHarness.AssertEqual(ArmyGroupPlanAction.None,
            ArmyGroupDoctrine.ResolveArmyGroupPlanAction(ownGroupFound: true, ownGroupHasRoom: false, areaGroupFound: true),
            "full own group + unrelated group in range -> nothing");
        TestHarness.AssertEqual(ArmyGroupPlanAction.AttachToOwn,
            ArmyGroupDoctrine.ResolveArmyGroupPlanAction(true, true, true), "own group with room");
        TestHarness.AssertEqual(ArmyGroupPlanAction.AttachToArea,
            ArmyGroupDoctrine.ResolveArmyGroupPlanAction(false, false, true), "no own group -> join area group");
        TestHarness.AssertEqual(ArmyGroupPlanAction.CreateNew,
            ArmyGroupDoctrine.ResolveArmyGroupPlanAction(false, false, false), "nothing in range -> create");
        LogPass("c2a army group: a full own group takes no more and nothing else");
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

}
