# Strategic Operational Probe / Contact Plan

Status: implemented, console/build/deploy/hash verified on 2026-05-05; runtime smoke pending after game restart.

## Goal

Add the missing strategic operation cycle between objective choice and full commitment: send one bounded same-area probe, observe contact/reaction, then pause, withdraw, continue, or escalate without stripping other theaters.

## Vanilla Findings

Vanilla already mimics operational friction mechanically:

- `AICampaign.UpdateUnitAI()` spreads campaign AI across staged jobs instead of one instant decision.
- `CheckOffensiveMovements()` assigns units to `unitsinoffensiveoperations` only after local value, enemy strength, commander initiative, winter/aggression, timing, and readiness checks.
- `RollUpEnemyObjectivesInZone()` keeps offensive units moving locally and cancels weak/low-readiness operations.
- `UpdateCampaignTheaters()` uses `theaterposition` / `IsWithinOperationsTheater()` as a coarse operations box.
- `CheckTransferOfUnits()` moves strength from surplus to deficit positions with transfer caps.

The missing Whiskey layer was the doctrine loop that decides whether a limited contact should remain a probe, become a mass commitment, or pause after enemy reaction.

## Shipped Implementation

Files:

- `src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs`
- `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`
- `src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs`
- `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- `tests/WhiskeyRealism.Tests/Program.cs`
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

Behavior:

- Builds a pure `OperationalProbeLedger` from the active plan target, front ledger, formation directives, and prior probe state.
- Starts at most one same-area probe from an eligible non-hold, non-critical, direct-move formation.
- Refuses critical hold donors and oversized formations that would violate the local front budget.
- Pauses when enemy strength jumps enough to indicate reaction and local odds are not favorable.
- Escalates only after the minimum probe duration and favorable friendly/enemy ratio.
- Withdraws when the probe is overmatched.
- Applies a formation-directive overlay: `Probe`, `Delay`, `Recover`, or `Counterstroke`/`Mass`.
- Uses `OperationalProbeRuntime` from coordinator cadence to add/remove the selected formation from vanilla `unitsinoffensiveoperations` through vanilla `AICampaign.MoveUnitTo`.
- Does not touch tactical battle AI.

## Acceptance Tests

- `operational probe assigns one bounded same-area formation`
- `operational probe pauses on enemy reaction`
- `operational probe escalates after favorable contact`
- `operational probe refuses critical hold donor`
- `operational probe overlays formation directive`

## Verification

Commands run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
cp dist/WhiskeyRealism.dll "<GTCW>/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "<GTCW>/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "<GTCW>/BepInEx/plugins/WhiskeyRealism.dll"
```

Results:

- Console harness passed.
- Build passed with 0 warnings / 0 errors.
- Deployed DLL size matched: `333824` bytes.
- Deployed SHA-256 matched `dist`: `61dff1bdc0579672fec4744b92536d9650dcedc94be3e7ed1a133ba0fe23e04b`.

## Runtime Smoke Pending

After restarting GTCW, tail `LogOutput.log` and verify:

- `[OperationalProbe] alliance=... decision=Probe ...` appears only for one bounded formation per alliance/target.
- Enemy reaction produces `decision=Pause` instead of pulling armies from other theaters.
- Favorable contact can produce `decision=Escalate`.
- No repeated warnings/errors from `[OperationalProbe]`, `[FormationDirective]`, or `AICampaign.MoveUnitTo`.
- Anti-zerg pass criteria still hold: no `[DefenseIntent] custom-order ... threat=asset:` lines.
