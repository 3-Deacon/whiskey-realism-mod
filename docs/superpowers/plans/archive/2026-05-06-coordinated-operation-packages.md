# Coordinated Operation Packages Implementation Plan

Status: archived after implementation. Current behavior lives in `docs/coordinated-operation-packages.md`, shipped code, and `docs/patch-catalog.md`.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build coordinated campaign-map attack and reinforcement packages across Whiskey's strategic probe/director layer and vanilla offensive operations, while preserving W&L current-order and vanilla operation-list semantics.

**Architecture:** Add a pure `CoordinatedOperationPackageLedger` and small DTOs for package selection, then adapt live formation/director/vanilla state into those DTOs. Commit packages through a shared runtime that rechecks vanilla availability and W&L bridge eligibility. Add patch #38 on `AICampaign.CheckOffensiveMovements(...)` with per-cycle cached filtering so vanilla offensive packages are steered without a Transpiler.

**Tech Stack:** C# netstandard2.1, BepInEx 5/HarmonyX, Unity 2021 Mono, console harness `tests/WhiskeyRealism.Tests`, decompile source `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

---

## Source Anchors

- Spec: `docs/superpowers/specs/archive/2026-05-06-coordinated-operation-packages-design.md`
- Vanilla offensive package: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14166`
- Vanilla offensive scheduler: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:11319`
- Vanilla offensive operation list commit: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14378`
- Vanilla W&L offensive current orders: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14449`
- Vanilla empty-target single-unit rule: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14374`
- Vanilla offensive micro-movement skip-active-path gate: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14004`
- Current W&L bridge: `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`
- Probe runtime: `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`
- Probe ledger: `src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs`
- Formation runtime/ledger: `src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs`, `src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs`, `src/WhiskeyRealism/Strategic/FormationSnapshot.cs`
- Defensive filter pattern: `src/WhiskeyRealism/Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs`
- Patch catalog reservation: `docs/patch-catalog.md` reserves #38.

## File Structure

- Create `src/WhiskeyRealism/Strategic/CoordinatedOperationPackageLedger.cs`
  - Pure DTOs, selector, director option builder, target-name helper methods that do not touch Unity.
- Create `src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs`
  - Live adapter from `FormationDirectiveLedger`/vanilla lists/W&L facts to pure candidates, target-name resolver, package commit path.
- Create `src/WhiskeyRealism/Patches/CoordinatedOffensiveOperationsPatch.cs`
  - Patch #38 Prefix/Postfix on `AICampaign.CheckOffensiveMovements(...)`; per-cycle cache, ownunits snapshot/restore, lead/candidate filtering.
- Modify `src/WhiskeyRealism/Strategic/FormationSnapshot.cs`
  - Add `StableUnitId`, `X`, `Z`.
- Modify `src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs`
  - Add stable id and position fields to `FormationDirectiveAssignment`; update probe overlay to support packages.
- Modify `src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs`
  - Populate stable id and X/Z from live `Regiment`.
- Modify `src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs`
  - Add package output fields; keep legacy fields during transition.
- Modify `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`
  - Build package input, call selector/runtime commit, log package result.
- Modify `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
  - Pass current `DirectorPosture` once into package options; no double-application.
- Modify `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`
  - Add `WlStrategicIntent.Reinforce` mapped to order type 5.
- Modify `tests/WhiskeyRealism.Tests/Program.cs`
  - Add pure tests and bridge tests.
- Modify `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
  - Explicit `<Compile Include>` for new strategic files.
- Modify `docs/patch-catalog.md`
  - Move #38 from pending reservation into the shipped table when implementation is verified.
- Modify `docs/handoff.md`
  - Record shipped behavior, deploy hash, smoke status.

---

### Task 1: W&L Reinforce Intent

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing tests**

Add the test registration near the other W&L bridge tests:

```csharp
("wl bridge reinforce maps to redeploy order", WlBridgeReinforceMapsToRedeployOrder),
("wl bridge reinforce eligible under commander issues current order", WlBridgeReinforceEligibleUnderCommanderIssuesCurrentOrder),
```

Add these methods near `WlBridgeEligibleUnderCommanderIssuesCurrentOrder()`:

```csharp
private static void WlBridgeReinforceMapsToRedeployOrder()
{
    var decision = WlStrategicOrderBridge.Classify(
        WlStrategicIntent.Reinforce,
        new WlStrategicRoleFacts(wlActive: false, isPlayerAlliance: true));

    AssertEqual(WlStrategicOrderResult.NotWl, decision.Result);
    AssertEqual(5, decision.WlOrderType);
    AssertEqual(true, decision.MayDirectMove);
}

private static void WlBridgeReinforceEligibleUnderCommanderIssuesCurrentOrder()
{
    var facts = new WlStrategicRoleFacts
    {
        WlActive = true,
        IsPlayerAlliance = true,
        IsUnderCommander = true,
        CurrentCommandIsCampaignGroup = true,
        CurrentCommandParentIsUnderTargetUnit = true
    };

    var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.Reinforce, facts);

    AssertEqual(WlStrategicOrderResult.IssuedWlCurrentOrder, decision.Result);
    AssertEqual(5, decision.WlOrderType);
    AssertEqual(false, decision.MayDirectMove);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure because `WlStrategicIntent.Reinforce` does not exist.

- [ ] **Step 3: Implement bridge intent**

In `WlStrategicOrderBridge.cs`, add enum member after `Probe`:

```csharp
Reinforce,
```

Update `WlOrderTypeForIntent(...)`:

```csharp
case WlStrategicIntent.Redeploy:
case WlStrategicIntent.Probe:
case WlStrategicIntent.Reinforce:
    return 5;
```

- [ ] **Step 4: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS, including the two new W&L bridge tests.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: map wl reinforce strategic order"
```

---

### Task 2: Formation Position And Stable Identity

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/FormationSnapshot.cs`
- Modify: `src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs`
- Modify: `src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing tests**

Add registrations near existing formation directive tests:

```csharp
("formation directive carries stable id and position", FormationDirectiveCarriesStableIdAndPosition),
("formation directive summary changes on stable position", FormationDirectiveSummaryChangesOnStablePosition),
```

Add methods near `FormationDirectiveSummaryChangesWhenAssignmentChanges()`:

```csharp
private static void FormationDirectiveCarriesStableIdAndPosition()
{
    var snap = Snapshot("position-corps", 1, 15, 9000f, 5000f, FormationLevel.Corps, FrontPosture.Counterstroke);
    snap.StableUnitId = 4242;
    snap.X = 123.5f;
    snap.Z = -456.25f;

    var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Operational1862, null);
    var assignment = ledger.GetAssignment("position-corps");

    AssertEqual(4242, assignment.StableUnitId);
    AssertNear(123.5f, assignment.X, 0.0001f, "assignment X");
    AssertNear(-456.25f, assignment.Z, 0.0001f, "assignment Z");
}

private static void FormationDirectiveSummaryChangesOnStablePosition()
{
    var a = Snapshot("position-corps", 1, 15, 9000f, 5000f, FormationLevel.Corps, FrontPosture.Counterstroke);
    var b = Snapshot("position-corps", 1, 15, 9000f, 5000f, FormationLevel.Corps, FrontPosture.Counterstroke);
    a.StableUnitId = 1;
    b.StableUnitId = 1;
    a.X = 10f;
    a.Z = 10f;
    b.X = 40f;
    b.Z = 10f;

    string first = FormationDirectiveLedger.Build(new[] { a }, EraStage.Operational1862, null).Summary();
    string second = FormationDirectiveLedger.Build(new[] { b }, EraStage.Operational1862, null).Summary();

    AssertTrue(first != second, "summary must change when stable position changes");
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure for missing `StableUnitId`, `X`, and `Z`.

- [ ] **Step 3: Add fields**

In `FormationSnapshot.cs`, add after `AllianceId`:

```csharp
public int StableUnitId;
```

Add after `SectorKey`:

```csharp
public float X;
public float Z;
```

In `FormationDirectiveAssignment`, add after `AllianceId`:

```csharp
public int StableUnitId;
```

Add after `SectorKey`:

```csharp
public float X;
public float Z;
```

- [ ] **Step 4: Populate assignment fields**

In `FormationDirectiveLedger.BaseAssignment(...)`, add:

```csharp
StableUnitId = snapshot.StableUnitId,
X = snapshot.X,
Z = snapshot.Z,
```

Update `FormationDirectiveLedger.Summary()` to include stable position:

```csharp
parts.Add($"{assignment.UnitKey}:{assignment.StableUnitId}:{assignment.Directive}:{assignment.Reason}:{Math.Round(assignment.X, 1)}:{Math.Round(assignment.Z, 1)}");
```

- [ ] **Step 5: Populate live snapshot fields**

In `FormationDirectiveRuntime.SnapshotUnit(...)`, add to object initializer:

```csharp
StableUnitId = ((UnityEngine.Object)unit).GetInstanceID(),
X = unit.transform.position.x,
Z = unit.transform.position.z,
```

- [ ] **Step 6: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/WhiskeyRealism/Strategic/FormationSnapshot.cs src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: carry formation stable id and position"
```

---

### Task 3: Pure Coordinated Package Selector

**Files:**
- Create: `src/WhiskeyRealism/Strategic/CoordinatedOperationPackageLedger.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

- [ ] **Step 1: Add compile include first**

Add to `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` after `OperationalProbeLedger.cs`:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\CoordinatedOperationPackageLedger.cs" Link="CoordinatedOperationPackageLedger.cs" />
```

- [ ] **Step 2: Write failing tests**

Add registrations near operational probe tests:

```csharp
("coordinated ops attack selects local support", CoordinatedOpsAttackSelectsLocalSupport),
("coordinated ops blocked wl support does not fake attack", CoordinatedOpsBlockedWlSupportDoesNotFakeAttack),
("coordinated ops empty target is single lead", CoordinatedOpsEmptyTargetIsSingleLead),
("coordinated ops high risk tightens donor caps", CoordinatedOpsHighRiskTightensDonorCaps),
("coordinated ops player cic returns none", CoordinatedOpsPlayerCicReturnsNone),
("coordinated ops refuses live operation list candidates", CoordinatedOpsRefusesLiveOperationListCandidates),
("coordinated ops deterministic tie by stable id", CoordinatedOpsDeterministicTieByStableId),
```

Add helper and tests near the operational probe test block:

```csharp
private static CoordinatedOperationCandidate OpCandidate(
    int id,
    string key,
    float x,
    float z,
    float strength,
    CoordinatedCommitMode commit = CoordinatedCommitMode.DirectMovement,
    string sector = "VirginiaCapitalCorridor",
    string area = "VirginiaCapitalCorridor")
{
    return new CoordinatedOperationCandidate
    {
        StableUnitId = id,
        DisplayUnitKey = key,
        AllianceId = 1,
        Level = FormationLevel.Corps,
        Directive = FormationDirective.Counterstroke,
        AreaKey = area,
        SectorKey = sector,
        X = x,
        Z = z,
        CombatAvailability = strength,
        ExchangePressure = strength,
        Readiness = 0.8f,
        Morale = 0.8f,
        Ammo = 0.8f,
        Supply = 0.8f,
        OffensiveAllowed = true,
        DefensiveAllowed = true,
        DirectMovementAllowed = true,
        CommitMode = commit,
        FrontPosture = FrontPosture.Counterstroke
    };
}

private static CoordinatedOperationInput OpInput(params CoordinatedOperationCandidate[] candidates)
{
    return new CoordinatedOperationInput
    {
        AllianceId = 1,
        IsPlayerCic = false,
        Intent = CoordinatedOperationIntent.Attack,
        TargetName = "Manassas",
        TargetAreaKey = "VirginiaCapitalCorridor",
        TargetSectorKey = "VirginiaCapitalCorridor",
        TargetX = 0f,
        TargetZ = 0f,
        TargetEnemyStrength = 10000f,
        Options = CoordinatedOperationOptions.StableDefaults(10000f),
        Candidates = new List<CoordinatedOperationCandidate>(candidates)
    };
}

private static void CoordinatedOpsAttackSelectsLocalSupport()
{
    var output = CoordinatedOperationPackageLedger.Build(OpInput(
        OpCandidate(10, "lead", 0f, 0f, 8000f),
        OpCandidate(20, "support", 5f, 0f, 6000f),
        OpCandidate(30, "remote", 500f, 0f, 8000f, CoordinatedCommitMode.DirectMovement, "RemoteSector", "RemoteArea")));

    AssertEqual(CoordinatedOperationDecision.CoordinateAttack, output.Decision);
    AssertEqual(10, output.LeadStableUnitId);
    AssertEqual(1, output.SupportStableUnitIds.Count);
    AssertEqual(20, output.SupportStableUnitIds[0]);
    AssertTrue(output.Ratio >= 1.25f, "attack ratio should pass");
}

private static void CoordinatedOpsBlockedWlSupportDoesNotFakeAttack()
{
    var output = CoordinatedOperationPackageLedger.Build(OpInput(
        OpCandidate(10, "lead", 0f, 0f, 9000f),
        OpCandidate(20, "blocked", 5f, 0f, 6000f, CoordinatedCommitMode.BlockedWlPlayerChain)));

    AssertTrue(output.Decision != CoordinatedOperationDecision.CoordinateAttack, "blocked support must not count");
    AssertEqual(CoordinatedOperationDecision.SingleLead, output.Decision);
    AssertEqual(1, output.Suppressed.Count);
    AssertEqual("blocked-commit-mode", output.Suppressed[0].Reason);
}

private static void CoordinatedOpsEmptyTargetIsSingleLead()
{
    var input = OpInput(
        OpCandidate(10, "lead", 0f, 0f, 9000f),
        OpCandidate(20, "support", 5f, 0f, 9000f));
    input.Intent = CoordinatedOperationIntent.Probe;
    input.TargetEnemyStrength = 0f;

    var output = CoordinatedOperationPackageLedger.Build(input);

    AssertEqual(CoordinatedOperationDecision.SingleLead, output.Decision);
    AssertEqual(0, output.SupportStableUnitIds.Count);
}

private static void CoordinatedOpsHighRiskTightensDonorCaps()
{
    var options = CoordinatedOperationOptions.FromDirector(10000f, new DirectorPosture
    {
        Pace = CampaignPace.Overheated,
        Risk = CollapseRisk.Critical
    });
    var input = OpInput(
        OpCandidate(10, "lead", 0f, 0f, 9000f),
        OpCandidate(20, "support-a", 5f, 0f, 4000f),
        OpCandidate(30, "support-b", 6f, 0f, 4000f));
    input.Options = options;

    var output = CoordinatedOperationPackageLedger.Build(input);

    AssertTrue(output.SupportStableUnitIds.Count <= 1, "high risk caps support units to one");
}

private static void CoordinatedOpsPlayerCicReturnsNone()
{
    var input = OpInput(OpCandidate(10, "lead", 0f, 0f, 20000f));
    input.IsPlayerCic = true;

    var output = CoordinatedOperationPackageLedger.Build(input);

    AssertEqual(CoordinatedOperationDecision.None, output.Decision);
    AssertEqual("player-cic", output.Reason);
}

private static void CoordinatedOpsRefusesLiveOperationListCandidates()
{
    var inOps = OpCandidate(10, "in-ops", 0f, 0f, 20000f);
    inOps.InOffensiveOperation = true;

    var output = CoordinatedOperationPackageLedger.Build(OpInput(inOps));

    AssertEqual(CoordinatedOperationDecision.None, output.Decision);
    AssertEqual("no-eligible-lead", output.Reason);
}

private static void CoordinatedOpsDeterministicTieByStableId()
{
    var output = CoordinatedOperationPackageLedger.Build(OpInput(
        OpCandidate(30, "higher", 0f, 0f, 9000f),
        OpCandidate(10, "lower", 0f, 0f, 9000f)));

    AssertEqual(10, output.LeadStableUnitId);
}
```

- [ ] **Step 3: Run tests to verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure because `CoordinatedOperationPackageLedger` and DTOs do not exist.

- [ ] **Step 4: Create selector file**

Create `CoordinatedOperationPackageLedger.cs` with these public/internal types:

```csharp
using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public enum CoordinatedOperationIntent { Probe, Attack, Reinforce, Continuation }
    public enum CoordinatedOperationDecision { None, SingleLead, CoordinateAttack, Reinforce, Delay, Recover }
    public enum CoordinatedCommitMode { DirectMovement, WlCurrentOrder, BlockedWlPlayerChain }

    public sealed class CoordinatedOperationOptions
    {
        public float RequiredAttackRatio = 1.25f;
        public float RequiredReinforceRatio = 0.85f;
        public int MaxSupportUnits = 2;
        public float MaxSupportEffectiveStrength = 12500f;
        public bool AllowRemoteTier;
        public bool AllowEmptyTargetPackage;
        public float NearbyRange = 80f;
        public float RemoteRange = 180f;
        public float MinimumReadiness = 0.55f;
        public float MinimumMorale = 0.35f;
        public float MinimumAmmo = 0.25f;
        public float MinimumSupply = 0.35f;

        public static CoordinatedOperationOptions StableDefaults(float desiredStrength)
        {
            return new CoordinatedOperationOptions
            {
                RequiredAttackRatio = 1.25f,
                RequiredReinforceRatio = 0.85f,
                MaxSupportUnits = 2,
                MaxSupportEffectiveStrength = Math.Max(0f, desiredStrength) * 1.25f,
                AllowRemoteTier = false
            };
        }

        public static CoordinatedOperationOptions FromDirector(float desiredStrength, DirectorPosture posture)
        {
            var options = StableDefaults(desiredStrength);
            if (posture == null) return options;
            if (posture.Pace == CampaignPace.TooQuiet || posture.Pace == CampaignPace.Stalemated)
            {
                options.RequiredAttackRatio = 1.15f;
                options.RequiredReinforceRatio = 0.75f;
                options.MaxSupportUnits = 3;
                options.MaxSupportEffectiveStrength = Math.Max(0f, desiredStrength) * 1.50f;
                options.AllowRemoteTier = true;
            }
            if (posture.Pace == CampaignPace.Overheated ||
                posture.Pace == CampaignPace.TooFastCollapse ||
                posture.Risk >= CollapseRisk.Critical)
            {
                options.RequiredAttackRatio = 1.40f;
                options.RequiredReinforceRatio = 1.00f;
                options.MaxSupportUnits = 1;
                options.MaxSupportEffectiveStrength = Math.Max(0f, desiredStrength) * 0.75f;
                options.AllowRemoteTier = false;
            }
            return options;
        }
    }

    public sealed class CoordinatedOperationCandidate
    {
        public int StableUnitId;
        public string DisplayUnitKey;
        public int AllianceId;
        public FormationLevel Level;
        public FormationDirective Directive;
        public string AreaKey;
        public string SectorKey;
        public float X;
        public float Z;
        public float CombatAvailability;
        public float ExchangePressure;
        public float LocalFriendlySupport;
        public float LocalEnemyStrength;
        public float Readiness;
        public float Morale;
        public float Ammo;
        public float Supply;
        public float Fatigue;
        public bool OffensiveAllowed;
        public bool DefensiveAllowed;
        public bool TransferDonorAllowed;
        public bool DirectMovementAllowed;
        public bool InheritsFromParent;
        public bool CriticalSector;
        public FrontPosture FrontPosture;
        public bool InOffensiveOperation;
        public bool InDefensiveOperation;
        public bool ConstructingSupplyDepot;
        public CoordinatedCommitMode CommitMode;
    }

    public sealed class CoordinatedOperationInput
    {
        public int AllianceId;
        public bool IsPlayerCic;
        public CoordinatedOperationIntent Intent;
        public string TargetName;
        public string TargetAreaKey;
        public string TargetSectorKey;
        public float TargetX;
        public float TargetZ;
        public float TargetEnemyStrength;
        public int PreferredLeadStableUnitId;
        public CoordinatedOperationOptions Options;
        public List<CoordinatedOperationCandidate> Candidates = new List<CoordinatedOperationCandidate>();
    }

    public sealed class CoordinatedOperationSuppression
    {
        public int StableUnitId;
        public string DisplayUnitKey;
        public string Reason;
    }

    public sealed class CoordinatedOperationOutput
    {
        public CoordinatedOperationDecision Decision;
        public string Reason;
        public int LeadStableUnitId;
        public string LeadDisplayUnitKey;
        public List<int> SupportStableUnitIds = new List<int>();
        public List<string> SupportDisplayUnitKeys = new List<string>();
        public List<CoordinatedOperationSuppression> Suppressed = new List<CoordinatedOperationSuppression>();
        public float PackageEffectiveStrength;
        public float TargetEnemyStrength;
        public float Ratio;
        public string TargetName;

        public string Signature()
        {
            return ((int)Decision) + "|" + LeadStableUnitId + "|" +
                string.Join(",", SupportStableUnitIds) + "|" +
                (Reason ?? "-") + "|" + (TargetName ?? "-") + "|" +
                Math.Round(Ratio, 2);
        }
    }

    public static class CoordinatedOperationPackageLedger
    {
        public static CoordinatedOperationOutput Build(CoordinatedOperationInput input)
        {
            var output = new CoordinatedOperationOutput();
            if (input == null) return NoOp(output, "missing-input");
            output.TargetName = input.TargetName;
            output.TargetEnemyStrength = Math.Max(0f, input.TargetEnemyStrength);
            if (input.IsPlayerCic) return NoOp(output, "player-cic");
            if (input.Candidates == null || input.Candidates.Count == 0) return NoOp(output, "no-candidates");

            var options = input.Options ?? CoordinatedOperationOptions.StableDefaults(output.TargetEnemyStrength);
            var eligible = new List<CoordinatedOperationCandidate>();
            foreach (var c in input.Candidates)
            {
                string reason;
                if (!EligibleLead(c, input, options, out reason))
                {
                    Suppress(output, c, reason);
                    continue;
                }
                eligible.Add(c);
            }
            eligible.Sort(CompareLead);
            if (eligible.Count == 0) return NoOp(output, "no-eligible-lead");

            var lead = input.PreferredLeadStableUnitId > 0
                ? eligible.Find(c => c.StableUnitId == input.PreferredLeadStableUnitId) ?? eligible[0]
                : eligible[0];

            output.LeadStableUnitId = lead.StableUnitId;
            output.LeadDisplayUnitKey = lead.DisplayUnitKey;
            output.PackageEffectiveStrength = Math.Max(0f, lead.CombatAvailability);

            if (output.TargetEnemyStrength <= 0f && !options.AllowEmptyTargetPackage)
                return Finish(output, CoordinatedOperationDecision.SingleLead, "empty-target-single-lead");

            var supports = new List<CoordinatedOperationCandidate>();
            foreach (var c in eligible)
            {
                if (c.StableUnitId == lead.StableUnitId) continue;
                string reason;
                if (!EligibleSupport(c, lead, input, options, out reason))
                {
                    Suppress(output, c, reason);
                    continue;
                }
                supports.Add(c);
            }
            supports.Sort((a, b) =>
            {
                int d = DistanceBucket(a, input).CompareTo(DistanceBucket(b, input));
                if (d != 0) return d;
                return a.StableUnitId.CompareTo(b.StableUnitId);
            });

            float supportEffective = 0f;
            foreach (var s in supports)
            {
                if (output.SupportStableUnitIds.Count >= options.MaxSupportUnits)
                {
                    Suppress(output, s, "support-unit-cap");
                    continue;
                }
                if (supportEffective + Math.Max(0f, s.CombatAvailability) > options.MaxSupportEffectiveStrength)
                {
                    Suppress(output, s, "support-strength-cap");
                    continue;
                }
                if (Ratio(output.PackageEffectiveStrength, output.TargetEnemyStrength) >= options.RequiredAttackRatio &&
                    output.SupportStableUnitIds.Count > 0)
                {
                    Suppress(output, s, "overmatch");
                    continue;
                }
                output.SupportStableUnitIds.Add(s.StableUnitId);
                output.SupportDisplayUnitKeys.Add(s.DisplayUnitKey);
                supportEffective += Math.Max(0f, s.CombatAvailability);
                output.PackageEffectiveStrength += Math.Max(0f, s.CombatAvailability);
            }

            output.Ratio = Ratio(output.PackageEffectiveStrength, output.TargetEnemyStrength);
            if (output.Ratio >= options.RequiredAttackRatio && output.SupportStableUnitIds.Count > 0)
                return Finish(output, CoordinatedOperationDecision.CoordinateAttack, "attack-ratio-passed");
            if (output.Ratio >= options.RequiredReinforceRatio && output.SupportStableUnitIds.Count > 0)
                return Finish(output, CoordinatedOperationDecision.Reinforce, "reinforce-ratio-passed");
            if (lead.Morale < options.MinimumMorale || lead.Readiness < options.MinimumReadiness)
                return Finish(output, CoordinatedOperationDecision.Recover, "lead-health-low");
            if (output.SupportStableUnitIds.Count == 0)
                return Finish(output, CoordinatedOperationDecision.SingleLead, "single-committable-lead");
            return Finish(output, CoordinatedOperationDecision.Delay, "package-understrength");
        }

        private static bool EligibleLead(CoordinatedOperationCandidate c, CoordinatedOperationInput input, CoordinatedOperationOptions options, out string reason)
        {
            reason = null;
            if (c == null) { reason = "null-candidate"; return false; }
            if (c.AllianceId != input.AllianceId) { reason = "wrong-alliance"; return false; }
            if (c.InheritsFromParent) { reason = "inherits-parent"; return false; }
            if (!c.DirectMovementAllowed) { reason = "direct-movement-blocked"; return false; }
            if (!c.OffensiveAllowed) { reason = "offensive-blocked"; return false; }
            if (c.InOffensiveOperation) { reason = "in-offensive-operation"; return false; }
            if (c.InDefensiveOperation) { reason = "in-defensive-operation"; return false; }
            if (c.ConstructingSupplyDepot) { reason = "constructing-supply-depot"; return false; }
            if (c.CommitMode == CoordinatedCommitMode.BlockedWlPlayerChain) { reason = "blocked-commit-mode"; return false; }
            if (c.Directive == FormationDirective.Guard || c.Directive == FormationDirective.Hold ||
                c.Directive == FormationDirective.Recover || c.Directive == FormationDirective.Concede)
            { reason = "directive-blocked"; return false; }
            if (c.CriticalSector) { reason = "critical-sector"; return false; }
            if (c.Readiness < options.MinimumReadiness) { reason = "low-readiness"; return false; }
            if (c.Morale < options.MinimumMorale) { reason = "low-morale"; return false; }
            if (c.Ammo < options.MinimumAmmo) { reason = "low-ammo"; return false; }
            if (c.Supply < options.MinimumSupply) { reason = "low-supply"; return false; }
            return true;
        }

        private static bool EligibleSupport(CoordinatedOperationCandidate c, CoordinatedOperationCandidate lead, CoordinatedOperationInput input, CoordinatedOperationOptions options, out string reason)
        {
            if (!EligibleLead(c, input, options, out reason)) return false;
            int bucket = DistanceBucket(c, input);
            if (bucket > 1 && !options.AllowRemoteTier) { reason = "remote-tier-blocked"; return false; }
            if (bucket > 2) { reason = "outside-range"; return false; }
            return true;
        }

        private static int CompareLead(CoordinatedOperationCandidate a, CoordinatedOperationCandidate b)
        {
            int d = b.CombatAvailability.CompareTo(a.CombatAvailability);
            return d != 0 ? d : a.StableUnitId.CompareTo(b.StableUnitId);
        }

        private static int DistanceBucket(CoordinatedOperationCandidate c, CoordinatedOperationInput input)
        {
            float d = Distance(c.X, c.Z, input.TargetX, input.TargetZ);
            var options = input.Options ?? CoordinatedOperationOptions.StableDefaults(input.TargetEnemyStrength);
            if (StringEquals(c.SectorKey, input.TargetSectorKey) && d <= options.RemoteRange) return 0;
            if (StringEquals(c.AreaKey, input.TargetAreaKey) && d <= options.RemoteRange) return 1;
            if (d <= options.NearbyRange) return 1;
            if (d <= options.RemoteRange) return 2;
            return 3;
        }

        private static CoordinatedOperationOutput Finish(CoordinatedOperationOutput output, CoordinatedOperationDecision decision, string reason)
        {
            output.Decision = decision;
            output.Reason = reason;
            output.Ratio = Ratio(output.PackageEffectiveStrength, output.TargetEnemyStrength);
            return output;
        }

        private static CoordinatedOperationOutput NoOp(CoordinatedOperationOutput output, string reason)
        {
            output.Decision = CoordinatedOperationDecision.None;
            output.Reason = reason;
            return output;
        }

        private static void Suppress(CoordinatedOperationOutput output, CoordinatedOperationCandidate c, string reason)
        {
            if (c == null) return;
            output.Suppressed.Add(new CoordinatedOperationSuppression
            {
                StableUnitId = c.StableUnitId,
                DisplayUnitKey = c.DisplayUnitKey,
                Reason = reason
            });
        }

        private static float Ratio(float own, float enemy) => own / Math.Max(1f, enemy);
        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
        private static bool StringEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 5: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS for new selector tests and existing harness.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Strategic/CoordinatedOperationPackageLedger.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat: add coordinated operation package selector"
```

---

### Task 4: Runtime Adapter And Commit Path

**Files:**
- Create: `src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`

- [ ] **Step 1: Add compile include**

Add to `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` after the selector include:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\CoordinatedOperationRuntime.cs" Link="CoordinatedOperationRuntime.cs" />
```

- [ ] **Step 2: Create runtime file with pure helper surface first**

Create `CoordinatedOperationRuntime.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class CoordinatedOperationRuntime
    {
        internal static CoordinatedCommitMode CommitModeFromBridge(WlStrategicOrderDecision decision)
        {
            if (decision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
                return CoordinatedCommitMode.WlCurrentOrder;
            if (decision.MayDirectMove)
                return CoordinatedCommitMode.DirectMovement;
            return CoordinatedCommitMode.BlockedWlPlayerChain;
        }

        internal static string ResolveTargetName(int objectiveId, string fallbackAreaKey, CampaignMapLedger map, Vector3 target)
        {
            string objectiveName = ObjectiveAdapter.ResolveObjectiveName(objectiveId);
            if (!string.IsNullOrEmpty(objectiveName)) return objectiveName;
            string nearest = NearestMapName(map, target);
            if (!string.IsNullOrEmpty(nearest)) return nearest;
            if (!string.IsNullOrEmpty(fallbackAreaKey)) return fallbackAreaKey;
            return "Objective";
        }

        internal static string NearestMapName(CampaignMapLedger map, Vector3 target)
        {
            if (map == null) return null;
            string bestName = null;
            float best = float.MaxValue;
            foreach (var town in map.Towns)
                Consider(town.CityName, town.X, town.Z, target, ref bestName, ref best);
            foreach (var asset in map.Assets)
                Consider(asset.Name, asset.X, asset.Z, target, ref bestName, ref best);
            return bestName;
        }

        private static void Consider(string name, float x, float z, Vector3 target, ref string bestName, ref float best)
        {
            if (string.IsNullOrEmpty(name)) return;
            float dx = x - target.x;
            float dz = z - target.z;
            float d = dx * dx + dz * dz;
            if (d < best)
            {
                best = d;
                bestName = name;
            }
        }
    }
}
```

- [ ] **Step 3: Add objective-name resolver**

Add this method to `ObjectiveAdapter.cs` after `ResolveObjectivePosition(int)`:

```csharp
internal static string ResolveObjectiveName(int objectiveId)
{
    try
    {
        var obj = FindCampaignObjective(objectiveId);
        if (obj == null) return null;
        return AccessTools.Field(obj.GetType(), "ObjectiveName")?.GetValue(obj) as string;
    }
    catch (Exception ex)
    {
        OnceLog.Warning(
            "objname:" + objectiveId,
            $"[ObjectiveAdapter] name resolve failed for objective ID {objectiveId}: {ex.Message}");
        return null;
    }
}
```

- [ ] **Step 4: Add commit implementation**

Extend `CoordinatedOperationRuntime` with:

```csharp
internal static void CommitPackage(
    int allianceId,
    int aifactionIndex,
    CoordinatedOperationOutput output,
    Vector3 target,
    string targetName,
    WlStrategicIntent intent,
    string sourceSystem)
{
    try
    {
        if (output == null || output.Decision == CoordinatedOperationDecision.None) return;
        var faction = AICampaignReflect.GetFaction(aifactionIndex);
        if (faction == null) return;
        var ownUnits = AccessTools.Field(faction.GetType(), "ownunits")?.GetValue(faction) as IList;
        var offensive = AccessTools.Field(faction.GetType(), "unitsinoffensiveoperations")?.GetValue(faction) as IList;
        if (ownUnits == null || offensive == null) return;

        CommitUnit(allianceId, aifactionIndex, ownUnits, offensive, output.LeadStableUnitId, target, targetName, intent, sourceSystem, output.Signature());
        for (int i = 0; i < output.SupportStableUnitIds.Count; i++)
        {
            var supportIntent = output.Decision == CoordinatedOperationDecision.Reinforce
                ? WlStrategicIntent.Reinforce
                : intent;
            CommitUnit(allianceId, aifactionIndex, ownUnits, offensive, output.SupportStableUnitIds[i], target, targetName, supportIntent, sourceSystem, output.Signature());
        }
    }
    catch (Exception ex)
    {
        OnceLog.Warning("coordinated-ops:commit", "[CoordinatedOps] commit failed: " + ex.Message);
    }
}

private static void CommitUnit(
    int allianceId,
    int aifactionIndex,
    IList ownUnits,
    IList offensive,
    int stableUnitId,
    Vector3 target,
    string targetName,
    WlStrategicIntent intent,
    string sourceSystem,
    string packageSignature)
{
    var unit = FindUnitById(ownUnits, stableUnitId);
    if (unit == null) return;
    if (!OffensiveAvailabilityWrapper.IsAvailable(aifactionIndex, unit, target))
    {
        Plugin.Log.LogInfo($"[CoordinatedOps] alliance={allianceId} unit={SafeName(unit)} action=skip reason=availability package={packageSignature}");
        return;
    }

    var decision = WlStrategicOrderBridge.TryIssue(new WlStrategicOrderRequest
    {
        AllianceId = allianceId,
        AifactionIndex = aifactionIndex,
        Unit = unit,
        TargetPosition = target,
        TargetName = string.IsNullOrEmpty(targetName) ? "Objective" : targetName,
        ObjectiveId = -1,
        Intent = intent,
        Width = 20f,
        Depth = 20f,
        SourceSystem = sourceSystem
    });

    if (decision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
    {
        Plugin.Log.LogInfo($"[CoordinatedOps] alliance={allianceId} unit={SafeName(unit)} action=wl-current-order type={decision.WlOrderType} package={packageSignature}");
        return;
    }
    if (!decision.MayDirectMove)
    {
        Plugin.Log.LogInfo($"[CoordinatedOps] alliance={allianceId} unit={SafeName(unit)} action=skip wlResult={decision.Result} reason={decision.Reason} package={packageSignature}");
        return;
    }
    if (AICampaign.MoveUnitTo(unit, target, true) && !offensive.Contains(unit))
    {
        offensive.Add(unit);
        Plugin.Log.LogInfo($"[CoordinatedOps] alliance={allianceId} unit={SafeName(unit)} action=direct-move package={packageSignature}");
    }
}

internal static Regiment FindUnitById(IList ownUnits, int stableUnitId)
{
    if (ownUnits == null || stableUnitId == 0) return null;
    for (int i = 0; i < ownUnits.Count; i++)
    {
        var unit = ownUnits[i] as Regiment;
        if (unit == null) continue;
        if (((UnityEngine.Object)unit).GetInstanceID() == stableUnitId) return unit;
    }
    return null;
}

private static string SafeName(UnityEngine.Object obj)
{
    try { return obj != null ? obj.name : "<unknown>"; }
    catch { return "<unknown>"; }
}
```

- [ ] **Step 5: Run build**

Run:

```bash
./build.sh
```

Expected: build succeeds with `ObjectiveAdapter.ResolveObjectiveName(int)` available to `CoordinatedOperationRuntime`.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs src/WhiskeyRealism/Strategic/ObjectiveAdapter.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat: add coordinated operation runtime adapter"
```

---

### Task 5: Operational Probe Package Integration

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs`
- Modify: `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`
- Modify: `src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing tests**

Add registrations:

```csharp
("operational probe escalates with support package", OperationalProbeEscalatesWithSupportPackage),
("operational probe support overlay blocks donor", OperationalProbeSupportOverlayBlocksDonor),
```

Add tests:

```csharp
private static void OperationalProbeEscalatesWithSupportPackage()
{
    var input = BuildProbeInput();
    input.DaySerial = 104;
    input.Previous = new OperationalProbeState
    {
        ProbeId = "1:VirginiaCapitalCorridor:probe-corps",
        UnitKey = "probe-corps",
        TargetAreaKey = "VirginiaCapitalCorridor",
        StartedDaySerial = 100,
        LastObservedEnemyStrength = 7000f,
        LastObservedFriendlyStrength = 7000f
    };
    input.CurrentEnemyStrength = 10000f;
    input.CurrentFriendlyStrength = 9000f;
    input.PackageOptions = CoordinatedOperationOptions.StableDefaults(10000f);

    var support = Snapshot("support-corps", 1, 15, 7000f, 2000f, FormationLevel.Division, FrontPosture.Counterstroke);
    support.StableUnitId = 222;
    support.AreaKey = "VirginiaCapitalCorridor";
    support.SectorKey = "VirginiaCapitalCorridor";
    input.FormationDirectives = FormationDirectiveLedger.Build(new[]
    {
        ProbeSnapshot("probe-corps", 1, 15, 9000f, 4000f, FormationLevel.Division, FrontPosture.Counterstroke, "VirginiaCapitalCorridor"),
        support
    }, EraStage.Operational1862, "VirginiaCapitalCorridor");

    var output = OperationalProbeLedger.Build(input);

    AssertEqual(OperationalProbeDecision.Escalate, output.Decision);
    AssertTrue(output.Package != null, "package output should be set");
    AssertEqual(CoordinatedOperationDecision.CoordinateAttack, output.Package.Decision);
}

private static void OperationalProbeSupportOverlayBlocksDonor()
{
    var input = BuildProbeInput();
    var output = OperationalProbeLedger.Build(input);
    output.Package = new CoordinatedOperationOutput
    {
        Decision = CoordinatedOperationDecision.Reinforce,
        LeadStableUnitId = 0,
        LeadDisplayUnitKey = "probe-corps",
        Reason = "reinforce-ratio-passed"
    };
    output.Package.SupportDisplayUnitKeys.Add("support-corps");

    var support = Snapshot("support-corps", 1, 15, 7000f, 2000f, FormationLevel.Division, FrontPosture.Counterstroke);
    input.FormationDirectives = FormationDirectiveLedger.Build(new[]
    {
        ProbeSnapshot("probe-corps", 1, 15, 9000f, 4000f, FormationLevel.Division, FrontPosture.Counterstroke, "VirginiaCapitalCorridor"),
        support
    }, EraStage.Operational1862, "VirginiaCapitalCorridor");

    bool changed = input.FormationDirectives.ApplyOperationalProbe(output);
    var assignment = input.FormationDirectives.GetAssignment("support-corps");

    AssertEqual(true, changed);
    AssertEqual(false, assignment.TransferDonorAllowed);
    AssertEqual("probe-support:reinforce-ratio-passed", assignment.Reason);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure for missing `PackageOptions` / `Package`.

- [ ] **Step 3: Extend probe DTOs**

In `OperationalProbeInput`, add:

```csharp
public CoordinatedOperationOptions PackageOptions;
```

In `OperationalProbeOutput`, add:

```csharp
public CoordinatedOperationOutput Package;
```

Update `Signature()` to include `Package?.Signature()`:

```csharp
"|" + (Package?.Signature() ?? "-");
```

- [ ] **Step 4: Build package candidates inside `OperationalProbeLedger`**

Add private method:

```csharp
private static CoordinatedOperationOutput BuildPackage(OperationalProbeInput input, OperationalProbeOutput probe, CoordinatedOperationIntent intent)
{
    var packageInput = new CoordinatedOperationInput
    {
        AllianceId = input.AllianceId,
        IsPlayerCic = false,
        Intent = intent,
        TargetName = input.PlanTargetAreaKey,
        TargetAreaKey = input.PlanTargetAreaKey,
        TargetSectorKey = probe.SourceSectorKey,
        TargetEnemyStrength = Math.Max(0f, CurrentEnemy(input, null)),
        PreferredLeadStableUnitId = StableIdFor(input.FormationDirectives, probe.SelectedUnitKey),
        Options = input.PackageOptions ?? CoordinatedOperationOptions.StableDefaults(Math.Max(0f, CurrentEnemy(input, null)))
    };

    if (input.FormationDirectives?.Assignments != null)
    {
        foreach (var a in input.FormationDirectives.Assignments)
            packageInput.Candidates.Add(CandidateFromAssignment(a));
    }
    return CoordinatedOperationPackageLedger.Build(packageInput);
}

private static int StableIdFor(FormationDirectiveLedger ledger, string unitKey)
{
    return ledger?.GetAssignment(unitKey)?.StableUnitId ?? 0;
}

private static CoordinatedOperationCandidate CandidateFromAssignment(FormationDirectiveAssignment a)
{
    return new CoordinatedOperationCandidate
    {
        StableUnitId = a.StableUnitId,
        DisplayUnitKey = a.UnitKey,
        AllianceId = a.AllianceId,
        Level = a.Level,
        Directive = a.Directive,
        AreaKey = a.AreaKey,
        SectorKey = a.SectorKey,
        X = a.X,
        Z = a.Z,
        CombatAvailability = a.CombatAvailability,
        ExchangePressure = a.ExchangePressure,
        LocalFriendlySupport = a.LocalFriendlySupport,
        LocalEnemyStrength = a.LocalEnemyStrength,
        Readiness = a.Readiness,
        Morale = a.Morale,
        Ammo = a.Ammo,
        Supply = a.Supply,
        Fatigue = a.Fatigue,
        OffensiveAllowed = a.OffensiveAllowed || a.Directive == FormationDirective.Probe || a.Directive == FormationDirective.Counterstroke,
        DefensiveAllowed = a.DefensiveAllowed,
        TransferDonorAllowed = a.TransferDonorAllowed,
        DirectMovementAllowed = a.DirectMovementAllowed,
        InheritsFromParent = a.InheritsFromParent,
        CriticalSector = false,
        FrontPosture = FrontPosture.Counterstroke,
        CommitMode = CoordinatedCommitMode.DirectMovement
    };
}
```

Call it when probe output is `Probe` or `Escalate`:

```csharp
output.Package = BuildPackage(input, output,
    output.Decision == OperationalProbeDecision.Escalate
        ? CoordinatedOperationIntent.Attack
        : CoordinatedOperationIntent.Probe);
```

- [ ] **Step 5: Update formation overlay**

In `FormationDirectiveLedger.ApplyOperationalProbe(...)`, after updating the selected lead, loop package supports:

```csharp
if (probe.Package != null)
{
    foreach (var supportKey in probe.Package.SupportDisplayUnitKeys)
    {
        var support = GetAssignment(supportKey);
        if (support == null) continue;
        support.Directive = probe.Package.Decision == CoordinatedOperationDecision.Reinforce
            ? FormationDirective.Reinforce
            : FormationDirective.Counterstroke;
        support.Reason = "probe-support:" + (probe.Package.Reason ?? probe.Reason ?? "package");
        support.OffensiveAllowed = probe.Package.Decision == CoordinatedOperationDecision.CoordinateAttack;
        support.DefensiveAllowed = true;
        support.TransferDonorAllowed = false;
    }
}
```

- [ ] **Step 6: Wire director options once**

In `StrategicCoordinator.UpdateOperationalProbe(...)`, after the existing `StrategicResilienceDirector.ApplyTo(input.Options, posture)` call, set:

```csharp
input.PackageOptions = CoordinatedOperationOptions.FromDirector(
    Math.Max(1f, input.CurrentEnemyStrength),
    posture);
```

Do not pass raw posture into the selector elsewhere.

- [ ] **Step 7: Commit packages from `OperationalProbeRuntime.Run(...)`**

Before the old single-unit direct move block, add:

```csharp
if (output.Package != null &&
    output.Package.Decision != CoordinatedOperationDecision.None &&
    output.Package.Decision != CoordinatedOperationDecision.Delay &&
    output.Package.Decision != CoordinatedOperationDecision.Recover)
{
    CoordinatedOperationRuntime.CommitPackage(
        allianceId,
        aifactionIndex,
        output.Package,
        target.Value,
        string.IsNullOrEmpty(output.TargetAreaKey) ? "Objective" : output.TargetAreaKey,
        output.Decision == OperationalProbeDecision.Escalate ? WlStrategicIntent.Offensive : WlStrategicIntent.Probe,
        "OperationalProbe");
    Plugin.Log.LogInfo(
        $"[CoordinatedOps] alliance={allianceId} intent=Probe decision={output.Package.Decision} " +
        $"target={output.Package.TargetName ?? output.TargetAreaKey} ratio={output.Package.Ratio:0.00} " +
        $"lead={output.Package.LeadDisplayUnitKey} support={output.Package.SupportStableUnitIds.Count} reason={output.Package.Reason}");
    return;
}
```

- [ ] **Step 8: Run tests and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: tests PASS and build succeeds.

- [ ] **Step 9: Commit**

```bash
git add src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs src/WhiskeyRealism/Strategic/StrategicCoordinator.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: route operational probes through coordinated packages"
```

---

### Task 6: Live Candidate Adapter For Vanilla Operation Lists And W&L Commit Mode

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs`
- Modify: `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`
- Modify: `src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add pure conversion helper tests**

Add registration:

```csharp
("coordinated ops bridge decision maps blocked commit mode", CoordinatedOpsBridgeDecisionMapsBlockedCommitMode),
```

Add test:

```csharp
private static void CoordinatedOpsBridgeDecisionMapsBlockedCommitMode()
{
    var blocked = new WlStrategicOrderDecision(
        WlStrategicOrderResult.WlCurrentOrderIneligible,
        16,
        mayDirectMove: false,
        mayMutateOperationList: false,
        reason: "chain");
    var issued = new WlStrategicOrderDecision(
        WlStrategicOrderResult.IssuedWlCurrentOrder,
        16,
        mayDirectMove: false,
        mayMutateOperationList: false,
        reason: "issued");
    var direct = new WlStrategicOrderDecision(
        WlStrategicOrderResult.DirectMovementAllowed,
        16,
        mayDirectMove: true,
        mayMutateOperationList: true,
        reason: "direct");

    AssertEqual(CoordinatedCommitMode.BlockedWlPlayerChain, CoordinatedOperationRuntime.CommitModeFromBridge(blocked));
    AssertEqual(CoordinatedCommitMode.WlCurrentOrder, CoordinatedOperationRuntime.CommitModeFromBridge(issued));
    AssertEqual(CoordinatedCommitMode.DirectMovement, CoordinatedOperationRuntime.CommitModeFromBridge(direct));
}
```

- [ ] **Step 2: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS because Task 4 added `CommitModeFromBridge(...)`.

- [ ] **Step 3: Add live candidate builder**

Add this non-issuing classifier to `WlStrategicOrderBridge.cs` after `TryIssue(...)`:

```csharp
internal static WlStrategicOrderDecision ClassifyOnly(WlStrategicOrderRequest request)
{
    if (request == null)
    {
        return new WlStrategicOrderDecision(
            WlStrategicOrderResult.DirectMovementAllowed,
            wlOrderType: -1,
            mayDirectMove: true,
            mayMutateOperationList: true,
            reason: "null-request");
    }
    return Classify(request.Intent, BuildFacts(request));
}
```

Add to `CoordinatedOperationRuntime`:

```csharp
internal static CoordinatedOperationCandidate CandidateFromAssignment(
    FormationDirectiveAssignment assignment,
    bool inOffensive,
    bool inDefensive,
    bool constructingSupplyDepot,
    CoordinatedCommitMode commitMode)
{
    if (assignment == null) return null;
    return new CoordinatedOperationCandidate
    {
        StableUnitId = assignment.StableUnitId,
        DisplayUnitKey = assignment.UnitKey,
        AllianceId = assignment.AllianceId,
        Level = assignment.Level,
        Directive = assignment.Directive,
        AreaKey = assignment.AreaKey,
        SectorKey = assignment.SectorKey,
        X = assignment.X,
        Z = assignment.Z,
        CombatAvailability = assignment.CombatAvailability,
        ExchangePressure = assignment.ExchangePressure,
        LocalFriendlySupport = assignment.LocalFriendlySupport,
        LocalEnemyStrength = assignment.LocalEnemyStrength,
        Readiness = assignment.Readiness,
        Morale = assignment.Morale,
        Ammo = assignment.Ammo,
        Supply = assignment.Supply,
        Fatigue = assignment.Fatigue,
        OffensiveAllowed = assignment.OffensiveAllowed || assignment.Directive == FormationDirective.Probe || assignment.Directive == FormationDirective.Counterstroke,
        DefensiveAllowed = assignment.DefensiveAllowed,
        TransferDonorAllowed = assignment.TransferDonorAllowed,
        DirectMovementAllowed = assignment.DirectMovementAllowed,
        InheritsFromParent = assignment.InheritsFromParent,
        InOffensiveOperation = inOffensive,
        InDefensiveOperation = inDefensive,
        ConstructingSupplyDepot = constructingSupplyDepot,
        CommitMode = commitMode
    };
}
```

Replace the private duplicate `CandidateFromAssignment(...)` in `OperationalProbeLedger` with a call to this helper, passing false/false/false and `DirectMovement` for pure tests. The live runtime adapter in Task 7 will pass real lists and W&L facts.

- [ ] **Step 4: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: centralize coordinated operation candidate adapter"
```

---

### Task 7: Patch #38 Coordinated Offensive Operations

**Files:**
- Create: `src/WhiskeyRealism/Patches/CoordinatedOffensiveOperationsPatch.cs`
- Modify: `src/WhiskeyRealism/WhiskeyRealism.csproj` only if the project uses explicit source includes; otherwise no csproj change.
- Modify: `docs/patch-catalog.md`

- [ ] **Step 1: Verify plugin project source include style**

Run:

```bash
rg -n "<Compile Include" src/WhiskeyRealism/WhiskeyRealism.csproj
```

Expected: no output. The plugin project uses SDK-style source globs, so the new patch file is included automatically.

- [ ] **Step 2: Create patch file**

Create `CoordinatedOffensiveOperationsPatch.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Patch #38 - coordinated offensive operation steering.
    // Vanilla CheckOffensiveMovements(int, Regiment, float) at decompile line 14166
    // is called per offensive unit from UpdateUnitAI line 11319. We cache the
    // package decision per faction/lead/signature and filter ownunits only during
    // the current vanilla call. Postfix restores ownunits exactly.
    [HarmonyPatch(typeof(AICampaign), "CheckOffensiveMovements")]
    internal static class CoordinatedOffensiveOperationsPatch
    {
        private sealed class Snapshot
        {
            internal int AifactionIndex;
            internal List<object> OwnUnits = new List<object>();
            internal string Signature;
            internal Stopwatch Watch;
        }

        private static readonly Dictionary<int, Snapshot> _snapshots = new Dictionary<int, Snapshot>();
        private static readonly Dictionary<string, HashSet<int>> _allowedBySignature = new Dictionary<string, HashSet<int>>();

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction, Regiment unit, float timediff)
        {
            OnceLog.Info("coordinated-ops:offensive:wired", "CoordinatedOffensiveOperationsPatch wired (#38)");
            try
            {
                if (unit == null || timediff <= 0f) return;
                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0 || allianceId > 1) return;
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, GameVars.playeralliance)) return;

                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;
                var ownUnits = AccessTools.Field(faction.GetType(), "ownunits")?.GetValue(faction) as IList;
                if (ownUnits == null || ownUnits.Count == 0) return;

                int leadId = ((UnityEngine.Object)unit).GetInstanceID();
                string signature = BuildSignature(allianceId, _aifaction, leadId, unit);
                if (!_allowedBySignature.TryGetValue(signature, out var allowed))
                {
                    allowed = BuildAllowedSet(allianceId, _aifaction, ownUnits, unit);
                    _allowedBySignature[signature] = allowed;
                }
                if (allowed.Count == 0) return;

                var snapshot = new Snapshot { AifactionIndex = _aifaction, Signature = signature, Watch = Stopwatch.StartNew() };
                for (int i = 0; i < ownUnits.Count; i++) snapshot.OwnUnits.Add(ownUnits[i]);
                _snapshots[_aifaction] = snapshot;

                for (int i = ownUnits.Count - 1; i >= 0; i--)
                {
                    var obj = ownUnits[i] as UnityEngine.Object;
                    if (obj == null) continue;
                    if (!allowed.Contains(obj.GetInstanceID()))
                        ownUnits.RemoveAt(i);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinated-ops:offensive:prefix", "[CoordinatedOps] offensive Prefix failed: " + ex.Message);
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            try
            {
                if (!_snapshots.TryGetValue(_aifaction, out var snapshot)) return;
                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction != null)
                {
                    var ownUnits = AccessTools.Field(faction.GetType(), "ownunits")?.GetValue(faction) as IList;
                    if (ownUnits != null)
                    {
                        ownUnits.Clear();
                        for (int i = 0; i < snapshot.OwnUnits.Count; i++) ownUnits.Add(snapshot.OwnUnits[i]);
                    }
                }
                snapshot.Watch?.Stop();
                if (snapshot.Watch != null && snapshot.Watch.ElapsedMilliseconds > 5)
                    Plugin.Log.LogInfo($"[CoordinatedOps:Perf] offensiveFilterMs={snapshot.Watch.ElapsedMilliseconds} sig={snapshot.Signature}");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinated-ops:offensive:postfix", "[CoordinatedOps] offensive Postfix failed: " + ex.Message);
            }
            finally
            {
                _snapshots.Remove(_aifaction);
            }
        }

        private static string BuildSignature(int allianceId, int aifactionIndex, int leadId, Regiment unit)
        {
            string formationSig = "-";
            var coordinator = StrategicCoordinator.Instance;
            if (coordinator?.FormationDirectives != null && allianceId < coordinator.FormationDirectives.Length)
                formationSig = coordinator.FormationDirectives[allianceId]?.Summary() ?? "-";
            return allianceId + "|" + aifactionIndex + "|" + leadId + "|" + unit.theaterposition + "|" + formationSig;
        }

        private static HashSet<int> BuildAllowedSet(int allianceId, int aifactionIndex, IList ownUnits, Regiment lead)
        {
            var allowed = new HashSet<int>();
            var coordinator = StrategicCoordinator.Instance;
            if (coordinator?.FormationDirectives == null || allianceId >= coordinator.FormationDirectives.Length)
            {
                allowed.Add(((UnityEngine.Object)lead).GetInstanceID());
                return allowed;
            }

            var ledger = coordinator.FormationDirectives[allianceId];
            if (ledger == null)
            {
                allowed.Add(((UnityEngine.Object)lead).GetInstanceID());
                return allowed;
            }

            var candidates = new List<CoordinatedOperationCandidate>();
            for (int i = 0; i < ledger.Assignments.Count; i++)
            {
                var assignment = ledger.Assignments[i];
                var unit = FindUnitById(ownUnits, assignment.StableUnitId);
                if (unit == null) continue;
                var mode = CoordinatedOperationRuntime.CommitModeFromBridge(
                    WlStrategicOrderBridge.ClassifyOnly(new WlStrategicOrderRequest
                    {
                        AllianceId = allianceId,
                        AifactionIndex = aifactionIndex,
                        Unit = unit,
                        TargetPosition = lead.transform.position,
                        TargetName = "Objective",
                        ObjectiveId = -1,
                        Intent = WlStrategicIntent.Offensive,
                        Width = 20f,
                        Depth = 20f,
                        SourceSystem = "CoordinatedOffensive"
                    }));
                candidates.Add(CoordinatedOperationRuntime.CandidateFromAssignment(
                    assignment,
                    ListContains(aifactionIndex, "unitsinoffensiveoperations", unit),
                    ListContains(aifactionIndex, "unitsindefensiveoperations", unit),
                    ListContains(aifactionIndex, "unitsconstructingsupplydepots", unit),
                    mode));
            }

            var leadAssignment = ledger.GetAssignment(UnitKey(lead));
            var input = new CoordinatedOperationInput
            {
                AllianceId = allianceId,
                IsPlayerCic = false,
                Intent = CoordinatedOperationIntent.Attack,
                TargetName = "Objective",
                TargetAreaKey = leadAssignment?.AreaKey,
                TargetSectorKey = leadAssignment?.SectorKey,
                TargetX = lead.transform.position.x,
                TargetZ = lead.transform.position.z,
                TargetEnemyStrength = Math.Max(1f, leadAssignment?.LocalEnemyStrength ?? lead.groupstrengthactive),
                PreferredLeadStableUnitId = ((UnityEngine.Object)lead).GetInstanceID(),
                Options = CoordinatedOperationOptions.StableDefaults(Math.Max(1f, leadAssignment?.LocalEnemyStrength ?? lead.groupstrengthactive)),
                Candidates = candidates
            };
            var output = CoordinatedOperationPackageLedger.Build(input);
            if (output.Decision == CoordinatedOperationDecision.None ||
                output.Decision == CoordinatedOperationDecision.Delay ||
                output.Decision == CoordinatedOperationDecision.Recover)
                return allowed;

            allowed.Add(output.LeadStableUnitId);
            foreach (var id in output.SupportStableUnitIds) allowed.Add(id);
            Plugin.Log.LogInfo($"[CoordinatedOps] alliance={allianceId} intent=VanillaOffensive decision={output.Decision} target={output.TargetName ?? input.TargetAreaKey ?? \"Objective\"} ratio={output.Ratio:0.00} lead={output.LeadDisplayUnitKey} support={output.SupportStableUnitIds.Count} reason={output.Reason}");
            return allowed;
        }

        private static Regiment FindUnitById(IList ownUnits, int stableUnitId)
        {
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i] as Regiment;
                if (unit == null) continue;
                if (((UnityEngine.Object)unit).GetInstanceID() == stableUnitId) return unit;
            }
            return null;
        }

        private static bool ListContains(int aifactionIndex, string fieldName, Regiment unit)
        {
            var faction = AICampaignReflect.GetFaction(aifactionIndex);
            if (faction == null) return false;
            var list = AccessTools.Field(faction.GetType(), fieldName)?.GetValue(faction) as IList;
            return list != null && list.Contains(unit);
        }

        private static string UnitKey(Regiment unit)
        {
            return SafeName(unit) + ":" + ReadInt(unit, "commander");
        }

        private static string SafeName(UnityEngine.Object obj)
        {
            try { return obj != null ? obj.name : "<unknown>"; }
            catch { return "<unknown>"; }
        }

        private static int ReadInt(object target, string field)
        {
            try
            {
                var f = AccessTools.Field(target.GetType(), field);
                if (f != null) return Convert.ToInt32(f.GetValue(target));
            }
            catch { }
            return -1;
        }
    }
}
```

- [ ] **Step 3: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds. Fix namespace/accessibility issues by moving helper methods from private to internal where needed. Do not add a Transpiler.

- [ ] **Step 4: Update patch catalog**

In `docs/patch-catalog.md`, add row #38 to the shipped table after #37:

```markdown
| 38 | `CoordinatedOffensiveOperationsPatch` | Prefix/Postfix | `Patches/CoordinatedOffensiveOperationsPatch.cs` | `AICampaign.CheckOffensiveMovements` (14166) | Coordinated offensive package steering. Caches package decisions per offensive lead/cycle, filters `aifaction[i].ownunits` only for the current vanilla call, restores the list in Postfix, and preserves vanilla W&L/offensive operation-list semantics. Logs `[CoordinatedOps]` package decisions and `[CoordinatedOps:Perf]` slow filter samples. |
```

Replace the pending #38 reservation line with:

```markdown
Next unreserved patch ordinal is #39. Slice B behavior patches remain unnumbered until implemented; #35 is observer-only and default-off.
```

- [ ] **Step 5: Run tests and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: tests PASS and build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Patches/CoordinatedOffensiveOperationsPatch.cs docs/patch-catalog.md
git commit -m "feat: steer vanilla offensive packages"
```

---

### Task 8: Target Name Resolver And W&L Text Guard

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs`
- Modify: `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add tests**

Add registrations:

```csharp
("coordinated ops nearest map name resolves target", CoordinatedOpsNearestMapNameResolvesTarget),
("coordinated ops target name falls back to area key", CoordinatedOpsTargetNameFallsBackToAreaKey),
```

Add tests:

```csharp
private static void CoordinatedOpsNearestMapNameResolvesTarget()
{
    var map = CampaignMapLedger.Build(new[]
    {
        new CampaignMapTown { CityName = "Richmond", X = 100f, Z = 100f },
        new CampaignMapTown { CityName = "Manassas", X = 10f, Z = 0f }
    });

    string name = CoordinatedOperationRuntime.NearestMapName(map, new UnityEngine.Vector3(11f, 0f, 0f));

    AssertEqual("Manassas", name);
}

private static void CoordinatedOpsTargetNameFallsBackToAreaKey()
{
    string name = CoordinatedOperationRuntime.ResolveTargetName(-1, "VirginiaCapitalCorridor", null, new UnityEngine.Vector3(11f, 0f, 0f));

    AssertEqual("VirginiaCapitalCorridor", name);
}
```

- [ ] **Step 2: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS after Task 4 added the resolver. Keep resolver methods `internal` so the linked test harness can call them.

- [ ] **Step 3: Use resolver in probe runtime**

In `OperationalProbeRuntime.Run(...)`, before committing the package, derive:

```csharp
string targetName = CoordinatedOperationRuntime.ResolveTargetName(
    output.ObjectiveId,
    output.TargetAreaKey,
    StrategicCoordinator.Instance?.CampaignMap,
    target.Value);
```

Add `ObjectiveId` to both `OperationalProbeInput` and `OperationalProbeOutput`:

```csharp
public int ObjectiveId = -1;
```

Set `input.ObjectiveId = objectiveId;` in `OperationalProbeRuntime.BuildInput(...)`, and copy `output.ObjectiveId = input.ObjectiveId;` at the start of `OperationalProbeLedger.Build(...)`.

Use `targetName` for `CommitPackage(...)` and W&L requests.

- [ ] **Step 4: Run tests and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: PASS and build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: resolve coordinated operation target names"
```

---

### Task 9: Micro-Movement Guard For Package-Locked Units

**Files:**
- Create: `src/WhiskeyRealism/Patches/CoordinatedOffensiveMicroMovementPatch.cs`
- Modify: `src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs`
- Modify: `docs/patch-catalog.md`

- [ ] **Step 1: Add in-memory package lock**

In `CoordinatedOperationRuntime`, add:

```csharp
private static readonly Dictionary<int, string> _packageLockByUnitId = new Dictionary<int, string>();

internal static void MarkPackageLocked(int stableUnitId, string packageSignature)
{
    if (stableUnitId == 0 || string.IsNullOrEmpty(packageSignature)) return;
    _packageLockByUnitId[stableUnitId] = packageSignature;
}

internal static bool IsPackageLocked(Regiment unit)
{
    if (unit == null) return false;
    int id = ((UnityEngine.Object)unit).GetInstanceID();
    return _packageLockByUnitId.ContainsKey(id) && unit.regimentpaths > 0;
}

internal static void ClearPackageLock(Regiment unit)
{
    if (unit == null) return;
    _packageLockByUnitId.Remove(((UnityEngine.Object)unit).GetInstanceID());
}
```

In `CommitUnit(...)`, after successful direct move or W&L current order, call:

```csharp
MarkPackageLocked(stableUnitId, packageSignature);
```

- [ ] **Step 2: Create Prefix/Postfix filter patch**

Create `CoordinatedOffensiveMicroMovementPatch.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    [HarmonyPatch(typeof(AICampaign), "UpdateMicroMovementInOffensive")]
    internal static class CoordinatedOffensiveMicroMovementPatch
    {
        private static readonly Dictionary<int, List<object>> _snapshotByFaction = new Dictionary<int, List<object>>();

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction)
        {
            try
            {
                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;
                var offensive = AccessTools.Field(faction.GetType(), "unitsinoffensiveoperations")?.GetValue(faction) as IList;
                if (offensive == null || offensive.Count == 0) return;
                var snapshot = new List<object>(offensive.Count);
                for (int i = 0; i < offensive.Count; i++) snapshot.Add(offensive[i]);
                int removed = 0;
                for (int i = offensive.Count - 1; i >= 0; i--)
                {
                    var unit = offensive[i] as Regiment;
                    if (unit == null) continue;
                    if (CoordinatedOperationRuntime.IsPackageLocked(unit))
                    {
                        offensive.RemoveAt(i);
                        removed++;
                    }
                    else
                    {
                        CoordinatedOperationRuntime.ClearPackageLock(unit);
                    }
                }
                if (removed > 0) _snapshotByFaction[_aifaction] = snapshot;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinated-ops:micro:prefix", "[CoordinatedOps] micro Prefix failed: " + ex.Message);
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            try
            {
                if (!_snapshotByFaction.TryGetValue(_aifaction, out var snapshot)) return;
                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;
                var offensive = AccessTools.Field(faction.GetType(), "unitsinoffensiveoperations")?.GetValue(faction) as IList;
                if (offensive == null) return;
                offensive.Clear();
                for (int i = 0; i < snapshot.Count; i++) offensive.Add(snapshot[i]);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinated-ops:micro:postfix", "[CoordinatedOps] micro Postfix failed: " + ex.Message);
            }
            finally
            {
                _snapshotByFaction.Remove(_aifaction);
            }
        }
    }
}
```

- [ ] **Step 3: Update patch catalog**

Add this text to #38's description rather than creating a new ordinal:

```markdown
Includes `CoordinatedOffensiveMicroMovementPatch` on `AICampaign.UpdateMicroMovementInOffensive` (13968) to temporarily filter package-locked units with active paths so vanilla continuation logic cannot retarget them before the initial package move is consumed.
```

- [ ] **Step 4: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs src/WhiskeyRealism/Patches/CoordinatedOffensiveMicroMovementPatch.cs docs/patch-catalog.md
git commit -m "feat: guard coordinated offensive micro movement"
```

---

### Task 10: Full Verification, Deploy, And Smoke Checklist

**Files:**
- Modify: `docs/handoff.md`
- Modify: `docs/superpowers/plans/archive/2026-05-06-coordinated-operation-packages.md` only to check completed boxes if this repo tracks plan progress.

- [ ] **Step 1: Run console harness**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests PASS. Record total PASS count in `docs/handoff.md`.

- [ ] **Step 2: Build DLL**

Run:

```bash
./build.sh
```

Expected: `dist/WhiskeyRealism.dll` created with 0 errors.

- [ ] **Step 3: Deploy DLL**

Close the game first, then run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

Expected: command exits 0. If Windows reports `Invalid argument`, the game still has the DLL loaded; close it and rerun.

- [ ] **Step 4: Verify deployed hash**

Run:

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: size and SHA-256 match exactly. Do not claim ready without this.

- [ ] **Step 5: Runtime smoke**

Start a W&L career and tail:

```bash
tail -n 240 "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected evidence:

```text
CoordinatedOffensiveOperationsPatch wired (#38)
[CoordinatedOps] alliance=... intent=Probe decision=...
[CoordinatedOps] alliance=... intent=VanillaOffensive decision=...
```

Check that logs distinguish:

- `decision=CoordinateAttack` when more than one committable unit is selected;
- `decision=Reinforce` when support moves without attack ratio;
- `decision=SingleLead` when W&L chain limits leave only one committable unit;
- `action=wl-current-order` without direct movement fallback for W&L player-chain units;
- no `"to none"` dispatch text in fresh generated messages.

- [ ] **Step 6: Scan for exceptions**

Run:

```bash
rg -n "Exception|Harmony|CoordinatedOps|W&LDispatch|to none" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected: no repeated exceptions, no Harmony patch failure, and no fresh `"to none"` evidence.

- [ ] **Step 7: Update handoff**

Add to `docs/handoff.md`:

```markdown
**2026-05-06 — coordinated operation packages shipped.** Adds pure `CoordinatedOperationPackageLedger`, package-aware operational probes, #38 `CoordinatedOffensiveOperationsPatch` on `AICampaign.CheckOffensiveMovements`, and package micro-movement guard. W&L player-chain units route through `WlStrategicOrderBridge`; blocked W&L supports are suppressed before package ratio acceptance and logged as `SingleLead` instead of fake coordinated attacks. Record the exact console harness PASS count from Step 1, the exact matching deploy SHA-256 from Step 4, and the observed `[CoordinatedOps]` smoke result from Steps 5-6 in this paragraph.
```

- [ ] **Step 8: Commit docs closeout**

```bash
git add docs/handoff.md docs/patch-catalog.md docs/superpowers/plans/archive/2026-05-06-coordinated-operation-packages.md
git commit -m "docs: close out coordinated operation packages"
```

---

## Plan Self-Review Checklist

- Spec coverage:
  - Pure selector and DTOs: Task 3.
  - W&L reinforce intent: Task 1.
  - Stable id/X/Z: Task 2.
  - Director single-application path and donor caps: Task 3 + Task 5.
  - Operation-list exclusions: Task 3 + Task 6.
  - Probe consumer: Task 5.
  - Vanilla offensive consumer #38: Task 7.
  - Micro-movement interaction guard: Task 9.
  - W&L target names and sanitizer smoke: Task 8 + Task 10.
  - Tests/csproj/build/deploy/hash/docs: Tasks 3, 4, 10.
- Diagnostic-only behavior is not accepted: Task 7 implements a behavior patch.
- W&L multi-order fan-out is not faked: Task 3 suppresses blocked supports; Task 10 smoke checks `SingleLead`.
- No new persistent sidecar state: Task 9 lock is in-memory only.
