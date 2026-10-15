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
            ("wl staff scope reaches the player's own units in the carrying map group only", StaffScopeReachesPlayerUnitsOnly),
            ("wl supply scope never carries vanilla's inverted efficiency", SupplyScopeNeverCarriesInvertedEfficiency),
            ("wl army panel shows player staff effects only on the player's groups", PanelShowsStaffEffectsOnlyOnPlayerGroups),
            ("wl once effect fold covers the promised morale, renewal and staff effects", OnceEffectFoldPromisedEffects),
            ("wl once effect fold covers signal corps order delays", OnceEffectFoldSignalCorps),
            ("wl running effect fold covers gamble's incident chance", RunningEffectFoldGamble),
            ("wl effect folds run over whole arrays with the sign coupling", EffectFoldsOverArrays),
            ("wl attrition bonus stays on the player's own regiments without the staff scope", AttritionStaysOnPlayerRegiments),
            ("wl transfer stale count matches vanilla's purge", TransferStaleMatchesVanillaPurge),
        });
    }

    private static void AttritionNeutralisedOnlyOutsidePlayerGroups()
    {
        AssertTrue(WlEconomyFixes.NeutralizeAttritionBonus(careerActive: true, groupOwned: false, unitOwned: false), "AI or enemy group in a career");
        AssertFalse(WlEconomyFixes.NeutralizeAttritionBonus(careerActive: true, groupOwned: true, unitOwned: false), "player's group keeps the bonus");
        AssertFalse(WlEconomyFixes.NeutralizeAttritionBonus(careerActive: false, groupOwned: false, unitOwned: false), "no career: vanilla already returns 1");
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
        AssertFalse(WlEconomyFixes.FoldsOnceEffect(0), "prestige generation: item ids do not match their texts");
        AssertFalse(WlEconomyFixes.FoldsOnceEffect(3), "rank progression: only companion type-0 data, not promised");
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

    private static void StaffScopeReachesPlayerUnitsOnly()
    {
        AssertTrue(WlEconomyFixes.RaiseGroupForPlayerUnit(careerActive: true, groupOwned: false, unitOwned: true, groupIsPlayersMapGroup: true), "player's regiment in the AI parent group");
        AssertFalse(WlEconomyFixes.RaiseGroupForPlayerUnit(careerActive: true, groupOwned: false, unitOwned: false, groupIsPlayersMapGroup: true), "AI peer in the same map group");
        AssertFalse(WlEconomyFixes.RaiseGroupForPlayerUnit(careerActive: true, groupOwned: false, unitOwned: true, groupIsPlayersMapGroup: false), "not the carrying map group");
        AssertFalse(WlEconomyFixes.RaiseGroupForPlayerUnit(careerActive: true, groupOwned: true, unitOwned: true, groupIsPlayersMapGroup: true), "already owned: vanilla applies it");
        AssertFalse(WlEconomyFixes.RaiseGroupForPlayerUnit(careerActive: false, groupOwned: false, unitOwned: true, groupIsPlayersMapGroup: true), "no career");
    }

    private static void SupplyScopeNeverCarriesInvertedEfficiency()
    {
        // Vanilla's later multiplications are num15 *= running[9]; num15 *= camp.
        float camp = 1.4f, effect = 1.25f;
        float r = WlEconomyFixes.SupplyEfficiencyForCall(nativelyOwned: false, raisedByScope: true, signFixOn: false, effect: effect, campModifier: camp);
        AssertNear(1f, r * camp, 1e-6f, "scope without the sign fix: effect 9 and camp 5 cancel to neutral");
        r = WlEconomyFixes.SupplyEfficiencyForCall(nativelyOwned: false, raisedByScope: true, signFixOn: true, effect: effect, campModifier: camp);
        AssertNear(1f / (effect * camp), r * camp, 1e-6f, "scope with the sign fix: corrected direction");
        r = WlEconomyFixes.SupplyEfficiencyForCall(nativelyOwned: true, raisedByScope: false, signFixOn: true, effect: effect, campModifier: camp);
        AssertNear(1f / (effect * camp), r * camp, 1e-6f, "owned group with the sign fix");
        AssertTrue(float.IsNaN(WlEconomyFixes.SupplyEfficiencyForCall(nativelyOwned: true, raisedByScope: false, signFixOn: false, effect: effect, campModifier: camp)), "owned group, sign fix off: vanilla (rollback)");
        AssertTrue(float.IsNaN(WlEconomyFixes.SupplyEfficiencyForCall(nativelyOwned: false, raisedByScope: false, signFixOn: true, effect: effect, campModifier: camp)), "AI group: vanilla already reads 1");
        AssertTrue(float.IsNaN(WlEconomyFixes.SupplyEfficiencyForCall(nativelyOwned: false, raisedByScope: true, signFixOn: false, effect: effect, campModifier: 0f)), "camp clamped to 0: leave vanilla");
    }

    private static void PanelShowsStaffEffectsOnlyOnPlayerGroups()
    {
        AssertFalse(WlEconomyFixes.HidePlayerStaffEffectsOnPanel(careerActive: true, panelUnitOwned: true, isPlayersMapGroup: false), "player's own group");
        AssertFalse(WlEconomyFixes.HidePlayerStaffEffectsOnPanel(careerActive: true, panelUnitOwned: false, isPlayersMapGroup: true), "map group carrying the player's command");
        AssertTrue(WlEconomyFixes.HidePlayerStaffEffectsOnPanel(careerActive: true, panelUnitOwned: false, isPlayersMapGroup: false), "AI or enemy group");
        AssertFalse(WlEconomyFixes.HidePlayerStaffEffectsOnPanel(careerActive: false, panelUnitOwned: false, isPlayersMapGroup: false), "no career: nothing to hide");
        AssertEqual(6, WlEconomyFixes.PanelStaffEffects.Length, "supply 9/10/11/14, morale 16, fighting spirit 17");
    }

    private static void OnceEffectFoldPromisedEffects()
    {
        AssertTrue(WlEconomyFixes.FoldsOnceEffect(17), "fighting spirit (action 31 High Spirits)");
        AssertTrue(WlEconomyFixes.FoldsOnceEffect(19), "contract renewal (actions 36/37/46/64/65)");
        AssertTrue(WlEconomyFixes.FoldsOnceEffect(27), "staff efficiency (actions 30/66)");
        AssertTrue(WlEconomyFixes.OnceFoldNeedsSignFix(9), "supply efficiency needs the sign fix");
        AssertFalse(WlEconomyFixes.OnceFoldNeedsSignFix(17), "fighting spirit points the right way in vanilla");
    }

    private static void OnceEffectFoldSignalCorps()
    {
        // Action 29 "Signal Corps": (12, type 2, 0.2) and (13, type 2, 0.2), "Decreases order delays in your unit".
        AssertTrue(WlEconomyFixes.FoldsOnceEffect(12), "battle order delays (DC:125136)");
        AssertTrue(WlEconomyFixes.FoldsOnceEffect(13), "campaign order delays (DC:125069)");
        AssertNear(0.8f, WlEconomyFixes.FoldOnceIntoRunning(running: 1f, once: -0.2f), 1e-6f, "type 2 shortens the delay");
    }

    private static void RunningEffectFoldGamble()
    {
        // Action 20 "Gamble": (4, type 3, 0.05); incident chance is read only as once (DC:46139 1 + EM(4, 0)).
        AssertTrue(WlEconomyFixes.FoldsRunningEffect(4), "incident chance");
        AssertFalse(WlEconomyFixes.FoldsRunningEffect(9), "supply efficiency has a running reader");
        AssertNear(0.1f, WlEconomyFixes.FoldRunningIntoOnce(once: 0.05f, running: 1.05f), 1e-6f, "x1.05 adds +0.05 to 1 + once");
        AssertNear(0.05f, WlEconomyFixes.FoldRunningIntoOnce(once: 0.05f, running: 1f), 1e-6f, "neutral running adds nothing");
    }

    private static void EffectFoldsOverArrays()
    {
        float[] running = { 1f, 1f, 1f, 1f, 1.05f, 1f, 1f, 1f, 1f, 1.25f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        float[] once = new float[running.Length];
        once[9] = 0.2f; once[12] = -0.2f; once[22] = 0.2f; once[4] = 0.05f;
        int folded = WlEconomyFixes.FoldEffects(running, once, signFixOn: false);
        AssertNear(1.25f, running[9], 1e-6f, "sign fix off: effect 9 not folded");
        AssertNear(0.8f, running[12], 1e-6f, "order delay folded");
        AssertNear(1f, running[22], 1e-6f, "attrition left vanilla");
        AssertNear(0.1f, once[4], 1e-6f, "incident chance folded into once");
        AssertEqual(2, folded, "two slots folded");
        float[] running2 = (float[])running.Clone();
        running2[9] = 1.25f;
        WlEconomyFixes.FoldEffects(running2, once, signFixOn: true);
        AssertNear(1.5f, running2[9], 1e-6f, "sign fix on: effect 9 folded");
        AssertEqual(0, WlEconomyFixes.FoldEffects(null, once, signFixOn: true), "missing array");
    }

    private static void AttritionStaysOnPlayerRegiments()
    {
        // Vanilla gave the bonus to every unit; the player's own regiments keep it whatever the staff-scope flag says.
        AssertFalse(WlEconomyFixes.NeutralizeAttritionBonus(careerActive: true, groupOwned: false, unitOwned: true), "player's regiment inside an AI parent group");
        AssertTrue(WlEconomyFixes.NeutralizeAttritionBonus(careerActive: true, groupOwned: false, unitOwned: false), "AI peer in the same group");
    }

    private static void TransferStaleMatchesVanillaPurge()
    {
        // Vanilla's purge reads transfertime off every entry (DC:17257): a Unity-destroyed unit is still an entry.
        AssertTrue(WlEconomyFixes.CountsAsStaleTransfer(isEntry: true, transferTime: 0f), "arrived or never started");
        AssertFalse(WlEconomyFixes.CountsAsStaleTransfer(isEntry: true, transferTime: 2f), "in transit");
        AssertFalse(WlEconomyFixes.CountsAsStaleTransfer(isEntry: false, transferTime: 0f), "not a regiment reference");
    }
}
