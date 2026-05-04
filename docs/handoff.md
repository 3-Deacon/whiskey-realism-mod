# Whiskey Realism — Master Handoff

**Read first at session start.** Single-page master plan across all workstreams. The canonical answer to "where are we, what's next, what's the long-term plan."

---

## At a glance

| | |
|---|---|
| **Current shipped version** | **v0.2.2** — Slice A enrichment through #22 verified, tagged, and released on 2026-05-04. |
| **Main branch current** | **Post-v0.2.2 checkpoint `a0cf709`.** Built and deployed DLL SHA-256 is `3b0423317bd62c78062bf62929dee6e6432dbda029e865196b82c9eaa88c630d`. Adds locked-Hard casualty tolerance, #7 role-aware campaign perk steering, and a hardened #22 W&L command-selection retry on top of the released v0.2.2 smoke-confirmed build. Runtime smoke confirmed the W&L picker works, command selection clears the gate, and campaign time advances into normal systems with no #22 errors. |
| **Active workstream** | Post-v0.2.2 planning: deeper construction/fort doctrine, full-map coordinate capture, succession long-run smoke, Policy.CurrentChapter/EraStage cleanup, and any naval runtime patches proven necessary by smoke. Task 7 offensive-safety Prefix is intentionally deferred until formation directives have more runtime soak. |
| **Repo** | [`3-Deacon/whiskey-realism-mod`](https://github.com/3-Deacon/whiskey-realism-mod) (public, MIT) |
| **Last updated** | 2026-05-04 |

---

## Slice roadmap

We design and ship **one slice at a time.** Each slice goes through: brainstorm → spec → plan → implement → ship. Estimated patch counts are rough — they get pinned during the spec phase.

| Slice | Spec | Plan | Implementation | Ship target | Est. patches | Notes |
|---|---|---|---|---|---|---|
| **A — Strategic brain** | shipped 2026-05-02 | shipped 2026-05-03 | **shipped + verified end-to-end 2026-05-04** | v0.2.2 released; post-release main continues Slice A cleanup | 22 numbered + 2 persistence | v0.2.1.1 replaces random objective picker; era × faction × officer personality system; 12 triggered-scripted succession events with concrete `AssignCommando` swaps; phased operational plans; weekly strategic review + event-triggered dirty-plan cadence; two-tier CIC + theater-commander hierarchy; town/battle war-state observers; settings-lock patches (#10-#14) + sidecar persistence. v0.2.2 adds battle-history observer (#5), transfer/front-budget steering (#3), defensive ops (#4), recruitment state steering (#8), historical army-area steering (#15), army-group steering (#16), grand-strategy objective/project steering (#17), fiscal economy/construction steering (#18/#20), policy/naval grand-strategy timing (#19), fast-forward catch-up (#21), W&L command-selection retry (#22), and formation directives for independent divisions/corps/armies. Post-release main fills #7 with role-aware campaign army/fleet perk steering, routes locked-Hard difficulty into `CIC.Effective`, and smoke-confirms the hardened W&L picker retry/time-advance path. |
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

**W&L command-selection invariant:** vanilla calls `CareerInformationPanel.ShowStartUnitSelectionList()` once at campaign frame 50, then pauses. `WlCareerStartSelectionRetryPatch` retries that same vanilla popup call while `WlCareerStartGate` says the player has no command, throttled by Unity frame count after `GameVars.frame >= 50`. Do not invoke the picker before frame 50; the panel can exist but its dependent list/OOB state is not fully safe yet. `CampaignController.Update` is the primary anchor because it owns the `careerinformationpanel` field; `AICampaign.Update` can fire too early and only logs a fallback no-panel warning. If this regresses, look for `[W&LStartSelection]` lines or one-time `wl-start-selection:*` warnings in `BepInEx/LogOutput.log`.

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
- **2026-05-04 — fiscal economy AI implemented, merged to main, and deployed.** Fiscal posture/intent logic, telemetry/config gates, #18 `FinancialAIPatch`, and #20 `EconomyConstructionPatch` are on `main`. The AI now treats credit rating, treasury/debt pressure, supply protection, force-cost pressure, and CSA/Union asymmetry as explicit intent before nudging tax/subsidy/construction choices. Logging is non-spammy: posture signatures and telemetry only on configured heartbeat/signature change, plus bounded lane-correction lines. Console tests and `./build.sh` pass; earlier fiscal-task DLL was deployed and SHA-256 verified (`0ecd07a095b31fc440862d4ad88bd6a7194005405cdd7ea02fa3a1516b044af3`). Current deployed DLL hash is superseded by the latest checkpoint below.
- **2026-05-04 — default-on fast-forward AI catch-up implemented and deployed.** Adds pure `FastForwardAiScheduler` and #21 `FastForwardAiCatchUpPatch` on `AICampaign.Update`. Vanilla campaign speed tiers are `1x/5x/20x/50x`, but vanilla only runs `floor(sqrt(gamespeed))` `UpdateUnitAI` passes per frame; the new patch adds bounded extra passes at 20x/50x under a configurable frame budget so strategic jobs fall less far behind calendar time. Logging is non-spammy: `[once:fast-forward-ai]` confirms the patch is wired, and `[Patch:FastForwardAI]` logs the first actual catch-up sample plus any speed/pass/budget signature change. Console tests and `./build.sh` pass; runtime smoke was confirmed in the v0.2.2 release run.
- **2026-05-04 — ObjectiveAdapter operating-area table implemented and deployed.** Adds pure `ObjectiveCatalog` metadata for W&L objective IDs 3, 4, 17, and 29-37, then routes `ObjectiveAdapter` table hits through it before geographic fallback. Known Richmond/Washington, Mississippi River, West Virginia, Shenandoah/B&O, Maryland/Pennsylvania, Coastal NC, Saltville, and Norfolk-area objectives now carry explicit theater/category/strategy tags for objective scoring and later recruitment/economy steering. Console tests and `./build.sh` pass; remaining objective work is coordinate capture for western/gulf objectives, not a generic first-fire gate.
- **2026-05-04 — #8 recruitment state steering implemented and deployed.** Adds pure `RecruitmentIntentLedger` plus `RecruitmentPatch`: a scoped `ZoneRecruiting` context and `AIArea.GetBestRecruitingState` Postfix. The patch only changes the selected state while vanilla recruitment is already running and only to candidates that satisfy recruitable/support/pool gates; it does not alter unit type, group creation, or raid/sea-invasion recruiting. Console tests and `./build.sh` pass; runtime smoke was confirmed in the v0.2.2 release run.
- **2026-05-04 — policy/naval grand-strategy policy timing implemented and deployed.** Adds pure `GrandStrategyPolicyScorer` and wires #19 `PolicySelectionPatch` to combine fiscal intent with the active `GrandStrategyProfile`. Union early policy timing now favors blockade setup (`Arming Civilian Ships`, `Legal Blockade`) over generic enrollment when the margin is clear. CSA early policy timing favors King Cotton, Free Trade, Organized Blockade Running, Letters of Marque, and diplomacy/recognition over naval parity, while fiscal force/cost suppression remains dominant. Logging remains non-spammy: one `[Patch:PolicySelection]` line only when the patch starts, replaces, or blocks policy research. Console tests and `./build.sh` pass; release smoke saw no errors, but an actual policy replacement line remains conditional on vanilla policy timing.
- **2026-05-04 — startup lag root cause investigated and fixed.** Early commits and current code both had reflection-heavy hot paths: `AICampaign.Update` readiness checks and `MainMenu.CheckForCheckBoxUpdates` settings locks. Commit `c4361f5` caches W&L/startup fields, `AICampaign.aifaction`, checkbox fields/methods, and `GameVars` setting fields; #12/#14 now early-return when the forced setting state is already locked. The user smoke-confirmed this fixed the lag issue. Functionality is intended to remain identical: same forced Historic/realism/automanage settings and same W&L strategic deferral, just without repeated reflection and repeated `Check()`/`Freeze()` calls.
- **2026-05-04 — W&L command-selection prompt regression fixed.** The lag fix exposed a vanilla timing race: `CampaignController.Update` calls `careerinformationpanel.ShowStartUnitSelectionList()` once at game frame 50, then pauses with `GameVars.SetGameSpeed(0f)`. The first retry attempt (`933eb4a`) incorrectly waited for game frame 55, which never arrived in the stuck paused state. Commit `df5aa28` adds #22 `WlCareerStartSelectionRetryPatch`: while `WlCareerStartGate` says the player has no command, retry the same vanilla popup call from game frame 50, throttle with Unity `Time.frameCount`, stop when the unit-selection list is visible or the player receives a command, and log one useful `[W&LStartSelection]` status line plus one-time boundary warnings. The user smoke-confirmed the command-selection popup is fixed.
- **2026-05-04 — v0.2.2 release checkpoint.** Current release DLL was rebuilt, deployed, and SHA-256 verified (`4c3a2966256b1fc498a66bfad511d956d6a541436f3ed8001ba3cb2638b1c7f5`). Console tests pass; `./build.sh` passes with 0 warnings / 0 errors. Runtime smoke confirmed W&L command selection, weekly ops, fiscal intent, financial AI, construction patch, recruitment patch, project steering, transfer budget guard, formation directives, battle-history observer, and fast-forward catch-up with no repeated warnings/errors.
- **2026-05-04 — locked-Hard casualty tolerance integrated and deployed.** `CIC.Effective(...)` now applies a small historical-Hard casualty-tolerance modifier (`+0.10`) when Whiskey's vanilla-settings override is enabled and `Locked Difficulty = 3`. This keeps the campaign on the intended historical-Hard setting without turning difficulty into a player-adjustable hidden personality scale. Console tests pass; `./build.sh` passes with 0 warnings / 0 errors. DLL deployed and SHA-256 verified (`552f2319d396dd95f97b65c82c3b80f4c1f3c594e3ded7b0a3316fd48d0b24cc`).
- **2026-05-04 — #7 role-aware campaign perk steering implemented and deployed.** Vanilla `AICampaign.CheckPerkSelection` randomly selected army and fleet perks. `PerkSelectionPatch` now mirrors vanilla eligibility, preserves the W&L player-subordinate army skip, scores army perks by directive/objective role and fleet perks by faction naval strategy, and still calls `Regiment.ChoosePerk` for assignment. Console scorer tests cover siege, raid, Union blockade, CSA raiding, and unavailable-candidate handling. DLL deployed and SHA-256 verified (`5852e56aaa613aa636767fb96d75546f3ef4ee8ed1b99c016aff2a16483ec29b`).
- **2026-05-04 — #22 W&L command-selection retry hardened and deployed.** A later smoke run showed `StrategicCoordinator` detecting W&L career-start pending with no `[W&LStartSelection]` retry first-fire line. Root cause: #22 still waited for `GameVars.frame >= 50`, but the startup path can pause before that campaign frame advances. The retry now uses a pure `WlStartSelectionRetryGate` driven only by Unity `Time.frameCount`, logs one active first-fire line as soon as pending is observed, allows up to 120 bounded attempts, and keeps one-time no-panel/no-method/max-attempt warnings. Console tests pass; `./build.sh` passes with 0 warnings / 0 errors. DLL deployed and SHA-256 verified (`f1f456bd273b72e0640232cbee5b8a6b761c3fb77b14e4dc6b1cd614e2bcff6a`).
- **2026-05-04 — #22 W&L command-selection retry moved to CampaignController and deployed.** Live log then showed `[once:wl-start-selection]` followed by `[once:wl-start-selection:no-panel]` from the `AICampaign.Update` anchor: the retry patch was loaded, but it fired before `UI/CareerInformation` existed and did not get another usable callback. #22 now also patches `CampaignController.Update`, reads its private `careerinformationpanel` field, and uses that as the primary retry source while keeping `AICampaign.Update` as fallback. The retry gate no longer consumes attempts when the panel is unavailable. Console tests pass; `./build.sh` passes with 0 warnings / 0 errors. DLL deployed and SHA-256 verified (`b5f86e5cdb4ef669a25ca6adfb406da343b100344345aa423d2df0e02b31075e`).
- **2026-05-04 — #22 W&L command-selection retry ready-frame gate added and deployed.** Live log then showed the CampaignController retry invoking `ShowStartUnitSelectionList` before vanilla frame 50 and throwing a reflected `TargetInvocationException`, leaving the picker visible but unusable and the campaign paused. #22 now requires `GameVars.frame >= 50` before consuming a retry attempt, preserving the CampaignController panel anchor but waiting until vanilla's own ready frame. Reflection errors now log inner exception type/message. Console tests pass; `./build.sh` passes with 0 warnings / 0 errors. DLL deployed and SHA-256 verified (`3b0423317bd62c78062bf62929dee6e6432dbda029e865196b82c9eaa88c630d`).
- **2026-05-04 — #22 W&L command-selection retry smoke-confirmed.** User confirmed the command picker flow worked. Fresh log confirmed no `[W&LStartSelection] retry failed`, no `TargetInvocationException`, no no-panel warning, and normal campaign progression after command selection: weekly ops, battle history, project steering, fiscal/financial AI, recruitment, transfer budget, fast-forward catch-up, and sidecar saves all fired. Current pushed/deployed main is `a0cf709`; SHA-256 remains `3b0423317bd62c78062bf62929dee6e6432dbda029e865196b82c9eaa88c630d`.

---

## Next concrete action

v0.2.2 shipped (tag + GitHub Release at https://github.com/3-Deacon/whiskey-realism-mod/releases/tag/v0.2.2). Strategic core verified working end-to-end in-game. Current pushed/deployed main is `a0cf709` and also includes post-release locked-Hard casualty-tolerance integration, #7 role-aware campaign perk steering, and hardened #22 W&L command-selection retry. The W&L picker/time-advance path is runtime smoke-confirmed; #7 perk assignment remains conditional on vanilla perk timing.

**Post-v0.2.2 backlog**:

1. **Construction/fort doctrine design** — vanilla construction deep dive is documented in `docs/superpowers/specs/2026-05-04-construction-vanilla-deep-dive.md`. Current #20 biases vanilla-valid private-economy candidates only; it does not score exact sites and does not affect supply depots, forts, telegraphs, or railroads. Next building slice should add a `ConstructionIntentLedger` before any direct fort/depot/telegraph/rail patches.
2. **Full-map runtime coordinate capture** — objective table now covers known W&L eastern/coastal/river IDs. Still capture Vicksburg, Memphis, Baton Rouge, New Orleans, Louisville, St. Louis, Chattanooga, Atlanta, Nashville, and Corinth before relying on western/gulf objective metadata.
3. **Succession gate long-run smoke** — #5 battle-history observer is verified for recording/persistence. Still needs a longer campaign run to confirm date-gated succession events #1, #3, #4, #5, #6, #11, #12 fire from observed game state without test-mode bypass. **Verified anchors (do NOT use `aifaction[].history` — it doesn't exist):**
    - `BattleMonument.UpdateAllianceWon(...)` at decompile line 76496 — final result hook with `_alliancewon`, `_battleresulttype`, `_battleendtype`, `_casualties`, `_commanderskia0/1`, `_commanderfame`, `_commanderdefamed`, `_battlename`, `_position`. #5 Postfix records final land-battle outcomes into `StrategicCoordinator.BattleHistory`.
    - `ImportExportUnitData.ExportBattleResultData(...)` at line 69670 — same data set, called from a different code path; useful as a fallback hook.
    - `BattleUnits.SaveBattleResult()` at line 83625 — returns a `LoadingScreen.BattleResult` struct; called at battle exit.
    - The `BattleMonument` class itself (line 76393) is a `MonoBehaviour` whose instances persist as historical markers on the campaign map; could be enumerated for retrospective queries.
    - From recorded battles, derive: ANV-lost-major-battle (CSA East-theater major defeat), AoP-failed-offensive (two Union East-theater defeats), Burnside-first-defeat (Burnside commands losing side), Lee-invading-Pennsylvania (current Lee unit position via `Commander.currentcommand.transform.position` against state boundary), WesternMajorDefeat (CSA West/River/TransMiss major defeat), ValleyOpsNeeded (Union East-theater defeat proxy), WarClearlyLost (CSA morale below 30% OR Atlanta threatened plus Western major defeat).
4. **`Policy.CurrentChapter` integration** — vanilla's 5-chapter system overlaps with our 4-stage `EraStage`. Map our era stages onto vanilla chapters (or retire `EraStage`). For W&L scenario "002", chapter transitions are: 1 (start), 2 (after 1862-11-05), 3 (after 1864-11-09 if objective 26 accomplished AND 27 not). Decompile reference: `Policy.CheckForChapterUpdate` at line 211604.
5. **Naval runtime patch only if smoke proves policy/project steering is insufficient** — candidate anchors remain `AICampaign.CheckShipConstruction`, `AICampaign.CheckFleetMovements`, `Config/projects.dat`, and `Config/shiptypes.dat`. Do not add a ship-construction or fleet-movement patch until policy/project steering has been observed in runtime.

**Constraints observed and documented during v0.2.1 smoke-testing:**

- **Fresh-campaign chapter timing.** `Policy.CurrentChapter` is `-1` until vanilla's `Policy.CheckForChapterUpdate()` runs (per-day cycle). v0.2.1.1 invokes it at the top of `OnMonthlyTick` so we don't read stale state. **Don't undo this.**
- **Fresh-campaign AI runtime timing.** The first valid date can arrive before `AICampaign.aifaction` exists. Keep the immediate heartbeat, but do not build front/army-area/formation-directive ledgers until `aifaction` is non-null/non-empty. `OperationalStartupGate` exists so the same in-game day can notify again once vanilla AI initializes.
- **Fresh W&L command-selection timing.** Vanilla tries the command-selection popup at campaign frame 50 and then pauses. Retry from `CampaignController.Update`, but do not invoke `CareerInformationPanel.ShowStartUnitSelectionList(true)` before `GameVars.frame >= 50`; invoking earlier can create a half-open, unusable picker. #22 is intentionally a retry of vanilla `CareerInformationPanel.ShowStartUnitSelectionList(true)`, not a custom command picker.
- **Vanilla saves go to game install dir, not persistentDataPath.** `SceneManagement.SaveAll` calls `Directory.CreateDirectory("Campaigns/<level>/<sublevel>/<save>/")` with a relative path — CWD-resolved. Our sidecar uses the same convention.
- **`BattleUnits.armygroups` is null/empty until AI promotes someone.** Test mode falls back to any-commander displacement for forced succession. Real mode now has #16 `ArmyGroupManagementPatch`, which can create a historically coherent ArmyGroup from committed top formations once the weekly army-area ledger identifies at least two eligible units in the same operating command.
- **W&L scenario "002" has fewer town-targeted objectives than main "001"** — most are abstract win-conditions. We pass `mintownobjectives=0` to `GetAvailableObjectives` to let them through.
- **Many vanilla method signatures have default-valued tail parameters.** Reflection lookup must include them (`new[] { typeof(int), typeof(bool), typeof(int) }` not `new[] { typeof(int) }`). See `docs/findings.md` once it gets a "reflection-signature gotchas" section.
