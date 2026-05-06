# Vanilla AI Economy, Policy, And Construction Bugs

This queue holds the economy/policy/building issues found during the 2026-05-05 investigation. It is not a design spec. Each item still needs a bounded plan before code unless marked `In progress` or `Shipped`.

## Findings

| ID | Status | Failure mode | Vanilla / patch anchor | Evidence | Narrow fix direction |
|---|---|---|---|---|---|
| `BUG-ECO-001` | In progress | Subsidy focus sentinel can leak into live subsidy value. | Vanilla financial AI is `AICampaign.UpdateFinancialAI(int alliance)` at decompile line 15352. Whiskey #18 reads `AIPersonality.subsidyfocus` in `FinancialAIPatch`. | Live `BepInEx/LogOutput.log` showed `subsidyLane=3 old=0.20 new=-1.00` and then `old=-0.95 new=-1.00` for CSA. | Clamp subsidy focus caps: negative focus means disabled lane cap `0`, never a valid subsidy value. Repair already-negative lanes before normal movement. |
| `BUG-ECO-002` | Needs repro | Policy selection can crash if AI personality is unavailable. | `Policies.CheckAIPolicyChange(int alliance)` starts at 211015 and dereferences `aIPersonality.id` at 211024 without a null check. | Code-level hazard only in current investigation; no fresh runtime stack was captured. | If startup/runtime logs show this path firing before personality init, add a narrow Prefix false/true guard that preserves vanilla once personality exists. |
| `BUG-ECO-003` | Needs repro | Economy alliance update emits pre-existing vanilla NRE noise. | `Economy.UpdateEconomyAllianceData(float timediff, bool initialization)` starts at 32344. | Existing handoff notes say Player.log NRE histogram showed only pre-existing vanilla `Economy.UpdateEconomyAllianceData` noise after defense smoke; current Player.log path was not reacquired in this pass. | Reacquire the exact stack and failing field. Do not patch the whole method from a method-name-only log. |
| `BUG-ECO-004` | Backlog | Supply depot AI is outside current construction steering and can seize field units or place depots directly. | `AICampaign.CheckSupplyDepotConstruction(int _aifaction)` starts at 14659; low-supply unit move at 14767; direct `CBuilding.AddConstructionWish(CBuilding.id_supplydepot, ...)` at 14772; construction queue owner `CBuilding.AddConstructionWish` starts at 97479. | Decompiled behavior confirmed; no current bad runtime sample captured. | Start with observer/telemetry. Any guard should respect supply state, `unitsconstructingsupplydepots`, W&L subordinate control, front budget, and vanilla construction queue side effects. |
| `BUG-ECO-005` | Backlog | Railroad starts are random per eligible line, not tied to front logistics, fiscal posture, or active corridors. | `AICampaign.UpdateRailroadConstruction(int alliance, float timediff)` starts at 16052 and loops all railroads at 16067-16072; `BattleUnits.Railroad.StartConstruction` starts at 77818 and only checks built/owned/permitted/check-only gates before start. | Decompiled behavior confirmed; #23 observes railroad starts but no steering patch currently filters them. | Consider a Prefix/filter or pre-start observer only after runtime telemetry shows wasteful starts. CSA rail doctrine cap already exists in `ConstructionIntentLedger`, but it does not currently steer vanilla starts. |
| `BUG-BLD-001` | Shipped | Fort construction could accumulate dense local/capital clusters over repeated completed orders. | `AICampaign.CheckFortConstruction(int _aifaction)` starts at 16347; #27 `FortConstructionGovernorPatch` owns the shipped guard. | Decompile confirmed vanilla had one active fort order per faction and spacing checks, but no durable area/capital saturation cap. | Already shipped. Tune #27 only from runtime `[Patch:FortGovernor]` evidence. |

## Not Verified

- Current Player.log path for `BUG-ECO-003`; the WSL paths checked during this pass did not exist.
- Whether `BUG-ECO-002` can happen in a clean current campaign; it may be unreachable if vanilla always initializes AI personality before policy cadence.
- Whether `BUG-ECO-004` and `BUG-ECO-005` produce bad decisions in the current W&L active map; both are confirmed unpatched vanilla surfaces, not yet confirmed runtime regressions.

## Immediate Follow-Up

1. Finish `BUG-ECO-001` as the first Bug Fixes item if the local fiscal guard remains in the working tree.
2. Add console tests proving negative subsidy focus and already-negative subsidies clamp to `0`.
3. Run `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`.
4. For DLL readiness, run `./build.sh`, deploy, and verify `sha256sum` against the BepInEx plugin DLL.
