# Need-Driven Policy Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Use superpowers:test-driven-development for every pure-logic change. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every government Whiskey runs researches the policy its war needs (credit, manpower, recognition, blockade, industry), with the faction's vanilla personality list (history) as a strong preference, never triggering irreversible/scripted policies by accident, and never removing or replacing a policy the player chose. Spec: `docs/superpowers/specs/2026-10-14-need-driven-policy-design.md` (its "Vanilla and W&L model" section is the investigation record).

**Architecture:** A pure `CampaignNeedBuilder` turns a daily per-alliance snapshot (own-alliance and public data only) into a smoothed `NeedVector`; a pure `PolicyEffectCatalog` describes what each policy does; `PolicyEligibility` + `PolicyNeedScorer` + `PolicyPlanLedger` rank candidates with hysteresis on the daily strategic review; `PolicySelectionPatch` only consumes the ranking, and protects the player's in-progress research from vanilla's emergency cancel. `ProjectSelectionPatch` reads the same need vector. The old hand-tagged scorers are deleted.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4 + HarmonyX, Unity 2021.3 Mono; console harness `tests/WhiskeyRealism.Tests`.

**Branch:** `feat/need-driven-policy` from `main` 163e681, worktree `.worktrees/need-driven-policy` (`refs` symlinked).

## Plan corrections after the 2026-10-15 investigation

1. **Emergencies.** The earlier deviation ("the Prefix returns true for emergency personalities so vanilla handles them") stays for AI alliances, but vanilla's emergency branch (211065-211074) cancels a W&L prestige-bought policy (vanilla bug; the player cannot cancel, 211368). For the player's alliance the Prefix appends the in-progress ids to the emergency list and a Finalizer removes them (snapshot/restore, #25 pattern). The spec's older "cancel only research Whiskey started" is dropped (it needed persisted provenance).
2. **Blockade.** `ProjectSelectionPatch` reading `alliance[1].averageblockaderatio` for both sides is correct (own-harbor blockade, `GetAverageBlockadeRatio` 93766; Union pressure = 1 − CSA ratio). Not a bug.
3. **Recognition** need is the intervention probability for both sides (positive `AdjustInterventionLevel` helps the actor on either side, 62822/62887) — not `1 − p` for the Union.
4. **Player gating.** No vanilla CIC exemption exists for policies (211019); W&L forces `ai_vs_ai` so vanilla picks for the player's alliance at every rank. User decision 2026-10-15 (verify round 1): a CIC player's alliance gets Whiskey's picks too; the review computes its fiscal posture unpublished and its policy plan. A non-W&L player alliance never gets a Whiskey pick.
5. **Grand filter.** `markedasgrandpolicy` is UI-only (209337); use `Policy.grandpolicies` and `ResearchDuration <= 0`.
6. **Fog honesty.** IndustryGap from the enemy's project levels is replaced by an own weapon-stock coverage need; ArtilleryDeficit/NavalDeficit stay 0 (no own-only source). `GetStrengthRatio` is **not** fog-honest (12366-12402 adds enemy `troopsinrecruitment`; `SeparateUnits` 10462 has no fog filter): OffensiveTempo stays 0 and the collapse risk drops its strength term until stage G's `EnemyArmyModel` exists.
7. **Catalog.** `NeedKind` drops TradeWar, Naval and Command (no need source; folded into Blockade or dropped). Side costs are a cost vector over need kinds instead of three scalars; `PolicyFlags` is replaced by an `IrreversibleReason` string. 42 (Lt Gen Act) is not irreversible.
8. **Constants.** `DurationWeight = 1.0` per research-year (0.15 was negligible against 0.05-0.2-year durations).
9. **Sidecar.** Need EMA and the ledger pick persist on `FactionDto`; version stays 2 (optional fields).

## Global Constraints

- Vanilla first: anchors (decompile `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`, build 14703111) in headers and commit messages.
- Patch hygiene (AGENTS.md): never throw; `TelemetryPerf.Scope` + `PatchFirstFire.Mark` on the Prefix and the Finalizer; static OnceLog keys; alliance bounds; state written only by the daily review; the patch reads the ledger.
- No new Prefix-skip beyond the existing `PolicySelectionPatch` Prefix; no Transpilers. The emergency protection mutates a vanilla list and restores it in a Finalizer.
- Plugin.cs: add `EnableNeedDrivenPolicySelection` (`"Strategic", "Enable Need-Driven Policy Selection"`, default true) as the rollback for the selection; the emergency protection runs under the master switch.
- Tests in `tests/WhiskeyRealism.Tests/NeedDrivenPolicyTests.cs` (`internal static class NeedDrivenPolicyTests { public static void Run() }`, auto-discovered), every new pure file added to the test csproj; every deleted file removed.
- Tuning constants (named `public const`, pinned by tests): `NeedVector.EmaDays = 14`; `PolicyNeedScorer.PersonalityWeight = 0.7f`, `ProfileWeight = 0.3f`, `LookAheadWeight = 0.5f`, `SlotPressureWeight = 0.4f`, `DurationWeight = 1.0f`, `IrreversibleNeedGate = 0.7f` (hard hold, no penalty), `LookAheadDepth = 3`; `PolicyPlanLedger.SwitchMargin = 0.25f`, `EmergencyNeedThreshold = 0.85f`.
- Gates before each commit (wave3-common): `./build.sh` 0 warnings; test build 0 warnings; harness exit 0 + PASS count; `python3 -I tools/config-audit.py .` 0; `python3 -I tools/orphan-sweep.py .` 0 and `--include-test-only` adds nothing; `python3 -I tools/test_smoke_check.py` OK.

## File map

| File | Kind | Responsibility |
|---|---|---|
| `src/WhiskeyRealism/Strategic/Policies/PolicyEffectCatalog.cs` | pure, create | `NeedKind`; id → `PolicyEffect` (weights, costs, irreversible reason) |
| `src/WhiskeyRealism/Strategic/Policies/PolicyEligibility.cs` | pure, create | `PolicyCandidateFacts`; hard filters |
| `src/WhiskeyRealism/Strategic/Policies/PolicyNeedScorer.cs` | pure, create | `PolicyScore`; score formula |
| `src/WhiskeyRealism/Strategic/Policies/PolicyPlanLedger.cs` | pure, create | ranking + hysteresis, per alliance |
| `src/WhiskeyRealism/Strategic/Policies/PolicyListOverride.cs` | pure, create | ids to protect from vanilla's emergency cancel; held acts out of vanilla's fallback walk; snapshot restore |
| `src/WhiskeyRealism/Strategic/Policies/PolicyRuntime.cs` | runtime, create | vanilla policies → facts, personality, dependents, start research |
| `src/WhiskeyRealism/Strategic/CampaignNeedBuilder.cs` | pure, create | `NeedSnapshot` → smoothed `NeedVector`; `ToProjectSignals` |
| `src/WhiskeyRealism/Strategic/CampaignNeedRuntime.cs` | runtime, create | vanilla alliance state → `NeedSnapshot` |
| `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs` | modify (shared) | daily: need vector + policy plan after fiscal intent; persistence wiring; reset |
| `src/WhiskeyRealism/Strategic/PersistenceDto.cs`, `SidecarDtoCodec.cs` | modify | persist need EMA + pick |
| `src/WhiskeyRealism/Patches/PolicySelectionPatch.cs` | rewrite | consume ledger; emergency protection Prefix/Finalizer |
| `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs` | modify | signals from the need vector |
| `src/WhiskeyRealism/Plugin.cs` | modify (shared) | `EnableNeedDrivenPolicySelection` |
| `src/WhiskeyRealism/Strategic/GrandStrategyPolicyScorer.cs` | delete | |
| `src/WhiskeyRealism/Strategic/Fiscal/FiscalPolicyScorer.cs` | modify | delete `PolicyWeight`, `PolicySelection`, `FiscalPolicySelector` |
| `src/WhiskeyRealism/Strategic/Projects/ProjectDoctrineSignalBuilder.cs`, `ProjectDoctrineSignalInput` | delete | |
| tests: `NeedDrivenPolicyTests.cs` (new), `FiscalTests.cs`, `CampaignEconomyFixTests.cs`, `StrategicMapTests.cs`, `ProjectDoctrineTests.cs` | modify | delete tests of deleted code; port still-valid assertions |

---

### Task 1: Policy effect catalog

**Produces:** `NeedKind { Credit, Treasury, Manpower, ContractStability, Morale, Support, Recognition, Blockade, Industry, Agriculture, Logistics, Supply }`; `PolicyEffect { PolicyId, Weight(NeedKind), Cost(NeedKind), IrreversibleReason }`; `PolicyEffectCatalog.Get(id)`, `NeedKindCount`, `AllianceOf(id)` (no id enumeration: the completeness test walks the `policies.dat` fixture).

- [x] Fixture of every policy id in `Config/policies.dat` (parsed: count line, then per record id, scenarios, prerequisite count + 5 slots, duration, act, chapter, headline, tooltip, newspaper flag, headline count + 5 slots, picture, picture headline, content). Failing tests: every non-grand fixture id has an entry and every grand id (70-79, 170-179) none; weights/costs in [0, 1]; irreversible set exactly 33, 39, 40, 48, 130, 133, 134, 139, 140, 144, 147; Bank Acts cost Support; Print Notes cost Treasury; Bounties cost Credit; Tariff and Letters of Marque cost Recognition; `AllianceOf` (< 100 Union, 100-199 CSA, else −1).
- [x] Implement as a static table, one line per id with a `LaunchPolicyEffect` line comment; weights from default campaignprefs magnitudes.

### Task 2: Eligibility

**Produces:** `PolicyCandidateFacts { PolicyId, IsGrand, IsAct, ActiveOrDone, ReferedChapter, CurrentChapter, HasPrePolicies, Deactivated, Blocked, AvailableInScenario, SlotFree, ResearchDuration }`; `PolicyEligibility.IsEligible(facts, alliance, out reason)`.

- [x] Failing tests per filter (alliance ≥ 2 / < 0, foreign id, grand incl. duration 0, active, chapter, prerequisites, deactivated, blocked, scenario, no slot for non-acts while acts pass, Union 36 in chapter 0 only), `reason` names the first failing filter.
- [x] Implement; `PolicyRuntime` reads the same vanilla fields as 211082 and `CanBeActivated` 209541; `IsDeactivated` via cached reflection, fail closed; dependents map from `NeededPrePolicies` excluding branches through an unheld grand act.

### Task 3: Campaign need vector

**Produces:** `NeedSnapshot` (own/public fields only), `NeedVector` (`EmaDays = 14`, indexer by `NeedKind`, `Copy`), `CampaignNeedBuilder.Raw/Smooth/Next/ToProjectSignals`, `CampaignNeedRuntime.Read(alliance, era, fiscalInput, fiscalOutput)`.

- [x] Failing tests: Credit 1/0.75/0.25/0 by posture; Treasury from deficit/spending; Manpower from recruit ratio and recruitable states; ContractStability; Morale/Support inversions; Recognition = intervention probability for both sides; Blockade CSA own, Union 1 − CSA; Industry from weapon coverage; Logistics above 60 % peak; all in [0, 1], NaN → 0; EMA alpha 2/(EmaDays+1), `Smooth(p, r, 2) == Smooth(Smooth(p, r, 1), r, 1)`; emergencies force Credit/Manpower to 1 after smoothing; `ToProjectSignals` ports the `ProjectDoctrineSignalBuilder` assertions (posture → CreditStress, late-war collapse, clamps, null → neutral 0.5 blockade/port, non-finite logistics) and maps the new signals.
- [x] Implement builder and runtime (anchors per field in the spec table).

### Task 4: Scorer, ledger, emergency protection

**Produces:** `PolicyScore { PolicyId, Total, Need, Personality, Profile, LookAhead, SlotPressure, Duration, SideCost, Held }`; `PolicyNeedScorer.Score(facts, need, personalityList, profile, usedSlots, maxSlots, dependents)`, `IsHeld(id, need)`; `PolicyPlanLedger { CurrentPick, Ranked, Update(scored, need, previousNeed), NextEligible(stillEligible), Restore(pick), DescribeTop(n) }`; `PolicyCallFacts`, `PolicyCallAction`, `PolicyListOverride.Decide(facts, findPick, startPick)`, `ProtectedIds(researchIds, progress, personalityList)`, `HeldIdsToRemove(need, personalityList)`, `Append(list, ids, out snapshot)`, `Remove(list, ids, out snapshot)`, `Restore(snapshot)`.

- [x] Failing tests: credit crisis → War Bonds / Bank Act over Diplomacy; manpower crisis → drafts over Diplomacy; near tie → history wins; irreversible acts held below 0.7 and never started (slots-full probe); Bank Act penalised at low support; slot pressure non-acts only; look-ahead lifts a cheap prerequisite; ledger hysteresis, emergency switch, `NextEligible` order (pick first), `Restore`; protected ids (progress < 1, not already listed, distinct); constants pinned.

### Task 5: Wire review, persistence, patch; delete old scorers

- [x] `StrategicCoordinator`: `Needs`, `NeedSnapshots`, `PolicyPlans`; `UpdatePolicyPlan(alliance, era, reviewSerial)` after `UpdateFiscalIntent` inside `TelemetryPerf.Scope("campaign.policy-plan", …, 4.0)`; `policyMs` in `[DailyOps:Perf]`; reset in `ResetAll`; sidecar save/apply.
- [x] `PolicySelectionPatch`: Prefix order — master switch; alliance bounds; mirror 211019; personality; **emergency** → player alliance: append protected ids (`__state`), emit `emergency-protect`, return true; other alliances return true; `EnableNeedDrivenPolicySelection`; coordinator; research in progress; `NextEligible` with live `PolicyRuntime.IsEligibleNow`; `TryStartResearch`; emit `select-policy`; return false. Finalizer restores the list.
- [x] Delete `GrandStrategyPolicyScorer.cs`, `FiscalPolicyScorer.PolicyWeight`, `PolicySelection`, `FiscalPolicySelector` and their tests; port the force-cap draft suppression to the new SideCost/eligibility semantics only if still wanted (it is not: force caps are a recruitment concern, drafts are judged by Manpower need).
- [x] Plugin flag; patch-catalog row #19 rewritten.

### Task 6: Projects read the shared need vector

- [x] `ProjectSelectionPatch` uses `CampaignNeedBuilder.ToProjectSignals(NeedSnapshots[a], Needs[a])`; delete `BuildSignals`, `ReadRecognitionProbability`, `ProjectDoctrineSignalBuilder`, `ProjectDoctrineSignalInput` and the csproj line.

### Task 7: Docs, sweep

- [x] `docs/patch-catalog.md` (#17, #19, runtime row), `docs/handoff.md` open items, `docs/findings.md` "Policy selection" section, `docs/wl-player-model.md` (purchase starts research; bugs A/B; Lt Gen Act), `docs/bug-fixes/` (BUG-POL-002 mitigated, BUG-WL-003 fixed, BUG-WL-004 recorded), `src/WhiskeyRealism/Strategic/AGENTS.md` (Policies folder). No `tools/smoke-situational.txt` entry: the patch ids became `campaign.patch.policy-selection.prefix` / `.finalizer`, and both fire on every `CheckAIPolicyChange` call of any campaign session.

## As built (2026-10-15)

Commits `1833a80` (investigation + corrections), `60c6b2b` (implementation), `d34616f` (restore handle published before the vanilla list is touched), `b93f3f6`, and the verify round 1 revision (hard hold on irreversible acts, no `GetStrengthRatio`, CIC alliance included, needs only once factions exist, sidecar round-trip test). Harness 1950 PASS / 0 FAIL (1917 − 13 deleted tests of deleted code + 46 `NeedDrivenPolicyTests`); build and test build 0 warnings; config-audit 0 over 102 entries; orphan-sweep 0 and `--include-test-only` unchanged (29, all predating this slice); `test_smoke_check.py` OK. Not merged, not deployed.

Deliberate deltas against vanilla's AI for the user: the Regulars Acts (48/147) and the other irreversible acts are never started by Whiskey or vanilla's fallback below their gating need (≥ 0.7) where some vanilla personalities took them early (Historical, which #12 forces, never lists 48/147); policies outside the personality list (e.g. CSA War Bonds/Bank Act) are now reachable when needed; a player-alliance emergency pick waits for the player's protected research.

Landing revision (after verify round 2): the hold is total. With selection on, a need vector for the alliance, no emergency, nothing in research and no Whiskey pick, #19 snapshots vanilla's personality list, removes the held acts (`PolicyListOverride.HeldIdsToRemove` / `Remove`, `PolicyNeedScorer.IsHeld` shared with the ranking) and the Finalizer restores the snapshot (`Restore`: `Clear` + `AddRange`, contents and order); the emergency protection now restores through the same snapshot (`ProtectedTail` → `ListSnapshot`). `PolicyEmergencyProtection` was renamed `PolicyListOverride`. "Has a plan" is `StrategicCoordinator.Needs[alliance] != null` (the review's need vector, set with the ranking and persisted in the sidecar), so the hold also covers acts that became eligible after the last review. Harness 1954 PASS before the main merge (+4 fallback-hold tests). Accepted strength read: `EmergencyManpower` mirrors vanilla's recruiting emergency (`GetAIPersonality` 62733 calls `GetStrengthRatio`), which vanilla announces publicly (newspaper event 211026-211058); no other strength read.

Verify round 3 landing items: the Prefix's branch order moved into the pure `PolicyListOverride.Decide(PolicyCallFacts, findPick, startPick)` → `Vanilla` / `ProtectPlayerResearch` / `Pick` / `HoldFallback` (emergency first, then research in progress, then the flag, then the started pick, then the need vector); the patch only gathers the facts and follows the action. `HeldIdsToRemove(need, list)` lost its dead `emergency`/flag parameters (Decide is the guard). The snapshot moved into the pure layer (`PolicyListOverride.Snapshot`; `Append` / `Remove` take it and publish it through the `out` parameter before the edit). The Finalizer logs `[once:policy-selection:restore]` with the restored length.

Arms follow-up (2026-10-15, `fix/arms-bugs`, plan [`2026-10-15-arms-bugs.md`](2026-10-15-arms-bugs.md) item 6): the Industry need was `1 − Σ weaponstock / troopsinrecruitment`, 0 whenever nothing recruited, although recruits never draw per-model stock (`CreateUnitDefault` 89508). It is now the own arming need: per lane (guns / other units, alliance tick 81589-81605) the larger of vanilla's purchase-trigger coverage gap (`ArmsCoverage` replicates `CheckPurchaseWeapons` 16287-16312 against `aiminnumberofequippableunits`) and the WSF quality gap against the best model the alliance can order (`WeaponOrderability`); Industry takes the worse lane. `ArtilleryDeficit` is the gun lane (was 0: own gun averages are own-side data). ArmsImport projects scale by vanilla's blockade import factor (`ProjectDoctrineSignals.ArmsImportViability`, 1 → 0.1, 64120-64124). `NeedSnapshot.WeaponStock` / `TroopsInRecruitment` removed. Tests updated red first: `need-policy industry and logistics needs`, `need-policy arms coverage mirrors vanilla's count`, `need-policy project signals fill the new needs`, `project doctrine arms imports follow the blockade's import cut`.

## PROVE-MODE

PROVE-MODE: harness for the pure layer, in-game smoke for the runtime. Per-slice proof is `NeedDrivenPolicyTests` (exit 0): catalog completeness against the `policies.dat` fixture, eligibility filters, need builder and project-signal port, scorer scenarios (credit crisis, manpower crisis, history tie-break, irreversible gate, support cost, slot pressure, look-ahead, profile, Union/CSA openings), ledger hysteresis, the emergency protected-id set, the held-act set (held acts removed in vanilla's order, nothing held without a need vector), the snapshot edits (`Append` / `Remove` snapshot before the edit and only when they change the list; `Restore` gives back the identical list, never clears without a snapshot), and the call decision `Decide` (an emergency never holds and protects only the player's alliance; selection off and research in progress leave vanilla untouched and never ask for a pick; a started pick skips vanilla with no list edit; with no started pick the fallback is held only with a need vector). Red first for the revision: with `Held` declared but never set, `need-policy irreversible acts wait for their need` failed (`Mexico held at low need`); the verifier's probe (CSA, slots full, field {130,133,134,144}) is `need-policy held irreversible acts are never started`. Mutation evidence: dropping the irreversible gate turned `need-policy irreversible acts wait for their need` red; swapping the Union/CSA blockade branch turned `need-policy blockade need per side` red; removing the `progress >= 1` skip turned `need-policy emergency protects only in-progress research` red (each exit 134). Landing revision red first: with `HeldIdsToRemove` returning empty and `Remove` a no-op, `need-policy fallback hold removes held acts and keeps vanilla's order` failed (`held acts in list order, each once (count): expected 3 got 0`); a tail-only restore turned `need-policy emergency restore gives back vanilla's list` red (exit 134). Round 3 red first: with `Decide` returning `Vanilla`, `need-policy call decision: an emergency never holds and protects only the player's alliance` failed (`player emergency: protect research, no hold: expected ProtectPlayerResearch got Vanilla`). Round 3 plants, each in a scratch copy, each exit 134: hold on an AI emergency (B2) and the emergency branch dropped fail `AI emergency: vanilla's own call, list unfiltered`; flag ignored (C2) fails `selection off: vanilla`; research-in-progress ignored fails `research in progress: vanilla returns at 211061`; snapshot after `Remove` (A3) fails `snapshot taken before the edit`; snapshot after `Append` fails `restored length`; hold after a started pick fails `started pick: vanilla skipped, no list edit`. Not proven by the harness (no Unity): `PolicyRuntime`, `CampaignNeedRuntime`, the coordinator wiring, and the patch's own lines — reading the facts, passing `__state` to `Append` / `Remove`, and calling `Restore` from the Finalizer; those are the smoke items below (5, 9, 10).

## Smoke expectations (after BepInEx reinstall and deploy; not part of this slice's gate)

1. LogOutput.log shows `[first-fire] campaign.patch.policy-selection.prefix` and `[first-fire] campaign.patch.policy-selection.finalizer` within the first campaign days.
2. LogOutput.log shows `[once:policy-selection:wired] PolicySelectionPatch need-driven pick alliance=<a> policy=<id>` once per session after the first Whiskey pick.
3. With `Logging Profile = CampaignTuning`, the campaign tuning sidecar has `FiscalIntent` rows with `source=PolicySelection`, `decision=select-policy` and fields `selectedPolicy`, `score`, `need`, `history`, `profile`, `lookAhead`, `cost`, `top` (the top three as `id:total(need/history/profile/lookahead/cost)`) for both alliances in 1861-62, and `decision=policy-plan` rows (fields `pick`, `candidates`, `top`, `needs`) when an alliance's ranked pick changes.
4. No research stall: whenever an AI alliance has nothing in research at a daily review, a `select-policy` row (or vanilla's pick) follows within one alliance-data update.
5. Emergencies (rating past 0.4 notches or ≤ 4 recruitable states in war) still launch vanilla's newspaper event; for the player's alliance with research in progress, LogOutput.log shows `[once:policy-selection:emergency-protect] PolicySelectionPatch kept the player's research through vanilla's emergency cancel alliance=<a> protected=<n>`, a `decision=emergency-protect` row follows, and the purchased policy keeps its progress.
8. With `Logging Profile = CampaignTuning`, `[DailyOps:Perf]` rows in the campaign tuning sidecar carry `policyMs=`, and `campaign.policy-plan` rarely appears among slow scopes (threshold 4 ms).
6. No `[once:policy-selection:*]`, `[once:policy-runtime:*]` or `[once:campaign-need:*]` warnings (needs and policy plans are built only once `AICampaign.aifaction` exists).
9. When an alliance with a need vector has nothing in research and no eligible Whiskey pick while its personality list holds irreversible acts below their gate (e.g. Union Historical 33/39/40 early in 1861-62), LogOutput.log shows `[once:policy-selection:hold-fallback] PolicySelectionPatch held irreversible acts out of vanilla's fallback walk alliance=<a> held=<ids>`, a `decision=hold-irreversible` row (reason `vanilla-fallback`, field `heldIds`) follows, and vanilla's pick (if any) is not one of those ids.
7. W&L career as CIC: `select-policy` rows appear for the player's alliance only while nothing (no prestige purchase either) is in research; no `FiscalIntent` posture rows for that alliance (posture is computed for the needs only).
10. Whenever item 5 or 9 fires, LogOutput.log also shows `[once:policy-selection:restore] PolicySelectionPatch restored vanilla's personality list after the call restored=<n>`, where `<n>` is the personality list's length before the edit (never 0 for a non-empty list), and no `[once:policy-selection:restore-failed]` warning.

## Rollback

`Enable Need-Driven Policy Selection = false` → vanilla `CheckAIPolicyChange` chooses (the emergency protection for the player's alliance stays on; master `Enabled = false` removes it too). Project selection has no separate flag; its signals come from the need vector.
