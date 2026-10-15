using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;
using static TestFixtures;

// Battle-core BC8 (plan § "BC8 — experience and drill"): the mod's replacement decisions
// weigh troop quality where vanilla's do. Vanilla anchors in the plan section.
internal static class TroopQualityTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("troop-quality PerceivedExperienceIsTheStarMidpoint", PerceivedExperienceIsTheStarMidpoint),
            ("troop-quality ProbeCarriesExperienceOnlyWhenVisible", ProbeCarriesExperienceOnlyWhenVisible),
            ("troop-quality RememberedKeepsLastSeenExperience", RememberedKeepsLastSeenExperience),
            ("troop-quality PerceivedQualityWeightsRememberedByConfidence", PerceivedQualityWeightsRememberedByConfidence),
            ("troop-quality ReinforcementRowsHaveNoQuality", ReinforcementRowsHaveNoQuality),
            ("troop-quality BeginBuildClearsPerceivedQuality", BeginBuildClearsPerceivedQuality),
            ("troop-quality OwnQualityMeansAliveCombatRegiments", OwnQualityMeansAliveCombatRegiments),
            ("troop-quality SnapshotQualityAllocatesNothingAfterWarmup", SnapshotQualityAllocatesNothingAfterWarmup),
            ("troop-quality ForceBalanceFactorMatchesVanillaShape", ForceBalanceFactorMatchesVanillaShape),
            ("troop-quality CurrentRatioUsesQualityFactor", CurrentRatioUsesQualityFactor),
            ("troop-quality VeteranOutnumberedAttacks", VeteranOutnumberedAttacks),
            ("troop-quality GreenEarlyWarHolds", GreenEarlyWarHolds),
            ("troop-quality AttackThresholdDividedByInitiative", AttackThresholdDividedByInitiative),
            ("troop-quality DefenderStillNeverAttacks", DefenderStillNeverAttacks),
            ("troop-quality FlagOffIsHeadcountOnly", FlagOffIsHeadcountOnly),
            ("troop-quality AiDecisionBlendMatchesVanilla", AiDecisionBlendMatchesVanilla),
            ("troop-quality SectorScalesOwnAndEnemyByBlend", SectorScalesOwnAndEnemyByBlend),
            ("troop-quality SectorBlendsUseClosestVisibleEnemy", SectorBlendsUseClosestVisibleEnemy),
            ("troop-quality SideBlendsNeutralWhenUnknown", SideBlendsNeutralWhenUnknown),
            ("troop-quality OddsUseExperienceBlends", OddsUseExperienceBlends),
            ("troop-quality LossFractionMatchesVanilla", LossFractionMatchesVanilla),
            ("troop-quality BreakLossFractionMatchesVanilla", BreakLossFractionMatchesVanilla),
            ("troop-quality NearBreakUnderFireWithdraws", NearBreakUnderFireWithdraws),
            ("troop-quality VeteranHoldsAtSameLosses", VeteranHoldsAtSameLosses),
            ("troop-quality ChargeOddsMatchVanilla", ChargeOddsMatchVanilla),
            ("troop-quality ChargeRefusedBelowVanillaOdds", ChargeRefusedBelowVanillaOdds),
            ("troop-quality ArmedChargeNotRevertedByOdds", ArmedChargeNotRevertedByOdds),
            ("troop-quality FlagOffDisablesBreakAndChargeOdds", FlagOffDisablesBreakAndChargeOdds),
            ("troop-quality OffensiveRatioScaledByInitiative", OffensiveRatioScaledByInitiative),
            ("troop-quality CautiousLeadNeedsMoreForAnOffensive", CautiousLeadNeedsMoreForAnOffensive),
            ("troop-quality SectorBlendsWeighGroupMorale", SectorBlendsWeighGroupMorale),
            ("troop-quality SideBlendsWeighMorale", SideBlendsWeighMorale),
            ("troop-quality LadderAttackBoundaryIsDividedLikeVanillaAttack", LadderAttackBoundaryIsDividedLikeVanillaAttack),
            ("troop-quality LadderWaitBandIsBelowTheDividedThreshold", LadderWaitBandIsBelowTheDividedThreshold),
            ("troop-quality LadderDelayingDefenseIsBelowTheDividedThreshold", LadderDelayingDefenseIsBelowTheDividedThreshold),
            ("troop-quality LadderWithdrawalIsUndividedLikeVanillaRetreat", LadderWithdrawalIsUndividedLikeVanillaRetreat),
            ("troop-quality LadderProlongedEscalationIsUndivided", LadderProlongedEscalationIsUndivided),
            ("troop-quality LadderDefensiveHoldAboveTheDividedThreshold", LadderDefensiveHoldAboveTheDividedThreshold),
        });
    }

    private const double Minute = 1.0 / 60.0;

    // ---------------------------------------------------------------- B: snapshot + fog

    // UpdateUnitIcon draws floor(Experience() * 5) stars for a visible regiment
    // (121832-121852); the AI sees the bucket midpoint, capped at 1.
    private static void PerceivedExperienceIsTheStarMidpoint()
    {
        AssertNear(0.1f, TroopQuality.PerceivedExperience(0f), 1e-5f, "0 stars");
        AssertNear(0.1f, TroopQuality.PerceivedExperience(0.19f), 1e-5f, "still 0 stars");
        AssertNear(0.3f, TroopQuality.PerceivedExperience(0.2f), 1e-5f, "1 star");
        AssertNear(0.7f, TroopQuality.PerceivedExperience(0.65f), 1e-5f, "3 stars");
        AssertNear(0.9f, TroopQuality.PerceivedExperience(0.99f), 1e-5f, "4 stars");
        AssertNear(1f, TroopQuality.PerceivedExperience(1f), 1e-5f, "5 stars is the cap");
        AssertNear(1f, TroopQuality.PerceivedExperience(1.6f), 1e-5f, "above 1 still 5 stars");
        AssertNear(0.1f, TroopQuality.PerceivedExperience(-0.3f), 1e-5f, "negative clamps to 0 stars");
        AssertNear(0.1f, TroopQuality.PerceivedExperience(float.NaN), 1e-5f, "NaN reads as 0 stars");
    }

    private static PerceptionProbeResult Probe(int capacity)
    {
        return PerceptionProbeResult.Create(capacity);
    }

    private static void Row(ref PerceptionProbeResult p, int id, bool visible, float experience, float morale,
        bool reinforcement = false)
    {
        int i = p.Count++;
        p.EnemyIds[i] = id;
        p.Visible[i] = visible;
        p.X[i] = 100f * i;
        p.Z[i] = -500f;
        p.Strength[i] = 400f;
        p.UnitTyp[i] = 0;
        p.Routed[i] = false;
        p.IsReinforcement[i] = reinforcement;
        p.ArrivalHours[i] = reinforcement ? 1f : 0f;
        p.Experience[i] = experience;
        p.Morale[i] = morale;
    }

    private static int Find(EnemyObservation[] memory, int count, int id)
    {
        for (int i = 0; i < count; i++)
            if (memory[i].InstanceId == id) return i;
        return -1;
    }

    private static void ProbeCarriesExperienceOnlyWhenVisible()
    {
        var memory = new EnemyObservation[8];
        int count = 0;
        var p = Probe(4);
        Row(ref p, 11, visible: false, experience: 0.9f, morale: 0.9f);
        Row(ref p, 12, visible: true, experience: 0.65f, morale: 0.55f);
        BattlePerception.Update(memory, ref count, in p, 1.0);
        AssertEqual(-1, Find(memory, count, 11), "a hidden enemy never seen leaves no observation (and no experience)");
        int seen = Find(memory, count, 12);
        AssertTrue(seen >= 0, "visible enemy observed");
        AssertNear(0.7f, memory[seen].Experience, 1e-5f, "visible enemy carries its star experience");
        AssertNear(0.55f, memory[seen].Morale, 1e-5f, "visible enemy carries its morale");
    }

    private static void RememberedKeepsLastSeenExperience()
    {
        var memory = new EnemyObservation[8];
        int count = 0;
        var p = Probe(4);
        Row(ref p, 21, visible: true, experience: 0.65f, morale: 0.6f);
        BattlePerception.Update(memory, ref count, in p, 1.0);
        // Hidden for HideConfirmTicks; the unit meanwhile gained experience and lost morale.
        for (int t = 1; t <= BattlePerception.HideConfirmTicks; t++)
        {
            p.Count = 0;
            Row(ref p, 21, visible: false, experience: 0.95f, morale: 0.2f);
            BattlePerception.Update(memory, ref count, in p, 1.0 + t * Minute);
        }
        int i = Find(memory, count, 21);
        AssertTrue(i >= 0, "still remembered");
        AssertEqual(ObservationState.Remembered, memory[i].State, "remembered");
        AssertNear(0.7f, memory[i].Experience, 1e-5f, "remembered keeps the last seen stars");
        AssertNear(0.6f, memory[i].Morale, 1e-5f, "remembered keeps the last seen morale");
    }

    private static BattleSnapshot Begin(double hours = 1.0)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, hours);
        s.SetSliceCount(20);
        s.IndexCommandTree(null);
        return s;
    }

    private static EnemyObservation Obs(int id, float experience, float morale, bool remembered = false,
        float confidence = 1f, bool reinforcement = false, float strength = 500f)
    {
        return new EnemyObservation
        {
            InstanceId = id,
            UnitTyp = 0,
            X = 0f,
            Z = -400f,
            Strength = strength,
            State = remembered ? ObservationState.Remembered : ObservationState.Visible,
            LastSeenHours = 1.0,
            Confidence01 = confidence,
            IsReinforcement = reinforcement,
            Experience = experience,
            Morale = morale,
        };
    }

    private static void PerceivedQualityWeightsRememberedByConfidence()
    {
        var memory = new[]
        {
            Obs(1, 0.3f, 0.8f),
            Obs(2, 0.9f, 0.4f, remembered: true, confidence: 0.5f),
        };
        var s = Begin();
        BattlePerception.Publish(s, PerceptionMode.FogGrid, PerceptionFallbackReason.None, memory, memory.Length, PerceptionProbeResult.Create(1));
        SideQuality q = s.PerceivedEnemyQuality;
        AssertTrue(q.Known, "quality known with observations");
        AssertNear((0.3f + 0.9f * 0.5f) / 1.5f, q.Experience, 1e-5f, "experience: visible 1, remembered by confidence");
        AssertNear((0.8f + 0.4f * 0.5f) / 1.5f, q.Morale, 1e-5f, "morale: visible 1, remembered by confidence");
    }

    private static void ReinforcementRowsHaveNoQuality()
    {
        var memory = new[] { Obs(1, 0.9f, 0.9f, reinforcement: true) };
        var s = Begin();
        BattlePerception.Publish(s, PerceptionMode.FogGrid, PerceptionFallbackReason.None, memory, memory.Length, PerceptionProbeResult.Create(1));
        AssertFalse(s.PerceivedEnemyQuality.Known, "a reinforcement column alone gives no quality");

        memory = new[] { Obs(1, 0.9f, 0.9f, reinforcement: true), Obs(2, 0.1f, 0.5f) };
        s = Begin();
        BattlePerception.Publish(s, PerceptionMode.FogGrid, PerceptionFallbackReason.None, memory, memory.Length, PerceptionProbeResult.Create(1));
        AssertNear(0.1f, s.PerceivedEnemyQuality.Experience, 1e-5f, "column excluded from the mean");
    }

    private static void BeginBuildClearsPerceivedQuality()
    {
        var memory = new[] { Obs(1, 0.5f, 0.5f) };
        var s = Begin();
        BattlePerception.Publish(s, PerceptionMode.FogGrid, PerceptionFallbackReason.None, memory, memory.Length, PerceptionProbeResult.Create(1));
        AssertTrue(s.PerceivedEnemyQuality.Known, "known after publish");
        s.BeginBuild(1, 0, 0, 2, 2.0);
        AssertFalse(s.PerceivedEnemyQuality.Known, "a rebuild forgets last tick's quality");
    }

    private static UnitRecord Regiment(int id, int unitTyp, float morale, float experience, int strength = 400,
        UnitFlags extra = UnitFlags.None)
    {
        return new UnitRecord
        {
            InstanceId = id,
            UnitTyp = unitTyp,
            Strength = strength,
            Morale = morale,
            Experience = experience,
            ParentIndex = -1,
            Flags = UnitFlags.Alive | UnitFlags.Active | extra,
        };
    }

    private static void OwnQualityMeansAliveCombatRegiments()
    {
        var own = new[]
        {
            Regiment(1, 0, 0.8f, 0.6f),
            Regiment(2, 1, 0.6f, 0.2f),
            Regiment(3, 0, 0.1f, 0.0f, extra: UnitFlags.Routed),     // routed: out
            Regiment(4, 0, 0.1f, 0.0f, strength: 0),                 // destroyed: out
            Regiment(5, 16, 0.1f, 0.0f, extra: UnitFlags.IsGroup),   // group: out (its regiments count)
            new UnitRecord { InstanceId = 6, UnitTyp = 0, Strength = 400, Morale = 0.1f, Flags = UnitFlags.Alive }, // inactive
        };
        SideQuality q = TroopQuality.OwnQuality(own, own.Length);
        AssertTrue(q.Known, "two regiments sampled");
        AssertNear(0.7f, q.Morale, 1e-5f, "mean morale of the two");
        AssertNear(0.4f, q.Experience, 1e-5f, "mean experience of the two");
        AssertFalse(TroopQuality.OwnQuality(own, 0).Known, "nothing sampled is unknown");
    }

    private static void SnapshotQualityAllocatesNothingAfterWarmup()
    {
        const int enemies = 300;
        var memory = new EnemyObservation[enemies * 2];
        int memoryCount = 0;
        var probe = PerceptionProbeResult.Create(enemies);
        var own = new UnitRecord[300];
        for (int i = 0; i < own.Length; i++) own[i] = Regiment(1000 + i, i % 2, 0.7f, (i % 10) / 10f);
        var s = new BattleSnapshot();

        void Tick(int tick)
        {
            probe.Count = 0;
            for (int i = 0; i < enemies; i++)
                Row(ref probe, 5000 + i, ((i + tick) % 3) != 0, (i % 10) / 10f, 0.6f, reinforcement: i >= enemies - 5);
            double now = 10.0 + tick * Minute;
            s.BeginBuild(1, 0, 0, tick, now);
            s.SetSliceCount(20);
            for (int i = 0; i < own.Length; i++) s.AddOwn(in own[i]);
            s.IndexCommandTree(null);
            BattlePerception.Update(memory, ref memoryCount, in probe, now);
            BattlePerception.Publish(s, PerceptionMode.FogGrid, PerceptionFallbackReason.None, memory, memoryCount, in probe);
            TroopQuality.OwnQuality(s.Own, s.OwnCount);
        }

        Tick(0);
        Tick(1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int t = 2; t < 6; t++) Tick(t);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        AssertTrue(s.PerceivedEnemyQuality.Known, "quality published");
        AssertEqual(0L, allocated, "quality path allocated bytes after warm-up");
    }

    // ---------------------------------------------------------------- C: force balance

    // CalculateSideStatsAndUpdateAITasks 84862: ratio × (1 + Δmorale) × (1 + Δexperience).
    private static void ForceBalanceFactorMatchesVanillaShape()
    {
        var own = new SideQuality(0.8f, 0.5f);
        var enemy = new SideQuality(0.6f, 0.2f);
        AssertNear(1.2f * 1.3f, TroopQuality.ForceBalanceFactor(own, enemy), 1e-5f, "(1 + 0.2)(1 + 0.3)");
        AssertNear(0.8f * 0.7f, TroopQuality.ForceBalanceFactor(enemy, own), 1e-5f, "mirror side");
        AssertNear(1f, TroopQuality.ForceBalanceFactor(own, own), 1e-5f, "equal quality is neutral");
        AssertNear(1f, TroopQuality.ForceBalanceFactor(own, SideQuality.Unknown), 1e-5f, "unknown enemy is neutral");
        AssertNear(1f, TroopQuality.ForceBalanceFactor(SideQuality.Unknown, enemy), 1e-5f, "unknown own is neutral");
        // Two terms below -1 must not multiply into a positive factor.
        AssertNear(0f, TroopQuality.ForceBalanceFactor(new SideQuality(0f, 0f), new SideQuality(1.5f, 1.5f)), 1e-5f,
            "each term floors at 0");
    }

    private static TacticalForceBalanceEvidence Evidence(float own, float enemy, SideQuality ownQ, SideQuality enemyQ,
        float initiative = 0.5f, float influence = 0.7f, float aggression = 0.5f, BattleStance stance = BattleStance.Meeting)
    {
        return new TacticalForceBalanceEvidence(own, enemy, Array.Empty<ReinforcementArrival>(),
            initiative, aggression, 0.8f, stance: stance,
            ownQuality: ownQ, enemyQuality: enemyQ, initiativeTriggerInfluence: influence);
    }

    private static void CurrentRatioUsesQualityFactor()
    {
        var ev = Evidence(9000f, 10000f, new SideQuality(0.8f, 0.5f), new SideQuality(0.6f, 0.2f));
        AssertNear(0.9f * 1.56f, ev.CurrentRatio, 1e-4f, "headcount 0.9 × quality 1.56");
        AssertNear(10000f / 1.56f, ev.EffectiveEnemyStrength, 0.5f, "enemy in own-equivalent men");
        AssertNear(0.9f, MakeForceBalance(9000f, 10000f).CurrentRatio, 1e-4f, "no quality input keeps the headcount ratio");
        var noEnemy = Evidence(9000f, 0f, new SideQuality(0.8f, 0.5f), new SideQuality(0.6f, 0.2f));
        AssertNear(10f, noEnemy.CurrentRatio, 1e-5f, "no enemy keeps its sentinel");
    }

    // A veteran army slightly outnumbered by a green one attacks.
    private static void VeteranOutnumberedAttacks()
    {
        var ev = Evidence(9000f, 10000f, new SideQuality(0.75f, 0.7f), new SideQuality(0.7f, 0.2f));
        var decision = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.AttackNow, decision.Opportunity, "veterans attack a larger green army: " + decision.Reason);
        var headcount = TacticalReinforcementOpportunityDoctrine.Decide(MakeForceBalance(9000f, 10000f));
        AssertTrue(headcount.Opportunity != ReinforcementOpportunity.AttackNow, "headcount alone would not attack");
    }

    // A green early-war army facing veterans holds instead of attacking on numbers.
    private static void GreenEarlyWarHolds()
    {
        var ev = Evidence(11000f, 10000f, new SideQuality(0.7f, 0.1f), new SideQuality(0.7f, 0.6f));
        var decision = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.DelayingDefense, decision.Opportunity, "green army holds: " + decision.Reason);
        var headcount = TacticalReinforcementOpportunityDoctrine.Decide(MakeForceBalance(11000f, 10000f));
        AssertEqual(ReinforcementOpportunity.AttackNow, headcount.Opportunity, "headcount alone would attack");
    }

    // CheckGlobalAIStrategy 6396-6430: trigger / (1 + initiative × influence), influence 0.7.
    private static void AttackThresholdDividedByInitiative()
    {
        AssertNear(1f / 1.35f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(0.5f, BattleStance.Meeting, 0.5f, 0.7f), 1e-5f,
            "mid initiative: parity cap then / 1.35");
        AssertNear(1f / 1.7f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(0.5f, BattleStance.Attacker, 1f, 0.7f), 1e-5f,
            "full initiative: / 1.7");
        AssertNear(1f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(0.5f, BattleStance.Meeting, 0f, 0.7f), 1e-5f,
            "no initiative: unchanged");
        AssertNear(1f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(0.5f, BattleStance.Meeting, 0.5f, 0f), 1e-5f,
            "no influence: unchanged");
        AssertNear(1.32f / 1.35f, TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(0.5f, BattleStance.Defender, 0.5f, 0.7f), 1e-4f,
            "defender: personality threshold / 1.35");
        // Through Decide: a 0.8 ratio whose own arrivals restore parity inside the attack
        // window attacks at mid initiative with the vanilla influence; without it, it waits.
        var arrivals = new[] { new ReinforcementArrival(0.5f, 2500f) };
        var withInfluence = new TacticalForceBalanceEvidence(8000f, 10000f, arrivals, 0.5f, 0.5f, 0.8f,
            initiativeTriggerInfluence: 0.7f);
        var withoutInfluence = new TacticalForceBalanceEvidence(8000f, 10000f, arrivals, 0.5f, 0.5f, 0.8f);
        AssertEqual(ReinforcementOpportunity.AttackNow, TacticalReinforcementOpportunityDoctrine.Decide(withInfluence).Opportunity, "0.8 >= 0.74");
        AssertEqual(ReinforcementOpportunity.WaitAndConsolidate, TacticalReinforcementOpportunityDoctrine.Decide(withoutInfluence).Opportunity, "0.8 < 1");
    }

    private static void DefenderStillNeverAttacks()
    {
        var ev = Evidence(10000f, 4000f, new SideQuality(0.9f, 0.9f), new SideQuality(0.4f, 0.1f),
            initiative: 1f, stance: BattleStance.Defender);
        AssertTrue(TacticalReinforcementOpportunityDoctrine.Decide(ev).Opportunity != ReinforcementOpportunity.AttackNow,
            "a defender never attacks, whatever the quality and initiative");
    }

    // Rollback: Enable Experience And Drill AI Weighting = false gives the neutral prefs.
    private static void FlagOffIsHeadcountOnly()
    {
        var vanilla = new TroopQualityPrefs
        {
            Enabled = true,
            ExpInfluenceOnAiDecisions = 0.25f,
            InitiativeTriggerInfluence = 0.7f,
            MoraleInfluenceOnAiDecisions = 0.25f,
        };
        AssertNear(0.25f, vanilla.ExpInfluence, 1e-6f, "on: vanilla exp influence");
        AssertNear(0.25f, vanilla.MoraleInfluence, 1e-6f, "on: vanilla morale influence");
        AssertNear(0.7f, vanilla.InitiativeInfluence, 1e-6f, "on: vanilla initiative influence");
        var off = vanilla;
        off.Enabled = false;
        AssertNear(0f, off.ExpInfluence, 1e-6f, "off: no exp influence");
        AssertNear(0f, off.MoraleInfluence, 1e-6f, "off: no morale influence");
        AssertNear(0f, off.InitiativeInfluence, 1e-6f, "off: no initiative influence");
        AssertFalse(off.Quality(new SideQuality(0.8f, 0.8f)).Known, "off: side quality unknown (factor 1)");
        AssertTrue(vanilla.Quality(new SideQuality(0.8f, 0.8f)).Known, "on: side quality passes");
    }

    // ---------------------------------------------------------------- D: group stance / odds / sector

    // GetGroupStrength 6057: 1 - ei + groupexperience × ei, ei = expinfluenceonaidecisions (0.25).
    private static void AiDecisionBlendMatchesVanilla()
    {
        AssertNear(0.75f, TroopQuality.AiDecisionBlend(0f, 0.25f), 1e-6f, "green");
        AssertNear(0.9f, TroopQuality.AiDecisionBlend(0.6f, 0.25f), 1e-6f, "seasoned");
        AssertNear(1f, TroopQuality.AiDecisionBlend(1f, 0.25f), 1e-6f, "veteran");
        AssertNear(1f, TroopQuality.AiDecisionBlend(1.5f, 0.25f), 1e-6f, "experience clamps at 1");
        AssertNear(0.75f, TroopQuality.AiDecisionBlend(float.NaN, 0.25f), 1e-6f, "NaN reads as 0");
        AssertNear(1f, TroopQuality.AiDecisionBlend(0f, 0f), 1e-6f, "no influence is neutral");
    }

    private static void SectorScalesOwnAndEnemyByBlend()
    {
        TacticalGroupContactInput Input(float own, float enemy) => new TacticalGroupContactInput(1, 4000f, 0f, 3000f,
            0f, -1, string.Empty, false, false, false, false, ownExperienceBlend: own, enemyExperienceBlend: enemy);
        var plain = TacticalGroupSectorEstimator.BuildSector(Input(1f, 1f));
        var weighted = TacticalGroupSectorEstimator.BuildSector(Input(0.9f, 0.75f));
        AssertNear(4000f, plain.OwnStrength, 1e-3f, "neutral blends keep own strength");
        AssertNear(3000f, plain.EnemyStrength, 1e-3f, "neutral blends keep enemy strength");
        AssertNear(3600f, weighted.OwnStrength, 1e-3f, "own x own blend");
        AssertNear(2250f, weighted.EnemyStrength, 1e-3f, "enemy x enemy blend");
        var bad = TacticalGroupSectorEstimator.BuildSector(Input(float.NaN, -1f));
        AssertNear(4000f, bad.OwnStrength, 1e-3f, "a non-finite blend is neutral");
        AssertNear(3000f, bad.EnemyStrength, 1e-3f, "a non-positive blend is neutral");
    }

    private static void SectorBlendsUseClosestVisibleEnemy()
    {
        var s = Begin();
        var group = Regiment(1, 16, 0.7f, 0.8f, extra: UnitFlags.IsGroup);
        s.AddOwn(in group);
        s.IndexCommandTree(null);
        var near = Obs(10, 0.1f, 0.5f);
        near.Z = -200f;
        var far = Obs(11, 0.9f, 0.5f);
        far.Z = -900f;
        var closerRemembered = Obs(12, 0.9f, 0.5f, remembered: true, confidence: 0.9f);
        closerRemembered.Z = -50f;
        s.AddObservation(in near);
        s.AddObservation(in far);
        s.AddObservation(in closerRemembered);
        SlicePressureBuilder.Build(s, new SliceGeometry { MaxRangeMeters = 1000f, FlankShiftMeters = 150f });
        TroopQuality.SectorBlends(s, 0, 0.25f, 0f, out float own, out float enemy);
        AssertNear(0.95f, own, 1e-5f, "own: the group's groupexperience 0.8");
        AssertNear(0.775f, enemy, 1e-5f, "enemy: the closest VISIBLE observation (0.1), not the closer remembered one");

        var hidden = Begin();
        hidden.AddOwn(in group);
        hidden.IndexCommandTree(null);
        var onlyRemembered = Obs(12, 0.5f, 0.5f, remembered: true, confidence: 0.5f);
        hidden.AddObservation(in onlyRemembered);
        BattlePerception.Publish(hidden, PerceptionMode.FogGrid, PerceptionFallbackReason.None,
            new[] { onlyRemembered }, 1, PerceptionProbeResult.Create(1));
        TroopQuality.SectorBlends(hidden, 0, 0.25f, 0f, out _, out float fallback);
        AssertNear(0.875f, fallback, 1e-5f, "no visible enemy: the side's perceived mean (0.5)");

        var empty = Begin();
        empty.AddOwn(in group);
        empty.IndexCommandTree(null);
        TroopQuality.SectorBlends(empty, 0, 0.25f, 0f, out float ownAlone, out float none);
        AssertNear(1f, none, 1e-6f, "nothing perceived: neutral enemy blend");
        AssertNear(1f, ownAlone, 1e-6f, "nothing perceived: neutral own blend too (no comparison)");
        TroopQuality.SectorBlends(s, 0, 0f, 0f, out float offOwn, out float offEnemy);
        AssertNear(1f, offOwn, 1e-6f, "weighting off: own neutral");
        AssertNear(1f, offEnemy, 1e-6f, "weighting off: enemy neutral");
    }

    private static void SideBlendsNeutralWhenUnknown()
    {
        TroopQuality.SideBlends(new SideQuality(0.7f, 0.2f), new SideQuality(0.7f, 0.6f), 0.25f, 0f, out float own, out float enemy);
        AssertNear(0.8f, own, 1e-6f, "own side mean experience 0.2");
        AssertNear(0.9f, enemy, 1e-6f, "perceived mean experience 0.6");
        TroopQuality.SideBlends(new SideQuality(0.7f, 0.2f), SideQuality.Unknown, 0.25f, 0.25f, out own, out enemy);
        AssertNear(1f, own, 1e-6f, "unknown enemy: own neutral");
        AssertNear(1f, enemy, 1e-6f, "unknown enemy: enemy neutral");
    }

    private static void OddsUseExperienceBlends()
    {
        var contact = TacticalContactLedger.Classify(new TacticalContactInput(5000f, 5000f, 0f, 0f, true, false));
        TacticalOddsAssessment Odds(float ownBlend, float enemyBlend) => TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
            6000f, 5000f, 5000f, 0f, 1000f, 0f, contact, null, ownBlend, enemyBlend));
        var plain = Odds(1f, 1f);
        var weighted = Odds(0.8f, 0.9f);
        AssertNear(6000f / 5000f, plain.CurrentGlobalOdds, 1e-4f, "neutral blends keep the headcount odds");
        AssertNear(6000f * 0.8f / (5000f * 0.9f), weighted.CurrentGlobalOdds, 1e-4f, "current odds weighted");
        AssertNear(7000f * 0.8f / (5000f * 0.9f), weighted.ProjectedGlobalOdds, 1e-4f, "projected odds weighted");
    }

    // ---------------------------------------------------------------- E: withdrawal and charge

    // LossesAreUnbearable 139831: (Σ losses - losses[3]) / max(1, that + strength).
    private static void LossFractionMatchesVanilla()
    {
        AssertNear(200f / 600f, TroopQuality.LossFraction(new[] { 100, 60, 40, 999 }, 400), 1e-6f, "missing (losses[3]) excluded");
        AssertNear(0f, TroopQuality.LossFraction(new[] { 0, 0, 0, 0 }, 400), 1e-6f, "no losses");
        AssertNear(0f, TroopQuality.LossFraction(null, 400), 1e-6f, "no array");
        AssertNear(30f / 30f, TroopQuality.LossFraction(new[] { 30 }, 0), 1e-6f, "short array, destroyed unit");
    }

    // Shipped unitprefs: trigger0 0.8, trigger100 0.55, influences 0.91 / 0.9 / 0.75,
    // skirmishers 0.5, per-day factors 1.1 / 1.3; campaignprefs AI bonus 0.3.
    private static BreakToleranceRules ShippedBreakRules(bool aiVsAi = false, float campaignBonus = 0.5f)
    {
        return new BreakToleranceRules
        {
            Trigger0 = 0.8f, Trigger100 = 0.55f,
            InfluenceExperience = 0.91f, InfluenceTraining = 0.9f, InfluenceLeadership = 0.75f,
            SkirmisherFactor = 0.5f,
            AiBonusRestoringMorale = 0.3f, AiShiftedBonus = 1.45f, AiVsAi = aiVsAi, UsedCampaignBonusRunning = campaignBonus,
            PerDayExperience0 = 1.1f, PerDayExperience100 = 1.3f,
        };
    }

    // GetUnbearableLossesTrigger 113363-113400, worked by hand.
    private static void BreakLossFractionMatchesVanilla()
    {
        var rules = ShippedBreakRules();
        var green = new BreakToleranceInput
        {
            MilitaryExperience = 0.3f, Experience = 0.2f, Training = 0.5f, Leadership = 0.5f,
            UnitTyp = 0, PlayerAlliance = false, BattlePassedDays = 0,
        };
        float num = 0.8f + (0.55f - 0.8f) * 0.3f;
        num *= 1f - 0.2f * (1f - 0.91f);
        num *= 1f - 0.5f * (1f - 0.9f);
        num *= 1f - 0.5f * (1f - 0.75f);
        float aiBonus = 1f + (0f + 0.5f) * 0.3f;
        AssertNear((1f - num) * aiBonus, TroopQuality.BreakLossFraction(rules, green), 1e-5f, "AI regiment, day 0");

        var player = green;
        player.PlayerAlliance = true;
        AssertNear(1f - num, TroopQuality.BreakLossFraction(rules, player), 1e-5f, "player alliance: no AI bonus");

        var aiVsAi = ShippedBreakRules(aiVsAi: true);
        AssertNear((1f - num) * (1f + (1.45f + 0.5f) * 0.3f), TroopQuality.BreakLossFraction(aiVsAi, green), 1e-5f, "ai_vs_ai adds the shifted bonus");

        var skirmisher = player;
        skirmisher.UnitTyp = 3;
        AssertNear((1f - num) * 0.5f, TroopQuality.BreakLossFraction(rules, skirmisher), 1e-5f, "skirmishers x 0.5");

        var dayTwo = player;
        dayTwo.BattlePassedDays = 2;
        float perDay = (1.1f + (1.3f - 1.1f) * 0.2f - 1f) * 2f + 1f;
        AssertNear((1f - num) * perDay, TroopQuality.BreakLossFraction(rules, dayTwo), 1e-5f, "day factor");

        var veteran = player;
        veteran.MilitaryExperience = 0.8f; veteran.Experience = 0.9f; veteran.Training = 0.9f; veteran.Leadership = 0.8f;
        AssertTrue(TroopQuality.BreakLossFraction(rules, veteran) > TroopQuality.BreakLossFraction(rules, player) + 0.15f,
            "veteran, drilled, well led: a much higher break point");

        var cap = ShippedBreakRules(aiVsAi: true, campaignBonus: 5f);
        AssertNear(1f, TroopQuality.BreakLossFraction(cap, green), 1e-6f, "capped at 1");
    }

    private static TacticalMoralePressure.Input UnderFire(float lossFraction, float breakLossFraction)
    {
        return new TacticalMoralePressure.Input
        {
            CurrentMorale = 0.95f,
            BattleStartMorale = 1f,
            BattleStartMoraleInitialized = true,
            FallbackThreshold = 0.4f,
            ReceivedFireFromClosestFar = true,
            CoverValue = 0.5f,
            AiFeudStance = -1,
            LossFraction = lossFraction,
            BreakLossFraction = breakLossFraction,
        };
    }

    private static void NearBreakUnderFireWithdraws()
    {
        AssertEqual(TacticalMoralePressure.Result.WithdrawalCandidate,
            TacticalMoralePressure.Score(UnderFire(0.33f, 0.40f)), "0.33 >= 0.8 x 0.40: pull back before breaking");
        AssertEqual(TacticalMoralePressure.Result.Stable,
            TacticalMoralePressure.Score(UnderFire(0.30f, 0.40f)), "0.30 < 0.32: holds");
        var notUnderFire = UnderFire(0.39f, 0.40f);
        notUnderFire.ReceivedFireFromClosestFar = false;
        AssertEqual(TacticalMoralePressure.Result.Stable, TacticalMoralePressure.Score(notUnderFire), "not under fire: no near-break pull back");
        AssertEqual(TacticalMoralePressure.Result.Stable,
            TacticalMoralePressure.Score(UnderFire(0.9f, 0f)), "no break point known: unchanged ladder");
        var collapse = UnderFire(0.39f, 0.40f);
        collapse.CurrentMorale = 0.3f;
        AssertEqual(TacticalMoralePressure.Result.CollapseCandidate, TacticalMoralePressure.Score(collapse), "collapse still wins");
    }

    private static void VeteranHoldsAtSameLosses()
    {
        var rules = ShippedBreakRules();
        var green = new BreakToleranceInput { MilitaryExperience = 0.3f, Experience = 0.2f, Training = 0.5f, Leadership = 0.5f, PlayerAlliance = true };
        var veteran = new BreakToleranceInput { MilitaryExperience = 0.8f, Experience = 0.9f, Training = 0.9f, Leadership = 0.8f, PlayerAlliance = true };
        const float losses = 0.36f;
        AssertEqual(TacticalMoralePressure.Result.WithdrawalCandidate,
            TacticalMoralePressure.Score(UnderFire(losses, TroopQuality.BreakLossFraction(rules, green))), "green pulls back at 36% losses");
        AssertEqual(TacticalMoralePressure.Result.Stable,
            TacticalMoralePressure.Score(UnderFire(losses, TroopQuality.BreakLossFraction(rules, veteran))), "veteran holds at 36% losses");
    }

    private static ChargeOddsRules ShippedChargeOdds()
    {
        return new ChargeOddsRules { Trigger = 3f, WeightStrength = 1f, WeightExperience = 0.7f, WeightMorale = 1.5f, WeightFatigue = 0.6f };
    }

    // UpdateChargeTarget 122229-122231 (infantry: no meleemounted factor).
    private static void ChargeOddsMatchVanilla()
    {
        var w = ShippedChargeOdds();
        float own = 400f * 2f * (1f + 0.5f * 0.7f) * (1f + 0.8f * 1.5f) * (1f + 0.8f * 0.6f);
        float target = 300f * 2f * (1f + 0.1f * 0.7f) * (1f + 0.5f * 1.5f) * (1f + 0.5f * 0.6f);
        AssertNear(own, TroopQuality.VanillaChargeStrength(400f, 0.5f, 0.8f, 0.2f, w), 1e-2f, "own weighted strength");
        AssertNear(own / target, TroopQuality.VanillaChargeOdds(400f, 0.5f, 0.8f, 0.2f, 300f, 0.1f, 0.5f, 0.5f, w), 1e-4f, "odds");
        AssertNear(own, TroopQuality.VanillaChargeOdds(400f, 0.5f, 0.8f, 0.2f, 0f, 0f, 0f, 1f, w), 1e-2f, "max(1, enemy)");
    }

    private static ChargeInput ReadyCharge(float odds, float trigger)
    {
        return new ChargeInput
        {
            ConfigOn = true, IsInfantryRegiment = true, GroupAiStance = ChargeDoctrine.AssaultStance,
            TargetVisible = true, TargetIsFirstInRange = true, DistanceToTarget = 30f, VanillaMaxChargeRadius = 50f,
            OwnMorale = 0.8f, TargetMorale = 0.5f, OwnFatigue = 0.2f, Moving = true,
            MovementMode = ChargeDoctrine.NormalMovementMode, IsLeading = true,
            SecondsSinceLastAiChargeRefusal = double.PositiveInfinity,
            VanillaChargeOdds = odds, VanillaChargeTrigger = trigger,
        };
    }

    private static void ChargeRefusedBelowVanillaOdds()
    {
        AssertEqual(ChargeAction.Charge, ChargeDoctrine.Decide(ReadyCharge(3.5f, 3f)), "vanilla's odds pass: arm");
        AssertEqual(ChargeAction.None, ChargeDoctrine.Decide(ReadyCharge(2.4f, 3f)), "vanilla would refuse: do not arm");
        AssertEqual(ChargeAction.None, ChargeDoctrine.Decide(ReadyCharge(3f, 3f)), "strictly above the trigger, as 122259");
        AssertEqual(ChargeAction.Charge, ChargeDoctrine.Decide(ReadyCharge(0f, 0f)), "no trigger: no pre-filter");
    }

    private static void ArmedChargeNotRevertedByOdds()
    {
        var armed = ReadyCharge(1f, 3f);
        armed.WhiskeyArmed = true;
        armed.MovementMode = ChargeDoctrine.ArmedMovementMode;
        AssertEqual(ChargeAction.None, ChargeDoctrine.Decide(armed), "armed: vanilla's own check decides, the doctrine holds");
    }

    private static void FlagOffDisablesBreakAndChargeOdds()
    {
        var on = new TroopQualityPrefs { Enabled = true, Break = ShippedBreakRules(), ChargeOdds = ShippedChargeOdds() };
        AssertTrue(on.BreakToleranceOn, "on: break tolerance read");
        AssertNear(3f, on.ChargeOddsRulesIfEnabled.Trigger, 1e-6f, "on: vanilla charge trigger");
        var off = on;
        off.Enabled = false;
        AssertFalse(off.BreakToleranceOn, "off: no break input");
        AssertNear(0f, off.ChargeOddsRulesIfEnabled.Trigger, 1e-6f, "off: no charge pre-filter");
    }

    // ---------------------------------------------------------------- F: campaign offensives

    // CheckOffensiveMovements 14250/14281: required strength × (1 + (1 - initiative) ×
    // commanderinfluenceinitiativeforoperations), campaignprefs 0.25.
    private static void OffensiveRatioScaledByInitiative()
    {
        var cautious = CoordinatedOperationOptions.StableDefaults(1000f).ScaledByCommanderInitiative(0f, 0.25f);
        AssertNear(1.25f * 1.25f, cautious.RequiredAttackRatio, 1e-5f, "initiative 0: attack ratio x 1.25");
        AssertNear(0.85f * 1.25f, cautious.RequiredReinforceRatio, 1e-5f, "initiative 0: reinforce ratio x 1.25");
        var bold = CoordinatedOperationOptions.StableDefaults(1000f).ScaledByCommanderInitiative(1f, 0.25f);
        AssertNear(1.25f, bold.RequiredAttackRatio, 1e-5f, "initiative 1: unchanged");
        AssertNear(0.85f, bold.RequiredReinforceRatio, 1e-5f, "initiative 1: unchanged");
        var mid = CoordinatedOperationOptions.StableDefaults(1000f).ScaledByCommanderInitiative(0.5f, 0.25f);
        AssertNear(1.25f * 1.125f, mid.RequiredAttackRatio, 1e-5f, "initiative 0.5: x 1.125");
        var off = CoordinatedOperationOptions.StableDefaults(1000f).ScaledByCommanderInitiative(0f, 0f);
        AssertNear(1.25f, off.RequiredAttackRatio, 1e-5f, "influence 0 (weighting off): unchanged");
        var nan = CoordinatedOperationOptions.StableDefaults(1000f).ScaledByCommanderInitiative(float.NaN, 0.25f);
        AssertNear(1.25f * 1.25f, nan.RequiredAttackRatio, 1e-5f, "unreadable initiative reads as 0 (cautious)");
        AssertNear(1250f, mid.MaxSupportEffectiveStrength, 1e-3f, "support cap untouched");
    }

    private static CoordinatedOperationInput OffensiveInput(CoordinatedOperationOptions options)
    {
        return new CoordinatedOperationInput
        {
            AllianceId = 0,
            Intent = CoordinatedOperationIntent.Attack,
            TargetEnemyStrength = 1000f,
            TargetX = 0f,
            TargetZ = 0f,
            PreferredLeadStableUnitId = 1,
            Options = options,
            Candidates = new List<CoordinatedOperationCandidate>
            {
                new CoordinatedOperationCandidate
                {
                    StableUnitId = 1, AllianceId = 0, Directive = FormationDirective.Mass, X = 10f, Z = 10f,
                    CombatAvailability = 1300f, Readiness = 0.9f, Morale = 0.9f, Ammo = 0.9f, Supply = 0.9f,
                    OffensiveAllowed = true, DirectMovementAllowed = true,
                },
            },
        };
    }

    private static void CautiousLeadNeedsMoreForAnOffensive()
    {
        var bold = CoordinatedOperationPackageLedger.Build(OffensiveInput(
            CoordinatedOperationOptions.StableDefaults(1000f).ScaledByCommanderInitiative(1f, 0.25f)));
        AssertEqual(CoordinatedOperationDecision.SingleLead, bold.Decision, "bold lead at 1.3: overmatch, goes alone");
        AssertEqual("lead-overmatch", bold.Reason, "bold reason");
        var cautious = CoordinatedOperationPackageLedger.Build(OffensiveInput(
            CoordinatedOperationOptions.StableDefaults(1000f).ScaledByCommanderInitiative(0f, 0.25f)));
        AssertEqual("single-committable-lead", cautious.Reason, "cautious lead at 1.3 < 1.56: no overmatch, commits only at the reinforce ratio 1.06");
        var timid = CoordinatedOperationPackageLedger.Build(OffensiveInput(
            CoordinatedOperationOptions.StableDefaults(1000f).ScaledByCommanderInitiative(0f, 1f)));
        AssertEqual(CoordinatedOperationDecision.Delay, timid.Decision, "influence 1: 1.3 < 0.85 x 2 = 1.7, the lead waits");
    }

    // ---------------------------------------------------------------- review follow-ups

    // GetGroupStrength 6052-6055 (includemorale, AdjustGroupAIStance 4238):
    // × (1 - mi + groupmorale × mi) / max(0.01, 1 - mi + enemymorale × mi),
    // mi = moraleinfluenceonaidecisions (battleprefs 0.25; 54712).
    private static void SectorBlendsWeighGroupMorale()
    {
        var s = Begin();
        var group = Regiment(1, 16, 0.7f, 0.8f, extra: UnitFlags.IsGroup);
        group.GroupMorale = 0.9f;
        s.AddOwn(in group);
        s.IndexCommandTree(null);
        var near = Obs(10, 0.1f, 0.4f);
        near.Z = -200f;
        s.AddObservation(in near);
        SlicePressureBuilder.Build(s, new SliceGeometry { MaxRangeMeters = 1000f, FlankShiftMeters = 150f });
        TroopQuality.SectorBlends(s, 0, 0.25f, 0.25f, out float own, out float enemy);
        AssertNear((0.75f + 0.8f * 0.25f) * (0.75f + 0.9f * 0.25f), own, 1e-5f, "own: groupexperience and groupmorale blends");
        AssertNear((0.75f + 0.1f * 0.25f) * (0.75f + 0.4f * 0.25f), enemy, 1e-5f, "enemy: the closest visible observation's stars and morale");
        TroopQuality.SectorBlends(s, 0, 0f, 0.25f, out float moraleOnly, out float enemyMoraleOnly);
        AssertNear(0.75f + 0.9f * 0.25f, moraleOnly, 1e-5f, "morale alone");
        AssertNear(0.75f + 0.4f * 0.25f, enemyMoraleOnly, 1e-5f, "enemy morale alone");

        var hidden = Begin();
        hidden.AddOwn(in group);
        hidden.IndexCommandTree(null);
        var remembered = Obs(12, 0.5f, 0.6f, remembered: true, confidence: 0.5f);
        hidden.AddObservation(in remembered);
        BattlePerception.Publish(hidden, PerceptionMode.FogGrid, PerceptionFallbackReason.None,
            new[] { remembered }, 1, PerceptionProbeResult.Create(1));
        TroopQuality.SectorBlends(hidden, 0, 0f, 0.25f, out _, out float fallback);
        AssertNear(0.75f + 0.6f * 0.25f, fallback, 1e-5f, "no visible enemy: the perceived (remembered) mean morale");
    }

    private static void SideBlendsWeighMorale()
    {
        TroopQuality.SideBlends(new SideQuality(0.8f, 0.2f), new SideQuality(0.5f, 0.6f), 0.25f, 0.25f, out float own, out float enemy);
        AssertNear(0.8f * 0.95f, own, 1e-6f, "own: experience 0.2, morale 0.8");
        AssertNear(0.9f * 0.875f, enemy, 1e-6f, "perceived: experience 0.6, morale 0.5");
    }

    // CheckGlobalAIStrategy 6396-6430 (battleprefs: defend 0.80, attack 0.80, assault 3.0 / 4.0,
    // influence 0.7): the attack, assault and defend triggers are divided by
    // 1 + initiative × influence; the retreat trigger GetScreeningTrigger() = attack × 0.5
    // (4216, read at 6414) is not. The mod's ladder: AttackNow / Wait / DelayingDefense
    // compare against the divided threshold (vanilla's attack and defend boundary, one value
    // since both triggers are 0.80); WithdrawalToFightLater and the prolonged-delay
    // escalation use undivided thresholds (vanilla's retreat).
    private static TacticalForceBalanceEvidence Ladder(float own, float enemy, float initiative,
        ReinforcementArrival[] arrivals = null, float hours = 0f)
    {
        return new TacticalForceBalanceEvidence(own, enemy, arrivals ?? Array.Empty<ReinforcementArrival>(),
            initiative, 0.5f, 0.8f, battleHoursElapsed: hours, initiativeTriggerInfluence: 0.7f);
    }

    private static ReinforcementOpportunity Decide(TacticalForceBalanceEvidence ev) =>
        TacticalReinforcementOpportunityDoctrine.Decide(ev).Opportunity;

    private static void LadderAttackBoundaryIsDividedLikeVanillaAttack()
    {
        // Own arrivals restore parity inside the window; ratio 0.62: above 1/1.7 at full
        // initiative, below 1/1.35 at mid.
        var arrivals = new[] { new ReinforcementArrival(0.5f, 4000f) };
        AssertEqual(ReinforcementOpportunity.AttackNow, Decide(Ladder(6200f, 10000f, 1f, arrivals)), "0.62 >= 0.588 (initiative 1)");
        AssertEqual(ReinforcementOpportunity.WaitAndConsolidate, Decide(Ladder(6200f, 10000f, 0.5f, arrivals)), "0.62 < 0.741 (initiative 0.5)");
    }

    private static void LadderWaitBandIsBelowTheDividedThreshold()
    {
        var arrivals = new[] { new ReinforcementArrival(2f, 6000f) }; // parity at 2 h, after the attack window
        AssertEqual(ReinforcementOpportunity.WaitAndConsolidate, Decide(Ladder(5000f, 10000f, 1f, arrivals)), "0.5 < 0.588: wait for own arrivals");
        AssertEqual(ReinforcementOpportunity.WaitAndConsolidate, Decide(Ladder(7000f, 10000f, 0f, arrivals)), "0.7 < 1 (initiative 0): wait");
        AssertTrue(Decide(Ladder(7000f, 10000f, 1f, arrivals)) != ReinforcementOpportunity.WaitAndConsolidate,
            "0.7 >= 0.588 (initiative 1): above vanilla's divided attack boundary, no longer waiting");
    }

    private static void LadderDelayingDefenseIsBelowTheDividedThreshold()
    {
        AssertEqual(ReinforcementOpportunity.DelayingDefense, Decide(Ladder(7000f, 10000f, 0f)), "0.7 < 1 at initiative 0: defend (vanilla 6400-6401)");
        AssertEqual(ReinforcementOpportunity.DelayingDefense, Decide(Ladder(5500f, 10000f, 1f)), "0.55 < 0.588 at initiative 1: defend");
    }

    private static void LadderWithdrawalIsUndividedLikeVanillaRetreat()
    {
        // withdrawal threshold at aggression 0.5 is 0.40 (= vanilla's screening 0.80 × 0.5), whatever the initiative.
        AssertEqual(ReinforcementOpportunity.WithdrawalToFightLater, Decide(Ladder(3500f, 10000f, 1f)), "0.35 < 0.40 at initiative 1");
        AssertEqual(ReinforcementOpportunity.WithdrawalToFightLater, Decide(Ladder(3500f, 10000f, 0f)), "0.35 < 0.40 at initiative 0");
        AssertEqual(ReinforcementOpportunity.DelayingDefense, Decide(Ladder(4500f, 10000f, 0f)), "0.45 >= 0.40: no withdrawal at initiative 0");
        AssertEqual(ReinforcementOpportunity.DelayingDefense, Decide(Ladder(4500f, 10000f, 1f)), "0.45 >= 0.40: no withdrawal at initiative 1");
    }

    private static void LadderProlongedEscalationIsUndivided()
    {
        AssertEqual(ReinforcementOpportunity.WithdrawalToFightLater, Decide(Ladder(4500f, 10000f, 1f, hours: 7f)), "0.45 < 0.50 after 6 h: escalates at any initiative");
        AssertEqual(ReinforcementOpportunity.DelayingDefense, Decide(Ladder(5200f, 10000f, 0f, hours: 7f)), "0.52 >= 0.50: no escalation at initiative 0");
    }

    private static void LadderDefensiveHoldAboveTheDividedThreshold()
    {
        // Above the divided boundary without the parity tests: no opinion; vanilla's own
        // CheckGlobalAIStrategy decides (it attacks there, 6403-6406).
        AssertEqual(ReinforcementOpportunity.DefensiveHold, Decide(Ladder(8000f, 10000f, 0.5f)), "0.8 >= 0.741, outnumbered, no arrivals");
        AssertEqual(ReinforcementOpportunity.DelayingDefense, Decide(Ladder(8000f, 10000f, 0f)), "same ratio at initiative 0: below 1, defend");
    }
}
