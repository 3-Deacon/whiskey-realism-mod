using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.Producers;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// BC6a: pure infantry charge doctrine. Vanilla anchors (Assembly-CSharp build 14703111):
// Regiment.UpdateChargeTarget 122199-122345, MicroAICheckForCharges 4905-4930, micro cycle 6
// 5617-5638, StopRegiment 132316 (regimentpaths = 0 at 132397, movementmode untouched).
//
// BC6b: ChargeIntentProducer.Plan over a BattleSnapshot fixture, the executor's
// LeaveChargeMode flag, and the arbiter-fixture DenialPrecedenceOverCharge (ChargeDemotion
// Safety on the Stance channel, this doctrine's Executor intent on CombatBehavior).
// SetMovementMode(3) zeroes combatbehavior/combatbehaviorordered of infantry (124770-124774),
// so an armed regiment in mode 3/6 reads ordered 0.
internal static class ChargeDoctrineTests
{
    public static void Run()
    {
        RunAll(new (string name, Action run)[]
        {
            ("charge doctrine fires on a weaker visible target inside the vanilla radius", ChargeFiresOnWeakerVisibleTargetInRadius),
            ("charge doctrine fires on a disordered or flanked target without the morale margin", ChargeFiresOnDisorderedOrFlankedTarget),
            ("charge doctrine needs the full morale margin", NoChargeWithoutMoraleMargin),
            ("charge doctrine needs the target to be enemyinrangereg[0]", NoChargeWhenTargetNotFirstInRange),
            ("charge doctrine never charges a hidden target", NoChargeWhenHidden),
            ("charge doctrine stays strictly inside the vanilla charge radius", NoChargeBeyondVanillaRadius),
            ("charge doctrine needs own morale and fatigue floors", NoChargeWhenFatiguedOrShaken),
            ("charge doctrine never charges through a friendly lane", NoChargeWhenLaneBlocked),
            ("charge doctrine never charges a stationary regiment", NoChargeWhenStationary),
            ("charge doctrine waits out the refusal backoff before re-arming", NoRearmInsideRefusalBackoff),
            ("charge doctrine never touches player-owned units", NoChargeOnPlayerOwned),
            ("charge doctrine is infantry in assault stance only", NoChargeOutsideInfantryAssault),
            ("charge doctrine does not re-arm a vanilla charge mode already running", NoChargeWhenVanillaChargeModeActive),
            ("charge doctrine restores vanilla's stance-drop cancel for any AI regiment in mode 3", VanillaStanceDropDisarmsUnarmedChargeMode),
            ("charge doctrine only arms the vanilla 122214 gate and leaves the 122259 odds check to vanilla", ChargeComposesWithVanillaTrigger),
            ("charge doctrine holds while the courier carries the order", HoldsWhileCourierPending),
            ("charge doctrine sustains through the stationary volley pause", SustainsThroughVolleyPauseWhenStationary),
            ("charge doctrine holds while the vanilla assault runs", HoldsDuringVanillaAssault),
            ("charge doctrine reverts an armed regiment when conditions fail", RevertWhenArmedConditionsFail),
            ("charge doctrine reverts after the charge resolves", RevertAfterChargeResolves),
            ("charge doctrine charge cycle ends on the vanilla mode transitions", ChargeCycleEndsOnVanillaTransitions),
            ("charge doctrine revert in mode 3 disarms in the same action", RevertInArmedModeDisarms),
            ("charge doctrine revert during an assault disarms once vanilla returns to mode 3", DeferredDisarmAfterAssault),
            ("charge doctrine pending disarm flag follows the action and the mode", PendingDisarmFlagTransitions),
            ("charge doctrine reverts when the group stance is demoted", RevertWhenStanceDemoted),
            ("charge doctrine stance denial beats a charge in every movement mode", DenialPrecedenceOverCharge), // arbiter fixture: BC6b
            ("charge doctrine arms only the two leading regiments", OnlyTwoLeadingRegimentsCharge),
            ("charge doctrine config off never charges and releases its own arming", ConfigOffNeverCharges),
            ("charge doctrine decide and leading selection do not allocate", DecideAndLeadingDoNotAllocate),
            // BC6b: producer over the snapshot, executor flag, arbiter fixture.
            ("charge producer arms the two leading regiments on their visible first target", ProducerArmsTwoLeadingRegiments),
            ("charge producer reads armed from its memory, not the ordered behavior vanilla zeroes", ProducerArmedFromMemoryNotOrderedBehavior),
            ("charge producer arms only on an executor-confirmed charge", ProducerUnconfirmedChargeIsNotArmed),
            ("charge producer takes a fresh lastaichargetime as the baseline, not a refusal", ProducerFreshStampIsNotARefusal),
            ("charge producer backs off a refusal on the doctrine clock with vanilla's renew interval", ProducerRefusalBacksOffWithVanillaRenew),
            ("charge producer measures from the parent group, strictly inside the radius", ProducerDistanceFromParentGroupStrict),
            ("charge producer group stance is 4 only while ordered and current are 4", ProducerGroupStanceNeedsOrderedAndCurrent),
            ("charge producer lane is blocked only by a friendly between regiment and target", ProducerLaneBlockedOnlyBetween),
            ("charge producer lane ignores routed and inactive friendlies", ProducerLaneIgnoresRoutedAndInactive),
            ("charge producer applies vanilla's unit filter (detached, routed, marked for rout, feud)", ProducerVanillaUnitFilter),
            ("charge producer honours vanilla's no-charge-if-outflanked pref", ProducerHonoursNoChargeIfOutflanked),
            ("charge producer arms only when vanilla's experience-weighted odds pass", ProducerArmsOnlyWhenVanillaOddsPass),
            ("charge producer odds read the target's stars, not its true experience", ProducerOddsReadTheTargetsStars),
            ("charge producer excludes regiments of a feuding brigade", ProducerExcludesFeudingBrigadeRegiments),
            ("charge producer reads target state only from a visible target's record", ProducerTargetStateFromVisibleRecord),
            ("charge producer never touches player-owned regiments", ProducerNeverTouchesPlayerOwned),
            ("charge producer disarms after a denied assault once vanilla returns to mode 3", ProducerDeferredDisarmAfterAssault),
            ("charge producer releases a regiment vanilla re-ordered to behavior 2", ProducerReleasesVanillaReorder),
            ("charge producer disarms a charge courier that lands after the revert", ProducerLateChargeDeliveryIsDisarmed),
            ("charge producer config off releases its own arming", ProducerConfigOffReleases),
            ("charge producer plan does not allocate after warm-up", ProducerPlanAllocatesNothingAfterWarmup),
            ("charge executor leave-charge-mode is not a duplicate while in mode 3", LeaveChargeModeIsNotADuplicateInChargeMode),
            ("charge doctrine has no executor recent-order window on combat behavior", ChargeHasNoExecutorWindow),
            ("charge DenialPrecedenceOverCharge through the arbiter", DenialPrecedenceOverChargeArbiter),
        });
    }

    // A leading AI infantry regiment of an assault-stance group, moving at movementmode 0,
    // facing a visible, weaker enemyinrangereg[0] inside the vanilla radius.
    private static ChargeInput Ready()
    {
        return new ChargeInput
        {
            ConfigOn = true,
            PlayerOwned = false,
            IsInfantryRegiment = true,
            GroupAiStance = ChargeDoctrine.AssaultStance,
            TargetVisible = true,
            TargetIsFirstInRange = true,
            DistanceToTarget = 80f,
            VanillaMaxChargeRadius = 120f,
            OwnMorale = 0.8f,
            TargetMorale = 0.5f,
            OwnFatigue = 0.2f,
            TargetDisordered = false,
            TargetFlanked = false,
            LaneBlockedByFriendly = false,
            Moving = true,
            MovementMode = ChargeDoctrine.NormalMovementMode,
            ChargeResolved = false,
            WhiskeyArmed = false,
            IsLeading = true,
            WhiskeyArmedPendingDisarm = false,
            SecondsSinceLastAiChargeRefusal = double.PositiveInfinity,
        };
    }

    // The same regiment after the executor issued the doctrine's behavior 3 (producer memory).
    private static ChargeInput Armed(int movementMode)
    {
        var input = Ready();
        input.WhiskeyArmed = true;
        input.MovementMode = movementMode;
        return input;
    }

    private static ChargeAction Decide(ChargeInput input) => ChargeDoctrine.Decide(in input);

    // A doctrine revert leaves charge mode 3 in the same action (Disarm); elsewhere it only
    // writes behavior 2 (Revert).
    private static ChargeAction RevertAt(int movementMode) =>
        movementMode == ChargeDoctrine.ArmedMovementMode ? ChargeAction.Disarm : ChargeAction.Revert;

    private static void ChargeFiresOnWeakerVisibleTargetInRadius()
    {
        AssertEqual(ChargeAction.Charge, Decide(Ready()), "ready regiment charges");
    }

    private static void ChargeFiresOnDisorderedOrFlankedTarget()
    {
        var even = Ready();
        even.TargetMorale = even.OwnMorale;
        AssertEqual(ChargeAction.None, Decide(even), "even morale, ordered target");

        var disordered = even;
        disordered.TargetDisordered = true;
        AssertEqual(ChargeAction.Charge, Decide(disordered), "disordered target");

        var flanked = even;
        flanked.TargetFlanked = true;
        AssertEqual(ChargeAction.Charge, Decide(flanked), "flanked target");
    }

    private static void NoChargeWithoutMoraleMargin()
    {
        var atMargin = Ready();
        atMargin.TargetMorale = 0.5f;
        atMargin.OwnMorale = atMargin.TargetMorale + ChargeDoctrine.MoraleMargin;
        AssertEqual(ChargeAction.None, Decide(atMargin), "target + margin == own is not below");

        var pastMargin = atMargin;
        pastMargin.TargetMorale = 0.45f;
        AssertEqual(ChargeAction.Charge, Decide(pastMargin), "target + margin < own");
    }

    private static void NoChargeWhenTargetNotFirstInRange()
    {
        var input = Ready();
        input.TargetIsFirstInRange = false;
        AssertEqual(ChargeAction.None, Decide(input), "vanilla charges enemyinrangereg[0] only (122228)");
    }

    private static void NoChargeWhenHidden()
    {
        var input = Ready();
        input.TargetVisible = false;
        AssertEqual(ChargeAction.None, Decide(input), "hidden target");
    }

    private static void NoChargeBeyondVanillaRadius()
    {
        var beyond = Ready();
        beyond.DistanceToTarget = 121f;
        AssertEqual(ChargeAction.None, Decide(beyond), "beyond radius");

        var boundary = Ready();
        boundary.DistanceToTarget = boundary.VanillaMaxChargeRadius;
        AssertEqual(ChargeAction.None, Decide(boundary), "vanilla compares distance < maxchargeradius strictly (122257)");

        var unknown = Ready();
        unknown.DistanceToTarget = float.NaN;
        AssertEqual(ChargeAction.None, Decide(unknown), "unknown distance");
    }

    private static void NoChargeWhenFatiguedOrShaken()
    {
        var fatigued = Ready();
        fatigued.OwnFatigue = ChargeDoctrine.OwnFatigueCeiling + 0.01f;
        AssertEqual(ChargeAction.None, Decide(fatigued), "fatigue above ceiling");

        var atCeiling = Ready();
        atCeiling.OwnFatigue = ChargeDoctrine.OwnFatigueCeiling;
        AssertEqual(ChargeAction.Charge, Decide(atCeiling), "fatigue at ceiling still charges");

        var shaken = Ready();
        shaken.OwnMorale = ChargeDoctrine.OwnMoraleFloor - 0.01f;
        shaken.TargetMorale = 0.1f;
        AssertEqual(ChargeAction.None, Decide(shaken), "morale below floor");

        var atFloor = Ready();
        atFloor.OwnMorale = ChargeDoctrine.OwnMoraleFloor;
        atFloor.TargetMorale = 0.1f;
        AssertEqual(ChargeAction.Charge, Decide(atFloor), "morale at floor still charges");
    }

    private static void NoChargeWhenLaneBlocked()
    {
        var input = Ready();
        input.LaneBlockedByFriendly = true;
        AssertEqual(ChargeAction.None, Decide(input), "friendly in the lane");
    }

    private static void NoChargeWhenStationary()
    {
        var input = Ready();
        input.Moving = false;
        AssertEqual(ChargeAction.None, Decide(input), "vanilla arms only with regimentpaths > 0 (122214)");
    }

    // After vanilla refuses on odds (122290 SetMovementMode(), 122292 lastaichargetime stamp)
    // behavior 3 would re-arm at the 122214 gate on the next frame; vanilla's own renew check
    // (timetorenewaichargecheck at 4917) sits in dead code. The doctrine waits on its own clock.
    private static void NoRearmInsideRefusalBackoff()
    {
        AssertEqual(120.0, ChargeDoctrine.RearmAfterRefusalBattleSeconds, "backoff constant");

        var justRefused = Ready();
        justRefused.SecondsSinceLastAiChargeRefusal = 0.0;
        AssertEqual(ChargeAction.None, Decide(justRefused), "just refused");

        var inside = Ready();
        inside.SecondsSinceLastAiChargeRefusal = ChargeDoctrine.RearmAfterRefusalBattleSeconds - 0.1;
        AssertEqual(ChargeAction.None, Decide(inside), "inside the backoff");

        var elapsed = Ready();
        elapsed.SecondsSinceLastAiChargeRefusal = ChargeDoctrine.RearmAfterRefusalBattleSeconds;
        AssertEqual(ChargeAction.Charge, Decide(elapsed), "backoff elapsed");

        var never = Ready();
        never.SecondsSinceLastAiChargeRefusal = double.PositiveInfinity;
        AssertEqual(ChargeAction.Charge, Decide(never), "never refused");

        var unknown = Ready();
        unknown.SecondsSinceLastAiChargeRefusal = double.NaN;
        AssertEqual(ChargeAction.None, Decide(unknown), "unknown refusal age fails closed");

        // Vanilla's renew interval (timetorenewaichargecheck) above the floor lengthens it.
        var renew = Ready();
        renew.RefusalBackoffSeconds = 1800.0;
        renew.SecondsSinceLastAiChargeRefusal = 1799.0;
        AssertEqual(ChargeAction.None, Decide(renew), "inside vanilla's renew interval");
        renew.SecondsSinceLastAiChargeRefusal = 1800.0;
        AssertEqual(ChargeAction.Charge, Decide(renew), "renew interval elapsed");
        renew.RefusalBackoffSeconds = 30.0;
        renew.SecondsSinceLastAiChargeRefusal = 60.0;
        AssertEqual(ChargeAction.None, Decide(renew), "a shorter renew keeps the 120 s floor");
    }

    private static void NoChargeOnPlayerOwned()
    {
        var input = Ready();
        input.PlayerOwned = true;
        AssertEqual(ChargeAction.None, Decide(input), "player-owned never charged");

        // The player UI sets combat behavior 3 on its own units (203769); never undo it.
        var playerArmed = Armed(ChargeDoctrine.ArmedMovementMode);
        playerArmed.PlayerOwned = true;
        playerArmed.GroupAiStance = 1;
        playerArmed.ChargeResolved = true;
        AssertEqual(ChargeAction.None, Decide(playerArmed), "player-owned behavior 3 never reverted");

        var playerPending = Ready();
        playerPending.PlayerOwned = true;
        playerPending.MovementMode = ChargeDoctrine.ArmedMovementMode;
        playerPending.WhiskeyArmedPendingDisarm = true;
        AssertEqual(ChargeAction.None, Decide(playerPending), "player-owned never disarmed");
    }

    private static void NoChargeOutsideInfantryAssault()
    {
        var cavalry = Ready();
        cavalry.IsInfantryRegiment = false;
        AssertEqual(ChargeAction.None, Decide(cavalry), "cavalry charges through AdjustCavalryBehavior (5036)");

        for (int stance = 0; stance <= 3; stance++)
        {
            var input = Ready();
            input.GroupAiStance = stance;
            AssertEqual(ChargeAction.None, Decide(input), "group stance " + stance);
        }
    }

    private static void NoChargeWhenVanillaChargeModeActive()
    {
        // Not armed by the doctrine but already in charge mode 3 or assault 6 (a contact
        // melee ended 6 -> 3, 122302/122326): vanilla runs its own cycle, the doctrine adds
        // nothing.
        foreach (int mode in new[] { ChargeDoctrine.ArmedMovementMode, ChargeDoctrine.AssaultMovementMode })
        {
            var input = Ready();
            input.MovementMode = mode;
            AssertEqual(ChargeAction.None, Decide(input), "vanilla mode " + mode);
        }
    }

    // MicroAICheckForCharges' cancel (4922-4924: mode 3 and the group out of assault ->
    // SetMovementMode()) applies to every regiment of the AI group, whoever armed it, but is
    // dead behind the 4910 compare. The doctrine restores it; mode 6 waits for 6 -> 3.
    private static void VanillaStanceDropDisarmsUnarmedChargeMode()
    {
        foreach (int stance in new[] { 0, 1, 2, 3 })
        {
            var input = Ready();
            input.MovementMode = ChargeDoctrine.ArmedMovementMode;
            input.GroupAiStance = stance;
            AssertEqual(ChargeAction.Disarm, Decide(input), "unarmed mode 3, group stance " + stance);
        }

        var assault = Ready();
        assault.MovementMode = ChargeDoctrine.AssaultMovementMode;
        assault.GroupAiStance = 2;
        AssertEqual(ChargeAction.None, Decide(assault), "mode 6 resolves first (4924 is mode 3 only)");

        var inAssault = Ready();
        inAssault.MovementMode = ChargeDoctrine.ArmedMovementMode;
        AssertEqual(ChargeAction.None, Decide(inAssault), "stance 4: vanilla's own charge (4919's intent)");

        var noGroup = inAssault;
        noGroup.GroupAiStance = -1;
        AssertEqual(ChargeAction.None, Decide(noGroup), "no AI group, no group stance");

        var off = inAssault;
        off.GroupAiStance = 2;
        off.ConfigOn = false;
        AssertEqual(ChargeAction.None, Decide(off), "off = vanilla (the cancel is dead there)");

        var filtered = off;
        filtered.ConfigOn = true;
        filtered.ExcludedByVanillaFilter = true;
        AssertEqual(ChargeAction.None, Decide(filtered), "permanentlydetached / routed / markedforrout skipped (4922)");

        var player = filtered;
        player.ExcludedByVanillaFilter = false;
        player.PlayerOwned = true;
        AssertEqual(ChargeAction.None, Decide(player), "player-owned never disarmed");

        var filteredCharge = Ready();
        filteredCharge.ExcludedByVanillaFilter = true;
        AssertEqual(ChargeAction.None, Decide(filteredCharge), "vanilla's 4917 filters also bar arming");
    }

    // Exhaustive sweep: every Charge the doctrine emits lands exactly in the vanilla arming
    // gate (combatbehavior 3 && movementmode 0 && regimentpaths > 0, 122214) with the vanilla
    // target and radius (122228, 122257). Decide has no strength input, so vanilla's own
    // odds check against microaitriggerforcharge (122259) stays the final word on the assault.
    private static void ChargeComposesWithVanillaTrigger()
    {
        AssertEqual(3, ChargeDoctrine.ChargeBehavior, "behavior vanilla's gate reads (122214)");
        AssertEqual(2, ChargeDoctrine.RevertBehavior, "cycle-6 value for stances 1-4 (5619-5634)");
        AssertEqual(4, ChargeDoctrine.AssaultStance, "vanilla assault stance (4913)");

        int charges = 0;
        for (int mode = 0; mode <= 6; mode++)
        for (int bits = 0; bits < 64; bits++)
        {
            var input = Ready();
            input.MovementMode = mode;
            input.Moving = (bits & 1) != 0;
            input.TargetIsFirstInRange = (bits & 2) != 0;
            input.TargetVisible = (bits & 4) != 0;
            input.DistanceToTarget = (bits & 8) != 0 ? 80f : 130f;
            input.TargetMorale = (bits & 16) != 0 ? 0.5f : 0.9f;
            input.WhiskeyArmed = (bits & 32) == 0;
            if (Decide(input) != ChargeAction.Charge) continue;
            charges++;
            AssertEqual(ChargeDoctrine.NormalMovementMode, input.MovementMode, "charge only at movementmode 0");
            AssertTrue(input.Moving, "charge only while moving");
            AssertTrue(input.TargetIsFirstInRange && input.TargetVisible, "charge only on the visible enemyinrangereg[0]");
            AssertTrue(input.DistanceToTarget < input.VanillaMaxChargeRadius, "charge only inside the vanilla radius");
            AssertTrue(input.TargetMorale + ChargeDoctrine.MoraleMargin < input.OwnMorale, "Whiskey margin on top");
            AssertFalse(input.WhiskeyArmed, "an armed regiment is never re-armed");
        }
        AssertEqual(1, charges, "one charging cell, unarmed");
    }

    private static void HoldsWhileCourierPending()
    {
        // Under useorderdelays ChangeCombatBehavior stamps combatbehaviorordered at once and
        // queues the real behavior change (90944-90962); movementmode stays 0 until it lands.
        AssertEqual(ChargeAction.None, Decide(Armed(ChargeDoctrine.NormalMovementMode)), "pending courier");
    }

    private static void SustainsThroughVolleyPauseWhenStationary()
    {
        // Volley before the charge: StopRegiment (122273) sets regimentpaths = 0 (132397) and
        // leaves movementmode at 3. The doctrine must not revert that pause.
        var input = Armed(ChargeDoctrine.ArmedMovementMode);
        input.Moving = false;
        AssertEqual(ChargeAction.None, Decide(input), "stationary volley pause");
    }

    private static void HoldsDuringVanillaAssault()
    {
        // Movement mode 6 means vanilla already passed its odds check (122259 -> 122286).
        var input = Armed(ChargeDoctrine.AssaultMovementMode);
        input.TargetIsFirstInRange = false;
        input.LaneBlockedByFriendly = true;
        input.Moving = false;
        input.OwnFatigue = 0.9f;
        AssertEqual(ChargeAction.None, Decide(input), "assault in progress");
    }

    private static void RevertWhenArmedConditionsFail()
    {
        var hidden = Armed(ChargeDoctrine.ArmedMovementMode);
        hidden.TargetVisible = false;
        AssertEqual(ChargeAction.Disarm, Decide(hidden), "armed, target hidden");

        var tired = Armed(ChargeDoctrine.ArmedMovementMode);
        tired.OwnFatigue = 0.9f;
        AssertEqual(ChargeAction.Disarm, Decide(tired), "armed, fatigued");

        var notLeading = Armed(ChargeDoctrine.ArmedMovementMode);
        notLeading.IsLeading = false;
        AssertEqual(ChargeAction.Disarm, Decide(notLeading), "armed, no longer leading");

        var pendingBlocked = Armed(ChargeDoctrine.NormalMovementMode);
        pendingBlocked.LaneBlockedByFriendly = true;
        AssertEqual(ChargeAction.Revert, Decide(pendingBlocked), "pending, lane blocked");

        var retreating = Armed(4);
        AssertEqual(ChargeAction.Revert, Decide(retreating), "armed, movement mode left the charge cycle");
    }

    private static void RevertAfterChargeResolves()
    {
        foreach (int mode in new[] { ChargeDoctrine.NormalMovementMode, ChargeDoctrine.ArmedMovementMode, ChargeDoctrine.AssaultMovementMode })
        {
            var input = Armed(mode);
            input.ChargeResolved = true;
            AssertEqual(RevertAt(mode), Decide(input), "resolved at movement mode " + mode);
        }
    }

    private static void ChargeCycleEndsOnVanillaTransitions()
    {
        // Ended: odds refused 3 -> 0 (122290); assault done 6 -> 3 (122326) or routed target
        // dropped 6 -> 3 (122302); any exit from 3/6 to retreat/other modes.
        AssertTrue(ChargeDoctrine.ChargeCycleEnded(3, 0), "3 -> 0 odds refused");
        AssertTrue(ChargeDoctrine.ChargeCycleEnded(6, 3), "6 -> 3 assault resolved");
        AssertTrue(ChargeDoctrine.ChargeCycleEnded(6, 0), "6 -> 0");
        AssertTrue(ChargeDoctrine.ChargeCycleEnded(3, 4), "3 -> 4 retreat");
        AssertTrue(ChargeDoctrine.ChargeCycleEnded(6, 4), "6 -> 4 retreat");
        // Not ended: arming 0 -> 3 (122214), launch 3 -> 6 (122286), steady states.
        AssertFalse(ChargeDoctrine.ChargeCycleEnded(0, 3), "0 -> 3 armed");
        AssertFalse(ChargeDoctrine.ChargeCycleEnded(3, 6), "3 -> 6 assault launched");
        AssertFalse(ChargeDoctrine.ChargeCycleEnded(3, 3), "3 steady");
        AssertFalse(ChargeDoctrine.ChargeCycleEnded(6, 6), "6 steady");
        AssertFalse(ChargeDoctrine.ChargeCycleEnded(0, 0), "0 steady");
    }

    // Behavior 2 alone leaves movementmode 3, and the 122226 evaluation runs on mode 3
    // regardless of combat behavior. Vanilla's own disarm (4922-4924, movementmode == 3 &&
    // stance != 4 -> SetMovementMode()) is unreachable behind the dead 4910 compare, so a
    // doctrine revert in mode 3 is a Disarm: behavior 2 plus SetMovementMode(0).
    private static void RevertInArmedModeDisarms()
    {
        var input = Armed(ChargeDoctrine.ArmedMovementMode);
        input.GroupAiStance = 2;
        AssertEqual(ChargeAction.Disarm, Decide(input), "armed mode 3 revert");

        var pendingCourier = Armed(ChargeDoctrine.NormalMovementMode);
        pendingCourier.GroupAiStance = 2;
        AssertEqual(ChargeAction.Revert, Decide(pendingCourier), "mode 0 has nothing to disarm");
    }

    // Denial during an assault: Revert writes behavior 2 but never aborts mode 6. Vanilla then
    // ends the assault 6 -> 3 (122302 / 122326) and mode 3 keeps re-evaluating charges
    // (122226 -> 122286) although behavior is 2, so the producer's pending flag drives a
    // Disarm on the first tick back in mode 3.
    private static void DeferredDisarmAfterAssault()
    {
        var assault = Armed(ChargeDoctrine.AssaultMovementMode);
        assault.GroupAiStance = 2;
        var action = Decide(assault);
        AssertEqual(ChargeAction.Revert, action, "denied mid-assault: revert, no abort");
        bool pending = ChargeDoctrine.PendingDisarmAfter(false, action, assault.MovementMode);
        AssertTrue(pending, "pending disarm set by a mode-6 revert");

        var stillAssaulting = Ready();
        stillAssaulting.GroupAiStance = 2;
        stillAssaulting.MovementMode = ChargeDoctrine.AssaultMovementMode;
        stillAssaulting.WhiskeyArmedPendingDisarm = pending;
        action = Decide(stillAssaulting);
        AssertEqual(ChargeAction.None, action, "assault still running: wait");
        pending = ChargeDoctrine.PendingDisarmAfter(pending, action, stillAssaulting.MovementMode);
        AssertTrue(pending, "still pending in mode 6");

        var backToArmed = stillAssaulting;
        backToArmed.MovementMode = ChargeDoctrine.ArmedMovementMode;
        action = Decide(backToArmed);
        AssertEqual(ChargeAction.Disarm, action, "6 -> 3 with the pending flag: disarm");
        pending = ChargeDoctrine.PendingDisarmAfter(pending, action, backToArmed.MovementMode);
        AssertFalse(pending, "cleared by the disarm");

        // Even with stance restored and every charge condition met, the pending disarm wins.
        var conditionsBack = Ready();
        conditionsBack.MovementMode = ChargeDoctrine.ArmedMovementMode;
        conditionsBack.WhiskeyArmedPendingDisarm = true;
        AssertEqual(ChargeAction.Disarm, Decide(conditionsBack), "pending disarm beats a fresh charge");
    }

    private static void PendingDisarmFlagTransitions()
    {
        AssertTrue(ChargeDoctrine.PendingDisarmAfter(false, ChargeAction.Revert, ChargeDoctrine.AssaultMovementMode), "revert in 6 sets");
        AssertFalse(ChargeDoctrine.PendingDisarmAfter(false, ChargeAction.Revert, ChargeDoctrine.NormalMovementMode), "revert in 0 does not");
        AssertFalse(ChargeDoctrine.PendingDisarmAfter(false, ChargeAction.Disarm, ChargeDoctrine.ArmedMovementMode), "disarm in 3 does not");
        AssertFalse(ChargeDoctrine.PendingDisarmAfter(true, ChargeAction.Disarm, ChargeDoctrine.ArmedMovementMode), "disarm clears");
        AssertFalse(ChargeDoctrine.PendingDisarmAfter(true, ChargeAction.None, ChargeDoctrine.NormalMovementMode), "left 3/6 to 0 clears");
        AssertFalse(ChargeDoctrine.PendingDisarmAfter(true, ChargeAction.None, 4), "left 3/6 to retreat clears");
        AssertTrue(ChargeDoctrine.PendingDisarmAfter(true, ChargeAction.None, ChargeDoctrine.AssaultMovementMode), "kept in 6");
        AssertTrue(ChargeDoctrine.PendingDisarmAfter(true, ChargeAction.None, ChargeDoctrine.ArmedMovementMode), "kept in 3 until disarmed");
    }

    private static void RevertWhenStanceDemoted()
    {
        var input = Armed(ChargeDoctrine.ArmedMovementMode);
        input.GroupAiStance = 3;
        AssertEqual(ChargeAction.Disarm, Decide(input), "stance 4 -> 3");
    }

    // Pure half of DenialPrecedenceOverCharge. ChargeDemotion (Safety, Stance channel) wins
    // the group stance, so the next tick sees stance != 4 and Decide reverts even when every
    // other charge condition holds, in every movement mode. The arbiter-fixture version
    // (Safety stance claim + Executor CombatBehavior intent in one tick) belongs to BC6b,
    // which has BC4's arbiter.
    private static void DenialPrecedenceOverCharge()
    {
        foreach (int mode in new[] { ChargeDoctrine.NormalMovementMode, ChargeDoctrine.ArmedMovementMode, ChargeDoctrine.AssaultMovementMode })
        {
            var input = Armed(mode);
            input.GroupAiStance = 2;
            AssertEqual(RevertAt(mode), Decide(input), "denied at movement mode " + mode);
        }

        var unarmed = Ready();
        unarmed.GroupAiStance = 2;
        AssertEqual(ChargeAction.None, Decide(unarmed), "denied before arming: nothing to write");
    }

    private static void OnlyTwoLeadingRegimentsCharge()
    {
        AssertEqual(2, ChargeDoctrine.MaxLeadingRegimentsPerGroup, "leading cap");

        // Per-regiment distance to its own target; infinity = no target.
        float[] distances = { 120f, 60f, float.PositiveInfinity, 90f, 60f };
        bool[] expected = { false, true, false, false, true };
        int charges = 0;
        for (int i = 0; i < distances.Length; i++)
        {
            bool leading = ChargeDoctrine.IsLeading(distances, i);
            AssertEqual(expected[i], leading, "leading " + i);
            var input = Ready();
            input.IsLeading = leading;
            if (Decide(input) == ChargeAction.Charge) charges++;
        }
        AssertEqual(2, charges, "two leading regiments charge");

        AssertTrue(ChargeDoctrine.IsLeading(new[] { 50f }, 0), "single regiment leads");
        AssertFalse(ChargeDoctrine.IsLeading(new[] { float.NaN, 10f }, 0), "unknown distance never leads");
        AssertFalse(ChargeDoctrine.IsLeading(new[] { 10f }, 1), "index out of range");
        AssertFalse(ChargeDoctrine.IsLeading(ReadOnlySpan<float>.Empty, 0), "empty group");

        var notLeading = Ready();
        notLeading.IsLeading = false;
        AssertEqual(ChargeAction.None, Decide(notLeading), "non-leading never charges");
    }

    private static void ConfigOffNeverCharges()
    {
        var input = Ready();
        input.ConfigOn = false;
        AssertEqual(ChargeAction.None, Decide(input), "config off, unarmed");

        // OFF = vanilla: an AI regiment the doctrine armed is handed back.
        var armed = Armed(ChargeDoctrine.ArmedMovementMode);
        armed.ConfigOn = false;
        AssertEqual(ChargeAction.Disarm, Decide(armed), "config off, armed in mode 3");

        var pending = Ready();
        pending.ConfigOn = false;
        pending.MovementMode = ChargeDoctrine.ArmedMovementMode;
        pending.WhiskeyArmedPendingDisarm = true;
        AssertEqual(ChargeAction.Disarm, Decide(pending), "config off, pending disarm after an assault");
    }

    private static void DecideAndLeadingDoNotAllocate()
    {
        var ready = Ready();
        var armed = Armed(ChargeDoctrine.ArmedMovementMode);
        float[] distances = { 120f, 60f, 90f, 30f };
        ChargeDoctrine.Decide(in ready);
        ChargeDoctrine.IsLeading(distances, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int hits = 0;
        for (int i = 0; i < 10000; i++)
        {
            if (ChargeDoctrine.Decide(in ready) == ChargeAction.Charge) hits++;
            if (ChargeDoctrine.Decide(in armed) == ChargeAction.None) hits++;
            if (ChargeDoctrine.IsLeading(distances, i & 3)) hits++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        AssertEqual(25000, hits, "hit count");
        AssertEqual(0L, allocated, "allocated bytes");
    }

    // ------------------------------------------------------------------ BC6b producer

    private const int BrigadeId = 100;
    private const int RegA = 101, RegB = 102, RegC = 103, RegD = 104;
    private const int TargetId = 900;
    private const float Radius = 120f;
    private static long _tick;

    // Brigade HQ (500, -500) in assault; three moving infantry regiments on z = -520 at
    // x 420/500/580, ordered behavior 2, all with the visible enemy 900 at (500, -580) as
    // enemyinrangereg[0]. Parent distance 80 < 120. Regiment distances 100/60/100: B and A
    // lead (A wins the tie with C on the lower index).
    private static BattleSnapshot Line(double hours)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, ++_tick, hours);
        s.SetSliceCount(20);
        s.AddOwn(new UnitRecord
        {
            InstanceId = BrigadeId, UnitTyp = TacticalUnitType.BattleGroupBrigade, X = 500f, Z = -500f,
            AiStance = 4, AiStanceOrdered = 4, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup,
        });
        AddRegiment(s, RegA, 420f);
        AddRegiment(s, RegB, 500f);
        AddRegiment(s, RegC, 580f);
        for (int i = 1; i < s.OwnCount; i++) s.Own[i].ParentIndex = 0;
        s.IndexCommandTree(null);
        s.AddEnemy(new UnitRecord
        {
            InstanceId = TargetId, UnitTyp = TacticalUnitType.Infantry, X = 500f, Z = -580f, Morale = 0.4f,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry,
        });
        s.AddObservation(new EnemyObservation
        {
            InstanceId = TargetId, UnitTyp = TacticalUnitType.Infantry, X = 500f, Z = -580f, Strength = 500f,
            State = ObservationState.Visible, Confidence01 = 1f, LastSeenHours = hours,
        });
        return s;
    }

    private static void AddRegiment(BattleSnapshot s, int id, float x)
    {
        s.AddOwn(new UnitRecord
        {
            InstanceId = id, UnitTyp = TacticalUnitType.Infantry, X = x, Z = -520f, Morale = 0.8f, Fatigue = 0.2f,
            RegimentPaths = 1, MovementMode = 0, CombatBehaviorOrdered = 2, FirstEnemyInRangeId = TargetId,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry,
        });
    }

    private static ref UnitRecord Own(BattleSnapshot s, int id) => ref s.Own[s.IndexOfOwn(id)];

    private static ChargeIntentProducer.Settings On(float renewHours = 0f) => new ChargeIntentProducer.Settings(true, Radius, renewHours);

    private sealed class Sink : IIntentSink
    {
        public readonly BattleOrderIntent[] Items = new BattleOrderIntent[16];
        public int Count;
        public void Submit(in BattleOrderIntent intent) { if (Count < Items.Length) Items[Count++] = intent; }
        public void Clear() { Count = 0; }

        public bool TryFind(int id, out BattleOrderIntent intent)
        {
            for (int i = 0; i < Count; i++)
                if (Items[i].GroupId == id) { intent = Items[i]; return true; }
            intent = default(BattleOrderIntent);
            return false;
        }
    }

    private sealed class Harness
    {
        public readonly ChargeIntentProducer.ChargeMemory Memory = new ChargeIntentProducer.ChargeMemory();
        public readonly OrderExecutor Executor = new OrderExecutor();
        public readonly Sink Sink = new Sink();
        public int Confirmed;

        public void Plan(BattleSnapshot s, ChargeIntentProducer.Settings settings)
        {
            Sink.Clear();
            Confirmed = ChargeIntentProducer.Plan(s, settings, Memory, Executor, Sink);
        }

        // The executor issued every intent of the last Plan at its doctrine time.
        public void ExecuteAll(double hours)
        {
            for (int i = 0; i < Sink.Count; i++)
                Executor.RecordIssued(Sink.Items[i], hours, null, 0, null, 0, 0, 0);
        }
    }

    // Two ticks: the doctrine arms A and B, the executor issues both.
    private static Harness Armed()
    {
        var h = new Harness();
        h.Plan(Line(1.0), On());
        h.ExecuteAll(1.0);
        return h;
    }

    private static void ProducerArmsTwoLeadingRegiments()
    {
        var h = new Harness();
        h.Plan(Line(1.0), On());
        AssertEqual(2, h.Sink.Count, "two leading regiments");
        AssertTrue(h.Sink.TryFind(RegB, out BattleOrderIntent b), "closest regiment charges");
        AssertTrue(h.Sink.TryFind(RegA, out _), "tie with C goes to the lower index");
        AssertFalse(h.Sink.TryFind(RegC, out _), "third regiment does not lead");
        AssertEqual(OrderChannel.CombatBehavior, b.Channel, "combat behavior channel");
        AssertEqual(ChargeDoctrine.ChargeBehavior, b.Value, "behavior 3");
        AssertEqual(IntentPriority.Executor, b.Priority, "executor priority");
        AssertEqual(IntentSource.ChargeDoctrine, b.Source, "source");
        AssertEqual(IntentFlags.PerRegiment, b.Flags, "per regiment only");
        AssertEqual(-1f, b.Heading, "no heading");
        AssertEqual(ChargeIntentProducer.ChargeReason, b.Reason, "static reason");
        AssertEqual(0, h.Confirmed, "nothing confirmed before the executor ran");

        // The brigade itself never gets a behavior write (cycle 6 would re-order it, D10).
        AssertFalse(h.Sink.TryFind(BrigadeId, out _), "never the group");
    }

    // Vanilla's arming gate zeroes the ordered behavior (124770-124774): the regiment reads
    // mode 3 / ordered 0 while armed. A stance demotion must still disarm it.
    private static void ProducerArmedFromMemoryNotOrderedBehavior()
    {
        Harness h = Armed();
        BattleSnapshot s = Line(1.01);
        foreach (int id in new[] { RegA, RegB })
        {
            Own(s, id).MovementMode = ChargeDoctrine.ArmedMovementMode;
            Own(s, id).CombatBehaviorOrdered = 0;
        }
        Own(s, BrigadeId).AiStanceOrdered = 2;
        h.Plan(s, On());
        AssertEqual(2, h.Confirmed, "both charges confirmed by the executor");
        AssertTrue(h.Sink.TryFind(RegB, out BattleOrderIntent b), "armed regiment gets an intent");
        AssertEqual(ChargeDoctrine.RevertBehavior, b.Value, "behavior 2");
        AssertTrue((b.Flags & IntentFlags.LeaveChargeMode) != 0, "disarm leaves charge mode 3");
        AssertEqual(ChargeIntentProducer.DisarmReason, b.Reason, "disarm reason");
        AssertTrue(h.Sink.TryFind(RegA, out _), "both armed regiments disarmed");
        AssertFalse(h.Sink.TryFind(RegC, out _), "never armed, nothing to disarm");

        // Stance kept: an armed regiment in mode 3 holds.
        Harness hold = Armed();
        BattleSnapshot s2 = Line(1.01);
        Own(s2, RegB).MovementMode = ChargeDoctrine.ArmedMovementMode;
        Own(s2, RegB).CombatBehaviorOrdered = 0;
        hold.Plan(s2, On());
        AssertFalse(hold.Sink.TryFind(RegB, out _), "armed in mode 3, conditions hold: no write");

        // A confirmed Revert releases the regiment. In assault stance a later mode 3 without
        // a behavior-3 sighting (a contact melee ending 6 -> 3, 122302/122326) is vanilla's
        // own charge (4919's intent) and is left alone even though this regiment's
        // conditions fail (a friendly in its lane).
        Harness rev = Armed();
        BattleSnapshot r1 = LineWith(RegD, 500f, -550f);
        Own(r1, RegB).CombatBehaviorOrdered = 0; // the gate fired and the regiment is back at 0
        rev.Plan(r1, On());
        AssertTrue(rev.Sink.TryFind(RegB, out BattleOrderIntent laneRevert) && (laneRevert.Flags & IntentFlags.LeaveChargeMode) == 0,
            "lane blocked at mode 0: plain revert");
        rev.ExecuteAll(1.0);
        BattleSnapshot r2 = LineWith(RegD, 500f, -550f);
        Own(r2, RegB).MovementMode = ChargeDoctrine.ArmedMovementMode;
        Own(r2, RegB).CombatBehaviorOrdered = 0;
        rev.Plan(r2, On());
        AssertFalse(rev.Sink.TryFind(RegB, out _), "released: vanilla's own mode 3 in assault is left alone");
    }

    // Under order delays the charge's courier (behavior 3, 90967) can land after the
    // doctrine's Revert stamped ordered 2: the delivery (125401-125403) sets behavior and
    // ordered 3, and the 122214 gate fires on the next slow tick, so the producer rarely
    // sees the 3. The group being out of assault, vanilla's intended cancel disarms it.
    private static void ProducerLateChargeDeliveryIsDisarmed()
    {
        Harness h = Armed();
        BattleSnapshot r1 = Line(1.01);
        Own(r1, BrigadeId).AiStanceOrdered = 2;
        Own(r1, RegB).CombatBehaviorOrdered = 3; // courier still carrying the charge
        h.Plan(r1, On());
        AssertTrue(h.Sink.TryFind(RegB, out BattleOrderIntent revert) && (revert.Flags & IntentFlags.LeaveChargeMode) == 0,
            "demoted while the courier is out: plain revert");
        h.ExecuteAll(1.01);

        BattleSnapshot r2 = Line(1.02);
        Own(r2, BrigadeId).AiStanceOrdered = 2;
        Own(r2, RegB).MovementMode = ChargeDoctrine.ArmedMovementMode; // late 3 consumed by the gate
        Own(r2, RegB).CombatBehaviorOrdered = 0;
        h.Plan(r2, On());
        AssertTrue(h.Sink.TryFind(RegB, out BattleOrderIntent disarm) && (disarm.Flags & IntentFlags.LeaveChargeMode) != 0,
            "late charge disarmed");

        // A stationary regiment shows the delivered 3 (the gate needs paths): behavior 3 on an
        // AI regiment is only ever the doctrine's, so it is armed again and reverted.
        Harness still = Armed();
        BattleSnapshot s1 = Line(1.01);
        Own(s1, BrigadeId).AiStanceOrdered = 2;
        Own(s1, RegB).CombatBehaviorOrdered = 3;
        still.Plan(s1, On());
        still.ExecuteAll(1.01);
        BattleSnapshot s2 = Line(1.02);
        Own(s2, BrigadeId).AiStanceOrdered = 2;
        Own(s2, RegB).CombatBehaviorOrdered = 3;
        Own(s2, RegB).RegimentPaths = 0;
        still.Plan(s2, On());
        AssertTrue(still.Sink.TryFind(RegB, out BattleOrderIntent again) && again.Value == ChargeDoctrine.RevertBehavior,
            "delivered 3 seen: reverted again");
    }

    private static void ProducerUnconfirmedChargeIsNotArmed()
    {
        var h = new Harness();
        h.Plan(Line(1.0), On());
        // The executor refused (guard, claim, cooldown): no Issued outcome.
        BattleSnapshot s = Line(1.01);
        Own(s, BrigadeId).AiStanceOrdered = 2;
        h.Plan(s, On());
        AssertEqual(0, h.Sink.Count, "nothing armed, nothing to revert");

        // An outcome from another source does not arm either.
        var other = new Harness();
        other.Plan(Line(1.0), On());
        other.Executor.RecordIssued(new BattleOrderIntent { GroupId = RegB, Channel = OrderChannel.CombatBehavior, Value = 1, Source = IntentSource.FireControl },
            1.0, null, 0, null, 0, 0, 0);
        other.Plan(s, On());
        AssertEqual(0, other.Confirmed, "another source's Issued confirms nothing");
        AssertFalse(other.Sink.TryFind(RegB, out _), "fire control's write is not the doctrine's arming");

        // A stale doctrine outcome (a Revert issued before this Charge was submitted) is no
        // confirmation either.
        var stale = new Harness();
        stale.Executor.RecordIssued(ChargeIntentProducer.Intent(RegB, ChargeAction.Revert), 0.9, null, 0, null, 0, 0, 0);
        stale.Plan(Line(1.0), On());
        stale.Plan(s, On());
        AssertEqual(0, stale.Confirmed, "outcome older than the submission");
        AssertFalse(stale.Sink.TryFind(RegB, out _), "the refused charge is not armed");
    }

    private static void ProducerFreshStampIsNotARefusal()
    {
        var h = new Harness();
        BattleSnapshot s = Line(1.0);
        Own(s, RegB).LastAiChargeTime = 0.75f; // a refusal from before the producer ever saw it
        h.Plan(s, On());
        AssertTrue(h.Sink.TryFind(RegB, out _), "first-seen stamp is the baseline");
        AssertTrue(h.Sink.TryFind(RegA, out _), "fresh unit (0) charges");
    }

    // Refusal: 122290 SetMovementMode() + 122292 stamp; the gate had already zeroed the
    // ordered behavior. The doctrine reverts to 2 and waits max(120 s, renew) on its clock.
    private static void ProducerRefusalBacksOffWithVanillaRenew()
    {
        AssertEqual(1800.0, ChargeDoctrine.RefusalBackoffSeconds(0.5f), "shipped renew 0.5 h");
        AssertEqual(ChargeDoctrine.RearmAfterRefusalBattleSeconds, ChargeDoctrine.RefusalBackoffSeconds(0f), "floor 120 s");
        AssertEqual(ChargeDoctrine.RearmAfterRefusalBattleSeconds, ChargeDoctrine.RefusalBackoffSeconds(float.NaN), "unknown renew keeps the floor");

        foreach (float renewHours in new[] { 0f, 0.5f })
        {
            double backoff = ChargeDoctrine.RefusalBackoffSeconds(renewHours);
            var h = new Harness();
            h.Plan(Line(1.0), On(renewHours));
            h.ExecuteAll(1.0);

            double refusedAt = 1.01;
            BattleSnapshot s = Line(refusedAt);
            Own(s, RegB).CombatBehaviorOrdered = 0;
            Own(s, RegB).LastAiChargeTime = 1.005f;
            h.Plan(s, On(renewHours));
            AssertTrue(h.Sink.TryFind(RegB, out BattleOrderIntent revert), "refusal reverts");
            AssertEqual(ChargeDoctrine.RevertBehavior, revert.Value, "back to behavior 2 (renew " + renewHours + ")");
            AssertFalse((revert.Flags & IntentFlags.LeaveChargeMode) != 0, "mode 0: nothing to disarm");
            h.ExecuteAll(refusedAt);

            double inside = refusedAt + (backoff - 1.0) / 3600.0;
            BattleSnapshot s2 = Line(inside);
            Own(s2, RegB).LastAiChargeTime = 1.005f;
            h.Plan(s2, On(renewHours));
            AssertFalse(h.Sink.TryFind(RegB, out _), "inside the back-off (renew " + renewHours + ")");

            BattleSnapshot s3 = Line(refusedAt + (backoff + 1.0) / 3600.0);
            Own(s3, RegB).LastAiChargeTime = 1.005f;
            h.Plan(s3, On(renewHours));
            AssertTrue(h.Sink.TryFind(RegB, out BattleOrderIntent again) && again.Value == ChargeDoctrine.ChargeBehavior,
                "re-armed after the back-off (renew " + renewHours + ")");
        }
    }

    private static void ProducerDistanceFromParentGroupStrict()
    {
        var h = new Harness();
        BattleSnapshot s = Line(1.0);
        // Regiments stay 60-100 m from the target; the HQ moves to exactly the radius.
        Own(s, BrigadeId).Z = -580f + Radius;
        h.Plan(s, On());
        AssertEqual(0, h.Sink.Count, "parent at the radius: vanilla's < fails (122257)");

        BattleSnapshot inside = Line(1.0);
        Own(inside, BrigadeId).Z = -580f + Radius - 0.5f;
        h = new Harness();
        h.Plan(inside, On());
        AssertEqual(2, h.Sink.Count, "parent just inside");

        BattleSnapshot orphan = Line(1.0);
        Own(orphan, RegB).ParentIndex = -1;
        h = new Harness();
        h.Plan(orphan, On());
        AssertFalse(h.Sink.TryFind(RegB, out _), "no command parent, no charge");
    }

    private static void ProducerGroupStanceNeedsOrderedAndCurrent()
    {
        UnitRecord g = new UnitRecord { AiStance = 4, AiStanceOrdered = 4 };
        AssertEqual(4, ChargeIntentProducer.GroupStance(g), "both 4");
        g.AiStance = 3;
        AssertEqual(3, ChargeIntentProducer.GroupStance(g), "assault ordered, courier not arrived: not yet 4");
        g.AiStance = 4;
        g.AiStanceOrdered = 2;
        AssertEqual(2, ChargeIntentProducer.GroupStance(g), "demotion ordered: reverts at once");

        var h = new Harness();
        BattleSnapshot s = Line(1.0);
        Own(s, BrigadeId).AiStance = 3;
        h.Plan(s, On());
        AssertEqual(0, h.Sink.Count, "pending assault order arms nothing");
    }

    private static void ProducerLaneBlockedOnlyBetween()
    {
        // A cavalry regiment (no target, never leads) straight between B and the target.
        BattleSnapshot s = LineWith(RegD, 500f, -550f);
        var h = new Harness();
        h.Plan(s, On());
        AssertFalse(h.Sink.TryFind(RegB, out _), "friendly in B's lane");

        // Behind B (projection <= 0): not in the lane.
        BattleSnapshot behind = LineWith(RegD, 500f, -480f);
        h = new Harness();
        h.Plan(behind, On());
        AssertTrue(h.Sink.TryFind(RegB, out _), "friendly behind is not in the lane");

        // Beyond the target (projection >= 1): not in the lane.
        BattleSnapshot beyond = LineWith(RegD, 500f, -620f);
        h = new Harness();
        h.Plan(beyond, On());
        AssertTrue(h.Sink.TryFind(RegB, out _), "friendly beyond the target is not in the lane");
    }

    private static void ProducerLaneIgnoresRoutedAndInactive()
    {
        foreach (UnitFlags drop in new[] { UnitFlags.Active, UnitFlags.Alive })
        {
            BattleSnapshot s = LineWith(RegD, 500f, -550f);
            Own(s, RegD).Flags &= ~drop;
            var h = new Harness();
            h.Plan(s, On());
            AssertTrue(h.Sink.TryFind(RegB, out _), "friendly without " + drop + " does not block");
        }
        BattleSnapshot routed = LineWith(RegD, 500f, -550f);
        Own(routed, RegD).Flags |= UnitFlags.Routed;
        var r = new Harness();
        r.Plan(routed, On());
        AssertTrue(r.Sink.TryFind(RegB, out _), "routed friendly does not block");
    }

    // MicroAICheckForCharges' unit filter (4917 arm, 4922 cancel): not permanentlydetached,
    // not routed, not markedforrout, group not feuding.
    private static void ProducerVanillaUnitFilter()
    {
        foreach (UnitFlags flag in new[] { UnitFlags.PermanentlyDetached, UnitFlags.Routed, UnitFlags.MarkedForRout, UnitFlags.Feud })
        {
            BattleSnapshot s = Line(1.0);
            Own(s, RegB).Flags |= flag;
            var h = new Harness();
            h.Plan(s, On());
            AssertFalse(h.Sink.TryFind(RegB, out _), flag + ": never armed");

            BattleSnapshot cancel = Line(1.0);
            Own(cancel, BrigadeId).AiStanceOrdered = 2;
            Own(cancel, RegB).MovementMode = ChargeDoctrine.ArmedMovementMode;
            Own(cancel, RegB).CombatBehaviorOrdered = 0;
            Own(cancel, RegB).Flags |= flag;
            h = new Harness();
            h.Plan(cancel, On());
            AssertFalse(h.Sink.TryFind(RegB, out _), flag + ": no stance-drop cancel");
        }
    }

    // battleprefs "No micro ai charge if unit is outflanked or receiving flanking fire"
    // (GamePrefs.nomicroaichargeifunitisoutflanked, 54784; shipped true, never read by vanilla).
    private static void ProducerHonoursNoChargeIfOutflanked()
    {
        BattleSnapshot s = Line(1.0);
        Own(s, RegB).Outflanked = 2;
        var h = new Harness();
        h.Plan(s, new ChargeIntentProducer.Settings(true, Radius, 0f, noChargeIfOutflanked: true));
        AssertFalse(h.Sink.TryFind(RegB, out _), "pref set: an outflanked regiment does not charge");
        AssertTrue(h.Sink.TryFind(RegA, out _), "a regiment not outflanked still charges");

        h = new Harness();
        h.Plan(s, new ChargeIntentProducer.Settings(true, Radius, 0f, noChargeIfOutflanked: false));
        AssertTrue(h.Sink.TryFind(RegB, out _), "pref clear: outflanked does not matter");

        // "... or receiving flanking fire": own firefromflank (111492, 123208-123264).
        BattleSnapshot fire = Line(1.0);
        Own(fire, RegB).FireFromFlank = 1;
        h = new Harness();
        h.Plan(fire, new ChargeIntentProducer.Settings(true, Radius, 0f, noChargeIfOutflanked: true));
        AssertFalse(h.Sink.TryFind(RegB, out _), "pref set: flanking fire bars the charge");
        h = new Harness();
        h.Plan(fire, new ChargeIntentProducer.Settings(true, Radius, 0f, noChargeIfOutflanked: false));
        AssertTrue(h.Sink.TryFind(RegB, out _), "pref clear: flanking fire does not matter");
    }

    // Feud on the brigade only (ActivateFeud sets the group, 116613): its regiments are
    // excluded once the builder's propagation ran (PlayerOwnership.ApplyFeudGroups).
    private static void ProducerExcludesFeudingBrigadeRegiments()
    {
        BattleSnapshot s = Line(1.0);
        Own(s, BrigadeId).Flags |= UnitFlags.Feud;
        bool[] scratch = null;
        PlayerOwnership.ApplyFeudGroups(s, ref scratch);
        var h = new Harness();
        h.Plan(s, On());
        AssertEqual(0, h.Sink.Count, "no regiment of a feuding brigade is armed");

        BattleSnapshot cancel = Line(1.0);
        Own(cancel, BrigadeId).Flags |= UnitFlags.Feud;
        Own(cancel, BrigadeId).AiStanceOrdered = 2;
        Own(cancel, RegB).MovementMode = ChargeDoctrine.ArmedMovementMode;
        PlayerOwnership.ApplyFeudGroups(cancel, ref scratch);
        h = new Harness();
        h.Plan(cancel, On());
        AssertEqual(0, h.Sink.Count, "nor cancelled: the feud instance owns them");
    }

    // BC8: the producer computes vanilla's own odds (122229-122231) from its regiment and the
    // visible target (strength / morale / fatigue from the record, experience as the stars of
    // the observation) and arms only above microaitriggerforcharge (122259).
    private static void ProducerArmsOnlyWhenVanillaOddsPass()
    {
        var odds = new ChargeOddsRules { Trigger = 3f, WeightStrength = 1f, WeightExperience = 0.7f, WeightMorale = 1.5f, WeightFatigue = 0.6f };
        var settings = new ChargeIntentProducer.Settings(true, Radius, 0f, chargeOdds: odds);

        BattleSnapshot strongTarget = Line(1.0);
        foreach (int id in new[] { RegA, RegB, RegC })
        {
            Own(strongTarget, id).Strength = 400;
            Own(strongTarget, id).Experience = 0.8f;
        }
        var h = new Harness();
        h.Plan(strongTarget, settings);
        AssertEqual(0, h.Sink.Count, "500 men at the target: odds ~1.6, vanilla would refuse, nothing armed");

        BattleSnapshot weakTarget = Line(1.0);
        foreach (int id in new[] { RegA, RegB, RegC })
        {
            Own(weakTarget, id).Strength = 400;
            Own(weakTarget, id).Experience = 0.8f;
        }
        weakTarget.Observations[0].Strength = 150f;
        weakTarget.Observations[0].Experience = 0.1f;
        h = new Harness();
        h.Plan(weakTarget, settings);
        AssertEqual(2, h.Sink.Count, "150 green men at the target: odds ~5.3, the two leaders arm");

        h = new Harness();
        h.Plan(strongTarget, new ChargeIntentProducer.Settings(true, Radius, 0f));
        AssertEqual(2, h.Sink.Count, "no odds rules (weighting off): no pre-filter");
    }

    // Fog (verify M12 / M24): the odds use the target observation's star value, never the
    // enemy record's true experience nor the regiment's own. Own 400 at experience 0.8 against
    // 200 men: stars 0.1 → odds ~3.71 (arm); experience 1.0 → ~2.33; own 0.8 → ~2.54.
    private static void ProducerOddsReadTheTargetsStars()
    {
        var odds = new ChargeOddsRules { Trigger = 3f, WeightStrength = 1f, WeightExperience = 0.7f, WeightMorale = 1.5f, WeightFatigue = 0.6f };
        var settings = new ChargeIntentProducer.Settings(true, Radius, 0f, chargeOdds: odds);
        BattleSnapshot Target(float starExperience, float trueExperience)
        {
            BattleSnapshot s = Line(1.0);
            foreach (int id in new[] { RegA, RegB, RegC })
            {
                Own(s, id).Strength = 400;
                Own(s, id).Experience = 0.8f;
            }
            s.Observations[0].Strength = 200f;
            s.Observations[0].Experience = starExperience;
            s.EnemyRecords[0].Experience = trueExperience;
            return s;
        }

        var h = new Harness();
        h.Plan(Target(starExperience: 0.1f, trueExperience: 1f), settings);
        AssertEqual(2, h.Sink.Count, "green stars arm although the true experience is high");
        h = new Harness();
        h.Plan(Target(starExperience: 1f, trueExperience: 0.1f), settings);
        AssertEqual(0, h.Sink.Count, "veteran stars refuse although the true experience is low");
    }

    private static BattleSnapshot LineWith(int id, float x, float z)
    {
        BattleSnapshot s = Line(1.0);
        int count = s.OwnCount;
        s.AddOwn(new UnitRecord
        {
            InstanceId = id, UnitTyp = TacticalUnitType.Cavalry, X = x, Z = z, Morale = 0.8f,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsCavalry,
        });
        for (int i = 1; i <= count; i++) s.Own[i].ParentIndex = 0;
        s.IndexCommandTree(null);
        return s;
    }

    private static void ProducerTargetStateFromVisibleRecord()
    {
        BattleSnapshot even = Line(1.0);
        even.EnemyRecords[0].Morale = 0.7f; // 0.7 + 0.15 >= 0.8
        var h = new Harness();
        h.Plan(even, On());
        AssertEqual(0, h.Sink.Count, "no morale margin, ordered target");

        BattleSnapshot flanked = Line(1.0);
        flanked.EnemyRecords[0].Morale = 0.7f;
        flanked.EnemyRecords[0].Outflanked = 1;
        h = new Harness();
        h.Plan(flanked, On());
        AssertEqual(2, h.Sink.Count, "visible target outflanked by this side (UpdateFlanking 123149)");

        foreach (int mode in new[] { 2, 4 })
        {
            BattleSnapshot falling = Line(1.0);
            falling.EnemyRecords[0].Morale = 0.7f;
            falling.EnemyRecords[0].MovementMode = mode;
            h = new Harness();
            h.Plan(falling, On());
            AssertEqual(2, h.Sink.Count, "target disordered, movement mode " + mode);
        }

        // Remembered, not visible: nothing is read, nothing charges.
        BattleSnapshot hidden = Line(1.0);
        hidden.Observations[0].State = ObservationState.Remembered;
        h = new Harness();
        h.Plan(hidden, On());
        AssertEqual(0, h.Sink.Count, "remembered target");

        // First in range is some other unit: the doctrine never substitutes a target.
        BattleSnapshot other = Line(1.0);
        Own(other, RegB).FirstEnemyInRangeId = 901;
        h = new Harness();
        h.Plan(other, On());
        AssertFalse(h.Sink.TryFind(RegB, out _), "first in range unseen");
    }

    private static void ProducerNeverTouchesPlayerOwned()
    {
        BattleSnapshot s = Line(1.0);
        for (int i = 1; i < s.OwnCount; i++) s.Own[i].Flags |= UnitFlags.PlayerOwned;
        var h = new Harness();
        h.Plan(s, On());
        AssertEqual(0, h.Sink.Count, "player-owned never charged");

        Harness armed = Armed();
        BattleSnapshot later = Line(1.01);
        Own(later, RegB).Flags |= UnitFlags.PlayerOwned;
        Own(later, RegB).MovementMode = ChargeDoctrine.ArmedMovementMode;
        Own(later, BrigadeId).AiStanceOrdered = 2;
        armed.Plan(later, On());
        AssertFalse(armed.Sink.TryFind(RegB, out _), "player-owned never reverted or disarmed");
    }

    private static void ProducerDeferredDisarmAfterAssault()
    {
        Harness h = Armed();
        BattleSnapshot assault = Line(1.01);
        Own(assault, RegB).MovementMode = ChargeDoctrine.AssaultMovementMode;
        Own(assault, RegB).CombatBehaviorOrdered = 0;
        Own(assault, RegB).RegimentPaths = 1;
        Own(assault, BrigadeId).AiStanceOrdered = 2;
        h.Plan(assault, On());
        AssertTrue(h.Sink.TryFind(RegB, out BattleOrderIntent revert), "denied mid-assault");
        AssertEqual(ChargeDoctrine.RevertBehavior, revert.Value, "behavior 2");
        AssertFalse((revert.Flags & IntentFlags.LeaveChargeMode) != 0, "never aborts mode 6");
        h.ExecuteAll(1.01);

        BattleSnapshot still = Line(1.02);
        Own(still, RegB).MovementMode = ChargeDoctrine.AssaultMovementMode;
        Own(still, RegB).CombatBehaviorOrdered = 2;
        Own(still, BrigadeId).AiStanceOrdered = 2;
        h.Plan(still, On());
        AssertFalse(h.Sink.TryFind(RegB, out _), "assault still running: wait");

        BattleSnapshot back = Line(1.03);
        Own(back, RegB).MovementMode = ChargeDoctrine.ArmedMovementMode; // 122302 / 122326
        Own(back, RegB).CombatBehaviorOrdered = 0;
        Own(back, BrigadeId).AiStanceOrdered = 2;
        h.Plan(back, On());
        AssertTrue(h.Sink.TryFind(RegB, out BattleOrderIntent disarm), "6 -> 3: disarm");
        AssertTrue((disarm.Flags & IntentFlags.LeaveChargeMode) != 0, "leaves mode 3");
        h.ExecuteAll(1.03);

        BattleSnapshot done = Line(1.04);
        Own(done, RegB).CombatBehaviorOrdered = 2;
        Own(done, BrigadeId).AiStanceOrdered = 2;
        h.Plan(done, On());
        AssertFalse(h.Sink.TryFind(RegB, out _), "disarmed: nothing left");
    }

    // Cycle 6 re-cascades behavior 2 after a stance change (SetAIStance zeroes the group's
    // ordered behavior, 122099-122120 -> 5631-5634): the regiment is back at mode 0 / 2.
    private static void ProducerReleasesVanillaReorder()
    {
        Harness h = Armed();
        BattleSnapshot s = Line(1.01);
        Own(s, RegB).CombatBehaviorOrdered = 2;
        Own(s, BrigadeId).AiStanceOrdered = 2; // conditions fail: Revert would write 2 again
        h.Plan(s, On());
        AssertFalse(h.Sink.TryFind(RegB, out _), "behavior 2 already ordered: no write");
        AssertTrue(h.Memory.ById.TryGetValue(RegB, out ChargeIntentProducer.RegimentMemory m) && !m.Armed, "released");

        // Cascade while every condition still holds: released, then armed afresh.
        Harness again = Armed();
        BattleSnapshot cascade = Line(1.01);
        Own(cascade, RegB).CombatBehaviorOrdered = 2;
        again.Plan(cascade, On());
        AssertFalse(again.Sink.TryFind(RegB, out _), "no write on the release tick");
        again.Plan(Line(1.02), On());
        AssertTrue(again.Sink.TryFind(RegB, out BattleOrderIntent charge) && charge.Value == ChargeDoctrine.ChargeBehavior, "re-armed");
    }

    private static void ProducerConfigOffReleases()
    {
        var off = new ChargeIntentProducer.Settings(false, Radius, 0f);
        var h = new Harness();
        h.Plan(Line(1.0), off);
        AssertEqual(0, h.Sink.Count, "off: nothing armed");

        Harness armed = Armed();
        BattleSnapshot s = Line(1.01);
        Own(s, RegB).MovementMode = ChargeDoctrine.ArmedMovementMode;
        Own(s, RegB).CombatBehaviorOrdered = 0;
        armed.Plan(s, off);
        AssertTrue(armed.Sink.TryFind(RegB, out BattleOrderIntent disarm) && (disarm.Flags & IntentFlags.LeaveChargeMode) != 0,
            "off hands an armed regiment back");
    }

    private static void ProducerPlanAllocatesNothingAfterWarmup()
    {
        var h = new Harness();
        BattleSnapshot a = Line(1.0);
        BattleSnapshot b = Line(1.01);
        Own(b, RegB).MovementMode = ChargeDoctrine.ArmedMovementMode;
        Own(b, RegB).CombatBehaviorOrdered = 0;
        var settings = On(0.5f);
        h.Plan(a, settings);
        h.ExecuteAll(1.0);
        h.Plan(b, settings);
        h.Plan(a, settings);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            h.Sink.Clear();
            ChargeIntentProducer.Plan((i & 1) == 0 ? b : a, settings, h.Memory, h.Executor, h.Sink);
        }
        AssertEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before, "allocated bytes");
    }

    private sealed class AllowGuard : IOrderGuard
    {
        public bool CanWrite(in UnitRecord unit, out SkipReason reason)
        {
            reason = SkipReason.None;
            return true;
        }
    }

    private static void LeaveChargeModeIsNotADuplicateInChargeMode()
    {
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        BattleSnapshot s = Line(1.0);
        BattleOrderIntent disarm = ChargeIntentProducer.Intent(RegB, ChargeAction.Disarm);
        BattleOrderIntent revert = ChargeIntentProducer.Intent(RegB, ChargeAction.Revert);

        // Ordered 2 already (cycle-6 cascade) but still in charge mode 3.
        Own(s, RegB).MovementMode = ChargeDoctrine.ArmedMovementMode;
        AssertTrue(executor.Plan(s, disarm, 1.0).Execute, "disarm in mode 3 executes");
        AssertEqual(SkipReason.Duplicate, executor.Plan(s, revert, 1.0).Skip, "plain revert with 2 ordered is a duplicate");

        Own(s, RegB).MovementMode = 0;
        AssertEqual(SkipReason.Duplicate, executor.Plan(s, disarm, 1.0).Skip, "mode 0 and 2 ordered: nothing to do");
    }

    private static void ChargeHasNoExecutorWindow()
    {
        AssertEqual(0f, OrderExecutor.RecentOrderBattleSecondsFor(OrderChannel.CombatBehavior, IntentSource.ChargeDoctrine), "stateful charge/revert");
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        BattleSnapshot s = Line(1.0);
        BattleOrderIntent charge = ChargeIntentProducer.Intent(RegB, ChargeAction.Charge);
        executor.RecordIssued(charge, 1.0, null, 0, null, 0, 0, 0);
        Own(s, RegB).CombatBehaviorOrdered = 0;
        AssertTrue(executor.Plan(s, ChargeIntentProducer.Intent(RegB, ChargeAction.Revert), 1.0001).Execute, "revert right after the charge");
    }

    // Plan's arbiter fixture: tick 1 the posture executor holds the brigade in assault,
    // the charge demotion (Safety, Stance) and the charge doctrine (Executor,
    // CombatBehavior per regiment) meet in one arbiter tick; tick 2 the group is out of
    // assault and the doctrine reverts what it armed.
    private static void DenialPrecedenceOverChargeArbiter()
    {
        var arbiter = new OrderArbiter();
        var h = new Harness();
        BattleSnapshot s = Line(1.0);
        arbiter.BeginTick();
        arbiter.Submit(new BattleOrderIntent { GroupId = BrigadeId, Channel = OrderChannel.Stance, Value = 4, Heading = -1f,
            Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "test" });
        AssertTrue(TacticalChargeDenial.TryDemotionIntent(Own(s, BrigadeId), DemoteTo(2), out BattleOrderIntent demotion), "demotion intent");
        arbiter.Submit(demotion);
        ChargeIntentProducer.Plan(s, On(), h.Memory, h.Executor, arbiter);

        var winners = new BattleOrderIntent[8];
        int count = arbiter.Resolve(winners);
        AssertEqual(3, count, "one stance winner plus two regiment behaviors");
        AssertEqual(OrderChannel.Stance, winners[0].Channel, "stance resolves first");
        AssertEqual(IntentSource.ChargeDemotion, winners[0].Source, "Safety demotion beats the executor's assault");
        AssertEqual(2, winners[0].Value, "demoted to 2");
        for (int w = 1; w < count; w++)
        {
            AssertEqual(OrderChannel.CombatBehavior, winners[w].Channel, "regiment behavior channel");
            AssertEqual(IntentSource.ChargeDoctrine, winners[w].Source, "the doctrine's own channel, no conflict");
            h.Executor.RecordIssued(winners[w], 1.0, null, 0, null, 0, 0, 0);
        }

        // Tick 2: the demotion landed (ordered 2). One regiment is still waiting for the
        // courier (mode 0, ordered 3), one had armed (mode 3, ordered 0).
        BattleSnapshot next = Line(1.01);
        Own(next, BrigadeId).AiStanceOrdered = 2;
        Own(next, RegA).CombatBehaviorOrdered = 3;
        Own(next, RegB).MovementMode = ChargeDoctrine.ArmedMovementMode;
        Own(next, RegB).CombatBehaviorOrdered = 0;
        arbiter.BeginTick();
        ChargeIntentProducer.Plan(next, On(), h.Memory, h.Executor, arbiter);
        count = arbiter.Resolve(winners);
        AssertEqual(2, count, "both armed regiments revert");
        for (int w = 0; w < count; w++)
        {
            AssertEqual(ChargeDoctrine.RevertBehavior, winners[w].Value, "behavior 2");
            bool disarm = (winners[w].Flags & IntentFlags.LeaveChargeMode) != 0;
            AssertEqual(winners[w].GroupId == RegB, disarm, "mode 3 disarms, mode 0 reverts");
        }
    }

    private static TacticalResolvedGroupDecision DemoteTo(int stance)
    {
        return new TacticalResolvedGroupDecision(stance, TacticalDoctrineDecisionKind.Skip, "charge-demote-doctrine", false, false, true, ChargeOverrideAction.DemoteToHold);
    }
}
