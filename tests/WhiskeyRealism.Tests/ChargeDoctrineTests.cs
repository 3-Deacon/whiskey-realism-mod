using System;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// BC6a: pure infantry charge doctrine. Vanilla anchors (Assembly-CSharp build 14703111):
// Regiment.UpdateChargeTarget 122199-122345, MicroAICheckForCharges 4905-4930, micro cycle 6
// 5617-5638, StopRegiment 132316 (regimentpaths = 0 at 132397, movementmode untouched).
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
            ("charge doctrine never touches player-owned units", NoChargeOnPlayerOwned),
            ("charge doctrine is infantry in assault stance only", NoChargeOutsideInfantryAssault),
            ("charge doctrine does not re-arm a vanilla charge mode already running", NoChargeWhenVanillaChargeModeActive),
            ("charge doctrine only arms the vanilla 122214 gate and leaves the 122259 odds check to vanilla", ChargeComposesWithVanillaTrigger),
            ("charge doctrine holds while the courier carries the order", HoldsWhileCourierPending),
            ("charge doctrine sustains through the stationary volley pause", SustainsThroughVolleyPauseWhenStationary),
            ("charge doctrine holds while the vanilla assault runs", HoldsDuringVanillaAssault),
            ("charge doctrine reverts an armed regiment when conditions fail", RevertWhenArmedConditionsFail),
            ("charge doctrine reverts after the charge resolves", RevertAfterChargeResolves),
            ("charge doctrine charge cycle ends on the vanilla mode transitions", ChargeCycleEndsOnVanillaTransitions),
            ("charge doctrine revert disarms movement mode 3 only", RevertDisarmsArmedModeOnly),
            ("charge doctrine reverts when the group stance is demoted", RevertWhenStanceDemoted),
            ("charge doctrine stance denial beats a charge in every movement mode", DenialPrecedenceOverCharge),
            ("charge doctrine arms only the two leading regiments", OnlyTwoLeadingRegimentsCharge),
            ("charge doctrine config off never charges and releases its own arming", ConfigOffNeverCharges),
            ("charge doctrine decide and leading selection do not allocate", DecideAndLeadingDoNotAllocate),
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
            CurrentBehaviorOrdered = ChargeDoctrine.RevertBehavior,
            IsLeading = true,
        };
    }

    // The same regiment after the doctrine armed it (combatbehaviorordered = 3).
    private static ChargeInput Armed(int movementMode)
    {
        var input = Ready();
        input.CurrentBehaviorOrdered = ChargeDoctrine.ChargeBehavior;
        input.MovementMode = movementMode;
        return input;
    }

    private static ChargeAction Decide(ChargeInput input) => ChargeDoctrine.Decide(in input);

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
        // Behavior is not ours (2) but the regiment is already in charge mode 3 or assault 6:
        // vanilla is running its own trigger, so the doctrine adds nothing.
        foreach (int mode in new[] { ChargeDoctrine.ArmedMovementMode, ChargeDoctrine.AssaultMovementMode })
        {
            var input = Ready();
            input.MovementMode = mode;
            AssertEqual(ChargeAction.None, Decide(input), "vanilla mode " + mode);
        }
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
            input.CurrentBehaviorOrdered = (bits & 32) != 0 ? ChargeDoctrine.RevertBehavior : 1;
            if (Decide(input) != ChargeAction.Charge) continue;
            charges++;
            AssertEqual(ChargeDoctrine.NormalMovementMode, input.MovementMode, "charge only at movementmode 0");
            AssertTrue(input.Moving, "charge only while moving");
            AssertTrue(input.TargetIsFirstInRange && input.TargetVisible, "charge only on the visible enemyinrangereg[0]");
            AssertTrue(input.DistanceToTarget < input.VanillaMaxChargeRadius, "charge only inside the vanilla radius");
            AssertTrue(input.TargetMorale + ChargeDoctrine.MoraleMargin < input.OwnMorale, "Whiskey margin on top");
        }
        AssertEqual(2, charges, "one charging cell per non-3 prior behavior");
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
        AssertEqual(ChargeAction.Revert, Decide(hidden), "armed, target hidden");

        var tired = Armed(ChargeDoctrine.ArmedMovementMode);
        tired.OwnFatigue = 0.9f;
        AssertEqual(ChargeAction.Revert, Decide(tired), "armed, fatigued");

        var notLeading = Armed(ChargeDoctrine.ArmedMovementMode);
        notLeading.IsLeading = false;
        AssertEqual(ChargeAction.Revert, Decide(notLeading), "armed, no longer leading");

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
            AssertEqual(ChargeAction.Revert, Decide(input), "resolved at movement mode " + mode);
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

    private static void RevertDisarmsArmedModeOnly()
    {
        // Revert to behavior 2 alone leaves movementmode 3, and the 122226 evaluation runs on
        // mode 3 regardless of combat behavior. Vanilla's own disarm (4922-4924,
        // movementmode == 3 && stance != 4 -> SetMovementMode()) is unreachable behind the
        // dead 4910 compare, so the doctrine's revert disarms mode 3 itself. Mode 6 is left
        // to vanilla's assault resolution, like 4922.
        AssertTrue(ChargeDoctrine.RevertRequiresDisarm(ChargeDoctrine.ArmedMovementMode), "mode 3 disarmed");
        AssertFalse(ChargeDoctrine.RevertRequiresDisarm(ChargeDoctrine.AssaultMovementMode), "mode 6 left to vanilla");
        AssertFalse(ChargeDoctrine.RevertRequiresDisarm(ChargeDoctrine.NormalMovementMode), "mode 0 already disarmed");
        AssertFalse(ChargeDoctrine.RevertRequiresDisarm(4), "retreat untouched");
    }

    private static void RevertWhenStanceDemoted()
    {
        var input = Armed(ChargeDoctrine.ArmedMovementMode);
        input.GroupAiStance = 3;
        AssertEqual(ChargeAction.Revert, Decide(input), "stance 4 -> 3");
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
            AssertEqual(ChargeAction.Revert, Decide(input), "denied at movement mode " + mode);
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
        AssertEqual(ChargeAction.Revert, Decide(armed), "config off, armed");
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
}
