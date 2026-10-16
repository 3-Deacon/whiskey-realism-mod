using System;
using System.IO;
using WhiskeyRealism;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// BC13 task 1: the personality derivation and the per-side table (spec § 4, requirement-commander-table,
// requirement-registry-first pure half, requirement-derive-from-stats, requirement-cautious-reachable,
// requirement-commander-duty).
internal static partial class CommanderOpportunityTests
{
    private static (string, Action)[] PersonalityTests()
    {
        return new (string, Action)[]
        {
            ("bc13 PERS-DERIVE formulas at three fixture points", PersDeriveFixturePoints),
            ("bc13 PERS-DERIVE clamps both ways", PersDeriveClamps),
            ("bc13 PERS-NOFACTION centre stats give the zero vector", PersNoFaction),
            ("bc13 PERS-EXEMPLARS McClellan cautious Jackson aggressive", PersExemplars),
            ("bc13 PERS-BANDS cautious and aggressive shares on the shipped roster", PersBands),
            ("bc13 PERS-PIN first write wins", PersPin),
            ("bc13 PERS-FALLBACK default is the ranked faction profile", PersFallback),
            ("bc13 PERS-TABLE counts by source and grows", PersTableCounts),
            ("bc13 PERS-DUTY out-of-duty factor", PersDuty),
            ("bc13 PERS-THRESH attack threshold band for Meeting and Attacker", PersThreshold),
            ("bc13 PERS-THRESH flag off restores the parity cap", PersThresholdFlagOff),
            ("bc13 PERS-CASUALTY threshold follows casualty tolerance", PersCasualty),
            ("bc13 PERS-CASUALTY flag off keeps the aggression basis", PersCasualtyFlagOff),
            ("bc13 PERS-ARMY resolution order registry derived default", PersResolutionOrder),
            ("bc13 PERS-ARMY an unlisted army commander is derived and feeds the army", PersArmyFromTable),
            ("bc13 PERS-ECHELON group records map to echelons", PersEchelon),
            ("bc13 PERS-REG every registry entry keeps its list vector", PersRegistry),
            ("bc13 PERS-DEAD dead personality paths are gone", PersDead),
        };
    }

    private static CommanderStats Stats(float init, float cun, float lead, float adm, bool vet = false, bool political = false)
    {
        return new CommanderStats
        {
            Initiative = init, Cunning = cun, Leadership = lead, Administration = adm,
            Veteran = vet, Political = political, WestPoint = false, Valid = true,
        };
    }

    private static void AssertVector(PersonalityVector expected, PersonalityVector actual, string label)
    {
        AssertNear(expected.Aggression, actual.Aggression, 1e-5f, label + " aggression");
        AssertNear(expected.Caution, actual.Caution, 1e-5f, label + " caution");
        AssertNear(expected.Audacity, actual.Audacity, 1e-5f, label + " audacity");
        AssertNear(expected.CasualtyTolerance, actual.CasualtyTolerance, 1e-5f, label + " casualty tolerance");
        AssertNear(expected.PoliticalResponsiveness, actual.PoliticalResponsiveness, 1e-5f, label + " political");
    }

    private static void PersDeriveFixturePoints()
    {
        // The spec's § 4 table, worked by hand.
        // Fixture A: the plan's OPP-LAUNCH commander. agg 3*(0.55-0.45)=0.30; caution 2*(0.45-0.55)+(0.50-0.55)=-0.25;
        // audacity 2.5*(0.40-0.38)+(0.55-0.45)=0.15; casualty 2*(0.60-0.55)+0.2 (veteran)=0.30.
        AssertVector(new PersonalityVector(0.30f, -0.25f, 0.15f, 0.30f, 0f),
            CommanderPersonalityDerivation.Derive(Stats(0.55f, 0.40f, 0.60f, 0.50f, vet: true)), "fixture A");
        // Fixture B: McClellan's stat row (init 0.20 adm 0.90 cun 0.35 lead 1.00 veteran).
        AssertVector(new PersonalityVector(-0.75f, 0.85f, -0.325f, 1.0f, 0f),
            CommanderPersonalityDerivation.Derive(Stats(0.20f, 0.35f, 1.00f, 0.90f, vet: true)), "fixture B");
        // Fixture C: a political commander adds 0.3 caution and 0.4 political responsiveness.
        AssertVector(new PersonalityVector(0f, 0.3f, 0f, 0f, 0.4f),
            CommanderPersonalityDerivation.Derive(Stats(0.45f, 0.38f, 0.55f, 0.55f, political: true)), "fixture C");
    }

    private static void PersDeriveClamps()
    {
        var hi = CommanderPersonalityDerivation.Derive(Stats(1f, 1f, 1f, 1f, vet: true, political: true));
        AssertEqual(1f, hi.Aggression, "aggression upper clamp");
        AssertEqual(1f, hi.Audacity, "audacity upper clamp");
        AssertEqual(1f, hi.CasualtyTolerance, "casualty upper clamp");
        var lo = CommanderPersonalityDerivation.Derive(Stats(0f, 0f, 0f, 0f));
        AssertEqual(-1f, lo.Aggression, "aggression lower clamp");
        AssertEqual(-1f, lo.Audacity, "audacity lower clamp");
        AssertEqual(-1f, lo.CasualtyTolerance, "casualty lower clamp");
        AssertEqual(1f, CommanderPersonalityDerivation.Derive(Stats(0f, 0f, 0f, 1f)).Caution, "caution upper clamp for a zero-initiative administrator");
        AssertEqual(0f, CommanderPersonalityDerivation.To01(-1f), "To01 lower");
        AssertEqual(1f, CommanderPersonalityDerivation.To01(1f), "To01 upper");
        AssertNear(0.5f, CommanderPersonalityDerivation.To01(0f), 1e-6f, "To01 centre");
    }

    private static void PersNoFaction()
    {
        // The roster centre has no faction vector on top: both alliances derive the same zero vector.
        AssertVector(default, CommanderPersonalityDerivation.Derive(Stats(0.45f, 0.38f, 0.55f, 0.55f)), "centre stats");
    }

    private static void PersExemplars()
    {
        float mcclellan = CommanderPersonalityDerivation.To01(
            CommanderPersonalityDerivation.Derive(Stats(0.20f, 0.35f, 1.00f, 0.90f, vet: true)).Aggression);
        AssertNear(0.125f, mcclellan, 1e-5f, "McClellan agg01");
        AssertTrue(mcclellan < 0.30f, "McClellan is in the cautious branch");
        float jackson = CommanderPersonalityDerivation.To01(
            CommanderPersonalityDerivation.Derive(Stats(1.00f, 0.90f, 0.60f, 0.80f, vet: true)).Aggression);
        AssertEqual(1.0f, jackson, "Jackson agg01");
        AssertTrue(jackson >= 0.70f, "Jackson is in the bypass branch");
    }

    // The shipped Config/commanders.txt (1,906 records), initiative as the file's percent value and the record count.
    // Regenerate with the integrator's roster parser (scratchpad cmdstats.py) if the game data changes.
    private static readonly (int Initiative, int Count)[] UnionInitiative =
    {
        (10, 5), (15, 2), (20, 21), (25, 18), (30, 151), (35, 106), (40, 338), (42, 2), (45, 80), (50, 184),
        (55, 26), (60, 108), (65, 15), (70, 55), (75, 7), (80, 17), (85, 2), (90, 8), (100, 1),
    };

    private static readonly (int Initiative, int Count)[] CsaInitiative =
    {
        (15, 1), (20, 6), (25, 8), (30, 58), (35, 34), (40, 187), (45, 48), (48, 1), (50, 150), (55, 26),
        (60, 90), (65, 15), (70, 55), (75, 7), (80, 18), (85, 1), (90, 11), (100, 7),
    };

    private static void BandShares((int Initiative, int Count)[] roster, out float cautious, out float aggressive, out int total)
    {
        int low = 0, high = 0;
        total = 0;
        foreach (var row in roster)
        {
            float init = row.Initiative / 100f;
            float agg01 = CommanderPersonalityDerivation.To01(CommanderPersonalityDerivation.Derive(Stats(init, 0.38f, 0.55f, 0.55f)).Aggression);
            total += row.Count;
            if (agg01 < 0.30f) low += row.Count;
            if (agg01 >= 0.70f) high += row.Count;
        }
        cautious = low / (float)total;
        aggressive = high / (float)total;
    }

    private static void PersBands()
    {
        BandShares(UnionInitiative, out float uLow, out float uHigh, out int uTotal);
        BandShares(CsaInitiative, out float cLow, out float cHigh, out int cTotal);
        AssertEqual(1146, uTotal, "Union record count");
        AssertEqual(723, cTotal, "CSA record count");
        AssertTrue(uLow >= 0.08f && uLow <= 0.25f, "Union cautious share " + uLow);
        AssertTrue(uHigh >= 0.15f && uHigh <= 0.35f, "Union aggressive share " + uHigh);
        AssertTrue(cLow >= 0.08f && cLow <= 0.25f, "CSA cautious share " + cLow);
        AssertTrue(cHigh >= 0.15f && cHigh <= 0.35f, "CSA aggressive share " + cHigh);
    }

    private static void PersPin()
    {
        var table = new CommanderPersonalityTable();
        table.Add(7, new PersonalityVector(0.1f, 0.2f, 0.3f, 0.4f, 0.5f), PersonalitySource.Registry);
        table.Add(7, new PersonalityVector(-0.9f, -0.9f, -0.9f, -0.9f, -0.9f), PersonalitySource.Derived);
        AssertTrue(table.TryGet(7, out var v, out var source), "entry present");
        AssertEqual(0.1f, v.Aggression, "the first vector stays");
        AssertEqual(PersonalitySource.Registry, source, "the first source stays");
        AssertEqual(1, table.Count, "one entry");
    }

    private static void PersFallback()
    {
        var table = new CommanderPersonalityTable();
        AssertFalse(table.TryGet(-1, out _, out _), "a negative index has no entry");
        table.Add(-1, default, PersonalitySource.Derived);
        AssertEqual(0, table.Count, "a negative index is never added");
        AssertFalse(table.TryGet(123456, out _, out _), "an unseen index has no entry");
        foreach (int alliance in new[] { 0, 1 })
        foreach (var echelon in new[] { EchelonKind.Army, EchelonKind.Corps, EchelonKind.Division, EchelonKind.Brigade })
        {
            var expected = TacticalCommanderRoster.ApplyRankBias(FactionProfiles.For(alliance), echelon);
            AssertVector(expected, CommanderPersonalityDerivation.Default(alliance, echelon), "default " + alliance + "/" + echelon);
        }
    }

    private static void PersTableCounts()
    {
        var table = new CommanderPersonalityTable();
        table.Add(0, default, PersonalitySource.Registry);
        table.Add(1500, default, PersonalitySource.Derived);
        table.Add(3, default, PersonalitySource.Derived);
        table.Add(4, default, PersonalitySource.Default);
        AssertEqual(4, table.Count, "count");
        AssertEqual(1, table.CountBySource(PersonalitySource.Registry), "registry");
        AssertEqual(2, table.CountBySource(PersonalitySource.Derived), "derived");
        AssertEqual(1, table.CountBySource(PersonalitySource.Default), "default");
        AssertTrue(table.TryGet(1500, out _, out var s) && s == PersonalitySource.Derived, "a late index is found after growth");
        AssertFalse(table.TryGet(1499, out _, out _), "a hole is not an entry");
    }

    private static void PersDuty()
    {
        AssertEqual(1f, CommanderPersonalityDerivation.Duty(0, 0.5f), "in duty");
        AssertEqual(0.5f, CommanderPersonalityDerivation.Duty(3, 0.5f), "wounded");
        AssertEqual(0.5f, CommanderPersonalityDerivation.Duty(2, 0.5f), "captured");
        AssertEqual(0.5f, CommanderPersonalityDerivation.Duty(4, 0.5f), "killed");
    }

    private static void PersThreshold()
    {
        // Meeting and Attacker: 1.0 + 0.4 x (0.5 - agg01), no cap (spec requirement-attack-threshold-band).
        foreach (var stance in new[] { BattleStance.Meeting, BattleStance.Attacker })
        {
            AssertNear(1.20f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(0f, stance), 1e-5f, stance + " agg 0");
            AssertNear(1.00f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(0.5f, stance), 1e-5f, stance + " agg 0.5");
            AssertNear(0.80f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(1f, stance), 1e-5f, stance + " agg 1");
        }
        // A Defender keeps the uncapped personality threshold.
        AssertNear(1.56f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(0f, BattleStance.Defender), 1e-5f, "defender agg 0");
        AssertNear(1.32f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(0.5f, BattleStance.Defender), 1e-5f, "defender agg 0.5");
        AssertNear(1.08f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(1f, BattleStance.Defender), 1e-5f, "defender agg 1");
        // Vanilla's initiative divisor is applied after the band (BC8): 1.2 / (1 + 0.5 x 0.7).
        AssertNear(1.20f / 1.35f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(0f, BattleStance.Meeting, 0.5f, 0.7f), 1e-5f, "divisor after the band");
    }

    private static void PersThresholdFlagOff()
    {
        bool saved = Plugin.PerCommanderPersonality;
        try
        {
            Plugin.PerCommanderPersonality = false;
            // Off: every non-Defender threshold is the parity cap of 1.0 (personality inert, as before BC13).
            foreach (float agg in new[] { 0f, 0.5f, 1f })
                AssertNear(1.0f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(agg, BattleStance.Meeting), 1e-5f, "capped agg " + agg);
            AssertNear(1.56f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(0f, BattleStance.Defender), 1e-5f, "defender unchanged when off");
        }
        finally
        {
            Plugin.PerCommanderPersonality = saved;
        }
    }

    private sealed class FakeCommander
    {
        public string combinedname;
        public string firstname;
        public string name;
    }

    private static void PersRegistry()
    {
        int checkedEntries = 0;
        foreach (var entry in HistoricalFigureRegistry.EntriesForTest)
        {
            int alliance = entry.AllianceTag == "CSA" ? 1 : 0;
            var commander = new FakeCommander { combinedname = entry.FullNames[0] };
            var (vector, historical) = HistoricalFigureRegistry.Resolve(commander, alliance);
            AssertTrue(historical, entry.CanonicalName + " resolves");
            AssertVector(entry.V, vector, entry.CanonicalName);
            checkedEntries++;
        }
        AssertTrue(checkedEntries > 50, "the registry is not empty");
        // A commander the list does not know is not historical and carries no derived vector from the registry.
        var (unlisted, isHistorical) = HistoricalFigureRegistry.Resolve(new FakeCommander { combinedname = "Nobody Unlisted" }, 0);
        AssertFalse(isHistorical, "unlisted is not historical");
        AssertVector(default, unlisted, "unlisted registry vector");
    }

    private static string AllSource(string repoDir)
    {
        var all = new System.Text.StringBuilder();
        foreach (string file in Directory.GetFiles(TestFixtures.FindRepoPath(repoDir), "*.cs", SearchOption.AllDirectories))
            all.Append(File.ReadAllText(file)).Append('\n');
        return all.ToString();
    }

    private static void PersDead()
    {
        string src = AllSource("src/WhiskeyRealism");
        AssertFalse(src.Contains("AggressionBias01"), "AggressionBias01 is gone");
        AssertFalse(src.Contains("aggressionBias01"), "aggressionBias01 parameters are gone");
        AssertFalse(src.Contains("WeakPointConfirmed"), "WeakPointConfirmed is gone");
        AssertFalse(src.Contains("commanderInitiative01: 0.50f"), "no constant friction initiative");
        string resolver = File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/TacticalCommanderIntent.cs"));
        AssertFalse(resolver.Contains("OperationPosture"), "the resolver's posture switch is gone");
        AssertFalse(resolver.Contains("HasPlan"), "HasPlan is gone");
    }

    private static void PersCasualty()
    {
        AssertNear(0.05f, TacticalReinforcementOpportunityDoctrine.ComputeCasualtyEscalationThreshold(0f), 1e-6f, "tolerance 0");
        AssertNear(0.10f, TacticalReinforcementOpportunityDoctrine.ComputeCasualtyEscalationThreshold(0.5f), 1e-6f, "tolerance 0.5 is unchanged");
        AssertNear(0.15f, TacticalReinforcementOpportunityDoctrine.ComputeCasualtyEscalationThreshold(1f), 1e-6f, "tolerance 1");
        // An aggressive commander who accepts little loss escalates at 8 %; a cautious one who accepts loss does not.
        var carefulAggressor = TestFixtures.MakeForceBalance(10000f, 18000f, aggression: 1.0f, ownCasualtyRatio: 0.08f, casualtyTolerance: 0.0f);
        AssertEqual(ReinforcementOpportunity.WithdrawalToFightLater, TacticalReinforcementOpportunityDoctrine.Decide(carefulAggressor).Opportunity,
            "tolerance 0 escalates at 8 % whatever the aggression");
        var tolerantCautious = TestFixtures.MakeForceBalance(10000f, 18000f, aggression: 0.0f, ownCasualtyRatio: 0.12f, casualtyTolerance: 1.0f);
        AssertEqual(ReinforcementOpportunity.DelayingDefense, TacticalReinforcementOpportunityDoctrine.Decide(tolerantCautious).Opportunity,
            "tolerance 1 holds at 12 % whatever the aggression");
    }

    private static void PersCasualtyFlagOff()
    {
        bool saved = Plugin.PerCommanderPersonality;
        try
        {
            Plugin.PerCommanderPersonality = false;
            // Off: the aggression-based threshold is back, the tolerance is ignored.
            var ev = TestFixtures.MakeForceBalance(10000f, 18000f, aggression: 1.0f, ownCasualtyRatio: 0.12f, casualtyTolerance: 0.0f);
            AssertEqual(ReinforcementOpportunity.DelayingDefense, TacticalReinforcementOpportunityDoctrine.Decide(ev).Opportunity,
                "aggression 1.0 holds at 12 % when the flag is off");
        }
        finally
        {
            Plugin.PerCommanderPersonality = saved;
        }
    }

    private static void PersResolutionOrder()
    {
        var listed = new PersonalityVector(0.7f, -0.5f, 0.6f, 0.4f, -0.2f);
        var stats = Stats(0.55f, 0.40f, 0.60f, 0.50f, vet: true);
        // Registry first: the list's vector, no rank bias, even with stats at hand.
        var source = CommanderPersonalityDerivation.Resolve(true, listed, true, stats, 1, EchelonKind.Corps, out var v);
        AssertEqual(PersonalitySource.Registry, source, "listed");
        AssertVector(listed, v, "listed vector as it is");
        // Else the derivation plus the echelon's rank bias.
        source = CommanderPersonalityDerivation.Resolve(false, default, true, stats, 1, EchelonKind.Corps, out v);
        AssertEqual(PersonalitySource.Derived, source, "derived");
        AssertVector(TacticalCommanderRoster.ApplyRankBias(CommanderPersonalityDerivation.Derive(stats), EchelonKind.Corps), v, "derived vector with rank bias");
        // Stats unreadable, or the flag off: the ranked faction profile.
        source = CommanderPersonalityDerivation.Resolve(false, default, true, default, 1, EchelonKind.Division, out v);
        AssertEqual(PersonalitySource.Default, source, "unreadable stats");
        AssertVector(CommanderPersonalityDerivation.Default(1, EchelonKind.Division), v, "default vector");
        source = CommanderPersonalityDerivation.Resolve(false, default, false, stats, 0, EchelonKind.Brigade, out v);
        AssertEqual(PersonalitySource.Default, source, "flag off");
        AssertVector(CommanderPersonalityDerivation.Default(0, EchelonKind.Brigade), v, "flag-off vector");
    }

    private static void PersArmyFromTable()
    {
        // An unlisted army commander: his table entry is Derived (no rank bias for the Army echelon) and the
        // ArmyOrchestrator is built from the table's entry, not the faction default.
        var stats = Stats(0.80f, 0.60f, 0.50f, 0.40f);
        var source = CommanderPersonalityDerivation.Resolve(false, default, true, stats, 0, EchelonKind.Army, out var armyVector);
        AssertEqual(PersonalitySource.Derived, source, "unlisted army commander is derived");
        AssertVector(CommanderPersonalityDerivation.Derive(stats), armyVector, "army vector is the plain derivation");
        AssertTrue(CommanderPersonalityDerivation.To01(armyVector.Aggression) > CommanderPersonalityDerivation.To01(FactionProfiles.For(0).Aggression),
            "an aggressive unlisted army commander is above the Union faction default");
        var table = new CommanderPersonalityTable();
        table.Add(42, armyVector, source);
        AssertTrue(table.TryGet(42, out var read, out var readSource) && readSource == PersonalitySource.Derived, "the table holds the army entry");
        AssertVector(armyVector, read, "table entry");
        string coordinator = File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs"));
        AssertContains(coordinator, "new ArmyOrchestrator(side.AllianceId, BuiltInPlaybooks.SeedCatalog(), armyVector)", "the army is built from the table's vector");
        AssertContains(coordinator, "CommanderPersonalityRuntime.BuildTable(", "the table is built before the army");
        AssertTrue(coordinator.IndexOf("BattleCoreTick.EnsureSnapshot(sideBattle, side.AllianceId)", StringComparison.Ordinal)
            < coordinator.IndexOf("CommanderPersonalityRuntime.BuildTable(", StringComparison.Ordinal), "snapshot, then table");
        AssertTrue(coordinator.IndexOf("CommanderPersonalityRuntime.BuildTable(", StringComparison.Ordinal)
            < coordinator.IndexOf("new ArmyOrchestrator(side.AllianceId", StringComparison.Ordinal), "table, then army");
    }

    private static void PersEchelon()
    {
        AssertEqual(EchelonKind.Army, CommanderPersonalityDerivation.EchelonOf(16, true), "root is the army");
        AssertEqual(EchelonKind.Army, CommanderPersonalityDerivation.EchelonOf(14, true), "a lone root is the army");
        AssertEqual(EchelonKind.Corps, CommanderPersonalityDerivation.EchelonOf(16, false), "non-top corps");
        AssertEqual(EchelonKind.Division, CommanderPersonalityDerivation.EchelonOf(15, false), "division");
        AssertEqual(EchelonKind.Brigade, CommanderPersonalityDerivation.EchelonOf(14, false), "brigade");
    }
}
