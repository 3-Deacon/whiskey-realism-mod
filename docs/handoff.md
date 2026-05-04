# Whiskey Realism — Master Handoff

**Read first at session start.** Single-page master plan across all workstreams. The canonical answer to "where are we, what's next, what's the long-term plan."

---

## At a glance

| | |
|---|---|
| **Current shipped version** | **v0.2.1.1** — strategic-brain core verified end-to-end in-game on 2026-05-03. Tagged + GitHub Release with binary attached. |
| **Main branch current** | **v0.2.2 enrichment implemented, pushed, deployed, and smoke-confirmed through #17 + formation-directive ledger.** Release packaging/tagging is still pending. |
| **Active workstream** | Slice A v0.2.2 release prep and next enrichment: ObjectiveAdapter operating-area table, policy/recruitment/naval strategy, locked-Hard casualty tolerance, then deferred #7/#8 concrete steering. Task 7 offensive-safety Prefix is intentionally deferred until formation directives have more runtime soak. |
| **Repo** | [`3-Deacon/whiskey-realism-mod`](https://github.com/3-Deacon/whiskey-realism-mod) (public, MIT) |
| **Last updated** | 2026-05-04 |

---

## Slice roadmap

We design and ship **one slice at a time.** Each slice goes through: brainstorm → spec → plan → implement → ship. Estimated patch counts are rough — they get pinned during the spec phase.

| Slice | Spec | Plan | Implementation | Ship target | Est. patches | Notes |
|---|---|---|---|---|---|---|
| **A — Strategic brain** | shipped 2026-05-02 | shipped 2026-05-03 | **shipped + verified end-to-end 2026-05-03** | v0.2.1.1 released; v0.2.2 main enrichment implemented/smoked, release pending | 17 numbered + 2 persistence | v0.2.1.1 replaces random objective picker; era × faction × officer personality system; 12 triggered-scripted succession events with concrete `AssignCommando` swaps; phased operational plans; weekly strategic review + event-triggered dirty-plan cadence; two-tier CIC + theater-commander hierarchy; town/battle war-state observers; settings-lock patches (#10-#14) + sidecar persistence. Main v0.2.2 adds battle-history observer (#5), transfer/front-budget steering (#3), defensive ops (#4), historical army-area steering (#15), army-group steering (#16), grand-strategy objective/project steering (#17), and formation directives for independent divisions/corps/armies. #7/#8 remain reserved. |
| **B — Tactical brain** | deferred | — | — | v0.3.0 | ~8 | Macro-AI stance scoring (instead of per-battle preset from `aistrategies.dat`); reserve management; feud-system gating with `PerformAIActionDLCWL` (fixes brigade auto-charge bug); smarter charge gates; retreat thresholds; AdjustGroupAIStance personality input. Depends on A's TheaterCommander layer being live. |
| **C — W&L hierarchy AI** | deferred | — | — | v0.4.0 | ~6 | The player's CO actively gives orders (vs vanilla just-passive-presence); peer commanders act with their own competence + relations to player; hierarchy-aware order generation; officer-relations effects on compliance. Depends on B's stance system. |
| **D — Additional historical flavor** | deferred | — | — | v0.5.0 | ~5 | Foreign-recognition modeling (CSA AI weights Antietam-class victories higher when European recognition is in play); economic strangulation logic (Union AI weights river/coastal interdiction in '63+); public-morale modeling (CSA conserves forces post-Gettysburg to prevent collapse). Sits on top of A/B/C as a weighting layer. |
| **E — Community Hotfix supersession** | future / open | — | — | v0.6.0+ | ~5 | Long-term path to fold the existing Steam-distributed Community Hotfix mod's behavior fixes (officer auto-replace, recruitment ratios 80/7/13, weapon-range selection, AI passive morale recovery) into Harmony patches so users only need this one mod. Currently *incompatible* — Community Hotfix replaces Assembly-CSharp.dll wholesale. |

**Total target patch count across slices A-E:** ~34. Compare UBoatCrewMod at 108 catalog items as a reference for "how big a Mono Unity AI mod can grow."

### Why this order

1. Strategic before tactical — a smart driver going the wrong direction is still going the wrong direction. Random objective picker is the highest-impact root bug.
2. Tactical before W&L hierarchy — the tactical layer is what theater commanders steer. C builds on B's stance system.
3. Historical flavor (D) on top — needs A/B/C decision surfaces to flavor.
4. Community Hotfix supersession (E) last — only valuable once this mod's coverage exceeds Community Hotfix's so the supersession is a real upgrade.

---

## Slice A — current state (active)

**Six locked design choices** from the 2026-05-02 brainstorming session:

1. **Slice A only.** Tactical / W&L hierarchy / historical flavor are deferred slices.
2. **Tier 3 scope** — replace existing weak decisions + extend + net-new operational plans.
3. **Era × faction × officer personality system** — all three layers compose additively in a 5-dimensional personality space.
4. **Triggered-scripted officer succession** — ~12 canonical historical events gated on date AND war-state. Fire when conditions reasonably hold; alternate histories emerge in unusual campaigns.
5. **Phased operational plans** — 2-4 phases per plan. One active plan per side. Phases gate on target taken / engaged / deadline / force below threshold.
6. **Weekly + event-triggered cadence.** CIC strategic review runs on first valid date and each 7-day in-game bucket. Event triggers mark plans dirty; the next weekly review processes the dirty bit. Monthly remains only the visible heartbeat/checkpoint boundary.

**Architecture: Approach 3 — two-tier hierarchy.**

```
StrategicCoordinator (singleton, startup heartbeat + weekly strategic review)
    ├── CIC[CSA]    → TheaterCommander[ANV / AoT / TransMiss / Coast]
    └── CIC[Union]  → TheaterCommander[AoP / AoT / AoO / Coast / River]
```

**Two-tier conflict rule (load-bearing):** CICs decide *target + force level + deadline*. Theater commanders decide *route + tempo + tactical posture*. Plans are read-only to theater commanders; only CICs can abandon a plan.

**Read-only mod-state invariant:** Harmony patches READ mod state, never WRITE. State writes happen only on startup/weekly strategic review + event-trigger handlers.

**Startup sequencing invariant:** the immediate campaign-start heartbeat may run before vanilla `AICampaign.aifaction` exists. CIC/objective planning may run then, but front/army-area/formation-directive operational ledgers must defer until `aifaction` initializes. `MonthlyTickHookPatch` allows one same-day callback when that runtime appears so first operational analysis does not wait a month.

**Persistence:** JSON sidecar (`<savename>.whiskeyrealism.json`) next to the game's save.

Full spec: [`docs/superpowers/specs/2026-05-02-strategic-brain-design.md`](superpowers/specs/2026-05-02-strategic-brain-design.md).

---

## Slice B — backlog (deferred, pre-research notes)

Captured during the W&L AI investigation. Will refine during Slice B's own brainstorming when its turn comes.

| Bug / target | Decompile coordinate |
|---|---|
| Brigade auto-charge despite player initiative-off | `AIBattle.CheckForFeudGroupActions` line 4953 — does NOT call `PerformAIActionDLCWL` |
| Macro-AI stance is preset per-battlefield from `aistrategies.dat` (no scoring) | `AIBattle.CheckGlobalAIStrategy` line 6314 |
| Group stance ladder fixed by data | `AIBattle.AdjustGroupAIStance` line 4222 |
| Reserve assignment can magnetize into melee | `AIBattle.AssignReserves` line 7018 + `LinkReservesToLineGroup` line 6643 |
| Charge gates ignore commander personality | `AIBattle.MicroAICheckForCharges` line 4906 |
| Hardcoded 1% retreat-trigger casualty floor | `AIBattle.CheckGlobalAIStrategy` line 6314 (literal in body) |

**Data-side surface that may absorb part of Slice B:** `Config/battleprefs.txt` exposes `strengthtriggerforassault_micro[]`, `strengthtriggerfordefend_micro[]`, `strengthtriggerforscreening_micro[]`, `probfeudgroupmovement`, `chanceoffeuds*`, `neededdistancefeudgroupmovement`, `timetorenewaichargecheck`. ~30% of community-flagged tactical bugs are tunable from this file without touching the DLL.

---

## Slice C — backlog (deferred)

W&L hierarchy mechanics live in `DLC_WL` class (line 40337, 7,996 lines) + 6 satellite DTO classes (`DLCWL_Actions`, `DLCWL_Biography`, `DLCWL_Commander`, `DLCWL_HQ`, `DLCWLEnd`, `DLCWLEnd_HighlightLine`).

Key state to leverage:
- `DLC_WL.dlc_chosencommander` — player's chosen officer
- `DLC_WL.givenorder` — player's last given order (struct `GivenOrders`)
- `DLC_WL.IsCommanderInChief()` — whether player is CIC vs subordinate
- `DLCWL_Commander.commanderrelations` — officer-relationship system

Open questions for Slice C brainstorming:
- How does the player's CO currently decide what to order? (or does it?)
- How rich is the existing `commanderrelations` model? Does it influence anything besides UI?
- Can we synthesize "your CO is annoyed at you" → "your CO gives unfavorable orders" via existing relation field, or does this need new state?

---

## Slice D — backlog (deferred)

Historical flavor on top of A/B/C. Notes:

- Foreign recognition: GTCW tracks British/French diplomatic state in `Policy` / alliance system; AI doesn't currently weight battle outcomes by recognition impact.
- Economic strangulation: river/coastal blockades exist; vanilla AI doesn't prioritize them. Anaconda Plan emerges if Slice A's faction-theater preferences are right but D adds the *consciousness* of the strategy.
- Public morale curves: `Policy.morale` is tracked per-faction; AI currently doesn't respond.

---

## Conventions

- **One concern per slice.** Don't open Slice B's spec until Slice A v0.2.2 enrichment is smoke-cleared/released or the user explicitly redirects.
- **One concern per file** for patches. Each Harmony patch class lives in its own `.cs` under `src/WhiskeyRealism/Patches/`.
- **Source-of-truth order:** shipped code > [`docs/patch-catalog.md`](patch-catalog.md) > per-patch design doc > umbrella slice spec > archived plan.
- **Stable patch ordinals.** `docs/patch-catalog.md` numbers each shipped patch sequentially. Withdrawn patches keep their ordinal with `(withdrawn)`. Stable across time and git history.
- **Per-slice retrospectives** land in `~/.claude/projects/-home-onebodyamerica-Projects-whiskey-realism-mod/memory/` (auto-memory) when a slice ships, not in this doc.

---

## What just shipped

`git log --oneline -20` is authoritative for chronology. This section trims to "what's worth knowing right now":

- **2026-05-02 — repo scaffolded** (commit `94863df`). BepInEx 5.4.21 + HarmonyX 2.10.2 + Unity 2021 refs. Build verified clean, 0 warnings, 0 errors.
- **2026-05-02 — strategic-brain design spec drafted** (commit `ce366ae`). 479 lines at `docs/superpowers/specs/2026-05-02-strategic-brain-design.md`.
- **2026-05-03 — Slice A v0.2.0 built and committed.** Strategic-brain core (`PersonalityVector`, `Theater`/`Category`, `Phase`/`OperationalPlan`, `ObjectiveMetadata`, `FactionProfiles`, `EraStageManager`, `HistoricalFigureRegistry` with 25 officers + derived fallback, `ObjectiveAdapter`, `TheaterCommander`, `CIC` with Replan/Adjust/ReviewPlan, `SuccessionScheduler` with 12 canonical events, `StrategicCoordinator` singleton with player-CIC gate + initial monthly tick/heartbeat later superseded by weekly review in v0.2.2, `PersistenceDto` + sidecar serialization, `OnceLog` + `Reflection` utility helpers) and 9 Harmony patches:
  - **Behavior** (4): #1 `PickCampaignObjective` Prefix, #2 `ImportanceValues` Postfix, #6 `CommanderReplacement` Prefix gate-only, #9 `MonthlyTickHook` Postfix.
  - **Settings lock** (3): #10 `CampaignParametersLockPatch` (value lock at finalize), #11 `AggressivenessSliderLockPatch` (UI grey-out), #12 `HistoricCheckboxLockPatch` (UI grey-out + radio reversal). Gated by `OverrideVanillaSettings` config (default true). Difficulty intentionally left player-controlled.
  - **Persistence** (2): `AICampaignSaveLoadPatch.SavePatch` + `LoadPatch` Postfix pair on `AICampaign.Save`/`Load`.
- **2026-05-03 — BepInEx 5.4.23.5 x64 UnityMono installed in GTCW** (was player-side prerequisite). DLL deployed to `<GTCW>/BepInEx/plugins/WhiskeyRealism.dll`. Build verified clean (0 warnings, 0 errors).
- **2026-05-03 — v0.2.0 smoke-test verified.** All 8 active Harmony patches first-fire correctly; settings-lock subsystem visible in campaign-create menu (Aggressiveness/Difficulty sliders display "Locked:Realism"; Historic radio + 5 realism CBs frozen at half-alpha via `CheckBox.Freeze`); `[Heartbeat]` line appears on campaign creation; sidecar JSON round-trips through save/reload. Bugs caught + fixed during smoke-test cycle: BepInEx Config.Bind brackets, three reflection-signature mismatches, missing `GameVars.year` lookup, first-tick latch. See plan §"Bugs caught + fixed during execution" for full table.
- **2026-05-03 — v0.2.0 tagged + GitHub Release published.** Tag at `https://github.com/3-Deacon/whiskey-realism-mod/releases/tag/v0.2.0` with DLL attached.
- **2026-05-03 — v0.2.1 built and tagged.** Three commits closing the loop on v0.2.0's deferred behavior:
  - `3a429d8` — `ImportanceValuesPatch` (#2) redesigned. Postfix on `AIArea.CalculateMostValueableAIZones` overrides `mostvalueableaiareaclose[aifaction]` to point at the plan target.
  - `3d28b23` — `WarStateObserver` added. Town-ownership reads for Vicksburg / Chattanooga / Atlanta. Unlocks succession events #8 (Grant→GiC), #9 (Sherman→Western), #10 (Hood replaces Johnston).
  - `79a35ca` — Concrete commander-swap inside `CommanderReplacementPatch` (#6). When scheduler has un-applied scripted events, finds replacement Commander by name+alliance, displaces an army-group commander, calls `AssignCommando` + `DoCommanderPromotion`. Applied-event tracking persisted in sidecar.
- **2026-05-03 — v0.2.1.1 patch release.** Five fixes from in-game smoke-testing:
  - `1e5a1ef` — `GetAvailableObjectives(int, bool, int)` now passes `mintownobjectives=0` so abstract win-condition objectives pass through. W&L scenario "002" has more abstract than town-targeted objectives.
  - `4bcdd15` + `30b4391` — Diagnostic logging in `CIC.Replan` (`[CIC:diag]` line) — OnceLog'd snapshot of filter-pass counts + `Policy.CurrentChapter` + `ObjectiveChapters`. Surfaces the `IsDeactivated()` sub-gate when something rejects everything.
  - `ace7e87` — **Critical**: invoke `Policy.CheckForChapterUpdate()` at top of `OnMonthlyTick`. Without this, fresh campaigns fired our tick BEFORE vanilla's per-day cycle ran; `Policy.CurrentChapter` was still `-1` (init value); every CampaignObjective was deactivated; plans never built. For W&L scenario "002", `CheckForChapterUpdate` unconditionally sets `CurrentChapter=1`.
  - `6ce3a31` — `FilterMap.GetColorOnPos` takes `(Vector3, float = -1f)`, not single-arg. Reflection lookup in `ImportanceValuesPatch` was failing silently — plan-target zone override never ran.
- **2026-05-03 — v0.2.1.1 verified end-to-end.** Smoke-test log signatures confirmed:
  - All 8 patches first-fire on launch.
  - `[CIC:diag] alliance=0 ... Policy.CurrentChapter=1 ... not-deact=4 not-accomp=4` (chapter advance worked, objectives no longer deactivated).
  - `[Heartbeat] 1861-06 alliance=0 ... cic=Lincoln plan=phase1/2 obj=29` (plans actually built).
  - `[Heartbeat] 1861-06 alliance=1 ... cic=Davis plan=phase1/2 obj=1` (CSA plan too).
  - `[ObjectiveAdapter] geographic fallback for objective ID 29 → theater=East` (geographic resolution works for Town-targeted objectives).
  - All 12 succession events `APPLIED` end-to-end under test mode.
  - Sidecar JSON round-trips through save/reload.
- **2026-05-03 — v0.2.2 battle-history observer implemented + smoke-verified.** Local implementation adds #5 `BattleResultObserverPatch` on `BattleMonument.UpdateAllianceWon`, a persisted `BattleHistory` ring buffer on `StrategicCoordinator`, and battle-history/commander-state gates in `WarStateObserver`. `./build.sh` passes with 0 warnings / 0 errors. DLL deployed to `<GTCW>/BepInEx/plugins/WhiskeyRealism.dll` and verified by SHA-256 (`bc3f4935f60253733a2c611282eaf03e71b5833db15a7124ea865b82b944d9f3`). Smoke log confirmed `[once:battle-result]`, two `[BattleHistory]` entries (Phillippi East, Hampton Coast), dirty-plan events for both factions, and sidecar `battleHistory` persistence. Not yet tagged or released.
- **2026-05-03 — v0.2.2 transfer steering implemented + smoke-verified.** Local implementation adds #3 `TransferOfUnitsPatch` on `AICampaign.CheckTransferOfUnits`. It temporarily redirects vanilla `positiondeficit` to the active plan target only when the target is under-strength, lets vanilla queue/move units, then restores vanilla state in Postfix. Logging is bounded: `[once:transfer]` first-fire plus `[Patch:Transfer]` queue lines only when a transfer is queued; detailed steering math is behind `Verbose Logging`. `Plugin.cs` metadata now reports `v0.2.2` so restart logs identify this build. `./build.sh` passes with 0 warnings / 0 errors. DLL deployed to `<GTCW>/BepInEx/plugins/WhiskeyRealism.dll` and verified by SHA-256 (`5263d2312816c7a28619a80314f6fc8c4802841632a499580ccee33fea3ad11e`). Smoke log confirmed `dev.kyle.whiskey-realism v0.2.2 loaded`, `[once:transfer]`, and `[Patch:Transfer] alliance=0 obj=31 queued=1`.
- **2026-05-03 — v0.2.2 defensive-ops steering implemented + deployed.** Implementation adds #4 `DefensiveOpsPatch` on the actual decompile method `AICampaign.AssignUnitToDefendCapital` (older docs called this `CheckPickDefensiveOps`, but that method does not exist). It runs after vanilla capital-defense assignment, applies the CIC personality to the capital-defense strength gate, and can pledge one extra eligible defender while leaving movement/order execution to vanilla's next pass. Logging is bounded: `[once:defensiveops]` first-fire plus `[Patch:DefensiveOps]` only when an extra defender is assigned. `./build.sh` passes with 0 warnings / 0 errors. First-fire smoke was confirmed in the 2026-05-04 v0.2.2 run; assignment lines remain conditional.
- **2026-05-03 — v0.2.2 front-sector ledger implemented + deployed.** Implementation adds pure `Strategic/FrontSectorLedger.cs`, `StrategicCoordinator.Fronts[alliance]` snapshots via `FrontSectorRuntime`, and a transfer-budget guard inside #3 `TransferOfUnitsPatch`. This keeps plan-target concentration from stripping `Hold`/critical sectors unless the source sector is already an explicit `EconomyOfForce`/`Concede` posture. Logging is bounded: `[FrontLedger]` only when the posture summary changes (or verbose logging is enabled) and `[Patch:TransferBudget]` only once per block/concession signature. Console tests pass; `./build.sh` passes with 0 warnings / 0 errors. Runtime smoke was confirmed in the 2026-05-04 v0.2.2 run.
- **2026-05-03 — v0.2.2 historical army-area steering implemented + weekly cadence deployed.** Implementation adds pure `ArmyAreaDoctrine` / `HistoricalArmyAreaRegistry` / `ArmyAreaLedger`, runtime `ArmyAreaRuntime`, weekly `StrategicCoordinator.ArmyAreas[alliance]`, `WeeklyCadence`, and #15 `ArmyAreaTheaterPatch` on `AICampaign.UpdateCampaignTheaters`. This uses vanilla's existing `Regiment.theaterposition` + `AICampaign.MoveUnitTo` surfaces to nudge idle AI top formations back toward historical operating areas instead of replacing the campaign army/corps/division system. CIC plan review/replan, front ledger, and army-area ledger now run weekly; monthly remains only the visible heartbeat. Logging is bounded: `[ArmyArea]` only on assignment signature change, `[WeeklyOps]` only when verbose logging is enabled, and `[Patch:ArmyArea]` once per return-area correction. Console tests pass; `./build.sh` passes with 0 warnings / 0 errors. First-fire and ledger smoke were confirmed in the 2026-05-04 v0.2.2 run.
- **2026-05-03 — v0.2.2 historical army-group steering implemented + deployed.** Implementation adds pure `ArmyGroupDoctrine` and #16 `ArmyGroupManagementPatch` on `AICampaign.CheckArmyGroupManagement`. Vanilla army-group logic still runs first; the Postfix then groups committed top formations by historical operating command and uses vanilla `ArmyGroup.AddUnit`, `ArmyGroup.CreateNewArmyGroup`, and `ArmyGroup.AppointCommander`. Commander movement is deliberately gated: preferred historical commanders are appointed only if already commanding one of the grouped formations or currently unassigned, so the patch does not yank unrelated commands across the map. Logging is bounded: `[once:armygroup]` first-fire plus `[Patch:ArmyGroup]` only on create/attach/appoint. Console tests pass; `./build.sh` passes with 0 warnings / 0 errors. First-fire smoke was confirmed in the 2026-05-04 v0.2.2 run; create/attach/appoint lines remain conditional.
- **2026-05-03 — v0.2.2 grand-strategy objective/project steering implemented + deployed.** Implementation adds pure `StrategyTag`, `GrandStrategyProfile`, `GrandStrategyRegistry`, `ObjectiveStrategyTagger`, `ObjectiveScoring`, and `ProjectSelectionScorer`; moves `EraStage` into a pure source-linked file; extends `ObjectiveMetadata` with strategy tags; routes CIC objective scoring through the active Union/CSA grand-strategy profile; and adds #17 `ProjectSelectionPatch` Prefix on `AICampaign.UpdateProjects`. The project patch mirrors vanilla player/frame/personality/subsidy gates, seeds/replaces `nextprojecttoresearch[subsidy]` before vanilla spending from the current AI personality project list, and leaves funding/appointment to vanilla. Logging is bounded: `[once:project-selection]` after real gates and `[Patch:ProjectSelection]` only on replacement. Console tests pass; `./build.sh` passes with 0 warnings / 0 errors. Runtime smoke confirmed `[once:project-selection]` and replacement lines for both alliances in the 2026-05-04 v0.2.2 run.
- **2026-05-04 — v0.2.2 formation directives implemented + smoke-confirmed.** Main now includes pure `FormationLevel`, `FormationDirective`, `FormationSnapshot`, `FormationDirectiveLedger`, runtime `FormationDirectiveRuntime`, `StrategicCoordinator.FormationDirectives[alliance]`, and `OperationalStartupGate`. Independent top divisions (`unittyp == 14`, `istopunit`, strength floor) are first-class strategic formations. Attached divisions inherit parent posture and are not yanked independently. #15 `ArmyAreaTheaterPatch` now consults directives before direct area movement; #16 `ArmyGroupManagementPatch` can attach directive-qualified independent divisions but cannot create division-only groups. Task 7 offensive-safety Prefix was intentionally not implemented yet. Console tests pass (28 tests); `./build.sh` passes with 0 warnings / 0 errors. DLL deployed and SHA-256 verified (`14e4ef9d0cb2ff342c34daf26de775dbad65e3cf8496b5e2b5e5edf4fc8d2a39`). Runtime smoke confirmed `[once:weeklyops]`, `[FrontLedger]`, `[ArmyArea]`, `[FormationDirective]` summaries for both alliances, project steering for both alliances, battle-history observer, transfer-budget decisions, and sidecar save.
- **2026-05-04 — v0.2.2 non-spammy runtime diagnostics + startup sequencing fixed, pushed.** Commit `1ffb0da` surfaces silent front/army-area/formation-directive build skips and army-area/army-group missing-ledger conditions with `OnceLog.Warning`. Commit `ff2f4fa` keeps the immediate heartbeat but defers operational ledgers until vanilla `AICampaign.aifaction` initializes, then allows a same-day callback so analysis starts immediately once the runtime is ready. This removes expected scary startup warnings while preserving early heartbeat visibility. `origin/main` is synced at `ff2f4fa`; deployed DLL hash remains `14e4ef9d0cb2ff342c34daf26de775dbad65e3cf8496b5e2b5e5edf4fc8d2a39`.
- **2026-05-04 — v0.2.3 fiscal economy AI implemented locally.** Fiscal economy steering is implemented on branch `fiscal-economy-ai` through diagnostics/telemetry config. Console tests and `./build.sh` pass; DLL was deployed and SHA-256 verified (`1900ec1aa676b6265a07603397f3bd6ab1b538a5603f26071a25b7c617620723`). Runtime smoke was not performed in this Task 8 pass.

---

## Next concrete action

v0.2.1.1 shipped (tag + GitHub Release at https://github.com/3-Deacon/whiskey-realism-mod/releases/tag/v0.2.1.1). Strategic core verified working end-to-end in-game.

**v0.2.2 backlog** (see `docs/patch-catalog.md` §"Pending"):

1. **ObjectiveAdapter operating-area table** — the old raw x/z theater fallback is too crude. Use the W&L objective/coordinate anchors captured in [`docs/superpowers/plans/2026-05-03-historical-army-areas-implementation.md`](superpowers/plans/2026-05-03-historical-army-areas-implementation.md): IDs 3, 4, 17, 29-37 and named targets like Richmond, Washington, Winchester, Hagerstown, Harrisburg, Norfolk, New Bern, Plymouth, Saltville. Western/Gulf coordinates need full-map runtime capture.
2. **Policy/recruitment/naval grand-strategy integration** — design captured at [`docs/superpowers/specs/2026-05-03-grand-strategy-and-research-tree-design.md`](superpowers/specs/2026-05-03-grand-strategy-and-research-tree-design.md). Objective tags and project steering are implemented and smoke-confirmed; remaining surfaces are policy picks, recruitment intent, and naval preparation beyond project choice. Verified anchors: `Policies.CheckAIPolicyChange(int alliance)` (policy AI walks ordered `AIPersonality.policies`), `AICampaign.ZoneRecruiting`, `AIArea.GetBestRecruitingState`, `AICampaign.CheckShipConstruction`, `AICampaign.CheckFleetMovements`, `AICampaign.CheckForDefensiveOperations`, `AICampaign.CheckOffensiveMovements`, `AICampaign.CheckTransferOfUnits`, `AICampaign.UpdateCampaignTheaters`, `AICampaign.CheckArmyGroupManagement`, `AIFaction.TransferData`, `ArmyGroup`, `Config/policies.dat`, `Config/projects.dat`, `Config/shiptypes.dat`, and `Campaigns/002/A/aipersonalities.dat`.
3. **Concrete steering for `PerkSelectionPatch` (#7), `RecruitmentPatch` (#8)** — still reserved / deferred from v0.2.0. #8 now has a concrete intended shape: scoped `AIArea.GetBestRecruitingState` Postfix active only during `ZoneRecruiting`, driven by `RecruitmentIntentLedger` so CSA/Union recruitment supports the grand-strategy/front posture without corrupting raid or sea-invasion recruitment. Task 7 offensive-safety Prefix from the formation-directive plan is not #7 and remains deferred until more directive runtime soak.
4. **Succession gate long-run smoke** — #5 battle-history observer is verified for recording/persistence. Still needs a longer campaign run to confirm date-gated succession events #1, #3, #4, #5, #6, #11, #12 fire from observed game state without test-mode bypass. **Verified anchors (do NOT use `aifaction[].history` — it doesn't exist):**
    - `BattleMonument.UpdateAllianceWon(...)` at decompile line 76496 — final result hook with `_alliancewon`, `_battleresulttype`, `_battleendtype`, `_casualties`, `_commanderskia0/1`, `_commanderfame`, `_commanderdefamed`, `_battlename`, `_position`. #5 Postfix records final land-battle outcomes into `StrategicCoordinator.BattleHistory`.
    - `ImportExportUnitData.ExportBattleResultData(...)` at line 69670 — same data set, called from a different code path; useful as a fallback hook.
    - `BattleUnits.SaveBattleResult()` at line 83625 — returns a `LoadingScreen.BattleResult` struct; called at battle exit.
    - The `BattleMonument` class itself (line 76393) is a `MonoBehaviour` whose instances persist as historical markers on the campaign map; could be enumerated for retrospective queries.
    - From recorded battles, derive: ANV-lost-major-battle (CSA East-theater major defeat), AoP-failed-offensive (two Union East-theater defeats), Burnside-first-defeat (Burnside commands losing side), Lee-invading-Pennsylvania (current Lee unit position via `Commander.currentcommand.transform.position` against state boundary), WesternMajorDefeat (CSA West/River/TransMiss major defeat), ValleyOpsNeeded (Union East-theater defeat proxy), WarClearlyLost (CSA morale below 30% OR Atlanta threatened plus Western major defeat).
5. **Vanilla settings → mod logic integration** — route the locked-Hard `usedcampaignbonus` into `CIC.Effective` to scale `CasualtyTolerance`. Currently the lock is informational only.
6. **`Policy.CurrentChapter` integration** — vanilla's 5-chapter system overlaps with our 4-stage `EraStage`. Map our era stages onto vanilla chapters (or retire `EraStage`). For W&L scenario "002", chapter transitions are: 1 (start), 2 (after 1862-11-05), 3 (after 1864-11-09 if objective 26 accomplished AND 27 not). Decompile reference: `Policy.CheckForChapterUpdate` at line 211604.

**Constraints observed and documented during v0.2.1 smoke-testing:**

- **Fresh-campaign chapter timing.** `Policy.CurrentChapter` is `-1` until vanilla's `Policy.CheckForChapterUpdate()` runs (per-day cycle). v0.2.1.1 invokes it at the top of `OnMonthlyTick` so we don't read stale state. **Don't undo this.**
- **Fresh-campaign AI runtime timing.** The first valid date can arrive before `AICampaign.aifaction` exists. Keep the immediate heartbeat, but do not build front/army-area/formation-directive ledgers until `aifaction` is non-null/non-empty. `OperationalStartupGate` exists so the same in-game day can notify again once vanilla AI initializes.
- **Vanilla saves go to game install dir, not persistentDataPath.** `SceneManagement.SaveAll` calls `Directory.CreateDirectory("Campaigns/<level>/<sublevel>/<save>/")` with a relative path — CWD-resolved. Our sidecar uses the same convention.
- **`BattleUnits.armygroups` is null/empty until AI promotes someone.** Test mode falls back to any-commander displacement for forced succession. Real mode now has #16 `ArmyGroupManagementPatch`, which can create a historically coherent ArmyGroup from committed top formations once the weekly army-area ledger identifies at least two eligible units in the same operating command.
- **W&L scenario "002" has fewer town-targeted objectives than main "001"** — most are abstract win-conditions. We pass `mintownobjectives=0` to `GetAvailableObjectives` to let them through.
- **Many vanilla method signatures have default-valued tail parameters.** Reflection lookup must include them (`new[] { typeof(int), typeof(bool), typeof(int) }` not `new[] { typeof(int) }`). See `docs/findings.md` once it gets a "reflection-signature gotchas" section.
