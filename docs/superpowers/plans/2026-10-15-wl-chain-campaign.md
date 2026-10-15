# W&L Chain of Command — Campaign Side (Stage F) Implementation Plan

> **For agentic workers:** execute task by task with superpowers:test-driven-development for the pure units; verify with superpowers:verification-before-completion.

**Goal:** Roadmap stage F: §D-§F of `docs/superpowers/specs/2026-10-14-wl-chain-of-command-design.md` (as corrected by its § "Vanilla and W&L model (campaign side)"). Campaign dispatches for subordinate W&L players, one campaign dispatcher for Whiskey's own strategic orders, the player-compliance ledger (campaign + battle) wired into the battle evidence and exposed for the campaign planner, and the CIC faction-AI bug guard.

**Base:** `main` 163e681 (battle core merged). Branch `feat/wl-chain-campaign`, worktree `.worktrees/wl-chain-campaign` (`refs` symlinked). No merge, push or deploy in this slice (wave-3 rule).

**User decisions (binding):** W&L is allowed the way vanilla allows it; on disobedience the AI adapts, no penalty; the player's own command is never moved by Whiskey.

## Decisions resolved by the investigation

| # | Question | Decision |
|---|---|---|
| F-D1 | Where is the dispatch type known? | Not in the `MoveUnitTo` Postfix: the offensive (14383) and the defensive attack branch (13771) add the unit to their lists after the call. The Postfix records a pending move (unit, destination, list membership before); a Postfix on `AICampaign.UpdateUnitAI` (every decision-time caller runs inside it) classifies and emits. |
| F-D2 | Which moved units count? | Only the player's campaign group itself (`GetCampaignGroup`, 82991); an army/army group above a corps is a separate map counter. |
| F-D3 | Queued moves pass `MoveUnitTo` twice (14523, then 10048) | The dedupe skips any emission within 110 units of the last one for one game day, whatever the type. |
| F-D4 | Issuer | Trigger 1: the moving group's commander (who ordered the move). Trigger 2: `GetSeniorCommander()` index. |
| F-D5 | Bridge and the subordinate's parent group | **Revised 2026-10-15 (coordinator, stage G decision C4, brought forward from G5a):** one classifier `WlChainRules` (CicSide / PlayerCommand / PlayerMapGroup / CarryingGroup / None). The bridge allows the AI map group carrying a player below map-group level (reason `carrying-group`); every Whiskey mover moves through `MoveUnitTo`, so #69 dispatches it. The player's own command is never moved, his map group is order-only and never a committed defender, a CIC side stands down. The same classifier gates DefensiveOpsPatch's pre-pledge, #25's revert, the coastal runner, the probe stop and defense coverage. |
| F-D6 | Prestige for subordinate dispatches | **User 2026-10-15: vanilla prestige.** Subordinate dispatches keep vanilla's zone mark and arrival prestige exactly as the base game pays them (5: 150, 6: 100, 7: 120, 8: 500/yr, 16: 0), bounded by the one-per-target-per-day cap, although the corps carries the player. One constant, `WlCampaignDispatchPolicy.SubordinateDispatchesShowZone` (true), would make them informational (`showzone: false`). |
| F-D7 | Compliance signal | Vanilla's `GivenOrders.firstunitreached` on the tracked order object; only orders the player decides (campaign: own map group; battle: orders with a zone). |
| F-D7b | Compliance of subordinate dispatches | Not scored, although vanilla's `firstunitreached` works for them: the player is carried and cannot disobey, so every one would count as complied and only inflate reliability. The runtime records campaign orders only while the player's command is its own map group. |
| F-D7c | Starting reliability | Prior 1.0 (coordinator decision): trust until evidence; the AI adapts after observed non-compliance, no penalty up front. |
| F-D8 | Ledger lifetime | Keyed by career (commander id + `SceneManagement.currentcampaignscenario`; `GamePrefs.leveltoload` is the battlefield during a campaign battle, 36650-36651), not reset by campaign reloads (a battle round-trip reloads the campaign), saved in the sidecar, adopted on load only when the session has no samples for that career. |
| F-D9 | §F flag scope | One-shot: the first `IsCommanderInChief()` call after arming is the guard in both bodies; nested calls keep the true value. Disarm in a `Priority.First` Postfix and a Finalizer. #16's repeat of the guard is made alliance-aware. |
| F-D3b | Rate-limit memory | The last four emitted targets (`WlDispatchHistory`, ring) within one game day, whatever the type (the queued second call reclassifies 16 → 6 for the same target). Tested A, B, A. |
| F-D10 | Patch-written state | The dispatcher's pending queue and dedupe and the compliance observations are W&L dispatch bookkeeping written by event handlers (the #69 Postfixes, as #62 writes its shadow) and the battle coordinator tick. No strategic plan state is written from a patch. |

## Tasks

### F1 — Pure policy: `Strategic/WlCampaignDispatchPolicy.cs` (tests `WlCampaignDispatchTests`)

- `IsTrigger(in WlMoveFacts)`: W&L active, has command, not CIC, player not its own campaign group, moved == campaign group, moved not player-owned, not moved by player, move succeeded, alliance == player alliance.
- `ClassifyType(in WlMoveContext)`: defensive after → 7; capital after → 8; offensive after: before → 6, new → 16 if enemy at target else 5; otherwise 5.
- `Decide(candidate, lastEmission, currentOrder, nowDays, moveDistance)`: skip `no-op-move` (< 20), `active-duplicate` (current order same session as last emission, same type, within 110), `rate-limit` (last emission within 110 and < 1 day); else issue.
- Red first: each rule, alliance/empty inputs, queued second call deduped, different target re-issues, next day re-issues.

### F2 — Runtime + patch #69

- `Strategic/WlCampaignDispatchRuntime.cs`: `OnMoveUnitTo(unit, destination, result)` (facts + list membership before; bounded pending list, 16), `Flush()` (classify, name/target for 7/8, `Emit`), `EmitForPlayerCommand(...)` (trigger 2), `Emit(...)` (policy + vanilla-identical build, F-D4 issuer). Dedupe state via `CampaignScope.Register`. Telemetry line `[W&LCampaignDispatch] trigger=… type=… unit=… issuer=… target=… action=issue|skip reason=…` through `OnceLog`-bounded signatures (one line per distinct type+target+action).
- `Patches/WlCampaignMoveDispatchPatch.cs` (#69): Prefix + Postfix `AICampaign.MoveUnitTo` (`campaign.patch.wl-campaign-dispatch.move.prefix` hands the caller's target to the Postfix, since vanilla reassigns `position` to the stack position at 14498; `campaign.patch.wl-campaign-dispatch.move`), Postfix private `AICampaign.UpdateUnitAI` (`campaign.patch.wl-campaign-dispatch.flush`), Prefix + Finalizer on `CheckFinalPositions` and `RollUpEnemyObjectivesInZone` marking the move source (`.final-positions.prefix|finalizer`, `.roll-up.prefix|finalizer`). All fire in every non-tutorial campaign session (first-fire marked before the W&L test).
- Config `[W&L] Enable Campaign Chain Dispatches` (default true): off → no pending moves, no Whiskey campaign emission (trigger 2 then reports ineligible).

### F3 — Bridge (trigger 2)

- `WlStrategicOrderBridge.TryIssue`: `IssuedWlCurrentOrder` path calls `WlCampaignDispatchRuntime.EmitForPlayerCommand`; delete the gated `AIBattle.CheckCurrentOrderUpdate(calledfromcampaign: true)` call, `CampaignDedupeState`, `BuildCampaignCandidate`, `DecideCampaignOrder`, `ClearCampaignCacheIfContextChanged`, the campaign context key (`CampaignContextSources`, `BuildCampaignContextKey`), the active-order snapshot readers and their tests (`WlCampaignTests` dedupe tests, `CampaignMilitaryFixTests` context-key tests). Remove PlayerOrders members only the campaign path used (orphan sweep decides).

### F4 — Pure ledger + runtime (tests `PlayerComplianceTests`)

- `Strategic/PlayerComplianceLedger.cs`: `OnDispatch(scope, session, nowHours)`, `OnReached(scope, session)`, `Tick(scope, nowHours, orderStillTracked)`, `Reliability`, `Samples`, `Restore(r, n)`, `StrengthWeight(bool playerOwned, float reliability)`. Red first: complied, missed after grace, superseded neutral, cleared neutral, EMA values, clamps, scopes independent, restore only when empty.
- `Strategic/PlayerComplianceRuntime.cs`: career key, `ObserveCampaign()` (from #69 flush), `ObserveBattle(nowHours)` (from `TacticalBattleCoordinator.Tick`), `Reliability` (1 when the flag is off or W&L inactive), sidecar export/restore.
- Sidecar: `SidecarDto.PlayerCompliance` (nullable `{careerKey, reliability, samples}`), `SidecarSnapshot`, codec, coordinator Build/Apply (minimal edits; shared with stage G).
- Battle consumer: `ArmyEvidenceBuilder.BuildEnemyVisibleState` own combat sample strength × `PlayerComplianceLedger.StrengthWeight(PlayerOwned, Reliability)`.
- Config `[W&L] Enable Player Compliance Ledger` (default true).

### F5 — §F guard (tests `CicFactionAiGuardTests`)

- `Strategic/CicFactionAiGuard.cs` (pure): `ShouldArm(wlActive, playerIsCic, cicAlliance, factionAlliance)`, `ConsumeAsNotCic(armed, manualCommander)`.
- `Patches/CicFactionAiBugGuardPatch.cs` (#70): Prefix/Postfix(Priority.First)/Finalizer on `CheckShipConstruction` and `CheckArmyGroupManagement`; Prefix + Postfix on `DLC_WL.IsCommanderInChief` (the Prefix captures `manualcommander == -1` as `__state`: vanilla overwrites the argument at 47477-47480, so the Postfix never sees -1). Scope ids `campaign.patch.cic-faction-ai-guard.ship.*`, `.army-group.*`, `.is-cic.prefix`, `.is-cic` (situational: W&L only). One `[once:cic-faction-ai-guard:applied]` info line the first time the guard lets an enemy faction past.
- `ArmyGroupDoctrine.ArmyGroupsAllowed` takes `playerIsCicOfThisAlliance`; #16 passes `IsCommanderInChief() && alliance == CIC alliance`.
- Config `[W&L] Enable CIC Faction AI Bug Guard` (default true). `docs/bug-fixes` `BUG-WL-001` → In progress (#70).

### F6 — Docs

`docs/patch-catalog.md` (#69, #70; next #71), `docs/wl-player-model.md` (campaign map facts, candidate bug moved to tracked), `docs/wl-dispatch-objective-bridge.md`, `docs/wl-player-order-doctrine.md` (campaign side), `docs/telemetry.md`, `docs/handoff.md`, `docs/bug-fixes/README.md` + `vanilla-wl-career.md`, `tools/smoke-situational.txt`.

## As built (2026-10-15)

- F5 landed first (commit "fix(wl): CIC career …"): `CicFactionAiGuard` + #70; `ArmyGroupDoctrine.ArmyGroupsAllowed(allow, allianceAllows, vanillaCicGuardBlocks)`. Harness 1922.
- F1-F4 + F6: `WlCampaignDispatchPolicy`/`Runtime`, #69, bridge (gated path, campaign `PlayerOrderDedupe` shadow, campaign context key and their tests deleted; `PlayerOrderIntent.DefendCapital/BuildFort/BuildSupplyDepot` and the campaign-candidate branch of `PlayerOrderDedupe.PassesScopePolicy` deleted with their tests, since only the bridge produced them), `PlayerComplianceLedger`/`Runtime`, sidecar `playerCompliance`, `ArmyEvidenceBuilder` weight, coordinator tick hook. `WlCampaignDispatchRuntime` is compiled into the harness (the bridge and `CoordinatedOperationRuntime` are), so `TestPluginLogStub` gained `Plugin.Instance.Enabled` and `EnableCampaignChainDispatches`. Terrain height is read by reflection (the mod does not reference `UnityEngine.TerrainModule`). Harness 1944 (32 new, 10 deleted with the deleted code).
- Red first: `WlCampaignDispatchTests` (16, compile-red), `PlayerComplianceTests` (16, compile-red incl. the 2 sidecar tests), `CicFactionAiGuardTests` (5) and the `ArmyGroupsAllowed` contract (compile-red).

- Review round (2026-10-15, coordinator + user): subordinate dispatches keep vanilla prestige (F-D6, user; one constant), prior 1.0 (F-D7c), last-four rate-limit history (F-D3b), G5a note (F-D5), AGENTS.md bookkeeping clause, sidecar version comment. Harness 1947.
- Verify round 1 (REVISE) fixes, after merging main a5cce64 (BC8; harness 1991): B1 #70 Prefix capture of the default argument; #69 Prefix for the caller's target (14498); M1 career key on `currentcampaignscenario`, cached (m4); M2 cancelled queued move dropped at flush; M3 `[W&LCampaignDispatch]` routed to the campaign sidecar and the main log; m1 vanilla re-issue = one compliance sample; m2 vanilla `num2`, roll-up → 6, final positions → no dispatch; m3 last move per corps per pass; m5 export only the current career; m6 battle grace calibration item. Coordinator extras F-extra-1..4 (C4 classifier). Harness 2014.

## Gates (before each commit)

`./build.sh` 0 warnings; test build 0 warnings; harness exit 0 + PASS count; `python3 -I tools/config-audit.py .` 0; `python3 -I tools/orphan-sweep.py .` 0 and `--include-test-only` adds nothing; `python3 -I tools/test_smoke_check.py` OK; `git log --oneline -10 && git status`.

PROVE-MODE: harness for the pure units (`WlCampaignDispatchTests`, `PlayerComplianceTests`, `CicFactionAiGuardTests`, the bridge classification tests); build + static gates for the runtime and patches; in-game proof is the smoke below after the integrator deploys (no deploy in this slice).

## Smoke items (exact log text)

1. Any campaign: `[first-fire] campaign.patch.wl-campaign-dispatch.move.prefix`, `[first-fire] campaign.patch.wl-campaign-dispatch.move`, `[first-fire] campaign.patch.wl-campaign-dispatch.flush`, `[first-fire] campaign.patch.wl-campaign-dispatch.final-positions.prefix`, `[first-fire] campaign.patch.wl-campaign-dispatch.roll-up.prefix`, `[first-fire] campaign.patch.cic-faction-ai-guard.ship.prefix`, `[first-fire] campaign.patch.cic-faction-ai-guard.army-group.prefix`; no `[W&LCampaignDispatch]` warning.
2. W&L brigade or non-top division career, let the AI run until the player's corps moves: the career panel pops "Redeploy" / "Offensive" / "Engage Enemy" / "Offensive Operation" naming the corps commander (portrait), the zone mark at the corps' destination and vanilla's arrival prestige when the corps arrives (none for type 16, BUG-WL-002); `LogOutput.log` (any `Logging Profile`): `[W&LCampaignDispatch] trigger=vanilla-move type=5|6|7|16 unit=… issuer=… target=x,z dest=… zone=shown action=issue`. A queued move logs no second issue for the same target; under `Logging Profile = CampaignTuning` the campaign sidecar shows the skip `[W&LCampaignDispatch] … action=skip reason=rate-limit` (and `reason=queued-order-cancelled` when #25 cancels the corps' queued march). The player's brigade is never moved by Whiskey (only its corps moves: by vanilla, or by a Whiskey runtime as an AI asset, `reason=carrying-group`, which #69 dispatches the same way).
3. W&L corps (top-level) career where a Whiskey runtime picks the player's corps: `LogOutput.log` `[W&LCampaignDispatch] trigger=whiskey-order type=… action=issue` and the panel names the superior (`GetSeniorCommander`), not the player. The corps is never pre-pledged to the capital or counted in a defense package (`[DefenseIntent]` suppression `player-controlled`).
4. W&L CIC career: enemy builds ships (`new ship created … for alliance <enemy>` in vanilla's debug log / fleets grow) and forms army groups; log `[once:cic-faction-ai-guard:applied]`. The player's own faction still gets no ship/army-group AI.
5. Compliance: after a battle with dispatches, `[PlayerCompliance] scope=battle outcome=complied|missed reliability=…`; after a campaign dispatch to a top-level player, `scope=campaign`. The value survives a return from battle (the `samples` count keeps growing across the battle and the reload) and is written to the sidecar (`playerCompliance` object in `whiskeyrealism.json`, `careerKey` = `<commander id>:<campaign>/<sub>`, never the battlefield's level). Capital defense re-issued every AI pass to a top-level player adds one sample, not one per pass.
6. Battle grace calibration (`PlayerComplianceLedger.BattleGraceHours` = 0.5 battle hours): in a W&L battle, obey several zone orders promptly and note the battle time from the order panel to the zone; every one must log `[PlayerCompliance] scope=battle outcome=complied`. Any `outcome=missed` for an order reached promptly means the grace is too short for that order distance: record the times and raise it.

## Rollback

`[W&L] Enable Campaign Chain Dispatches = false`, `Enable Player Compliance Ledger = false`, `Enable CIC Faction AI Bug Guard = false`.

## Out of scope

Relation/prestige consequences (user: none); BUG-WL-002; offensive packages still count a map-group player as a `WlCurrentOrder` member (stage G planner).
