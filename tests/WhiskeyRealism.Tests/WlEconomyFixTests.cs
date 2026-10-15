using System;
using WhiskeyRealism.Strategic;

using static TestHarness;

// W&L player-economy bug fixes (plan 2026-10-15-wl-economy-bugs): the pure
// decisions behind the attrition scope, Buy Supplies, supply-staff scope and
// sign, dead once-effects and transfer-telemetry patches.
internal static class WlEconomyFixTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("wl attrition bonus is neutralised only for groups the player does not own", AttritionNeutralisedOnlyOutsidePlayerGroups),
            ("wl effect snapshot restores only its own untouched write", SnapshotRestoresOnlyOwnUntouchedWrite),
            ("wl buy supplies recipients are owned basic units of any command", BuySuppliesRecipientsAreOwnedBasicUnits),
            ("wl buy supplies correction runs only for hq commands", BuySuppliesCorrectionOnlyForHqCommands),
            ("wl buy supplies average shortage matches vanilla weighting", BuySuppliesAverageShortageMatchesVanilla),
            ("wl buy supplies fill ratio matches vanilla clamp", BuySuppliesFillRatioMatchesVanilla),
            ("wl supply staff extends to the player's own map group only", SupplyStaffExtendsToPlayersMapGroupOnly),
            ("wl supply efficiency sign correction inverts the duration factor", SupplyEfficiencySignInvertsDurationFactor),
            ("wl supply efficiency sign correction refuses invalid factors", SupplyEfficiencySignRefusesInvalidFactors),
            ("wl once effect fold scales running by one plus once", OnceEffectFoldScalesRunning),
            ("wl once effect fold covers supply and engineering only", OnceEffectFoldIds),
            ("wl transfer telemetry counts additions after vanilla's purge", TransferAddedCountAfterPurge),
            ("wl transfer telemetry classifies what actually happened", TransferOutcomeClassification),
        });
    }

    private static void AttritionNeutralisedOnlyOutsidePlayerGroups()
    {
        AssertTrue(WlEconomyFixes.NeutralizeAttritionBonus(careerActive: true, groupOwned: false), "AI or enemy group in a career");
        AssertFalse(WlEconomyFixes.NeutralizeAttritionBonus(careerActive: true, groupOwned: true), "player's group keeps the bonus");
        AssertFalse(WlEconomyFixes.NeutralizeAttritionBonus(careerActive: false, groupOwned: false), "no career: vanilla already returns 1");
    }

    private static void SnapshotRestoresOnlyOwnUntouchedWrite()
    {
        AssertTrue(WlEconomyFixes.ShouldRestore(sameArray: true, current: 1f, written: 1f), "own write still in place");
        AssertFalse(WlEconomyFixes.ShouldRestore(sameArray: false, current: 1f, written: 1f), "array was replaced");
        AssertFalse(WlEconomyFixes.ShouldRestore(sameArray: true, current: 0.5f, written: 1f), "a recompute wrote a new value");
    }

    private static void BuySuppliesRecipientsAreOwnedBasicUnits()
    {
        AssertTrue(WlEconomyFixes.IsSupplyRecipient(present: true, owned: true, unitType: 0), "owned infantry");
        AssertTrue(WlEconomyFixes.IsSupplyRecipient(present: true, owned: true, unitType: 13), "owned brigade-scale basic unit");
        AssertFalse(WlEconomyFixes.IsSupplyRecipient(present: true, owned: true, unitType: 14), "division HQ holds no stock");
        AssertFalse(WlEconomyFixes.IsSupplyRecipient(present: true, owned: false, unitType: 0), "not the player's unit");
        AssertFalse(WlEconomyFixes.IsSupplyRecipient(present: false, owned: true, unitType: 0), "destroyed slot");
    }

    private static void BuySuppliesCorrectionOnlyForHqCommands()
    {
        AssertFalse(WlEconomyFixes.NeedsSubordinateTypeCorrection(13), "basic command: vanilla's test is already per unit");
        AssertTrue(WlEconomyFixes.NeedsSubordinateTypeCorrection(14), "division");
        AssertTrue(WlEconomyFixes.NeedsSubordinateTypeCorrection(15), "corps");
        AssertTrue(WlEconomyFixes.NeedsSubordinateTypeCorrection(16), "army");
    }

    private static void BuySuppliesAverageShortageMatchesVanilla()
    {
        // DC:47387-47407: sum((1 - stock) * strength) / max(1, sum(strength)), clamped to [0, 1].
        AssertNear(0.25f, WlEconomyFixes.AverageShortage(weightedShortage: 0.5f, totalStrength: 2f), 1e-6f, "weighted mean");
        AssertNear(0.3f, WlEconomyFixes.AverageShortage(weightedShortage: 0.3f, totalStrength: 0.5f), 1e-6f, "denominator floored at 1");
        AssertNear(1f, WlEconomyFixes.AverageShortage(weightedShortage: 5f, totalStrength: 2f), 1e-6f, "clamped high");
        AssertNear(0f, WlEconomyFixes.AverageShortage(weightedShortage: -1f, totalStrength: 2f), 1e-6f, "clamped low");
    }

    private static void BuySuppliesFillRatioMatchesVanilla()
    {
        // DC:47442-47446: max(0, amount / totalShortage), capped at 1.
        AssertNear(0.5f, WlEconomyFixes.FillRatio(amount: 1f, totalShortage: 2f), 1e-6f, "partial");
        AssertNear(1f, WlEconomyFixes.FillRatio(amount: 5f, totalShortage: 2f), 1e-6f, "capped");
        AssertNear(0f, WlEconomyFixes.FillRatio(amount: 5f, totalShortage: 0f), 1e-6f, "nothing missing");
    }

    private static void SupplyStaffExtendsToPlayersMapGroupOnly()
    {
        AssertTrue(WlEconomyFixes.TreatMapGroupAsPlayerOwned(careerActive: true, groupOwned: false, isPlayersMapGroup: true), "AI parent group carrying the player's command");
        AssertFalse(WlEconomyFixes.TreatMapGroupAsPlayerOwned(careerActive: true, groupOwned: false, isPlayersMapGroup: false), "any other AI group");
        AssertFalse(WlEconomyFixes.TreatMapGroupAsPlayerOwned(careerActive: true, groupOwned: true, isPlayersMapGroup: true), "already owned: vanilla applies it");
        AssertFalse(WlEconomyFixes.TreatMapGroupAsPlayerOwned(careerActive: false, groupOwned: false, isPlayersMapGroup: true), "no career");
    }

    private static void SupplyEfficiencySignInvertsDurationFactor()
    {
        // Vanilla multiplies the resupply duration num15 by running[9] and, for an
        // owned group, by Camp.GetModifier(5) (DC:114346-114351). The corrected
        // running[9] makes that product 1 / (effect x camp).
        float r = WlEconomyFixes.SignCorrectedSupplyEfficiency(effect: 1.25f, campModifier: 1f);
        AssertNear(0.8f, r, 1e-6f, "+25% Quartermaster shortens the duration by 1/1.25");
        r = WlEconomyFixes.SignCorrectedSupplyEfficiency(effect: 1.25f, campModifier: 1.5f);
        AssertNear(1f / (1.25f * 1.5f), r * 1.5f, 1e-6f, "vanilla's later x camp leaves 1/(effect x camp)");
        r = WlEconomyFixes.SignCorrectedSupplyEfficiency(effect: 1f, campModifier: 0.5f);
        AssertNear(2f, r * 0.5f, 1e-6f, "a camp malus lengthens the duration");
        AssertNear(1f, WlEconomyFixes.SignCorrectedSupplyEfficiency(effect: 1f, campModifier: 1f), 1e-6f, "neutral stays neutral");
    }

    private static void SupplyEfficiencySignRefusesInvalidFactors()
    {
        AssertTrue(float.IsNaN(WlEconomyFixes.SignCorrectedSupplyEfficiency(effect: 0f, campModifier: 1f)), "zero effect");
        AssertTrue(float.IsNaN(WlEconomyFixes.SignCorrectedSupplyEfficiency(effect: 1f, campModifier: 0f)), "camp clamped to 0");
        AssertTrue(float.IsNaN(WlEconomyFixes.SignCorrectedSupplyEfficiency(effect: float.NaN, campModifier: 1f)), "NaN");
    }

    private static void OnceEffectFoldScalesRunning()
    {
        AssertNear(1.2f, WlEconomyFixes.FoldOnceIntoRunning(running: 1f, once: 0.2f), 1e-6f, "Logistics Corps +20%");
        AssertNear(1.25f * 1.3f, WlEconomyFixes.FoldOnceIntoRunning(running: 1.25f, once: 0.3f), 1e-6f, "composes with staff multiplicatively");
        AssertNear(0f, WlEconomyFixes.FoldOnceIntoRunning(running: 1f, once: -1.5f), 1e-6f, "never negative");
        AssertNear(1.1f, WlEconomyFixes.FoldOnceIntoRunning(running: 1.1f, once: 0f), 1e-6f, "nothing to fold");
    }

    private static void OnceEffectFoldIds()
    {
        AssertTrue(WlEconomyFixes.FoldsOnceEffect(9), "supply efficiency (actions 28, 50)");
        AssertTrue(WlEconomyFixes.FoldsOnceEffect(23), "engineering points (action 27)");
        AssertFalse(WlEconomyFixes.FoldsOnceEffect(22), "attrition: intent ambiguous, left vanilla");
        AssertFalse(WlEconomyFixes.FoldsOnceEffect(4), "incident chance has a once reader (DC:46139)");
        AssertFalse(WlEconomyFixes.FoldsOnceEffect(24), "infantry experience has a once reader (DC:140121)");
    }

    private static void TransferAddedCountAfterPurge()
    {
        // Vanilla drops every entry with transfertime <= 0 (DC:17255-17262) before it may add one.
        AssertEqual(1, WlEconomyFixes.TransferAddedCount(before: 1, staleBefore: 1, after: 1), "stale entry purged, one added");
        AssertEqual(0, WlEconomyFixes.TransferAddedCount(before: 2, staleBefore: 1, after: 1), "purged only");
        AssertEqual(1, WlEconomyFixes.TransferAddedCount(before: 0, staleBefore: 0, after: 1), "first transfer");
        AssertEqual(0, WlEconomyFixes.TransferAddedCount(before: 3, staleBefore: 0, after: 3), "nothing changed");
    }

    private static void TransferOutcomeClassification()
    {
        AssertEqual(WlTransferOutcome.Queued, WlEconomyFixes.ClassifyTransfer(transitStarted: true, careerActive: true), "a started transit counts, career or not");
        AssertEqual(WlTransferOutcome.Queued, WlEconomyFixes.ClassifyTransfer(transitStarted: true, careerActive: false), "normal campaign transit");
        AssertEqual(WlTransferOutcome.BlockedByCareerGuard, WlEconomyFixes.ClassifyTransfer(transitStarted: false, careerActive: true), "MoveUnit's career guard (DC:93099)");
        AssertEqual(WlTransferOutcome.NoTransit, WlEconomyFixes.ClassifyTransfer(transitStarted: false, careerActive: false), "re-parented inside its corps, or refused");
    }
}
