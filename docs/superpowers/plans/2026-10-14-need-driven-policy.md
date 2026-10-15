# Need-Driven Policy Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Use superpowers:test-driven-development for every pure-logic change. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Both governments research the policy their war needs (credit, manpower, diplomacy, blockade, industry), with the faction's vanilla personality list as a strong preference, never triggering irreversible/scripted policies by accident. Spec: `docs/superpowers/specs/2026-10-14-need-driven-policy-design.md`.

**Architecture:** A pure `CampaignNeedBuilder` turns a daily per-alliance snapshot into a smoothed `NeedVector`; a pure `PolicyEffectCatalog` describes what each policy does; `PolicyEligibility` + `PolicyNeedScorer` + `PolicyPlanLedger` rank candidates with hysteresis on the daily strategic review; `PolicySelectionPatch` only consumes the cached ranking. `ProjectSelectionPatch` reads the same need vector. The old hand-tagged scorers are deleted.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4 + HarmonyX, Unity 2021.3 Mono; console harness `tests/WhiskeyRealism.Tests`.

**Branch:** `feat/need-driven-policy`, cut from `feat/orchestrator-worker-thread` **after WS C1 and C2a are merged** (the daily-review hook and sidecar DTO live in C1-owned files). Worktree `.worktrees/feat-need-driven-policy` + `ln -s ../../refs refs`.

## Global Constraints

- Vanilla first: read the vanilla body before using it; cite anchors (decompile `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`, build 14703111) in headers and commit messages.
- Vanilla facts this plan is built on (read them yourself):
  - `Policies.CheckAIPolicyChange` 211017-211098: runs for alliances 0/1 only, skipped for the player alliance unless `automanagepolicies || ai_vs_ai`; emergency personalities (`GamePrefs.emergencyaipolicycredit/recruiting`) launch a newspaper event and cancel in-progress research not on the emergency list; researches one thing at a time (`GetActivatedPolicies(researchedonly: true) < 1` = nothing below progress 1); takes the first personality-list policy passing the filters.
  - `GetActivatedPolicies` 62803; `Policies.CanBeActivated` 209541 (non-acts limited by `maxpolicies`); `AddResearch` 209554 (no-op when `CanBeActivated` fails); `GetAllianceFromPolicy` 211392; grand policies 209259/209337/211298; `LaunchPolicyEffect` 209681-210955 (effects per id); `UpdatePolicyEffects` 209616; research speed 209455.
  - `Config/policies.dat` (game install): first line = record count (117), then fixed-length records; the harness must not read the game install — copy what you need into a fixture string.
- **Emergency handling is vanilla's (deviation from spec §Emergencies, decided by the integrator):** when `GetAIPersonality(alliance).id` is an emergency personality the Prefix returns `true` so vanilla fires its newspaper event, cancels research and picks from the emergency list. The need vector still forces Credit/Manpower to 1 so projects stay consistent.
- Patch hygiene (AGENTS.md): never throw from a patch; `TelemetryPerf.Scope` on every Prefix/Postfix/Finalizer; static OnceLog keys; alliance bounds (`aifaction` includes 2-4; this feature runs for 0/1 only); state written only on the daily strategic review, patches read.
- No new Prefix-skip beyond the existing `PolicySelectionPatch` Prefix; no Transpilers.
- Plugin.cs: there is no policy-specific flag today (`PolicySelectionPatch`/`ProjectSelectionPatch` only check `Enabled`). Add `EnableNeedDrivenPolicySelection` (`Config.Bind("Strategic", "Enable Need-Driven Policy Selection", true, …)`, honest description, default on) as the rollback flag for the policy Prefix. This is the only Plugin.cs edit in the slice.
- Tests in `tests/WhiskeyRealism.Tests/NeedDrivenPolicyTests.cs` (`internal static class NeedDrivenPolicyTests { public static void Run() }`, one registration line in `Program.cs`), every new pure file added to the test csproj as `<Compile Include>`; every deleted file removed from it.
- Tuning constants live as named `public const` in the owning class and are pinned by tests: `NeedVector.EmaDays = 14`, `PolicyNeedScorer.PersonalityWeight = 0.7f`, `ProfileWeight = 0.3f`, `LookAheadWeight = 0.5f`, `SlotPressureWeight = 0.4f`, `DurationWeight = 0.15f` (per research-year × max need), `IrreversiblePenalty = 1.5f`, `IrreversibleNeedGate = 0.7f`, `PolicyPlanLedger.SwitchMargin = 0.25f`, `EmergencyNeedThreshold = 0.85f`.
- Verify each task: `./build.sh` (0 errors, no new warnings) and `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` (all PASS). Commit per task: `feat(policy): … ` + anchors + `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. `git status` before committing.
- Last step of the slice: `python3 -I tools/orphan-sweep.py .` must list nothing in files this slice touched, and `--include-test-only` must list nothing this slice added.

## File map

| File | Kind | Responsibility |
|---|---|---|
| `src/WhiskeyRealism/Strategic/Policies/PolicyEffectCatalog.cs` | pure, create | id → `PolicyEffect` (effect weights over `NeedKind` + flags) |
| `src/WhiskeyRealism/Strategic/Policies/PolicyEligibility.cs` | pure, create | hard filters over `PolicyCandidateFacts` |
| `src/WhiskeyRealism/Strategic/Policies/PolicyNeedScorer.cs` | pure, create | score formula |
| `src/WhiskeyRealism/Strategic/Policies/PolicyPlanLedger.cs` | pure, create | ranking + hysteresis, per alliance |
| `src/WhiskeyRealism/Strategic/Policies/PolicyRuntime.cs` | runtime, create | reads vanilla policies into `PolicyCandidateFacts` |
| `src/WhiskeyRealism/Strategic/CampaignNeedBuilder.cs` | pure, create | `NeedSnapshot` → smoothed `NeedVector`; adapter to `ProjectDoctrineSignals` |
| `src/WhiskeyRealism/Strategic/CampaignNeedRuntime.cs` | runtime, create | reads vanilla alliance state into `NeedSnapshot` |
| `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs` | modify | daily: need vector + policy plan per alliance after fiscal intent |
| `src/WhiskeyRealism/Strategic/PersistenceDto.cs` | modify | persist need EMA + ledger current pick |
| `src/WhiskeyRealism/Patches/PolicySelectionPatch.cs` | rewrite | consume ledger |
| `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs` | modify | signals from need vector; delete `BuildSignals`/`ReadRecognitionProbability` |
| `src/WhiskeyRealism/Strategic/GrandStrategyPolicyScorer.cs` | delete | replaced by catalog + profile prior |
| `src/WhiskeyRealism/Strategic/Fiscal/FiscalPolicyScorer.cs` | modify | delete `PolicyWeight`, `PolicySelection`, `FiscalPolicySelector`; keep `ProjectWeight` |
| `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineSignalBuilder.cs`, `ProjectDoctrineSignalInput` (in `ProjectDoctrineModels.cs`) | delete | replaced by `CampaignNeedBuilder.ToProjectSignals` |
| `tests/…/Program.cs`, `CampaignEconomyFixTests.cs` | modify | delete tests of deleted code; port still-valid assertions |

---

### Task 1: Policy effect catalog

**Files:** create `Strategic/Policies/PolicyEffectCatalog.cs`; test `NeedDrivenPolicyTests.cs`.

**Interfaces — Produces:**
```csharp
namespace WhiskeyRealism.Strategic.Policies {
  public enum NeedKind { Credit, Treasury, Manpower, ContractStability, Morale, Support, Recognition, Blockade, TradeWar, Naval, Industry, Agriculture, Logistics, Supply, Command }
  [System.Flags] public enum PolicyFlags { None = 0, Irreversible = 1, SpawnsUnits = 2, StartsWar = 4, CostsSupport = 8, CostsWealth = 16, CostsMoneyPerRecruit = 32, CostsMorale = 64, CostsIntervention = 128, ContractReset = 256 }
  public sealed class PolicyEffect { public int PolicyId; public float[] Weights /* indexed by NeedKind, 0..1 */; public PolicyFlags Flags; public float SupportCost; public float MoraleCost; public float InterventionCost; }
  public static class PolicyEffectCatalog {
    public static PolicyEffect Get(int policyId);          // null when unknown
    public static System.Collections.Generic.IReadOnlyCollection<int> KnownIds { get; }
  }
}
```

- [ ] Read `Policies.LaunchPolicyEffect` 209681-210955 completely and `Config/campaignprefs.txt` 2641-3126; build a scratch table id → effect (keep it in your notes, not the repo).
- [ ] Copy every policy id from `Config/policies.dat` into a fixture constant in the test file (ids only; note how you parsed the 23-line records). Write failing tests: every non-grand id in the fixture has a catalog entry; every entry's weights are in [0,1]; ids 144 (war with Mexico), 134 (ends slavery), 42 (Lt Gen Act), 130 (burn cotton), 33/133/48/147 (unit spawns), 39/40/139/140 (contract reset) carry `Irreversible` (+ `StartsWar`/`SpawnsUnits`/`ContractReset` as applicable); Bank Acts carry `CostsSupport`; Print Notes carries `CostsWealth`; Bounties carry `CostsMoneyPerRecruit`; Union ids are 0-99, CSA 100-199.
- [ ] Implement the catalog as a static table (one line per id with a trailing `// name — LaunchPolicyEffect <line>` comment). Weights are relative effect strengths you derive from the vanilla magnitudes; where magnitudes come from GamePrefs, use their default file values.
- [ ] Run tests, pass, commit.

### Task 2: Eligibility (hard filters)

**Files:** create `Strategic/Policies/PolicyEligibility.cs`, `Strategic/Policies/PolicyRuntime.cs`.

**Interfaces — Produces:**
```csharp
public struct PolicyCandidateFacts {
  public int PolicyId; public int Alliance; public bool IsGrand; public bool IsAct; public bool ActiveOrDone;
  public int ReferedChapter; public int CurrentChapter; public bool HasPrePolicies; public bool Deactivated;
  public bool Blocked; public bool AvailableInScenario; public bool CanBeActivated; public bool InResearch;
  public float ResearchDuration; public int[] UnheldRequiredGrandActs; // prerequisite branches needing an unheld grand act
}
public static class PolicyEligibility {
  public static bool IsEligible(PolicyCandidateFacts f, int alliance, out string reason);
}
public static class PolicyRuntime { // runtime, not in test csproj
  public static System.Collections.Generic.List<PolicyCandidateFacts> ReadCandidates(int alliance);
  public static int[] ReadPersonalityList(int alliance, out bool emergency);
  public static int ReadUsedSlots(int alliance, out int maxSlots);
  public static bool HasResearchInProgress(int alliance);  // GetActivatedPolicies(researchedonly:true) > 0
}
```

- [ ] Failing tests: grand rejected; other alliance's id rejected (`GetAllianceFromPolicy` rule: <100 Union, 100-199 CSA); active/done rejected; chapter gate; prerequisites; deactivated; blocked; scenario; `!CanBeActivated` for non-acts rejected, acts allowed; unreachable branch (unheld grand act 75/175, 76/176, 74) rejected; Union 36 rejected while `CurrentChapter == 0`; alliance ≥ 2 rejected; `reason` names the first failing filter.
- [ ] Implement `IsEligible` (pure). Implement `PolicyRuntime` from vanilla fields used by `CheckAIPolicyChange` (211076) and `CanBeActivated`; `IsDeactivated` via cached reflection with try/catch + OnceLog warning (fail closed = ineligible). Derive the grand-act prerequisite map by reading how `HasPrePolicies` and the policy prerequisite fields work (find the field in the `Policy` class near 211210).
- [ ] Tests pass; build; commit.

### Task 3: Campaign need vector

**Files:** create `Strategic/CampaignNeedBuilder.cs`, `Strategic/CampaignNeedRuntime.cs`.

**Interfaces — Produces:**
```csharp
public sealed class NeedSnapshot {
  public int Alliance; public EraStage Era; public WhiskeyRealism.Strategic.Fiscal.FiscalPosture FiscalPosture;
  public float RecruitsAvailable, TroopsFielded, ContractsExpiringShare; public int RecruitableStates;
  public float NationalMorale, AverageSupport, InterventionProbability, OwnBlockadeRatio, EnemyBlockadeRatio;
  public float OwnAverageRifles, EnemyBestAverageRifles, OwnAverageGuns, EnemyBestAverageGuns, OwnTonnage, EnemyTonnage;
  public float IndustryGap, SupplyPressure, TransportPressure, StrengthRatio; public bool EmergencyCredit, EmergencyManpower;
  public int[] ScriptedObjectivePolicies; // e.g. Union policy 11 until done
}
public sealed class NeedVector { public const int EmaDays = 14; public float[] Values = new float[15]; /* by NeedKind */ public float this[Policies.NeedKind k] => Values[(int)k]; }
public static class CampaignNeedBuilder {
  public static NeedVector Raw(NeedSnapshot s);
  public static NeedVector Smooth(NeedVector previous, NeedVector raw, int daysElapsed); // EMA, alpha = 2/(EmaDays+1) per day
  public static WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals ToProjectSignals(NeedSnapshot s, NeedVector v);
}
public static class CampaignNeedRuntime { public static NeedSnapshot Read(int alliance, EraStage era, WhiskeyRealism.Strategic.Fiscal.FiscalOutput fiscal); }
```

- [ ] Failing tests: Credit = 1 at EmergencySolvency, 0.75 CreditDefense, 0.25 BalancedWar, 0 Expansion; Manpower rises as recruits/fielded falls and when recruitable states ≤ 4; emergency flags force Credit/Manpower to 1; Recognition is the intervention probability for CSA and `1 - p` pressure for the Union (Union wants to keep it low); Blockade need for CSA = own blockade ratio, for the Union = 1 − enemy blockade ratio (blockade still to do); Morale = 1 − national morale; Support = 1 − average support; all values in [0,1], NaN → 0; EMA converges and is reproducible across `daysElapsed` > 1; `ToProjectSignals` reproduces the existing `ProjectDoctrineSignals` semantics (port the assertions of the current `ProjectDoctrineSignalBuilder` tests in Program.cs ~15347-15433, then delete those tests).
- [ ] Implement the builder. Implement `CampaignNeedRuntime.Read` from vanilla alliance fields — for each field cite the anchor you read (recruit pools, `nationalmorale`, `averagesupport`, `averageblockaderatio` of **each** side — the current code reads `alliance[1]` for both, which is a bug, `GetInterventionProbability(2/3)` 62822, project levels 102/104 as today). Reuse `FiscalRuntime` pressures rather than re-reading them.
- [ ] Tests pass; build; commit.

### Task 4: Scorer and plan ledger

**Files:** create `Strategic/Policies/PolicyNeedScorer.cs`, `Strategic/Policies/PolicyPlanLedger.cs`.

**Interfaces — Produces:**
```csharp
public struct PolicyScore { public int PolicyId; public float Total, Need, Personality, Profile, LookAhead, SlotPressure, Duration, SideCost, Irreversible; }
public static class PolicyNeedScorer {
  public static PolicyScore Score(PolicyCandidateFacts f, NeedVector need, int[] personalityList, GrandStrategyProfile profile,
                                  int usedSlots, int maxSlots, System.Func<int, System.Collections.Generic.IEnumerable<int>> dependents);
}
public sealed class PolicyPlanLedger {
  public const float SwitchMargin = 0.25f; public const float EmergencyNeedThreshold = 0.85f;
  public int CurrentPick { get; }                       // -1 = none
  public System.Collections.Generic.IReadOnlyList<PolicyScore> Ranked { get; }
  public void Update(System.Collections.Generic.IReadOnlyList<PolicyScore> scored, NeedVector need, NeedVector previousNeed);
  public int NextEligible(System.Func<int, bool> stillEligible); // first ranked id still eligible, else -1
  public void Restore(int currentPick);                 // sidecar
}
```
Score (spec §Score): `Need = Σk need_k·w[p,k]`; `Personality = PersonalityWeight·(1 − rank/len)` when in the list; `Profile = ProfileWeight·Σk profile.WeightFor(tag(k))·w[p,k]` (map `NeedKind` → existing `StrategyTag`; document the map); `LookAhead = LookAheadWeight·max_d Need(d)/(1+depth)` over dependents; `SlotPressure = −SlotPressureWeight·(used/max)²` for non-acts; `Duration = −DurationWeight·ResearchDuration·max_k need_k`; `SideCost = −(SupportCost·Support need + MoraleCost·Morale need + InterventionCost·Recognition need (CSA only) + CostsMoneyPerRecruit·Credit need)`; `Irreversible = −IrreversiblePenalty` unless the max need over the policy's nonzero kinds ≥ `IrreversibleNeedGate`.

- [ ] Failing tests: credit crisis → War Bonds / Bank Act outrank Diplomacy; manpower crisis → Conscription/Enrollment outrank Diplomacy; near-tie → personality-list policy wins; irreversible 144/134 suppressed at low need, allowed above the gate; Bank Act penalised when support is low; slot pressure lowers non-acts only; prerequisite look-ahead lifts a cheap prerequisite of a high-need policy; ledger keeps the current pick unless a challenger beats it by ≥ `SwitchMargin`; ledger switches immediately when any need crosses `EmergencyNeedThreshold` upward; `NextEligible` skips ineligible ids; constants pinned.
- [ ] Implement; tests pass; commit.

### Task 5: Wire daily review, persistence and the policy patch; delete old scorers

**Files:** modify `StrategicCoordinator.cs`, `PersistenceDto.cs`, rewrite `Patches/PolicySelectionPatch.cs`; delete `GrandStrategyPolicyScorer.cs`; trim `FiscalPolicyScorer.cs`; tests.

**Interfaces — Consumes:** Tasks 1-4. **Produces:** `StrategicCoordinator.PolicyPlans` (`PolicyPlanLedger[2]`), `StrategicCoordinator.Needs` (`NeedVector[2]`).

- [ ] Read the C1-merged `StrategicCoordinator` daily review and `UpdateFiscalIntentCore` (~742). After fiscal intent per alliance 0/1 (skip when the player is CIC of that alliance — `StrategicCoordinator.IsPlayerCICOf`): `Needs[a] = Smooth(prev, Raw(CampaignNeedRuntime.Read(...)), days)`; score `PolicyRuntime.ReadCandidates(a)` filtered by `PolicyEligibility`; `PolicyPlans[a].Update(...)`. Wrap in `TelemetryPerf.Scope("campaign.policy-plan", …, 4.0)`.
- [ ] Persist `Needs` values and `PolicyPlans[a].CurrentPick` in the sidecar DTO (null-safe load; old sidecars load with empty state). Clear both in `StrategicCoordinator.ResetAll()`.
- [ ] Rewrite `PolicySelectionPatch.Prefix(int alliance)`: keep the existing guards (enabled, coordinator, alliance 0/1, frame > 50, player alliance gate `automanagepolicies || ai_vs_ai`, player-CIC); return `true` for emergency personalities and when `PolicyRuntime.HasResearchInProgress`; take `PolicyPlans[a].NextEligible(id => PolicyEligibility.IsEligible(PolicyRuntime facts for id …))`; if −1 return `true`; else `Policies.AddResearch(alliance, Policy.GetPolicyFromID(id))`, emit the top-3 breakdown on the existing `FiscalIntent` decision event (signature = alliance|pick|reason), return `false`. Header comment documents vanilla 211017-211098 and the new behaviour.
- [ ] Delete `GrandStrategyPolicyScorer.cs`, `FiscalPolicyScorer.PolicyWeight`, `PolicySelection`, `FiscalPolicySelector`, and their tests (Program.cs ~14546-14562, ~17065-17084; `CampaignEconomyFixTests` ~364-383). Port any assertion that still describes wanted behaviour into `NeedDrivenPolicyTests` against the new API.
- [ ] Add `EnableNeedDrivenPolicySelection` to `Plugin.cs` (see Global Constraints); the Prefix returns `true` when it is off.
- [ ] Build + harness; commit.

### Task 6: Projects read the shared need vector

**Files:** modify `Patches/ProjectSelectionPatch.cs`; delete `ProjectDoctrineSignalBuilder.cs` and `ProjectDoctrineSignalInput`.

- [ ] Replace `BuildSignals`/`ReadRecognitionProbability` with `CampaignNeedBuilder.ToProjectSignals(snapshot, StrategicCoordinator.Instance.Needs[alliance])` using a snapshot cached by the daily review (add `StrategicCoordinator.NeedSnapshots[2]`; the patch must not call the runtime reader per lane). This fixes the shared `alliance[1]` blockade read and fills ManpowerStress / CivilOrderRisk / AgricultureFoodStress / OffensiveTempo (document each source; if a source truly does not exist in vanilla, say so in the report and leave 0 with a comment citing what you searched).
- [ ] Delete the builder/input types and the csproj lines; build + harness; commit.

### Task 7: Docs, sweep, review hand-off

- [ ] Update `docs/patch-catalog.md` (PolicySelectionPatch row), `docs/handoff.md` (what shipped), `src/WhiskeyRealism/Strategic/AGENTS.md` (new Policies folder), and add a short "Policy selection" section to `docs/findings.md` with the vanilla anchors above.
- [ ] `python3 -I tools/orphan-sweep.py .` and `--include-test-only`: nothing in touched files / nothing this slice added.
- [ ] Final `./build.sh` + harness; paste counts in the report.

## Smoke expectations (after BepInEx is reinstalled; not part of this slice's gate)

`FiscalIntent` decision events with `source=PolicySelection` show need-driven picks for both alliances in 1861-62; no research stall (a pick every time research is free); emergency personalities still produce vanilla newspaper events; no `policy-selection:*` warnings.

## Rollback

`Enable Need-Driven Policy Selection = false` → vanilla `CheckAIPolicyChange` runs untouched. Project selection has no separate flag (unchanged by this slice; its signals simply become accurate).
