# Strategic Brain — Design Spec

**Slice A** — strategic AI overhaul for Grand Tactician: The Civil War's Whiskey & Lemons DLC career mode.
**Status:** approved 2026-05-02 via brainstorming session. v0.2.1.1 released; local v0.2.2 enrichment is implemented through #16. This remains the historical Slice A design record; `docs/patch-catalog.md` is authoritative for live patch inventory.
**Decompile reference:** `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (see `docs/findings.md` for line numbers).

---

## 1. Goal

Replace the vanilla random-objective campaign AI with a thinking-but-not-historically-deterministic strategic engine that runs continuously for the AI-controlled faction(s) throughout a W&L career. Player commands a small actor (artillery section / regiment / brigade depending on questionnaire). The AI-controlled CIC and all theater commanders are AI-driven; the player's faction CIC is the player whenever `DLC_WL.IsCommanderInChief()` is true (see §3.1).

Three success criteria:

1. **Strategic awareness** — the AI commits to phased operational plans over multiple game-months instead of randomly picking objectives turn by turn.
2. **Historical character without scripting** — playthroughs follow recognizable arcs (1861 amateur mobilization → 1864-65 total war; CSA defensive-aggressive; Union slow consolidated pressure; ~60-80% of runs see McClellan get sacked, Lee take ANV command, Grant rise to general-in-chief at roughly historical dates) but alternate histories emerge in unusual campaigns.
3. **W&L-aware** — player-commanded units are not steamrolled by the AI's strategic decisions; the existing `DLC_WL.dlc_scenarioactive` / `dlcw_isundercommander` / `PerformAIActionDLCWL` gate is respected and extended. When the player IS the CIC, the mod does not override the player's strategic authority — the mod runs only the opposing CIC + both sides' theater commanders.

## 2. Locked design choices

Six choices locked during the 2026-05-02 brainstorming session. Reject any spec change that conflicts with these without re-opening the brainstorming.

| # | Choice | Rationale |
|---|---|---|
| 1 | Slice A only (strategic brain). Slices B/C/D deferred. | Single-slice discipline; tactical without strategic = smart driver going wrong direction. |
| 2 | Tier 3 scope (replace existing weak + extend + net-new operational plans). | Tier 1/2 are bug fixes. Tier 4 fights the engine. Tier 3 is the natural ceiling. |
| 3 | Era × faction × officer personality system. | User explicitly asked for both factions to feel different (faction asymmetry) and history-flavored. |
| 4 | Triggered-scripted officer succession (~12 events, ~60-80% historical fidelity). | Recognizable patterns without deterministic runs. |
| 5 | Phased operational plans (2-4 phases, one active per side). | Captures real campaigns (Peninsula, Vicksburg) without combinatorial explosion. |
| 6 | Weekly + event-triggered cadence; events mark plans dirty (next-tick processing); adjust by default, replan only on assumption-invalidating events. Monthly remains a heartbeat/checkpoint boundary only. | Campaign speed makes monthly command too slow; weekly keeps strategic control responsive without daily thrash. |

## 3. Architecture — two-tier hierarchy

```
┌──────────────────────────────────────────────────────────────┐
│  StrategicCoordinator   (singleton, weekly review + monthly heartbeat) │
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

**Read-only mod-state invariant:** Harmony patches **read** mod state, never **write** to it. State writes happen only on weekly strategic review and event-trigger handlers. This is the load-bearing invariant that keeps the bridge debuggable.

### 3.1 W&L player-CIC gate (load-bearing)

Vanilla `AICampaign` already skips the player's faction in its update loop when the player is CIC (decompile line 11620 — `if (DLC_WL.dlc_scenarioactive && ... && DLC_WL.IsCommanderInChief() && aifaction[currentfaction].allianceid == GameVars.commander[DLC_WL.dlc_chosencommander].alliance) { currentfaction++; ... }`). The mod must match this contract: when the player is CIC of faction X, the mod's `StrategicCoordinator` does NOT instantiate or run a `CIC[X]`. Theater commanders for faction X are also skipped — the player gives orders to subordinates directly through the W&L UI; the mod has no business steering those orders. The opposing faction's CIC + theater commanders run normally.

```
StrategicCoordinator.RunStrategicReview():
  for faction in [CSA, Union]:
    if IsPlayerCICOf(faction): continue   // player has authority; mod stands down
    // ... era / succession / plan review for AI-driven faction
```

`IsPlayerCICOf(faction)` is computed each tick (cheap), not cached, because W&L promotion/demotion is event-driven and the player can transition into and out of CIC rank mid-career. `dlcw_isundercommander` and `PerformAIActionDLCWL` continue to gate tactical-unit AI under the player at sub-CIC ranks; that's separate machinery owned by future Slice C.

Edge case: the player's faction is **not** strategically headless when the player is CIC — the player IS the strategic authority. The mod does not write a sidecar plan for that faction; on save, only the AI-driven faction's plan is persisted. On load, if the player has changed factions or rank since save, mod state initializes lazily for whichever side is now AI-driven.

### 3.2 Vanilla settings integration (added 2026-05-03)

GTCW's campaign-creation menu (`MainMenu` class, decompile line 193154) exposes three player-facing knobs that interact with the strategic brain:

| Vanilla setting | Field | Conflict with mod logic? | Mod's response |
|---|---|---|---|
| **Aggressiveness** (5 steps Calm…Very High) | `GameVars.usedcampaignagressiveness` (default 1.0) | YES — global multiplier on AI offensive/defensive rolls would double-count our `PersonalityVector.Aggression` dimension | **Lock to 1.0** ("Mediocre" — neutral midpoint). Mod's personality system owns aggression. |
| **Historic AI Personality** (toggle) | `GameVars.usehistoricaipersonality` (default true) | YES — when false, vanilla rolls random AI grand-strategy policies; our 12 scripted succession events assume historical context | **Lock to true.** Required for our `HistoricalFigureRegistry` and `SuccessionScheduler` to be coherent. |
| **Difficulty** (5 steps Very Easy…Very Hard) | `GameVars.usedcampaignbonus` → `GameVars.casualtiesmodifier` | NO functional conflict (mod doesn't read either field) — but design-asymmetric to leave only this one player-controlled | **Lock to "Hard"** (index 3 of 5; matches Civil War casualty reality — ~620k dead). |

Plus 5 realism-quality checkboxes (FogOfWar, OrderDelays, Feuds, FullReadiness, AllAutomanage) all forced ON for an immersive W&L experience.

**Locking implementation has two layers:**

1. **Value lock** at finalize: `Postfix` on `MainMenu.SetCampaignParameters` overwrites `GameVars.usedcampaignagressiveness`, `usehistoricaipersonality`, `usedcampaignbonus`, `usedcampaignbonusrunning`, `casualtiesmodifier` regardless of what the player picked.
2. **UI grey-out** on every menu interaction: `Postfix` on `MainMenu.SwitchAIMode`, `MainMenu.ChangeBonus`, `MainMenu.CheckForCheckBoxUpdates`. Sliders snap back to locked values + display "Locked:Realism" label. Checkboxes get force-checked + `CheckBox.Freeze(true)` (decompile line ~186890) which sets `frozen = true` (causes `CheckClicks()` to early-return) and applies a half-alpha visual color via `SetButtonColor`. Click input is dropped at the source — no flicker.

**Battle-panel exclusions:** `SwitchAIMode` and `ChangeBonus` are shared between the campaign-create menu and the battle-launch panel ("AI Mode: historic vs dynamic", "Difficulty: per-battle"). Patches gate by `gameObject.name == "BattlePanel"` to leave per-battle settings player-controlled (they don't conflict with strategic brain).

**Config escape hatches** (in `BepInEx/config/dev.kyle.whiskey-realism.cfg`):

```ini
[Strategic]
Override Vanilla Settings = true   ; flip false to disable all locks (advanced)
Locked Difficulty = 3              ; 0=Very Easy, 1=Easy, 2=Mediocre, 3=Hard, 4=Very Hard
```

**Why this design:** the player keeps the UI they expect; the mod just makes those settings deterministic so the strategic brain isn't competing with global multipliers. A player who genuinely wants to override (e.g., for a non-historical experiment) can flip the config — the mod stands down.

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

~12 canonical events. Each = `(date, war-state condition, new officer assignment)`. Fires on weekly strategic review when both gates pass. Won't fire if the named replacement is already in command (idempotent).

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

### 4.7 ObjectiveAdapter

`CampaignObjective` (decompile line 178484) exposes `UniqueObjectiveID`, `ObjectiveAlliance`, `ObjectiveScenario`, `ObjectiveChapters`, `validfromdate` / `validtodate`, `precampaignobjectives`, `objectives` (opaque `List<object>` of target Town / IIP refs), and `influenceprestige`. It does NOT expose theater, category, supply-reach, foreign-recognition, or attrition metadata. The CIC scoring in §5.3 needs these dimensions, so the mod owns an adapter that synthesizes them.

```csharp
public struct ObjectiveMetadata
{
    public Theater  Theater;                    // East / West / TransMiss / Coast / River
    public Category Category;                   // CapitalThreat / SupplyHub / ForeignRecognition / Attrition / RailroadCut / RiverControl / Other
    public float    SupplyReachWeight;          // [0..1] — how much this objective opens supply lines
    public float    ForeignRecognitionWeight;   // [0..1] — diplomatic value (Antietam-class strategic-defensive-victory targets)
    public float    AttritionWeight;            // [0..1] — bleed-the-enemy value
    public float    GeographicCentroidX;        // for distance scoring
    public float    GeographicCentroidY;
}

public static class ObjectiveAdapter
{
    // Hand-coded table keyed by UniqueObjectiveID. Populated during implementation
    // by reading <GTCW>/Modding/ModdingTool_1.11.xlsm objective sheets + per-objective
    // designer judgement. ~50-100 entries expected (full GTCW campaign objective set).
    private static readonly Dictionary<int, ObjectiveMetadata> Table = new()
    {
        // { 1001, new ObjectiveMetadata { Theater = Theater.East, Category = Category.CapitalThreat, ... } },
        // ...populated during implementation...
    };

    public static ObjectiveMetadata Resolve(CampaignObjective obj)
    {
        if (Table.TryGetValue(obj.UniqueObjectiveID, out var meta))
            return meta;
        return Derive(obj);   // fallback for unmapped objectives
    }

    // Geographic fallback: walk obj.objectives, grab any Town/IIP target,
    // pick theater by centroid lat/long bucketing. Category defaults to Other,
    // weights default to 0.5 across the board.
    private static ObjectiveMetadata Derive(CampaignObjective obj) { ... }
}
```

**Why a hand-coded table:** GTCW's vanilla objectives are a fixed, modest-sized set defined by the dev team; the metadata is editorial judgement (Antietam vs Vicksburg vs Gettysburg each have different strategic flavor) that can't be derived purely from geography. The table is small enough to hand-curate and small enough to audit. The geographic fallback is for safety only — it covers DLC additions, modded objectives, or vanilla updates that add new IDs before we update the table.

**Open question for implementation phase (added to §13):** confirm `objectives` (`List<object>`) members at runtime — likely a mix of `Town`, `IIP`, and possibly `BattleSite` references. Adapter geographic fallback needs reflection-safe casts.

## 5. Decision flow

### 5.1 Strategic cadence

Hooked via Postfix on `AICampaign.Update`. The hook reads `BattleUnits.uniStormSystem.dayCounter/monthCounter` plus `BattleUnits.year`, then self-latches into weekly strategic review buckets and monthly heartbeat boundaries.

```
StrategicCoordinator.RunStrategicReview():
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
  // events do NOT cause immediate re-eval; next weekly review processes the dirty bit
```

This deferred-processing rule prevents the AI from thrashing on a chain of events within a few game-days.

### 5.3 CIC.Replan()

```
1. For each obj in CampaignObjective.GetAvailableObjectives(faction):
     meta = ObjectiveAdapter.Resolve(obj)              // see §4.7
     score =
         factionTheaterPreference[meta.Theater]
       + meta.SupplyReachWeight        × supplyContext
       + meta.ForeignRecognitionWeight × factionForeignRecognitionWeight × erage(faction)
       + meta.AttritionWeight          × cic.CasualtyTolerance
       + forceRatioTerm     × (1 - cic.Caution)        // cautious wants higher ratio
       - distanceTerm(meta.GeographicCentroid, friendlyForceCentroid)
                            × (1 - cic.Audacity)       // audacious tolerates distance
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

Theater commanders cannot abandon a phase; they only signal completion (target taken/engaged) or failure (force below threshold). Phase-transition decisions belong to the CIC on weekly strategic review.

## 6. Bridge layer — Harmony patches

**v0.2.0 actual ship state** (10 active + 1 deferred). Patches numbered with stable ordinals (per `docs/patch-catalog.md`); withdrawn/deferred patches keep their slot. Postfix-preferred; Prefix only when the vanilla method directly mutates state we need to overwrite (#1, #6).

**Current v0.2.2 note:** this section preserves the original Slice A design record. The live patch inventory is now authoritative in `docs/patch-catalog.md` and includes #15 `ArmyAreaTheaterPatch`, #16 `ArmyGroupManagementPatch`, weekly strategic review cadence, battle-history observers, front-sector transfer budgets, and concrete #3/#4/#5/#6 steering.

**Critical runtime sequencing (added v0.2.1.1 after smoke-test):** before reading any `CampaignObjective` state in `StrategicCoordinator.OnMonthlyTick`, the coordinator invokes `Policy.CheckForChapterUpdate()` via reflection. Vanilla's per-day cycle calls this method to advance `Policy.CurrentChapter` (initial value `-1`) — but on a fresh-campaign first frame, our `OnMonthlyTick` can fire before vanilla's per-day cycle has run. Without this manual invocation, `CurrentChapter == -1` deactivates every objective (their `ObjectiveChapters` lists don't contain `-1`), `CIC.Replan` returns count=0, and plans never build. Decompile reference: `Policy.CheckForChapterUpdate` at line 211604.

### 6.1 Strategic-core patches

| # | Patch class | Hook type | Game method | Decompile line | Mod state read | Effect |
|---|---|---|---|---|---|---|
| 1 | `PickCampaignObjectivePatch` | **Prefix** | `AICampaign.PickCampaignObjective` | 17769 | CIC's active plan | If active plan has a valid target for this faction, set `aifaction[_aifaction].followedcampaignobjective = plan.TargetObjectiveID` and return `false` to skip vanilla. If no plan / plan stale / faction is player-CIC, return `true` for vanilla random fallback. |
| 2 | `ImportanceValuesPatch` | Postfix | `AIArea.CalculateMostValueableAIZones(int aifaction)` | 10964 | CIC's active plan + ObjectiveAdapter target lookup | After vanilla picks `mostvalueableaiareaclose[aifaction]`, override it to point at the plan-target AIArea. Resolves CampaignObjective UniqueID → first Town/IIP target → world position → vanilla `AICampaign.aiareas.GetColorOnPos(pos, -1f)` → `AIArea.GetAIArea(color)`. (v0.2.0 originally targeted `AICampaign.UpdateImportanceValues`; that method is parameterless, returns `bool`, and is a chunked per-IIP/cbuild/town processor that writes only to `importancevaluestemp` — wrong target. Redesigned for v0.2.1 to consumer-side override; ordinal #2 preserved.) |
| 3-5 | *(originally reserved for v0.2.1)* | — | — | — | — | Superseded by v0.2.2 concrete #3 transfer, #4 defensive-ops, and #5 battle-history observer implementations; see patch catalog. |
| 6 | `CommanderReplacementPatch` | **Prefix** | `AICampaign.CheckAICommanderReplacements` | 17008 | SuccessionScheduler state | Superseded from gate-only: now applies scripted succession swaps with vanilla `AssignCommando` + `DoCommanderPromotion`; see patch catalog. |
| 7-8 | *(reserved)* | — | — | — | — | Still reserved for concrete perk/recruitment steering. |
| 9 | `MonthlyTickHookPatch` | Postfix | `AICampaign.Update` | 11159 | — | Drives `StrategicCoordinator.NotifyDateAdvanced` from per-frame `Update`. Coordinator self-latches on 7-day in-game buckets for CIC review/replan and on month rollover for visible heartbeat, so per-frame call rate is fine. |

### 6.2 Settings-lock patches (added 2026-05-03; spec §3.2)

| # | Patch class | Hook type | Game method | Decompile line | Effect |
|---|---|---|---|---|---|
| 10 | `CampaignParametersLockPatch` | Postfix | `MainMenu.SetCampaignParameters` | 193675 | Final value lock at campaign-create finalize: overwrites `usedcampaignagressiveness=1.0`, `usehistoricaipersonality=true`, `usedcampaignbonus=maxstartbonuscampaign × (LockedDifficulty/4)`, plus `casualtiesmodifier` derived. Logs each overwrite when the player's pick differed. |
| 11 | `AggressivenessSliderLockPatch` | Postfix | `MainMenu.SwitchAIMode(float)` | ~193739 | UI grey-out for campaign-aggressiveness slider. Snaps `aimode = 1.0` and overwrites `aimodetext[].text` to `"Locked:Realism"`. Gated by `gameObject.name != "BattlePanel"` to leave the per-battle "AI Mode: historic vs dynamic" toggle alone. |
| 12 | `HistoricCheckboxLockPatch` | Postfix | `MainMenu.CheckForCheckBoxUpdates` | 193612 | Forces `CheckBoxes[0]` (Historic) ON, `CheckBoxes[1]` (Dynamic) OFF; calls `CheckBox.Freeze(true)` on both for visual grey-out + click-block. Re-invokes `ChooseHistoricPolicies(true)` if the player tried to flip to Dynamic. Syncs `lastcheckboxstates` cache. |
| 13 | `DifficultySliderLockPatch` | Postfix | `MainMenu.ChangeBonus(float)` | ~193786 | UI grey-out for campaign-difficulty slider. Snaps `bonus = maxstartbonuscampaign × (LockedDifficulty/4)`, overwrites `bonustext[].text` to `"Locked:Realism"`. Gated like #11 to leave BattlePanel's per-battle difficulty alone. |
| 14 | `RealismCheckboxesLockPatch` | Postfix | `MainMenu.CheckForCheckBoxUpdates` | 193612 | Forces 5 realism CBs ON + frozen: `FogOfWarCB`, `OrderDelaysCB`, `FeudsCB`, `FullReadinessCB`, `AllAutomanageCB`. Belt-and-suspenders writes `GameVars.usefow=true`, `useorderdelays=true`, `debug_deactivatefeuds=false`, `fullreadiness=false`, and calls `MainMenu.SetAutomanage(true)` directly. |

All settings-lock patches gate on `Plugin.Instance.OverrideVanillaSettings.Value` (default `true`); flipping that config to `false` cleanly disables all 5 in one switch.

### 6.3 Persistence patches (not numbered — symmetric pair)

| Patch | Hook type | Game method | Decompile line | Effect |
|---|---|---|---|---|
| `AICampaignSaveLoadPatch.SavePatch` | Postfix | `AICampaign.Save(string folder)` | 16631 | Writes `<folder>/whiskeyrealism.json` after vanilla save completes. `folder` is CWD-relative to the game install. |
| `AICampaignSaveLoadPatch.LoadPatch` | Postfix | `AICampaign.Load(string folder)` | 16435 | Reads sidecar JSON; falls back to fresh init with explicit log if missing. Calls `OnceLog.Reset()` so first-fire markers re-emit per save-load cycle. |

### 6.4 Player-CIC gate inside patches

Patches that read CIC plan state (#1, future #3) check `StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)` first and return the vanilla-fallback path when true. Belt-and-suspenders to §3.1's coordinator-level gate — if mod state is somehow populated for a player-CIC faction, the patches still hand control back to vanilla.

### 6.5 Patch hygiene

- Postfix preferred; Prefix only when the vanilla method directly mutates state we need to overwrite (#1, #6).
- Reflection for fields/properties via `HarmonyLib.AccessTools` (forward-compat to game updates).
- All reflection wrapped in try/catch + `Plugin.Log.LogWarning(...)`. Never throw from a patch.
- Each patch logs `OnceLog.Info(...)` on first invocation per save-load cycle for smoke-testing.
- Mod state is **read-only** to patches. They steer existing AI; they never mutate `CIC` / `TheaterCommander` / `OperationalPlan`.
- UI patches (#11-#14) carry extra risk of breaking across game updates — `MainMenu` is volatile. If they fail silently after a game patch, players can flip `OverrideVanillaSettings = false` as a workaround until a mod update lands.

## 7. Persistence

### 7.1 Sidecar JSON, not embedded save format

GTCW writes per-save folders, not single-file saves. **Path is CWD-relative (game install dir), NOT `Application.persistentDataPath`** — vanilla `SceneManagement.SaveAll` calls `Directory.CreateDirectory("Campaigns/...")` with a relative path. Our sidecar uses the same convention so it lands beside vanilla's files:

```
<game install>/Campaigns/<level>/<sublevel>/<saveFolder>/scenario.txt   (vanilla)
<game install>/Campaigns/<level>/<sublevel>/<saveFolder>/units.txt      (vanilla)
<game install>/Campaigns/<level>/<sublevel>/<saveFolder>/...            (vanilla)
<game install>/Campaigns/<level>/<sublevel>/<saveFolder>/whiskeyrealism.json   (mod sidecar)
```

Why sidecar:
- GTCW save format is binary; embedding mod state risks save corruption on game updates.
- JSON is debuggable — players can inspect mod state.
- Save-format independence means uninstalling the mod leaves vanilla saves untouched.

### 7.2 Save hook

GTCW has no `SavesManager` class (UBoatCrewMod's pattern doesn't transfer here). The save surface is `SceneManagement.SaveAll(...)` at decompile line 36708, which builds `text = "Campaigns/" + GamePrefs.leveltoload + "/" + GamePrefs.subleveltoload + "/" + Saving.latestfolder + "/"` and dispatches per-subsystem `Save(text)` calls. The cleanest hook is **Postfix on `AICampaign.Save(string folder)` (line 16631)** — symmetric with the load hook, single string argument is exactly the directory we need, fires only when AI state is being persisted (skipped when caller passes `savecampaigndata: false`, e.g. battle-only saves at line 36808).

```csharp
[HarmonyPatch(typeof(AICampaign), nameof(AICampaign.Save))]
public class AICampaignSavePatch
{
    [HarmonyPostfix]
    public static void Postfix(string folder)
    {
        try
        {
            // folder is "Campaigns/<level>/<sublevel>/<saveFolder>/"
            // sidecar lands beside vanilla save files under the game install CWD
            var sidecarPath = Path.Combine(folder, "whiskeyrealism.json");
            StrategicCoordinator.Instance.SaveSidecar(sidecarPath);
        }
        catch (Exception ex) { Plugin.Log.LogError("Sidecar save failed: " + ex); }
    }
}
```

Per-save-folder placement (rather than `<savename>.whiskeyrealism.json` in a Saves/ root) makes the sidecar travel with the save when the player copies/renames/deletes the game's save folder. It also avoids any chance of the mod's filename colliding with a vanilla file: the game writes scenario.txt, units.txt, etc. inside the folder; `whiskeyrealism.json` is a name we own.

### 7.3 Load hook

**Postfix on `AICampaign.Load(string folder)` (line 16435)**, called from the campaign-load batch operation at decompile line 30023 with the same folder shape used by Save. The Postfix reads the sidecar JSON and rehydrates `StrategicCoordinator` state. Theater-commander/CIC instances are recreated from saved personality vectors + officer name lookups against the live `GameVars.commander[]` array (officer assignments may have been altered by mid-career events the mod didn't drive — match by name + faction, fall back to derived personality if no match).

```csharp
[HarmonyPatch(typeof(AICampaign), nameof(AICampaign.Load))]
public class AICampaignLoadPatch
{
    [HarmonyPostfix]
    public static void Postfix(string folder)
    {
        try
        {
            var sidecarPath = Path.Combine(folder, "whiskeyrealism.json");
            if (File.Exists(sidecarPath))
                StrategicCoordinator.Instance.LoadSidecar(sidecarPath);
            else
                StrategicCoordinator.Instance.InitializeFromGameState();   // fresh-career init
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Sidecar load failed, falling back to fresh init: " + ex);
            StrategicCoordinator.Instance.InitializeFromGameState();
        }
    }
}
```

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

Pure strategic ledger logic now has a console harness at `tests/WhiskeyRealism.Tests`. Harmony/game integration still requires manual GTCW smoke-testing. Per-patch first-fire markers use `OnceLog.Info` for smoke-test discipline.

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
- `Plan Trace Logging` → on each strategic review tick, dump CIC's plan reasoning (objective scores, top-3, picked, phases, deadline).
- `Succession Trace Logging` → on each strategic review tick, log every succession event check (date gate result, war-state gate result, fired/not-fired).

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

1. Historical note: v0.2.0 originally searched for a monthly hook; shipped code uses `AICampaign.Update` and self-latches into weekly review/monthly heartbeat.
2. Identify how `aifaction[i].ownunits` exposes army-group-level top units (`unittyp >= 15`?). Map to TheaterCommander assignment at first encounter.
3. Identify `CampaignObjective.GetAvailableObjectives` return semantics (cached? per-faction? recomputed on demand?). Plan-scoring depends on this.
4. Resolved in v0.2.0 smoke: vanilla save folders are CWD-relative to the game install, not `Application.persistentDataPath`; sidecar code must keep using `Path.Combine(folder, "whiskeyrealism.json")`.
5. Verify event dispatcher sources for: town loss, major battle defeat, KIA-on-officer. Likely candidates in `BattleUnits` end-of-battle handling and `CampaignController` event hooks.
6. Theater-commander identification at runtime: is there a stable ID, or do we have to track by `commander.id_hash + arrivaldate`?
7. Phase-decomposition algorithm — section 5.3 says "decompose by geographic prerequisites" but the actual algorithm is left for implementation. Likely shape: from picked objective, walk back through geographic dependencies (target → covering force → supply hub → start position) and treat each step as a phase. Verify this maps cleanly onto GTCW's `AIArea` graph.
8. Personality vector serialization in JSON sidecar — array `[0.7, -0.5, ...]` vs object `{agg:0.7, caut:-0.5, ...}`. Object form is forward-compatible if dimensions are added later. Decide at first persistence implementation.

## 14. Acceptance criteria

The strategic-brain slice is "shippable" when:

1. Build is green (0 warnings, 0 errors).
2. Active bridge-layer patches register and log `[once:...]` first-fire markers in a fresh career; current authoritative count is in `docs/patch-catalog.md`.
3. Smoke-test scenarios 1-7 pass on a single career playthrough (boot, era=Amateur1861, sidecar round-trip, succession event #1 fires by 1862-05, monthly heartbeat appears, weekly review first-fire appears).
4. JSON sidecar is human-readable and round-trips through save → reload without losing state.
5. Mod state is observably read-only from patches (no patch mutates `CIC` / `TheaterCommander` state — verified by code review at PR time).
6. Reflection failures (simulated by deleting a target method's signature in a unit-test stub, or by checking on a manually-corrupted DLL) degrade to vanilla rather than crashing.
7. Player-commanded units are observably untouched by mod's strategic decisions (confirm `dlcw_isundercommander` units are not assigned to plans).
8. **Player-CIC noninterference (§3.1).** When `DLC_WL.IsCommanderInChief()` is true for the player's faction X: (a) `StrategicCoordinator` has no `CIC[X]` instance, (b) no strategic review mutates faction X's plan or theater state, (c) sidecar JSON contains no entry for faction X, (d) all bridge-layer patches return the vanilla-fallback path for faction X. Verified by playing through a CIC-rank promotion event in W&L and confirming via verbose logs.
9. **Settings lock visible in menu (§3.2).** Aggressiveness slider displays `"Locked:Realism"` and stays at neutral; Difficulty slider displays `"Locked:Realism"` and stays at Hard; Historic radio is checked + greyed; FogOfWar / OrderDelays / Feuds / FullReadiness / AllAutomanage all checked + greyed. Flipping `OverrideVanillaSettings = false` in the .cfg restores vanilla menu behavior cleanly.
10. `dist/WhiskeyRealism.dll` deploys cleanly to GTCW with BepInEx 5.4.x x64 UnityMono installed.

## 15. References

- Decompile coordinates: `docs/findings.md`.
- Game install layout, build commands: `AGENTS.md`.
- Roadmap context (slices, current state): `docs/handoff.md`.
- UBoatCrewMod reference architecture: `~/Projects/uboat-crew-mod/AGENTS.md`.
- Brainstorming session decisions: this spec section 2.
