# Operational Tempo Doctrine

Living reference for the strategic operational probe/contact loop. Source of truth remains shipped code first, then `docs/patch-catalog.md`, then this note.

## Purpose

Vanilla already has campaign-operation friction: staged AI jobs, operation lists, readiness gates, winter slowdown, chapter-weighted aggression, theater boxes, and transfer checks. Whiskey's added responsibility is narrower: decide whether an active strategic plan should test contact with one local formation, pause after enemy reaction, withdraw when overmatched, or escalate after favorable contact without turning the map into a cross-theater rush.

This is strategic-layer only. It does not steer battle AI or tactical stance.

## Vanilla Anchors

Verified against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`:

| Anchor | Line | Relevant behavior |
|---|---:|---|
| `AICampaign.UpdateUnitAI()` | 11304 | Stages campaign AI jobs across multiple passes, including offensive movement, defensive operations, theater update, transfer checks, objective picking, and offensive follow-through. |
| `AICampaign.RelieveUnitsInOperations(int)` | 14068 | Removes retreating or low-morale units from offensive and defensive operation lists. |
| `AICampaign.IsUnitAvailableForOffensiveOperations(...)` | 14080 | Gates operation candidates by readiness, strength, morale, existing operations, capital-defense duty, supply-depot construction, and related availability checks. |
| `AICampaign.CheckOffensiveMovements(int, Regiment, float)` | 14166 | Builds local offensive packages and commits them through `MoveUnitTo(...)` plus `unitsinoffensiveoperations`. |
| `GamePrefs.aiagressiveness[Policy.CurrentChapter]` in `CheckOffensiveMovements` | 14339 | Vanilla's offensive-operation probability is chapter-weighted and also depends on `timediff`, faction aggression, campaign aggression, winter modifier, and `timeofnotmoving`. |
| `Policy.CheckForChapterUpdate()` | 211606-211714 | Recomputes vanilla `Policy.CurrentChapter` at campaign frame 50 (30146-30149) and every `updatetimeforcampaigndata` (79305-79323), not once a day; Whiskey never calls it and the first review waits for the chapter to be 0 or more. Scenario `001` starts at chapter 0 (pre-war; no campaign movement, 91366) and reaches chapter 1 at the war start; W&L scenario `002` is forced to chapter 1 (211671). Both advance to chapter 2 after 1862-11-05 and to chapter 3 after 1864-11-09 when objective 26 (Union morale at least 0.50 on the verdict day) is accomplished and objective 27 is not. |

Vanilla therefore has a useful tempo substrate, but it does not have Whiskey's limited-contact doctrine that separates a probe from a mass commitment.

## Whiskey Implementation

Core files:

- `src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs`
- `src/WhiskeyRealism/Strategic/OperationalTempoDoctrine.cs`
- `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`
- `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- `src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs`

Runtime flow:

1. `StrategicCoordinator.UpdateOperationalProbe(...)` runs from the daily strategic review after front and formation ledgers are available.
2. The coordinator passes alliance, active CIC plan target, front ledger, formation directive ledger, prior probe state, day serial, Whiskey era, vanilla `Policy.CurrentChapter`, campaign month, and effective CIC personality into `OperationalProbeRuntime.BuildInput(...)`.
3. `OperationalProbeRuntime.BuildInput(...)` calls `OperationalTempoDoctrine.For(...)` to set probe duration, probe size, enemy-reaction, escalation, and withdrawal thresholds. If the active phase belongs to a named historical operation, phase metadata then adjusts the runtime contract: `OperationPosture` shapes thresholds, objective target coordinates drive package target X/Z, and `AllowCoordinatedAttack` / `AllowReinforcementPackage` / `AllowProbeOnly` gate package behavior.
4. `OperationalProbeLedger.Build(...)` returns one of `None`, `Probe`, `Pause`, `Withdraw`, or `Escalate`.
5. `FormationDirectiveLedger.ApplyOperationalProbe(...)` overlays the formation directive for the selected unit.
6. `OperationalProbeRuntime.Run(...)` uses vanilla `AICampaign.MoveUnitTo(...)` and `unitsinoffensiveoperations` for `Probe` / `Escalate`, or removes the selected unit from `unitsinoffensiveoperations` for `Pause` / `Withdraw`.

## Tempo Policy

`OperationalTempoDoctrine` combines vanilla chapter with Whiskey era instead of replacing either system. The late-war era is vanilla's chapter 3, not the calendar: `EraStage.TotalWar1864` starts with chapter 3 (Lincoln re-elected, 9 Nov 1864), so a lost election keeps the era at `Decisive1863` and the chapter-3 clause below is the era's own condition (`EraStageManager.IsLateWar`). The earlier eras keep their calendar years: no chapter boundary coincides with them (chapter 1 starts at the war, chapter 2 on 6 Nov 1862). A save written under the old calendar rule has its `TotalWar1864` pulled back to the chapter-derived era on the first review after load.

- Chapter 1 / `Amateur1861`: longer minimum probe time, smaller probe packages, higher escalation odds requirement.
- Chapter 2 / 1862-63: normal sustained operations.
- Chapter 3+ / `TotalWar1864` (from 9 Nov 1864): faster escalation and larger probes, especially for Union.
- Late Union: sustains pressure earlier after favorable contact.
- Late CSA: requires better odds and uses smaller probes.
- Winter months: extend probe time and reduce probe size.
- CIC personality: audacity/aggression lower friction; caution raises it.

All thresholds are clamped in `OperationalTempoDoctrine` so bad data cannot create zero-day probes, unlimited probe sizes, or impossible escalation ratios.

## Invariants

- At most one bounded same-area operational probe is active per alliance target.
- A probe cannot draw from `Hold`, `Guard`, `Recover`, or `Concede` directives.
- A probe cannot export a critical hold sector below its minimum hold ratio.
- Enemy reaction should produce `Pause` rather than pulling formations from other theaters.
- Favorable contact can produce `Escalate`, but the stored prior probe state is cleared so the same state does not re-escalate forever.
- Named operation phase flags can force probe-only behavior or disallow coordinated/reinforcement packages; those restrictions are explicit operation doctrine, not silent runtime fallback.
- Tactical battle AI remains untouched.

## Verification

Pure harness coverage:

- `operational probe assigns one bounded same-area formation`
- `operational probe pauses on enemy reaction`
- `operational probe escalates after favorable contact`
- `operational probe refuses critical hold donor`
- `operational probe overlays formation directive`
- `operational tempo chapter one delays escalation`
- `operational tempo late union sustains pressure`
- `operational tempo winter slows probes`
- `operational tempo late csa is more conservative than union`

Current deployed proof:

- `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` passed.
- `./build.sh` passed with 0 warnings / 0 errors.
- The operational-probe implementation is included in current deployed `main` DLL `562a61b5cd0cbbedc6d6002a349cd3d68ebf50ea1d60c941e3a5a9deeaafc57a` (1327104 bytes; 1110 PASS).
- Operational-probe runtime smoke remains pending after a game restart and a real probe opportunity.

Runtime smoke markers:

- `[OperationalProbe] alliance=... decision=Probe ...` should appear only for bounded same-area formations.
- `decision=Pause` should appear for enemy reaction instead of cross-map movement.
- `decision=Escalate` may appear after favorable contact and the doctrine-specific minimum probe duration.
- No repeated `[OperationalProbe]`, `[FormationDirective]`, Harmony, warning, or exception spam.
- Anti-zerg checks still apply: no `[DefenseIntent] custom-order ... threat=asset:` lines.
