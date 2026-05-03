# Grand Strategy Project Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the first grand-strategy implementation slice: historical strategy profiles, objective strategy tags, project scoring, and a bounded `ProjectSelectionPatch` that steers vanilla research/project choices without replacing vanilla funding or appointment logic.

**Architecture:** Keep all durable strategy decisions in pure `Strategic/` types that run from weekly review and are testable in `tests/WhiskeyRealism.Tests`. The Harmony patch reads that strategy state and only changes `GameVars.alliance[alliance].nextprojecttoresearch[subsidytype]` when a strategy candidate clearly beats vanilla's current random-weighted pick. Vanilla still owns subsidies, appointability, project completion, and all game-state mutation.

**Tech Stack:** C# `netstandard2.1`, BepInEx 5.4.x, HarmonyX, Unity Mono, console harness at `tests/WhiskeyRealism.Tests`, manual GTCW smoke via BepInEx logs.

---

## Scope

This plan intentionally implements only the first safe vertical slice from `docs/superpowers/specs/2026-05-03-grand-strategy-and-research-tree-design.md`.

In scope:

- `GrandStrategyProfile`
- strategy tags on `ObjectiveMetadata`
- objective score composition with grand-strategy tag weights
- pure project candidate scoring
- `ProjectSelectionPatch` on `AICampaign.UpdateProjects(int alliance)`
- bounded non-spam logging

Out of scope for this plan:

- `PolicySelectionPatch`
- #8 `RecruitmentPatch`
- #7 `PerkSelectionPatch`
- `NavalIntentLedger`
- direct fleet movement or ship construction patches
- data-file edits in the game install

## File Map

- Create `src/WhiskeyRealism/Strategic/StrategyTag.cs`
  - Defines strategy tags shared by objective and project scoring.
- Create `src/WhiskeyRealism/Strategic/GrandStrategyProfile.cs`
  - Immutable-ish profile object plus scoring helpers.
- Create `src/WhiskeyRealism/Strategic/GrandStrategyRegistry.cs`
  - Resolves alliance + era to a profile.
- Create `src/WhiskeyRealism/Strategic/ProjectSelectionScorer.cs`
  - Pure project candidate scoring and replacement threshold logic.
- Modify `src/WhiskeyRealism/Strategic/ObjectiveMetadata.cs`
  - Add `StrategyTags` and helper methods.
- Modify `src/WhiskeyRealism/Strategic/CIC.cs`
  - Compose grand-strategy profile into objective scoring and plan rationale.
- Create `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs`
  - Postfix on `AICampaign.UpdateProjects(int alliance)` that replaces `nextprojecttoresearch` only when safe.
- Modify `tests/WhiskeyRealism.Tests/Program.cs`
  - Add pure tests for profiles, objective tags, and project selection.
- Modify `docs/patch-catalog.md`
  - Add #17 `ProjectSelectionPatch`.
- Modify `docs/handoff.md`
  - Update next action and shipped-local state.

---

### Task 1: Add Strategy Tags and Profiles

**Files:**
- Create: `src/WhiskeyRealism/Strategic/StrategyTag.cs`
- Create: `src/WhiskeyRealism/Strategic/GrandStrategyProfile.cs`
- Create: `src/WhiskeyRealism/Strategic/GrandStrategyRegistry.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing tests for strategy profiles**

Add these entries to the `tests` array in `tests/WhiskeyRealism.Tests/Program.cs`:

```csharp
("union early profile favors blockade and river control", UnionEarlyProfileFavorsBlockadeAndRiver),
("csa early profile favors capital defense and foreign recognition", CsaEarlyProfileFavorsDefenseAndForeignRecognition),
```

Add these test methods:

```csharp
private static void UnionEarlyProfileFavorsBlockadeAndRiver()
{
    var profile = GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861);

    AssertEqual("Union Early Anaconda", profile.Name);
    AssertTrue(profile.WeightFor(StrategyTag.Blockade) > profile.WeightFor(StrategyTag.CapitalDefense),
        "Union early should prioritize blockade over capital defense");
    AssertTrue(profile.WeightFor(StrategyTag.RiverControl) > 0.9f,
        "Union early should strongly weight river control");
}

private static void CsaEarlyProfileFavorsDefenseAndForeignRecognition()
{
    var profile = GrandStrategyRegistry.Resolve(1, EraStage.Amateur1861);

    AssertEqual("CSA Early Cordon", profile.Name);
    AssertTrue(profile.WeightFor(StrategyTag.CapitalDefense) > profile.WeightFor(StrategyTag.Blockade),
        "CSA early should prioritize capital defense over blockade");
    AssertTrue(profile.WeightFor(StrategyTag.ForeignRecognition) > 0.9f,
        "CSA early should strongly weight foreign recognition");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure because `GrandStrategyRegistry` and `StrategyTag` do not exist.

- [ ] **Step 3: Add `StrategyTag.cs`**

Create `src/WhiskeyRealism/Strategic/StrategyTag.cs`:

```csharp
namespace WhiskeyRealism.Strategic
{
    public enum StrategyTag
    {
        Blockade,
        RiverControl,
        CapitalThreat,
        CapitalDefense,
        RailHub,
        ForeignRecognition,
        IndustrialBase,
        Agriculture,
        Manpower,
        ArmyDestruction,
        PortAccess,
        DefensiveDepth,
        Logistics,
        ArmsImports,
        TradeWarfare,
        Recruitment
    }
}
```

- [ ] **Step 4: Add `GrandStrategyProfile.cs`**

Create `src/WhiskeyRealism/Strategic/GrandStrategyProfile.cs`:

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class GrandStrategyProfile
    {
        public int AllianceId;
        public EraStage EraStage;
        public string Name;
        public readonly Dictionary<StrategyTag, float> TagWeights = new Dictionary<StrategyTag, float>();
        public readonly Dictionary<int, float> ProjectWeights = new Dictionary<int, float>();

        public float WeightFor(StrategyTag tag)
        {
            return TagWeights.TryGetValue(tag, out var weight) ? weight : 0f;
        }

        public float ProjectWeightFor(int projectId)
        {
            return ProjectWeights.TryGetValue(projectId, out var weight) ? weight : 0f;
        }

        public GrandStrategyProfile WithTag(StrategyTag tag, float weight)
        {
            TagWeights[tag] = weight;
            return this;
        }

        public GrandStrategyProfile WithProject(int projectId, float weight)
        {
            ProjectWeights[projectId] = weight;
            return this;
        }
    }
}
```

- [ ] **Step 5: Add `GrandStrategyRegistry.cs`**

Create `src/WhiskeyRealism/Strategic/GrandStrategyRegistry.cs`:

```csharp
namespace WhiskeyRealism.Strategic
{
    public static class GrandStrategyRegistry
    {
        public static GrandStrategyProfile Resolve(int allianceId, EraStage stage)
        {
            if (allianceId == 1)
                return ResolveCsa(stage);

            return ResolveUnion(stage);
        }

        private static GrandStrategyProfile ResolveUnion(EraStage stage)
        {
            if (stage == EraStage.TotalWar1864)
            {
                return Base(0, stage, "Union Late Exhaustion")
                    .WithTag(StrategyTag.ArmyDestruction, 1.35f)
                    .WithTag(StrategyTag.RailHub, 1.15f)
                    .WithTag(StrategyTag.IndustrialBase, 1.05f)
                    .WithTag(StrategyTag.Blockade, 0.95f)
                    .WithProject(104, 1.20f)
                    .WithProject(100, 1.10f);
            }

            if (stage == EraStage.Organized1862 || stage == EraStage.HardWar1863)
            {
                return Base(0, stage, "Union Coordinated Pressure")
                    .WithTag(StrategyTag.RiverControl, 1.25f)
                    .WithTag(StrategyTag.RailHub, 1.05f)
                    .WithTag(StrategyTag.Blockade, 1.05f)
                    .WithTag(StrategyTag.Logistics, 1.00f)
                    .WithProject(31, 1.15f)
                    .WithProject(100, 1.10f)
                    .WithProject(105, 1.00f);
            }

            return Base(0, EraStage.Amateur1861, "Union Early Anaconda")
                .WithTag(StrategyTag.Blockade, 1.25f)
                .WithTag(StrategyTag.RiverControl, 1.15f)
                .WithTag(StrategyTag.Logistics, 1.00f)
                .WithTag(StrategyTag.IndustrialBase, 0.90f)
                .WithTag(StrategyTag.CapitalDefense, 0.45f)
                .WithProject(35, 1.20f)
                .WithProject(41, 1.25f)
                .WithProject(31, 1.10f)
                .WithProject(100, 1.00f);
        }

        private static GrandStrategyProfile ResolveCsa(EraStage stage)
        {
            if (stage == EraStage.TotalWar1864)
            {
                return Base(1, stage, "CSA Late Protraction")
                    .WithTag(StrategyTag.CapitalDefense, 1.35f)
                    .WithTag(StrategyTag.ArmyDestruction, 0.95f)
                    .WithTag(StrategyTag.Manpower, 1.20f)
                    .WithTag(StrategyTag.TradeWarfare, 1.05f)
                    .WithProject(118, 1.20f)
                    .WithProject(100, 1.05f);
            }

            if (stage == EraStage.Organized1862 || stage == EraStage.HardWar1863)
            {
                return Base(1, stage, "CSA Offensive Defensive")
                    .WithTag(StrategyTag.CapitalDefense, 1.20f)
                    .WithTag(StrategyTag.DefensiveDepth, 1.10f)
                    .WithTag(StrategyTag.ArmsImports, 1.10f)
                    .WithTag(StrategyTag.TradeWarfare, 1.05f)
                    .WithProject(6, 1.10f)
                    .WithProject(37, 1.10f)
                    .WithProject(106, 1.00f);
            }

            return Base(1, EraStage.Amateur1861, "CSA Early Cordon")
                .WithTag(StrategyTag.CapitalDefense, 1.30f)
                .WithTag(StrategyTag.DefensiveDepth, 1.15f)
                .WithTag(StrategyTag.ForeignRecognition, 1.20f)
                .WithTag(StrategyTag.ArmsImports, 1.05f)
                .WithTag(StrategyTag.PortAccess, 0.95f)
                .WithTag(StrategyTag.Blockade, 0.20f)
                .WithProject(0, 1.00f)
                .WithProject(1, 1.05f)
                .WithProject(6, 1.15f)
                .WithProject(37, 1.05f)
                .WithProject(120, 0.95f);
        }

        private static GrandStrategyProfile Base(int allianceId, EraStage stage, string name)
        {
            return new GrandStrategyProfile
            {
                AllianceId = allianceId,
                EraStage = stage,
                Name = name
            };
        }
    }
}
```

- [ ] **Step 6: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all existing tests plus the two new profile tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/WhiskeyRealism/Strategic/StrategyTag.cs src/WhiskeyRealism/Strategic/GrandStrategyProfile.cs src/WhiskeyRealism/Strategic/GrandStrategyRegistry.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add grand strategy profiles"
```

---

### Task 2: Add Objective Strategy Tags and CIC Scoring

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/ObjectiveMetadata.cs`
- Modify: `src/WhiskeyRealism/Strategic/CIC.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing test for objective tag scoring**

Add this entry to the `tests` array:

```csharp
("grand strategy tags affect objective score", GrandStrategyTagsAffectObjectiveScore),
```

Add this method:

```csharp
private static void GrandStrategyTagsAffectObjectiveScore()
{
    var profile = GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861);
    var blockade = ObjectiveMetadata.DefaultDerived(Theater.Coast, 0f, 0f)
        .WithTag(StrategyTag.Blockade)
        .WithTag(StrategyTag.PortAccess);
    var capital = ObjectiveMetadata.DefaultDerived(Theater.East, 0f, 0f)
        .WithTag(StrategyTag.CapitalDefense);

    float blockadeScore = CIC.ScoreObjectiveForTest(0, profile, PersonalityVector.Zero, blockade);
    float capitalScore = CIC.ScoreObjectiveForTest(0, profile, PersonalityVector.Zero, capital);

    AssertTrue(blockadeScore > capitalScore, "Union early profile should prefer blockade-tagged objective");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure because `ObjectiveMetadata.WithTag` and `CIC.ScoreObjectiveForTest` do not exist.

- [ ] **Step 3: Update `ObjectiveMetadata.cs`**

Replace `ObjectiveMetadata` with this version:

```csharp
using System;

namespace WhiskeyRealism.Strategic
{
    public struct ObjectiveMetadata
    {
        public Theater  Theater;
        public Category Category;
        public float    SupplyReachWeight;
        public float    ForeignRecognitionWeight;
        public float    AttritionWeight;
        public float    GeographicCentroidX;
        public float    GeographicCentroidY;
        public StrategyTag[] StrategyTags;

        public bool IsDerived;

        public ObjectiveMetadata WithTag(StrategyTag tag)
        {
            if (HasTag(tag)) return this;
            var old = StrategyTags ?? Array.Empty<StrategyTag>();
            var next = new StrategyTag[old.Length + 1];
            Array.Copy(old, next, old.Length);
            next[next.Length - 1] = tag;
            StrategyTags = next;
            return this;
        }

        public bool HasTag(StrategyTag tag)
        {
            if (StrategyTags == null) return false;
            for (int i = 0; i < StrategyTags.Length; i++)
                if (StrategyTags[i] == tag) return true;
            return false;
        }

        public float StrategyWeight(GrandStrategyProfile profile)
        {
            if (profile == null || StrategyTags == null) return 0f;
            float score = 0f;
            for (int i = 0; i < StrategyTags.Length; i++)
                score += profile.WeightFor(StrategyTags[i]);
            return score;
        }

        public static ObjectiveMetadata DefaultDerived(Theater theater, float cx, float cy)
        {
            return new ObjectiveMetadata
            {
                Theater = theater,
                Category = Category.Other,
                SupplyReachWeight        = 0.5f,
                ForeignRecognitionWeight = 0.5f,
                AttritionWeight          = 0.5f,
                GeographicCentroidX      = cx,
                GeographicCentroidY      = cy,
                StrategyTags             = Array.Empty<StrategyTag>(),
                IsDerived = true
            };
        }
    }
}
```

- [ ] **Step 4: Update `CIC.cs` scoring**

Change `Replan` so it resolves a profile once after `var p = Effective(era);`:

```csharp
var p = Effective(era);
var strategy = GrandStrategyRegistry.Resolve(AllianceId, era.Stage);
var scored = new List<(object obj, float score, ObjectiveMetadata meta)>();
```

Change the scoring call:

```csharp
float score = ScoreObjective(p, strategy, meta);
```

Replace `ScoreObjective` with:

```csharp
private float ScoreObjective(PersonalityVector p, GrandStrategyProfile strategy, ObjectiveMetadata meta)
{
    return ScoreObjectiveCore(AllianceId, strategy, p, meta);
}

internal static float ScoreObjectiveForTest(int allianceId, GrandStrategyProfile strategy, PersonalityVector p, ObjectiveMetadata meta)
{
    return ScoreObjectiveCore(allianceId, strategy, p, meta);
}

private static float ScoreObjectiveCore(int allianceId, GrandStrategyProfile strategy, PersonalityVector p, ObjectiveMetadata meta)
{
    float theaterPref = FactionProfiles.TheaterPreferenceFor(allianceId, meta.Theater);
    float foreignWeight = FactionProfiles.ForeignRecognitionWeightFor(allianceId);
    float forceRatioTerm = 0.5f;
    float distanceTerm   = 0f;
    float strategyTerm = meta.StrategyWeight(strategy);

    return theaterPref
         + strategyTerm                  * 0.75f
         + meta.SupplyReachWeight        * 1.0f
         + meta.ForeignRecognitionWeight * foreignWeight
         + meta.AttritionWeight          * p.CasualtyTolerance
         + forceRatioTerm                * (1f - p.Caution)
         - distanceTerm                  * (1f - p.Audacity);
}
```

Update the plan rationale string inside `BuildPlan`:

```csharp
Rationale = $"strategy={strategy?.Name ?? "<none>"} theater={meta.Theater} category={meta.Category} forceFrac={forceFraction:F2}",
```

This requires adding `GrandStrategyProfile strategy` to `BuildPlan` parameters and passing it from `Replan`:

```csharp
ActivePlan = BuildPlan(picked.obj, picked.meta, p, strategy, currentMonth, currentYear);
```

- [ ] **Step 5: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Strategic/ObjectiveMetadata.cs src/WhiskeyRealism/Strategic/CIC.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: score objectives with grand strategy"
```

---

### Task 3: Add Pure Project Selection Scorer

**Files:**
- Create: `src/WhiskeyRealism/Strategic/ProjectSelectionScorer.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing project scoring tests**

Add these entries to the `tests` array:

```csharp
("project scorer replaces weak vanilla candidate", ProjectScorerReplacesWeakCandidate),
("project scorer keeps close vanilla candidate", ProjectScorerKeepsCloseCandidate),
```

Add these methods:

```csharp
private static void ProjectScorerReplacesWeakCandidate()
{
    var profile = GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861);
    var candidates = new[]
    {
        new ProjectCandidateInput { ProjectId = 41, SubsidyType = 5, IsAppointable = true, VanillaWeight = 0.2f },
        new ProjectCandidateInput { ProjectId = 96, SubsidyType = 5, IsAppointable = true, VanillaWeight = 0.6f }
    };

    var decision = ProjectSelectionScorer.Select(profile, subsidyType: 5, vanillaProjectId: 96, candidates);

    AssertEqual(true, decision.ShouldReplace);
    AssertEqual(41, decision.ProjectId);
}

private static void ProjectScorerKeepsCloseCandidate()
{
    var profile = GrandStrategyRegistry.Resolve(1, EraStage.Amateur1861);
    var candidates = new[]
    {
        new ProjectCandidateInput { ProjectId = 1, SubsidyType = 5, IsAppointable = true, VanillaWeight = 1.0f },
        new ProjectCandidateInput { ProjectId = 6, SubsidyType = 5, IsAppointable = true, VanillaWeight = 0.9f }
    };

    var decision = ProjectSelectionScorer.Select(profile, subsidyType: 5, vanillaProjectId: 1, candidates);

    AssertEqual(false, decision.ShouldReplace);
    AssertEqual(1, decision.ProjectId);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure because project scoring types do not exist.

- [ ] **Step 3: Add `ProjectSelectionScorer.cs`**

Create `src/WhiskeyRealism/Strategic/ProjectSelectionScorer.cs`:

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class ProjectCandidateInput
    {
        public int ProjectId;
        public int SubsidyType;
        public bool IsAppointable;
        public float VanillaWeight;
    }

    public sealed class ProjectSelectionDecision
    {
        public bool ShouldReplace;
        public int ProjectId;
        public float BestScore;
        public float VanillaScore;
        public string Reason;
    }

    public static class ProjectSelectionScorer
    {
        private const float ReplacementMargin = 0.35f;

        public static ProjectSelectionDecision Select(
            GrandStrategyProfile profile,
            int subsidyType,
            int vanillaProjectId,
            IEnumerable<ProjectCandidateInput> candidates)
        {
            int bestProject = vanillaProjectId;
            float bestScore = Score(profile, vanillaProjectId, 0f);
            float vanillaScore = bestScore;

            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate == null || !candidate.IsAppointable) continue;
                    if (candidate.SubsidyType != subsidyType) continue;

                    float score = Score(profile, candidate.ProjectId, candidate.VanillaWeight);
                    if (candidate.ProjectId == vanillaProjectId)
                        vanillaScore = score;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestProject = candidate.ProjectId;
                    }
                }
            }

            bool replace = bestProject >= 0 &&
                           vanillaProjectId >= 0 &&
                           bestProject != vanillaProjectId &&
                           bestScore >= vanillaScore + ReplacementMargin;

            if (vanillaProjectId < 0 && bestProject >= 0)
                replace = true;

            return new ProjectSelectionDecision
            {
                ShouldReplace = replace,
                ProjectId = replace ? bestProject : vanillaProjectId,
                BestScore = bestScore,
                VanillaScore = vanillaScore,
                Reason = replace ? "strategy-margin" : "vanilla-close"
            };
        }

        private static float Score(GrandStrategyProfile profile, int projectId, float vanillaWeight)
        {
            if (projectId < 0) return -999f;
            return vanillaWeight + (profile?.ProjectWeightFor(projectId) ?? 0f);
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/ProjectSelectionScorer.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: score strategy projects"
```

---

### Task 4: Add `ProjectSelectionPatch`

**Files:**
- Create: `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs`
- Modify: `docs/patch-catalog.md`

- [ ] **Step 1: Create patch skeleton**

Create `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // v0.2.2 grand-strategy steering for vanilla AICampaign.UpdateProjects.
    // Vanilla owns subsidy funding and project appointment. This patch only
    // replaces nextprojecttoresearch[subsidytype] when the current strategy has
    // a clearly better appointable candidate than vanilla's random-weighted pick.
    [HarmonyPatch(typeof(AICampaign), "UpdateProjects")]
    internal static class ProjectSelectionPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(int alliance)
        {
            OnceLog.Info("project-selection", "ProjectSelectionPatch wired");

            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (StrategicCoordinator.Instance == null) return;
                if (alliance < 0 || alliance >= StrategicCoordinator.Instance.CICs.Length) return;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(alliance, playerAlliance)) return;

                var era = StrategicCoordinator.Instance.Eras[alliance]?.Stage ?? EraStage.Amateur1861;
                var profile = GrandStrategyRegistry.Resolve(alliance, era);
                var allianceObj = GetAlliance(alliance);
                if (allianceObj == null) return;

                var nextField = AccessTools.Field(allianceObj.GetType(), "nextprojecttoresearch");
                var next = nextField?.GetValue(allianceObj) as int[];
                if (next == null) return;

                for (int subsidyType = 0; subsidyType < next.Length; subsidyType++)
                {
                    int vanilla = next[subsidyType];
                    var candidates = BuildCandidates(alliance, subsidyType);
                    var decision = ProjectSelectionScorer.Select(profile, subsidyType, vanilla, candidates);
                    if (!decision.ShouldReplace || decision.ProjectId < 0 || decision.ProjectId == vanilla) continue;

                    next[subsidyType] = decision.ProjectId;
                    Plugin.Log.LogInfo(
                        $"[Patch:ProjectSelection] alliance={alliance} subsidy={subsidyType} " +
                        $"old={vanilla} new={decision.ProjectId} profile=\"{profile.Name}\" reason={decision.Reason}");
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-selection:postfix", "[Patch:ProjectSelection] failed: " + ex.Message);
            }
        }

        private static object GetAlliance(int alliance)
        {
            var gameVars = AccessTools.TypeByName("GameVars");
            var alliances = AccessTools.Field(gameVars, "alliance")?.GetValue(null) as Array;
            if (alliances == null || alliance < 0 || alliance >= alliances.Length) return null;
            return alliances.GetValue(alliance);
        }

        private static IEnumerable<ProjectCandidateInput> BuildCandidates(int alliance, int subsidyType)
        {
            var result = new List<ProjectCandidateInput>();
            var projectsType = AccessTools.TypeByName("Projects+LoadedProjects");
            var allField = AccessTools.Field(projectsType, "loadedprojects");
            var all = allField?.GetValue(null) as IEnumerable;
            if (all == null) return result;

            foreach (var project in all)
            {
                if (project == null) continue;

                int projectId = ReadInt(project, "projectid", -1);
                int projectSubsidy = ReadInt(project, "subsidytype", -1);
                if (projectId < 0 || projectSubsidy != subsidyType) continue;

                if (!ProjectApplies(project, alliance)) continue;
                if (!ProjectIsAppointable(project, alliance)) continue;

                result.Add(new ProjectCandidateInput
                {
                    ProjectId = projectId,
                    SubsidyType = projectSubsidy,
                    IsAppointable = true,
                    VanillaWeight = ReadVanillaProjectWeight(alliance, projectId)
                });
            }

            return result;
        }

        private static bool ProjectApplies(object project, int alliance)
        {
            var alliances = AccessTools.Field(project.GetType(), "alliances")?.GetValue(project) as IList;
            if (alliances == null || !alliances.Contains(alliance)) return false;

            var scenarios = AccessTools.Field(project.GetType(), "applicablescenarios")?.GetValue(project) as IList;
            string level = AccessTools.Field(AccessTools.TypeByName("GamePrefs"), "leveltoload")?.GetValue(null) as string;
            if (scenarios != null && !string.IsNullOrEmpty(level) && !scenarios.Contains(level)) return false;

            var fulfills = AccessTools.Method(project.GetType(), "FulfillsDLC");
            return fulfills == null || (bool)fulfills.Invoke(project, null);
        }

        private static bool ProjectIsAppointable(object project, int alliance)
        {
            var projectsType = AccessTools.TypeByName("Projects");
            var method = AccessTools.Method(projectsType, "IsAppointable", new[] { project.GetType(), typeof(int), typeof(bool), typeof(bool) });
            if (method == null) return false;
            return (bool)method.Invoke(null, new object[] { project, alliance, false, false });
        }

        private static float ReadVanillaProjectWeight(int alliance, int projectId)
        {
            try
            {
                var allianceObj = GetAlliance(alliance);
                var personalityMethod = AccessTools.Method(allianceObj.GetType(), "GetAIPersonality", new[] { typeof(int) });
                var personality = personalityMethod?.Invoke(allianceObj, new object[] { alliance });
                if (personality == null) return 0f;

                var projects = AccessTools.Field(personality.GetType(), "projects")?.GetValue(personality) as IList;
                var probs = AccessTools.Field(personality.GetType(), "projectsprob")?.GetValue(personality) as IList;
                if (projects == null || probs == null) return 0f;

                for (int i = 0; i < projects.Count && i < probs.Count; i++)
                    if ((int)projects[i] == projectId)
                        return Convert.ToSingle(probs[i]) / 10f;
            }
            catch { }

            return 0f;
        }

        private static int ReadInt(object target, string field, int fallback)
        {
            try
            {
                var f = AccessTools.Field(target.GetType(), field);
                return f != null ? Convert.ToInt32(f.GetValue(target)) : fallback;
            }
            catch { return fallback; }
        }
    }
}
```

- [ ] **Step 2: Build to catch reflection/type mistakes**

Run:

```bash
./build.sh
```

Expected: `Build succeeded` with `0 Warning(s)` and `0 Error(s)`.

If `Projects+LoadedProjects` or `loadedprojects` does not match the decompile, inspect around the `Projects.LoadedProjects` class and adjust only the type/field names. Keep all failures as logged fallback, not throws.

- [ ] **Step 3: Update patch catalog**

Add row #17 to `docs/patch-catalog.md` after #16:

```markdown
| 17 | `ProjectSelectionPatch` | Postfix | `Patches/ProjectSelectionPatch.cs` | `AICampaign.UpdateProjects` (17487) | v0.2.2 grand-strategy project steering. Lets vanilla choose/fund projects, then replaces `nextprojecttoresearch[subsidy]` only when a strategy-weighted appointable project clearly beats vanilla's current candidate. Logs `[once:project-selection]` and bounded `[Patch:ProjectSelection]` replacement lines. |
```

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs docs/patch-catalog.md
git commit -m "feat: steer project selection"
```

---

### Task 5: Build, Deploy, and Runtime Smoke

**Files:**
- Modify: `docs/handoff.md`
- Modify: `docs/superpowers/specs/2026-05-03-grand-strategy-and-research-tree-design.md`

- [ ] **Step 1: Run pure tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests print `PASS ...` and command exits 0.

- [ ] **Step 2: Build DLL**

Run:

```bash
./build.sh
```

Expected: `Build succeeded` with `0 Warning(s)` and `0 Error(s)`, and `dist/WhiskeyRealism.dll` exists.

- [ ] **Step 3: Deploy DLL**

Run with GTCW closed:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

Expected: command exits 0. If it fails with `Invalid argument`, the game is running and Windows has the DLL locked; close GTCW and rerun the same command.

- [ ] **Step 4: Verify deployed DLL hash**

Run:

```bash
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: both hashes match exactly. Do not ask the user to smoke-test until they match.

- [ ] **Step 5: Runtime smoke instructions**

Ask the user to:

1. Close GTCW if it is open.
2. Start GTCW.
3. Start or load a W&L campaign.
4. Let the campaign run at least one AI update cycle after first campaign map load.

Tail:

```bash
tail -n 160 "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected first-fire marker:

```text
[once:project-selection] ProjectSelectionPatch wired
```

Expected replacement marker only if Whiskey actually changes a vanilla project:

```text
[Patch:ProjectSelection] alliance=... subsidy=... old=... new=... profile="..." reason=strategy-margin
```

No replacement line is acceptable if vanilla already picked a close-enough strategy candidate. The first-fire marker is required.

- [ ] **Step 6: Update docs after build/deploy**

Update `docs/handoff.md` with:

- commit hash for the implementation
- test command result
- build result
- deployed DLL SHA-256
- runtime smoke status for `[once:project-selection]`

Update `docs/superpowers/specs/2026-05-03-grand-strategy-and-research-tree-design.md` status line from:

```markdown
Status: partially implemented for v0.2.2 sequencing. Front/army-area/army-group steering is live locally through #16; objective tags, policy steering, project steering, recruitment intent, and naval intent remain design work.
```

to:

```markdown
Status: partially implemented for v0.2.2 sequencing. Front/army-area/army-group steering and grand-strategy project steering are live locally; policy steering, recruitment intent, role-aware perks, and naval intent remain design work.
```

- [ ] **Step 7: Commit docs**

```bash
git add docs/handoff.md docs/superpowers/specs/2026-05-03-grand-strategy-and-research-tree-design.md
git commit -m "docs: record project selection smoke state"
```

---

## Self-Review

Spec coverage:

- Historical profiles: Task 1.
- Objective strategy tags: Task 2.
- Project steering before policy steering: Tasks 3-4.
- Bounded logging: Task 4.
- Build/deploy/hash discipline: Task 5.
- Recruitment/naval/policy/perk work: explicitly deferred and preserved in docs.

Red-flag scan:

- No deferred markers or undefined future steps are required to complete this plan.
- The only conditional work is reflection-name correction if the decompile symbol name differs at build time; the instruction gives the exact recovery path.

Type consistency:

- `StrategyTag`, `GrandStrategyProfile`, `GrandStrategyRegistry`, `ProjectCandidateInput`, `ProjectSelectionDecision`, and `ProjectSelectionScorer` are introduced before use.
- `CIC.ScoreObjectiveForTest` is internal static and only used by the console harness.
- Harmony patch writes only `nextprojecttoresearch`; it does not appoint projects or mutate strategic mod state.
