# Whiskey Realism — Master Handoff

**Read first at session start.** Single-page master plan across all workstreams. The canonical answer to "where are we, what's next, what's the long-term plan."

---

## At a glance

| | |
|---|---|
| **Current shipped version** | v0.1.0 — scaffold only, no patches registered |
| **Active workstream** | Slice A (strategic brain) — spec drafted, awaiting user review |
| **Repo** | [`3-Deacon/whiskey-realism-mod`](https://github.com/3-Deacon/whiskey-realism-mod) (public, MIT) |
| **Last updated** | 2026-05-02 |

---

## Slice roadmap

We design and ship **one slice at a time.** Each slice goes through: brainstorm → spec → plan → implement → ship. Estimated patch counts are rough — they get pinned during the spec phase.

| Slice | Spec | Plan | Implementation | Ship target | Est. patches | Notes |
|---|---|---|---|---|---|---|
| **A — Strategic brain** | drafted 2026-05-02 | not started | not started | v0.2.0 | ~10 | Replaces random objective picker; era × faction × officer personality system; ~12 triggered-scripted succession events; phased operational plans; monthly + event-triggered cadence; two-tier CIC + theater-commander hierarchy. **Awaits user spec review.** |
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
6. **Monthly + event-triggered cadence.** Strategic re-eval on the 1st of each game-month. Event triggers mark plans dirty; the next monthly tick processes the dirty bit.

**Architecture: Approach 3 — two-tier hierarchy.**

```
StrategicCoordinator (singleton, monthly tick)
    ├── CIC[CSA]    → TheaterCommander[ANV / AoT / TransMiss / Coast]
    └── CIC[Union]  → TheaterCommander[AoP / AoT / AoO / Coast / River]
```

**Two-tier conflict rule (load-bearing):** CICs decide *target + force level + deadline*. Theater commanders decide *route + tempo + tactical posture*. Plans are read-only to theater commanders; only CICs can abandon a plan.

**Read-only mod-state invariant:** Harmony patches READ mod state, never WRITE. State writes happen only on monthly tick + event-trigger handlers.

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

- **One concern per slice.** Don't open Slice B's spec until A ships.
- **One concern per file** for patches. Each Harmony patch class lives in its own `.cs` under `src/WhiskeyRealism/Patches/`.
- **Source-of-truth order:** shipped code > [`docs/patch-catalog.md`](patch-catalog.md) > per-patch design doc > umbrella slice spec > archived plan.
- **Stable patch ordinals.** `docs/patch-catalog.md` numbers each shipped patch sequentially. Withdrawn patches keep their ordinal with `(withdrawn)`. Stable across time and git history.
- **Per-slice retrospectives** land in `~/.claude/projects/-home-onebodyamerica-Projects-whiskey-realism-mod/memory/` (auto-memory) when a slice ships, not in this doc.

---

## What just shipped

`git log --oneline -20` is authoritative for chronology. This section trims to "what's worth knowing right now":

- **2026-05-02 — repo scaffolded** (commit `94863df`). BepInEx 5.4.21 + HarmonyX 2.10.2 + Unity 2021 refs. Build verified clean, 0 warnings, 0 errors.
- **2026-05-02 — strategic-brain design spec drafted** (commit `ce366ae`). 479 lines at `docs/superpowers/specs/2026-05-02-strategic-brain-design.md`. Awaiting user review before plan-writing.

---

## Next concrete action

User reviews [`docs/superpowers/specs/2026-05-02-strategic-brain-design.md`](superpowers/specs/2026-05-02-strategic-brain-design.md). On approval, invoke `superpowers:writing-plans` to produce the implementation plan for Slice A. On plan approval, execute via `superpowers:subagent-driven-development` with isolation worktrees per task (UBoatCrewMod precedent).
