using System;
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
}
