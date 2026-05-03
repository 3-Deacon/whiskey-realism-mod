# Strategic Brain — Design Spec

**Slice A** — strategic AI overhaul for Grand Tactician: The Civil War's Whiskey & Lemons DLC career mode.
**Status:** approved 2026-05-02 via brainstorming session. Implementation plan TBD.
**Decompile reference:** `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (see `docs/findings.md` for line numbers).

---

## 1. Goal

Replace the vanilla random-objective campaign AI with a thinking-but-not-historically-deterministic strategic engine that runs continuously for both Confederate and Union AI throughout a W&L career. Player commands a small actor (artillery section / regiment / brigade depending on questionnaire); both nations' Commanders-in-Chief and theater commanders are AI-driven.

Three success criteria:

1. **Strategic awareness** — the AI commits to phased operational plans over multiple game-months instead of randomly picking objectives turn by turn.
2. **Historical character without scripting** — playthroughs follow recognizable arcs (1861 amateur mobilization → 1864-65 total war; CSA defensive-aggressive; Union slow consolidated pressure; ~60-80% of runs see McClellan get sacked, Lee take ANV command, Grant rise to general-in-chief at roughly historical dates) but alternate histories emerge in unusual campaigns.
3. **W&L-aware** — player-commanded units are not steamrolled by the AI's strategic decisions; the existing `DLC_WL.dlc_scenarioactive` / `dlcw_isundercommander` / `PerformAIActionDLCWL` gate is respected and extended.

## 2. Locked design choices

Six choices locked during the 2026-05-02 brainstorming session. Reject any spec change that conflicts with these without re-opening the brainstorming.

| # | Choice | Rationale |
|---|---|---|
| 1 | Slice A only (strategic brain). Slices B/C/D deferred. | Single-slice discipline; tactical without strategic = smart driver going wrong direction. |
| 2 | Tier 3 scope (replace existing weak + extend + net-new operational plans). | Tier 1/2 are bug fixes. Tier 4 fights the engine. Tier 3 is the natural ceiling. |
| 3 | Era × faction × officer personality system. | User explicitly asked for both factions to feel different (faction asymmetry) and history-flavored. |
| 4 | Triggered-scripted officer succession (~12 events, ~60-80% historical fidelity). | Recognizable patterns without deterministic runs. |
| 5 | Phased operational plans (2-4 phases, one active per side). | Captures real campaigns (Peninsula, Vicksburg) without combinatorial explosion. |
| 6 | Monthly + event-triggered cadence; events mark plans dirty (next-tick processing); adjust by default, replan only on assumption-invalidating events. | Matches real CIC review cadence; prevents AI thrashing. |

## 3. Architecture — two-tier hierarchy

```
┌──────────────────────────────────────────────────────────────┐
│  StrategicCoordinator   (singleton MonoBehaviour, monthly)   │
│  • runs the 1st-of-month re-eval loop                        │
│  • listens for event triggers (KIA, town loss, defeat)       │
└─────────┬────────────────────────────────────┬───────────────┘
          ▼                                    ▼
┌──────────────────────┐              ┌──────────────────────┐
│ CIC[CSA]              │              │ CIC[Union]            │
│ • personality (5d)    │              │ • personality (5d)    │
│ • era + faction prof  │              │ • era + faction prof  │
│ • OperationalPlan     │              │ • OperationalPlan     │
│ • succession state    │              │ • succession state    │
└────────┬─────────────┘              └────────┬─────────────┘
         ▼                                     ▼
┌──────────────────────┐              ┌──────────────────────┐
│ TheaterCommander[ANV] │              │ TheaterCommander[AoP] │
│ TheaterCommander[AoT] │              │ TheaterCommander[AoT] │
│ TheaterCommander[Tx]  │              │ TheaterCommander[AoO] │
│ • personality (5d)    │              │ • personality (5d)    │
│ • executes phase      │              │ • executes phase      │
└────────┬─────────────┘              └────────┬─────────────┘
         ▼                                     ▼
   Harmony Postfix patches → AICampaign / AIBattle decision points
```

**Two-tier conflict rule (load-bearing invariant):**

- **CICs decide** target + force level + deadline.
- **Theater commanders decide** route + tempo + tactical posture.
- Plans are **read-only** to theater commanders. Only CICs can abandon a plan.

Without this rule the layers fight each other (CIC says concentrate, theater commander says raid) and AI behavior becomes opaque.

**Read-only mod-state invariant:** Harmony patches **read** mod state, never **write** to it. State writes happen only on the monthly tick and event-trigger handlers. This is the load-bearing invariant that keeps the bridge debuggable.

## 4. Data model

### 4.1 PersonalityVector

5 floats in `[-1, +1]`. Faction profiles, era profiles, officer profiles all express in this space.

```csharp
public struct PersonalityVector
{
    public float Aggression;             // offensive vs defensive bias
    public float Caution;                // overestimates enemy / waits for perfect conditions
    public float Audacity;               // commits to bold/risky plans (envelopments, raids)
    public float CasualtyTolerance;      // willing to spend men for objectives
    public float PoliticalResponsiveness;// reacts to political/morale pressure

    public static PersonalityVector Compose(
        PersonalityVector officer,
        PersonalityVector era,
        PersonalityVector faction)
    {
        // Additive composition with [-1, +1] clamp.
        return new PersonalityVector
        {
            Aggression = Clamp(officer.Aggression + era.Aggression + faction.Aggression),
            Caution = Clamp(officer.Caution + era.Caution + faction.Caution),
            // ... same for the remaining 3 dims
        };
    }
}
```

### 4.2 EraStage

4 stages with date defaults + war-state overrides.

| Stage | Date default | War-state override | Vector delta (Agg / Caut / Aud / Cas / Pol) |
|---|---|---|---|
| 1861 amateur | 1861-04 to 1861-12 | — | -0.3 / +0.5 / -0.2 / -0.4 / +0.1 |
| 1862 operational | 1862-01 to 1862-12 | — | 0 / 0 / +0.1 / 0 / 0 |
| 1863 decisive | 1863-01 to 1863-12 | + Vicksburg falls before 1863-07 → advance | +0.2 / -0.2 / +0.3 / +0.2 / 0 |
| 1864-65 total war | 1864-01 onward | + CSA loses Atlanta before 1864-09 → advance | +0.4 / -0.4 / +0.2 / +0.6 / -0.2 |

Era stage advances when EITHER the date trigger fires OR the war-state override condition holds. Era stages never regress in this slice.

### 4.3 Faction profiles

```
CSA baseline:
    PersonalityVector: agg +0.2, caut 0,    aud +0.3, cas 0,    pol -0.1
    Theater preference: East 1.0, West 0.6, Trans-Miss 0.2, Coast 0.4

Union baseline:
    PersonalityVector: agg 0,    caut +0.2, aud -0.1, cas -0.1, pol +0.3
    Theater preference: East 1.0, West 0.9, River 1.0, Coast 0.8
```

Theater preference weights multiply zone importance when CIC scores objectives. Union weights East-and-West roughly equally + adds river + coast (Anaconda Plan); CSA weights East heavily, defends West, ignores Trans-Mississippi.

### 4.4 Historical figure registry

~25 hand-coded historical figures with personality vectors. Lookup at runtime by `(commander.faction, commander.name, commander.arrivaldate)` match. Disambiguation when multiple commanders match: fall back to highest `commander.fame` to pick the historical one. Minor commanders (no entry) get a **derived** profile from existing GTCW commander fields:

```
agg  = +0.1 if westpoint else 0  +  +0.2 if !political else 0
caut = +0.3 if political else 0
aud  = +0.2 if westpoint else 0
cas  = +0.1 * (commander.fame - commander.defamed) (clamped)
pol  = +0.4 if political else 0
```

Plus a small per-officer random spread `[-0.1, +0.1]` per dimension, frozen at first encounter and persisted in the sidecar.

Initial roster (subject to refinement during implementation):

| Faction | Officer | Agg | Caut | Aud | Cas | Pol |
|---|---|---|---|---|---|---|
| CSA | Davis (CIC) | -0.1 | +0.3 | -0.3 | -0.3 | +0.5 |
| CSA | Lee | +0.7 | -0.5 | +0.6 | +0.4 | -0.2 |
| CSA | Joe Johnston | -0.2 | +0.5 | -0.2 | -0.6 | +0.1 |
| CSA | Bragg | +0.2 | +0.3 | -0.4 | -0.1 | +0.4 |
| CSA | Beauregard | +0.4 | -0.1 | +0.5 | +0.1 | -0.1 |
| CSA | Hood | +0.9 | -0.8 | +0.4 | +0.9 | +0.3 |
| CSA | Jackson | +0.8 | -0.5 | +0.8 | +0.4 | -0.5 |
| CSA | Longstreet | +0.4 | +0.1 | +0.3 | -0.2 | -0.1 |
| CSA | Stuart | +0.5 | -0.4 | +0.7 | -0.1 | -0.2 |
| CSA | Forrest | +0.7 | -0.3 | +0.8 | +0.3 | -0.6 |
| Union | Lincoln (CIC, political) | +0.3 | +0.1 | +0.1 | +0.4 | +0.7 |
| Union | Scott | -0.1 | +0.4 | +0.2 | -0.4 | +0.3 |
| Union | McClellan | -0.3 | +0.9 | -0.6 | -0.7 | +0.6 |
| Union | Halleck | 0 | +0.6 | -0.3 | -0.2 | +0.5 |
| Union | Pope | +0.6 | -0.4 | +0.4 | +0.2 | +0.4 |
| Union | Burnside | +0.3 | +0.2 | -0.1 | +0.5 | +0.5 |
| Union | Hooker | +0.5 | -0.1 | +0.6 | +0.3 | +0.2 |
| Union | Meade | +0.2 | +0.4 | 0 | +0.1 | +0.3 |
| Union | Grant | +0.8 | -0.6 | +0.5 | +0.7 | -0.1 |
| Union | Sherman | +0.7 | -0.4 | +0.9 | +0.5 | -0.5 |
| Union | Sheridan | +0.8 | -0.3 | +0.7 | +0.4 | -0.2 |
| Union | Thomas | +0.3 | +0.4 | -0.1 | -0.1 | +0.1 |
| Union | Buell | -0.1 | +0.6 | -0.3 | -0.5 | +0.4 |
| Union | Rosecrans | +0.3 | +0.3 | +0.2 | 0 | +0.2 |
| Union | Banks | -0.1 | +0.5 | -0.2 | 0 | +0.7 |

### 4.5 OperationalPlan

```csharp
public class OperationalPlan
{
    public int  CICFactionId;           // 0 = CSA, 1 = Union
    public int  AssignedTheaterId;      // which TheaterCommander executes
    public List<Phase> Phases;          // 2-4 phases ordered
    public int  CurrentPhaseIndex;
    public Tools.Date PlanDeadline;     // overall plan deadline
    public string Rationale;            // human-readable for logs
    public bool IsDirty;                // event-trigger marker
}

public class Phase
{
    public AIArea TargetArea;
    public float  ForceFractionRequired;   // 0..1 of theater's units committed
    public PhaseTransition Transition;
    public Tools.Date Deadline;
    public Phase Fallback;                 // optional: next phase on failure
}

public enum PhaseTransition
{
    TargetTaken,         // friendly takes the target zone
    TargetEngaged,       // major battle in target zone (win or loss)
    DeadlineExpired,     // deadline date passed
    ForceBelowThreshold  // assigned force fell below required fraction
}
```

### 4.6 Succession events

~12 canonical events. Each = `(date, war-state condition, new officer assignment)`. Fires on monthly tick when both gates pass. Won't fire if the named replacement is already in command (idempotent).

| # | Event | Date | War-state condition |
|---|---|---|---|
| 1 | Lee → ANV command | 1862-05 | Johnston wounded/disabled OR ANV has lost a major battle |
| 2 | Bragg → Western theater | 1862-06 | Beauregard's command rating < threshold |
| 3 | McClellan removed | 1862-11 | Lincoln's patience: AoP has failed N offensive operations |
| 4 | Burnside → Hooker | 1863-01 | After Burnside's first major defeat |
| 5 | Hooker → Meade | 1863-06 | Lee invading Pennsylvania OR within X days of major Eastern battle |
| 6 | Bragg removed | 1863-11 | After Western theater's major defeat |
| 7 | Joe Johnston → Western command | 1863-12 | Bragg removed (cascade from #6) |
| 8 | Grant → General-in-Chief | 1864-03 | Union has won Vicksburg AND Chattanooga |
| 9 | Sherman → Western command | 1864-03 | Cascade from #8 (Grant goes East) |
| 10 | Hood replaces Johnston | 1864-07 | Atlanta within X distance of Union army AND Davis's patience exhausted |
| 11 | Sheridan → Shenandoah | 1864-08 | Valley operations needed (Confederate raid OR Union strategic pivot) |
| 12 | Lee → General-in-Chief CSA | 1865-02 | War clearly lost (CSA total morale + economy below threshold) |

## 5. Decision flow

### 5.1 Monthly tick

Hooked via Postfix on the game's existing month-rollover (TBD: identify hook in implementation phase; candidates include `Tools.Date.NewMonth` events or end-of-day cycle's monthly branch).

```
StrategicCoordinator.OnMonthlyTick():
  for faction in [CSA, Union]:
    1. EraStageManager.CheckTransition(faction)
       → advance era if date OR war-state trigger fires
    2. SuccessionScheduler.CheckEvents(faction)
       → fire any of the 12 events whose (date AND condition) gates pass
       → if event fires, swap officer assignment + mark plan dirty
    3. CIC.ReviewPlan(faction)
       Plan still valid?
         - army assigned still intact (above forceFraction threshold)?
         - target zone still hostile?
         - deadline not blown?
       → no  → CIC.Replan()
       → yes → CIC.Adjust()
    4. distribute plan to TheaterCommander assigned to plan's theater
```

### 5.2 Event triggers

Game events that fire `EventTrigger`:
- Town loss (faction loses a town)
- Major battle defeat (faction loses with >5% casualty rate)
- KIA on a CIC-tier or theater-commander-tier officer
- Army assigned to active plan destroyed (force below 25% of original)
- CIC officer KIA / replaced by succession event

Trigger handler:
```
StrategicCoordinator.OnEventTrigger(faction, eventType, details):
  cic = CICs[faction]
  if eventType invalidates cic.ActivePlan's assumptions:
    cic.ActivePlan.IsDirty = true
  // events do NOT cause immediate re-eval; next monthly tick processes the dirty bit
```

This deferred-processing rule prevents the AI from thrashing on a chain of events within a single month.

### 5.3 CIC.Replan()

```
1. Score all CampaignObjective.GetAvailableObjectives() for this faction
   using era × faction × CIC personality.
   Each objective contributes:
     theaterMatch    × factionTheaterPreference[obj.theater]
     forceRatio      × (cic.Caution invertedly weights this — cautious = needs higher ratio)
     distanceFromForces × negative weight (cic.Audacity offsets — audacious tolerates distance)
     supplyReach
     foreignRecognitionValue × factionForeignRecognitionWeight
     attritionValue          × cic.CasualtyTolerance
2. Weighted-random pick from the top 3 (not pure argmax — gives replay variety).
3. Decompose into 2-4 phases by geographic prerequisites:
     "must take supply hub before main objective"
     "must engage covering force before decisive phase"
   Phase count = clamp(2 + (1 if low audacity), 2, 4).
4. Assign to theater commander by geographic match.
5. Force fraction + deadline scaled by personality:
     forceFraction = 0.4 + 0.4 * cic.Caution + 0.3 * (1 - cic.Audacity)  (clamp 0.3..0.95)
     deadline = baseDeadline * (1 + 0.5 * cic.Caution)
```

### 5.4 TheaterCommander execution (per game-day)

Theater commander does NOT directly manipulate units. Its personality + active phase produce **score multipliers** that the bridge layer's Harmony patches read.

```
TheaterCommander.GetZoneRelevance(zone)        → used by UpdateImportanceValues patch
TheaterCommander.GetForceConsolidationUrgency  → used by CheckTransferOfUnits patch
TheaterCommander.GetDefensiveOpsThreshold      → used by CheckPickDefensiveOps patch
TheaterCommander.GetChargeRestraint            → used by CheckForFeudGroupActions patch (via tactical slice; placeholder here)
TheaterCommander.GetPerkPreference(perkId)     → used by CheckPerkSelection patch
TheaterCommander.GetRecruitmentTheaterWeight   → used by GetBestRecruitingState patch
```

Theater commanders cannot abandon a phase; they only signal completion (target taken/engaged) or failure (force below threshold). Phase-transition decisions belong to the CIC on monthly tick.

## 6. Bridge layer — Harmony patches

Approximately 10 patches. All Postfix unless noted. Each is a stable catalog item; ordinals assigned at implementation in `docs/patch-catalog.md`.

| # | Patch class | Game method | Decompile line | Mod state read | Effect |
|---|---|---|---|---|---|
| 1 | `PickCampaignObjectivePatch` | `AICampaign.PickCampaignObjective` | 17770 | CIC's active plan | Replace `Random.Range` with plan target |
| 2 | `ImportanceValuesPatch` | `AICampaign.UpdateImportanceValues` | 14906 | TheaterCommander zone relevance | Multiply per-zone importance |
| 3 | `MostValueableZonesPatch` | `AICampaign.CalculateMostValueableAIZones` | 10965 | Plan phase target | Bias toward phase target |
| 4 | `TransferOfUnitsPatch` | `AICampaign.CheckTransferOfUnits` | 17232 | TheaterCommander consolidation urgency | Lower threshold when phase demands consolidation |
| 5 | `DefensiveOpsPatch` | `AICampaign.CheckPickDefensiveOps` | 11791 | TheaterCommander defensive threshold | Per-personality strength gate |
| 6 | `CommanderReplacementPatch` | `AICampaign.CheckAICommanderReplacements` | 17009 | SuccessionScheduler state | Override picks during scripted events |
| 7 | `PerkSelectionPatch` | `AICampaign.CheckPerkSelection` | 11873 | Officer personality | Bias picks |
| 8 | `MacroAIStancePatch` | `AIBattle.CheckGlobalAIStrategy` | 6314 | Plan phase + theater personality | Override `macroai` per battle |
| 9 | `RecruitmentPatch` | `AICampaign.GetBestRecruitingState` | 10723 | CIC theater preferences | Weight recruitment toward priority theaters |
| 10 | `MonthlyTickHookPatch` | (TBD: month-rollover hook) | — | — | Drives StrategicCoordinator.OnMonthlyTick() |

**Patch hygiene:**
- Postfix-preferred. Reflection for fields/properties (forward-compat to game updates).
- All reflection wrapped in try/catch + `Plugin.Log.LogWarning(...)`. Never throw from a patch.
- Each patch logs `OnceLog.Info(...)` on first invocation per save-load cycle for smoke-testing.
- Mod state is **read-only** to patches. They steer existing AI; they never mutate `CIC` / `TheaterCommander` / `OperationalPlan`.

## 7. Persistence

### 7.1 Sidecar JSON, not embedded save format

```
<persistentDataPath>/Saves/MyCareer.gtsave           (vanilla)
<persistentDataPath>/Saves/MyCareer.whiskeyrealism.json   (mod sidecar)
```

Why sidecar:
- GTCW save format is binary; embedding mod state risks save corruption on game updates.
- JSON is debuggable — players can inspect mod state.
- Save-format independence means uninstalling the mod leaves vanilla saves untouched.

### 7.2 Save hook

Prefix `SavesManager.Save(string name)` to capture filename. Write JSON sidecar after game's save completes (Postfix on the EOD cycle that owns Save).

### 7.3 Load hook

`SavesManager.Loaded` is an `event Action<Queue<Action>>` (UBoatCrewMod precedent — same trap). Use `EventInfo.AddEventHandler` from a real-method hook (Postfix `SavesManager.Awake` for one-time install per process).

Sidecar missing or corrupt → log warning, init mod state from current game state (treat as fresh career — era stage from current date, no active plans, theater commanders newly assigned with personalities derived from existing officers).

### 7.4 Sidecar contents

```json
{
  "version": 1,
  "factions": [
    {
      "factionId": 0,
      "factionName": "CSA",
      "currentEra": "1862_operational",
      "succession": {
        "firedEvents": [1, 2],
        "lastChecked": "1862-08-01"
      },
      "cic": {
        "officerName": "Lee",
        "personality": [0.7, -0.5, 0.6, 0.4, -0.2],
        "activePlan": {
          "assignedTheaterId": 0,
          "phases": [...],
          "currentPhaseIndex": 1,
          "rationale": "Maryland invasion to relieve Virginia and pursue foreign recognition",
          "isDirty": false
        }
      },
      "theaterCommanders": [
        {"theaterId": 0, "officerName": "Lee", "personality": [...]},
        ...
      ]
    },
    { "factionId": 1, "factionName": "Union", ... }
  ],
  "minorOfficerProfiles": [
    {"commanderId": 142, "personality": [0.1, 0.4, -0.2, 0.0, 0.3]}
  ]
}
```

## 8. Error handling

**Degrade to vanilla, never throw.** Failure modes and responses:

| Failure | Response |
|---|---|
| Reflection lookup fails on game-method signature change | `LogWarning`, disable just that patch class, rest of mod continues |
| Mod state inconsistent (active plan target zone destroyed, theater commander unit missing, succession event names a non-existent commander) | Patch returns early; game's vanilla method output passes through |
| Sidecar JSON corrupt or missing | `LogWarning`, init from current game state |
| Sidecar save fails (disk full, permissions) | `LogError`, do NOT block game's save dialog |
| Game version drift on registration | Per-patch signature check at `Awake`; failed patches log error and skip |
| `StrategicCoordinator` initializes before game state is ready | `PeriodicTick` retry pattern (UBoat precedent) — coordinator self-defers until `aifaction` populated |

## 9. Testing

No automated tests (consistent with UBoatCrewMod). Per-patch first-fire markers via `OnceLog.Info` for smoke-test discipline.

### 9.1 Smoke-test scenarios

1. Fresh 1861 CSA career — confirm CIC=Davis, era=1861-amateur in log.
2. Run to 1862-05 — confirm event #1 (Lee→ANV) fires.
3. Run to 1862-06 — confirm CIC's first plan logged with phase targets and rationale.
4. Confirm theater commanders assigned to existing army groups (one log line per theater commander).
5. Run a campaign battle — confirm mod state is unchanged during battle (read-only invariant).
6. Save → quit → reload — confirm plan + era + succession state persisted via JSON sidecar inspection.
7. Run to 1864-03 — confirm Grant rises (event #8) AND era advances to 1864-65 total war.
8. Run to 1864-07 — confirm event #10 (Hood replaces Johnston) fires when Atlanta is threatened, OR doesn't fire when Atlanta is safe.
9. Observe a campaign that produces multiple Union defeats in 1862 — confirm event chain (McClellan removed → Burnside → Hooker → Meade) still fires on schedule. (Player can influence by choosing a CSA career; sequence is not deterministic.)
10. Observe a campaign where CSA holds Vicksburg through 1863 — confirm Grant rise event #8 does NOT fire prematurely (its war-state condition demands Vicksburg + Chattanooga both fallen).

### 9.2 Diagnostic logs

`Plugin.cs` `[Diagnostics]` section gates verbose logging:

- `Verbose Logging` → emit per-patch first-fire markers and decision-trace logs.
- `Plan Trace Logging` → on each monthly tick, dump CIC's plan reasoning (objective scores, top-3, picked, phases, deadline).
- `Succession Trace Logging` → on each monthly tick, log every succession event check (date gate result, war-state gate result, fired/not-fired).

## 10. Project setup (already done)

- New repo at `~/Projects/whiskey-realism-mod/`, public on GitHub at `3-Deacon/whiskey-realism-mod`, MIT license.
- Skeleton: `src/WhiskeyRealism/{Plugin.cs, csproj, Patches/, Strategic/, Util/}`, `docs/{handoff,findings,patch-catalog,superpowers/}/`, `refs/` symlinks (gitignored), `.gitignore`, `NuGet.config`, `build.sh`, `.claude/settings.json`.
- Build skeleton verified clean (BepInEx.Core 5.4.21 + HarmonyX 2.10.2 + Unity 2021 refs).
- Plugin GUID: `dev.kyle.whiskey-realism`. BepInPlugin attribute version 0.1.0 → bump to 0.2.0 on first patch ship.

## 11. Compatibility

**Explicitly incompatible with the "Community Hotfixes / Quality of Life Mod"** distributed via Steam. That mod replaces `Assembly-CSharp.dll` wholesale; this mod is a BepInEx plugin that patches the vanilla DLL via Harmony. They cannot coexist safely. Long-term path: extract Community Hotfix's behavior fixes (officer auto-replace, recruitment ratios, weapon-range selection, weapon-range selection, AI passive morale recovery) into this patch suite so users only need this mod.

Documented in:
- `README.md` Compatibility section.
- `Plugin.cs.Awake()` will emit a `LogWarning` if it detects Community Hotfix's signature in a future check (TBD in implementation).

## 12. Out of scope (future slices)

- **Slice B — Tactical brain.** Macro-AI stance scoring instead of per-battle preset; reserve management; feud-system gating with `PerformAIActionDLCWL`; smarter charge gates; retreat thresholds.
- **Slice C — W&L hierarchy AI.** The player's CO gives orders; peer divisions act with their own competence; officer relations (`DLCWL_Commander.commander relations`) shape compliance.
- **Slice D — Additional historical flavor.** Foreign-recognition modeling (CSA AI weights Antietam-class victories higher); economic strangulation logic (Union AI weights river/coastal interdiction in '63+); public-morale modeling (CSA conserves forces after Gettysburg to prevent collapse).

## 13. Open questions for implementation phase

1. Identify the exact game hook for monthly tick. Candidates: `Tools.Date.NewMonth`-style event, `Economy.UpdateMonthly`-style method, or end-of-day cycle's monthly branch in `bunits` / campaign-controller.
2. Identify how `aifaction[i].ownunits` exposes army-group-level top units (`unittyp >= 15`?). Map to TheaterCommander assignment at first encounter.
3. Identify `CampaignObjective.GetAvailableObjectives` return semantics (cached? per-faction? recomputed on demand?). Plan-scoring depends on this.
4. Verify GTCW's save format does not collide with our sidecar `.whiskeyrealism.json` filename (no game system writes to that extension).
5. Verify event dispatcher sources for: town loss, major battle defeat, KIA-on-officer. Likely candidates in `BattleUnits` end-of-battle handling and `CampaignController` event hooks.
6. Theater-commander identification at runtime: is there a stable ID, or do we have to track by `commander.id_hash + arrivaldate`?
7. Phase-decomposition algorithm — section 5.3 says "decompose by geographic prerequisites" but the actual algorithm is left for implementation. Likely shape: from picked objective, walk back through geographic dependencies (target → covering force → supply hub → start position) and treat each step as a phase. Verify this maps cleanly onto GTCW's `AIArea` graph.
8. Personality vector serialization in JSON sidecar — array `[0.7, -0.5, ...]` vs object `{agg:0.7, caut:-0.5, ...}`. Object form is forward-compatible if dimensions are added later. Decide at first persistence implementation.

## 14. Acceptance criteria

The strategic-brain slice is "shippable" when:

1. Build is green (0 warnings, 0 errors).
2. All ~10 bridge-layer patches are registered and log first-fire markers in a fresh career.
3. Smoke-test scenarios 1-7 pass on a single career playthrough.
4. JSON sidecar is human-readable and round-trips through save → reload without losing state.
5. Mod state is observably read-only from patches (no patch mutates `CIC` / `TheaterCommander` state — verified by code review at PR time).
6. Reflection failures (simulated by deleting a target method's signature in a unit-test stub, or by checking on a manually-corrupted DLL) degrade to vanilla rather than crashing.
7. Player-commanded units are observably untouched by mod's strategic decisions (confirm `dlcw_isundercommander` units are not assigned to plans).
8. `dist/WhiskeyRealism.dll` deploys cleanly to GTCW with BepInEx 5.4.x x64 UnityMono installed.

## 15. References

- Decompile coordinates: `docs/findings.md`.
- Game install layout, build commands: `AGENTS.md`.
- Roadmap context (slices, current state): `docs/handoff.md`.
- UBoatCrewMod reference architecture: `~/Projects/uboat-crew-mod/AGENTS.md`.
- Brainstorming session decisions: this spec section 2.
