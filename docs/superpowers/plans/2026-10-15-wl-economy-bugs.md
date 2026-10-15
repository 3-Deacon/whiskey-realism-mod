# W&L economy and battle stat bug fixes (2026-10-15)

Branch `fix/wl-economy-bugs` (worktree `.worktrees/wl-economy-bugs`, base `main` `a5cce64`). User direction: "okay, fix all of this correctly", in line with vanilla's evident intent; W&L is allowed the way vanilla allows it. Ordinals for the new patches are assigned by the integrator at merge.

Decompile: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (build 14703111, "DC:n"). Game data: `<GTCW>/Config/` (`actions.dat`, `staff.dat`, `camp.dat`, `effectdescriptions.dat`, `campaignprefs.txt`). Research inputs: the session's `research/wl-logistics.md`, `research/deploy-supply.md` (items 11-12), `research/battle-deploy.md`.

Out of scope: campaign AI logistics (depots, supply thresholds, camps in fog, winter modifier; `fix/campaign-logistics-bugs`), the W&L campaign order chain (stage F), the career transfer fix itself (stage G, `BUG-WL-003`).

## Items and verdicts

| # | Bug | Verdict | Mechanism |
|---|---|---|---|
| 1 | `BUG-WL-005` player attrition bonus applies to every unit | Fix | Prefix/Finalizer snapshot of effect 22 on `Regiment.UpdateCampaignUnitStats` |
| 2 | `BUG-WL-006` Buy Supplies adds nothing for HQ players | Fix | Postfix on `DLC_WL.GetAverageSupplySubordinates` / `AddSuppliesSubordinates` |
| 3 | `BUG-WL-007` supply staff and Inspect Logistics inert below the map group | Fix | Prefix/Finalizer on `Regiment.UpdateIIPSupplyLink` (map-group flag) |
| 3+ | `BUG-WL-008` supply efficiency (effect 9, camp station 5) slows resupply | Fix (found while fixing 3) | same Prefix/Finalizer, effect-9 snapshot |
| 4 | `BUG-WL-009` dead "once" action qualities (effects 9/22/23) | Fix 9 and 23; record 22 as ambiguous | Postfix on `DLC_WL.UpdateCumulativeEffects` |
| 5 | `BUG-WL-010` #3 logs `transfer-queued` when nothing moved | Fix (telemetry) | `TransferOfUnitsPatch` Postfix classification |
| 6a | `BUG-TAC-018` `enemyentrenchmentlevel` divided by the own count | Fix | Postfix on `BattleUnits.CalculateAllUnitsOfAlliance` |
| 6b | `BUG-TAC-019` group HQ horse/gun condition set from men | Fix | Prefix/Postfix on `BattleUnits.AssignConditionToSubordinates` |

Evidence and anchors per item: `docs/bug-fixes/vanilla-wl-career.md` and `docs/bug-fixes/vanilla-tactical-battlefield.md`.

## As built

Pure decisions (harness): `src/WhiskeyRealism/Strategic/WlEconomyFixes.cs` (`WlEconomyFixTests`, 13 tests), `src/WhiskeyRealism/Tactical/BattleStatFixes.cs` (`BattleStatFixTests`, 4 tests); both red first (missing type), both in the test csproj. Runtime (not in the test csproj):

| Patch (ordinal at merge) | Kind / target | Flag | Notes |
|---|---|---|---|
| `WlAttritionScopePatch` | Prefix / Finalizer on `Regiment.UpdateCampaignUnitStats` (114965) | W&L / `Enable W&L Attrition Scope Fix` | Career + group not owned + running[22] != 1: running[22] = 1 for the call; Finalizer restores only its own untouched write (`ShouldRestore`). Rule = `groupunit.dlcw_isundercommander`, as medical/training/readiness. |
| `WlBuySuppliesSubordinateTypePatch` (`.AverageSupplyPatch`, `.AddSuppliesPatch`) | Postfix on `DLC_WL.GetAverageSupplySubordinates` (47377) / `AddSuppliesSubordinates` (47411) | W&L / `Enable W&L Buy Supplies Fix` | Only for an HQ command (vanilla did nothing there); vanilla's loop with the attached unit's type. Basic-unit commands untouched (`allattachedunits` holds only basic units, self included). |
| `WlSupplyStaffScopePatch` | Prefix / Finalizer on private `Regiment.UpdateIIPSupplyLink` (114074) | W&L / `Enable W&L Supply Staff Scope Fix`, `Enable W&L Supply Efficiency Sign Fix` | Scope: the player's map group (`GetCampaignGroup(currentcommand)`, cached per frame, `CampaignScope` reset) gets `dlcw_isundercommander = true` for the call. Sign: for an owned group running[9] = `1/(effect × camp²)` (`Camp.GetModifier(5)` read the same way vanilla reads it, through #29's Postfix), so the duration is divided by effect × camp. Finalizer restores both. |
| `WlActionOnceEffectsPatch` | Postfix on `DLC_WL.UpdateCumulativeEffects` (44662) | W&L / `Enable W&L Action Once-Effects Fix` | running[id] ×= max(0, 1 + once[id]) for ids 9 and 23; id 9 only with the sign fix on. Effect 22 untouched. |
| `TransferOfUnitsPatch` (#3, changed) | Postfix classification | none (telemetry correction; "off" would only restore a wrong row) | `TransferAddedCount` after vanilla's purge; `ClassifyTransfer` by `transfertime > 0`; rows `transfer-queued` (Write) / `transfer-blocked reason=wl-career-moveunit-guard` / `transfer-no-transit` (Decision, deduped per alliance). |
| `BattleEntrenchmentAverageFixPatch` | Postfix on `BattleUnits.CalculateAllUnitsOfAlliance` (87335) | Tactical / `Enable Battle Entrenchment Average Fix` | Recomputes the AI-side sum with vanilla's predicate, divides by its own count. |
| `BattleHqConditionFixPatch` | Prefix / Postfix on `BattleUnits.AssignConditionToSubordinates` (85453) | Tactical / `Enable Battle HQ Condition Fix` | Prefix: HQ before-values, per-group horse/gun clamps, which groups reach each HQ (vanilla's own `GetComponentsInChildren<Regiment>()`); Postfix: HQ = min(before, enclosing, own) and side gun average recomputed. `__state` only. |

Shared helper `Patches/WlEffectArrays.cs` (no `[HarmonyPatch]`): cached `AccessTools.StaticFieldRefAccess` to the private effect arrays, one bounded warning on failure.

Why the battle fixes skip the battle-core executor: both write derived stats (`enemyentrenchmentlevel`, HQ `conditionhorses/guns`, side `conditionguns`), not movement, stance, formation, facing or combat behavior, and they restore vanilla's own formula for both sides and the player's units alike; `BattleWriteGateTests` does not list these fields. Neither holds static state, so no `BattleScope` registration.

Situational smoke ids (`tools/smoke-situational.txt`): `campaign.patch.wl-buy-supplies.*`, `campaign.patch.wl-action-once-effects` (career only). The other new ids fire in every campaign or battle.

Mutation check (scratch copy, harness must fail): every pure decision mutated once — attrition scope, recipient type, HQ-command test, map-group scope, sign formula, fold ids, fold formula, transfer added count, transfer classification, side average divisor, enemy predicate, HQ clamp. Baseline copy 1978 PASS; all 12 mutants killed (harness exit 134 on the first failing assertion).

Not fixed, recorded: Confiscation policy supply-flow inversion (209893 → 114329-114335; same root cause as BUG-WL-008, not W&L, left for the integrator); readiness (staff 21, camp 11) and morale (effect 16, camp 7) inert below the map group; dead once/type-0 qualities on ids 0/17/19/27 and companions; the unscoped army-panel staff display (173699-173702); effect 22 on actions 40/41/49 (ambiguous intent).

## PROVE-MODE

PROVE-MODE: static + harness only. Gates on the final tree: `./build.sh` 0 warnings / 0 errors; test build 0 warnings; harness exit 0 with 1978 PASS (base 1961 + 17); `config-audit` 0 findings over 109 entries; `orphan-sweep` 0, `--include-test-only` unchanged from base (30 lines); `test_smoke_check.py` OK. Not built into a deployed DLL from this branch (no deploy per brief); runtime proof is the smoke list below after the integrator merges and deploys.

## Smoke items

Run `python3 -I tools/smoke-check.py "<GTCW>/BepInEx/LogOutput.log"` first: every new id except the two situational ones must show `[first-fire]`.

1. **Any campaign:** `[first-fire] campaign.patch.wl-attrition-scope.prefix`, `.finalizer`, `campaign.patch.wl-supply-staff-scope.prefix`, `.finalizer`. Outside a career no `[once:wl-...]` line appears (all fixes are career-only).
2. **W&L career, player CIC or with Camp Rules / Provost Marshal-General:** `[once:wl-attrition-scope:first] [Patch:WlAttritionScope] player attrition effect x0.5 withheld from a group the player does not own` (the factor is the player's running effect 22). Check: enemy and AI-group sickness/desertion no longer drop when the player buys Camp Rules; the player's own units' tooltip still shows "Staff influence on attrition".
3. **W&L career as a division/corps/army commander, Buy Supplies (camp station Inspect Logistics):** `[first-fire] campaign.patch.wl-buy-supplies.add` and `[once:wl-buy-supplies:first] [Patch:WlBuySupplies] HQ command's units supplied (vanilla tested the command type)`; the command's units' supply stock rises (army panel). Brigade commander: no `wl-buy-supplies:first` line (vanilla path unchanged).
4. **W&L career as a brigade/regiment commander (or a division inside a corps):** on the first supply update of the player's map group, `[once:wl-supply-staff-scope:first] [Patch:WlSupplyStaff] player's supply staff and Inspect Logistics applied to the map group carrying the player's command: group=<parent group name>`. With a Quartermaster appointed, that group's army-panel supply figures now move with the staff (as for a corps player); no other group's do.
5. **W&L career with a Quartermaster (or camp station 5 time):** `[once:wl-supply-efficiency-sign:first] [Patch:WlSupplyEfficiencySign] supply-efficiency bonus now shortens the resupply duration: effect=<e> camp=<c> durationFactor=<1/(e×c)>`; with `e > 1` the factor is < 1, and the army panel's "Supply speed" for the player's group rises compared with the same save with the flag off.
6. **W&L career after buying Logistics Corps / Not Yet Ready / Engineer Corps:** `[first-fire] campaign.patch.wl-action-once-effects` and `[once:wl-action-once-effects:first] [Patch:WlActionEffects] action once-bonus folded into the running effect: id=9 once=0.2 running=<staff × 1.2>` (or id=23); the army panel's "Staff influences: Supply efficiency +x %" includes the action.
7. **W&L career, campaign tuning profile:** no `transfer-queued` Plan rows for a career faction; instead `transfer-blocked` rows with reason `wl-career-moveunit-guard` (deduped) and `[once:transfer:wl-career-blocked] [Patch:Transfer] W&L career: vanilla MoveUnit refused the transfer ...`. In a normal campaign `transfer-queued` rows only for units whose transfer time is running.
8. **Any battle:** `[first-fire] tactical.patch.entrenchment-average-fix`, `tactical.patch.hq-condition-fix.prefix`, `.postfix`. When the two sides have different group counts and the AI side is entrenched: `[once:tactical.entrenchment-average-fix:first] [Patch:EntrenchmentAverage] AI-side entrenchment level averaged over its own groups: vanilla=<v> corrected=<c> aiGroups=<n>` with `c = v × playerGroups / n`. When a side arrives with horse/gun condition above its men condition: `[once:tactical.hq-condition-fix:first] [Patch:HqCondition] ... hqsCorrected=<n>`; a corps HQ's group panel shows horses/guns no lower than its regiments'.
9. No `[Patch:Wl*] ... failed`, `[Patch:WlEffects]`, `[Patch:EntrenchmentAverage] postfix failed` or `[Patch:HqCondition] ... failed` warnings.
