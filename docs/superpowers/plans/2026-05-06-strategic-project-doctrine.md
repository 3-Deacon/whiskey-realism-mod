# Strategic Project Doctrine Implementation Plan

Status: active plan for `docs/superpowers/specs/2026-05-06-strategic-project-doctrine-design.md`.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Union and CSA AI project spending historically influenced, dynamically win-seeking, and observable without rewriting vanilla project appointment or effects.

**Architecture:** Add a pure `WhiskeyRealism.Strategic.Projects` doctrine layer for catalog, signals, lane intent, and scoring. `ProjectSelectionPatch` remains the only behavior surface for selection; observer patches only log appointments and init seeding. Fiscal lane starvation is detected and logged, but subsidy mutation stays outside this slice.

**Tech Stack:** BepInEx 5.4.x x64, HarmonyX, C# netstandard2.1, Unity 2021 Mono, console harness in `tests/WhiskeyRealism.Tests`, vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

---

## Source Inputs

Read these before coding:

- `AGENTS.md`
- `MEMORY.md`
- `docs/handoff.md`
- `docs/patch-catalog.md`
- `docs/superpowers/AGENTS.md`
- `src/WhiskeyRealism/Strategic/AGENTS.md`
- `src/WhiskeyRealism/Patches/AGENTS.md`
- `tests/WhiskeyRealism.Tests/AGENTS.md`
- `docs/superpowers/specs/2026-05-06-strategic-project-doctrine-design.md`
- `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs`
- `src/WhiskeyRealism/Strategic/ProjectSelectionScorer.cs`
- `src/WhiskeyRealism/Strategic/Fiscal/FiscalPolicyScorer.cs`

## Verified Vanilla Anchors

Refresh these before implementation:

```bash
rg -n "public static List<LoadedProjects> Import|public float GetSubsidyCost|private static int GetNextProjectRandom|public static bool IsAppointable|public static void AppointProject|public static void CheckProjectUnlocks|private static int UseSubsidyForPurpose|private static void UpdateProjects" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected current anchor areas:

```text
212585: LoadedProjects.Import(string filename)
212635: LoadedProjects.GetSubsidyCost(int level)
213338: Projects.IsAppointable(...)
213384: Projects.AppointProject(...)
213569: Projects.CheckProjectUnlocks(int alliance)
62048: GameVars.Alliance.AIPersonality.GetNextProjectRandom(...)
17487: AICampaign.UpdateProjects(int alliance)
17529: AICampaign.UseSubsidyForPurpose(int alliance, int subsidytype)
```

Also verify `CheckProjectUnlocks` has only the init call site:

```bash
rg -n "CheckProjectUnlocks" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected current result: one call site near new-campaign frame 32 plus the method definition.

## File Structure

Create:

- `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineCatalog.cs`  
  Static project ID catalog, doctrine buckets, UI side, lane, bug review state, and inactive row detection.
- `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineModels.cs`  
  Pure DTOs for signals, runtime facts, score breakdowns, decisions, and lane intent.
- `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineSignalBuilder.cs`  
  Pure clamped signal formulas from cheap scalar inputs.
- `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineScorer.cs`  
  Project selection scorer that composes vanilla weight, grand-strategy static weight, fiscal weight, and dynamic doctrine weight.
- `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineLogGate.cs`  
  Signature helper for bounded `[ProjectDoctrine]` and starved-lane logs.
- `src/WhiskeyRealism/Patches/ProjectAppointmentObserverPatch.cs`  
  Observer-only Postfix for `Projects.AppointProject(...)`.
- `src/WhiskeyRealism/Patches/ProjectUnlockObserverPatch.cs`  
  Observer-only Prefix/Postfix for `Projects.CheckProjectUnlocks(int alliance)`.

Modify:

- `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs`
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- `tests/WhiskeyRealism.Tests/Program.cs`
- `docs/patch-catalog.md`
- `docs/handoff.md`
- `MEMORY.md`

Do not modify:

- `Config/projects.dat`
- `Projects.AppointProject` behavior
- `Projects.CheckProjectUnlocks` behavior
- `Projects.IsAppointable` behavior
- `AICampaign.UseSubsidyForPurpose`
- weapon procurement surfaces such as `AICampaign.CheckPurchaseWeapons` or `WeaponList.PlaceWeaponOrder`

## Task 0: Branch, Anchor, And Scope Check

**Files:**

- Read: `docs/superpowers/specs/2026-05-06-strategic-project-doctrine-design.md`
- Read: `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs`
- Read: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`

- [ ] **Step 1: Confirm dirty worktree and branch**

Run:

```bash
git status --short --branch
```

Expected: note any pre-existing dirty files. Do not revert or include unrelated changes in commits.

- [ ] **Step 2: Refresh vanilla anchors**

Run:

```bash
rg -n "public static List<LoadedProjects> Import|public float GetSubsidyCost|public static bool IsAppointable|public static void AppointProject|public static void CheckProjectUnlocks|private static int GetNextProjectRandom|private static void UpdateProjects|private static int UseSubsidyForPurpose" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected: anchors match the ranges listed above. If line numbers drift but bodies are equivalent, update the plan notes during execution.

- [ ] **Step 3: Verify no lanes 6/7 in vanilla projects**

Run:

```bash
awk 'NR==1{next} {i=(NR-2)%18; if(i==4) lane=$0; if(i==17 && (lane=="6" || lane=="7")) print "lane=" lane}' "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/Config/projects.dat"
```

Expected: no output.

- [ ] **Step 4: Commit nothing**

This task is a read-only gate. Do not commit.

## Task 1: Add Project Doctrine Catalog

**Files:**

- Create: `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineCatalog.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing catalog tests**

Add these entries to the test list in `tests/WhiskeyRealism.Tests/Program.cs` near the existing project scorer tests:

```csharp
("project doctrine catalog maps all active vanilla project rows", ProjectDoctrineCatalogMapsAllActiveRows),
("project doctrine catalog marks market reform fully broken", ProjectDoctrineCatalogMarksMarketReformBroken),
("project doctrine catalog maps organization reform aliases", ProjectDoctrineCatalogMapsOrganizationReformAliases),
("project doctrine catalog has no lane six or seven entries", ProjectDoctrineCatalogHasNoLaneSixOrSevenEntries),
```

Add these test methods near the existing project scorer methods:

```csharp
private static void ProjectDoctrineCatalogMapsAllActiveRows()
{
    AssertEqual(69, WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.AllActive.Count);
    AssertEqual(true, WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.IsInactiveProjectId(20));
    AssertEqual(true, WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.IsInactiveProjectId(87));
    AssertEqual(false, WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.IsInactiveProjectId(88));
}

private static void ProjectDoctrineCatalogMarksMarketReformBroken()
{
    var entry = WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.Get(98);
    AssertEqual(WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.FinanceCreditAdmin, entry.Bucket);
    AssertEqual(WhiskeyRealism.Strategic.Projects.ProjectBugReviewState.FullyBrokenUntilReviewed, entry.BugReviewState);
}

private static void ProjectDoctrineCatalogMapsOrganizationReformAliases()
{
    var wl = WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.Get(89);
    var baseScenario = WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.Get(90);
    AssertEqual(WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.ManpowerTrainingCivilOrder, wl.Bucket);
    AssertEqual(wl.Bucket, baseScenario.Bucket);
    AssertEqual(wl.SubsidyLane, baseScenario.SubsidyLane);
}

private static void ProjectDoctrineCatalogHasNoLaneSixOrSevenEntries()
{
    foreach (var entry in WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.AllActive)
        AssertEqual(false, entry.SubsidyLane == 6 || entry.SubsidyLane == 7);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: FAIL because `WhiskeyRealism.Strategic.Projects` types do not exist.

- [ ] **Step 3: Create catalog file**

Create `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineCatalog.cs`:

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Projects
{
    public enum ProjectDoctrineBucket
    {
        None = 0,
        ArmsImport = 1,
        DomesticWeapons = 2,
        NavalBlockade = 3,
        LogisticsRail = 4,
        FinanceCreditAdmin = 5,
        AgricultureIndustry = 6,
        DiplomacyTradeRecognition = 7,
        ManpowerTrainingCivilOrder = 8
    }

    public enum ProjectUiSide
    {
        Military = 0,
        Civil = 1
    }

    public enum ProjectBugReviewState
    {
        None = 0,
        FullyBrokenUntilReviewed = 1,
        PartiallyBrokenUntilReviewed = 2
    }

    public sealed class ProjectDoctrineEntry
    {
        public int ProjectId;
        public string ShortName;
        public ProjectDoctrineBucket Bucket;
        public ProjectUiSide UiSide;
        public int SubsidyLane;
        public ProjectBugReviewState BugReviewState;
    }

    public static class ProjectDoctrineCatalog
    {
        private static readonly Dictionary<int, ProjectDoctrineEntry> ById = BuildById();
        public static readonly IReadOnlyList<ProjectDoctrineEntry> AllActive = new List<ProjectDoctrineEntry>(ById.Values).AsReadOnly();

        public static bool TryGet(int projectId, out ProjectDoctrineEntry entry)
        {
            return ById.TryGetValue(projectId, out entry);
        }

        public static ProjectDoctrineEntry Get(int projectId)
        {
            return ById.TryGetValue(projectId, out var entry) ? entry : null;
        }

        public static bool IsInactiveProjectId(int projectId)
        {
            return (projectId >= 20 && projectId <= 29) || (projectId >= 42 && projectId <= 87);
        }

        private static Dictionary<int, ProjectDoctrineEntry> BuildById()
        {
            var entries = new[]
            {
                Entry(0, "Austrian Rifles", ProjectDoctrineBucket.ArmsImport, ProjectUiSide.Military, 5),
                Entry(1, "British Rifles", ProjectDoctrineBucket.ArmsImport, ProjectUiSide.Military, 5),
                Entry(2, "British Artillery", ProjectDoctrineBucket.ArmsImport, ProjectUiSide.Military, 5),
                Entry(3, "French Weapons", ProjectDoctrineBucket.ArmsImport, ProjectUiSide.Military, 5),
                Entry(4, "Prussian Weapons", ProjectDoctrineBucket.ArmsImport, ProjectUiSide.Military, 5),
                Entry(5, "Hall's Carbines", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 0),
                Entry(6, "Confederate Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(7, "Cast Artillery", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(8, "Rifled Artillery", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(9, "Parrott Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(10, "Machineguns", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(11, "Confederate Guns", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(12, "Rebore Muskets", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(13, "Legacy Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(14, "Cavalry Carbines", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(15, "Medium Range Carbines", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(16, "Sharps Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(17, "Repeating Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(18, "CSA Springfield Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(19, "USA Springfield Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(30, "Ironclad Monitors", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(31, "Ironclad Gunboats", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(32, "Union Rebuilt Ironclads", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(33, "CSA Rebuilt Ironclads", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(34, "CSA Ironclad Gunboats", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(35, "Modern Warships", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(36, "Confederate Gunboats", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(37, "Armored Gunboats", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(38, "British Warships", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 5),
                Entry(39, "French Warships", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 5),
                Entry(40, "Gloire Class", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 5),
                Entry(41, "Warrior Class", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 5),
                Entry(88, "Command Reform", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(89, "Organization Reform W&L", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(90, "Organization Reform Base", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(91, "Propaganda", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Civil, 0),
                Entry(92, "Counter-propaganda", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Civil, 0),
                Entry(93, "Occupation Administration", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Civil, 0),
                Entry(94, "Suppress Population", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Civil, 0),
                Entry(95, "Administration Reform", ProjectDoctrineBucket.FinanceCreditAdmin, ProjectUiSide.Civil, 0),
                Entry(96, "Subsidize Banks", ProjectDoctrineBucket.FinanceCreditAdmin, ProjectUiSide.Civil, 1),
                Entry(97, "Improve Credit Rating", ProjectDoctrineBucket.FinanceCreditAdmin, ProjectUiSide.Civil, 1),
                Entry(98, "Market Reform", ProjectDoctrineBucket.FinanceCreditAdmin, ProjectUiSide.Civil, 1, ProjectBugReviewState.FullyBrokenUntilReviewed),
                Entry(99, "Infrastructure Reform", ProjectDoctrineBucket.LogisticsRail, ProjectUiSide.Civil, 3),
                Entry(100, "Logistics Reforms", ProjectDoctrineBucket.LogisticsRail, ProjectUiSide.Military, 4),
                Entry(101, "Military Railroad", ProjectDoctrineBucket.LogisticsRail, ProjectUiSide.Military, 4),
                Entry(102, "Weapon Production", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 3),
                Entry(103, "Send Envoys", ProjectDoctrineBucket.DiplomacyTradeRecognition, ProjectUiSide.Civil, 5),
                Entry(104, "Subsidize Industry", ProjectDoctrineBucket.AgricultureIndustry, ProjectUiSide.Civil, 3),
                Entry(105, "Subsidize Agriculture", ProjectDoctrineBucket.AgricultureIndustry, ProjectUiSide.Civil, 2),
                Entry(106, "Trade Warfare", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(107, "Civil Order", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Civil, 4, ProjectBugReviewState.PartiallyBrokenUntilReviewed),
                Entry(108, "Recruit Agents", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(109, "Recruitment Offices", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(110, "Cavalry Reform", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(111, "Cavalry Reform II", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(112, "Artillery Reform", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(113, "Farm Mechanization", ProjectDoctrineBucket.AgricultureIndustry, ProjectUiSide.Civil, 2),
                Entry(114, "Plantation Mechanization", ProjectDoctrineBucket.AgricultureIndustry, ProjectUiSide.Civil, 2),
                Entry(115, "Supply Reform", ProjectDoctrineBucket.LogisticsRail, ProjectUiSide.Military, 4),
                Entry(116, "Cotton is King", ProjectDoctrineBucket.DiplomacyTradeRecognition, ProjectUiSide.Civil, 2),
                Entry(117, "Corn is King", ProjectDoctrineBucket.DiplomacyTradeRecognition, ProjectUiSide.Civil, 2),
                Entry(118, "Training Manuals", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(119, "Railroad Construction", ProjectDoctrineBucket.LogisticsRail, ProjectUiSide.Civil, 0),
                Entry(120, "Improvised Shipyards", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 2),
                Entry(121, "Trade Deals", ProjectDoctrineBucket.DiplomacyTradeRecognition, ProjectUiSide.Civil, 5),
                Entry(122, "Military Education", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(123, "Horse Artillery", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(124, "6-gun Batteries", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4)
            };

            var byId = new Dictionary<int, ProjectDoctrineEntry>();
            foreach (var entry in entries)
                byId[entry.ProjectId] = entry;
            return byId;
        }

        private static ProjectDoctrineEntry Entry(
            int id,
            string shortName,
            ProjectDoctrineBucket bucket,
            ProjectUiSide side,
            int lane,
            ProjectBugReviewState bugState = ProjectBugReviewState.None)
        {
            return new ProjectDoctrineEntry
            {
                ProjectId = id,
                ShortName = shortName,
                Bucket = bucket,
                UiSide = side,
                SubsidyLane = lane,
                BugReviewState = bugState
            };
        }
    }
}
```

- [ ] **Step 4: Include new file in console test project**

Add to `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` near `ProjectSelectionScorer.cs`:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\Projects\ProjectDoctrineCatalog.cs" Link="ProjectDoctrineCatalog.cs" />
```

- [ ] **Step 5: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS count increases by 4.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineCatalog.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add project doctrine catalog"
```

## Task 2: Add Signals And Pure Signal Builder

**Files:**

- Create: `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineModels.cs`
- Create: `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineSignalBuilder.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing signal tests**

Add these test list entries:

```csharp
("project doctrine signals clamp weapon and artillery deficits", ProjectDoctrineSignalsClampWeaponAndArtilleryDeficits),
("project doctrine signals map fiscal posture to credit stress", ProjectDoctrineSignalsMapFiscalPosture),
("project doctrine signals compute late war collapse risk", ProjectDoctrineSignalsComputeLateWarCollapseRisk),
("project doctrine signals keep recognition and port values bounded", ProjectDoctrineSignalsBoundRecognitionAndPort),
```

Add these methods:

```csharp
private static void ProjectDoctrineSignalsClampWeaponAndArtilleryDeficits()
{
    var input = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalInput
    {
        Alliance = 1,
        Era = EraStage.Operational1862,
        FiscalPosture = FiscalPosture.BalancedWar,
        OwnAverageRifles = 0.25f,
        EnemyBestAverageRifles = 0.75f,
        OwnAverageGuns = 0.2f,
        EnemyBestAverageGuns = 0.4f
    };

    var signals = WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalBuilder.Build(input);

    AssertNear(0.6667f, signals.WeaponDeficit, 0.01f);
    AssertNear(0.5f, signals.ArtilleryDeficit, 0.01f);
}

private static void ProjectDoctrineSignalsMapFiscalPosture()
{
    var input = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalInput
    {
        Alliance = 0,
        Era = EraStage.Amateur1861,
        FiscalPosture = FiscalPosture.EmergencySolvency
    };

    var signals = WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalBuilder.Build(input);

    AssertEqual(1f, signals.CreditStress);
}

private static void ProjectDoctrineSignalsComputeLateWarCollapseRisk()
{
    var input = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalInput
    {
        Alliance = 1,
        Era = EraStage.TotalWar1864,
        FiscalPosture = FiscalPosture.CreditDefense,
        ManpowerStressInput = 0.8f,
        StrengthRatio = 0.6f
    };

    var signals = WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalBuilder.Build(input);

    AssertNear(0.7f, signals.LateWarCollapseRisk, 0.01f);
}

private static void ProjectDoctrineSignalsBoundRecognitionAndPort()
{
    var input = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalInput
    {
        Alliance = 1,
        Era = EraStage.Amateur1861,
        FiscalPosture = FiscalPosture.BalancedWar,
        PortViabilityInput = 3f,
        RecognitionProbability = 2f
    };

    var signals = WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalBuilder.Build(input);

    AssertEqual(1f, signals.PortViability);
    AssertEqual(1f, signals.RecognitionWindow);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: FAIL because signal types do not exist.

- [ ] **Step 3: Create models file**

Create `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineModels.cs`:

```csharp
using WhiskeyRealism.Strategic.Fiscal;

namespace WhiskeyRealism.Strategic.Projects
{
    public sealed class ProjectDoctrineSignalInput
    {
        public int Alliance;
        public EraStage Era;
        public FiscalPosture FiscalPosture = FiscalPosture.BalancedWar;
        public float OwnAverageRifles;
        public float EnemyBestAverageRifles;
        public float OwnAverageGuns;
        public float EnemyBestAverageGuns;
        public float OwnTotalTonnage;
        public float EnemyTotalTonnage;
        public float BlockadeRatio;
        public float PortViabilityInput = 0.5f;
        public float ManpowerStressInput;
        public float SupplyPressure;
        public float TransportPressure;
        public float IndustryGapInput;
        public float AgricultureFoodStressInput;
        public float CivilOrderRiskInput;
        public float RecognitionProbability;
        public float OffensiveTempoInput;
        public float StrengthRatio = 1f;
    }

    public sealed class ProjectDoctrineSignals
    {
        public int Alliance;
        public EraStage Era;
        public FiscalPosture FiscalPosture;
        public float WeaponDeficit;
        public float ArtilleryDeficit;
        public float NavalDeficit;
        public float BlockadePressure;
        public float PortViability;
        public float CreditStress;
        public float ManpowerStress;
        public float LogisticsTempoNeed;
        public float IndustryGap;
        public float AgricultureFoodStress;
        public float CivilOrderRisk;
        public float RecognitionWindow;
        public float OffensiveTempoNeed;
        public float LateWarCollapseRisk;
    }

    public sealed class ProjectRuntimeFacts
    {
        public int ProjectId;
        public int SubsidyLane;
        public int DateFromYear;
        public int DateFromMonth;
        public int DateFromDay;
        public float Cost;
        public bool DateFromKnown;
    }

    public sealed class ProjectLaneIntent
    {
        public int Alliance;
        public int SubsidyLane;
        public int QueuedProjectId;
        public float FundingAvailable;
        public float FundingNeeded;
        public float NetFundingPerDay;
        public float TimeToFundEstimateDays;
        public bool ConstructionCurrentlyWins;
        public bool CriticalDoctrineProject;
    }

    public sealed class ProjectDoctrineScore
    {
        public int ProjectId;
        public float VanillaWeight;
        public float ProfileWeight;
        public float FiscalWeight;
        public float DoctrineWeight;
        public float Total;
        public string Reason;
        public bool Suppressed;
        public bool OutOfWindow;
    }

    public sealed class ProjectDoctrineDecision
    {
        public bool ShouldReplace;
        public int ProjectId;
        public float BestScore;
        public float VanillaScore;
        public string Reason;
        public ProjectLaneIntent LaneIntent;
    }
}
```

- [ ] **Step 4: Create signal builder**

Create `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineSignalBuilder.cs`:

```csharp
using System;
using WhiskeyRealism.Strategic.Fiscal;

namespace WhiskeyRealism.Strategic.Projects
{
    public static class ProjectDoctrineSignalBuilder
    {
        public static ProjectDoctrineSignals Build(ProjectDoctrineSignalInput input)
        {
            if (input == null) input = new ProjectDoctrineSignalInput();

            var signals = new ProjectDoctrineSignals
            {
                Alliance = input.Alliance,
                Era = input.Era,
                FiscalPosture = input.FiscalPosture,
                WeaponDeficit = RatioDeficit(input.EnemyBestAverageRifles, input.OwnAverageRifles, 0.01f),
                ArtilleryDeficit = RatioDeficit(input.EnemyBestAverageGuns, input.OwnAverageGuns, 0.01f),
                NavalDeficit = RatioDeficit(input.EnemyTotalTonnage, input.OwnTotalTonnage, 1f),
                BlockadePressure = Clamp01(input.Alliance == 1 ? input.BlockadeRatio : 1f - input.BlockadeRatio),
                PortViability = Clamp01(input.PortViabilityInput),
                CreditStress = CreditStress(input.FiscalPosture),
                ManpowerStress = Clamp01(input.ManpowerStressInput),
                LogisticsTempoNeed = Clamp01(Math.Max(input.SupplyPressure, input.TransportPressure)),
                IndustryGap = Clamp01(input.IndustryGapInput),
                AgricultureFoodStress = Clamp01(input.AgricultureFoodStressInput),
                CivilOrderRisk = Clamp01(input.CivilOrderRiskInput),
                RecognitionWindow = Clamp01(input.RecognitionProbability),
                OffensiveTempoNeed = Clamp01(input.OffensiveTempoInput)
            };

            float strengthCollapse = Clamp01(1f - input.StrengthRatio);
            signals.LateWarCollapseRisk = input.Era == EraStage.TotalWar1864
                ? Clamp01((0.4f * signals.CreditStress) + (0.4f * signals.ManpowerStress) + (0.2f * strengthCollapse))
                : 0f;

            return signals;
        }

        private static float RatioDeficit(float enemy, float own, float floor)
        {
            if (!IsFinite(enemy) || !IsFinite(own)) return 0f;
            float denom = Math.Max(enemy, floor);
            return Clamp01(Math.Max(enemy - own, 0f) / denom);
        }

        private static float CreditStress(FiscalPosture posture)
        {
            if (posture == FiscalPosture.EmergencySolvency) return 1f;
            if (posture == FiscalPosture.CreditDefense) return 0.75f;
            if (posture == FiscalPosture.BalancedWar) return 0.25f;
            return 0f;
        }

        internal static float Clamp01(float value)
        {
            if (!IsFinite(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
```

- [ ] **Step 5: Include files in test project**

Add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\Projects\ProjectDoctrineModels.cs" Link="ProjectDoctrineModels.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Strategic\Projects\ProjectDoctrineSignalBuilder.cs" Link="ProjectDoctrineSignalBuilder.cs" />
```

- [ ] **Step 6: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS count increases by 4.

- [ ] **Step 7: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineModels.cs src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineSignalBuilder.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add project doctrine signals"
```

## Task 3: Add Doctrine Scorer With Suppression, Date Penalties, And Hysteresis

**Files:**

- Create: `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineScorer.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing scorer tests**

Add test list entries:

```csharp
("project doctrine scorer suppresses fully broken market reform", ProjectDoctrineScorerSuppressesMarketReform),
("project doctrine scorer keeps civil order casualty value without raiding value", ProjectDoctrineScorerPartialCivilOrder),
("project doctrine scorer penalizes out of window projects", ProjectDoctrineScorerPenalizesOutOfWindow),
("project doctrine scorer protects half funded queue", ProjectDoctrineScorerProtectsHalfFundedQueue),
("project doctrine scorer lets suppression bypass hysteresis", ProjectDoctrineScorerSuppressionBypassesHysteresis),
```

Add methods:

```csharp
private static void ProjectDoctrineScorerSuppressesMarketReform()
{
    var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
    {
        Alliance = 0,
        Era = EraStage.Amateur1861,
        FiscalPosture = FiscalPosture.BalancedWar
    };

    var candidates = new[]
    {
        new ProjectCandidateInput { ProjectId = 96, SubsidyType = 1, VanillaWeight = 0.2f }
    };

    var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
        GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861),
        signals,
        subsidyType: 1,
        vanillaProjectId: 98,
        vanillaWeight: 1f,
        candidates: candidates,
        fiscalWeight: null,
        runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts { ProjectId = id, SubsidyLane = 1, Cost = 1000f },
        fundingAvailable: 0f,
        netFundingPerDay: 0f,
        constructionCurrentlyWins: false);

    AssertEqual(true, decision.ShouldReplace);
    AssertEqual(96, decision.ProjectId);
    AssertEqual("suppressed-vanilla", decision.Reason);
}

private static void ProjectDoctrineScorerPartialCivilOrder()
{
    var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
    {
        Alliance = 1,
        Era = EraStage.Decisive1863,
        FiscalPosture = FiscalPosture.BalancedWar,
        CivilOrderRisk = 1f
    };

    float score = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.ScoreDoctrineOnly(107, signals);

    AssertEqual(true, score > 0f);
    AssertEqual(true, score < 1.5f);
}

private static void ProjectDoctrineScorerPenalizesOutOfWindow()
{
    var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
    {
        Alliance = 0,
        Era = EraStage.Amateur1861,
        FiscalPosture = FiscalPosture.BalancedWar,
        NavalDeficit = 1f
    };

    var candidates = new[]
    {
        new ProjectCandidateInput { ProjectId = 31, SubsidyType = 4, VanillaWeight = 0.2f }
    };

    var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
        GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861),
        signals,
        subsidyType: 4,
        vanillaProjectId: 35,
        vanillaWeight: 1f,
        candidates: candidates,
        fiscalWeight: null,
        runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts
        {
            ProjectId = id,
            SubsidyLane = 4,
            Cost = 1000f,
            DateFromKnown = id == 35,
            DateFromYear = id == 35 ? 1864 : 0,
            DateFromMonth = 1,
            DateFromDay = 1
        },
        fundingAvailable: 0f,
        netFundingPerDay: 0f,
        constructionCurrentlyWins: false);

    AssertEqual(true, decision.ShouldReplace);
    AssertEqual(31, decision.ProjectId);
}

private static void ProjectDoctrineScorerProtectsHalfFundedQueue()
{
    var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
    {
        Alliance = 1,
        Era = EraStage.Operational1862,
        FiscalPosture = FiscalPosture.BalancedWar,
        WeaponDeficit = 1f
    };

    var candidates = new[]
    {
        new ProjectCandidateInput { ProjectId = 6, SubsidyType = 4, VanillaWeight = 0.8f }
    };

    var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
        GrandStrategyRegistry.Resolve(1, EraStage.Operational1862),
        signals,
        subsidyType: 4,
        vanillaProjectId: 11,
        vanillaWeight: 1f,
        candidates: candidates,
        fiscalWeight: null,
        runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts { ProjectId = id, SubsidyLane = 4, Cost = 1000f },
        fundingAvailable: 600f,
        netFundingPerDay: 20f,
        constructionCurrentlyWins: false);

    AssertEqual(false, decision.ShouldReplace);
    AssertEqual(11, decision.ProjectId);
    AssertEqual("queued-half-funded", decision.Reason);
}

private static void ProjectDoctrineScorerSuppressionBypassesHysteresis()
{
    var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
    {
        Alliance = 0,
        Era = EraStage.Operational1862,
        FiscalPosture = FiscalPosture.BalancedWar
    };

    var candidates = new[]
    {
        new ProjectCandidateInput { ProjectId = 96, SubsidyType = 1, VanillaWeight = 0.1f }
    };

    var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
        GrandStrategyRegistry.Resolve(0, EraStage.Operational1862),
        signals,
        subsidyType: 1,
        vanillaProjectId: 98,
        vanillaWeight: 1f,
        candidates: candidates,
        fiscalWeight: null,
        runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts { ProjectId = id, SubsidyLane = 1, Cost = 1000f },
        fundingAvailable: 900f,
        netFundingPerDay: 50f,
        constructionCurrentlyWins: false);

    AssertEqual(true, decision.ShouldReplace);
    AssertEqual(96, decision.ProjectId);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: FAIL because `ProjectDoctrineScorer` does not exist.

- [ ] **Step 3: Create scorer file**

Create `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineScorer.cs`:

```csharp
using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic.Fiscal;

namespace WhiskeyRealism.Strategic.Projects
{
    public static class ProjectDoctrineScorer
    {
        public const float ReplacementMargin = 0.35f;
        private const float SuppressedScore = -1000f;

        public static ProjectDoctrineDecision Select(
            GrandStrategyProfile profile,
            ProjectDoctrineSignals signals,
            int subsidyType,
            int vanillaProjectId,
            float vanillaWeight,
            IEnumerable<ProjectCandidateInput> candidates,
            Func<int, float> fiscalWeight,
            Func<int, ProjectRuntimeFacts> runtimeFacts,
            float fundingAvailable,
            float netFundingPerDay,
            bool constructionCurrentlyWins)
        {
            if (signals == null) signals = new ProjectDoctrineSignals();
            ProjectRuntimeFacts vanillaFacts = Facts(runtimeFacts, vanillaProjectId, subsidyType);
            ProjectDoctrineScore vanillaScore = ScoreProject(profile, signals, vanillaProjectId, vanillaWeight, fiscalWeight, vanillaFacts);

            int bestProjectId = vanillaProjectId;
            ProjectDoctrineScore bestScore = vanillaScore;
            bool anyReplacementCandidate = false;

            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate == null || candidate.SubsidyType != subsidyType) continue;
                    ProjectRuntimeFacts facts = Facts(runtimeFacts, candidate.ProjectId, subsidyType);
                    ProjectDoctrineScore score = ScoreProject(profile, signals, candidate.ProjectId, candidate.VanillaWeight, fiscalWeight, facts);
                    if (score.Suppressed) continue;
                    anyReplacementCandidate = true;
                    if (score.Total > bestScore.Total)
                    {
                        bestProjectId = candidate.ProjectId;
                        bestScore = score;
                    }
                }
            }

            var laneIntent = BuildLaneIntent(signals.Alliance, subsidyType, vanillaProjectId, fundingAvailable, vanillaFacts, netFundingPerDay, constructionCurrentlyWins, vanillaScore);

            if (vanillaScore.Suppressed && anyReplacementCandidate && bestProjectId != vanillaProjectId)
                return Decision(true, bestProjectId, bestScore.Total, vanillaScore.Total, "suppressed-vanilla", laneIntent);

            if (vanillaScore.OutOfWindow && anyReplacementCandidate && bestProjectId != vanillaProjectId && bestScore.Total >= vanillaScore.Total)
                return Decision(true, bestProjectId, bestScore.Total, vanillaScore.Total, "out-of-window-vanilla", laneIntent);

            bool halfFunded = vanillaProjectId >= 0 && vanillaFacts != null && vanillaFacts.Cost > 0f && fundingAvailable >= vanillaFacts.Cost * 0.5f;
            float margin = halfFunded ? ReplacementMargin * 2f : ReplacementMargin;
            bool shouldReplace = bestProjectId >= 0 && bestProjectId != vanillaProjectId && bestScore.Total >= vanillaScore.Total + margin;

            if (shouldReplace)
                return Decision(true, bestProjectId, bestScore.Total, vanillaScore.Total, halfFunded ? "strategy-double-margin" : "strategy-margin", laneIntent);

            return Decision(false, vanillaProjectId, bestScore.Total, vanillaScore.Total, halfFunded ? "queued-half-funded" : "vanilla-close", laneIntent);
        }

        public static float ScoreDoctrineOnly(int projectId, ProjectDoctrineSignals signals)
        {
            return ScoreDoctrine(projectId, signals ?? new ProjectDoctrineSignals(), ProjectDoctrineCatalog.Get(projectId));
        }

        private static ProjectDoctrineScore ScoreProject(
            GrandStrategyProfile profile,
            ProjectDoctrineSignals signals,
            int projectId,
            float vanillaWeight,
            Func<int, float> fiscalWeight,
            ProjectRuntimeFacts facts)
        {
            var entry = ProjectDoctrineCatalog.Get(projectId);
            if (projectId < 0 || ProjectDoctrineCatalog.IsInactiveProjectId(projectId) || entry == null)
            {
                return new ProjectDoctrineScore { ProjectId = projectId, Total = -999f, Reason = "inactive", Suppressed = true };
            }

            if (entry.BugReviewState == ProjectBugReviewState.FullyBrokenUntilReviewed)
            {
                return new ProjectDoctrineScore { ProjectId = projectId, Total = SuppressedScore, Reason = "fully-broken", Suppressed = true };
            }

            float profileWeight = profile != null ? profile.ProjectWeightFor(projectId) : 0f;
            float fiscal = fiscalWeight != null ? fiscalWeight.Invoke(projectId) : 0f;
            float doctrine = ScoreDoctrine(projectId, signals, entry);
            bool outOfWindow = IsOutOfWindow(signals.Era, facts);
            if (outOfWindow)
                doctrine -= ReplacementMargin + 0.25f;

            float total = vanillaWeight + profileWeight + fiscal + doctrine;
            return new ProjectDoctrineScore
            {
                ProjectId = projectId,
                VanillaWeight = vanillaWeight,
                ProfileWeight = profileWeight,
                FiscalWeight = fiscal,
                DoctrineWeight = doctrine,
                Total = total,
                Reason = outOfWindow ? "out-of-window" : entry.Bucket.ToString(),
                Suppressed = false,
                OutOfWindow = outOfWindow
            };
        }

        private static float ScoreDoctrine(int projectId, ProjectDoctrineSignals signals, ProjectDoctrineEntry entry)
        {
            if (entry == null) return 0f;
            float score = 0f;

            if (entry.Bucket == ProjectDoctrineBucket.ArmsImport)
                score += signals.Alliance == 1 ? 0.8f + signals.WeaponDeficit : Math.Max(0f, signals.WeaponDeficit - 0.35f);
            if (entry.Bucket == ProjectDoctrineBucket.DomesticWeapons)
                score += (0.7f * signals.WeaponDeficit) + (0.5f * signals.ArtilleryDeficit) + (0.3f * signals.IndustryGap);
            if (entry.Bucket == ProjectDoctrineBucket.NavalBlockade)
                score += (0.8f * signals.NavalDeficit) + (0.6f * signals.BlockadePressure) + (signals.PortViability < 0.25f && signals.Alliance == 1 ? -0.8f : 0f);
            if (entry.Bucket == ProjectDoctrineBucket.LogisticsRail)
                score += 0.9f * signals.LogisticsTempoNeed;
            if (entry.Bucket == ProjectDoctrineBucket.FinanceCreditAdmin)
                score += 1.1f * signals.CreditStress;
            if (entry.Bucket == ProjectDoctrineBucket.AgricultureIndustry)
                score += (0.7f * signals.IndustryGap) + (0.7f * signals.AgricultureFoodStress);
            if (entry.Bucket == ProjectDoctrineBucket.DiplomacyTradeRecognition)
                score += signals.Alliance == 1 ? 0.9f * signals.RecognitionWindow : 0.45f * signals.RecognitionWindow;
            if (entry.Bucket == ProjectDoctrineBucket.ManpowerTrainingCivilOrder)
                score += (0.7f * signals.ManpowerStress) + (0.5f * signals.OffensiveTempoNeed) + (0.6f * signals.CivilOrderRisk);

            if (projectId == 97 && signals.CreditStress >= 0.75f) score += 1.0f;
            if (projectId == 107) score += 0.4f * signals.CivilOrderRisk;
            if (projectId == 106 && signals.Alliance == 1) score += 0.5f * signals.BlockadePressure;
            if (projectId == 118) score += 0.5f * signals.LateWarCollapseRisk;

            if (signals.FiscalPosture >= FiscalPosture.CreditDefense && (projectId == 35 || projectId == 38 || projectId == 39 || projectId == 40 || projectId == 41))
                score -= 1.0f;

            return score;
        }

        private static bool IsOutOfWindow(EraStage era, ProjectRuntimeFacts facts)
        {
            if (facts == null || !facts.DateFromKnown) return false;
            if (facts.DateFromYear >= 1864 && era != EraStage.TotalWar1864) return true;
            if (facts.DateFromYear >= 1863 && era == EraStage.Amateur1861) return true;
            return false;
        }

        private static ProjectRuntimeFacts Facts(Func<int, ProjectRuntimeFacts> runtimeFacts, int projectId, int subsidyType)
        {
            if (runtimeFacts != null)
            {
                var facts = runtimeFacts.Invoke(projectId);
                if (facts != null) return facts;
            }

            return new ProjectRuntimeFacts { ProjectId = projectId, SubsidyLane = subsidyType };
        }

        private static ProjectLaneIntent BuildLaneIntent(
            int alliance,
            int subsidyType,
            int queuedProjectId,
            float fundingAvailable,
            ProjectRuntimeFacts facts,
            float netFundingPerDay,
            bool constructionCurrentlyWins,
            ProjectDoctrineScore score)
        {
            float needed = facts != null ? facts.Cost : 0f;
            float costToGo = Math.Max(0f, needed - fundingAvailable);
            return new ProjectLaneIntent
            {
                Alliance = alliance,
                SubsidyLane = subsidyType,
                QueuedProjectId = queuedProjectId,
                FundingAvailable = fundingAvailable,
                FundingNeeded = needed,
                NetFundingPerDay = Math.Max(0f, netFundingPerDay),
                TimeToFundEstimateDays = netFundingPerDay > 0f ? costToGo / netFundingPerDay : float.PositiveInfinity,
                ConstructionCurrentlyWins = constructionCurrentlyWins,
                CriticalDoctrineProject = score != null && score.Total >= 1.25f
            };
        }

        private static ProjectDoctrineDecision Decision(bool replace, int projectId, float best, float vanilla, string reason, ProjectLaneIntent laneIntent)
        {
            return new ProjectDoctrineDecision
            {
                ShouldReplace = replace,
                ProjectId = projectId,
                BestScore = best,
                VanillaScore = vanilla,
                Reason = reason,
                LaneIntent = laneIntent
            };
        }
    }
}
```

- [ ] **Step 4: Include scorer in test project**

Add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\Projects\ProjectDoctrineScorer.cs" Link="ProjectDoctrineScorer.cs" />
```

- [ ] **Step 5: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS count increases by 5.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineScorer.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: score strategic project doctrine"
```

## Task 4: Add Doctrine Log Gate And Lane Intent Tests

**Files:**

- Create: `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineLogGate.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing log-gate tests**

Add test list entries:

```csharp
("project doctrine log gate suppresses repeated signatures", ProjectDoctrineLogGateSuppressesRepeatedSignatures),
("project lane intent estimates days from observed rate", ProjectLaneIntentEstimatesDaysFromObservedRate),
```

Add methods:

```csharp
private static void ProjectDoctrineLogGateSuppressesRepeatedSignatures()
{
    var gate = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineLogGate();
    string first = WhiskeyRealism.Strategic.Projects.ProjectDoctrineLogGate.SelectionSignature(1, 4, 11, 6, "strategy-margin");
    string repeat = WhiskeyRealism.Strategic.Projects.ProjectDoctrineLogGate.SelectionSignature(1, 4, 11, 6, "strategy-margin");
    string changed = WhiskeyRealism.Strategic.Projects.ProjectDoctrineLogGate.SelectionSignature(1, 4, 11, 118, "strategy-margin");

    AssertEqual(true, gate.ShouldLog(first));
    AssertEqual(false, gate.ShouldLog(repeat));
    AssertEqual(true, gate.ShouldLog(changed));
    AssertEqual(false, gate.ShouldLog(first));
    AssertEqual(false, gate.ShouldLog(null));
    AssertEqual(false, gate.ShouldLog(""));
}

private static void ProjectLaneIntentEstimatesDaysFromObservedRate()
{
    var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
    {
        Alliance = 0,
        Era = EraStage.Amateur1861,
        FiscalPosture = FiscalPosture.BalancedWar
    };

    var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
        GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861),
        signals,
        subsidyType: 4,
        vanillaProjectId: 100,
        vanillaWeight: 1f,
        candidates: new ProjectCandidateInput[0],
        fiscalWeight: null,
        runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts { ProjectId = id, SubsidyLane = 4, Cost = 1000f },
        fundingAvailable: 250f,
        netFundingPerDay: 25f,
        constructionCurrentlyWins: true);

    AssertEqual(30f, decision.LaneIntent.TimeToFundEstimateDays);
    AssertEqual(true, decision.LaneIntent.ConstructionCurrentlyWins);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: FAIL because `ProjectDoctrineLogGate` does not exist.

- [ ] **Step 3: Create log gate**

Create `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineLogGate.cs`:

```csharp
namespace WhiskeyRealism.Strategic.Projects
{
    public sealed class ProjectDoctrineLogGate
    {
        private readonly System.Collections.Generic.HashSet<string> seenSignatures = new System.Collections.Generic.HashSet<string>();

        public bool ShouldLog(string signature)
        {
            if (string.IsNullOrEmpty(signature)) return false;
            return seenSignatures.Add(signature);
        }

        public static string SelectionSignature(int alliance, int lane, int oldProjectId, int newProjectId, string reason)
        {
            return alliance + "|" + lane + "|" + oldProjectId + "|" + newProjectId + "|" + (reason ?? "");
        }

        public static string StarvedLaneSignature(ProjectLaneIntent intent)
        {
            if (intent == null) return "missing";
            return intent.Alliance + "|" + intent.SubsidyLane + "|" + intent.QueuedProjectId + "|"
                + intent.FundingAvailable.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "|"
                + intent.FundingNeeded.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "|"
                + intent.NetFundingPerDay.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "|"
                + intent.TimeToFundEstimateDays.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "|"
                + intent.ConstructionCurrentlyWins + "|" + intent.CriticalDoctrineProject;
        }
    }
}
```

- [ ] **Step 4: Include log gate in test project**

Add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\Projects\ProjectDoctrineLogGate.cs" Link="ProjectDoctrineLogGate.cs" />
```

- [ ] **Step 5: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS count increases by 2.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineLogGate.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add project doctrine telemetry gate"
```

## Task 5: Wire Doctrine Into ProjectSelectionPatch

**Files:**

- Modify: `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs`

- [ ] **Step 1: Re-read patch and vanilla surface**

Run:

```bash
sed -n '1,220p' src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs
sed -n '17487,17590p' /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected: `ProjectSelectionPatch` still patches `AICampaign.UpdateProjects` Prefix and vanilla still appoints only after `subsidyfunding[lane] >= cost`.

- [ ] **Step 2: Add namespace import**

Add:

```csharp
using WhiskeyRealism.Strategic.Projects;
```

- [ ] **Step 3: Add static log gates**

Inside `ProjectSelectionPatch`, add:

```csharp
private static readonly ProjectDoctrineLogGate SelectionLogGate = new ProjectDoctrineLogGate();
private static readonly ProjectDoctrineLogGate StarvedLaneLogGate = new ProjectDoctrineLogGate();
```

- [ ] **Step 4: Short-circuit non-project lanes**

Inside the `for (int subsidyType = 0; subsidyType < lanes; subsidyType++)` loop, before `UseSubsidyForPurpose`, add:

```csharp
if (subsidyType == 6 || subsidyType == 7) continue;
```

- [ ] **Step 5: Add catalog once-log**

Before entering the lane loop, after `profile` and `fiscalIntent` are resolved, add:

```csharp
OnceLog.Info("project-doctrine-catalog", $"Project doctrine catalog active entries={ProjectDoctrineCatalog.AllActive.Count}");
```

- [ ] **Step 6: Preserve construction gate but capture starvation**

Replace:

```csharp
if (UseSubsidyForPurpose(alliance, subsidyType) != 0) continue;
```

with:

```csharp
bool constructionCurrentlyWins = UseSubsidyForPurpose(alliance, subsidyType) != 0;
if (constructionCurrentlyWins && nextProjects[subsidyType] < 0) continue;
```

This keeps vanilla construction preference but still lets a queued, starved critical project be logged.

- [ ] **Step 7: Build doctrine signals from cheap runtime state**

Before calling the scorer in the loop, add:

```csharp
var signals = BuildSignals(alliance, era, fiscalIntent);
float fundingAvailable = allianceState.subsidyfunding != null && subsidyType < allianceState.subsidyfunding.Length
    ? allianceState.subsidyfunding[subsidyType]
    : 0f;
```

- [ ] **Step 8: Replace scorer call**

Replace the `ProjectSelectionScorer.Select(...)` call with:

```csharp
var decision = ProjectDoctrineScorer.Select(
    profile,
    signals,
    subsidyType,
    vanillaProjectId,
    vanillaWeight,
    candidates,
    projectId => FiscalPolicyScorer.ProjectWeight(fiscalIntent, alliance, projectId, subsidyType),
    projectId => BuildRuntimeFacts(projectId, subsidyType, alliance),
    fundingAvailable,
    netFundingPerDay: 0f,
    constructionCurrentlyWins: constructionCurrentlyWins);
```

- [ ] **Step 9: Log starved critical lanes**

After `decision` is computed and before replacement, add a starved-lane observer. In the hot patch, gate this with a stable lane/project/state signature instead of `ProjectDoctrineLogGate.StarvedLaneSignature(decision.LaneIntent)`: that helper intentionally includes funding trajectory fields for non-hot telemetry, and using it here would log again as subsidy funding changes. The patch log may still print current funding values in the message.

```csharp
if (decision.LaneIntent != null && decision.LaneIntent.CriticalDoctrineProject && decision.LaneIntent.ConstructionCurrentlyWins)
{
    string starvedSignature = StableStarvedLaneSignature(decision.LaneIntent);
    if (StarvedLaneLogGate.ShouldLog(starvedSignature))
    {
        OnceLog.Info("project-doctrine-starved-lane", "Project doctrine starved-lane observer wired");
        Plugin.Log.LogInfo(
            $"[ProjectDoctrine] alliance={alliance} lane={subsidyType} queued={decision.LaneIntent.QueuedProjectId} " +
            $"funding={decision.LaneIntent.FundingAvailable:F0}/{decision.LaneIntent.FundingNeeded:F0} " +
            $"rate={(decision.LaneIntent.NetFundingPerDay > 0f ? decision.LaneIntent.NetFundingPerDay.ToString(\"F0\") : \"unknown\")} " +
            $"constructionWins={decision.LaneIntent.ConstructionCurrentlyWins} reason=starved-critical-project");
    }
}
```

Add the local stable signature helper:

```csharp
private static string StableStarvedLaneSignature(ProjectLaneIntent intent)
{
    if (intent == null)
        return null;

    return intent.Alliance + "|"
        + intent.SubsidyLane + "|"
        + intent.QueuedProjectId + "|"
        + intent.ConstructionCurrentlyWins + "|"
        + intent.CriticalDoctrineProject;
}
```

- [ ] **Step 10: Bound selection logs by signature**

Replace the unconditional replacement log with:

```csharp
string signature = ProjectDoctrineLogGate.SelectionSignature(alliance, subsidyType, vanillaProjectId, decision.ProjectId, decision.Reason);
if (SelectionLogGate.ShouldLog(signature))
{
    OnceLog.Info("project-doctrine-selection", "Project doctrine selection observer wired");
    Plugin.Log.LogInfo(
        $"[ProjectDoctrine] alliance={alliance} lane={subsidyType} old={vanillaProjectId} new={decision.ProjectId} " +
        $"era={era} funding={fundingAvailable:F0} vanillaScore={decision.VanillaScore:F2} " +
        $"bestScore={decision.BestScore:F2} reason={decision.Reason}");
}
```

- [ ] **Step 11: Add runtime fact helper**

Add this helper near `ResolveProject`:

```csharp
private static ProjectRuntimeFacts BuildRuntimeFacts(int projectId, int subsidyType, int alliance)
{
    var project = ResolveProject(projectId);
    if (project == null)
        return new ProjectRuntimeFacts { ProjectId = projectId, SubsidyLane = subsidyType };

    var facts = new ProjectRuntimeFacts
    {
        ProjectId = projectId,
        SubsidyLane = project.subsidytype
    };

    try
    {
        int level = 1;
        if (GameVars.alliance != null && alliance >= 0 && alliance < GameVars.alliance.Length && GameVars.alliance[alliance] != null)
            level = Math.Max(1, GameVars.alliance[alliance].GetProjectLevel(projectId) + 1);
        facts.Cost = project.GetSubsidyCost(level);
    }
    catch (Exception ex)
    {
        OnceLog.Warning("project-selection:cost", "[Patch:ProjectSelection] project cost read failed: " + ex.Message);
    }

    try
    {
        if (project.datefrom != null && alliance >= 0)
        {
            if (alliance < project.datefrom.Length && project.datefrom[alliance] != null)
            {
                facts.DateFromKnown = true;
                facts.DateFromYear = project.datefrom[alliance].year;
                facts.DateFromMonth = project.datefrom[alliance].month;
                facts.DateFromDay = project.datefrom[alliance].day;
            }
        }
    }
    catch (Exception ex)
    {
        OnceLog.Warning("project-selection:datefrom", "[Patch:ProjectSelection] project date read failed: " + ex.Message);
    }

    return facts;
}
```

If `Tools.Date` member names differ from `year/month/day`, use the names shown by the decompile and adjust the helper before build. Keep the try/catch and warning boundary.

- [ ] **Step 12: Add signal helper**

Add this helper:

```csharp
private static ProjectDoctrineSignals BuildSignals(int alliance, EraStage era, FiscalOutput fiscalIntent)
{
    var input = new ProjectDoctrineSignalInput
    {
        Alliance = alliance,
        Era = era,
        FiscalPosture = fiscalIntent != null ? fiscalIntent.Posture : FiscalPosture.BalancedWar,
        PortViabilityInput = 0.5f,
        StrengthRatio = 1f
    };

    try
    {
        int enemy = alliance == 0 ? 1 : 0;
        if (GameVars.alliance != null && alliance >= 0 && enemy >= 0 && alliance < GameVars.alliance.Length && enemy < GameVars.alliance.Length)
        {
            input.BlockadeRatio = GameVars.alliance[1] != null ? GameVars.alliance[1].averageblockaderatio : 0f;
            input.SupplyPressure = fiscalIntent != null && fiscalIntent.SupplyProtection ? 0.75f : 0f;
            input.TransportPressure = fiscalIntent != null && fiscalIntent.LogisticsExpansion ? 0.75f : 0f;
        }
    }
    catch (Exception ex)
    {
        OnceLog.Warning("project-selection:signals", "[Patch:ProjectSelection] signal read failed: " + ex.Message);
    }

    return ProjectDoctrineSignalBuilder.Build(input);
}
```

This first wiring intentionally uses conservative neutral defaults for weapon, artillery, port, and recognition signals until runtime-safe field reads are added. It must still apply catalog suppression, fiscal posture, era, date penalties, and hysteresis.

- [ ] **Step 13: Run build**

Run:

```bash
./build.sh
```

Expected: build passes. If `Tools.Date` field names or project cost signatures drift, fix only the helper.

- [ ] **Step 14: Commit**

```bash
git add src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs
git commit -m "feat: wire project doctrine selection"
```

## Task 6: Add Project Appointment Observer

**Files:**

- Create: `src/WhiskeyRealism/Patches/ProjectAppointmentObserverPatch.cs`

- [ ] **Step 1: Confirm vanilla signature**

Run:

```bash
sed -n '213384,213440p' /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected: `Projects.AppointProject(Projects.LoadedProjects project, int alliance, bool manualappointment=false)` still exists.

- [ ] **Step 2: Create observer patch**

Create `src/WhiskeyRealism/Patches/ProjectAppointmentObserverPatch.cs`:

```csharp
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Projects;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Projects.AppointProject spends subsidy/prestige, records the project,
    // and applies effects. This Postfix observes appointments only.
    [HarmonyPatch(typeof(Projects), "AppointProject")]
    internal static class ProjectAppointmentObserverPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(Projects.LoadedProjects project, int alliance)
        {
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (project == null) return;
                if (alliance < 0 || alliance >= 2) return;

                OnceLog.Info("project-appoint-observer", "Project appointment observer wired");

                var entry = ProjectDoctrineCatalog.Get(project.projectid);
                string bucket = entry != null ? entry.Bucket.ToString() : "Unknown";
                string side = entry != null ? entry.UiSide.ToString() : "Unknown";
                int lane = entry != null ? entry.SubsidyLane : project.subsidytype;
                int level = 0;
                try
                {
                    if (GameVars.alliance != null && alliance < GameVars.alliance.Length && GameVars.alliance[alliance] != null)
                        level = GameVars.alliance[alliance].GetProjectLevel(project.projectid);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("project-appoint-observer:level", "[ProjectAppointed] level read failed: " + ex.Message);
                }

                Plugin.Log.LogInfo(
                    $"[ProjectAppointed] alliance={alliance} project={project.projectid} name=\"{project.projectname}\" " +
                    $"lane={lane} side={side} bucket={bucket} level={Math.Max(0, level - 1)}->{level} cost={project.subsidycostfirstlevel:F0}");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-appoint-observer:postfix", "[ProjectAppointed] observer failed: " + ex.Message);
            }
        }
    }
}
```

- [ ] **Step 3: Build**

Run:

```bash
./build.sh
```

Expected: build passes.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Patches/ProjectAppointmentObserverPatch.cs
git commit -m "feat: observe project appointments"
```

## Task 7: Add CheckProjectUnlocks Observer

**Files:**

- Create: `src/WhiskeyRealism/Patches/ProjectUnlockObserverPatch.cs`

- [ ] **Step 1: Confirm vanilla signature**

Run:

```bash
sed -n '213569,213590p' /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected: `Projects.CheckProjectUnlocks(int alliance)` still directly adds unlocked project IDs.

- [ ] **Step 2: Create observer patch**

Create `src/WhiskeyRealism/Patches/ProjectUnlockObserverPatch.cs`:

```csharp
using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic.Projects;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Projects.CheckProjectUnlocks appears to seed date/scenario projects
    // during new-campaign setup. This observer logs added project IDs only.
    [HarmonyPatch(typeof(Projects), "CheckProjectUnlocks")]
    internal static class ProjectUnlockObserverPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(int alliance, out HashSet<int> __state)
        {
            __state = Snapshot(alliance);
        }

        [HarmonyPostfix]
        internal static void Postfix(int alliance, HashSet<int> __state)
        {
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (alliance < 0 || alliance >= 2) return;

                OnceLog.Info("project-unlock-observer", "Project unlock observer wired");

                var after = Snapshot(alliance);
                foreach (int projectId in after)
                {
                    if (__state != null && __state.Contains(projectId)) continue;
                    var project = Resolve(projectId);
                    var entry = ProjectDoctrineCatalog.Get(projectId);
                    int lane = entry != null ? entry.SubsidyLane : (project != null ? project.subsidytype : -1);
                    string bucket = entry != null ? entry.Bucket.ToString() : "Unknown";
                    string name = project != null ? project.projectname : "unknown";
                    Plugin.Log.LogInfo(
                        $"[ProjectUnlock] alliance={alliance} project={projectId} name=\"{name}\" lane={lane} bucket={bucket} source=CheckProjectUnlocks");
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-unlock-observer:postfix", "[ProjectUnlock] observer failed: " + ex.Message);
            }
        }

        private static HashSet<int> Snapshot(int alliance)
        {
            var ids = new HashSet<int>();
            try
            {
                if (GameVars.alliance == null || alliance < 0 || alliance >= GameVars.alliance.Length) return ids;
                var allianceState = GameVars.alliance[alliance];
                if (allianceState == null || allianceState.projects == null) return ids;
                foreach (int id in allianceState.projects)
                    ids.Add(id);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-unlock-observer:snapshot", "[ProjectUnlock] snapshot failed: " + ex.Message);
            }
            return ids;
        }

        private static Projects.LoadedProjects Resolve(int projectId)
        {
            try
            {
                return Projects.LoadedProjects.GetLoadedProjectFromID(projectId);
            }
            catch
            {
                return null;
            }
        }
    }
}
```

- [ ] **Step 3: Build**

Run:

```bash
./build.sh
```

Expected: build passes.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Patches/ProjectUnlockObserverPatch.cs
git commit -m "feat: observe project unlock seeding"
```

## Task 8: Add Runtime-Safe Dynamic Signals

**Files:**

- Modify: `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs`

- [ ] **Step 1: Add conservative weapon/naval/intervention reads**

Extend `BuildSignals(...)` only with fields already verified in vanilla and already cheap from `GameVars.alliance`. Add guarded reads for:

```csharp
input.BlockadeRatio = GameVars.alliance[1] != null ? GameVars.alliance[1].averageblockaderatio : 0f;
input.RecognitionProbability = alliance == 1
    ? Math.Max(GameVars.alliance[alliance].GetInterventionProbability(2), GameVars.alliance[alliance].GetInterventionProbability(3))
    : Math.Max(GameVars.alliance[1].GetInterventionProbability(2), GameVars.alliance[1].GetInterventionProbability(3));
```

If `GetInterventionProbability` is not callable with those parameters from this context, remove only that read and leave `RecognitionProbability = 0f`; keep a bounded warning:

```csharp
OnceLog.Warning("project-selection:recognition-signal", "[Patch:ProjectSelection] recognition signal unavailable; using neutral value");
```

- [ ] **Step 2: Add project-level proxy reads**

Still inside guarded alliance checks, add:

```csharp
int enemy = alliance == 0 ? 1 : 0;
int ownWeaponProduction = GameVars.alliance[alliance].GetProjectLevel(102);
int enemyWeaponProduction = GameVars.alliance[enemy].GetProjectLevel(102);
int ownIndustry = GameVars.alliance[alliance].GetProjectLevel(104);
int enemyIndustry = GameVars.alliance[enemy].GetProjectLevel(104);
input.IndustryGapInput = ProjectDoctrineSignalBuilder.Clamp01(Math.Max(enemyWeaponProduction + enemyIndustry - ownWeaponProduction - ownIndustry, 0f) / 3f);
```

- [ ] **Step 3: Build**

Run:

```bash
./build.sh
```

Expected: build passes. No new map-wide scans, no reflection-heavy loops.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs
git commit -m "feat: add cheap project doctrine runtime signals"
```

## Task 9: Update Living Docs

**Files:**

- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`
- Modify: `MEMORY.md`

- [ ] **Step 1: Update patch catalog**

Add new patch entries after current highest ordinal:

```markdown
### #36 ProjectSelectionPatch doctrine expansion

- **File:** `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs`
- **Vanilla surface:** `AICampaign.UpdateProjects(int alliance)`
- **Behavior:** Replaces queued AI project choices only when pure project doctrine beats vanilla by margin, with bug suppression, date penalties, and half-funded hysteresis. Does not spend subsidies, appoint projects, or mutate strategic state.
- **Smoke:** `[once:project-selection]`, `[once:project-doctrine-selection]`, `[ProjectDoctrine]`.

### #37 ProjectAppointmentObserverPatch

- **File:** `src/WhiskeyRealism/Patches/ProjectAppointmentObserverPatch.cs`
- **Vanilla surface:** `Projects.AppointProject(LoadedProjects project, int alliance, bool manualappointment=false)`
- **Behavior:** Observer-only appointment telemetry. Does not mutate project effects or spending.
- **Smoke:** `[once:project-appoint-observer]`, `[ProjectAppointed]`.

### #38 ProjectUnlockObserverPatch

- **File:** `src/WhiskeyRealism/Patches/ProjectUnlockObserverPatch.cs`
- **Vanilla surface:** `Projects.CheckProjectUnlocks(int alliance)`
- **Behavior:** Observer-only init-seeding telemetry. Does not change unlock behavior.
- **Smoke:** `[once:project-unlock-observer]`, `[ProjectUnlock]`.
```

If ordinals `#36-#38` are already used by parallel work, use the next open ordinals and update the headings consistently.

- [ ] **Step 2: Update handoff**

Add a short active-workstream note:

```markdown
- **Strategic project doctrine plan active.** Spec: `docs/superpowers/specs/2026-05-06-strategic-project-doctrine-design.md`; plan: `docs/superpowers/plans/2026-05-06-strategic-project-doctrine.md`. This slice owns project selection scoring and observer telemetry only. It does not patch `IsAppointable`, `AppointProject`, `CheckProjectUnlocks`, `UseSubsidyForPurpose`, or weapon purchase orders.
```

- [ ] **Step 3: Update repo memory**

Add a durable memory bullet:

```markdown
- Strategic project doctrine is planned as a bounded Slice A economy enrichment: pure catalog/signals/scorer under `Strategic/Projects`, `ProjectSelectionPatch` selection-only integration, observer-only appointment/unlock telemetry, and no fiscal subsidy-lane mutation in this slice.
```

- [ ] **Step 4: Run markdown grep for drift**

Run:

```bash
rg -n "ProjectSelectionPatch doctrine expansion|ProjectAppointmentObserverPatch|ProjectUnlockObserverPatch|strategic project doctrine" docs/patch-catalog.md docs/handoff.md MEMORY.md
```

Expected: all three living docs mention the new slice consistently.

- [ ] **Step 5: Commit**

```bash
git add docs/patch-catalog.md docs/handoff.md MEMORY.md
git commit -m "docs: document project doctrine slice"
```

## Task 10: Verification, Deploy, And Runtime Smoke

**Files:**

- Verify: `dist/WhiskeyRealism.dll`
- Deploy target: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll`
- Log: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log`

- [ ] **Step 1: Run console harness**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests PASS.

- [ ] **Step 2: Run build**

Run:

```bash
./build.sh
```

Expected: build passes with 0 errors.

- [ ] **Step 3: Check whitespace**

Run:

```bash
git diff --check
```

Expected: no output.

- [ ] **Step 4: Deploy DLL**

Close GTCW first. Then run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

Expected: command succeeds. If it fails with `Invalid argument`, the game still has the DLL loaded.

- [ ] **Step 5: Hash-verify deployed DLL**

Run:

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: timestamps and sizes are current, SHA-256 hashes match exactly.

- [ ] **Step 6: Runtime smoke grep**

Start a fresh campaign and let at least one economy/project cycle run. Then run:

```bash
rg "\[once:project-(selection|doctrine-catalog|doctrine-selection|doctrine-starved-lane|appoint-observer|unlock-observer)\]|\[ProjectDoctrine\]|\[ProjectAppointed\]|\[ProjectUnlock\]" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected:

- `[once:project-selection]` appears.
- `[once:project-doctrine-selection]` appears after first project selection decision.
- `[once:project-appoint-observer]` appears after first appointment callback.
- `[once:project-unlock-observer]` appears during fresh campaign init if `CheckProjectUnlocks` fires.
- `[ProjectDoctrine]` lines include alliance, lane, old/new project IDs, funding, and reason.
- `[ProjectAppointed]` appears when a project appoints.
- `[ProjectUnlock]` appears if init seeding adds projects.

- [ ] **Step 7: Error scan**

Run:

```bash
rg -n "Exception|Harmony|project-selection:|project-doctrine|ProjectDoctrine|ProjectAppointed|ProjectUnlock" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected: no repeated exceptions or Harmony failures. One-time bounded warnings are acceptable only for documented unavailable signals.

- [ ] **Step 8: Final commit or amend docs**

If smoke reveals doc drift but no code issue, update `docs/handoff.md` with exact smoke result and deployed SHA-256, then commit:

```bash
git add docs/handoff.md MEMORY.md
git commit -m "docs: record project doctrine smoke"
```

If code changes are needed, fix them in the responsible task file, rerun Steps 1-7, then commit the corrected file set only.

## Deferral Boundaries

Do not expand this plan to include:

- hard date gates in `Projects.IsAppointable`;
- `CheckProjectUnlocks` spending changes;
- `Projects.UpdateProjectEffects` fixes for Market Reform or Civil Order raiding;
- fiscal subsidy-lane mutation;
- direct weapon or cannon purchasing doctrine;
- direct edits to `Config/projects.dat`;
- persisted project-doctrine memory.

Separate plans are required for:

- Market Reform bug fix after IL/runtime proof;
- Civil Order raiding bug fix after IL/runtime proof;
- fiscal lane nudge if starved-lane telemetry proves it matters;
- direct weapon procurement under `AICampaign.CheckPurchaseWeapons`.

## Self-Review Checklist

- [ ] Catalog covers all 69 non-inactive project rows and marks `20-29` plus `42-87` inactive.
- [ ] Alliance `2` and higher are skipped before indexing per-alliance arrays.
- [ ] Lanes `6/7` produce no candidates and no doctrine action.
- [ ] Project `98` is strongly suppressed until reviewed.
- [ ] Project `107` keeps casualty/civil-order scoring but receives no raiding-mitigation bonus.
- [ ] Out-of-window penalty is at least `ReplacementMargin`.
- [ ] Half-funded queue hysteresis uses `2 * ReplacementMargin`.
- [ ] Starved-lane telemetry does not mutate subsidies.
- [ ] Observer patches are Postfix/Prefix telemetry only and never alter vanilla return values or project lists.
- [ ] Console test project includes every new pure strategic source file explicitly.
- [ ] `./build.sh`, deploy, and SHA-256 verification pass before reporting runtime readiness.
