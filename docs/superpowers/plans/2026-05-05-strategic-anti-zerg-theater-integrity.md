# Strategic Anti-Zerg Theater Integrity Plan

Status: implemented, console/build/deploy/hash verified on 2026-05-05; runtime smoke pending after game restart.

## Goal

Stop strategic defense from turning the campaign map into a zerg pull where broad asset pressure near Richmond, Washington, or Annapolis strips multiple theaters. The strategic layer must preserve theater integrity: local threats use local forces first, donor theaters keep minimum holding force, cross-map movement requires a real national emergency, capital defense is capped, and released formations resume normal theater duty instead of remaining stranded.

## Root Cause

Runtime log evidence before this fix showed repeated lines like:

```text
[DefenseIntent] custom-order alliance=0 threat=asset:SeaHarbor:Annapolis Port:... unit=...
```

Source review confirmed the cause:

- `DefenseIntentRuntime.ExtractAssetProximityThreats()` emitted `AssetProximity` threats when enemies were near owned forts/ports/harbors.
- `DefenseIntentLedger.DerivePosture()` mapped every `AssetProximity` to `ActiveInvasion`.
- `CoastalDefenseCustomOrderRunner` treated `ActiveInvasion` as sufficient to call `AICampaign.MoveUnitTo(...)`.
- `CheckForDefensiveOperationsCandidateFilterPatch` only filtered the older suppression reasons, so vanilla could still re-pull some forbidden units.
- `ArmyAreaRuntime` return-area movement checked formation directives but not the front transfer budget before moving a unit across sectors.

Vanilla has partial theater controls (`UpdateCampaignTheaters`, `IsWithinOperationsTheater`, `CheckForDefensiveOperations`, `CheckTransferOfUnits`), but the Whiskey custom-order surface was bypassing the important strategic-budget intent.

## Shipped Implementation

Files:

- `src/WhiskeyRealism/Strategic/DefenseCustomOrderPolicy.cs`
- `src/WhiskeyRealism/Strategic/StrategicMovementBudget.cs`
- `src/WhiskeyRealism/Strategic/DefenseIntentTypes.cs`
- `src/WhiskeyRealism/Strategic/DefenseIntentInput.cs`
- `src/WhiskeyRealism/Strategic/DefenseIntentLedger.cs`
- `src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs`
- `src/WhiskeyRealism/Strategic/DefensePackageAggregator.cs`
- `src/WhiskeyRealism/Strategic/CoastalDefenseCustomOrderRunner.cs`
- `src/WhiskeyRealism/Strategic/ArmyAreaRuntime.cs`
- `src/WhiskeyRealism/Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs`
- `tests/WhiskeyRealism.Tests/Program.cs`
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- `docs/handoff.md`
- `docs/patch-catalog.md`
- `MEMORY.md`
- `README.md`
- `AGENTS.md`
- `docs/superpowers/README.md`

Behavior:

- `DefenseThreat.SourceKind` now carries the source kind through ledger output.
- `DefenseCustomOrderPolicy` allows custom defensive movement only for real `SeaInvasion` / `RaidForce` active responses.
- `AssetProximity` stays local-only and cannot custom-order movement.
- `StrategicMovementBudget` evaluates defense candidates against formation directives, donor flags, front-sector budget, national-emergency rules, and capital-defense caps.
- `DefensePackageAggregator` accepts an optional max effective-strength cap, used for capital-defense package limits.
- `DefenseIntentRuntime` attaches current `FrontSectorLedger` and `FormationDirectiveLedger` context to defense input and candidates.
- #25 candidate filtering now treats `asset-proximity-local-only`, `national-emergency-required`, `formation-directive`, `formation-donor`, `min-hold`, and `critical-sector-budget` as forbidden movement reasons.
- #15 return-area movement now asks `StrategicMovementBudget.EvaluateAreaMovement(...)` before moving a unit out of a sector.

## Acceptance Tests

New console-harness cases:

- `defense ledger asset proximity stays local and cannot custom order`
- `defense ledger donor theater budget blocks critical front export`
- `defense ledger formation directive blocks defense movement`
- `defense ledger capital defense package is capped`
- `strategic movement budget blocks area export from hold sector`

## Verification

Commands run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
git diff --check
```

Results:

- Console harness passed.
- Build passed with 0 warnings / 0 errors.
- `git diff --check` passed.
- Deployed DLL size matched: `326144` bytes.
- Deployed SHA-256 matched `dist`: `fb3bae96c36e51c59d52cf2c85dce4afe7d0b3c7291ee4908592f31bf712e826`.

## Runtime Smoke Pending

Current `LogOutput.log` mtime was `2026-05-05 13:59:34 -0500`, which predates the deploy. It still shows pre-fix asset custom-orders and must not be used to judge the new DLL.

After restarting GTCW, smoke with:

```bash
rg -n "DefenseIntent|Patch:ArmyArea|Exception|TargetInvocationException|Harmony|ERROR|WARN" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Pass criteria:

- No `[DefenseIntent] custom-order ... threat=asset:` lines.
- No repeated warnings/errors from defense intent, #25, or army-area movement.
- Normal `[Patch:DefensiveOps]` capital-defense behavior can still appear.
- If verbose defense logging is enabled, expected suppressions include `asset-proximity-local-only`, `min-hold`, `critical-sector-budget`, `formation-directive`, or `capital-defense-cap`.

## Rollback

If runtime smoke shows real sea invasions no longer get a response:

- keep `DefenseThreat.SourceKind`;
- keep `AssetProximity` custom-order block;
- inspect `StrategicMovementBudget.EvaluateDefenseCandidate(...)` first, especially national-emergency and formation-donor gates;
- do not revert to posture-only `ActiveInvasion` custom-order logic.
