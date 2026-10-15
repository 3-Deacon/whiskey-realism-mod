using System;
using WhiskeyRealism.Strategic;

// WS C2b — campaign economy / admin write-surface regressions (2026-10-14 review).
internal static class CampaignEconomyFixTests
{
    public static void Run()
    {
        RecruitmentDraftsOnlyStateIneligibleWhenStrengthRatioAboveOne();
        RecruitmentDraftsOnlyStateEligibleAtParityUnderConscription();
        RecruitmentWithoutPlanTheaterKeepsVanilla();
        RecruitmentWithoutPlanTheaterStillProtectsThreatenedCapital();
    }

    private static void LogPass(string message)
    {
        Console.WriteLine("PASS " + message);
    }

    // ---------------------------------------------------------------- C-Recruit

    private static RecruitmentStateCandidate State(int id, Theater theater, int volunteers, int drafts, float support = 0.9f, string area = null)
    {
        return new RecruitmentStateCandidate
        {
            StateId = id,
            Theater = theater,
            AreaKey = area,
            Volunteers = volunteers,
            Drafts = drafts,
            Support = support,
            IsRecruitable = true,
            IsEnemyState = false,
            IsLocalArea = true
        };
    }

    // Vanilla ZoneRecruiting (decompile 12605-12609): num13 = volunteers < size; if
    // strengthRatio > 1 && num13 == 1 the whole recruitment pass aborts. Steering to a
    // drafts-only state above parity would cancel recruitment, even under conscription.
    private static void RecruitmentDraftsOnlyStateIneligibleWhenStrengthRatioAboveOne()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 0,
            PreferredTheater = Theater.East,
            StrengthRatio = 1.2f,
            OwnStateSupportFloor = 0.5f,
            AllowDraftReplacement = true
        };
        var candidates = new[]
        {
            State(38, Theater.East, volunteers: 100, drafts: 9000),
            State(15, Theater.River, volunteers: 6000, drafts: 0, support: 0.5f)
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 15, strengthNeeded: 5000, excludeEnemyStates: false);

        TestHarness.AssertFalse(decision.ShouldReplace, "drafts-only state must not be chosen above parity");
        TestHarness.AssertEqual(15, decision.StateId, "keeps vanilla volunteer state");
        LogPass("c2b recruitment drafts-only state ineligible when strength ratio above one");
    }

    // Vanilla only aborts on strengthRatio > 1 (strict), so at exactly 1.0 with
    // conscription active the drafts state stays a legal steering target.
    private static void RecruitmentDraftsOnlyStateEligibleAtParityUnderConscription()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 0,
            PreferredTheater = Theater.East,
            StrengthRatio = 1.0f,
            OwnStateSupportFloor = 0.5f,
            AllowDraftReplacement = true
        };
        var candidates = new[]
        {
            State(38, Theater.East, volunteers: 100, drafts: 9000),
            State(15, Theater.River, volunteers: 6000, drafts: 0, support: 0.5f)
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 15, strengthNeeded: 5000, excludeEnemyStates: false);

        TestHarness.AssertTrue(decision.ShouldReplace, "drafts state allowed at parity under conscription");
        TestHarness.AssertEqual(38, decision.StateId, "steers to preferred-theater drafts state");
        LogPass("c2b recruitment drafts-only state eligible at parity under conscription");
    }

    // No active plan => PreferredTheater Unknown. Without a threatened capital area the
    // ledger has no strategic reason to override vanilla's distance/pool choice.
    private static void RecruitmentWithoutPlanTheaterKeepsVanilla()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.Unknown,
            StrengthRatio = 0.8f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            State(38, Theater.East, volunteers: 9000, drafts: 0, support: 0.95f),
            State(15, Theater.River, volunteers: 6000, drafts: 0, support: 0.55f)
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 15, strengthNeeded: 5000, excludeEnemyStates: false);

        TestHarness.AssertFalse(decision.ShouldReplace, "no plan theater => vanilla");
        TestHarness.AssertEqual(15, decision.StateId, "vanilla state kept");
        LogPass("c2b recruitment without plan theater keeps vanilla");
    }

    private static void RecruitmentWithoutPlanTheaterStillProtectsThreatenedCapital()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.Unknown,
            StrengthRatio = 0.8f,
            OwnStateSupportFloor = 0.5f,
            ProtectedAreaKey = "VirginiaCapitalCorridor",
            ProtectedAreaThreatLevel = 0.9f
        };
        var candidates = new[]
        {
            State(38, Theater.East, volunteers: 9000, drafts: 0, support: 0.95f, area: "VirginiaCapitalCorridor"),
            State(15, Theater.River, volunteers: 6000, drafts: 0, support: 0.55f, area: "Tennessee")
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 15, strengthNeeded: 5000, excludeEnemyStates: false);

        TestHarness.AssertTrue(decision.ShouldReplace, "threatened capital still steers without plan");
        TestHarness.AssertEqual(38, decision.StateId, "protected-area state");
        LogPass("c2b recruitment without plan theater still protects threatened capital");
    }
}
