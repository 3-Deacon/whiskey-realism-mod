using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// Campaign terrain, perk-pick and engineer bug fixes (BUG-AI-011..014, BUG-WL-015,
// BUG-TAC-020): pure decisions, the swap helpers on stand-ins and the real game types, and
// source pins for the runtime wiring the harness does not compile.
internal static class CampaignTerrainFixTests
{
    private sealed class FakeCommander
    {
        public int Branch;
    }

    private static readonly int[] AllArmyPerks = Enumerable.Range(0, 18).ToArray();
    private static readonly int[] AllFleetPerks = Enumerable.Range(0, 11).ToArray();

    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("PerkPickRangeIncludesLastIdWithFix", PerkPickRangeIncludesLastIdWithFix),
            ("PerkScorerReachesRiverExpeditionAndPortDefender", PerkScorerReachesRiverExpeditionAndPortDefender),
            ("PerkScorerGivesSappersNoRiverBonus", PerkScorerGivesSappersNoRiverBonus),
            ("PerkScorerGivesSappersNoCapitalDefenseBonus", PerkScorerGivesSappersNoCapitalDefenseBonus),
            ("PerkScorerNeverPrefersEngineersForAi", PerkScorerNeverPrefersEngineersForAi),
            ("PerkScorerSiegeTakesSiegePerksNotPontoon", PerkScorerSiegeTakesSiegePerksNotPontoon),
            ("PerkScorerRecoveryTakesPontoonSupply", PerkScorerRecoveryTakesPontoonSupply),
            ("PontoonCreekSpeedMatchesTooltip", PontoonCreekSpeedMatchesTooltip),
            ("PontoonCreekSpeedOnlyForCampaignGroupsOnCreeks", PontoonCreekSpeedOnlyForCampaignGroupsOnCreeks),
            ("PontoonLevelFollowsAddPathPrecedence", PontoonLevelFollowsAddPathPrecedence),
            ("GoodGroundCapAppliesOnlyWhereVanillaDoubled", GoodGroundCapAppliesOnlyWhereVanillaDoubled),
            ("PontoonPathCostIsInverseSpeed", PontoonPathCostIsInverseSpeed),
            ("PontoonRouteChoiceIsTimeOptimalWithFix", PontoonRouteChoiceIsTimeOptimalWithFix),
            ("EngineerBranchMasksSelectEngineersForVanillaTest", EngineerBranchMasksSelectEngineersForVanillaTest),
            ("EngineerBranchMasksRestoreOnlyTheirOwnWrites", EngineerBranchMasksRestoreOnlyTheirOwnWrites),
            ("GoodGroundCoverCappedAtNinetyPercent", GoodGroundCoverCappedAtNinetyPercent),
            ("PerceptionMasksPlayerIntelIncidentsOnlyForFogGrid", PerceptionMasksPlayerIntelIncidentsOnlyForFogGrid),
            ("IncidentMaskWorksOnRealIncidentType", IncidentMaskWorksOnRealIncidentType),
            ("AutoresolveDefenderUsesOwnEngineeringPoints", AutoresolveDefenderUsesOwnEngineeringPoints),
            ("PlayerEntrenchmentTermBecomesLinear", PlayerEntrenchmentTermBecomesLinear),
            ("EntrenchmentPatchesPassTheirFlagAndRestore", EntrenchmentPatchesPassTheirFlagAndRestore),
            ("TerrainFixPatchTargetsExist", TerrainFixPatchTargetsExist),
            ("TerrainFixPatchesPassTheirFlagAndRestore", TerrainFixPatchesPassTheirFlagAndRestore),
            ("PerkSelectionPatchUsesFullRangeWithFlag", PerkSelectionPatchUsesFullRangeWithFlag),
            ("PerceptionProbeMasksAndRestoresIncidents", PerceptionProbeMasksAndRestoresIncidents),
        });
    }

    private static void PerkPickRangeIncludesLastIdWithFix()
    {
        AssertEqual(18, PerkSelectionScorer.CandidateLimit(18, true), "army: ids 0..17");
        AssertEqual(11, PerkSelectionScorer.CandidateLimit(11, true), "fleet: ids 0..10");
        AssertEqual(1, PerkSelectionScorer.CandidateLimit(1, true), "single perk");
        AssertEqual(0, PerkSelectionScorer.CandidateLimit(0, true), "no perks");
        AssertEqual(18, PerkSelectionScorer.RandomPickUpperBound(18, true), "replay: Random.Range(0, 18)");
        AssertEqual(17, PerkSelectionScorer.RandomPickUpperBound(18, false), "vanilla: Random.Range(0, 17)");
        AssertEqual(0, PerkSelectionScorer.RandomPickUpperBound(0, true), "no perks");
    }

    // The last ids were never candidates, so the River and PortDefense roles' best perks
    // could not be taken.
    private static void PerkScorerReachesRiverExpeditionAndPortDefender()
    {
        var army = AllArmyPerks.Take(PerkSelectionScorer.CandidateLimit(18, true));
        AssertEqual(17, PerkSelectionScorer.SelectArmyPerk(0, Theater.River, ArmyPerkRole.River, default(PersonalityVector), army), "River Expedition");
        var fleet = AllFleetPerks.Take(PerkSelectionScorer.CandidateLimit(11, true));
        AssertEqual(10, PerkSelectionScorer.SelectFleetPerk(1, FleetPerkRole.PortDefense, fleet), "Port Defender");
    }

    // Sappers and Miners is siege assault only (Autocalc.FightUnit 21100): no river term.
    private static void PerkScorerGivesSappersNoRiverBonus()
    {
        int selected = PerkSelectionScorer.SelectArmyPerk(0, Theater.River, ArmyPerkRole.River, default(PersonalityVector), new[] { 0, 10 });
        AssertEqual(0, selected, "base Siege Train 2.2 over base Sappers 2.1: no river bonus for Sappers");
        selected = PerkSelectionScorer.SelectArmyPerk(0, Theater.River, ArmyPerkRole.River, default(PersonalityVector), new[] { 1, 3 });
        AssertEqual(3, selected, "BMI keeps only its base score");
    }

    // A fort holder defends (CalculateStance 22449 -> stance 2); Sappers needs assault stance 0.
    private static void PerkScorerGivesSappersNoCapitalDefenseBonus()
    {
        int selected = PerkSelectionScorer.SelectArmyPerk(1, Theater.East, ArmyPerkRole.CapitalDefense, default(PersonalityVector), new[] { 10, 15 });
        AssertEqual(15, selected, "Limelights (night siege, either side) over Sappers");
    }

    // Engineers & Mechanics is read only on the player's manual placement (96405-96414).
    private static void PerkScorerNeverPrefersEngineersForAi()
    {
        AssertEqual(14, PerkSelectionScorer.SelectArmyPerk(0, Theater.West, ArmyPerkRole.General, default(PersonalityVector), new[] { 7, 14 }),
            "even Embedded Reporters outranks Engineers");
        foreach (ArmyPerkRole role in Enum.GetValues(typeof(ArmyPerkRole)))
        {
            var others = AllArmyPerks.Where(p => p != 7).ToArray();
            int best = PerkSelectionScorer.SelectArmyPerk(0, Theater.West, role, default(PersonalityVector), others);
            int withEngineers = PerkSelectionScorer.SelectArmyPerk(0, Theater.West, role, default(PersonalityVector), AllArmyPerks);
            AssertEqual(best, withEngineers, role + ": Engineers never wins");
        }
    }

    private static void PerkScorerSiegeTakesSiegePerksNotPontoon()
    {
        AssertEqual(15, PerkSelectionScorer.SelectArmyPerk(0, Theater.East, ArmyPerkRole.Siege, default(PersonalityVector), new[] { 7, 9, 15 }),
            "Limelights over Pontoon Train and Engineers");
        AssertEqual(17, PerkSelectionScorer.SelectArmyPerk(0, Theater.East, ArmyPerkRole.Siege, default(PersonalityVector), new[] { 7, 9, 17 }),
            "River Expedition's siege assault over Pontoon Train");
    }

    private static void PerkScorerRecoveryTakesPontoonSupply()
    {
        AssertEqual(9, PerkSelectionScorer.SelectArmyPerk(0, Theater.West, ArmyPerkRole.Recovery, default(PersonalityVector), new[] { 7, 9 }),
            "Pontoon Train supply speed over Engineers");
    }

    private static void PontoonCreekSpeedMatchesTooltip()
    {
        AssertNear(1.25f, TerrainBugFixes.PontoonCreekSpeedFactor(0), 1e-6f, "I +25%");
        AssertNear(1.5f, TerrainBugFixes.PontoonCreekSpeedFactor(1), 1e-6f, "II +50%");
        AssertNear(1.75f, TerrainBugFixes.PontoonCreekSpeedFactor(2), 1e-6f, "III +75%");
        AssertNear(1.75f, TerrainBugFixes.PontoonCreekSpeedFactor(5), 1e-6f, "clamped like GetPerkLevel");
        AssertNear(1f, TerrainBugFixes.PontoonCreekSpeedFactor(-1), 1e-6f, "no perk");
        AssertEqual(3, TerrainBugFixes.PontoonLevels, "three levels");
    }

    private static void PontoonCreekSpeedOnlyForCampaignGroupsOnCreeks()
    {
        AssertNear(1.5f, TerrainBugFixes.PontoonCreekSpeedMultiplier(true, true, 15, 3, 1), 1e-6f, "corps on creek");
        AssertNear(1.75f, TerrainBugFixes.PontoonCreekSpeedMultiplier(true, true, 18, 3, 2), 1e-6f, "army group on creek");
        AssertNear(1f, TerrainBugFixes.PontoonCreekSpeedMultiplier(false, true, 15, 3, 1), 1e-6f, "flag off: vanilla");
        AssertNear(1f, TerrainBugFixes.PontoonCreekSpeedMultiplier(true, false, 15, 3, 1), 1e-6f, "battle");
        AssertNear(1f, TerrainBugFixes.PontoonCreekSpeedMultiplier(true, true, 15, 5, 1), 1e-6f, "ford: not a small river");
        AssertNear(1f, TerrainBugFixes.PontoonCreekSpeedMultiplier(true, true, 15, 0, 1), 1e-6f, "grassland");
        AssertNear(1f, TerrainBugFixes.PontoonCreekSpeedMultiplier(true, true, 17, 3, 1), 1e-6f, "fleet 9 is Battery Runner");
        AssertNear(1f, TerrainBugFixes.PontoonCreekSpeedMultiplier(true, true, 1, 3, 1), 1e-6f, "battle-table 9 is Cold Steel");
        AssertNear(1f, TerrainBugFixes.PontoonCreekSpeedMultiplier(true, true, 15, 3, -1), 1e-6f, "no perk");
        AssertFalse(TerrainBugFixes.PontoonApplies(true, true, 13), "brigade");
        AssertTrue(TerrainBugFixes.PontoonApplies(true, true, 14), "division");
    }

    // AddPath 130421-130427: num6 = own level, then num6 = parent's level if the parent has it.
    private static void PontoonLevelFollowsAddPathPrecedence()
    {
        AssertEqual(0, TerrainBugFixes.PontoonLevel(true, 2, true, 0), "parent replaces own");
        AssertEqual(2, TerrainBugFixes.PontoonLevel(true, 2, false, 0), "own");
        AssertEqual(1, TerrainBugFixes.PontoonLevel(false, 0, true, 1), "parent only");
        AssertEqual(-1, TerrainBugFixes.PontoonLevel(false, 2, false, 2), "none");
    }

    // CheckCoyCovers 138350 / 138373: vanilla doubles only a battle unit (<= 13) of the W&L
    // player's command with no pending path or building assignment while incident 18 is on.
    private static void GoodGroundCapAppliesOnlyWhereVanillaDoubled()
    {
        AssertTrue(TerrainBugFixes.GoodGroundCapApplies(true, true, 0, false, -1, true), "player's regiment in cover");
        AssertTrue(TerrainBugFixes.GoodGroundCapApplies(true, true, 13, false, -1, true), "brigade HQ");
        AssertFalse(TerrainBugFixes.GoodGroundCapApplies(false, true, 0, false, -1, true), "flag off");
        AssertFalse(TerrainBugFixes.GoodGroundCapApplies(true, false, 0, false, -1, true), "AI or other own unit: vanilla never doubled it");
        AssertFalse(TerrainBugFixes.GoodGroundCapApplies(true, true, 14, false, -1, true), "division HQ returns early");
        AssertFalse(TerrainBugFixes.GoodGroundCapApplies(true, true, 0, true, -1, true), "building assignment returns early");
        AssertFalse(TerrainBugFixes.GoodGroundCapApplies(true, true, 0, false, 0, true), "pending path returns early");
        AssertFalse(TerrainBugFixes.GoodGroundCapApplies(true, true, 0, false, -1, false), "incident not active");
        AssertTrue(TerrainBugFixes.ReadsParentPerk(true, 15), "corps asks its army");
        AssertFalse(TerrainBugFixes.ReadsParentPerk(true, 16) || TerrainBugFixes.ReadsParentPerk(false, 15), "army / battle: no parent perk");
    }

    private static void PontoonPathCostIsInverseSpeed()
    {
        var cost = new float[3];
        TerrainBugFixes.FillPontoonPathCost(cost);
        for (int i = 0; i < cost.Length; i++)
            AssertNear(1f, cost[i] * TerrainBugFixes.PontoonCreekSpeedFactor(i), 1e-6f, "level " + i + ": cost x speed = 1");
        TerrainBugFixes.FillPontoonPathCost(null);
    }

    // W-4: vanilla prices a creek at 50 x 0.25 for Pontoon III but crosses it at 1/50, so the
    // pathfinder takes creeks that are slower than the land detour (and slower than an army
    // without the perk). With the fix the route the pathfinder picks is the fastest one and the
    // perk never makes a march slower. Creek width 1, grass detour of length L (campaignprefs
    // costs: creek 50, grass 4).
    private static void PontoonRouteChoiceIsTimeOptimalWithFix()
    {
        const float creekCost = 50f, grassCost = 4f;
        float[] vanillaPathFactor = { 0.75f, 0.5f, 0.25f };
        var fixedPathFactor = new float[3];
        TerrainBugFixes.FillPontoonPathCost(fixedPathFactor);
        bool vanillaSlowerSeen = false;
        for (int level = 0; level < 3; level++)
        {
            for (float detour = 0.5f; detour <= 20f; detour += 0.25f)
            {
                float landTime = detour * grassCost;
                float noPerkTime = Math.Min(creekCost, landTime);

                // Vanilla: priced with the cost factor, crossed at 1/50.
                bool vanillaTakesCreek = creekCost * vanillaPathFactor[level] < landTime;
                float vanillaTime = vanillaTakesCreek ? creekCost : landTime;
                if (vanillaTime > noPerkTime) vanillaSlowerSeen = true;

                // Fix: priced at 1/speed, crossed at speed x factor.
                float speed = TerrainBugFixes.PontoonCreekSpeedFactor(level);
                bool fixTakesCreek = creekCost * fixedPathFactor[level] < landTime;
                float creekTime = creekCost / speed;
                float fixTime = fixTakesCreek ? creekTime : landTime;
                AssertNear(Math.Min(creekTime, landTime), fixTime, 1e-3f, "fix picks the fastest route, level " + level + " detour " + detour);
                AssertTrue(fixTime <= noPerkTime + 1e-3f, "the perk never slows a march, level " + level + " detour " + detour);
            }
        }
        AssertTrue(vanillaSlowerSeen, "vanilla: the perk can make a march slower than without it");
    }




    private static void GoodGroundCoverCappedAtNinetyPercent()
    {
        const float cap = TerrainBugFixes.GoodGroundCoverCap;
        AssertNear(0.9f, cap, 1e-6f, "integrator decision");
        AssertNear(0.9f, TerrainBugFixes.CapGoodGroundCover(1.1f, 2f, cap), 1e-6f, "parapet 0.55 doubled -> 0.9");
        AssertNear(0.9f, TerrainBugFixes.CapGoodGroundCover(1.65f, 2f, cap), 1e-6f, "with Ace of Spade");
        AssertNear(0.6f, TerrainBugFixes.CapGoodGroundCover(0.6f, 2f, cap), 1e-6f, "fence 0.3 keeps its full doubling");
        AssertNear(0.9f, TerrainBugFixes.CapGoodGroundCover(0.9f, 2f, cap), 1e-6f, "at the cap");
        AssertNear(0.95f, TerrainBugFixes.CapGoodGroundCover(1.9f, 2f, cap), 1e-6f, "never below the cover without the incident");
        AssertNear(1.2f, TerrainBugFixes.CapGoodGroundCover(1.2f, 1f, cap), 1e-6f, "no multiplier: vanilla");
        AssertNear(0.5f, TerrainBugFixes.CapGoodGroundCover(0.5f, 0.5f, cap), 1e-6f, "a reducing multiplier: vanilla");
        AssertTrue(float.IsNaN(TerrainBugFixes.CapGoodGroundCover(float.NaN, 2f, cap)), "NaN untouched");
        // Ranged losses: x clamp01(1 - cover) (126510).
        float losses = Math.Min(Math.Max(1f - TerrainBugFixes.CapGoodGroundCover(1.1f, 2f, cap), 0f), 1f);
        AssertTrue(losses >= 0.1f - 1e-6f, "ranged losses never below 10%");
    }

    private static void PerceptionMasksPlayerIntelIncidentsOnlyForFogGrid()
    {
        AssertTrue(BattlePerception.MasksPlayerIntelIncidents(PerceptionMode.FogGrid, true), "AI side's fog grid");
        AssertFalse(BattlePerception.MasksPlayerIntelIncidents(PerceptionMode.OwnSideWithinFog, true), "player alliance side keeps the incident");
        AssertFalse(BattlePerception.MasksPlayerIntelIncidents(PerceptionMode.RangeOnly, true), "range only never asks the grid");
        AssertFalse(BattlePerception.MasksPlayerIntelIncidents(PerceptionMode.FogGrid, false), "no W&L scenario");
        AssertEqual("60,61,64", string.Join(",", BattlePerception.PlayerIntelIncidents), "Lost Orders and Scouting Ahead (101183)");
        AssertTrue(BattlePerception.IncidentLaunched(0f), "launched at time 0");
        AssertFalse(BattlePerception.IncidentLaunched(BattlePerception.IncidentNotLaunched), "masked value reads not launched");
    }


    private static FakeCommander[] Commanders(params int[] branches)
    {
        return branches.Select(b => new FakeCommander { Branch = b }).ToArray();
    }

    // Vanilla's test at 87443 / 22578 is historicalbranch == 4; during the call it must select
    // exactly the engineers (3), never a navy officer (4).
    private static void EngineerBranchMasksSelectEngineersForVanillaTest()
    {
        var masks = new EngineerBranchMasks<FakeCommander>(c => c.Branch, (c, v) => c.Branch = v);
        FakeCommander[] all = Commanders(-1, 0, 1, 2, 3, 4, 3, 4);
        AssertEqual(2, masks.Apply(all), "two engineers now read as 4");
        AssertTrue(masks.Active, "active");
        AssertEqual("4,6", string.Join(",", Enumerable.Range(0, all.Length).Where(i => all[i].Branch == 4)), "vanilla's == 4 selects the engineers only");
        AssertEqual(TerrainBugFixes.NavyMaskedBranch, all[5].Branch, "navy masked");
        AssertEqual(null, masks.Restore(), "clean restore");
        AssertEqual("-1,0,1,2,3,4,3,4", string.Join(",", all.Select(c => c.Branch)), "all branches back");
        AssertFalse(masks.Active, "inactive");
        AssertEqual(null, masks.Restore(), "second restore is a no-op");
        AssertEqual(0, masks.Apply(null), "no commander list");
    }

    private static void EngineerBranchMasksRestoreOnlyTheirOwnWrites()
    {
        var masks = new EngineerBranchMasks<FakeCommander>(c => c.Branch, (c, v) => c.Branch = v);
        FakeCommander[] all = Commanders(3, 4, 0);
        masks.Apply(all);
        all[0].Branch = 2; // vanilla wrote during the call
        all[1].Branch = 1;
        masks.Restore();
        AssertEqual(2, all[0].Branch, "engineer: vanilla's value kept");
        AssertEqual(1, all[1].Branch, "navy: vanilla's value kept");
        AssertEqual(0, all[2].Branch, "untouched");
        AssertEqual(4, TerrainBugFixes.BranchNavy, "vanilla's tested value");
        AssertEqual(3, TerrainBugFixes.BranchEngineer, "engineer (branchtext 65091)");
    }

    private static void IncidentMaskWorksOnRealIncidentType()
    {
        var incident = (DLC_WL.Incident)RuntimeHelpers.GetUninitializedObject(typeof(DLC_WL.Incident));
        var idle = (DLC_WL.Incident)RuntimeHelpers.GetUninitializedObject(typeof(DLC_WL.Incident));
        incident.launchedtime = 12.5f;
        idle.launchedtime = -1f;
        var mask = new FieldMask<DLC_WL.Incident, float>(i => i.launchedtime, (i, v) => i.launchedtime = v, BattlePerception.IncidentNotLaunched);
        AssertEqual(1, mask.Mask(new[] { incident, null, idle }, i => BattlePerception.IncidentLaunched(i.launchedtime)), "only the launched one");
        AssertFalse(BattlePerception.IncidentLaunched(incident.launchedtime), "masked");
        AssertEqual(null, mask.Restore(i => i == null), "clean restore");
        AssertNear(12.5f, incident.launchedtime, 1e-6f, "launch time back");
        AssertNear(-1f, idle.launchedtime, 1e-6f, "idle untouched");
    }

    // FightUnit 21075-21077: protection = 1 - engineeringpoints[1] / 100 x experience when the
    // side fired at (num2) defends. Points 40 for side 0, 10 for side 1, experience 1.
    private static void AutoresolveDefenderUsesOwnEngineeringPoints()
    {
        float[] points = { 40f, 10f };
        AssertEqual(1, TerrainBugFixes.DefendingSide(true), "unit0 on side 0 fires at side 1");
        AssertEqual(0, TerrainBugFixes.DefendingSide(false), "unit0 on side 1 fires at side 0");
        AssertFalse(TerrainBugFixes.EntrenchmentReadNeedsFix(true, 1), "side 1 defending: vanilla already right");
        AssertTrue(TerrainBugFixes.EntrenchmentReadNeedsFix(true, 0), "side 0 defending: fix");
        AssertFalse(TerrainBugFixes.EntrenchmentReadNeedsFix(false, 0), "flag off: vanilla");

        int defender = TerrainBugFixes.DefendingSide(false);
        float vanilla = 1f - points[TerrainBugFixes.VanillaEntrenchmentReadSide] / 100f;
        AssertNear(0.9f, vanilla, 1e-6f, "vanilla: side 0 protected by side 1's works");
        // The patch writes a two-slot array of the defender's points, so index 1 reads them.
        float[] masked = { points[defender], points[defender] };
        AssertNear(0.6f, 1f - masked[TerrainBugFixes.VanillaEntrenchmentReadSide] / 100f, 1e-6f, "fix: side 0 protected by its own works");
    }

    // ResetEngineeringPoints 87475-87481 with battleprefs k = 0.00125 (622) and an effect factor.
    private static void PlayerEntrenchmentTermBecomesLinear()
    {
        const float k = 0.00125f, effect = 1.5f, strength = 20000f, engineers = 30f;
        foreach (float level in new[] { 0f, 0.25f, 0.5f, 0.8f, 1f })
        {
            float scale = 1f + (effect - 1f) * level;
            float vanillaPlayer = (engineers + level * strength * k * level) * scale;
            float aiSameWorks = (engineers + strength * k * level) * scale;
            float fixedPlayer = vanillaPlayer + TerrainBugFixes.LinearEntrenchmentDelta(level, strength, k, effect);
            AssertNear(aiSameWorks, fixedPlayer, 1e-2f, "player equals the AI's linear rule at level " + level);
        }
        AssertNear(0f, TerrainBugFixes.LinearEntrenchmentDelta(0f, strength, k, effect), 1e-6f, "no works");
        AssertNear(0f, TerrainBugFixes.LinearEntrenchmentDelta(1f, strength, k, effect), 1e-6f, "full works: square equals linear");
        AssertTrue(TerrainBugFixes.LinearEntrenchmentDelta(0.5f, strength, k, effect) > 0f, "half works: the player gains");
        AssertNear(0f, TerrainBugFixes.LinearEntrenchmentDelta(float.NaN, strength, k, effect), 1e-6f, "NaN: vanilla");
    }

    private static void EntrenchmentPatchesPassTheirFlagAndRestore()
    {
        string dir = TestFixtures.FindRepoPath("src/WhiskeyRealism/Patches");
        string side = File.ReadAllText(Path.Combine(dir, "AutocalcEntrenchmentSidePatch.cs"));
        string prefix = Body(side, "internal static void Prefix(Autocalc __instance, Regiment unit0, Regiment unit1)");
        AssertTrue(prefix.Contains("EntrenchmentReadNeedsFix(Enabled(), defending)"), "autoresolve passes the flag");
        AssertTrue(prefix.Contains("TerrainBugFixes.DefendingSide(unit0.alliance == __instance.alliance[0])"), "defender side as vanilla's num2");
        int restore = prefix.IndexOf("Swap.Restore(", StringComparison.Ordinal);
        AssertTrue(restore >= 0 && restore < prefix.IndexOf("Swap.Mask(", StringComparison.Ordinal), "autoresolve Prefix restores before it masks");
        AssertTrue(Body(side, "internal static void Finalizer()").Contains("Swap.Restore("), "autoresolve Finalizer restores");

        string points = File.ReadAllText(Path.Combine(dir, "PlayerEntrenchmentPointsPatch.cs"));
        string postfix = Body(points, "internal static void Postfix(BattleUnits __instance)");
        AssertTrue(postfix.Contains("if (!Enabled() ||"), "player entrenchment passes the flag");
        AssertTrue(postfix.Contains("LinearEntrenchmentDelta(__instance.entrenchmentlevel, __instance.allunitsofalliance[side].totalstrength,"), "uses vanilla's player fields");
        AssertTrue(postfix.Contains("GamePrefs.influenceofentrenchmentlevelonengineeringpoints, GamePrefs.effectentrenchmentonengineeringpoints"), "uses vanilla's prefs");
        AssertTrue(postfix.Contains("__instance.GetSideFromAlliance(GameVars.playeralliance)"), "player side as vanilla");
        AssertTrue(postfix.Contains("bui.deploymentavailabletime += delta;"), "adds to the player's pool");
    }

    private static void TerrainFixPatchTargetsExist()
    {
        AssertTrue(AccessTools.Method(typeof(Regiment), "GetTerrainSpeedAdjustment") != null, "GetTerrainSpeedAdjustment");
        AssertEqual(typeof(float), AccessTools.Method(typeof(Regiment), "GetTerrainSpeedAdjustment").ReturnType, "returns the speed factor");
        AssertTrue(AccessTools.Method(typeof(Regiment), "AddPath") != null, "AddPath");
        AssertTrue(AccessTools.Method(typeof(Regiment), "CheckCoyCovers") != null, "CheckCoyCovers");
        AssertTrue(AccessTools.Method(typeof(Autocalc), "CalculateEngineeringPoints") != null, "CalculateEngineeringPoints");
        AssertTrue(AccessTools.Method(typeof(BattleUnits), "CalculateAllUnitsOfAlliance") != null, "CalculateAllUnitsOfAlliance");
        AssertEqual(typeof(float[]), AccessTools.Field(typeof(GamePrefs), "movementcostpontoontrain").FieldType, "pontoon cost prefs");
        AssertEqual(typeof(int), AccessTools.Field(typeof(GameVars.Commander), "historicalbranch").FieldType, "commander branch");
        AssertEqual(typeof(float), AccessTools.Field(typeof(DLC_WL.Incident), "launchedtime").FieldType, "incident launch time");
        var fight = AccessTools.Method(typeof(Autocalc), "FightUnit");
        AssertTrue(fight != null, "FightUnit");
        AssertEqual("unit0,unit1", string.Join(",", fight.GetParameters().Take(2).Select(p => p.Name)), "FightUnit parameter names");
        AssertEqual(typeof(float[]), AccessTools.Field(typeof(Autocalc), "engineeringpoints").FieldType, "autoresolve engineering points");
        AssertTrue(AccessTools.Method(typeof(BattleUnits), "ResetEngineeringPoints") != null, "ResetEngineeringPoints");
        AssertEqual(typeof(BattleUI), AccessTools.Field(typeof(BattleUnits), "bui").FieldType, "BattleUnits.bui");
        AssertEqual(typeof(float), AccessTools.Field(typeof(BattleUI), "deploymentavailabletime").FieldType, "player deployment points");
    }

    private static void TerrainFixPatchesPassTheirFlagAndRestore()
    {
        string dir = TestFixtures.FindRepoPath("src/WhiskeyRealism/Patches");
        string pontoon = File.ReadAllText(Path.Combine(dir, "PontoonTrainCreekPatch.cs"));
        string speed = Body(pontoon, "internal static void Postfix(Regiment __instance, ref float __result)");
        AssertTrue(speed.Contains("PontoonApplies(Enabled(),"), "speed passes the flag");
        AssertTrue(speed.Contains("__result *= factor;"), "speed applies the factor to vanilla's result");
        AssertTrue(speed.Contains("int ownLevel = ownHas ? __instance.GetPerkLevel("), "own level read only for a holder");
        AssertTrue(speed.Contains("int parentLevel = parentHas ? army.GetPerkLevel("), "parent level read only for a holder");
        AssertFalse(speed.Contains("HasPerkParent(") || speed.Contains("GetPerkLevelParent("), "no per-frame parent-name read");
        string parentArmy = Body(pontoon, "private static Regiment ParentArmy(Regiment unit)");
        AssertTrue(parentArmy.Contains("TerrainBugFixes.ReadsParentPerk(unit.calledfromcampaign, unit.unittyp)"), "parent read only where vanilla's is");
        AssertTrue(parentArmy.Contains("UnityEngine.Transform parent = unit.transform.parent;"), "reads the corps' current parent");
        AssertTrue(parentArmy.Contains("if (!_armyOf.TryGetValue(parent, out Regiment army))") && parentArmy.Contains("_armyOf[parent] = army;"),
            "cache keyed by the parent transform, so a re-parented corps reads its new army");
        AssertTrue(speed.Contains("bool parentHas = (UnityEngine.Object)army != null && army.HasPerk(TerrainBugFixes.CampaignPontoonTrain);"),
            "the parent army's own Pontoon Train counts (AddPath's parent precedence)");
        string pathPrefix = Body(pontoon, "internal static void Prefix(Regiment __instance)");
        AssertTrue(pathPrefix.Contains("PontoonApplies(Enabled(),"), "path cost passes the flag");
        AssertTrue(pathPrefix.IndexOf("Swap.Restore()", StringComparison.Ordinal) < pathPrefix.IndexOf("Swap.Apply(", StringComparison.Ordinal), "path Prefix restores before it applies");
        AssertTrue(Body(pontoon, "internal static void Finalizer()").Contains("Swap.Restore()"), "path Finalizer restores");

        string engineer = File.ReadAllText(Path.Combine(dir, "EngineerBranchBonusPatch.cs"));
        AssertEqual(2, Regex.Matches(engineer, @"if \(!Enabled\(\)\) return;\s*if \(Swap\.Apply\(GameVars\.commander\)").Count, "both engineer Prefixes gate on the flag before swapping");
        var finalizers = Regex.Matches(engineer, @"\[HarmonyFinalizer\]");
        AssertEqual(2, finalizers.Count, "two engineer Finalizers");
        foreach (Match f in finalizers)
            AssertTrue(Body(engineer.Substring(f.Index), "internal static void Finalizer()").Contains("Swap.Restore()"), "each engineer Finalizer restores");
        AssertEqual(2, Regex.Matches(engineer, @"LogRestore\(Swap\.Restore\(\), ""engineer-branch:\w+:restore""\);\s*if \(!Enabled").Count, "both engineer Prefixes restore first");

        string ground = File.ReadAllText(Path.Combine(dir, "GoodGroundCoverCapPatch.cs"));
        string postfix = Body(ground, "internal static void Postfix(Regiment __instance)");
        AssertTrue(postfix.Contains("if (!DLC_WL.dlc_scenarioactive || !__instance.dlcw_isundercommander) return;"), "good ground: only the W&L player's command passes the early return");
        AssertTrue(postfix.Contains("TerrainBugFixes.GoodGroundCapApplies(Enabled(), __instance.dlcw_isundercommander, __instance.unittyp,"), "good ground passes the flag and the unit to the pure decision");
        AssertTrue(postfix.Contains("__instance.GetLastTransmittedPath(),") && postfix.Contains("DLC_WL.IsIncidentActive(GoodGroundIncident))) return;"), "good ground passes vanilla's path and incident tests");
        AssertTrue(postfix.Contains("CapGoodGroundCover(doubled, GamePrefs.dlcwl_incidentgoodgroundcovermultiplier, TerrainBugFixes.GoodGroundCoverCap)"), "good ground uses the integrator cap");
        AssertTrue(postfix.Contains("__instance.covervalue = capped;") && postfix.Contains("__instance.covervaluetemp = capped;"), "good ground writes the capped cover to both vanilla fields");
    }

    private static void PerkSelectionPatchUsesFullRangeWithFlag()
    {
        string patch = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Patches/PerkSelectionPatch.cs");
        AssertTrue(patch.Contains("PerkSelectionScorer.CandidateLimit(perkCount, FixPickRange())"), "steered pick passes the flag");
        AssertTrue(patch.Contains("return !(FixPickRange() && ReplayVanillaPick(faction));"), "unsteered: replay only with the flag, else vanilla");
        string replay = Body(patch, "private static bool ReplayVanillaPick(object faction)");
        AssertEqual(2, Regex.Matches(replay, @"RandomPickUpperBound\([^;]*, true\)").Count, "army and fleet replay use the full range");
        AssertTrue(replay.Contains("unit.dlcw_isundercommander && DLC_WL.dlc_scenarioactive) continue;"), "replay keeps vanilla's W&L eligibility");
        AssertTrue(replay.Contains("if (AICampaign.aiarea == null) return false;"), "replay returns where vanilla returns");
        AssertFalse(patch.Contains("VanillaCandidateLimit"), "old window helper gone");
    }

    private static void PerceptionProbeMasksAndRestoresIncidents()
    {
        string probe = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Tactical/Battle/BattlePerceptionProbe.cs");
        string fill = Body(probe, "private static bool FillProbe(");
        int mask = fill.IndexOf("MaskPlayerIntelIncidents(mode);", StringComparison.Ordinal);
        int enemies = fill.IndexOf("IsEnemyVisible(", StringComparison.Ordinal);
        int reinforcements = fill.IndexOf("AddReinforcements(", StringComparison.Ordinal);
        AssertTrue(mask >= 0 && mask < enemies && mask < reinforcements, "masked before both UnitIsHidden paths");
        int finallyAt = fill.IndexOf("finally", StringComparison.Ordinal);
        AssertTrue(finallyAt > reinforcements && fill.IndexOf("_incidentMask.Restore(", finallyAt, StringComparison.Ordinal) > finallyAt, "restored in finally");
        AssertTrue(Body(probe, "private static void MaskPlayerIntelIncidents(").Contains("BattlePerception.MasksPlayerIntelIncidents(mode, DLC_WL.dlc_scenarioactive)"), "mask only for the AI side's fog grid");
    }

    private static string Body(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        AssertTrue(start >= 0, "found " + signature);
        int open = source.IndexOf('{', start), depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(start, i - start + 1);
        }
        return source.Substring(start);
    }
}
