# Campaign terrain, perk-pick and engineer bug fixes — plan and as-built

Branch `fix/campaign-terrain-bugs` (worktree `.worktrees/campaign-terrain-bugs`, base `main` 4ba3805). User direction 2026-10-15: "Okay, fix it all correctly" — fix in line with vanilla's evident intent; W&L is treated the way vanilla treats it. Evidence and anchors: [`../../bug-fixes/vanilla-campaign-terrain.md`](../../bug-fixes/vanilla-campaign-terrain.md).

## Items and verdicts

| Item | Verdict | Fix |
|---|---|---|
| 1 Scorer weights (Sappers river bonus) | Whiskey bug, BUG-AI-012 | `PerkSelectionScorer` role bonuses re-anchored on each perk's code effect (Sappers: no River / CapitalDefense; Pontoon: Recovery not Siege; Limelights to Siege and CapitalDefense; River Expedition's siege assault to Siege; BMI no River) |
| W-6 Engineers & Mechanics | Whiskey bug, BUG-AI-012: dead for AI construction (only the player's manual placement reads it, 96405-96414) | base weight 0, no role bonus |
| 2 / W-4 Pontoon Train speed | Vanilla bug, BUG-AI-013 | `PontoonTrainCreekPatch`: creek speed x1.25/1.5/1.75 (tooltip) and creek path cost x1/factor, so routes are chosen by real travel time |
| 3 Random pick never draws the last id | Vanilla bug on campaign, BUG-AI-011; battle pick is correct (5429) | #7 `PerkSelectionPatch`: full candidate window; full-range replay of vanilla's pick where Whiskey does not steer |
| 4 Ford pull-off | Real path, intent-consistent: an AI unit idle on a ford is outside engagement (`CountTerrainLine`, 27695; 80683) | none |
| 5 Campaign forest/swamp | Intent existed (layers 9/10 are DeepForest/Swamp) but code excludes it and would misread ids 32/33; campaign-wide rebalance | none; BUG-AI-015 Backlog, recommend leaving vanilla (user's call) |
| W-1 Engineer branch bonus tests navy | Vanilla bug, BUG-AI-014 (61 historical engineer commanders) | `EngineerBranchBonusPatch` |
| W-2 Engineer Corps once-effect | Already fixed on `fix/wl-economy-bugs` (BUG-WL-009, effect 23 in `FoldsOnceEffect`) | none here |
| W-5 A Good Ground uncapped | Vanilla W&L bug, BUG-WL-015 | `GoodGroundCoverCapPatch`, cap 0.9 |
| W-7 FogGrid probe inherits incident 60/61/64 | Whiskey bug, BUG-TAC-020 | `BattlePerceptionProbe` masks the incidents for the AI side's probe |
| Research "BUG-AI-011" (fogged model swap) | Fixed on `fix/campaign-logistics-bugs` as BUG-LOG-002a (`FoggedEmbarkationPatch`) | cross-reference only |

## Decisions

- **Integrator decision (W-5), 2026-10-15:** clamp the doubled "A Good Ground" cover so it never exceeds 0.9; ranged losses are then never below 10%, and the incident keeps its full doubling below that. As built, the cap never lowers a unit below its cover without the incident (`min(doubled, max(0.9, undoubled))`).
- **Pontoon (item 2):** the tooltip (+25/50/75%) is the speed, as the brief asks. The path cost is set to 1/speed, not left at the prefs' 0.75/0.5/0.25, because vanilla prices every terrain at the cost it moves at; leaving the prefs factor would still make Pontoon III armies pick creek routes up to 2.3x slower than the detour (tested). Alternative for the user: speed = 1/prefs factor (+33/+100/+300%), which keeps the prefs numbers and pathing unchanged but exceeds the tooltip.
- **Perk pick (item 3):** no Transpiler. #7 already Prefix-skips `CheckPerkSelection`; the only cases where vanilla still picked are the alliances Whiskey does not steer. The method is two loops, so the Prefix replays them (same units, same W&L test `dlcw_isundercommander && dlc_scenarioactive`, same `aiarea == null` early return, uniform random id, vanilla's `ChoosePerk`) with the full range and returns false. Prefix-skip justification (wave-3 rule): the skipped body is exactly what the replay runs, so nothing vanilla does is lost; with the flag off the Prefix returns true there and vanilla runs. The rejected alternative is a narrow Transpiler on the two `Count - 1` sites in the style of the approved `RandomCandidatePickPatch` (`fix/campaign-logistics-bugs`, BUG-LOG-011); it would need the user's approval and buys nothing here.
- **Engineer branch (W-1):** the test is an inline constant; a Prefix swaps 3 <-> 4 on every commander for the call instead of a Transpiler. Neither method reads `historicalbranch` elsewhere; both run a few times per battle or autoresolve day.
- **No new Transpiler** in this slice. No item needs the user's approval.

## As built

| File | Kind | Notes |
|---|---|---|
| `Strategic/TerrainBugFixes.cs` (new, pure) | | pontoon level/speed/path factors, engineer swap, Good Ground cap |
| `Strategic/PerkSelectionScorer.cs` | | `CandidateLimit(n, includeLastId)`, `RandomPickUpperBound`, role weights |
| `Patches/TerrainSwaps.cs` (new, pure) | | `FieldValueSwap<TItem,TValue>`, `StaticRefSwap<T>` (restore only where still ours) |
| `Patches/PerkSelectionPatch.cs` (#7) | Prefix | full window; `ReplayVanillaPick` |
| `Patches/PontoonTrainCreekPatch.cs` | Postfix; Prefix / Finalizer | `GetTerrainSpeedAdjustment`, `AddPath` |
| `Patches/EngineerBranchBonusPatch.cs` | Prefix / Finalizer ×2 | `BattleUnits.CalculateAllUnitsOfAlliance`, `Autocalc.CalculateEngineeringPoints` |
| `Patches/GoodGroundCoverCapPatch.cs` | Postfix | `Regiment.CheckCoyCovers` |
| `Tactical/Battle/BattlePerception.cs`, `BattlePerceptionProbe.cs` | runtime (no patch) | incident mask around the FogGrid probe, restored in `finally` |
| `Plugin.cs` | | `Campaign / Fix AI Perk Pick Range`, `Fix Pontoon Train Creek Speed`, `Fix Engineer Branch Bonus`; `W&L / Fix Good Ground Cover Cap` (all default ON; config entries 112) |
| `tools/smoke-situational.txt` | | `campaign.patch.pontoon-creek.speed`, `campaign.patch.engineer-branch.autocalc.*`, `tactical.patch.good-ground-cover-cap` |

Ordinals: assigned at merge (other branches hold #80 BC9 and #81+ logistics).

Tests: `CampaignTerrainFixTests` (22) and the updated C-Perk window test. Red first: the scorer tests fail on the old weights; every fix was mutation-checked in a scratch copy (24 mutations incl. the runtime restore and flag paths, all killed).

Gates (this branch): `./build.sh` 0 warnings; test build 0 warnings; harness exit 0, 2126 PASS; config-audit 0; orphan-sweep 0, `--include-test-only` 29 (unchanged); `tools/test_smoke_check.py` OK.

## PROVE-MODE

PROVE-MODE: harness + source pins; runtime unproven until the in-game smoke below.

- **The harness proves:** every pure decision with its flag on and off (candidate window and replay bound, scorer role choices including River Expedition / Port Defender now reachable, pontoon level precedence, speed and path factors, the route choice is time-optimal and never slower than without the perk while vanilla's is, engineer swap, Good Ground cap, incident mask scope); both swap helpers restore only their own writes (twice, and keep a value vanilla wrote); the incident mask on the real `DLC_WL.Incident` type; the Harmony targets and fields exist in `refs/Assembly-CSharp.dll`; by source scan, each patch passes its flag, restores at the top of its Prefix and in its Finalizer, and the probe masks before both `UnitIsHidden` paths and restores in `finally`.
- **Only the in-game smoke proves:** HarmonyX applies the patches on Unity's Mono (`AddPath` and `CheckCoyCovers` are private); the swaps restore around the live calls; the Postfix cost on `GetTerrainSpeedAdjustment` (per unit per frame) stays negligible; the AI actually takes River Expedition / Port Defender.

## Smoke

Build, deploy and hash-verify per `AGENTS.md`, then `python3 -I tools/smoke-check.py "<GTCW>/BepInEx/LogOutput.log"`.

1. Campaign: `[first-fire] campaign.patch.pontoon-creek.path.prefix` and `.path.finalizer`; once a Pontoon Train group paths: `[once:pontoon-creek:path] [Patch:Pontoon] creek path cost for Pontoon Train groups priced at 1/speed`; once it stands on a creek: `[first-fire] campaign.patch.pontoon-creek.speed` and `[once:pontoon-creek:speed] [Patch:Pontoon] creek crossing speed x1.25` (or 1.5 / 1.75). Fail: `[Patch:Pontoon] … failed`.
2. Campaign perks: `[FormationDirective] source=PerkSelection … perk=17` (army) or `kind=fleet … perk=10` appears over a long run (River role armies, port-defence fleets); in a W&L CIC career `[once:perks:replay] [Patch:Perks] unsteered alliance: vanilla random perk pick replayed over every perk id`. No army logs `perk=7` under any role.
3. Battle start: `[first-fire] tactical.patch.engineer-branch.battle.prefix` / `.finalizer`; with an engineer-branch commander on an engineer regiment: `[once:engineer-branch:battle] [Patch:EngineerBranch] battle engineering points: branch bonus tests the engineer branch`; deployment points higher than before for that side. Autoresolve: `campaign.patch.engineer-branch.autocalc.*`. Fail: `branch restore failed`.
4. W&L battle with companion Resolute Officer and incident "A Good Ground": `[first-fire] tactical.patch.good-ground-cover-cap`; behind parapets `[once:good-ground:cap] [Patch:GoodGround] A Good Ground cover 1.10 capped at 0.90`; the player's units behind works still take some ranged losses.
5. W&L battle with "Lost Orders" active (or a campaign Scouting Ahead incident): `[once:tactical.battle-perception:incident-mask] [BattlePerception] W&L incident 60 (player intelligence) ignored by the AI side's fog-grid probe`; the enemy AI does not react to player units it cannot see. Fail: `W&L incident restore failed`.

Rollback: each flag above set false restores vanilla for its fix; the scorer weights and the perception mask have no flag (Whiskey's own code).
