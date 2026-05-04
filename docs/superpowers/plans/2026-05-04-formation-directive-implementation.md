# Formation Directive Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a weekly formation directive layer that treats independent divisions, corps, and armies as first-class campaign actors, then uses those directives to steer existing army-area and army-group behavior without replacing vanilla campaign movement.

**Architecture:** Add a pure `FormationDirectiveLedger` under `src/WhiskeyRealism/Strategic/` and test it without Unity. Add `FormationDirectiveRuntime` as the only Unity/reflection extraction layer. `StrategicCoordinator` refreshes the ledger weekly after front and army-area ledgers, while Harmony patches only read the ledger and apply bounded steering to existing vanilla surfaces.

**Tech Stack:** C# `netstandard2.1`, BepInEx 5.4.x, HarmonyX, Unity 2021 Mono, repo-local console tests in `tests/WhiskeyRealism.Tests`, runtime verification through `./build.sh`, deploy, and BepInEx log smoke.

---

## Scope And Ordering

This plan implements the formation-directive slice first because `docs/superpowers/specs/2026-05-04-fiscal-economy-ai-design.md` consumes formation supply/ammo pressure downstream. Fiscal economy planning should wait until this ledger exists or it will have to guess at the military supply signals.

This plan intentionally does not patch `Autocalc.StartSkirmishing`, does not create a parallel raid engine, and does not rewrite vanilla campaign movement. Offensive blocking is placed behind an explicit late task because it uses a narrow Prefix gate and should only be enabled after the pure ledger, runtime extraction, and #15/#16 integrations are verified.

## Files

Create:

- `src/WhiskeyRealism/Strategic/FormationLevel.cs` - formation level enum and vanilla `unittyp` mapping.
- `src/WhiskeyRealism/Strategic/FormationDirective.cs` - directive enum.
- `src/WhiskeyRealism/Strategic/FormationSnapshot.cs` - pure input model extracted from runtime.
- `src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs` - pure scoring, assignment, summary, and pressure outputs.
- `src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs` - Unity/reflection snapshot builder and optional runtime helpers.
- `src/WhiskeyRealism/Patches/FormationOffensiveSafetyPatch.cs` - late narrow safety gate around vanilla offensive movement, only after prior tasks pass.

Modify:

- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` - link new pure strategic files into tests.
- `tests/WhiskeyRealism.Tests/Program.cs` - add pure tests.
- `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs` - store and refresh `FormationDirectiveLedger[]`.
- `src/WhiskeyRealism/Strategic/ArmyAreaRuntime.cs` - include independent top divisions and consult directives before area correction.
- `src/WhiskeyRealism/Patches/ArmyGroupManagementPatch.cs` - allow directive-qualified independent divisions as attachments, not army-group seeds.
- `docs/handoff.md` - update next-step and smoke notes after implementation.
- `docs/patch-catalog.md` - add patch entry only when `FormationOffensiveSafetyPatch` lands.

Do not modify:

- `docs/superpowers/specs/2026-05-04-fiscal-economy-ai-design.md` in this plan.
- Game install files except for normal DLL deployment.

## Task 1: Pure Formation Types

**Files:**

- Create: `src/WhiskeyRealism/Strategic/FormationLevel.cs`
- Create: `src/WhiskeyRealism/Strategic/FormationDirective.cs`
- Create: `src/WhiskeyRealism/Strategic/FormationSnapshot.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing type/classification tests**

Add these entries to the `tests` array in `tests/WhiskeyRealism.Tests/Program.cs`:

```csharp
("formation level maps vanilla unit types", FormationLevelMapsVanillaUnitTypes),
("independent top division requires top unit and strength floor", IndependentTopDivisionRequiresTopAndStrengthFloor),
("attached division is not directly controllable", AttachedDivisionIsNotDirectlyControllable),
```

Add these methods near the other test methods:

```csharp
private static void FormationLevelMapsVanillaUnitTypes()
{
    AssertEqual(FormationLevel.Division, FormationSnapshot.LevelFromUnitType(14));
    AssertEqual(FormationLevel.Corps, FormationSnapshot.LevelFromUnitType(15));
    AssertEqual(FormationLevel.Army, FormationSnapshot.LevelFromUnitType(16));
    AssertEqual(FormationLevel.Unknown, FormationSnapshot.LevelFromUnitType(17));
}

private static void IndependentTopDivisionRequiresTopAndStrengthFloor()
{
    var snap = new FormationSnapshot
    {
        UnitKey = "div:1",
        UnitType = 14,
        IsTopUnit = true,
        IsGarrisoned = false,
        GroupStrengthDirect = 1500f
    };

    AssertEqual(true, snap.IsIndependentTopDivision);

    snap.IsTopUnit = false;
    AssertEqual(false, snap.IsIndependentTopDivision);

    snap.IsTopUnit = true;
    snap.GroupStrengthDirect = 999f;
    AssertEqual(false, snap.IsIndependentTopDivision);
}

private static void AttachedDivisionIsNotDirectlyControllable()
{
    var snap = new FormationSnapshot
    {
        UnitKey = "attached:1",
        ParentUnitKey = "corps:1",
        UnitType = 14,
        IsTopUnit = false,
        GroupStrengthDirect = 3000f
    };

    AssertEqual(FormationLevel.Division, snap.Level);
    AssertEqual(true, snap.IsAttachedDivision);
    AssertEqual(false, snap.CanReceiveDirectDirective);
}
```

- [ ] **Step 2: Link new pure files into the test project**

Add these lines to the `<ItemGroup>` in `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\FormationLevel.cs" Link="FormationLevel.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Strategic\FormationDirective.cs" Link="FormationDirective.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Strategic\FormationSnapshot.cs" Link="FormationSnapshot.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Strategic\FormationDirectiveLedger.cs" Link="FormationDirectiveLedger.cs" />
```

- [ ] **Step 3: Run tests and verify the new tests fail**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure because `FormationLevel`, `FormationDirective`, and `FormationSnapshot` do not exist yet.

- [ ] **Step 4: Create `FormationLevel.cs`**

```csharp
namespace WhiskeyRealism.Strategic
{
    public enum FormationLevel
    {
        Unknown = 0,
        Division = 14,
        Corps = 15,
        Army = 16,
        ArmyGroup = 100
    }
}
```

- [ ] **Step 5: Create `FormationDirective.cs`**

```csharp
namespace WhiskeyRealism.Strategic
{
    public enum FormationDirective
    {
        Hold,
        Screen,
        Delay,
        Guard,
        Probe,
        Reserve,
        Reinforce,
        Counterstroke,
        Mass,
        RaidSupport,
        Recover,
        Concede,
        CoordinateHold,
        CoordinateMass,
        CoordinateReserve,
        CoordinateConcede
    }
}
```

- [ ] **Step 6: Create `FormationSnapshot.cs`**

```csharp
using System;

namespace WhiskeyRealism.Strategic
{
    public sealed class FormationSnapshot
    {
        public string UnitKey;
        public string ParentUnitKey;
        public int AllianceId;
        public string UnitName;
        public string CommanderName;
        public int UnitType;
        public bool IsTopUnit;
        public bool IsGarrisoned;
        public bool GrandArmyStructureAvailable;
        public string AreaKey;
        public string SectorKey;
        public float GroupStrengthActive;
        public float GroupStrengthDirect;
        public float Morale = 1f;
        public float Readiness = 1f;
        public float RifleAmmo = 1f;
        public float ArtilleryAmmo = 1f;
        public float Supply = 1f;
        public float Fatigue;
        public float WeaponFirepower = 1f;
        public float CommandRange;
        public float BugleRange;
        public bool InBattle;
        public bool OnRetreat;
        public bool HasActivePath;
        public bool IsCavalryCapable;
        public FormationLevel VisibleEnemyLevel = FormationLevel.Unknown;
        public float LocalEnemyStrength;
        public float LocalEnemyExchangePressure;
        public float LocalFriendlySupportStrength;
        public bool SupportCanReach;
        public bool IsPlanTargetArea;
        public bool IsCriticalSector;
        public FrontPosture FrontPosture = FrontPosture.Hold;

        public FormationLevel Level => LevelFromUnitType(UnitType);

        public bool IsAttachedDivision =>
            UnitType == 14 && !IsTopUnit && !string.IsNullOrEmpty(ParentUnitKey);

        public bool IsIndependentTopDivision =>
            UnitType == 14 && IsTopUnit && !IsGarrisoned && GroupStrengthDirect > 1000f;

        public bool IsTopStrategicFormation =>
            !IsGarrisoned && IsTopUnit && UnitType >= 14 && UnitType <= 16;

        public bool CanReceiveDirectDirective =>
            IsTopStrategicFormation;

        public bool CanReceiveDirectMovement =>
            IsTopStrategicFormation;

        public float MinimumAmmo =>
            Math.Min(Clamp01(RifleAmmo), Clamp01(ArtilleryAmmo));

        public static FormationLevel LevelFromUnitType(int unitType)
        {
            if (unitType == 14) return FormationLevel.Division;
            if (unitType == 15) return FormationLevel.Corps;
            if (unitType == 16) return FormationLevel.Army;
            return FormationLevel.Unknown;
        }

        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
```

- [ ] **Step 7: Run tests and verify they pass**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all existing tests plus the three new formation type tests pass.

- [ ] **Step 8: Commit Task 1**

Run:

```bash
git add src/WhiskeyRealism/Strategic/FormationLevel.cs src/WhiskeyRealism/Strategic/FormationDirective.cs src/WhiskeyRealism/Strategic/FormationSnapshot.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add formation directive model"
```

## Task 2: Pure Formation Directive Ledger

**Files:**

- Create: `src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing ledger tests**

Add these entries to the `tests` array:

```csharp
("division refuses enemy army without support", DivisionRefusesEnemyArmyWithoutSupport),
("csa coherent outnumbered division delays instead of retreating", CsaCoherentOutnumberedDivisionDelays),
("low ammo formation recovers", LowAmmoFormationRecovers),
("army masses for plan target when hierarchy exists", ArmyMassesForPlanTargetWhenHierarchyExists),
("raid support maps only to cavalry capable formations", RaidSupportMapsOnlyToCavalryCapableFormations),
("formation directive summary changes when assignment changes", FormationDirectiveSummaryChangesWhenAssignmentChanges),
```

Add these helper/test methods:

```csharp
private static FormationSnapshot Snapshot(
    string key,
    int alliance,
    int unitType,
    float strength,
    float enemy,
    FormationLevel enemyLevel,
    FrontPosture posture)
{
    return new FormationSnapshot
    {
        UnitKey = key,
        AllianceId = alliance,
        UnitName = key,
        UnitType = unitType,
        IsTopUnit = true,
        GroupStrengthActive = strength,
        GroupStrengthDirect = strength,
        Morale = 0.8f,
        Readiness = 0.8f,
        RifleAmmo = 0.8f,
        ArtilleryAmmo = 0.8f,
        Supply = 0.8f,
        WeaponFirepower = 1.0f,
        AreaKey = "VirginiaCapitalCorridor",
        SectorKey = "Richmond",
        LocalEnemyStrength = enemy,
        VisibleEnemyLevel = enemyLevel,
        FrontPosture = posture
    };
}

private static void DivisionRefusesEnemyArmyWithoutSupport()
{
    var snap = Snapshot("division", 1, 14, 4500f, 50000f, FormationLevel.Army, FrontPosture.Hold);
    var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Amateur1861, null);
    var assignment = ledger.GetAssignment("division");

    AssertEqual(FormationDirective.Screen, assignment.Directive);
    AssertEqual(false, assignment.OffensiveAllowed);
}

private static void CsaCoherentOutnumberedDivisionDelays()
{
    var snap = Snapshot("csa-delay", 1, 14, 6000f, 14000f, FormationLevel.Corps, FrontPosture.Delay);
    snap.LocalFriendlySupportStrength = 5000f;
    snap.SupportCanReach = true;

    var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Amateur1861, null);
    var assignment = ledger.GetAssignment("csa-delay");

    AssertEqual(FormationDirective.Delay, assignment.Directive);
    AssertEqual(false, assignment.OffensiveAllowed);
    AssertEqual(true, assignment.DefensiveAllowed);
}

private static void LowAmmoFormationRecovers()
{
    var snap = Snapshot("low-ammo", 0, 15, 16000f, 10000f, FormationLevel.Corps, FrontPosture.Counterstroke);
    snap.RifleAmmo = 0.1f;
    snap.ArtilleryAmmo = 0.2f;

    var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Operational1862, null);
    var assignment = ledger.GetAssignment("low-ammo");

    AssertEqual(FormationDirective.Recover, assignment.Directive);
    AssertEqual(false, assignment.OffensiveAllowed);
}

private static void ArmyMassesForPlanTargetWhenHierarchyExists()
{
    var snap = Snapshot("army", 0, 16, 50000f, 30000f, FormationLevel.Army, FrontPosture.Exploit);
    snap.GrandArmyStructureAvailable = true;
    snap.IsPlanTargetArea = true;

    var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.TotalWar1864, "VirginiaCapitalCorridor");
    var assignment = ledger.GetAssignment("army");

    AssertEqual(FormationDirective.Mass, assignment.Directive);
    AssertEqual(true, assignment.OffensiveAllowed);
}

private static void RaidSupportMapsOnlyToCavalryCapableFormations()
{
    var cavalry = Snapshot("cav", 1, 14, 2500f, 1000f, FormationLevel.Division, FrontPosture.EconomyOfForce);
    cavalry.IsCavalryCapable = true;
    cavalry.Supply = 0.9f;
    cavalry.Readiness = 0.9f;

    var infantry = Snapshot("inf", 1, 14, 2500f, 1000f, FormationLevel.Division, FrontPosture.EconomyOfForce);
    infantry.IsCavalryCapable = false;

    var ledger = FormationDirectiveLedger.Build(new[] { cavalry, infantry }, EraStage.Operational1862, null);

    AssertEqual(true, ledger.GetAssignment("cav").RaidAllowed);
    AssertEqual(FormationDirective.RaidSupport, ledger.GetAssignment("cav").Directive);
    AssertEqual(false, ledger.GetAssignment("inf").RaidAllowed);
}

private static void FormationDirectiveSummaryChangesWhenAssignmentChanges()
{
    var a = Snapshot("unit", 0, 15, 15000f, 10000f, FormationLevel.Corps, FrontPosture.Hold);
    var b = Snapshot("unit", 0, 15, 15000f, 10000f, FormationLevel.Corps, FrontPosture.Counterstroke);

    string first = FormationDirectiveLedger.Build(new[] { a }, EraStage.Operational1862, null).Summary();
    string second = FormationDirectiveLedger.Build(new[] { b }, EraStage.Operational1862, null).Summary();

    AssertEqual(false, string.Equals(first, second, StringComparison.Ordinal));
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure because `FormationDirectiveLedger` is not implemented.

- [ ] **Step 3: Create `FormationDirectiveLedger.cs`**

Implement the ledger with this public surface:

```csharp
using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class FormationDirectiveAssignment
    {
        public string UnitKey;
        public int AllianceId;
        public FormationLevel Level;
        public string AreaKey;
        public string SectorKey;
        public FormationDirective Directive;
        public string Reason;
        public float CombatAvailability;
        public float ExchangePressure;
        public float LocalFriendlySupport;
        public float LocalEnemyStrength;
        public float Readiness;
        public float Morale;
        public float Ammo;
        public float Supply;
        public float Fatigue;
        public float WeaponFirepower;
        public bool OffensiveAllowed;
        public bool DefensiveAllowed;
        public bool TransferDonorAllowed;
        public bool DirectMovementAllowed;
        public bool RaidAllowed;
        public bool InheritsFromParent;
        public string ParentUnitKey;
    }

    public sealed class FormationPressureSummary
    {
        public int LowSupplyCount;
        public int LowAmmoCount;
        public int RecoverCount;
        public int GuardCount;
        public int MassCount;
        public string TopSupplyAreaKey;
    }

    public sealed class FormationDirectiveLedger
    {
        private readonly Dictionary<string, FormationDirectiveAssignment> _assignments =
            new Dictionary<string, FormationDirectiveAssignment>();
        private readonly List<FormationDirectiveAssignment> _ordered =
            new List<FormationDirectiveAssignment>();

        public IReadOnlyList<FormationDirectiveAssignment> Assignments => _ordered;
        public FormationPressureSummary Pressure = new FormationPressureSummary();

        public static FormationDirectiveLedger Build(IEnumerable<FormationSnapshot> snapshots, EraStage era, string planTargetAreaKey)
        {
            var ledger = new FormationDirectiveLedger();
            if (snapshots == null) return ledger;

            var pendingAttached = new List<FormationSnapshot>();
            foreach (var snapshot in snapshots)
            {
                if (snapshot == null || string.IsNullOrEmpty(snapshot.UnitKey)) continue;
                if (snapshot.IsAttachedDivision)
                {
                    pendingAttached.Add(snapshot);
                    continue;
                }
                if (!snapshot.CanReceiveDirectDirective) continue;
                if (!snapshot.IsTopStrategicFormation) continue;

                var assignment = ResolveTopDirective(snapshot, era, planTargetAreaKey);
                ledger.Add(assignment);
            }

            for (int i = 0; i < pendingAttached.Count; i++)
            {
                var child = pendingAttached[i];
                FormationDirectiveAssignment parent = null;
                if (!string.IsNullOrEmpty(child.ParentUnitKey))
                    parent = ledger.GetAssignment(child.ParentUnitKey);

                var assignment = BaseAssignment(child);
                assignment.InheritsFromParent = true;
                assignment.ParentUnitKey = child.ParentUnitKey;
                assignment.DirectMovementAllowed = false;
                assignment.Directive = parent != null ? parent.Directive : FormationDirective.Hold;
                assignment.Reason = parent != null ? "attached-inherits-parent" : "attached-parent-missing";
                assignment.OffensiveAllowed = false;
                assignment.DefensiveAllowed = parent == null || parent.DefensiveAllowed;
                assignment.TransferDonorAllowed = false;
                ledger.Add(assignment);
            }

            ledger.RecomputePressure();
            return ledger;
        }

        public FormationDirectiveAssignment GetAssignment(string unitKey)
        {
            if (unitKey == null) return null;
            _assignments.TryGetValue(unitKey, out var assignment);
            return assignment;
        }

        public string Summary()
        {
            if (_ordered.Count == 0) return "<none>";
            var parts = new List<string>();
            foreach (var assignment in _ordered)
                parts.Add($"{assignment.UnitKey}:{assignment.Directive}:{assignment.Reason}");
            return string.Join(",", parts);
        }

        private void Add(FormationDirectiveAssignment assignment)
        {
            _assignments[assignment.UnitKey] = assignment;
            _ordered.Add(assignment);
        }

        private static FormationDirectiveAssignment ResolveTopDirective(FormationSnapshot snapshot, EraStage era, string planTargetAreaKey)
        {
            var assignment = BaseAssignment(snapshot);

            if (snapshot.Morale < 0.35f || snapshot.Readiness < 0.35f)
                return Set(assignment, FormationDirective.Recover, "low-morale-readiness", false, false, false);

            if (snapshot.MinimumAmmo < 0.25f || snapshot.Supply < 0.25f)
                return Set(assignment, FormationDirective.Recover, "low-ammo-supply", false, false, false);

            if (snapshot.OnRetreat && snapshot.HasActivePath)
                return Set(assignment, FormationDirective.Delay, "retreat-skirmish-risk", false, true, false);

            if (snapshot.IsCavalryCapable && snapshot.Readiness >= 0.65f && snapshot.Supply >= 0.65f &&
                snapshot.FrontPosture == FrontPosture.EconomyOfForce && snapshot.LocalEnemyStrength <= snapshot.GroupStrengthActive)
            {
                assignment.RaidAllowed = true;
                return Set(assignment, FormationDirective.RaidSupport, "cavalry-raid-support", false, false, true);
            }

            if (snapshot.Level == FormationLevel.Division &&
                snapshot.VisibleEnemyLevel == FormationLevel.Army &&
                !HasReachableSupport(snapshot))
                return Set(assignment, FormationDirective.Screen, "division-vs-army-no-support", false, true, false);

            if (snapshot.Level == FormationLevel.Division &&
                snapshot.VisibleEnemyLevel == FormationLevel.Corps &&
                !DivisionCanCounterstroke(snapshot))
                return Set(assignment, FormationDirective.Delay, "division-vs-corps-delay", false, true, false);

            if (snapshot.FrontPosture == FrontPosture.Concede)
                return Set(assignment, FormationDirective.Concede, "front-concede", false, false, true);

            if (snapshot.FrontPosture == FrontPosture.EconomyOfForce)
                return Set(assignment, FormationDirective.Reserve, "economy-of-force", false, true, true);

            if (snapshot.IsPlanTargetArea && snapshot.Level == FormationLevel.Army && snapshot.GrandArmyStructureAvailable)
                return Set(assignment, FormationDirective.Mass, "army-plan-target", true, true, false);

            if ((snapshot.FrontPosture == FrontPosture.Counterstroke || snapshot.FrontPosture == FrontPosture.Exploit) &&
                AttackRiskPasses(snapshot))
            {
                var directive = snapshot.FrontPosture == FrontPosture.Exploit ? FormationDirective.Mass : FormationDirective.Counterstroke;
                return Set(assignment, directive, "risk-gate-passed", true, true, false);
            }

            if (snapshot.IsCriticalSector)
                return Set(assignment, FormationDirective.Guard, "critical-sector", false, true, false);

            if (snapshot.Level == FormationLevel.Army)
                return Set(assignment, FormationDirective.Hold, "army-hold", false, true, false);

            return Set(assignment, FormationDirective.Screen, "default-screen", false, true, false);
        }

        private static FormationDirectiveAssignment BaseAssignment(FormationSnapshot snapshot)
        {
            float availability = CombatAvailability(snapshot);
            return new FormationDirectiveAssignment
            {
                UnitKey = snapshot.UnitKey,
                AllianceId = snapshot.AllianceId,
                Level = snapshot.Level,
                AreaKey = snapshot.AreaKey,
                SectorKey = snapshot.SectorKey,
                CombatAvailability = availability,
                ExchangePressure = ExchangePressure(snapshot),
                LocalFriendlySupport = snapshot.LocalFriendlySupportStrength,
                LocalEnemyStrength = snapshot.LocalEnemyStrength,
                Readiness = FormationSnapshot.Clamp01(snapshot.Readiness),
                Morale = FormationSnapshot.Clamp01(snapshot.Morale),
                Ammo = snapshot.MinimumAmmo,
                Supply = FormationSnapshot.Clamp01(snapshot.Supply),
                Fatigue = FormationSnapshot.Clamp01(snapshot.Fatigue),
                WeaponFirepower = Math.Max(0f, snapshot.WeaponFirepower),
                DirectMovementAllowed = snapshot.CanReceiveDirectMovement
            };
        }

        private static FormationDirectiveAssignment Set(FormationDirectiveAssignment assignment, FormationDirective directive, string reason, bool offensive, bool defensive, bool donor)
        {
            assignment.Directive = directive;
            assignment.Reason = reason;
            assignment.OffensiveAllowed = offensive;
            assignment.DefensiveAllowed = defensive;
            assignment.TransferDonorAllowed = donor;
            return assignment;
        }

        public static float CombatAvailability(FormationSnapshot snapshot)
        {
            return Math.Max(0f, snapshot.GroupStrengthActive) *
                   Gate(snapshot.Morale, 0.35f) *
                   Gate(snapshot.Readiness, 0.35f) *
                   Gate(snapshot.MinimumAmmo, 0.25f) *
                   Gate(snapshot.Supply, 0.25f) *
                   (1f - 0.5f * FormationSnapshot.Clamp01(snapshot.Fatigue));
        }

        public static float ExchangePressure(FormationSnapshot snapshot)
        {
            return Sqrt(Math.Max(1f, snapshot.GroupStrengthActive)) *
                   Sqrt(Math.Max(1f, snapshot.WeaponFirepower)) *
                   FormationSnapshot.Clamp01(snapshot.Morale) *
                   FormationSnapshot.Clamp01(snapshot.Readiness) *
                   FormationSnapshot.Clamp01(snapshot.MinimumAmmo) *
                   FormationSnapshot.Clamp01(snapshot.Supply) *
                   (1f - 0.5f * FormationSnapshot.Clamp01(snapshot.Fatigue));
        }

        private static bool HasReachableSupport(FormationSnapshot snapshot)
        {
            return snapshot.SupportCanReach && snapshot.LocalFriendlySupportStrength >= snapshot.GroupStrengthActive * 0.5f;
        }

        private static bool DivisionCanCounterstroke(FormationSnapshot snapshot)
        {
            if (!HasReachableSupport(snapshot)) return false;
            float own = snapshot.GroupStrengthActive + snapshot.LocalFriendlySupportStrength;
            return own >= snapshot.LocalEnemyStrength * 1.25f;
        }

        private static bool AttackRiskPasses(FormationSnapshot snapshot)
        {
            float own = snapshot.GroupStrengthActive + (snapshot.SupportCanReach ? snapshot.LocalFriendlySupportStrength : 0f);
            float ratio = own / Math.Max(1f, snapshot.LocalEnemyStrength);
            if (snapshot.Level == FormationLevel.Division) return ratio >= 1.5f;
            if (snapshot.Level == FormationLevel.Corps) return ratio >= 1.2f;
            return ratio >= 1.05f;
        }

        private void RecomputePressure()
        {
            var areaScores = new Dictionary<string, int>();
            foreach (var assignment in _ordered)
            {
                if (assignment.Supply < 0.35f)
                {
                    Pressure.LowSupplyCount++;
                    AddAreaPressure(areaScores, assignment.AreaKey);
                }
                if (assignment.Ammo < 0.35f)
                {
                    Pressure.LowAmmoCount++;
                    AddAreaPressure(areaScores, assignment.AreaKey);
                }
                if (assignment.Directive == FormationDirective.Recover) Pressure.RecoverCount++;
                if (assignment.Directive == FormationDirective.Guard) Pressure.GuardCount++;
                if (assignment.Directive == FormationDirective.Mass) Pressure.MassCount++;
            }

            int best = 0;
            foreach (var kv in areaScores)
            {
                if (kv.Value > best)
                {
                    best = kv.Value;
                    Pressure.TopSupplyAreaKey = kv.Key;
                }
            }
        }

        private static void AddAreaPressure(Dictionary<string, int> scores, string areaKey)
        {
            areaKey = string.IsNullOrEmpty(areaKey) ? "Unknown" : areaKey;
            scores.TryGetValue(areaKey, out var count);
            scores[areaKey] = count + 1;
        }

        private static float Gate(float value, float floor)
        {
            value = FormationSnapshot.Clamp01(value);
            if (value < floor) return 0f;
            return value;
        }

        private static float Sqrt(float value)
        {
            return (float)Math.Sqrt(value);
        }
    }
}
```

- [ ] **Step 4: Run tests and verify pass**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 5: Commit Task 2**

Run:

```bash
git add src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add formation directive ledger"
```

## Task 3: Runtime Snapshot Extraction

**Files:**

- Create: `src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`

- [ ] **Step 1: Create `FormationDirectiveRuntime.cs`**

Create a runtime layer that owns reflection and Unity access:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class FormationDirectiveRuntime
    {
        internal static FormationDirectiveLedger BuildForAlliance(int allianceId, EraStage era, string planTargetAreaKey)
        {
            try
            {
                var faction = FindFaction(allianceId);
                if (faction == null) return null;

                var ownUnits = AccessTools.Field(faction.GetType(), "ownunits")?.GetValue(faction) as IList;
                if (ownUnits == null) return null;

                bool grandArmyStructure = GrandArmyStructure(allianceId);
                var snapshots = new List<FormationSnapshot>();
                for (int i = 0; i < ownUnits.Count; i++)
                {
                    var unit = ownUnits[i];
                    var snapshot = SnapshotUnit(allianceId, unit, grandArmyStructure);
                    if (snapshot != null) snapshots.Add(snapshot);
                }

                return FormationDirectiveLedger.Build(snapshots, era, planTargetAreaKey);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation:build", "[FormationDirective] build failed: " + ex.Message);
                return null;
            }
        }

        internal static FormationSnapshot SnapshotUnit(int allianceId, object unit, bool grandArmyStructure)
        {
            if (unit == null) return null;
            int unitType = ReadInt(unit, "unittyp", -1);
            if (unitType < 14 || unitType > 16) return null;

            var pos = UnitPosition(unit);
            var snapshot = new FormationSnapshot
            {
                UnitKey = UnitKey(unit),
                ParentUnitKey = ParentUnitKey(unit),
                AllianceId = allianceId,
                UnitName = ObjectName(unit),
                CommanderName = CommanderName(unit),
                UnitType = unitType,
                IsTopUnit = ReadBool(unit, "istopunit", false),
                IsGarrisoned = ReadObject(unit, "garrisonreference") != null,
                GrandArmyStructureAvailable = grandArmyStructure,
                GroupStrengthActive = ReadFloat(unit, 0f, "groupstrengthactive"),
                GroupStrengthDirect = ReadFloat(unit, 0f, "groupstrengthdirect", "groupstrengthactive"),
                Morale = ReadFloat(unit, 1f, "groupmorale"),
                Readiness = ReadFloat(unit, 1f, "readiness"),
                RifleAmmo = ReadSupplyState(unit, 0, ReadFloat(unit, 1f, "groupammo")),
                ArtilleryAmmo = ReadSupplyState(unit, 1, ReadFloat(unit, 1f, "groupammo")),
                Supply = ReadFloat(unit, 1f, "groupsupply", "supply"),
                Fatigue = ReadFloat(unit, 0f, "groupfatigue"),
                WeaponFirepower = EstimateWeaponFirepower(unit),
                CommandRange = ReadFloat(unit, 0f, "commanderrange"),
                BugleRange = ReadFloat(unit, 0f, "buglerange"),
                InBattle = ReadBool(unit, "inbattle", false),
                OnRetreat = ReadBool(unit, "onretreat", false),
                HasActivePath = ReadInt(unit, "regimentpaths", 0) > 0,
                IsCavalryCapable = ReadFloat(unit, 0f, "groupstatshorses", "groupstatshorsesactive") > 0f
            };

            if (pos.HasValue)
            {
                snapshot.AreaKey = ArmyAreaRuntime.AreaKey(pos.Value);
                snapshot.SectorKey = FrontSectorRuntime.SectorKey(pos.Value);
            }

            PopulateLocalPressure(snapshot, unit, allianceId);
            return snapshot;
        }

        internal static bool ShouldAllowAreaMovement(int allianceId, string unitKey)
        {
            var assignment = StrategicCoordinator.Instance?.FormationDirectives?[allianceId]?.GetAssignment(unitKey);
            if (assignment == null) return true;
            if (!assignment.DirectMovementAllowed) return false;
            if (assignment.Directive == FormationDirective.Recover) return false;
            if (assignment.Directive == FormationDirective.Delay) return false;
            if (assignment.Directive == FormationDirective.Concede) return false;
            return true;
        }

        internal static bool AllowsArmyGroupAttachment(int allianceId, string unitKey)
        {
            var assignment = StrategicCoordinator.Instance?.FormationDirectives?[allianceId]?.GetAssignment(unitKey);
            if (assignment == null) return false;
            return assignment.Directive == FormationDirective.Reinforce ||
                   assignment.Directive == FormationDirective.Reserve ||
                   assignment.Directive == FormationDirective.Guard ||
                   assignment.Directive == FormationDirective.Mass ||
                   assignment.Level == FormationLevel.Corps ||
                   assignment.Level == FormationLevel.Army;
        }

        private static object FindFaction(int allianceId)
        {
            var aicType = AccessTools.TypeByName("AICampaign");
            var list = AccessTools.Field(aicType, "aifaction")?.GetValue(null) as IList;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                var faction = list[i];
                if (ReadInt(faction, "allianceid", -1) == allianceId) return faction;
            }
            return null;
        }

        private static bool GrandArmyStructure(int allianceId)
        {
            try
            {
                var method = AccessTools.Method(AccessTools.TypeByName("AICampaign"), "GrandArmyStructure");
                if (method == null) return false;
                return Convert.ToBoolean(method.Invoke(null, new object[] { allianceId }));
            }
            catch { return false; }
        }

        private static void PopulateLocalPressure(FormationSnapshot snapshot, object unit, int allianceId)
        {
            var pos = UnitPosition(unit);
            if (!pos.HasValue) return;

            var allRegiments = UnityEngine.Object.FindObjectsOfType<Regiment>();
            for (int i = 0; i < allRegiments.Length; i++)
            {
                var other = allRegiments[i];
                if ((UnityEngine.Object)(object)other == (UnityEngine.Object)null) continue;
                if ((object)other == unit) continue;
                if (other.unittyp < 14 || other.unittyp > 16) continue;

                float distance = Vector3.Distance(pos.Value, ((Component)other).transform.position);
                bool sameAlliance = ReadInt(other, "alliance", ReadInt(other, "allianceid", -1)) == allianceId;
                float strength = Math.Max(0f, other.groupstrengthactive);

                if (sameAlliance)
                {
                    if (distance <= Math.Max(1f, snapshot.CommandRange))
                    {
                        snapshot.LocalFriendlySupportStrength += strength;
                        snapshot.SupportCanReach = true;
                    }
                }
                else if (snapshot.LocalEnemyStrength <= 0f || distance < Math.Max(1f, snapshot.BugleRange * 2f))
                {
                    snapshot.LocalEnemyStrength += strength;
                    if (other.unittyp > 0)
                        snapshot.VisibleEnemyLevel = FormationSnapshot.LevelFromUnitType(other.unittyp);
                }
            }
        }

        private static float EstimateWeaponFirepower(object unit)
        {
            float guns = ReadFloat(unit, 0f, "groupstatsgunsactive", "groupstatsguns");
            float active = Math.Max(1f, ReadFloat(unit, 1f, "groupstrengthactive"));
            return 1f + Math.Min(2f, guns / active * 50f);
        }

        private static float ReadSupplyState(object unit, int index, float fallback)
        {
            var field = AccessTools.Field(unit.GetType(), "groupsupplystate");
            var value = field?.GetValue(unit) as IList;
            if (value == null || index < 0 || index >= value.Count) return FormationSnapshot.Clamp01(fallback);
            try { return FormationSnapshot.Clamp01(Convert.ToSingle(value[index])); }
            catch { return FormationSnapshot.Clamp01(fallback); }
        }

        private static string UnitKey(object unit)
        {
            return ObjectName(unit) + ":" + ReadInt(unit, "commander", -1).ToString();
        }

        private static string ParentUnitKey(object unit)
        {
            var component = unit as Component;
            var parent = component?.transform?.parent?.GetComponent<Regiment>();
            if ((UnityEngine.Object)(object)parent == (UnityEngine.Object)null) return null;
            return UnitKey(parent);
        }

        private static string ObjectName(object obj)
        {
            var unityObj = obj as UnityEngine.Object;
            return unityObj != null ? unityObj.name : obj?.ToString() ?? "<unknown>";
        }

        private static string CommanderName(object unit)
        {
            try
            {
                int commanderId = ReadInt(unit, "commander", -1);
                var commanders = AccessTools.Field(AccessTools.TypeByName("GameVars"), "commander")?.GetValue(null) as IList;
                if (commanders == null || commanderId < 0 || commanderId >= commanders.Count) return null;
                return AccessTools.Field(commanders[commanderId].GetType(), "combinedname")?.GetValue(commanders[commanderId]) as string;
            }
            catch { return null; }
        }

        private static Vector3? UnitPosition(object unit)
        {
            if (unit is Component component) return component.transform.position;
            return null;
        }

        private static object ReadObject(object target, string field)
        {
            try { return AccessTools.Field(target.GetType(), field)?.GetValue(target); }
            catch { return null; }
        }

        private static bool ReadBool(object target, string field, bool fallback)
        {
            try
            {
                var info = AccessTools.Field(target.GetType(), field);
                return info == null ? fallback : Convert.ToBoolean(info.GetValue(target));
            }
            catch { return fallback; }
        }

        private static int ReadInt(object target, string field, int fallback)
        {
            try
            {
                var info = AccessTools.Field(target.GetType(), field);
                return info == null ? fallback : Convert.ToInt32(info.GetValue(target));
            }
            catch { return fallback; }
        }

        private static float ReadFloat(object target, float fallback, params string[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                try
                {
                    var info = AccessTools.Field(target.GetType(), fields[i]);
                    if (info != null) return Math.Max(0f, Convert.ToSingle(info.GetValue(target)));
                }
                catch { return fallback; }
            }
            return fallback;
        }
    }
}
```

- [ ] **Step 2: Build and fix compile errors locally**

Run:

```bash
./build.sh
```

Expected: build passes. If it fails because a field like `alliance` is not present on `Regiment`, replace that read with the known faction/ownunits loop context rather than throwing from runtime extraction.

- [ ] **Step 3: Commit Task 3**

Run:

```bash
git add src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs
git commit -m "feat: extract campaign formation snapshots"
```

## Task 4: Weekly Coordinator Wiring And Logging

**Files:**

- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`

- [ ] **Step 1: Add ledger fields**

Add the array and signature cache next to `ArmyAreas`:

```csharp
public FormationDirectiveLedger[] FormationDirectives = new FormationDirectiveLedger[2];
private readonly string[] _formationDirectiveSignatures = new string[2];
```

- [ ] **Step 2: Call the formation update after army-area update**

In `RunStrategicReview`, after:

```csharp
UpdateArmyAreaLedger(alliance, cic);
```

add:

```csharp
UpdateFormationDirectiveLedger(alliance, cic, era);
```

- [ ] **Step 3: Add the update method**

Add this method below `UpdateArmyAreaLedger`:

```csharp
private void UpdateFormationDirectiveLedger(int alliance, CIC cic, EraStageManager era)
{
    int targetObjectiveId = cic?.ActivePlan?.CurrentPhase?.TargetObjectiveId ?? -1;
    string planTargetAreaKey = null;
    var targetPosition = ObjectiveAdapter.ResolveObjectivePosition(targetObjectiveId);
    if (targetPosition.HasValue)
        planTargetAreaKey = ArmyAreaRuntime.AreaKey(targetPosition.Value);

    var ledger = FormationDirectiveRuntime.BuildForAlliance(alliance, era.Stage, planTargetAreaKey);
    if (ledger == null) return;

    FormationDirectives[alliance] = ledger;
    string signature = ledger.Summary();
    if (Plugin.Instance.VerboseLogging.Value || _formationDirectiveSignatures[alliance] != signature)
    {
        Plugin.Log.LogInfo(
            $"[FormationDirective] alliance={alliance} summary={signature} " +
            $"lowSupply={ledger.Pressure.LowSupplyCount} lowAmmo={ledger.Pressure.LowAmmoCount} " +
            $"recover={ledger.Pressure.RecoverCount} mass={ledger.Pressure.MassCount} " +
            $"supplyArea={ledger.Pressure.TopSupplyAreaKey ?? "<none>"}");
        _formationDirectiveSignatures[alliance] = signature;
    }
}
```

- [ ] **Step 4: Verify tests and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: tests pass and `dist/WhiskeyRealism.dll` is produced.

- [ ] **Step 5: Commit Task 4**

Run:

```bash
git add src/WhiskeyRealism/Strategic/StrategicCoordinator.cs
git commit -m "feat: refresh formation directives weekly"
```

## Task 5: Army-Area Integration For Independent Divisions

**Files:**

- Modify: `src/WhiskeyRealism/Strategic/ArmyAreaRuntime.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add pure regression tests for division inclusion**

The existing pure `ArmyAreaLedger` already consumes input rows. Add this test to prove division-like rows remain safe when out of area:

```csharp
("army area ledger can redirect independent division input", ArmyAreaLedgerCanRedirectIndependentDivisionInput),
```

Add the method:

```csharp
private static void ArmyAreaLedgerCanRedirectIndependentDivisionInput()
{
    var ledger = ArmyAreaLedger.Build(new[]
    {
        new ArmyAreaInput
        {
            UnitKey = "division",
            AllianceId = 1,
            UnitName = "Army of Northern Virginia",
            CommanderName = "Lee",
            CurrentAreaKey = "TennesseeGeorgiaCorridor",
            Strength = 5000f,
            Readiness = 0.75f
        }
    }, planTargetAreaKey: null);

    var assignment = ledger.GetAssignment("division");
    AssertEqual("VirginiaCapitalCorridor", assignment.AssignedAreaKey);
    AssertEqual(true, assignment.OutOfArea);
}
```

- [ ] **Step 2: Update top strategic unit classification**

Replace `IsTopStrategicUnit` in `ArmyAreaRuntime.cs` with:

```csharp
private static bool IsTopStrategicUnit(object unit)
{
    if (unit == null) return false;
    try
    {
        int unitType = Convert.ToInt32(AccessTools.Field(unit.GetType(), "unittyp")?.GetValue(unit) ?? -1);
        bool top = Convert.ToBoolean(AccessTools.Field(unit.GetType(), "istopunit")?.GetValue(unit) ?? false);
        bool garrisoned = AccessTools.Field(unit.GetType(), "garrisonreference")?.GetValue(unit) != null;
        float direct = ReadFloat(0f, unit, "groupstrengthdirect", "groupstrengthactive", "groupstrength", "strength");
        return top && !garrisoned && unitType >= 14 && unitType <= 16 && direct > 1000f;
    }
    catch { return false; }
}
```

- [ ] **Step 3: Consult directives before issuing area movement**

In `ApplyHistoricalAreaOrders`, after the assignment/out-of-area checks and before `TryGetAnchor`, add:

```csharp
if (!FormationDirectiveRuntime.ShouldAllowAreaMovement(allianceId, UnitKey(unit)))
{
    OnceLog.Info(
        $"army-area:{allianceId}:{UnitKey(unit)}:directive-block",
        $"[Patch:ArmyArea] alliance={allianceId} unit={ObjectName(unit)} action=skip-return-area reason=formation-directive");
    continue;
}
```

- [ ] **Step 4: Verify**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: tests and build pass.

- [ ] **Step 5: Commit Task 5**

Run:

```bash
git add src/WhiskeyRealism/Strategic/ArmyAreaRuntime.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: include independent divisions in army areas"
```

## Task 6: Army-Group Directive-Aware Attachments

**Files:**

- Modify: `src/WhiskeyRealism/Patches/ArmyGroupManagementPatch.cs`

- [ ] **Step 1: Allow division attachments without creating division-led groups**

Replace `IsEligibleTopUnit` with this version:

```csharp
private static bool IsEligibleTopUnit(Regiment unit)
{
    if ((UnityEngine.Object)(object)unit == (UnityEngine.Object)null) return false;
    if ((UnityEngine.Object)(object)unit.garrisonreference != (UnityEngine.Object)null) return false;
    if (!unit.istopunit) return false;
    if (unit.unittyp < 14 || unit.unittyp > 16) return false;
    if (unit.groupstrengthdirect <= 1000f) return false;
    if (unit.inbattle || unit.onretreat) return false;
    return true;
}
```

- [ ] **Step 2: Filter resolved units through directives**

In `ResolveUnits`, after the existing `plan.UnitKeys.Contains(UnitKey(unit))` check, add:

```csharp
int allianceId = unit.alliance;
if (unit.unittyp == 14 && !FormationDirectiveRuntime.AllowsArmyGroupAttachment(allianceId, UnitKey(unit)))
    continue;
```

If `Regiment.alliance` does not compile, pass `allianceId` into `ResolveUnits` from `ApplyPlan` and use that method parameter instead:

```csharp
var units = ResolveUnits(allianceId, plan, ownUnits);
```

and:

```csharp
private static List<Regiment> ResolveUnits(int allianceId, ArmyGroupPlan plan, IList ownUnits)
```

- [ ] **Step 3: Prevent division-only group creation**

Before creating a new group in `ApplyPlan`, require at least one corps or army:

```csharp
bool hasCorpsOrArmy = false;
for (int i = 0; i < unassigned.Count; i++)
{
    if (unassigned[i].unittyp >= 15 && unassigned[i].unittyp <= 16)
    {
        hasCorpsOrArmy = true;
        break;
    }
}
if (!hasCorpsOrArmy) return;
```

- [ ] **Step 4: Verify**

Run:

```bash
./build.sh
```

Expected: build passes.

- [ ] **Step 5: Commit Task 6**

Run:

```bash
git add src/WhiskeyRealism/Patches/ArmyGroupManagementPatch.cs
git commit -m "feat: make army groups directive aware"
```

## Task 7: Narrow Offensive Safety Gate

**Files:**

- Create: `src/WhiskeyRealism/Patches/FormationOffensiveSafetyPatch.cs`
- Modify: `docs/patch-catalog.md`

This task uses a Prefix that can return `false` only for non-player AI offensive checks where the current formation directive explicitly blocks offense. It should not run until Tasks 1-6 are built, deployed, and smoke-confirmed.

- [ ] **Step 1: Create `FormationOffensiveSafetyPatch.cs`**

```csharp
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla CheckOffensiveMovements builds local attacking packages using
    // strength, morale, weapon, support, initiative, area, and dominance gates.
    // This Prefix does not replace that logic. It only blocks clearly unsafe
    // offensive checks when the weekly FormationDirectiveLedger says this exact
    // top formation should not be used offensively.
    [HarmonyPatch(typeof(AICampaign), "CheckOffensiveMovements")]
    internal static class FormationOffensiveSafetyPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(int _aifaction, Regiment _unit)
        {
            OnceLog.Info("formation-offensive-safety", "FormationOffensiveSafetyPatch wired");

            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return true;
                if (StrategicCoordinator.Instance == null) return true;
                if ((UnityEngine.Object)(object)_unit == (UnityEngine.Object)null) return true;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0 || allianceId >= StrategicCoordinator.Instance.FormationDirectives.Length) return true;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return true;

                var ledger = StrategicCoordinator.Instance.FormationDirectives[allianceId];
                if (ledger == null) return true;

                var assignment = ledger.GetAssignment(UnitKey(_unit));
                if (assignment == null || assignment.OffensiveAllowed) return true;

                OnceLog.Info(
                    $"formation-offense:block:{allianceId}:{UnitKey(_unit)}:{assignment.Directive}",
                    $"[Patch:FormationDirective] alliance={allianceId} unit={_unit.name} action=block-offense directive={assignment.Directive} reason={assignment.Reason}");
                return false;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-offense:prefix", "[Patch:FormationDirective] offensive safety failed: " + ex.Message);
                return true;
            }
        }

        private static string UnitKey(Regiment unit)
        {
            int commander = -1;
            try { commander = unit.commander; }
            catch { }
            return unit.name + ":" + commander.ToString();
        }
    }
}
```

- [ ] **Step 2: Add patch catalog entry**

Add a new entry in `docs/patch-catalog.md` with:

```markdown
### #18 Formation Offensive Safety

- **Patch:** `FormationOffensiveSafetyPatch`
- **Target:** `AICampaign.CheckOffensiveMovements`
- **Shape:** Prefix, returns `false` only when the current non-player AI formation has a weekly directive that blocks offense.
- **First-fire marker:** `[once:formation-offensive-safety] FormationOffensiveSafetyPatch wired`
- **Runtime log:** `[Patch:FormationDirective] alliance=1 unit=Army of the Valley action=block-offense directive=Screen reason=division-vs-army-no-support`
- **Safety:** Falls through on missing coordinator, missing ledger, player-CIC faction, reflection failures, or assignments that allow offense.
```

- [ ] **Step 3: Verify**

Run:

```bash
./build.sh
```

Expected: build passes.

- [ ] **Step 4: Commit Task 7**

Run:

```bash
git add src/WhiskeyRealism/Patches/FormationOffensiveSafetyPatch.cs docs/patch-catalog.md
git commit -m "feat: block unsafe formation offensives"
```

## Task 8: Docs, Build, Deploy, Smoke

**Files:**

- Modify: `docs/handoff.md`
- Modify: `docs/superpowers/specs/2026-05-04-formation-directive-design.md` only if implementation reveals a corrected contract.

- [ ] **Step 1: Update handoff**

Add a v0.2.2 note to `docs/handoff.md`:

```markdown
- Formation directives: weekly ledger now classifies independent divisions/corps/armies, logs `[FormationDirective]`, feeds supply/ammo pressure for fiscal work, includes independent divisions in army-area steering, and makes army-group attachments directive-aware. Offensive safety patch blocks only non-player AI formations whose directive explicitly disallows offense.
```

- [ ] **Step 2: Run full local verification**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
git diff --check
```

Expected:

- tests pass;
- `dist/WhiskeyRealism.dll` exists;
- `git diff --check` has no output.

- [ ] **Step 3: Deploy DLL**

Run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
stat -c "%n %s %y" dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected:

- copy succeeds;
- both SHA-256 hashes match;
- both file sizes match;
- deployed timestamp is current.

- [ ] **Step 4: Runtime smoke**

Start GTCW, load/start a campaign, then tail:

```bash
tail -n 250 "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected markers:

- `[once:weeklyops] Weekly operational analysis active`
- `[FormationDirective] alliance=1 summary=Army of the Valley:Screen:division-vs-army-no-support`
- `[once:armyarea]` existing marker still appears when that patch fires
- `[once:armygroup]` existing marker still appears when that patch fires
- `[once:formation-offensive-safety] FormationOffensiveSafetyPatch wired` if Task 7 landed

Failure triage:

- If no `[FormationDirective]` appears, verify `StrategicCoordinator.UpdateFormationDirectiveLedger` is called after `UpdateArmyAreaLedger`.
- If log spam appears, verify the signature cache is suppressing unchanged summaries.
- If reflection warnings repeat, switch that warning to `OnceLog.Warning` keyed by field/method name.
- If the DLL copy fails with `Invalid argument`, close the game and redeploy.

- [ ] **Step 5: Commit docs closeout**

Run:

```bash
git add docs/handoff.md docs/superpowers/specs/2026-05-04-formation-directive-design.md
git commit -m "docs: record formation directive implementation"
```

Only include the spec file if it changed during implementation.

## Final Verification Before Push

- [ ] **Step 1: Confirm working tree state**

Run:

```bash
git status --short --branch
git log --oneline -8
```

Expected: only intentional files changed or committed. Do not commit unrelated staged fiscal-economy spec changes unless the user explicitly says to include them.

- [ ] **Step 2: Push**

Run:

```bash
git push origin main
git status --short --branch
```

Expected: branch reports in sync except for any intentionally uncommitted unrelated work.

## Self-Review Notes

- Spec coverage: Tasks 1-2 cover pure model, directive vocabulary, risk gates, pressure outputs, and no raw-headcount scoring. Task 3 covers runtime extraction. Task 4 covers weekly coordinator/logging. Tasks 5-6 cover #15/#16 integration. Task 7 covers offensive safety as the only narrow Prefix gate. Task 8 covers docs, deploy, and smoke.
- Fiscal dependency: `FormationPressureSummary` intentionally exposes low-supply, low-ammo, recover, guard, mass, and top supply area so the later fiscal plan can consume real military pressure.
- Known risk: Task 7 is a Prefix-blocking patch. Keep it late, narrow, and non-player only. If runtime smoke shows vanilla offensive cadence gets too quiet, revert only Task 7 and keep ledger/#15/#16.
