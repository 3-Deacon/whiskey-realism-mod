# Vanilla Tick-System Bugs

This queue holds narrow bugs found during the 2026-05-05 vanilla campaign tick review. It is not a broad AI cadence spec.

## Findings

| ID | Status | Failure mode | Vanilla / patch anchor | Evidence | Narrow fix direction |
|---|---|---|---|---|---|
| `BUG-TICK-001` | In progress | Paused campaigns can still run one `UpdateUnitAI()` pass per `AICampaign.Update` frame after vanilla AI initializes. | Vanilla `AICampaign.Update` starts at decompile line 11159 and computes `Mathf.Max(1, FloorToInt(Pow(GameVars.gamespeed, 0.5f)))` at 11195. `GameVars.SetGameSpeed(0f)` sets `gamepaused=true` and `uniStormSystem.timeStopped=true` at 66368-66379, but `AICampaign.Update` does not check either flag. Whiskey #26 `CampaignAiUpdateGovernorPatch` owns the patch surface. | Code-level confirmed against vanilla decompile. Regression coverage added for paused/game-speed-zero skip behavior. Clean staged-tree tests/build passed; deployed DLL SHA-256 is `4e01274babb3af3def1143ab4acee6db8a31c27817d80c1789385b60e3c0f19f`. | Keep vanilla `InitializeAI()` behavior intact, then skip already-initialized `AICampaign.Update` when `GameVars.gamepaused` is true or `GameVars.gamespeed <= 0f`. Fresh-launch smoke still pending before marking shipped. |
| `BUG-TICK-002` | In progress | Campaign economy initialization can spin indefinitely if `Economy.UpdateFilterMaps(initialization:true)` keeps returning false after a stuck iterator or repeated exception path. | `BattleUnits.CampaignDataRuns` calls `while (!Economy.UpdateFilterMaps(initialization: true)) { }` at decompile line 79874. `Economy.UpdateFilterMaps` starts at 31852; #31 `EconomyFilterMapInitializationGuardPatch` owns the guard. | Current logs predate the latest #28/#31 deployed DLL but previously showed repeated `Economy.UpdateEconomyAllianceData` NREs through `Economy.UpdateFilterMaps`. | #31 forces completion only after repeated no-progress initialization returns or initialization-only exceptions; build/deploy/hash verified in DLL `7da618bf...`; fresh-launch smoke pending. |

## Not Verified

- Runtime smoke for `BUG-TICK-001`; verify the paused first-fire marker appears once and no repeated pause-log spam is emitted from deployed DLL `4e01274b...`.
- Whether `BUG-TICK-002` emits `[Patch:FilterMapInit]` after #28/#31 have been deployed and the game has been restarted.

## Immediate Follow-Up

1. Fresh-launch smoke deployed DLL `4e01274b...`, then pause a campaign tick.
2. Fresh-launch smoke #28/#31: confirm campaign startup does not hang and `[Patch:FilterMapInit]` appears only if vanilla makes no progress.
