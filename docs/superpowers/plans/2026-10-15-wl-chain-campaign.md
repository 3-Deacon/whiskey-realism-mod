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
| F-D5 | Bridge and the subordinate's parent group | Unchanged: Whiskey never moves the player's parent group (it carries the player's command); vanilla does, and trigger 1 tells the player. Open question to the user. |
| F-D6 | Prestige for subordinate dispatches | Vanilla-identical emission (zone shown, `CheckDLCWLTopics` unchanged). A subordinate is carried into the zone, so it earns the arrival prestige (5: 150, 6: 100, 7: 120, 16: 0) without acting. Open question to the user; the alternative is `showzone: false` (informational, no zone, no prestige). |
| F-D7 | Compliance signal | Vanilla's `GivenOrders.firstunitreached` on the tracked order object; only orders the player decides (campaign: own map group; battle: orders with a zone). |
| F-D8 | Ledger lifetime | Keyed by career (commander id + level/sublevel), not reset by campaign reloads (a battle round-trip reloads the campaign), saved in the sidecar, adopted on load only when the session has no samples for that career. |
| F-D9 | §F flag scope | One-shot: the first `IsCommanderInChief()` call after arming is the guard in both bodies; nested calls keep the true value. Disarm in a `Priority.First` Postfix and a Finalizer. #16's repeat of the guard is made alliance-aware. |
| F-D10 | Patch-written state | The dispatcher's pending queue and dedupe and the compliance observations are W&L dispatch bookkeeping written by event handlers (the #69 Postfixes, as #62 writes its shadow) and the battle coordinator tick. No strategic plan state is written from a patch. |

## Tasks

### F1 — Pure policy: `Strategic/WlCampaignDispatchPolicy.cs` (tests `WlCampaignDispatchTests`)

- `IsTrigger(in WlMoveFacts)`: W&L active, has command, not CIC, player not its own campaign group, moved == campaign group, moved not player-owned, not moved by player, move succeeded, alliance == player alliance.
- `ClassifyType(in WlMoveContext)`: defensive after → 7; capital after → 8; offensive after: before → 6, new → 16 if enemy at target else 5; otherwise 5.
- `Decide(candidate, lastEmission, currentOrder, nowDays, moveDistance)`: skip `no-op-move` (< 20), `active-duplicate` (current order same session as last emission, same type, within 110), `rate-limit` (last emission within 110 and < 1 day); else issue.
- Red first: each rule, alliance/empty inputs, queued second call deduped, different target re-issues, next day re-issues.

### F2 — Runtime + patch #69

- `Strategic/WlCampaignDispatchRuntime.cs`: `OnMoveUnitTo(unit, destination, result)` (facts + list membership before; bounded pending list, 16), `Flush()` (classify, name/target for 7/8, `Emit`), `EmitForPlayerCommand(...)` (trigger 2), `Emit(...)` (policy + vanilla-identical build, F-D4 issuer). Dedupe state via `CampaignScope.Register`. Telemetry line `[W&LCampaignDispatch] trigger=… type=… unit=… issuer=… target=… action=issue|skip reason=…` through `OnceLog`-bounded signatures (one line per distinct type+target+action).
- `Patches/WlCampaignMoveDispatchPatch.cs` (#69): Postfix `AICampaign.MoveUnitTo` (`campaign.patch.wl-campaign-dispatch.move`), Postfix private `AICampaign.UpdateUnitAI` (`campaign.patch.wl-campaign-dispatch.flush`). Both fire in every campaign session (first-fire marked before the W&L test).
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
- `Patches/CicFactionAiBugGuardPatch.cs` (#70): Prefix/Postfix(Priority.First)/Finalizer on `CheckShipConstruction` and `CheckArmyGroupManagement`; Postfix on `DLC_WL.IsCommanderInChief`. Scope ids `campaign.patch.cic-faction-ai-guard.ship.*`, `.army-group.*`, `.is-cic` (situational: W&L only). One `[once:cic-faction-ai-guard:applied]` info line the first time the guard lets an enemy faction past.
- `ArmyGroupDoctrine.ArmyGroupsAllowed` takes `playerIsCicOfThisAlliance`; #16 passes `IsCommanderInChief() && alliance == CIC alliance`.
- Config `[W&L] Enable CIC Faction AI Bug Guard` (default true). `docs/bug-fixes` `BUG-WL-001` → In progress (#70).

### F6 — Docs

`docs/patch-catalog.md` (#69, #70; next #71), `docs/wl-player-model.md` (campaign map facts, candidate bug moved to tracked), `docs/wl-dispatch-objective-bridge.md`, `docs/wl-player-order-doctrine.md` (campaign side), `docs/telemetry.md`, `docs/handoff.md`, `docs/bug-fixes/README.md` + `vanilla-wl-career.md`, `tools/smoke-situational.txt`.

## As built (2026-10-15)

- F5 landed first (commit "fix(wl): CIC career …"): `CicFactionAiGuard` + #70; `ArmyGroupDoctrine.ArmyGroupsAllowed(allow, allianceAllows, vanillaCicGuardBlocks)`. Harness 1922.
- F1-F4 + F6: `WlCampaignDispatchPolicy`/`Runtime`, #69, bridge (gated path, campaign `PlayerOrderDedupe` shadow, campaign context key and their tests deleted; `PlayerOrderIntent.DefendCapital/BuildFort/BuildSupplyDepot` and the campaign-candidate branch of `PlayerOrderDedupe.PassesScopePolicy` deleted with their tests, since only the bridge produced them), `PlayerComplianceLedger`/`Runtime`, sidecar `playerCompliance`, `ArmyEvidenceBuilder` weight, coordinator tick hook. `WlCampaignDispatchRuntime` is compiled into the harness (the bridge and `CoordinatedOperationRuntime` are), so `TestPluginLogStub` gained `Plugin.Instance.Enabled` and `EnableCampaignChainDispatches`. Terrain height is read by reflection (the mod does not reference `UnityEngine.TerrainModule`). Harness 1944 (32 new, 10 deleted with the deleted code).
- Red first: `WlCampaignDispatchTests` (16, compile-red), `PlayerComplianceTests` (16, compile-red incl. the 2 sidecar tests), `CicFactionAiGuardTests` (5) and the `ArmyGroupsAllowed` contract (compile-red).

## Gates (before each commit)

`./build.sh` 0 warnings; test build 0 warnings; harness exit 0 + PASS count; `python3 -I tools/config-audit.py .` 0; `python3 -I tools/orphan-sweep.py .` 0 and `--include-test-only` adds nothing; `python3 -I tools/test_smoke_check.py` OK; `git log --oneline -10 && git status`.

PROVE-MODE: harness for the pure units (`WlCampaignDispatchTests`, `PlayerComplianceTests`, `CicFactionAiGuardTests`, the bridge classification tests); build + static gates for the runtime and patches; in-game proof is the smoke below after the integrator deploys (no deploy in this slice).

## Smoke items (exact log text)

1. Any campaign: `[first-fire] campaign.patch.wl-campaign-dispatch.move`, `[first-fire] campaign.patch.wl-campaign-dispatch.flush`, `[first-fire] campaign.patch.cic-faction-ai-guard.ship.prefix`, `[first-fire] campaign.patch.cic-faction-ai-guard.army-group.prefix`; no `[W&LCampaignDispatch]` warning.
2. W&L brigade or non-top division career, let the AI run until the player's corps moves: the career panel pops "Redeploy" / "Offensive" / "Engage Enemy" / "Offensive Operation" naming the corps commander (portrait), the zone mark at the corps' destination; log `[W&LCampaignDispatch] trigger=vanilla-move type=5|6|7|16 ... action=issue`. A queued move logs no second issue for the same target (`reason=rate-limit`). The player's brigade is never moved by Whiskey (only its corps moves, by vanilla).
3. W&L corps (top-level) career where a Whiskey runtime picks the player's corps: `[W&LCampaignDispatch] trigger=whiskey-order ... action=issue` and the panel names the superior (`GetSeniorCommander`), not the player.
4. W&L CIC career: enemy builds ships (`new ship created … for alliance <enemy>` in vanilla's debug log / fleets grow) and forms army groups; log `[once:cic-faction-ai-guard:applied]`. The player's own faction still gets no ship/army-group AI.
5. Compliance: after a battle with dispatches, `[PlayerCompliance] scope=battle outcome=complied|missed reliability=…`; after a campaign dispatch to a top-level player, `scope=campaign`. The value survives a return from battle and is written to the sidecar (`playerCompliance` object in `whiskeyrealism.json`).

## Rollback

`[W&L] Enable Campaign Chain Dispatches = false`, `Enable Player Compliance Ledger = false`, `Enable CIC Faction AI Bug Guard = false`.

## Out of scope

Relation/prestige consequences (user: none); Whiskey moving the subordinate player's parent group (F-D5); BUG-WL-002.
