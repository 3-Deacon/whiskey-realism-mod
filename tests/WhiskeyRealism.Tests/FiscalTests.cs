using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WhiskeyRealism;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Util;

using static TestFixtures;
using static TestHarness;

internal static class FiscalTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("recruitment intent prefers supported volunteers", RecruitmentIntentPrefersSupportedVolunteers),
            ("recruitment intent does not leave preferred theater for raw pool", RecruitmentIntentDoesNotLeavePreferredTheaterForRawPool),
            ("recruitment intent keeps vanilla when preferred theater unavailable", RecruitmentIntentKeepsVanillaWhenPreferredTheaterUnavailable),
            ("recruitment intent keeps vanilla when draft would be forced at parity", RecruitmentIntentKeepsVanillaWhenDraftWouldBeForcedAtParity),
            ("recruitment intent protects threatened priority area", RecruitmentIntentProtectsThreatenedPriorityArea),
            ("recruitment intent ignores priority area without threat", RecruitmentIntentIgnoresPriorityAreaWithoutThreat),
            ("recruitment intent avoids enemy states when excluded", RecruitmentIntentAvoidsEnemyStatesWhenExcluded),
            ("recruitment log gate suppresses repeated replacements", RecruitmentLogGateSuppressesRepeatedReplacements),
            ("fiscal csa healthy credit stays balanced", FiscalCsaHealthyCreditStaysBalanced),
            ("fiscal enters credit defense before gate", FiscalEntersCreditDefenseBeforeGate),
            ("fiscal enters emergency before bond floor", FiscalEntersEmergencyBeforeBondFloor),
            ("fiscal protects supply before force growth", FiscalProtectsSupplyBeforeForceGrowth),
            ("fiscal force cap suppresses manpower policies", FiscalForceCapSuppressesManpowerPolicies),
            ("fiscal force costs suppress manpower policies", FiscalForceCostsSuppressManpowerPolicies),
            ("fiscal hysteresis prevents immediate recovery", FiscalHysteresisPreventsImmediateRecovery),
            ("fiscal credit defense requires stable exit ticks", FiscalCreditDefenseRequiresStableExitTicks),
            ("fiscal emergency residue clears after stable ticks", FiscalEmergencyResidueClearsAfterStableTicks),
            ("fiscal clamps disabled subsidy focus to zero", FiscalClampsDisabledSubsidyFocusToZero),
            ("fiscal clamps negative saved subsidy values", FiscalClampsNegativeSavedSubsidyValues),
            ("financial ai log gate suppresses repeated corrections", FinancialAiLogGateSuppressesRepeatedCorrections),
        });
    }

    private static void RecruitmentIntentPrefersSupportedVolunteers()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.East,
            StrengthRatio = 0.85f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            new RecruitmentStateCandidate
            {
                StateId = 38,
                Theater = Theater.East,
                Volunteers = 7000,
                Drafts = 2000,
                Support = 0.9f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            },
            new RecruitmentStateCandidate
            {
                StateId = 15,
                Theater = Theater.River,
                Volunteers = 8000,
                Drafts = 5000,
                Support = 0.75f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            }
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 15, strengthNeeded: 5000, excludeEnemyStates: false);

        AssertEqual(true, decision.ShouldReplace);
        AssertEqual(38, decision.StateId);
        AssertEqual("preferred-theater-volunteers", decision.Reason);
    }

    private static void RecruitmentIntentKeepsVanillaWhenDraftWouldBeForcedAtParity()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.East,
            StrengthRatio = 1.05f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            new RecruitmentStateCandidate
            {
                StateId = 38,
                Theater = Theater.East,
                Volunteers = 1000,
                Drafts = 7000,
                Support = 0.9f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            },
            new RecruitmentStateCandidate
            {
                StateId = 15,
                Theater = Theater.River,
                Volunteers = 6000,
                Drafts = 0,
                Support = 0.7f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            }
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 15, strengthNeeded: 5000, excludeEnemyStates: false);

        AssertEqual(false, decision.ShouldReplace);
        AssertEqual(15, decision.StateId);
    }

    private static void RecruitmentIntentDoesNotLeavePreferredTheaterForRawPool()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.East,
            StrengthRatio = 0.8f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            new RecruitmentStateCandidate
            {
                StateId = 38,
                Theater = Theater.East,
                Volunteers = 2000,
                Drafts = 0,
                Support = 0.9f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            },
            new RecruitmentStateCandidate
            {
                StateId = 0,
                Theater = Theater.West,
                Volunteers = 20000,
                Drafts = 0,
                Support = 0.9f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            }
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 38, strengthNeeded: 60, excludeEnemyStates: false);

        AssertEqual(false, decision.ShouldReplace);
        AssertEqual(38, decision.StateId);
    }

    private static void RecruitmentIntentKeepsVanillaWhenPreferredTheaterUnavailable()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.East,
            StrengthRatio = 0.8f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            new RecruitmentStateCandidate
            {
                StateId = 8,
                Theater = Theater.Coast,
                Volunteers = 60,
                Drafts = 0,
                Support = 0.5f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            },
            new RecruitmentStateCandidate
            {
                StateId = 0,
                Theater = Theater.West,
                Volunteers = 20000,
                Drafts = 0,
                Support = 0.95f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = false
            }
        };

        var exploratory = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 8, strengthNeeded: 0, excludeEnemyStates: false);
        var concrete = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 8, strengthNeeded: 60, excludeEnemyStates: false);

        AssertEqual(false, exploratory.ShouldReplace);
        AssertEqual(8, exploratory.StateId);
        AssertEqual(false, concrete.ShouldReplace);
        AssertEqual(8, concrete.StateId);
    }

    private static void RecruitmentIntentProtectsThreatenedPriorityArea()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.East,
            ProtectedAreaKey = "VirginiaCapitalCorridor",
            ProtectedAreaThreatLevel = 0.8f,
            ProtectedAreaThreatThreshold = 0.35f,
            StrengthRatio = 0.85f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            new RecruitmentStateCandidate
            {
                StateId = 29,
                Theater = Theater.East,
                AreaKey = "NorthwestVirginia",
                Volunteers = 9000,
                Drafts = 0,
                Support = 0.9f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            },
            new RecruitmentStateCandidate
            {
                StateId = 38,
                Theater = Theater.East,
                AreaKey = "VirginiaCapitalCorridor",
                Volunteers = 6000,
                Drafts = 0,
                Support = 0.85f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = false
            }
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 29, strengthNeeded: 5000, excludeEnemyStates: false);

        AssertEqual(true, decision.ShouldReplace);
        AssertEqual(38, decision.StateId);
        AssertEqual("protected-area-volunteers", decision.Reason);
    }

    private static void RecruitmentIntentIgnoresPriorityAreaWithoutThreat()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.East,
            ProtectedAreaKey = "VirginiaCapitalCorridor",
            ProtectedAreaThreatLevel = 0.1f,
            ProtectedAreaThreatThreshold = 0.35f,
            StrengthRatio = 0.85f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            new RecruitmentStateCandidate
            {
                StateId = 29,
                Theater = Theater.East,
                AreaKey = "NorthwestVirginia",
                Volunteers = 9000,
                Drafts = 0,
                Support = 0.9f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            },
            new RecruitmentStateCandidate
            {
                StateId = 38,
                Theater = Theater.East,
                AreaKey = "VirginiaCapitalCorridor",
                Volunteers = 6000,
                Drafts = 0,
                Support = 0.85f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = false
            }
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 29, strengthNeeded: 5000, excludeEnemyStates: false);

        AssertEqual(false, decision.ShouldReplace);
        AssertEqual(29, decision.StateId);
    }

    private static void RecruitmentIntentAvoidsEnemyStatesWhenExcluded()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 0,
            PreferredTheater = Theater.East,
            StrengthRatio = 0.8f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            new RecruitmentStateCandidate
            {
                StateId = 39,
                Theater = Theater.East,
                Volunteers = 8000,
                Drafts = 5000,
                Support = 0.7f,
                IsRecruitable = true,
                IsEnemyState = true,
                IsLocalArea = true
            },
            new RecruitmentStateCandidate
            {
                StateId = 31,
                Theater = Theater.East,
                Volunteers = 7000,
                Drafts = 0,
                Support = 0.85f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = false
            }
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 39, strengthNeeded: 5000, excludeEnemyStates: true);

        AssertEqual(true, decision.ShouldReplace);
        AssertEqual(31, decision.StateId);
    }

    private static void RecruitmentLogGateSuppressesRepeatedReplacements()
    {
        var gate = new RecruitmentLogGate();
        string first = RecruitmentLogGate.Signature(1, 38, 0, 60, Theater.East, "volunteer-high-support");
        string repeat = RecruitmentLogGate.Signature(1, 38, 0, 60, Theater.East, "volunteer-high-support");
        string changed = RecruitmentLogGate.Signature(1, 8, 0, 60, Theater.East, "volunteer-high-support");

        AssertEqual(true, gate.ShouldLog(first));
        AssertEqual(false, gate.ShouldLog(repeat));
        AssertEqual(true, gate.ShouldLog(changed));
    }

    private static FiscalInput BuildFiscalInput()
    {
        return new FiscalInput
        {
            AllianceId = 1,
            EraStage = EraStage.Amateur1861,
            CurrentChapter = 1,
            CurrentRating = 4,
            RatingNotches = 12,
            EmergencyPolicyFailureRating = 7,
            RecruitmentFailureRating = 8,
            ConstructionFailureRating = 8,
            WeaponFailureRating = 9,
            Treasury = 15000000f,
            Debt = 75000000f,
            AnnualBalance = -5000000f,
            InterestCost = -3000000f,
            ArmyUpkeep = -25000000f,
            NavyUpkeep = -3000000f,
            RecruitmentCost = -4000000f,
            SupplyDepotPurchases = -2000000f,
            SupplyPressure = 0.15f,
            AmmoPressure = 0.10f,
            TransportPressure = 0.20f,
            LowSupplyFormationCount = 0,
            LowAmmoFormationCount = 0,
            TopSupplyTheater = "VirginiaCapitalCorridor"
        };
    }

    private static void FiscalCsaHealthyCreditStaysBalanced()
    {
        var output = FiscalIntentLedger.Compute(BuildFiscalInput(), new FiscalOptions());
        AssertEqual(FiscalPosture.BalancedWar, output.Posture);
        AssertEqual(false, output.ForceCapWarning);
    }

    private static void FiscalEntersCreditDefenseBeforeGate()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 6;
        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        AssertEqual(FiscalPosture.CreditDefense, output.Posture);
        AssertEqual(FiscalGate.EmergencyPolicy, output.DefendedGate);
    }

    private static void FiscalEntersEmergencyBeforeBondFloor()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 11;
        input.Treasury = -1000000f;
        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        AssertEqual(FiscalPosture.EmergencySolvency, output.Posture);
        AssertEqual(true, output.ForceCapWarning);
    }

    private static void FiscalProtectsSupplyBeforeForceGrowth()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 8;
        input.SupplyPressure = 0.85f;
        input.LowSupplyFormationCount = 4;
        input.LowAmmoFormationCount = 2;
        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        AssertEqual(true, output.SupplyProtection);
        AssertEqual(true, output.ForceCapWarning);
        AssertEqual("VirginiaCapitalCorridor", output.TheaterSupplyPriority);
    }

    private static void FiscalForceCapSuppressesManpowerPolicies()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 10;
        input.SupplyPressure = 0.80f;
        input.LowSupplyFormationCount = 5;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        float weight = FiscalPolicyScorer.PolicyWeight(output, 1, 136);

        AssertTrue(weight < 0f, "force-cap state should suppress CSA draft escalation");
    }

    private static void FiscalForceCostsSuppressManpowerPolicies()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 4;
        input.SupplyPressure = 0.10f;
        input.LowSupplyFormationCount = 0;
        input.LowAmmoFormationCount = 0;
        input.ArmyUpkeep = -50000000f;
        input.NavyUpkeep = -5000000f;
        input.RecruitmentCost = -5000000f;
        input.SupplyDepotPurchases = 0f;
        input.AnnualBalance = -30000000f;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        float weight = FiscalPolicyScorer.PolicyWeight(output, 1, 136);

        AssertEqual(true, output.ForceCapWarning);
        AssertTrue(weight < 0f, "force-cost pressure should suppress CSA draft escalation");
    }

    private static void FiscalHysteresisPreventsImmediateRecovery()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 6;
        input.Memory.PreviousPosture = FiscalPosture.CreditDefense;
        input.Memory.EmergencyResidue = true;
        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        AssertEqual(FiscalPosture.CreditDefense, output.Posture);
    }

    private static void FiscalCreditDefenseRequiresStableExitTicks()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 4;
        input.AnnualBalance = 500000f;
        input.Treasury = 2500000f;
        input.Memory.PreviousPosture = FiscalPosture.CreditDefense;
        input.Memory.EmergencyResidue = false;
        input.Memory.StableTicksAboveEmergency = 13;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        AssertEqual(FiscalPosture.CreditDefense, output.Posture);

        input.Memory.StableTicksAboveEmergency = 14;
        output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        AssertEqual(FiscalPosture.BalancedWar, output.Posture);
    }

    private static void FiscalEmergencyResidueClearsAfterStableTicks()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 4;
        input.AnnualBalance = 500000f;
        input.Treasury = 2500000f;
        input.Memory.PreviousPosture = FiscalPosture.CreditDefense;
        input.Memory.EmergencyResidue = true;
        input.Memory.StableTicksAboveEmergency = 14;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        AssertEqual(FiscalPosture.BalancedWar, output.Posture);
    }

    private static void FiscalClampsDisabledSubsidyFocusToZero()
    {
        var input = BuildFiscalInput();
        input.Subsidies[3] = 0.20f;
        input.SubsidyFocus[3] = -1f;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());

        AssertEqual(0f, output.TargetSubsidyMin[3]);
        AssertEqual(0f, output.TargetSubsidyMax[3]);
    }

    private static void FiscalClampsNegativeSavedSubsidyValues()
    {
        var input = BuildFiscalInput();
        input.Subsidies[3] = -0.95f;
        input.SubsidyFocus[3] = 0.20f;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());

        // Bands are directional (C-Fiscal): never negative, ceiling = personality focus cap.
        AssertEqual(0f, output.TargetSubsidyMin[3]);
        AssertEqual(0.20f, output.TargetSubsidyMax[3]);
    }

    private static void FinancialAiLogGateSuppressesRepeatedCorrections()
    {
        var gate = new FinancialAiLogGate();
        string first = FinancialAiLogGate.Signature(1, "subsidy", 4, 0.15f, 0.10f, FiscalPosture.BalancedWar);
        string repeat = FinancialAiLogGate.Signature(1, "subsidy", 4, 0.15f, 0.10f, FiscalPosture.BalancedWar);
        string changed = FinancialAiLogGate.Signature(1, "subsidy", 3, 0.50f, 0.20f, FiscalPosture.BalancedWar);

        AssertEqual(true, gate.ShouldLog(first));
        AssertEqual(false, gate.ShouldLog(repeat));
        AssertEqual(true, gate.ShouldLog(changed));
    }
}
