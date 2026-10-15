using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Strategic.Policies;
using static TestHarness;

// Stage E — need-driven policy selection (spec docs/superpowers/specs/2026-10-14-need-driven-policy-design.md).
internal static class NeedDrivenPolicyTests
{
    // Every record of Config/policies.dat (build 14703111; ImportPolicyData 211717):
    // id|ResearchDuration (years)|A = act, P = policy|ReferedChapter|prerequisite ids (+-joined).
    // Parsed from the file's layout: count line, then per record id, validscenarios,
    // prerequisite count + 5 slots, duration, IsAct, chapter, headline, tooltip, newspaper
    // flag, headline count + 5 slots, picture id, picture headline, content.
    private const string PoliciesDat =
        "0|0.068|P|0| 1|0.123|P|1|0 2|0.164|P|2|1 3|0.068|P|0| 4|0.082|P|0|3 5|0.123|P|1|4+75 6|0.164|P|2|5 " +
        "7|0.068|P|0| 8|0.082|P|0|7 9|0.123|P|1|8+76 10|0.164|P|2|9 11|0.068|P|0| 12|0.123|P|1|11 13|0.164|P|2|12 " +
        "14|0.205|P|3|13 15|0.068|P|0| 16|0.082|P|0|15 17|0.096|P|0|16 18|0.123|P|1|17+74 19|0.164|P|2|18 " +
        "20|0.055|A|0|0 21|0.068|A|0|0+20 22|0.082|A|1|1 23|0.110|A|2|2 24|0.055|A|0|0 25|0.068|A|1|0 26|0.082|A|1|1 " +
        "27|0.123|A|2|1+26 28|0.137|A|2|2+27 29|0.110|A|3|2+23 30|0.055|A|0|4+8 31|0.082|A|1|5 32|0.123|A|2|6+31 " +
        "33|0.123|A|1|12+34 34|0.082|A|1|12 35|0.055|A|0|7+11 36|0.027|A|0| 37|0.055|A|0|36 38|0.082|A|1|37 " +
        "39|0.082|A|1|12 40|0.123|A|2|12+39 41|0.055|A|0|11 42|0.110|A|2|13 43|0.055|A|0|11 44|0.123|A|2|12+34 " +
        "45|0.082|A|1|12 46|0.123|A|2|12+45 47|0.096|A|1|38 48|0.055|A|0|11 70|0.000|A|0| 71|0.000|A|0| 72|0.000|A|0| " +
        "73|0.000|A|0| 74|0.000|A|0| 75|0.000|A|0| 76|0.000|A|0| 77|0.000|A|0| 78|0.000|A|0| 79|0.000|A|0| " +
        "100|0.068|P|0| 101|0.123|P|1|100 102|0.164|P|2|101 103|0.068|P|0| 104|0.082|P|0|103 105|0.123|P|1|104+175 " +
        "106|0.164|P|2|105 107|0.068|P|0| 108|0.082|P|0|107 109|0.123|P|1|108+176 110|0.164|P|2|109 111|0.068|P|0| " +
        "112|0.123|P|1|111 113|0.164|P|2|112 114|0.205|P|3|113 115|0.068|P|0| 116|0.082|P|0|115 117|0.096|P|0|116 " +
        "118|0.123|P|1|117 119|0.164|P|2|118 120|0.055|A|0|100 121|0.068|A|0|100+120 122|0.082|A|1|101 " +
        "123|0.110|A|2|102 124|0.055|A|0|100 125|0.068|A|1|100 126|0.082|A|1|101 127|0.123|A|2|101+126 " +
        "128|0.137|A|2|102+127 129|0.137|A|3|102 130|0.110|A|2|106 131|0.055|A|1|104 132|0.082|A|2|109 " +
        "133|0.123|A|2|109+132 134|0.151|A|3|110+132 135|0.055|A|0|107+111 136|0.027|A|0| 137|0.068|A|1|136 " +
        "138|0.096|A|1|137 139|0.082|A|1|112 140|0.123|A|2|112+139 141|0.055|A|0|116 142|0.082|A|1|112+118 " +
        "143|0.055|A|0|111 144|0.082|A|1|112+118 145|0.082|A|1|112 146|0.123|A|2|112+145 147|0.055|A|0|111 " +
        "170|0.000|A|0| 171|0.000|A|0| 172|0.000|A|0| 173|0.000|A|0| 174|0.000|A|0| 175|0.000|A|0| 176|0.000|A|0| " +
        "177|0.000|A|0| 178|0.000|A|0| 179|0.000|A|0|";

    private sealed class DatRecord
    {
        public int Id;
        public float Duration;
        public bool IsAct;
        public int Chapter;
        public int[] Prerequisites;
    }

    private static readonly Dictionary<int, DatRecord> Dat = ParseDat();

    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("need-policy fixture has every policies.dat record", FixtureHasEveryRecord),
            ("need-policy catalog covers every non-grand policy and no grand one", CatalogCoversNonGrandPolicies),
            ("need-policy catalog weights and costs stay in unit range", CatalogValuesInUnitRange),
            ("need-policy catalog irreversible set is the scripted acts", CatalogIrreversibleSet),
            ("need-policy catalog side costs follow vanilla effects", CatalogSideCosts),
            ("need-policy catalog alliance follows the id range", CatalogAllianceFromId),
            ("need-policy eligibility accepts a clean candidate", EligibilityAcceptsCleanCandidate),
            ("need-policy eligibility rejects alliance out of range", EligibilityRejectsAllianceOutOfRange),
            ("need-policy eligibility rejects the other side's policy", EligibilityRejectsForeignPolicy),
            ("need-policy eligibility rejects grand policies", EligibilityRejectsGrand),
            ("need-policy eligibility filters in vanilla order", EligibilityFiltersInVanillaOrder),
            ("need-policy eligibility needs a free slot for non-acts only", EligibilitySlotForNonActsOnly),
            ("need-policy eligibility keeps the war start scripted", EligibilityKeepsWarStartScripted),
            ("need-policy credit need follows the fiscal posture", NeedCreditFollowsPosture),
            ("need-policy treasury need is the deficit share of spending", NeedTreasuryFromDeficit),
            ("need-policy manpower need from recruits and recruitable states", NeedManpowerFromRecruits),
            ("need-policy contract, morale and support needs", NeedContractMoraleSupport),
            ("need-policy recognition need is the intervention probability on both sides", NeedRecognitionBothSides),
            ("need-policy blockade need per side", NeedBlockadePerSide),
            ("need-policy industry and logistics needs", NeedIndustryAndLogistics),
            ("need-policy needs stay in unit range and drop non-finite input", NeedValuesBoundedAndFinite),
            ("need-policy need smoothing is a reproducible daily ema", NeedSmoothingIsDailyEma),
            ("need-policy emergencies force their need after smoothing", NeedEmergencyForcesNeed),
            ("need-policy project signals keep the old builder semantics", ProjectSignalsKeepBuilderSemantics),
            ("need-policy project signals fill the new needs", ProjectSignalsFillNewNeeds),
            ("need-policy credit crisis prefers war bonds and bank act", ScoreCreditCrisisPrefersCreditActs),
            ("need-policy manpower crisis prefers drafts", ScoreManpowerCrisisPrefersDrafts),
            ("need-policy history decides between equal needs", ScoreHistoryDecidesTies),
            ("need-policy irreversible acts wait for their need", ScoreIrreversibleGated),
            ("need-policy held irreversible acts are never started", LedgerNeverStartsHeldActs),
            ("need-policy support-costly acts lose when support is low", ScoreSupportCostWhenSupportLow),
            ("need-policy slot pressure lowers non-acts only", ScoreSlotPressureNonActsOnly),
            ("need-policy look-ahead lifts a prerequisite", ScoreLookAheadLiftsPrerequisite),
            ("need-policy profile prior follows the grand strategy", ScoreProfilePrior),
            ("need-policy scorer constants pinned", ScorerConstantsPinned),
            ("need-policy union opening prefers blockade over enrollment", ScoreUnionOpeningPrefersBlockade),
            ("need-policy csa opening prefers its trade and recognition over naval parity", ScoreCsaOpeningPrefersTradeAndRecognition),
            ("need-policy ledger picks the top and keeps it within the margin", LedgerHysteresis),
            ("need-policy ledger switches on an emergency need", LedgerEmergencySwitch),
            ("need-policy ledger replaces a pick that left the field", LedgerReplacesGonePick),
            ("need-policy ledger next eligible serves the pick first", LedgerNextEligible),
            ("need-policy ledger restores a saved pick", LedgerRestore),
            ("need-policy ledger describes its top candidates", LedgerDescribeTop),
            ("need-policy sidecar round-trips policy needs and pick", SidecarRoundTripsPolicyState),
            ("need-policy emergency protects only in-progress research", EmergencyProtectedIds),
            ("need-policy emergency restore gives back vanilla's list", EmergencyRestoreGivesBackList),
            ("need-policy fallback hold removes held acts and keeps vanilla's order", FallbackHoldRemovesHeldActs),
            ("need-policy fallback hold restore gives back vanilla's exact list", FallbackHoldRestoreIsExact),
            ("need-policy fallback hold leaves vanilla untouched without a plan or with selection off", FallbackHoldNeedsPlanAndFlag),
            ("need-policy fallback hold never filters the emergency list", FallbackHoldSkipsEmergency),
        });
    }

    private static Dictionary<int, DatRecord> ParseDat()
    {
        var result = new Dictionary<int, DatRecord>();
        foreach (string token in PoliciesDat.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = token.Split('|');
            var prerequisites = new List<int>();
            foreach (string pre in parts[4].Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries))
                prerequisites.Add(int.Parse(pre));
            var record = new DatRecord
            {
                Id = int.Parse(parts[0]),
                Duration = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                IsAct = parts[2] == "A",
                Chapter = int.Parse(parts[3]),
                Prerequisites = prerequisites.ToArray()
            };
            result[record.Id] = record;
        }
        return result;
    }

    private static bool IsGrandFixture(DatRecord record) => record.Duration <= 0f;

    private static IEnumerable<int> NonGrandIds()
    {
        foreach (var record in Dat.Values)
            if (!IsGrandFixture(record))
                yield return record.Id;
    }

    // ---------------------------------------------------------------- catalog

    private static void FixtureHasEveryRecord()
    {
        AssertEqual(117, Dat.Count, "policies.dat record count");
    }

    private static void CatalogCoversNonGrandPolicies()
    {
        int nonGrand = 0;
        foreach (var record in Dat.Values)
        {
            var effect = PolicyEffectCatalog.Get(record.Id);
            if (IsGrandFixture(record))
            {
                AssertTrue(effect == null, "grand policy " + record.Id + " has no catalog entry");
                continue;
            }
            nonGrand++;
            AssertTrue(effect != null, "catalog entry for policy " + record.Id);
            AssertEqual(record.Id, effect.PolicyId, "entry id");
        }
        AssertEqual(97, nonGrand, "non-grand policies in policies.dat (117 minus 20 grand)");
        for (int id = -1; id <= 200; id++)
            if (!Dat.ContainsKey(id))
                AssertTrue(PolicyEffectCatalog.Get(id) == null, "no catalog entry outside policies.dat: " + id);
    }

    private static void CatalogValuesInUnitRange()
    {
        foreach (int id in NonGrandIds())
        {
            var effect = PolicyEffectCatalog.Get(id);
            bool anyWeight = false;
            for (int k = 0; k < PolicyEffectCatalog.NeedKindCount; k++)
            {
                float w = effect.Weight((NeedKind)k);
                float c = effect.Cost((NeedKind)k);
                AssertTrue(w >= 0f && w <= 1f, "weight in [0,1] for " + id);
                AssertTrue(c >= 0f && c <= 1f, "cost in [0,1] for " + id);
                anyWeight |= w > 0f;
            }
            AssertTrue(anyWeight, "policy " + id + " meets at least one need");
        }
    }

    private static void CatalogIrreversibleSet()
    {
        var expected = new HashSet<int> { 33, 39, 40, 48, 130, 133, 134, 139, 140, 144, 147 };
        foreach (int id in NonGrandIds())
        {
            bool irreversible = PolicyEffectCatalog.Get(id).IrreversibleReason != null;
            AssertEqual(expected.Contains(id), irreversible, "irreversible flag for " + id);
        }
        AssertContains(PolicyEffectCatalog.Get(144).IrreversibleReason, "war", "Mexican intervention starts a war");
        AssertContains(PolicyEffectCatalog.Get(33).IrreversibleReason, "units", "USCT spawns units");
        AssertContains(PolicyEffectCatalog.Get(139).IrreversibleReason, "contract", "conscription resets contracts");
        AssertTrue(PolicyEffectCatalog.Get(42).IrreversibleReason == null, "Lt Gen Act is not gated");
    }

    private static void CatalogSideCosts()
    {
        AssertTrue(PolicyEffectCatalog.Get(23).Cost(NeedKind.Support) > 0f, "Bank Act I costs support (bankactsupport)");
        AssertTrue(PolicyEffectCatalog.Get(29).Cost(NeedKind.Support) > 0f, "Bank Act II costs support");
        AssertTrue(PolicyEffectCatalog.Get(123).Cost(NeedKind.Support) > 0f, "CSA Bank Act costs support");
        AssertTrue(PolicyEffectCatalog.Get(20).Cost(NeedKind.Treasury) > 0f, "Print Notes costs public wealth");
        AssertTrue(PolicyEffectCatalog.Get(120).Cost(NeedKind.Treasury) > 0f, "CSA Print Notes costs public wealth");
        AssertTrue(PolicyEffectCatalog.Get(45).Cost(NeedKind.Credit) > 0f, "Bounties cost money per recruit");
        AssertTrue(PolicyEffectCatalog.Get(146).Cost(NeedKind.Credit) > 0f, "CSA Bounties II cost money per recruit");
        AssertTrue(PolicyEffectCatalog.Get(24).Cost(NeedKind.Recognition) > 0f, "Tariff Act lowers own intervention level");
        AssertTrue(PolicyEffectCatalog.Get(143).Cost(NeedKind.Recognition) > 0f, "Letters of Marque lower own intervention level");
        AssertTrue(PolicyEffectCatalog.Get(39).Cost(NeedKind.Morale) > 0f, "Enrollment costs national morale");
        AssertTrue(PolicyEffectCatalog.Get(22).Weight(NeedKind.Credit) >= 0.7f, "War Bonds improve credit");
        AssertTrue(PolicyEffectCatalog.Get(139).Weight(NeedKind.Manpower) >= 0.7f, "Conscription raises men");
        AssertTrue(PolicyEffectCatalog.Get(41).Weight(NeedKind.Blockade) >= 0.7f, "Legal Blockade blockades");
        AssertTrue(PolicyEffectCatalog.Get(115).Weight(NeedKind.Recognition) > 0f, "Diplomacy raises recognition");
    }

    private static void CatalogAllianceFromId()
    {
        AssertEqual(0, PolicyEffectCatalog.AllianceOf(0), "Union low id");
        AssertEqual(0, PolicyEffectCatalog.AllianceOf(48), "Union high id");
        AssertEqual(1, PolicyEffectCatalog.AllianceOf(100), "CSA low id");
        AssertEqual(1, PolicyEffectCatalog.AllianceOf(147), "CSA high id");
        AssertEqual(-1, PolicyEffectCatalog.AllianceOf(-1), "negative");
        AssertEqual(-1, PolicyEffectCatalog.AllianceOf(200), "out of range");
        foreach (int id in NonGrandIds())
            AssertEqual(id < 100 ? 0 : 1, PolicyEffectCatalog.AllianceOf(id), "alliance of " + id);
    }

    // ---------------------------------------------------------------- eligibility

    internal static PolicyCandidateFacts Facts(int id, int currentChapter = 1)
    {
        var record = Dat[id];
        return new PolicyCandidateFacts
        {
            PolicyId = id,
            IsGrand = IsGrandFixture(record),
            IsAct = record.IsAct,
            ActiveOrDone = false,
            ReferedChapter = record.Chapter,
            CurrentChapter = currentChapter,
            HasPrePolicies = true,
            Deactivated = false,
            Blocked = false,
            AvailableInScenario = true,
            SlotFree = true,
            ResearchDuration = record.Duration
        };
    }

    private static void EligibilityAcceptsCleanCandidate()
    {
        AssertTrue(PolicyEligibility.IsEligible(Facts(22), 0, out string reason), "War Bonds eligible");
        AssertEqual("eligible", reason, "reason");
        AssertTrue(PolicyEligibility.IsEligible(Facts(139), 1, out _), "Conscription eligible");
    }

    private static void EligibilityRejectsAllianceOutOfRange()
    {
        AssertFalse(PolicyEligibility.IsEligible(Facts(22), 2, out string reason), "Europe never researches");
        AssertEqual("alliance", reason, "reason");
        AssertFalse(PolicyEligibility.IsEligible(Facts(22), -1, out reason), "negative alliance");
        AssertEqual("alliance", reason, "reason");
    }

    private static void EligibilityRejectsForeignPolicy()
    {
        AssertFalse(PolicyEligibility.IsEligible(Facts(122), 0, out string reason), "Union cannot research a CSA policy");
        AssertEqual("foreign", reason, "reason");
        AssertFalse(PolicyEligibility.IsEligible(Facts(22), 1, out reason), "CSA cannot research a Union policy");
        AssertEqual("foreign", reason, "reason");
    }

    private static void EligibilityRejectsGrand()
    {
        AssertFalse(PolicyEligibility.IsEligible(Facts(75), 0, out string reason), "grand act (duration 0)");
        AssertEqual("grand", reason, "reason");
        var flagged = Facts(22);
        flagged.IsGrand = true;
        AssertFalse(PolicyEligibility.IsEligible(flagged, 0, out reason), "member of Policy.grandpolicies");
        AssertEqual("grand", reason, "reason");
    }

    private static void EligibilityFiltersInVanillaOrder()
    {
        var f = Facts(22);
        f.ActiveOrDone = true;
        f.CurrentChapter = 0;
        AssertFalse(PolicyEligibility.IsEligible(f, 0, out string reason), "active first");
        AssertEqual("active", reason, "reason");

        f = Facts(22, currentChapter: 0);
        AssertFalse(PolicyEligibility.IsEligible(f, 0, out reason), "chapter 1 policy in chapter 0");
        AssertEqual("chapter", reason, "reason");

        f = Facts(22);
        f.HasPrePolicies = false;
        AssertFalse(PolicyEligibility.IsEligible(f, 0, out reason), "prerequisites missing");
        AssertEqual("prerequisites", reason, "reason");

        f = Facts(22);
        f.Deactivated = true;
        AssertFalse(PolicyEligibility.IsEligible(f, 0, out reason), "Policies.IsDeactivated");
        AssertEqual("deactivated", reason, "reason");

        f = Facts(22);
        f.Blocked = true;
        AssertFalse(PolicyEligibility.IsEligible(f, 0, out reason), "blocked");
        AssertEqual("blocked", reason, "reason");

        f = Facts(22);
        f.AvailableInScenario = false;
        AssertFalse(PolicyEligibility.IsEligible(f, 0, out reason), "scenario");
        AssertEqual("scenario", reason, "reason");
    }

    private static void EligibilitySlotForNonActsOnly()
    {
        var policy = Facts(12);
        policy.SlotFree = false;
        AssertFalse(PolicyEligibility.IsEligible(policy, 0, out string reason), "non-act at the slot cap (vanilla stall)");
        AssertEqual("slots", reason, "reason");

        var act = Facts(22);
        act.SlotFree = false;
        AssertTrue(PolicyEligibility.IsEligible(act, 0, out _), "acts take no slot");
    }

    private static void EligibilityKeepsWarStartScripted()
    {
        AssertFalse(PolicyEligibility.IsEligible(Facts(36, currentChapter: 0), 0, out string reason), "Militia Act I before the war");
        AssertEqual("scripted-war-start", reason, "reason");
        AssertTrue(PolicyEligibility.IsEligible(Facts(36, currentChapter: 1), 0, out _), "after the war starts it is a normal act");
        AssertTrue(PolicyEligibility.IsEligible(Facts(136, currentChapter: 0), 1, out _), "CSA Militia Act I is not scripted");
    }

    // ---------------------------------------------------------------- need vector

    private static NeedSnapshot Snapshot(int alliance = 0)
    {
        return new NeedSnapshot
        {
            Alliance = alliance,
            Era = EraStage.Operational1862,
            FiscalPosture = FiscalPosture.Expansion,
            NationalMorale = 1f,
            AverageSupport = 1f,
            RecruitableStates = 20,
            CsaBlockadeRatio = 1f
        };
    }

    private static void NeedCreditFollowsPosture()
    {
        var s = Snapshot();
        s.FiscalPosture = FiscalPosture.EmergencySolvency;
        AssertEqual(1f, CampaignNeedBuilder.Raw(s)[NeedKind.Credit], "emergency solvency");
        s.FiscalPosture = FiscalPosture.CreditDefense;
        AssertEqual(0.75f, CampaignNeedBuilder.Raw(s)[NeedKind.Credit], "credit defense");
        s.FiscalPosture = FiscalPosture.BalancedWar;
        AssertEqual(0.25f, CampaignNeedBuilder.Raw(s)[NeedKind.Credit], "balanced war");
        s.FiscalPosture = FiscalPosture.Expansion;
        AssertEqual(0f, CampaignNeedBuilder.Raw(s)[NeedKind.Credit], "expansion");
    }

    private static void NeedTreasuryFromDeficit()
    {
        var s = Snapshot();
        s.AnnualSpending = 100f;
        s.AnnualBalance = -25f;
        AssertNear(0.25f, CampaignNeedBuilder.Raw(s)[NeedKind.Treasury], 0.001f, "deficit a quarter of spending");
        s.AnnualBalance = 40f;
        AssertEqual(0f, CampaignNeedBuilder.Raw(s)[NeedKind.Treasury], "surplus");
        s.AnnualBalance = -500f;
        AssertEqual(1f, CampaignNeedBuilder.Raw(s)[NeedKind.Treasury], "deficit beyond spending clamps");
        s.AnnualSpending = 0f;
        AssertEqual(0f, CampaignNeedBuilder.Raw(s)[NeedKind.Treasury], "no spending read");
    }

    private static void NeedManpowerFromRecruits()
    {
        var s = Snapshot();
        s.TroopsFielded = 100f;
        s.RecruitsAvailable = 50f;
        AssertEqual(0f, CampaignNeedBuilder.Raw(s)[NeedKind.Manpower], "half the army again in reserve");
        s.RecruitsAvailable = 12.5f;
        AssertNear(0.5f, CampaignNeedBuilder.Raw(s)[NeedKind.Manpower], 0.001f, "an eighth in reserve");
        s.RecruitsAvailable = 0f;
        AssertEqual(1f, CampaignNeedBuilder.Raw(s)[NeedKind.Manpower], "no recruits");

        s = Snapshot();
        s.TroopsFielded = 0f;
        s.RecruitsAvailable = 0f;
        AssertEqual(0f, CampaignNeedBuilder.Raw(s)[NeedKind.Manpower], "no army yet");
        s.RecruitableStates = 4;
        AssertEqual(1f, CampaignNeedBuilder.Raw(s)[NeedKind.Manpower], "vanilla recruiting-emergency trigger (4 states)");
        s.RecruitableStates = 6;
        AssertNear(0.5f, CampaignNeedBuilder.Raw(s)[NeedKind.Manpower], 0.001f, "six states");
        s.RecruitableStates = 8;
        AssertEqual(0f, CampaignNeedBuilder.Raw(s)[NeedKind.Manpower], "eight states");
        s.RecruitableStates = -1;
        AssertEqual(0f, CampaignNeedBuilder.Raw(s)[NeedKind.Manpower], "unread state count");
    }

    private static void NeedContractMoraleSupport()
    {
        var s = Snapshot();
        s.TroopsFielded = 100f;
        s.RecruitsAvailable = 100f;
        s.ContractExpiries = 10f;
        s.NationalMorale = 0.7f;
        s.AverageSupport = 0.4f;
        var v = CampaignNeedBuilder.Raw(s);
        AssertNear(0.5f, v[NeedKind.ContractStability], 0.001f, "a tenth of the army expiring");
        AssertNear(0.3f, v[NeedKind.Morale], 0.001f, "morale");
        AssertNear(0.6f, v[NeedKind.Support], 0.001f, "support");
    }

    private static void NeedRecognitionBothSides()
    {
        var union = Snapshot(0);
        union.InterventionProbability = 0.4f;
        var csa = Snapshot(1);
        csa.InterventionProbability = 0.4f;
        AssertNear(0.4f, CampaignNeedBuilder.Raw(union)[NeedKind.Recognition], 0.001f, "Union: threat");
        AssertNear(0.4f, CampaignNeedBuilder.Raw(csa)[NeedKind.Recognition], 0.001f, "CSA: opportunity");
    }

    private static void NeedBlockadePerSide()
    {
        var csa = Snapshot(1);
        csa.OwnBlockadeRatio = 0.6f;
        csa.CsaBlockadeRatio = 0.6f;
        AssertNear(0.6f, CampaignNeedBuilder.Raw(csa)[NeedKind.Blockade], 0.001f, "CSA: own ports closing");
        var union = Snapshot(0);
        union.OwnBlockadeRatio = 0f;
        union.CsaBlockadeRatio = 0.6f;
        AssertNear(0.4f, CampaignNeedBuilder.Raw(union)[NeedKind.Blockade], 0.001f, "Union: blockade still to do");
    }

    private static void NeedIndustryAndLogistics()
    {
        var s = Snapshot();
        s.TroopsInRecruitment = 100f;
        s.WeaponStock = 25f;
        s.PeakTransportUse = 0.8f;
        s.SupplyPressure = 0.3f;
        var v = CampaignNeedBuilder.Raw(s);
        AssertNear(0.75f, v[NeedKind.Industry], 0.001f, "a quarter of the recruits armed");
        AssertNear(0.5f, v[NeedKind.Logistics], 0.001f, "transport at 80 %");
        AssertNear(0.3f, v[NeedKind.Supply], 0.001f, "front supply pressure");
        AssertNear(0.3f, v[NeedKind.Agriculture], 0.001f, "agriculture feeds the front");
        s.TroopsInRecruitment = 0f;
        AssertEqual(0f, CampaignNeedBuilder.Raw(s)[NeedKind.Industry], "nobody to arm");
    }

    private static void NeedValuesBoundedAndFinite()
    {
        var s = Snapshot(1);
        s.NationalMorale = float.NaN;
        s.AverageSupport = -3f;
        s.InterventionProbability = float.PositiveInfinity;
        s.OwnBlockadeRatio = 4f;
        s.AnnualBalance = float.NaN;
        s.AnnualSpending = 10f;
        s.PeakTransportUse = float.NaN;
        var v = CampaignNeedBuilder.Raw(s);
        for (int k = 0; k < PolicyEffectCatalog.NeedKindCount; k++)
        {
            float value = v[(NeedKind)k];
            AssertFinite(value, ((NeedKind)k).ToString());
            AssertTrue(value >= 0f && value <= 1f, "unit range " + (NeedKind)k);
        }
        AssertEqual(0f, v[NeedKind.Morale], "NaN morale reads as no need");
        AssertEqual(1f, v[NeedKind.Blockade], "blockade clamps");
        AssertEqual(0f, CampaignNeedBuilder.Raw(null)[NeedKind.Credit], "null snapshot");
    }

    private static void NeedSmoothingIsDailyEma()
    {
        AssertEqual(14, NeedVector.EmaDays, "EMA days pinned");
        var low = CampaignNeedBuilder.Raw(Snapshot());
        var s = Snapshot();
        s.FiscalPosture = FiscalPosture.EmergencySolvency;
        var high = CampaignNeedBuilder.Raw(s);

        AssertEqual(1f, CampaignNeedBuilder.Smooth(null, high, 1)[NeedKind.Credit], "first reading taken as is");
        float alpha = 2f / (NeedVector.EmaDays + 1);
        AssertNear(alpha, CampaignNeedBuilder.Smooth(low, high, 1)[NeedKind.Credit], 0.0001f, "one day");
        var twoSteps = CampaignNeedBuilder.Smooth(CampaignNeedBuilder.Smooth(low, high, 1), high, 1);
        AssertNear(twoSteps[NeedKind.Credit], CampaignNeedBuilder.Smooth(low, high, 2)[NeedKind.Credit], 0.0001f, "two days at once");
        AssertNear(alpha, CampaignNeedBuilder.Smooth(low, high, 0)[NeedKind.Credit], 0.0001f, "same-day rerun counts one day");
        var converged = CampaignNeedBuilder.Smooth(low, high, 200);
        AssertNear(1f, converged[NeedKind.Credit], 0.001f, "converges");
        AssertEqual(0f, low[NeedKind.Credit], "inputs are not mutated");
    }

    private static void NeedEmergencyForcesNeed()
    {
        var calm = CampaignNeedBuilder.Raw(Snapshot());
        var s = Snapshot();
        s.EmergencyCredit = true;
        var v = CampaignNeedBuilder.Next(calm, s, 1);
        AssertEqual(1f, v[NeedKind.Credit], "credit emergency (personality 101)");
        AssertEqual(0f, v[NeedKind.Manpower], "manpower untouched");
        s.EmergencyCredit = false;
        s.EmergencyManpower = true;
        v = CampaignNeedBuilder.Next(calm, s, 1);
        AssertEqual(1f, v[NeedKind.Manpower], "recruiting emergency (personality 100)");
    }

    private static void ProjectSignalsKeepBuilderSemantics()
    {
        var neutral = CampaignNeedBuilder.ToProjectSignals(null, null);
        AssertEqual(0.5f, neutral.BlockadePressure, "neutral blockade before the first review");
        AssertEqual(0.5f, neutral.PortViability, "neutral port viability");
        AssertEqual(0f, neutral.CreditStress, "neutral credit");

        var s = Snapshot(0);
        s.Era = EraStage.Amateur1861;
        s.FiscalPosture = FiscalPosture.EmergencySolvency;
        var signals = CampaignNeedBuilder.ToProjectSignals(s, CampaignNeedBuilder.Next(null, s, 1));
        AssertEqual(1f, signals.CreditStress, "posture maps to credit stress");
        AssertEqual(EraStage.Amateur1861, signals.Era, "era");
        AssertEqual(FiscalPosture.EmergencySolvency, signals.FiscalPosture, "posture");

        s = Snapshot(1);
        s.Era = EraStage.TotalWar1864;
        s.FiscalPosture = FiscalPosture.CreditDefense;
        s.TroopsFielded = 100f;
        s.RecruitsAvailable = 5f;
        signals = CampaignNeedBuilder.ToProjectSignals(s, CampaignNeedBuilder.Next(null, s, 1));
        AssertNear(0.8f, signals.ManpowerStress, 0.001f, "manpower stress");
        AssertNear(0.62f, signals.LateWarCollapseRisk, 0.001f, "late war collapse risk from own credit and manpower only");

        s = Snapshot(1);
        s.InterventionProbability = 2f;
        s.OwnBlockadeRatio = -2f;
        signals = CampaignNeedBuilder.ToProjectSignals(s, CampaignNeedBuilder.Next(null, s, 1));
        AssertEqual(1f, signals.RecognitionWindow, "recognition clamps");
        AssertEqual(1f, signals.PortViability, "port viability clamps");

        s = Snapshot(0);
        s.SupplyPressure = float.NaN;
        s.PeakTransportUse = 0.88f;
        signals = CampaignNeedBuilder.ToProjectSignals(s, CampaignNeedBuilder.Next(null, s, 1));
        AssertNear(0.7f, signals.LogisticsTempoNeed, 0.001f, "non-finite supply side ignored");
    }

    private static void ProjectSignalsFillNewNeeds()
    {
        var s = Snapshot(1);
        s.TroopsFielded = 100f;
        s.RecruitsAvailable = 12.5f;
        s.AverageSupport = 0.6f;
        s.SupplyPressure = 0.4f;
        s.TroopsInRecruitment = 10f;
        s.WeaponStock = 5f;
        s.OwnBlockadeRatio = 0.8f;
        s.CsaBlockadeRatio = 0.8f;
        var v = CampaignNeedBuilder.Next(null, s, 1);
        var signals = CampaignNeedBuilder.ToProjectSignals(s, v);
        AssertNear(0.5f, signals.ManpowerStress, 0.001f, "manpower stress");
        AssertNear(0.4f, signals.CivilOrderRisk, 0.001f, "civil order risk from support");
        AssertNear(0.4f, signals.AgricultureFoodStress, 0.001f, "agriculture");
        AssertEqual(0f, signals.OffensiveTempoNeed, "no fog-honest enemy strength source yet (stage G)");
        AssertNear(0.5f, signals.WeaponDeficit, 0.001f, "weapon deficit from own stock");
        AssertNear(0.5f, signals.IndustryGap, 0.001f, "industry gap");
        AssertNear(0.8f, signals.BlockadePressure, 0.001f, "CSA blockade pressure");
        AssertNear(0.2f, signals.PortViability, 0.001f, "CSA ports mostly closed");
        AssertEqual(0f, signals.ArtilleryDeficit, "no fog-honest artillery source");
        AssertEqual(0f, signals.NavalDeficit, "no fog-honest naval source");
    }

    // ---------------------------------------------------------------- scorer

    private static readonly int[] UnionHistorical =
        { 35, 37, 38, 24, 47, 25, 33, 34, 42, 45, 46, 23, 39, 40, 29, 22, 26, 27, 39, 40, 41, 29, 7, 11, 0, 8, 12, 15, 9, 13, 1, 2, 3, 16, 10, 14, 16, 5, 17, 6, 18, 19 };
    private static readonly int[] CsaHistorical =
        { 136, 125, 143, 142, 137, 120, 131, 138, 139, 140, 121, 126, 127, 129, 145, 146, 103, 111, 115, 104, 105, 112, 100, 107, 101, 108, 116, 117, 102, 118, 113, 109, 106, 114, 119, 110 };

    private static readonly Func<int, IEnumerable<int>> NoDependents = id => new int[0];

    private static NeedVector Needs(params (NeedKind kind, float value)[] values)
    {
        var raw = new float[PolicyEffectCatalog.NeedKindCount];
        foreach (var (kind, value) in values)
            raw[(int)kind] = value;
        return NeedVector.FromArray(raw);
    }

    private static PolicyScore Score(int id, NeedVector need, int[] personality, int used = 3, int max = 7,
        Func<int, IEnumerable<int>> dependents = null, int chapter = 2)
    {
        int alliance = PolicyEffectCatalog.AllianceOf(id);
        var profile = GrandStrategyRegistry.Resolve(alliance, EraStage.Operational1862);
        return PolicyNeedScorer.Score(Facts(id, chapter), need, personality, profile, used, max, dependents ?? NoDependents);
    }

    private static void ScoreCreditCrisisPrefersCreditActs()
    {
        var need = Needs((NeedKind.Credit, 1f), (NeedKind.Treasury, 0.6f));
        float warBonds = Score(22, need, UnionHistorical).Total;
        float bankAct = Score(23, need, UnionHistorical).Total;
        float diplomacy = Score(15, need, UnionHistorical).Total;
        AssertTrue(warBonds > diplomacy, "War Bonds over Diplomacy I");
        AssertTrue(bankAct > diplomacy, "Bank Act over Diplomacy I");

        float csaWarBonds = Score(122, need, CsaHistorical).Total;
        float csaDiplomacy = Score(115, need, CsaHistorical).Total;
        AssertTrue(csaWarBonds > csaDiplomacy, "CSA War Bonds (absent from its Historical list) over Diplomacy I");
    }

    private static void ScoreManpowerCrisisPrefersDrafts()
    {
        var need = Needs((NeedKind.Manpower, 1f), (NeedKind.ContractStability, 0.2f), (NeedKind.Morale, 0.2f));
        float conscription = Score(139, need, CsaHistorical).Total;
        float diplomacy = Score(115, need, CsaHistorical).Total;
        AssertTrue(conscription > diplomacy, "Conscription over Diplomacy I");
        AssertFalse(Score(139, need, CsaHistorical).Held, "manpower crisis opens the contract-reset gate");

        float enrollment = Score(39, need, UnionHistorical).Total;
        float unionDiplomacy = Score(15, need, UnionHistorical).Total;
        AssertTrue(enrollment > unionDiplomacy, "Enrollment over Diplomacy I");
    }

    private static void ScoreHistoryDecidesTies()
    {
        var need = Needs((NeedKind.Treasury, 0.5f));
        float tariffPreferred = Score(24, need, new[] { 24 }).Total - Score(26, need, new[] { 24 }).Total;
        float revenuePreferred = Score(26, need, new[] { 26 }).Total - Score(24, need, new[] { 26 }).Total;
        AssertTrue(tariffPreferred > 0f, "listed Tariff Act wins");
        AssertTrue(revenuePreferred > 0f, "listed Revenue Act wins");
        var score = Score(24, need, new[] { 24, 26 });
        AssertNear(PolicyNeedScorer.PersonalityWeight, score.Personality, 0.0001f, "first in the list gets the full weight");
        AssertNear(PolicyNeedScorer.PersonalityWeight * 0.5f, Score(26, need, new[] { 24, 26 }).Personality, 0.0001f, "second of two gets half");
        AssertEqual(0f, Score(26, need, new int[0]).Personality, "not listed");
    }

    private static void ScoreIrreversibleGated()
    {
        var low = Needs((NeedKind.Recognition, 0.3f));
        var high = Needs((NeedKind.Recognition, 0.8f));
        AssertTrue(Score(144, low, CsaHistorical).Held, "Mexico held at low need");
        AssertFalse(Score(144, high, CsaHistorical).Held, "Mexico allowed above the gate");
        AssertTrue(Score(134, low, CsaHistorical).Held, "CSA emancipation held");
        AssertFalse(Score(22, low, UnionHistorical).Held, "reversible act never held");
    }

    private static void ScoreSupportCostWhenSupportLow()
    {
        var supportive = Needs((NeedKind.Credit, 0.8f), (NeedKind.Support, 0.1f));
        var restless = Needs((NeedKind.Credit, 0.8f), (NeedKind.Support, 0.9f));
        float calm = Score(23, supportive, UnionHistorical).SideCost;
        float angry = Score(23, restless, UnionHistorical).SideCost;
        AssertTrue(angry < calm, "Bank Act costs more when support is low");
        AssertNear(-0.3f * 0.9f, angry, 0.0001f, "support cost × support need");
        AssertTrue(Score(22, restless, UnionHistorical).Total > Score(23, restless, UnionHistorical).Total, "War Bonds (no support cost) beats Bank Act when support is low");
    }

    private static void ScoreSlotPressureNonActsOnly()
    {
        var need = Needs((NeedKind.Manpower, 0.5f));
        var policy = Score(12, need, UnionHistorical, used: 6, max: 7);
        AssertNear(-PolicyNeedScorer.SlotPressureWeight * (6f / 7f) * (6f / 7f), policy.SlotPressure, 0.0001f, "non-act");
        AssertEqual(0f, Score(22, need, UnionHistorical, used: 6, max: 7).SlotPressure, "act takes no slot");
        AssertEqual(0f, Score(12, need, UnionHistorical, used: 0, max: 7).SlotPressure, "empty slots");
        AssertEqual(0f, Score(12, need, UnionHistorical, used: 3, max: 0).SlotPressure, "unread slot cap");
    }

    private static void ScoreLookAheadLiftsPrerequisite()
    {
        var need = Needs((NeedKind.Credit, 1f), (NeedKind.Treasury, 0.6f));
        Func<int, IEnumerable<int>> deps = id => id == 1 ? new[] { 22 } : new int[0];
        var withChain = Score(1, need, new int[0], dependents: deps);
        var alone = Score(1, need, new int[0]);
        float warBondsNeed = 0.8f * 1f + 0.2f * 0.6f;
        AssertNear(PolicyNeedScorer.LookAheadWeight * warBondsNeed / 2f, withChain.LookAhead, 0.0001f, "direct dependent at depth 1");
        AssertEqual(0f, alone.LookAhead, "no dependents");
        AssertTrue(withChain.Total > Score(15, need, new int[0]).Total, "Funding II (War Bonds prerequisite) over Diplomacy I");

        Func<int, IEnumerable<int>> chain = id => id == 1 ? new[] { 2 } : id == 2 ? new[] { 23 } : new int[0];
        float bankActNeed = 0.8f * 1f + 0.6f * 0.6f;
        float fundingIIINeed = 0.6f * 0.6f + 0.3f * 1f;
        float expected = PolicyNeedScorer.LookAheadWeight * Math.Max(fundingIIINeed / 2f, bankActNeed / 3f);
        AssertNear(expected, Score(1, need, new int[0], dependents: chain).LookAhead, 0.0001f, "depth 2 discounted");

        Func<int, IEnumerable<int>> cycle = id => new[] { id };
        AssertFinite(Score(1, need, new int[0], dependents: cycle).LookAhead, "cycle terminates at the depth cap");
    }

    private static void ScoreProfilePrior()
    {
        var none = Needs();
        var csaEarly = GrandStrategyRegistry.Resolve(1, EraStage.Amateur1861);
        var diplomacy = PolicyNeedScorer.Score(Facts(115, 0), none, new int[0], csaEarly, 0, 7, NoDependents);
        AssertNear(PolicyNeedScorer.ProfileWeight * csaEarly.WeightFor(StrategyTag.ForeignRecognition) * 0.4f, diplomacy.Profile, 0.0001f,
            "CSA early profile values recognition");
        var unionEarly = GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861);
        var blockade = PolicyNeedScorer.Score(Facts(41, 0), none, new int[0], unionEarly, 0, 7, NoDependents);
        AssertTrue(blockade.Profile > 0f, "Union Anaconda profile values Legal Blockade");
        AssertEqual(0f, PolicyNeedScorer.Score(Facts(41, 0), none, new int[0], null, 0, 7, NoDependents).Profile, "no profile");
    }

    private static void ScorerConstantsPinned()
    {
        AssertEqual(0.7f, PolicyNeedScorer.PersonalityWeight, "personality");
        AssertEqual(0.3f, PolicyNeedScorer.ProfileWeight, "profile");
        AssertEqual(0.5f, PolicyNeedScorer.LookAheadWeight, "look-ahead");
        AssertEqual(3, PolicyNeedScorer.LookAheadDepth, "look-ahead depth");
        AssertEqual(0.4f, PolicyNeedScorer.SlotPressureWeight, "slot pressure");
        AssertEqual(1.0f, PolicyNeedScorer.DurationWeight, "duration");
        AssertEqual(0.7f, PolicyNeedScorer.IrreversibleNeedGate, "irreversible gate");
        AssertEqual(0.25f, PolicyPlanLedger.SwitchMargin, "switch margin");
        AssertEqual(0.85f, PolicyPlanLedger.EmergencyNeedThreshold, "emergency threshold");

        var need = Needs((NeedKind.Treasury, 0.5f));
        var score = Score(26, need, new int[0]);
        AssertNear(-PolicyNeedScorer.DurationWeight * 0.082f * 0.5f, score.Duration, 0.0001f, "duration × max need");
        AssertNear(score.Need + score.Personality + score.Profile + score.LookAhead + score.SlotPressure + score.Duration + score.SideCost,
            score.Total, 0.0001f, "total is the sum of its terms");
    }

    // ---------------------------------------------------------------- ledger

    private static PolicyScore Scored(int id, float total) => new PolicyScore { PolicyId = id, Total = total };

    private static List<PolicyScore> Field(params PolicyScore[] scores) => new List<PolicyScore>(scores);

    private static void LedgerHysteresis()
    {
        var calm = Needs((NeedKind.Credit, 0.5f));
        var ledger = new PolicyPlanLedger();
        AssertEqual(-1, ledger.CurrentPick, "empty ledger");
        AssertTrue(ledger.Update(Field(Scored(1, 0.9f), Scored(2, 1.0f)), calm, calm), "first pick is a change");
        AssertEqual(2, ledger.CurrentPick, "top");
        AssertFalse(ledger.Update(Field(Scored(1, 1.2f), Scored(2, 1.0f)), calm, calm), "challenger within margin");
        AssertEqual(2, ledger.CurrentPick, "kept");
        AssertEqual(2, ledger.Ranked[0].PolicyId, "pick ranks first");
        AssertEqual(1, ledger.Ranked[1].PolicyId, "then the rest");
        AssertTrue(ledger.Update(Field(Scored(1, 1.3f), Scored(2, 1.0f)), calm, calm), "challenger beats the margin");
        AssertEqual(1, ledger.CurrentPick, "switched");
        ledger.Update(Field(), calm, calm);
        AssertEqual(-1, ledger.CurrentPick, "nothing eligible");
        AssertEqual(0, ledger.Ranked.Count, "empty ranking");
    }

    private static void LedgerEmergencySwitch()
    {
        var ledger = new PolicyPlanLedger();
        var before = Needs((NeedKind.Credit, 0.5f));
        var after = Needs((NeedKind.Credit, 0.9f));
        ledger.Update(Field(Scored(1, 1.0f), Scored(2, 0.9f)), before, before);
        AssertEqual(1, ledger.CurrentPick, "calm pick");
        AssertTrue(ledger.Update(Field(Scored(1, 1.0f), Scored(2, 1.1f)), after, before), "need crossed the emergency threshold");
        AssertEqual(2, ledger.CurrentPick, "switched without the margin");
        AssertFalse(ledger.Update(Field(Scored(1, 1.2f), Scored(2, 1.1f)), after, after), "already above: margin applies again");
        AssertEqual(2, ledger.CurrentPick, "kept");
        ledger.Update(Field(Scored(1, 1.0f), Scored(2, 1.1f)), after, null);
        AssertEqual(2, ledger.CurrentPick, "no previous vector is not a crossing");
    }

    private static void LedgerReplacesGonePick()
    {
        var calm = Needs();
        var ledger = new PolicyPlanLedger();
        ledger.Update(Field(Scored(1, 1.0f), Scored(2, 0.9f)), calm, calm);
        ledger.Update(Field(Scored(2, 0.9f), Scored(3, 0.4f)), calm, calm);
        AssertEqual(2, ledger.CurrentPick, "researched pick replaced by the top");
    }

    private static void LedgerNextEligible()
    {
        var calm = Needs();
        var ledger = new PolicyPlanLedger();
        AssertEqual(-1, ledger.NextEligible(id => true), "empty");
        ledger.Update(Field(Scored(1, 1.0f), Scored(2, 0.9f), Scored(3, 0.5f)), calm, calm);
        ledger.Update(Field(Scored(1, 1.0f), Scored(2, 1.2f), Scored(3, 0.5f)), calm, calm);
        AssertEqual(1, ledger.NextEligible(id => true), "pick first even when outscored within the margin");
        AssertEqual(2, ledger.NextEligible(id => id != 1), "then by score");
        AssertEqual(3, ledger.NextEligible(id => id == 3), "skips ineligible");
        AssertEqual(-1, ledger.NextEligible(id => false), "nothing passes");
    }

    private static void LedgerRestore()
    {
        var calm = Needs();
        var ledger = new PolicyPlanLedger();
        ledger.Restore(5);
        AssertEqual(5, ledger.CurrentPick, "restored");
        ledger.Update(Field(Scored(5, 1.0f), Scored(6, 1.2f)), calm, calm);
        AssertEqual(5, ledger.CurrentPick, "restored pick keeps its hysteresis");
        ledger.Restore(-7);
        AssertEqual(-1, ledger.CurrentPick, "invalid restores to none");
    }

    private static void EmergencyProtectedIds()
    {
        var personality = new List<int> { 20, 21, 22 };
        var ids = PolicyListOverride.ProtectedIds(new[] { 12, 22, 31, 12, 40 }, new[] { 0.3f, 0.5f, 1f, 0.3f, 0.99f }, personality);
        AssertEqual(2, ids.Count, "in progress, not listed, distinct");
        AssertEqual(12, ids[0], "first");
        AssertEqual(40, ids[1], "second");
        AssertEqual(0, PolicyListOverride.ProtectedIds(new int[0], new float[0], personality).Count, "nothing in research");
        AssertEqual(0, PolicyListOverride.ProtectedIds(null, null, personality).Count, "null research");
        AssertEqual(1, PolicyListOverride.ProtectedIds(new[] { 12 }, new[] { 0.1f }, null).Count, "null list");
        AssertEqual(1, PolicyListOverride.ProtectedIds(new[] { 12, 13 }, new[] { 0.1f }, personality).Count, "mismatched lengths read the common prefix");
    }

    private static void LedgerDescribeTop()
    {
        var ledger = new PolicyPlanLedger();
        AssertEqual("-", ledger.DescribeTop(3), "empty");
        ledger.Update(Field(
            new PolicyScore { PolicyId = 22, Total = 1.5f, Need = 0.9f, Personality = 0.5f, Profile = 0.1f, LookAhead = 0f, SideCost = -0.05f, Duration = 0.05f },
            Scored(15, 0.4f), Scored(7, 0.3f), Scored(8, 0.2f)), Needs(), Needs());
        AssertEqual("22:1.50(0.90/0.50/0.10/0.00/0.00) 15:0.40(0.00/0.00/0.00/0.00/0.00) 7:0.30(0.00/0.00/0.00/0.00/0.00)",
            ledger.DescribeTop(3), "top three, pick first");
    }

    // Ported from the deleted GrandStrategyPolicyScorer tests (StrategicMapTests): the
    // Union's Anaconda opening, with the whole blockade still to do (CSA ratio 0).
    private static void ScoreUnionOpeningPrefersBlockade()
    {
        var need = Needs((NeedKind.Blockade, 1f), (NeedKind.Manpower, 0.2f));
        var profile = GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861);
        float Total(int id) => PolicyNeedScorer.Score(Facts(id, 1), need, UnionHistorical, profile, 3, 7, NoDependents).Total;
        AssertTrue(Total(41) > Total(39), "Legal Blockade over Enrollment");
        AssertTrue(Total(35) > Total(39), "Civilian Ships over Enrollment");
    }

    private static void ScoreCsaOpeningPrefersTradeAndRecognition()
    {
        var need = Needs((NeedKind.Blockade, 0.3f), (NeedKind.Recognition, 0.2f));
        var profile = GrandStrategyRegistry.Resolve(1, EraStage.Amateur1861);
        float Total(int id) => PolicyNeedScorer.Score(Facts(id, 1), need, CsaHistorical, profile, 3, 7, NoDependents).Total;
        AssertTrue(Total(142) > Total(135), "Organized Blockade Running over Civilian Ships");
        AssertTrue(Total(115) > Total(135), "Diplomacy over Civilian Ships");
    }

    private static void EmergencyRestoreGivesBackList()
    {
        var list = new List<int> { 20, 21, 22 };
        var snapshot = list.ToArray();
        list.AddRange(PolicyListOverride.ProtectedIds(new[] { 12, 40 }, new[] { 0.3f, 0.5f }, list));
        AssertEqual(5, list.Count, "appended");
        PolicyListOverride.Restore(list, snapshot);
        AssertSequence(new[] { 20, 21, 22 }, list, "vanilla list and order back");
        PolicyListOverride.Restore(list, snapshot);
        AssertSequence(new[] { 20, 21, 22 }, list, "second restore is a no-op");
        PolicyListOverride.Restore(list, null);
        AssertSequence(new[] { 20, 21, 22 }, list, "no snapshot never clears the list");
        PolicyListOverride.Restore(null, snapshot);
    }

    // Verifier probe (stage E verify round 2): with a plan and no eligible Whiskey pick,
    // vanilla's fallback walk (211076-211090) would start the first available entry of the
    // Union Historical list, where USCT (33) and the enrollment acts (39/40, listed twice)
    // sit. Below their need gate they leave the walk; everything else keeps vanilla's order.
    private static void FallbackHoldRemovesHeldActs()
    {
        var calm = Needs((NeedKind.Manpower, 0.2f), (NeedKind.Treasury, 0.5f));
        var list = new List<int>(UnionHistorical);
        var held = PolicyListOverride.HeldIdsToRemove(true, false, calm, list);
        AssertSequence(new[] { 33, 39, 40 }, held, "held acts in list order, each once");
        PolicyListOverride.Remove(list, held);
        var expected = new List<int>();
        foreach (int id in UnionHistorical)
            if (id != 33 && id != 39 && id != 40) expected.Add(id);
        AssertSequence(expected, list, "every occurrence removed, vanilla order kept");

        var csa = PolicyListOverride.HeldIdsToRemove(true, false, calm, CsaHistorical);
        AssertSequence(new[] { 139, 140 }, csa, "CSA conscription acts held");

        var crisis = Needs((NeedKind.Manpower, 0.9f));
        AssertEqual(0, PolicyListOverride.HeldIdsToRemove(true, false, crisis, UnionHistorical).Count, "a manpower crisis opens the gate: vanilla walks its own list");
        var recognitionOnly = new List<int> { 144, 143, 130 };
        AssertSequence(new[] { 144, 130 }, PolicyListOverride.HeldIdsToRemove(true, false, crisis, recognitionOnly), "recognition acts stay held in a manpower crisis");
        AssertEqual(0, PolicyListOverride.HeldIdsToRemove(true, false, calm, new List<int> { 22, 35, 41 }).Count, "nothing irreversible listed: untouched");
        AssertEqual(0, PolicyListOverride.HeldIdsToRemove(true, false, calm, null).Count, "null list");
    }

    private static void FallbackHoldRestoreIsExact()
    {
        var calm = Needs((NeedKind.Manpower, 0.2f));
        var list = new List<int>(UnionHistorical);
        var snapshot = list.ToArray();
        PolicyListOverride.Remove(list, PolicyListOverride.HeldIdsToRemove(true, false, calm, list));
        AssertTrue(list.Count < UnionHistorical.Length, "filtered for the call");
        PolicyListOverride.Restore(list, snapshot);
        AssertSequence(UnionHistorical, list, "identical list, duplicates and order included");
    }

    private static void FallbackHoldNeedsPlanAndFlag()
    {
        var calm = Needs((NeedKind.Manpower, 0.2f));
        AssertEqual(0, PolicyListOverride.HeldIdsToRemove(true, false, null, UnionHistorical).Count, "no plan (no need vector yet): vanilla untouched");
        AssertEqual(0, PolicyListOverride.HeldIdsToRemove(false, false, calm, UnionHistorical).Count, "selection off: vanilla untouched");
        var list = new List<int>(UnionHistorical);
        PolicyListOverride.Remove(list, new List<int>());
        AssertSequence(UnionHistorical, list, "removing nothing leaves the list");
    }

    private static void FallbackHoldSkipsEmergency()
    {
        var calm = Needs((NeedKind.Manpower, 0.2f));
        var emergencyList = new List<int> { 39, 40, 33, 22 };
        AssertEqual(0, PolicyListOverride.HeldIdsToRemove(true, true, calm, emergencyList).Count, "emergency list walked as vanilla wrote it");
    }

    private static void AssertSequence(IList<int> expected, IList<int> actual, string label)
    {
        AssertEqual(expected.Count, actual.Count, label + " (count)");
        for (int i = 0; i < expected.Count; i++)
            AssertEqual(expected[i], actual[i], label + " [" + i + "]");
    }

    // Verifier probe (stage E verify round 1): CSA, low recognition and manpower need,
    // non-act slots full, only irreversible acts left — vanilla's own list starts nothing
    // it does not list; Whiskey must not start CSA emancipation at a soft-penalised score.
    private static void LedgerNeverStartsHeldActs()
    {
        var need = Needs((NeedKind.Recognition, 0.2f), (NeedKind.Manpower, 0.2f));
        var field = new List<PolicyScore>();
        foreach (int id in new[] { 130, 133, 134, 144 })
            field.Add(Score(id, need, CsaHistorical, used: 7, max: 7));
        var ledger = new PolicyPlanLedger();
        ledger.Update(field, need, need);
        AssertEqual(-1, ledger.CurrentPick, "no pick among held acts");
        AssertEqual(-1, ledger.NextEligible(id => true), "nothing to start: vanilla runs its own list");
        AssertEqual(0, ledger.Ranked.Count, "held acts never rank");

        var crisis = Needs((NeedKind.Recognition, 0.2f), (NeedKind.Manpower, 0.9f));
        field.Clear();
        foreach (int id in new[] { 130, 133, 134, 144 })
            field.Add(Score(id, crisis, CsaHistorical, used: 7, max: 7));
        ledger.Update(field, crisis, need);
        int pick = ledger.NextEligible(id => true);
        AssertTrue(pick == 133 || pick == 134, "a manpower crisis opens the manpower acts only");
        AssertEqual(2, ledger.Ranked.Count, "recognition acts stay held");
    }

    private static void SidecarRoundTripsPolicyState()
    {
        var snap = SidecarSnapshot.Empty();
        snap.PolicyNeeds[0] = new[] { 0.5f, 0.25f, 1f };
        snap.PolicyPicks[0] = 22;
        snap.PolicyPicks[1] = -1;
        string json = SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(snap));
        AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out _), "deserialize");
        AssertTrue(SidecarDtoCodec.TryDecode(dto, out var back, out _), "decode");
        AssertEqual(3, back.PolicyNeeds[0].Length, "needs length");
        AssertEqual(0.25f, back.PolicyNeeds[0][1], "need value");
        AssertEqual(22, back.PolicyPicks[0], "union pick");
        AssertEqual(-1, back.PolicyPicks[1], "no CSA pick");
        AssertTrue(back.PolicyNeeds[1] == null, "no CSA needs");

        AssertTrue(SidecarDtoCodec.TryDeserialize("{\"version\":2,\"factions\":[{\"factionId\":0}]}", out var old, out _), "older sidecar");
        AssertTrue(SidecarDtoCodec.TryDecode(old, out var oldSnap, out _), "older sidecar decodes");
        AssertTrue(oldSnap.PolicyNeeds[0] == null, "older sidecar has no needs");
        AssertEqual(-1, oldSnap.PolicyPicks[0], "older sidecar has no pick");
        AssertEqual(0f, NeedVector.FromArray(back.PolicyNeeds[0])[NeedKind.Treasury] - 0.25f, "restores into a need vector");
    }
}
