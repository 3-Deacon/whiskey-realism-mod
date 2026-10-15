using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic.BugFixes;
using static TestHarness;

// #82 per-frame camp placement budget and #83 hidden transport sounds (perf review L1,
// BUG-LOG-015). The simulation runs vanilla's camp rules (UpdateCampsAndEntrenchments
// 115408 place / 115426 fog destroy) on the 25-frame regular cycle (112216-112224,
// campaignprefs 3311-3312) through the real Prefix/Finalizer decisions.
internal static class FoggedModelBudgetTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("fogged camp budget units with a camp always flip", UnitsWithCampAlwaysFlip),
            ("fogged camp budget counts only a new camp from a flipped call", CountsOnlyNewCampFromFlippedCall),
            ("fogged camp budget placements per frame at least one", PlacementsPerFrameAtLeastOne),
            ("fogged camp budget exhausts within a frame and rolls over", BudgetExhaustsAndRollsOver),
            ("fogged camp budget simulation matches vanilla outcome at h=100", () => SimulationMatchesVanilla(100, 30, 2, 7)),
            ("fogged camp budget simulation matches vanilla outcome at h=50", () => SimulationMatchesVanilla(50, 10, 2, 11)),
            ("fogged camp budget simulation matches vanilla outcome at k=1", () => SimulationMatchesVanilla(60, 20, 1, 3)),
            ("fogged camp budget that never binds keeps vanilla timing", BudgetThatNeverBindsKeepsVanillaTiming),
            ("fogged transport sounds detached only while hidden with a model", TransportSoundsDetachedOnlyWhileHiddenWithModel),
            ("fogged transport flag deferred only inside the fix with a replay", TransportFlagDeferredOnlyInsideFixWithReplay),
            ("fogged transport flag ledger replays once on show and drops replaced models", TransportFlagLedgerReplaysOnceOnShow),
        });
    }

    private static void UnitsWithCampAlwaysFlip()
    {
        AssertTrue(FoggedModelRule.ShouldFlipUnderPlacementBudget(true, true, false), "camp, budget full: flip or 115426 destroys it");
        AssertTrue(FoggedModelRule.ShouldFlipUnderPlacementBudget(true, true, true), "camp, budget room");
        AssertTrue(FoggedModelRule.ShouldFlipUnderPlacementBudget(true, false, false), "camp, budget off");
        AssertTrue(FoggedModelRule.ShouldFlipUnderPlacementBudget(false, true, true), "no camp, room");
        AssertFalse(FoggedModelRule.ShouldFlipUnderPlacementBudget(false, true, false), "no camp, budget full: deferred");
        AssertTrue(FoggedModelRule.ShouldFlipUnderPlacementBudget(false, false, false), "no camp, budget off: vanilla-fix timing");
    }

    private static void CountsOnlyNewCampFromFlippedCall()
    {
        AssertTrue(FoggedModelRule.PlacedNewCamp(true, false, true), "flipped, none -> camp");
        AssertFalse(FoggedModelRule.PlacedNewCamp(true, true, true), "kept its camp (winter conversion is not budgeted)");
        AssertFalse(FoggedModelRule.PlacedNewCamp(true, false, false), "flipped, not ready yet");
        AssertFalse(FoggedModelRule.PlacedNewCamp(false, false, true), "not flipped (visible unit, vanilla placed it)");
        AssertFalse(FoggedModelRule.PlacedNewCamp(true, true, false), "camp broken");
    }

    private static void PlacementsPerFrameAtLeastOne()
    {
        AssertEqual(1, FoggedModelRule.EffectivePlacementsPerFrame(0), "zero");
        AssertEqual(1, FoggedModelRule.EffectivePlacementsPerFrame(-5), "negative");
        AssertEqual(1, FoggedModelRule.EffectivePlacementsPerFrame(1), "one");
        AssertEqual(2, FoggedModelRule.EffectivePlacementsPerFrame(2), "default");
        AssertEqual(9, FoggedModelRule.EffectivePlacementsPerFrame(9), "large");
    }

    private static void BudgetExhaustsAndRollsOver()
    {
        var budget = new PerFrameBudget();
        AssertTrue(budget.HasRoom(10, 2), "fresh frame");
        budget.Spend(10);
        AssertTrue(budget.HasRoom(10, 2), "one of two");
        budget.Spend(10);
        AssertFalse(budget.HasRoom(10, 2), "exhausted");
        AssertFalse(budget.HasRoom(10, 2), "still exhausted in the same frame");
        AssertTrue(budget.HasRoom(11, 2), "next frame starts at zero");
        budget.Spend(11);
        budget.Spend(11);
        budget.Spend(11);
        AssertFalse(budget.HasRoom(11, 3), "three of three");
        AssertTrue(budget.HasRoom(5, 3), "any other frame number (new scene) starts at zero");
        budget.Spend(6);
        AssertTrue(budget.HasRoom(6, 2), "a spend in a new frame resets first");
    }

    private sealed class SimUnit
    {
        internal int Phase;
        internal int EligibleFrame;
        internal bool HasCamp;
        internal bool StartedWithCamp;
        internal int PlacedFrame = -1;
        internal int Placements;
        internal bool Destroyed;
    }

    private sealed class SimResult
    {
        internal List<SimUnit> Units;
        internal int MaxPerFrame;
        internal int LastPlacement = -1;
    }

    private const int Cycle = 25;

    // H hidden AI units on one regular cycle with random phases (112850); all reach the
    // camp time within one cycle, as after a night halt (133958, 115408). `preCamped`
    // already have a camp. Units are visited in list order inside a frame.
    private static SimResult Simulate(int units, int preCamped, int perFrame, bool budgetOn, int seed)
    {
        var rng = new Random(seed);
        var list = new List<SimUnit>();
        for (int i = 0; i < units; i++)
        {
            bool camp = i < preCamped;
            list.Add(new SimUnit { Phase = rng.Next(Cycle), EligibleFrame = 100 + rng.Next(Cycle), HasCamp = camp, StartedWithCamp = camp });
        }
        var budget = new PerFrameBudget();
        var result = new SimResult { Units = list };
        int frames = 100 + Cycle * (units + 4);
        for (int f = 0; f < frames; f++)
        {
            int placedThisFrame = 0;
            foreach (var u in list)
            {
                if (f < u.Phase || (f - u.Phase) % Cycle != 0) continue;
                // Prefix
                bool hasCamp = u.HasCamp;
                bool flipped = FoggedModelRule.ShouldFlipUnderPlacementBudget(
                    hasCamp, budgetOn, budget.HasRoom(f, FoggedModelRule.EffectivePlacementsPerFrame(perFrame)));
                bool state = flipped && hasCamp;
                bool fog = !flipped;
                // vanilla: place (115408), fog destroy (115426)
                if (!u.HasCamp && f >= u.EligibleFrame && !fog)
                {
                    u.HasCamp = true;
                    u.PlacedFrame = f;
                    u.Placements++;
                    placedThisFrame++;
                    result.LastPlacement = f;
                }
                if (u.HasCamp && fog)
                {
                    u.HasCamp = false;
                    u.Destroyed = true;
                }
                // Finalizer
                if (FoggedModelRule.PlacedNewCamp(flipped, state, u.HasCamp)) budget.Spend(f);
            }
            if (placedThisFrame > result.MaxPerFrame) result.MaxPerFrame = placedThisFrame;
        }
        return result;
    }

    private static void SimulationMatchesVanilla(int units, int preCamped, int perFrame, int seed)
    {
        var vanilla = Simulate(units, preCamped, perFrame, budgetOn: false, seed);
        var budgeted = Simulate(units, preCamped, perFrame, budgetOn: true, seed);
        // A frame only visits the units of one phase (slot), so a slot with n fresh units
        // drains in ceil(n/K) of its cycles.
        var perSlot = new int[Cycle];
        foreach (var u in vanilla.Units)
            if (!u.StartedWithCamp) perSlot[u.Phase]++;
        int maxSlot = 0;
        foreach (int n in perSlot) maxSlot = Math.Max(maxSlot, n);
        int bound = vanilla.LastPlacement + Cycle * ((maxSlot + perFrame - 1) / perFrame);
        AssertTrue(budgeted.MaxPerFrame <= perFrame, "at most " + perFrame + " new camps per frame, got " + budgeted.MaxPerFrame);
        AssertTrue(budgeted.LastPlacement <= bound, "every camp placed within ceil(slot/K) extra cycles: " + budgeted.LastPlacement + " <= " + bound);
        for (int i = 0; i < units; i++)
        {
            var v = vanilla.Units[i];
            var b = budgeted.Units[i];
            AssertFalse(b.Destroyed, "unit " + i + " never reaches the fog destroy branch");
            AssertFalse(v.Destroyed, "unit " + i + " vanilla-fix run never destroys");
            AssertTrue(b.HasCamp && v.HasCamp, "unit " + i + " ends with a camp in both runs");
            AssertEqual(v.Placements, b.Placements, "unit " + i + " same number of placements");
            AssertEqual(v.StartedWithCamp ? 0 : 1, b.Placements, "unit " + i + " placed once unless it had a camp");
            if (v.StartedWithCamp) continue;
            AssertTrue(b.PlacedFrame >= v.PlacedFrame, "unit " + i + " never earlier");
            AssertEqual(0, (b.PlacedFrame - v.PlacedFrame) % Cycle, "unit " + i + " deferred by whole regular cycles");
        }
    }

    private static void BudgetThatNeverBindsKeepsVanillaTiming()
    {
        var a = Simulate(80, 10, 2, budgetOn: false, seed: 5);
        var b = Simulate(80, 10, 50, budgetOn: true, seed: 5);
        for (int i = 0; i < a.Units.Count; i++)
            AssertEqual(a.Units[i].PlacedFrame, b.Units[i].PlacedFrame, "unit " + i + " same frame when the budget never binds");
        AssertTrue(a.MaxPerFrame > 2, "the unbudgeted burst exceeds two per frame at h=80 (" + a.MaxPerFrame + ")");
    }

    private static void TransportSoundsDetachedOnlyWhileHiddenWithModel()
    {
        AssertTrue(FoggedModelRule.ShouldDetachTransportSounds(false, true, true, false), "hidden AI land unit with a model");
        AssertFalse(FoggedModelRule.ShouldDetachTransportSounds(false, true, false, false), "hidden, no model (RegimentMove turns them off)");
        AssertFalse(FoggedModelRule.ShouldDetachTransportSounds(false, false, true, false), "visible AI unit");
        AssertFalse(FoggedModelRule.ShouldDetachTransportSounds(true, true, true, false), "player alliance");
        AssertFalse(FoggedModelRule.ShouldDetachTransportSounds(false, true, true, true), "fleet");
    }

    private static void TransportFlagDeferredOnlyInsideFixWithReplay()
    {
        AssertTrue(FoggedModelRule.ShouldDeferTransportFlag(true, true, true), "fogged land model, flag on, replay resolved");
        AssertFalse(FoggedModelRule.ShouldDeferTransportFlag(false, true, true), "flag off: vanilla");
        AssertFalse(FoggedModelRule.ShouldDeferTransportFlag(true, false, true), "outside the fix's call (visible units, fleets, player units): vanilla");
        AssertFalse(FoggedModelRule.ShouldDeferTransportFlag(true, true, false), "no replay available: vanilla");
    }

    private sealed class SimModel
    {
        internal bool Active = true;
        internal int FlagSetups;
    }

    // The Finalizer's order per regular cycle: create (Prefix defers), hide/show, Resolve.
    private static void TransportFlagLedgerReplaysOnceOnShow()
    {
        var ledger = new DeferredFlagLedger<object, SimModel>(4);
        var unit = new object();
        var other = new object();
        var model = new SimModel();

        AssertFalse(ledger.Resolve(unit, model, true), "nothing deferred: no replay");
        ledger.Defer(unit, model);
        model.Active = false;
        AssertEqual(1, ledger.Count, "deferred at creation");
        for (int cycle = 0; cycle < 5; cycle++)
            AssertFalse(ledger.Resolve(unit, model, model.Active), "hidden cycles: no replay");
        AssertFalse(ledger.Resolve(other, model, true), "another unit: no replay");
        model.Active = true;
        if (ledger.Resolve(unit, model, model.Active)) model.FlagSetups++;
        AssertEqual(1, model.FlagSetups, "shown: flag set up");
        AssertEqual(0, ledger.Count, "entry consumed");
        for (int cycle = 0; cycle < 3; cycle++)
            if (ledger.Resolve(unit, model, model.Active)) model.FlagSetups++;
        AssertEqual(1, model.FlagSetups, "set up once (vanilla never refreshes a live model's flag)");

        // Replaced while hidden (rail -> river): the old model is dropped and never set up.
        var rail = new SimModel { Active = false };
        ledger.Defer(unit, rail);
        var river = new SimModel { Active = false };
        AssertFalse(ledger.Resolve(unit, river, false), "replaced model");
        AssertEqual(0, ledger.Count, "replaced entry dropped");
        AssertFalse(ledger.Resolve(unit, river, true), "the replacement was not deferred");

        // Destroyed while hidden: dropped.
        ledger.Defer(unit, rail);
        AssertFalse(ledger.Resolve(unit, null, false), "model gone");
        AssertEqual(0, ledger.Count, "destroyed entry dropped");

        // Left active (the fog restore failed): set up at once.
        var kept = new SimModel();
        ledger.Defer(other, kept);
        AssertTrue(ledger.Resolve(other, kept, kept.Active), "active right after creation: set up now");

        // A second deferral for the same unit replaces the first.
        ledger.Defer(unit, rail);
        ledger.Defer(unit, river);
        AssertEqual(1, ledger.Count, "one entry per unit");
        AssertTrue(ledger.Resolve(unit, river, true), "the newest model is the one set up");
        ledger.Defer(unit, river);
        ledger.Clear();
        AssertEqual(0, ledger.Count, "campaign reset");
        ledger.Defer(null, river);
        ledger.Defer(unit, null);
        AssertEqual(0, ledger.Count, "null unit or model ignored");
    }
}
