# Operational Tempo Doctrine

Living reference for how the campaign planner keeps vanilla's chapter pacing. Source of truth remains shipped code first, then `docs/patch-catalog.md`, then this note.

## What changed (stage G)

The operational probe/contact loop (`OperationalProbeLedger`, `OperationalTempoDoctrine`, `OperationalProbeRuntime`, the `Probe`/`Pause`/`Withdraw`/`Escalate` states and the `OperationalProbe` telemetry) was deleted with the phased-plan CIC (G6). Its job (testing contact before committing a mass) is now the planner's recon layer ([`strategic-recon-commitment.md`](strategic-recon-commitment.md)): pickets and recon missions build fog-honest beliefs before the main effort is launched, and the allocator sizes the mass against believed strength. What survives from this note is the chapter pacing, now the chapter tempo gate.

## Vanilla anchors

Verified against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`:

| Anchor | Line | Relevant behavior |
|---|---:|---|
| `AICampaign.UpdateUnitAI()` | 11304 | Stages campaign AI jobs across passes; the planner's Plan and Execute jobs are two entries of the time-sliced job list. |
| `AICampaign.IsUnitAvailableForOffensiveOperations(...)` | 14080 | Availability gate; the planner's Postfix (#127) answers "not available" for the planned mass and for withheld Support. |
| `AICampaign.CheckOffensiveMovements(int, Regiment, float)` | 14166 | Vanilla's offensive package builder; its tempo roll is at 14316-14350. |
| `GamePrefs.aiagressiveness[Policy.CurrentChapter]` | 14339 | Vanilla's chapter-weighted offensive probability. |
| `Policy.CheckForChapterUpdate()` | 211606-211714 | Recomputes `Policy.CurrentChapter` at campaign frame 50 and every `updatetimeforcampaigndata`, not once a day; Whiskey never calls it and the first review waits for the chapter to be 0 or more. Scenario `001` starts at chapter 0 (pre-war, no campaign movement, 91366) and reaches chapter 1 at the war start; W&L scenario `002` is forced to chapter 1 (211671). Both advance to chapter 2 after 1862-11-05 and to chapter 3 after 1864-11-09 when objective 26 (Union morale at least 0.50) is accomplished and objective 27 is not. |

## The chapter tempo gate (`Strategic/Planner/CampaignTempoGate.cs`, chapters decision 5)

Vanilla rolls once per offensive package whose lead targets enemy ground. The planner withholds the planned mass from vanilla's offensive, so it carries the same roll itself, once per new main effort:

- **Probability** `tval = aiagressiveness[chapter] x timediff x 10 x faction aggression x campaign aggression x winter modifier + clamp01(idle^3)`, times the W&L career aggressiveness (once more when the player's campaign group is in the package). `timediff` is the tempo clock (`TimeDiffYears`: days since the last executors-allowed review, capped at one review, over 365.25); `idle` is the campaign date less the package's mean `timeofnotmoving`.
- **Draw** `Roll01(campaign seed, alliance, decision serial, day serial)`: a seeded FNV-1a plus murmur3 mix, reproducible across runtimes and saves, never `string.GetHashCode`.
- **Release** when `roll <= tval`, always under the scripted 1861 Union push (scenario 001, objective 6 open, past 1861.513) or `GameVars.debug_permanentaimovement`. No roll is made when vanilla would not roll (ground in no other alliance's state, or no believed enemy at the target): the launch is released at once.
- **Withhold while unreleased.** The first MainEffort dispatch of a decision waits for the release; a Support move or order into enemy-held ground waits for it too (own or neutral ground is never held). `CampaignWithholdView` (`ExecutorContext.Released`) reports the decision's unreleased Support members unavailable to vanilla so vanilla's offensive cannot spend them first; MainEffort members are always withheld from vanilla. The draw is keyed by the day serial, so an unreleased decision re-rolls each review, which is vanilla's own cadence. A decision whose mass has no dispatchable MainEffort formation dispatches nothing and is never released (`[CampaignTempo] undispatchable=true`).
- The result is persisted (`PlannerState.TempoReleasedDecision` against `DecisionSerial`; sidecar v3 also records the last executors-allowed review day), so a load does not re-roll a released decision.

Chapter 1 is slow because `aiagressiveness[1]` is small; chapter 2-3 release readily; winter lowers the modifier. This is vanilla's pacing, not a Whiskey era table. The late-war era is vanilla's chapter 3, not the calendar: `EraStage.TotalWar1864` starts with chapter 3 (`EraStageManager.IsLateWar`), so a lost election keeps the era at `Decisive1863`.

## Pre-war gate (decision 8)

Scenario `001` starts at chapter 0 with no campaign movement allowed. The planner publishes plans (so the UI and logs show intent) but `ExecutorsAllowed` is false: the mover, recon executor, depot executor and appointments do not run, and `LastFullReviewDay` is not advanced. The first executors-allowed review after the war start uses a one-review tempo gap.

## Invariants

- Chapter pacing is vanilla's formula and vanilla's preferences; the planner adds no era multiplier of its own.
- The roll is seeded and persisted; reloading the same save yields the same release.
- The W&L player's chain classes are never moved by the planner.
- Tactical battle AI remains untouched.

## Verification

Harness: `CampaignTempoGateTests` (probability terms, forced invasion, `Releases`, seeded `Roll01` reproducibility and spread, tempo clock cap and reset, `NeedsRoll`, `Evaluate`), `CampaignExecutorTests` (withhold view and wiring pins), `PlannerSidecarTests` (tempo state round trip). In-game proof (a chapter-1 launch that takes several reviews to release, `[CampaignTempo]` rows with `released=` and `roll=`) is part of the stage G smoke in [`docs/handoff.md`](handoff.md).
