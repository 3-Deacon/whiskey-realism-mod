# Whiskey Realism — Master Handoff

Read first at session start. Single-page master plan across all workstreams.

## Current state — 2026-05-02

**v0.1.0 — scaffold only.**

What exists:
- Repo scaffolded: `~/Projects/whiskey-realism-mod/`, public on GitHub at `3-Deacon/whiskey-realism-mod`.
- Build skeleton compiles (BepInEx 5.4.21 + HarmonyX 2.10.2 from NuGet, refs/ symlinks to GTCW DLLs).
- `Plugin.cs` stub registers no patches yet — just logs a load message.
- Decompile of `Assembly-CSharp.dll` available at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (266k lines).
- Decompile coordinates for AI / strategy / tactical decision points captured in `docs/findings.md`.

What does NOT exist yet:
- Strategic-brain design spec (in progress — see "Next" below).
- Implementation plan.
- Any actual patch.
- BepInEx installed in the GTCW install (player prerequisite — not a mod-side concern).

## Next — Slice A: Strategic Brain

Six locked design choices (from brainstorming session 2026-05-02):

1. **Slice A — strategic brain only.** Tactical brain (Slice B), W&L hierarchy AI (Slice C), and additional historical flavor (Slice D) are deferred to later slices. One spec → one plan → one ship at a time.
2. **Tier 3 scope.** Replace existing weak decisions (random objective picker, etc.) + extend existing decisions + net-new behaviors (multi-month operational plans, officer appointment/firing logic, theater shifts).
3. **Era × faction × officer personality system.** All three layers compose additively in a 5-dimensional personality space.
4. **Triggered-scripted officer succession.** ~12 canonical historical events gated on date AND war-state. Fire when conditions reasonably hold; alternate histories emerge in unusual campaigns.
5. **Phased operational plans.** 2-4 phases per plan. One active plan per side. Phases gate on target taken / engaged / deadline / force below threshold.
6. **Monthly + event-triggered cadence.** Strategic re-eval on the 1st of each game-month. Event triggers (KIA, town loss, defeat) mark plans dirty; the next monthly tick processes the dirty bit. Adjust-current-plan by default; replan from scratch only when an event invalidates plan assumptions.

**Architecture: Approach 3 — two-tier hierarchy.**

```
StrategicCoordinator (singleton, monthly tick)
    ├── CIC[CSA]    → TheaterCommander[ANV / AoT / TransMiss / Coast]
    └── CIC[Union]  → TheaterCommander[AoP / AoT / AoO / Coast / River]
```

**Two-tier conflict rule:** CICs decide *target + force level + deadline*. Theater commanders decide *route + tempo + tactical posture*. Plans are read-only to theater commanders; only CICs can abandon a plan.

**State model:** `PersonalityVector` (5 floats `[-1, +1]`); `EraStage` (4 stages with date defaults + war-state overrides); faction profiles (5-d vector + theater-preference weights); ~25 hand-coded historical figures; ~12 succession events.

**Bridge layer:** ~10 Harmony patches (mostly Postfixes) into `AICampaign` / `AIBattle` decision points listed in `docs/findings.md`. Patches READ mod state, never WRITE — invariant.

**Persistence:** JSON sidecar (`<savename>.whiskeyrealism.json`) next to the game's save. Hooked via `SavesManager.Save` Prefix + `SavesManager.Loaded` event.

## Workstreams (roadmap)

| Slice | Status | Description |
|---|---|---|
| A — Strategic brain | spec in progress | Replaces vanilla random-objective picker; era × faction × officer; phased plans; succession |
| B — Tactical brain | deferred | Macro-AI stance scoring; reserve management; feud-system gating; smarter charge gates |
| C — W&L hierarchy AI | deferred | Player's CO behavior; peer-officer competence; hierarchy-aware orders |
| D — Additional historical flavor | deferred | Era-officer interaction depth; foreign-recognition modeling; public-morale curves |

## What just shipped

`git log --oneline` is authoritative. Per-slice retrospectives + lessons live at `~/.claude/projects/-home-onebodyamerica-Projects-whiskey-realism-mod/memory/` (auto-memory).
