using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Orchestrator;

internal static class TacticalResolvedGroupDecisionTests
{
    public static void Run()
    {
        DefaultIsNoOpSkip();
        LogPass("tactical resolved group decision default is no-op skip");

        ConstructPreservesFields();
        LogPass("tactical resolved group decision construct preserves fields");

        NullReasonNormalizes();
        LogPass("tactical resolved group decision null reason normalizes");

        ChargeOverrideEnumOrdinalsStable();
        LogPass("tactical resolved group decision charge override enum ordinals stable");
    }

    private static void LogPass(string message)
    {
        Console.WriteLine("PASS " + message);
    }

    private static void DefaultIsNoOpSkip()
    {
        // default(struct) should be safe to read from the lookup-miss
        // path (caller checks Kind before consuming FinalStance).
        var resolved = default(TacticalResolvedGroupDecision);
        TestHarness.AssertEqual(0, resolved.FinalStance, "defaultFinalStance");
        TestHarness.AssertEqual((int)TacticalDoctrineDecisionKind.Skip, (int)resolved.Kind, "defaultKindSkip");
        TestHarness.AssertFalse(resolved.FromDoctrine, "defaultFromDoctrine");
        TestHarness.AssertFalse(resolved.FromLedger, "defaultFromLedger");
        TestHarness.AssertFalse(resolved.IsChargeOverride, "defaultIsChargeOverride");
        TestHarness.AssertEqual((int)ChargeOverrideAction.None, (int)resolved.ChargeAction, "defaultChargeAction");
    }

    private static void ConstructPreservesFields()
    {
        var resolved = new TacticalResolvedGroupDecision(
            finalStance: 3,
            kind: TacticalDoctrineDecisionKind.Apply,
            reason: "doctrine-attack",
            fromDoctrine: true,
            fromLedger: false,
            isChargeOverride: false,
            chargeAction: ChargeOverrideAction.None);
        TestHarness.AssertEqual(3, resolved.FinalStance, "finalStance");
        TestHarness.AssertEqual((int)TacticalDoctrineDecisionKind.Apply, (int)resolved.Kind, "kind");
        TestHarness.AssertEqual("doctrine-attack", resolved.Reason, "reason");
        TestHarness.AssertTrue(resolved.FromDoctrine, "fromDoctrine");
        TestHarness.AssertFalse(resolved.FromLedger, "fromLedger");
        TestHarness.AssertFalse(resolved.IsChargeOverride, "isChargeOverride");
        TestHarness.AssertEqual((int)ChargeOverrideAction.None, (int)resolved.ChargeAction, "chargeAction");

        var demote = new TacticalResolvedGroupDecision(
            finalStance: -1,
            kind: TacticalDoctrineDecisionKind.Skip,
            reason: "charge-demote",
            fromDoctrine: false,
            fromLedger: false,
            isChargeOverride: true,
            chargeAction: ChargeOverrideAction.DemoteToHold);
        TestHarness.AssertTrue(demote.IsChargeOverride, "demoteIsChargeOverride");
        TestHarness.AssertEqual((int)ChargeOverrideAction.DemoteToHold, (int)demote.ChargeAction, "demoteAction");
        TestHarness.AssertEqual(-1, demote.FinalStance, "demoteFinalStanceMinusOne");
    }

    private static void NullReasonNormalizes()
    {
        // Telemetry reason must never be null — apply path concatenates
        // into log lines without a null check. Empty/null both normalize
        // to "unknown".
        var resolved = new TacticalResolvedGroupDecision(
            finalStance: 2,
            kind: TacticalDoctrineDecisionKind.Apply,
            reason: null,
            fromDoctrine: false,
            fromLedger: false,
            isChargeOverride: false,
            chargeAction: ChargeOverrideAction.None);
        TestHarness.AssertEqual("unknown", resolved.Reason, "nullReasonNormalized");

        var empty = new TacticalResolvedGroupDecision(
            finalStance: 2,
            kind: TacticalDoctrineDecisionKind.Apply,
            reason: "",
            fromDoctrine: false,
            fromLedger: false,
            isChargeOverride: false,
            chargeAction: ChargeOverrideAction.None);
        TestHarness.AssertEqual("unknown", empty.Reason, "emptyReasonNormalized");
    }

    private static void ChargeOverrideEnumOrdinalsStable()
    {
        // Apply path uses (int)resolved.ChargeAction in a switch; if the
        // ordinals shift, behavior silently changes. Lock them.
        TestHarness.AssertEqual(0, (int)ChargeOverrideAction.None, "noneOrdinal");
        TestHarness.AssertEqual(1, (int)ChargeOverrideAction.PreserveVanilla, "preserveOrdinal");
        TestHarness.AssertEqual(2, (int)ChargeOverrideAction.DemoteToHold, "demoteOrdinal");
        TestHarness.AssertEqual(3, (int)ChargeOverrideAction.DenyExplicit, "denyOrdinal");
    }
}
