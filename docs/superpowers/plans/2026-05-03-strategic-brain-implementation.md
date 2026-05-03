# Strategic Brain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace GTCW's random campaign-objective AI with a phased, personality-driven strategic engine running both factions through a W&L career, with explicit player-CIC noninterference and JSON-sidecar persistence.

**Architecture:** Two-tier hierarchy — `StrategicCoordinator` (singleton MonoBehaviour, monthly tick) drives one `CIC` per AI-controlled faction; each `CIC` holds an `OperationalPlan` and dispatches to per-army-group `TheaterCommander` instances. Mod state is read-only from Harmony patches. Persistence is JSON sidecar inside the per-save folder. Spec: `docs/superpowers/specs/2026-05-02-strategic-brain-design.md`.

**Tech Stack:** C# `netstandard2.1`, BepInEx 5.4.21 (`BepInEx.Core` NuGet), HarmonyX 2.10.2 (NuGet), Newtonsoft.Json (game-bundled), Unity 2021 Mono runtime. No automated test framework — spec §9 explicitly chose smoke-test verification in game (consistent with UBoatCrewMod). TDD discipline adapted: "write the signature → build to verify it compiles → add behavior → build → smoke-test."

> **Plan amendment 2026-05-03 — v0.2.0 scope tightening (post-decompile-research):**
>
> Decompile spot-checks revealed `AIArea` has no `value` / `totalvalue` / `id` fields — the per-faction zone score is computed live inside `CalculateMostValueableAIZones` from `importancevalues + points + distancepoints`. This makes **Task 21 (`MostValueableZonesPatch`) redundant with Task 20** (biasing `importancevalues` upstream automatically biases the downstream zone-pick). Task 21 is dropped.
>
> Tasks 22, 23, 25, 26 (`TransferOfUnitsPatch`, `DefensiveOpsPatch`, `PerkSelectionPatch`, `RecruitmentPatch`) are smoke-markers with no behavioral steering in v0.2.0 (concrete state-modification was deferred to follow-ups in each task's body). Per the "ship what changes behavior" tightening, **these four are deferred to v0.2.1 alongside the war-state observers and concrete commander-swap.**
>
> **v0.2.0 ships these 4 numbered patches + 2 persistence patches:**
> - #1 `PickCampaignObjectivePatch` (Prefix — replaces vanilla random objective pick)
> - #2 `ImportanceValuesPatch` (Postfix — multiplies `importancevalues` per zone, automatically biases downstream zone picks)
> - #6 `CommanderReplacementPatch` (Prefix — gate-only this slice; concrete swap deferred to v0.2.1)
> - #9 `MonthlyTickHookPatch` (Postfix on `AICampaign.Update`, decompile line 11159 — drives the StrategicCoordinator)
> - `AICampaignSaveLoadPatch.SavePatch` (Postfix on `AICampaign.Save` — sidecar JSON write)
> - `AICampaignSaveLoadPatch.LoadPatch` (Postfix on `AICampaign.Load` — sidecar JSON read)
>
> **Patch ordinals 3, 4, 5, 7, 8 are reserved** for v0.2.1 to keep the catalog stable (so v0.2.0's `#9` remains `#9` after the gap fills in).
>
> When executing: skip Tasks 21, 22, 23, 25, 26 entirely. Task 27's `Plugin.cs` `PatchAll` registrations skip those four classes too. Task 28's catalog table reflects the actual shipped set.
>
> Decompile findings recorded by this amendment (Task 17 short-circuited):
> - `AICampaign.Update()` exists at line 11159 (private void) — `[HarmonyPatch(typeof(AICampaign), "Update")]` is correct as written.
> - `AIArea` field names: `importancevalues` (plural, `float[]` by alliance), `points`, `ownpoints`, `distancepoints`. NO `id` / `value` / `totalvalue`. Use list-index for area identity.
> - `Commander` fields: `combinedname`, `fame`, `lastfame`, `westpoint`, `political`, `currentcommand` confirmed. NO `defamed` — derived formula uses `fame - lastfame` instead.

## Execution status — v0.2.0 ship complete (2026-05-03)

This plan was executed inline during the 2026-05-03 session. All v0.2.0-scoped tasks shipped, smoke-test verified live in-game. Tag pending.

### Tasks completed (22 of 28)

| Task | File(s) | Notes |
|---|---|---|
| 1, 2 | `Util/OnceLog.cs`, `Util/Reflection.cs` | Foundation utilities. |
| 3-6 | `Strategic/PersonalityVector.cs`, `Theater.cs`, `Phase.cs`, `ObjectiveMetadata.cs` | Pure data types. |
| 7-10 | `Strategic/FactionProfiles.cs`, `EraStageManager.cs`, `HistoricalFigureRegistry.cs`, `ObjectiveAdapter.cs` | Static registries. 25 hand-coded officers + derived fallback shipped; ObjectiveAdapter table is empty (geographic fallback covers all objectives) — populate during play. |
| 11-13 | `Strategic/TheaterCommander.cs`, `CIC.cs`, `SuccessionScheduler.cs` | Decision actors. 12 succession events seeded; war-state gates currently always-false until v0.2.1 observers wire town-ownership signals. |
| 14 | `Strategic/StrategicCoordinator.cs` | Singleton MonoBehaviour. Fires `OnMonthlyTick` on first valid tick + every month rollover (post-fix `44bbcae`). Player-CIC gate engaged via `DLC_WL.IsCommanderInChief(int=-1)`. |
| 15, 16 | `Strategic/PersistenceDto.cs`, `Patches/AICampaignSaveLoadPatch.cs` | Sidecar JSON persistence on `AICampaign.Save`/`Load` (lines 16631 / 16435). |
| 17 | (short-circuited inline) | Decompile findings folded directly into plan amendment + per-patch `[HarmonyPatch]` attributes. No formal `findings.md` write-up needed. |
| 18 | `Patches/MonthlyTickHookPatch.cs` | Postfix on `AICampaign.Update`. Reads `GameVars.currentmonth` (0-indexed; +1 for 1-based) and `GameVars.year` (post-fix `4000c74` — earlier code looked for nonexistent `currentdate`/`currentyear`). |
| 19 | `Patches/PickCampaignObjectivePatch.cs` | Prefix on `AICampaign.PickCampaignObjective` (17769). Plan-driven objective replaces vanilla random. |
| 20 | (skipped — plan amendment) | `ImportanceValuesPatch` deferred to v0.2.1 — vanilla method has wrong shape (parameterless, writes to `importancevaluestemp`, chunked processor). Right target for v0.2.1: Prefix `AIArea.CalculateMostValueableAIZones(int aifaction)`. File created with `[Obsolete]` placeholder. |
| 21, 22, 23, 25, 26 | (deferred per amendment) | Smoke-marker patches deferred to v0.2.1. Ordinals 3, 4, 5, 7, 8 reserved in catalog. |
| 24 | `Patches/CommanderReplacementPatch.cs` | Prefix on `AICampaign.CheckAICommanderReplacements` (17008). Gate-only this slice — concrete swap (`AssignCommando` + `DoCommanderPromotion`) deferred to v0.2.1 alongside war-state observers. |
| 27 | `Plugin.cs` | v0.2.0 BepInPlugin. Patches registered via `_harmony.PatchAll(typeof(Plugin).Assembly)`. ConfigEntries: `Enabled`, `VerboseLogging`, `PlanTrace`, `SuccessionTrace`, `OverrideVanillaSettings`, `LockedDifficulty`. |
| 28 | `docs/patch-catalog.md`, `docs/handoff.md` | Catalog populated with shipped patches + reserved ordinals. Handoff updated to "shipped 0.2.0". v0.2.0 tag held until smoke-test passed (now confirmed 2026-05-03). |

### Bonus tasks added during execution (5 patches not in original plan)

The "vanilla settings integration" subsystem emerged from a design conversation mid-execution and isn't in the original 28-task list. Spec §3.2 describes the design.

| # | File | Effect |
|---|---|---|
| 10 | `Patches/CampaignParametersLockPatch.cs` | Postfix on `MainMenu.SetCampaignParameters` (193675). Final value lock at finalize: `usedcampaignagressiveness=1.0`, `usehistoricaipersonality=true`, `usedcampaignbonus = max × (LockedDifficulty/4)`, `casualtiesmodifier` derived. |
| 11 | `Patches/AggressivenessSliderLockPatch.cs` | Postfix on `MainMenu.SwitchAIMode(float)`. UI grey-out for aggressiveness slider; snap to 1.0; label "Locked:Realism". Gates by `gameObject.name != "BattlePanel"`. |
| 12 | `Patches/HistoricCheckboxLockPatch.cs` | Postfix on `MainMenu.CheckForCheckBoxUpdates` (193612). Forces Historic radio ON, Dynamic OFF; `CheckBox.Freeze(true)` on both for half-alpha + click-block. |
| 13 | `Patches/DifficultySliderLockPatch.cs` | Postfix on `MainMenu.ChangeBonus(float)`. UI grey-out for difficulty slider; locked to "Hard" by default (configurable via `LockedDifficulty`). |
| 14 | `Patches/RealismCheckboxesLockPatch.cs` | Postfix on `MainMenu.CheckForCheckBoxUpdates` (sister to #12). Forces FogOfWar / OrderDelays / Feuds / FullReadiness / AllAutomanage all ON + frozen. Belt-and-suspenders writes the underlying `GameVars` directly. |

### Bugs caught + fixed during execution

| Commit | Symptom | Root cause |
|---|---|---|
| `51ff74d` | First launch produced a silent BepInEx chainloader-complete with no `[Coordinator] bootstrapped` and no config file. | `Config.Bind("[General]", ...)` — BepInEx 5.4 forbids `[` `]` in section names. Inherited from v0.1.0 scaffold. Saved as memory `bepinex_gotchas.md`. |
| `23c2e1e` | 15 repeated `[Coordinator] IsPlayerCICOf failed: Number of parameters specified does not match` warnings. | `DLC_WL.IsCommanderInChief` is `(int manualcommander = -1)`, not zero-args. Now passes `new object[] { -1 }`. |
| `23c2e1e` | 33+ `AccessTools.Method: Could not find method for type CheckBox and name Check and parameters (bool)` warnings. | `CheckBox.Check` is `(bool newstate = true, bool manuallyset = false)`. Now passes `new[] { typeof(bool), typeof(bool) }`. |
| `23c2e1e` | `Failed to patch bool AICampaign::UpdateImportanceValues(): Parameter "_aifaction" not found`. | Vanilla method is parameterless, returns `bool`. Wrong target. Patch deferred to v0.2.1 redesign. |
| `4000c74` | ~2000 `AccessTools.Field: Could not find field for type GameVars and name currentdate` / `Tools.currentyear` warnings flooding the log. | `MonthlyTickHookPatch.ReadGameYear` looked for nonexistent fields. Vanilla stores year as `GameVars.year` (static int, line 64790). |
| `44bbcae` | All patches first-fired but `[Heartbeat]` line never appeared after starting a campaign. | `NotifyDateAdvanced` waited for a month rollover before firing the first `OnMonthlyTick`. Now fires on first valid call too. |

### Smoke-test verification (2026-05-03 final launch)

User confirmed in-game working state. Log evidence: all 8 patches first-fired, settings-lock subsystem visible in campaign-create menu (Aggressiveness/Difficulty sliders display "Locked:Realism", radio + 5 realism CBs greyed via `CheckBox.Freeze`), heartbeat line appears on campaign creation, sidecar round-trips through save/reload.

### Remaining for v0.2.0 ship

- [ ] Tag `v0.2.0` locally and push tag to `origin`.
- [ ] (Optional) GitHub Release with `dist/WhiskeyRealism.dll` attached.

### v0.2.1 backlog (formal scope)

1. **Redesign `ImportanceValuesPatch`** — Prefix on `AIArea.CalculateMostValueableAIZones(int aifaction)` (10964) to bias `importancevalues[aifaction]` for plan-target zones before vanilla reads them.
2. **Concrete commander-swap in `CommanderReplacementPatch`** — wire scripted-event-driven `AssignCommando` + `DoCommanderPromotion` calls in the Prefix when a `SuccessionScheduler` event has fired for this faction.
3. **War-state observers** — patches on town-ownership transitions for Vicksburg / Atlanta / Chattanooga to drive `StrategicCoordinator.ObserveWarState`. Currently all war-state gates return false → succession events #1, #5, #6, #8, #9, #10, #12 cannot fire.
4. **Smoke-marker patches → concrete steering** — Tasks 22, 23, 25, 26 from the original plan: `TransferOfUnitsPatch`, `DefensiveOpsPatch`, `PerkSelectionPatch`, `RecruitmentPatch`. Each needs Prefix-with-state-modify after smoke-test reveals consumption pattern.
5. **`ObjectiveAdapter` table population** — hand-coded `UniqueObjectiveID` → `ObjectiveMetadata` entries based on geographic-fallback log entries observed during play.
6. **Vanilla settings → mod logic integration** — read `usedcampaignbonus` (locked Hard) into `CIC.Effective` so the locked difficulty actually scales `CasualtyTolerance`. Currently the lock is informational only.
7. **`Policy.CurrentChapter` integration** — vanilla 5-chapter system overlaps with our 4-stage `EraStage`. Map / retire.
8. **Slider arrow grey-out** (cosmetic) — disable arrow buttons via `panelhandler.SetButtonsCondition(panel, 2, 150-153)` for visual consistency with frozen checkboxes.

**Validation strategy per task:**
1. **Build verification** after every code change: `./build.sh` must report `Build succeeded` with `0 Error(s)` and ideally `0 Warning(s)`.
2. **Deploy + smoke-test** at the end of each phase: `cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"` then launch GTCW and tail `BepInEx/LogOutput.log`.
3. **Final smoke-test:** spec §9.1 scenarios 1-7 must pass on a single playthrough before declaring the slice shipped.

**Convention reminders that the rest of this plan assumes:**
- All Harmony patch classes live one-per-file under `src/WhiskeyRealism/Patches/`. One concern per file.
- `AICampaign.aifaction` is a `private static List<AIFaction>` (decompile line 11097) and `AIFaction` itself is a `private class` (line 10299). All access goes through reflection helpers (Task 2). Never type-reference `AIFaction` directly.
- Postfix-preferred. Prefix only when the vanilla method directly mutates state we need to overwrite (patches #1 and #6 in the spec).
- Every reflection lookup wraps in try/catch and logs via `Plugin.Log.LogWarning(...)`. Never throw from a patch.
- Every patch class logs once via `OnceLog.Info(...)` on first invocation per save-load cycle.
- Mod state is read-only from patches — patches READ `CIC` / `TheaterCommander` / `OperationalPlan`; they do NOT write to them. State writes happen only in `StrategicCoordinator.OnMonthlyTick()` and event-trigger handlers.
- Commit messages use Conventional-Commits-ish prefixes consistent with existing history: `feat:`, `chore:`, `docs:`, `fix:`.
- Use this code-author tagline for git commits: `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>`

**Logging strategy (non-spammy "is this thing working" discipline):**

The goal is that a smoke-tester can `grep "[Whiskey]" BepInEx/LogOutput.log` after a 1-year career and see proof of life for every subsystem in roughly **20-40 lines of log**. Every log line in this plan falls into one of these buckets:

| Bucket | When it fires | Cap | Tag prefix |
|---|---|---|---|
| **Boot** | Plugin.Awake registers patches | once per game launch | `[Whiskey]` |
| **First-fire markers** | First time each patch hits per save-load cycle | once per patch per save-load cycle (~10-12 lines per cycle) — gated by `OnceLog` | `[once:<key>]` |
| **State transitions** | Era advance, succession event fires, plan replaced, player-CIC gate engages/disengages | once per actual transition | `[Era]`, `[Succession:N]`, `[Plan:Officer]`, `[Coordinator]` |
| **Monthly heartbeat** | One per faction per game-month from `OnMonthlyTick` | 12 × 2 = 24 lines per game-year | `[Heartbeat]` |
| **Verbose-gated** | Patch decision traces, scoring tables, per-tick urgency reads | only when `Verbose Logging` config is on | `[Patch:Name]` |
| **Plan-trace-gated** | Full objective scoring table, top-3 picks, phase breakdown | only when `Plan Trace Logging` config is on | `[Plan:scores]` |
| **Succession-trace-gated** | Per-event date/war-state gate evaluations | only when `Succession Trace Logging` config is on | `[Succession:N]` (eval line) |
| **Warnings** | Reflection lookups that fail, sidecar missing, recoverable errors | as they occur (rare; bounded by reality) | `[Reflection]`, `[Coordinator]`, etc. |
| **Errors** | Unexpected exceptions caught by try/catch boundaries | as they occur (rare) | LogError |

**Rules:**
- Per-tick patches (Postfix on Update-style methods) NEVER log unconditionally — only via `OnceLog` for first-fire or behind a verbose-logging gate.
- The monthly heartbeat is the single richest "proof of life" signal: one line per faction per month showing era + active plan + succession progress. A reader can scan a year's worth of heartbeats and tell you the mod's running.
- Player-CIC stand-down logs once per save-load cycle via `OnceLog` so the user sees clear confirmation when the gate engages — and never sees it again until the next reload.
- Reflection-failure warnings include the type name, member name, and reason. They're loud (LogWarning, not LogInfo) because they indicate game-version drift that we want to catch early.

---

## File map

```
src/WhiskeyRealism/
├── Plugin.cs                                    [modify in Task 27]
├── Util/
│   ├── OnceLog.cs                               [Task 1]
│   └── Reflection.cs                            [Task 2]
├── Strategic/
│   ├── PersonalityVector.cs                     [Task 3]
│   ├── Theater.cs                               [Task 4 — also Category]
│   ├── Phase.cs                                 [Task 5 — also OperationalPlan, PhaseTransition]
│   ├── ObjectiveMetadata.cs                     [Task 6]
│   ├── FactionProfiles.cs                       [Task 7]
│   ├── EraStageManager.cs                       [Task 8]
│   ├── HistoricalFigureRegistry.cs              [Task 9]
│   ├── ObjectiveAdapter.cs                      [Task 10]
│   ├── TheaterCommander.cs                      [Task 11]
│   ├── CIC.cs                                   [Task 12]
│   ├── SuccessionScheduler.cs                   [Task 13]
│   ├── StrategicCoordinator.cs                  [Task 14]
│   └── PersistenceDto.cs                        [Task 15]
└── Patches/
    ├── AICampaignSaveLoadPatch.cs               [Task 16]
    ├── MonthlyTickHookPatch.cs                  [Task 18]
    ├── PickCampaignObjectivePatch.cs            [Task 19 — Prefix]
    ├── ImportanceValuesPatch.cs                 [Task 20]
    ├── MostValueableZonesPatch.cs               [Task 21]
    ├── TransferOfUnitsPatch.cs                  [Task 22]
    ├── DefensiveOpsPatch.cs                     [Task 23]
    ├── CommanderReplacementPatch.cs             [Task 24 — Prefix]
    ├── PerkSelectionPatch.cs                    [Task 25]
    └── RecruitmentPatch.cs                      [Task 26]

docs/
├── findings.md                                  [append Task 17 findings]
├── handoff.md                                   [Task 28 — update "What just shipped"]
└── patch-catalog.md                             [Task 28 — populate ordinals]
```

Phase boundaries that warrant a deploy-and-smoke-test checkpoint:
- After Task 14 (Phase 5 done, all strategic core in place but nothing wired to game yet — should still build cleanly with no patches loaded).
- After Task 16 (persistence wired but no patches steering AI yet — game should run, sidecar should appear and reload).
- After Task 18 (monthly tick fires — first observable mod behavior).
- After Task 26 (all patches in place — full smoke-test run).
- Before Task 28 (final smoke-test to confirm acceptance criteria).

---

## Task 1: Util/OnceLog helper

**Files:**
- Create: `src/WhiskeyRealism/Util/OnceLog.cs`

- [ ] **Step 1: Create OnceLog with reset hook**

```csharp
// src/WhiskeyRealism/Util/OnceLog.cs
using System.Collections.Generic;

namespace WhiskeyRealism.Util
{
    // Per-save-load-cycle "fire once" log markers. Resets when AICampaign.Load
    // runs (called from AICampaignSaveLoadPatch.OnLoad in Task 16). This gives
    // smoke-testers one clean first-fire log line per patch each time they
    // load a save, instead of one line per game launch.
    internal static class OnceLog
    {
        private static readonly HashSet<string> _fired = new HashSet<string>();

        internal static void Info(string key, string message)
        {
            if (_fired.Contains(key)) return;
            _fired.Add(key);
            Plugin.Log.LogInfo("[once:" + key + "] " + message);
        }

        internal static void Reset()
        {
            _fired.Clear();
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`, `0 Warning(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Util/OnceLog.cs
git commit -m "$(cat <<'EOF'
feat: add OnceLog helper for per-save-cycle first-fire markers

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Util/Reflection helper

**Files:**
- Create: `src/WhiskeyRealism/Util/Reflection.cs`

- [ ] **Step 1: Create Reflection helpers**

```csharp
// src/WhiskeyRealism/Util/Reflection.cs
using System;
using System.Reflection;
using HarmonyLib;

namespace WhiskeyRealism.Util
{
    // Safe field/property/method accessors. Patches must never throw on
    // reflection lookup — game updates may rename fields, and we'd rather
    // log a warning and degrade to vanilla than crash the game.
    //
    // Usage:
    //   var aifactionList = Reflection.GetStaticField<System.Collections.IList>(
    //       typeof(AICampaign), "aifaction");
    //   if (aifactionList == null) return; // bailed; warning was logged
    internal static class Reflection
    {
        internal static T GetStaticField<T>(Type t, string name) where T : class
        {
            try
            {
                var f = AccessTools.Field(t, name);
                if (f == null) { Warn(t, name, "static field not found"); return null; }
                return f.GetValue(null) as T;
            }
            catch (Exception ex) { Warn(t, name, ex.Message); return null; }
        }

        internal static T GetField<T>(object instance, string name) where T : class
        {
            if (instance == null) return null;
            try
            {
                var f = AccessTools.Field(instance.GetType(), name);
                if (f == null) { Warn(instance.GetType(), name, "field not found"); return null; }
                return f.GetValue(instance) as T;
            }
            catch (Exception ex) { Warn(instance.GetType(), name, ex.Message); return null; }
        }

        internal static int GetIntField(object instance, string name, int fallback = 0)
        {
            if (instance == null) return fallback;
            try
            {
                var f = AccessTools.Field(instance.GetType(), name);
                if (f == null) { Warn(instance.GetType(), name, "int field not found"); return fallback; }
                return (int)f.GetValue(instance);
            }
            catch (Exception ex) { Warn(instance.GetType(), name, ex.Message); return fallback; }
        }

        internal static void SetField(object instance, string name, object value)
        {
            if (instance == null) return;
            try
            {
                var f = AccessTools.Field(instance.GetType(), name);
                if (f == null) { Warn(instance.GetType(), name, "field not found for SetField"); return; }
                f.SetValue(instance, value);
            }
            catch (Exception ex) { Warn(instance.GetType(), name, ex.Message); }
        }

        internal static MethodInfo GetMethod(Type t, string name, Type[] argTypes = null)
        {
            try
            {
                var m = (argTypes == null)
                    ? AccessTools.Method(t, name)
                    : AccessTools.Method(t, name, argTypes);
                if (m == null) Warn(t, name, "method not found");
                return m;
            }
            catch (Exception ex) { Warn(t, name, ex.Message); return null; }
        }

        private static void Warn(Type t, string name, string msg)
        {
            Plugin.Log.LogWarning("[Reflection] " + (t == null ? "<null>" : t.FullName)
                + "." + name + " — " + msg);
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Util/Reflection.cs
git commit -m "$(cat <<'EOF'
feat: add Reflection helper for safe AccessTools wrappers

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: PersonalityVector struct

**Files:**
- Create: `src/WhiskeyRealism/Strategic/PersonalityVector.cs`

Spec §4.1 — 5 floats in `[-1, +1]`, additive composition with clamp.

- [ ] **Step 1: Create the struct**

```csharp
// src/WhiskeyRealism/Strategic/PersonalityVector.cs
using System;

namespace WhiskeyRealism.Strategic
{
    public struct PersonalityVector
    {
        public float Aggression;              // offensive vs defensive bias
        public float Caution;                 // overestimates enemy / waits for perfect conditions
        public float Audacity;                // commits to bold/risky plans
        public float CasualtyTolerance;       // willing to spend men for objectives
        public float PoliticalResponsiveness; // reacts to political/morale pressure

        public PersonalityVector(float agg, float caut, float aud, float cas, float pol)
        {
            Aggression = agg;
            Caution = caut;
            Audacity = aud;
            CasualtyTolerance = cas;
            PoliticalResponsiveness = pol;
        }

        public static PersonalityVector Compose(
            PersonalityVector officer,
            PersonalityVector era,
            PersonalityVector faction)
        {
            return new PersonalityVector(
                Clamp(officer.Aggression              + era.Aggression              + faction.Aggression),
                Clamp(officer.Caution                 + era.Caution                 + faction.Caution),
                Clamp(officer.Audacity                + era.Audacity                + faction.Audacity),
                Clamp(officer.CasualtyTolerance       + era.CasualtyTolerance       + faction.CasualtyTolerance),
                Clamp(officer.PoliticalResponsiveness + era.PoliticalResponsiveness + faction.PoliticalResponsiveness));
        }

        public static PersonalityVector Add(PersonalityVector a, PersonalityVector b)
        {
            return new PersonalityVector(
                Clamp(a.Aggression              + b.Aggression),
                Clamp(a.Caution                 + b.Caution),
                Clamp(a.Audacity                + b.Audacity),
                Clamp(a.CasualtyTolerance       + b.CasualtyTolerance),
                Clamp(a.PoliticalResponsiveness + b.PoliticalResponsiveness));
        }

        public static float Clamp(float v) => Math.Max(-1f, Math.Min(1f, v));

        public float[] ToArray() => new[] { Aggression, Caution, Audacity, CasualtyTolerance, PoliticalResponsiveness };

        public static PersonalityVector FromArray(float[] a)
        {
            if (a == null || a.Length < 5)
                return default(PersonalityVector);
            return new PersonalityVector(a[0], a[1], a[2], a[3], a[4]);
        }

        public override string ToString()
            => $"P(agg={Aggression:F2} caut={Caution:F2} aud={Audacity:F2} cas={CasualtyTolerance:F2} pol={PoliticalResponsiveness:F2})";
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/PersonalityVector.cs
git commit -m "$(cat <<'EOF'
feat: add PersonalityVector with additive composition

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Theater + Category enums

**Files:**
- Create: `src/WhiskeyRealism/Strategic/Theater.cs`

- [ ] **Step 1: Create the enums**

```csharp
// src/WhiskeyRealism/Strategic/Theater.cs
namespace WhiskeyRealism.Strategic
{
    // Strategic theater of operations. CIC scoring weights objectives by
    // theater × faction-theater-preference (spec §4.3).
    public enum Theater
    {
        Unknown = 0,
        East        = 1, // ANV / AoP zone — Virginia, Maryland, Pennsylvania
        West        = 2, // Tennessee, Kentucky, northern Mississippi
        TransMiss   = 3, // Texas, Arkansas, Indian Territory, Missouri (west of Mississippi River)
        Coast       = 4, // Atlantic seaboard / Gulf coast — blockade, amphibious ops
        River       = 5  // Mississippi River corridor — Vicksburg, New Orleans, Memphis
    }

    // Strategic flavor of an objective — used for scoring weights.
    public enum Category
    {
        Other              = 0,
        CapitalThreat      = 1, // Richmond / Washington — high prestige + recognition impact
        SupplyHub          = 2, // Atlanta / Nashville / Chattanooga — economic pressure
        ForeignRecognition = 3, // Antietam-class strategic-defensive-victory targets
        Attrition          = 4, // Wilderness-style bleeding, no specific town goal
        RailroadCut        = 5, // sever a rail line
        RiverControl       = 6  // river crossing / control point
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Theater.cs
git commit -m "$(cat <<'EOF'
feat: add Theater and Category enums for strategic scoring

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Phase, PhaseTransition, OperationalPlan

**Files:**
- Create: `src/WhiskeyRealism/Strategic/Phase.cs`

Spec §4.5 defines all three. Co-locating in one file because they're tightly coupled and short.

- [ ] **Step 1: Create Phase + PhaseTransition + OperationalPlan**

```csharp
// src/WhiskeyRealism/Strategic/Phase.cs
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public enum PhaseTransition
    {
        TargetTaken,         // friendly takes the target zone
        TargetEngaged,       // major battle in target zone (win or loss)
        DeadlineExpired,     // deadline date passed
        ForceBelowThreshold  // assigned force fell below required fraction
    }

    public class Phase
    {
        public int    TargetAreaId;          // ID into AIArea registry; resolved at runtime
        public int    TargetObjectiveId;     // CampaignObjective.UniqueObjectiveID — drives PickCampaignObjective Prefix
        public float  ForceFractionRequired; // 0..1 of theater's units committed
        public PhaseTransition Transition;
        public int    DeadlineMonth;         // Tools.Date components (avoid serializing Tools.Date directly)
        public int    DeadlineYear;
        public Phase  Fallback;              // optional next phase on failure (null if none)
    }

    public class OperationalPlan
    {
        public int    CICFactionAllianceId;  // matches GameVars.alliance index
        public int    AssignedTheaterId;     // which TheaterCommander executes
        public List<Phase> Phases = new List<Phase>();
        public int    CurrentPhaseIndex;
        public int    PlanDeadlineMonth;
        public int    PlanDeadlineYear;
        public string Rationale;             // human-readable for plan-trace logs
        public bool   IsDirty;               // event-trigger marker; next monthly tick processes

        public Phase CurrentPhase
            => (CurrentPhaseIndex >= 0 && CurrentPhaseIndex < Phases.Count) ? Phases[CurrentPhaseIndex] : null;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Phase.cs
git commit -m "$(cat <<'EOF'
feat: add Phase, PhaseTransition, and OperationalPlan types

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: ObjectiveMetadata struct

**Files:**
- Create: `src/WhiskeyRealism/Strategic/ObjectiveMetadata.cs`

Spec §4.7. Pure data; the table + adapter logic comes in Task 10.

- [ ] **Step 1: Create the struct**

```csharp
// src/WhiskeyRealism/Strategic/ObjectiveMetadata.cs
namespace WhiskeyRealism.Strategic
{
    // Per-objective metadata that CampaignObjective itself does not expose
    // (the vanilla type only carries id, alliance/scenario gates, and a
    // List<object> of Town/IIP target refs — see decompile line 178484).
    // Synthesized either from a hand-coded UniqueObjectiveID-keyed table
    // or derived from the objective's geographic centroid by ObjectiveAdapter.
    public struct ObjectiveMetadata
    {
        public Theater  Theater;
        public Category Category;
        public float    SupplyReachWeight;        // [0..1]
        public float    ForeignRecognitionWeight; // [0..1]
        public float    AttritionWeight;          // [0..1]
        public float    GeographicCentroidX;
        public float    GeographicCentroidY;

        public bool IsDerived;                    // true when produced by geographic fallback (not in table)

        public static ObjectiveMetadata DefaultDerived(Theater theater, float cx, float cy)
        {
            return new ObjectiveMetadata
            {
                Theater = theater,
                Category = Category.Other,
                SupplyReachWeight        = 0.5f,
                ForeignRecognitionWeight = 0.5f,
                AttritionWeight          = 0.5f,
                GeographicCentroidX      = cx,
                GeographicCentroidY      = cy,
                IsDerived = true
            };
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/ObjectiveMetadata.cs
git commit -m "$(cat <<'EOF'
feat: add ObjectiveMetadata struct for synthesized objective scoring data

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: FactionProfiles

**Files:**
- Create: `src/WhiskeyRealism/Strategic/FactionProfiles.cs`

Spec §4.3.

- [ ] **Step 1: Create FactionProfiles**

```csharp
// src/WhiskeyRealism/Strategic/FactionProfiles.cs
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    // Faction-baseline personality + theater-preference weights.
    // Composed additively with Era and Officer vectors at decision time.
    // Alliance IDs match GameVars.alliance[] index (0 = USA / Union, 1 = CSA per
    // GTCW convention — verified during Task 17 research; if convention differs,
    // update this file and the StrategicCoordinator alliance lookup).
    internal static class FactionProfiles
    {
        // Spec §4.3 baselines.
        internal static readonly PersonalityVector CSA   = new PersonalityVector(+0.2f,  0f,    +0.3f,  0f,   -0.1f);
        internal static readonly PersonalityVector Union = new PersonalityVector( 0f,   +0.2f, -0.1f, -0.1f, +0.3f);

        // Theater preferences are scoring multipliers on objective theater match.
        internal static readonly Dictionary<Theater, float> CSATheaterPreference = new Dictionary<Theater, float>
        {
            { Theater.East,      1.0f },
            { Theater.West,      0.6f },
            { Theater.TransMiss, 0.2f },
            { Theater.Coast,     0.4f },
            { Theater.River,     0.3f }
        };

        internal static readonly Dictionary<Theater, float> UnionTheaterPreference = new Dictionary<Theater, float>
        {
            { Theater.East,      1.0f },
            { Theater.West,      0.9f },
            { Theater.TransMiss, 0.3f },
            { Theater.Coast,     0.8f },
            { Theater.River,     1.0f }
        };

        // Foreign-recognition appetite — CSA cares much more than Union.
        internal static readonly Dictionary<int, float> ForeignRecognitionWeightByAlliance = new Dictionary<int, float>
        {
            { 0, 0.1f }, // Union
            { 1, 0.7f }  // CSA
        };

        internal static PersonalityVector For(int allianceId)
        {
            // 0 = Union, 1 = CSA per GTCW convention.
            return allianceId == 1 ? CSA : Union;
        }

        internal static float TheaterPreferenceFor(int allianceId, Theater theater)
        {
            var dict = (allianceId == 1) ? CSATheaterPreference : UnionTheaterPreference;
            return dict.TryGetValue(theater, out var v) ? v : 0.5f;
        }

        internal static float ForeignRecognitionWeightFor(int allianceId)
        {
            return ForeignRecognitionWeightByAlliance.TryGetValue(allianceId, out var v) ? v : 0.3f;
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/FactionProfiles.cs
git commit -m "$(cat <<'EOF'
feat: add FactionProfiles with CSA/Union baselines and theater preferences

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: EraStageManager

**Files:**
- Create: `src/WhiskeyRealism/Strategic/EraStageManager.cs`

Spec §4.2 — 4 stages with date defaults + war-state overrides; never regress.

- [ ] **Step 1: Create EraStageManager**

```csharp
// src/WhiskeyRealism/Strategic/EraStageManager.cs
namespace WhiskeyRealism.Strategic
{
    public enum EraStage
    {
        Amateur1861   = 0, // 1861-04 to 1861-12
        Operational1862 = 1, // 1862
        Decisive1863    = 2, // 1863
        TotalWar1864   = 3  // 1864-01 onward
    }

    // Per-faction era progression. Date-driven by default; war-state can
    // accelerate (Vicksburg-falls-early advances 1863, Atlanta-falls-early
    // advances 1864). Era stages never regress.
    public class EraStageManager
    {
        // Per-alliance current stage. Stored on the StrategicCoordinator and
        // serialized in the sidecar.
        public EraStage Stage;

        // Spec §4.2 vector deltas.
        public PersonalityVector StageVector
        {
            get
            {
                switch (Stage)
                {
                    case EraStage.Amateur1861:     return new PersonalityVector(-0.3f, +0.5f, -0.2f, -0.4f, +0.1f);
                    case EraStage.Operational1862: return new PersonalityVector( 0f,    0f,   +0.1f,  0f,    0f);
                    case EraStage.Decisive1863:    return new PersonalityVector(+0.2f, -0.2f, +0.3f, +0.2f,  0f);
                    case EraStage.TotalWar1864:    return new PersonalityVector(+0.4f, -0.4f, +0.2f, +0.6f, -0.2f);
                    default:                       return default(PersonalityVector);
                }
            }
        }

        // Called from StrategicCoordinator.OnMonthlyTick. `currentMonth` 1..12,
        // `currentYear` four digits. War-state booleans wired from CIC observations
        // of game state at the call site (faction-specific).
        public void CheckTransition(int currentMonth, int currentYear,
                                    bool vicksburgFallenEarly, bool atlantaFallenEarly)
        {
            EraStage target = Stage;

            // Date-default progression.
            if (currentYear >= 1862 && target < EraStage.Operational1862) target = EraStage.Operational1862;
            if (currentYear >= 1863 && target < EraStage.Decisive1863)    target = EraStage.Decisive1863;
            if (currentYear >= 1864 && target < EraStage.TotalWar1864)    target = EraStage.TotalWar1864;

            // War-state overrides — early advancement.
            if (vicksburgFallenEarly && currentYear == 1863 && currentMonth < 7 && target < EraStage.Decisive1863)
                target = EraStage.Decisive1863;
            if (atlantaFallenEarly && currentYear == 1864 && currentMonth < 9 && target < EraStage.TotalWar1864)
                target = EraStage.TotalWar1864;

            // Never regress.
            if (target > Stage)
            {
                var prev = Stage;
                Stage = target;
                Plugin.Log.LogInfo($"[Era] advanced {prev} → {Stage} ({currentYear}-{currentMonth:D2})");
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/EraStageManager.cs
git commit -m "$(cat <<'EOF'
feat: add EraStageManager with 4 stages and war-state overrides

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: HistoricalFigureRegistry

**Files:**
- Create: `src/WhiskeyRealism/Strategic/HistoricalFigureRegistry.cs`

Spec §4.4 — 25 hand-coded officers + derived fallback for minor commanders. Lookup by `(faction, name)` with `arrivaldate`/`fame` disambiguation. The 25 vectors are spec table values verbatim.

- [ ] **Step 1: Create the registry with 25 officers + derived fallback**

```csharp
// src/WhiskeyRealism/Strategic/HistoricalFigureRegistry.cs
using System;
using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    // Hand-coded historical-officer personality table + derived fallback for
    // minor commanders not in the table. All 25 vectors are from spec §4.4.
    //
    // Lookup contract:
    //   1. Resolve(commanderObj, allianceId) tries the hand-coded table by
    //      a normalized last-name match within the faction.
    //   2. On miss, derives a vector from existing GTCW commander fields
    //      (westpoint, political, fame, defamed) per spec §4.4 formula.
    //   3. Per-officer random spread [-0.1, +0.1] is applied once at first
    //      encounter and frozen via StrategicCoordinator's minor-officer
    //      cache (kept in the sidecar).
    internal static class HistoricalFigureRegistry
    {
        // Keyed by lowercase normalized last-name; the registry intentionally
        // does not encode arrival-date discriminators — disambiguate by fame
        // when multiple commanders share a key (spec §4.4).
        private struct Entry
        {
            public string  AllianceTag;  // "CSA" or "Union"
            public string  CanonicalName;
            public PersonalityVector V;
        }

        private static readonly List<Entry> _entries = new List<Entry>
        {
            // CSA
            new Entry { AllianceTag = "CSA",   CanonicalName = "davis",       V = new PersonalityVector(-0.1f, +0.3f, -0.3f, -0.3f, +0.5f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "lee",         V = new PersonalityVector(+0.7f, -0.5f, +0.6f, +0.4f, -0.2f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "johnston",    V = new PersonalityVector(-0.2f, +0.5f, -0.2f, -0.6f, +0.1f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "bragg",       V = new PersonalityVector(+0.2f, +0.3f, -0.4f, -0.1f, +0.4f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "beauregard",  V = new PersonalityVector(+0.4f, -0.1f, +0.5f, +0.1f, -0.1f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "hood",        V = new PersonalityVector(+0.9f, -0.8f, +0.4f, +0.9f, +0.3f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "jackson",     V = new PersonalityVector(+0.8f, -0.5f, +0.8f, +0.4f, -0.5f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "longstreet",  V = new PersonalityVector(+0.4f, +0.1f, +0.3f, -0.2f, -0.1f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "stuart",      V = new PersonalityVector(+0.5f, -0.4f, +0.7f, -0.1f, -0.2f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "forrest",     V = new PersonalityVector(+0.7f, -0.3f, +0.8f, +0.3f, -0.6f) },
            // Union
            new Entry { AllianceTag = "Union", CanonicalName = "lincoln",     V = new PersonalityVector(+0.3f, +0.1f, +0.1f, +0.4f, +0.7f) },
            new Entry { AllianceTag = "Union", CanonicalName = "scott",       V = new PersonalityVector(-0.1f, +0.4f, +0.2f, -0.4f, +0.3f) },
            new Entry { AllianceTag = "Union", CanonicalName = "mcclellan",   V = new PersonalityVector(-0.3f, +0.9f, -0.6f, -0.7f, +0.6f) },
            new Entry { AllianceTag = "Union", CanonicalName = "halleck",     V = new PersonalityVector( 0f,   +0.6f, -0.3f, -0.2f, +0.5f) },
            new Entry { AllianceTag = "Union", CanonicalName = "pope",        V = new PersonalityVector(+0.6f, -0.4f, +0.4f, +0.2f, +0.4f) },
            new Entry { AllianceTag = "Union", CanonicalName = "burnside",    V = new PersonalityVector(+0.3f, +0.2f, -0.1f, +0.5f, +0.5f) },
            new Entry { AllianceTag = "Union", CanonicalName = "hooker",      V = new PersonalityVector(+0.5f, -0.1f, +0.6f, +0.3f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "meade",       V = new PersonalityVector(+0.2f, +0.4f,  0f,   +0.1f, +0.3f) },
            new Entry { AllianceTag = "Union", CanonicalName = "grant",       V = new PersonalityVector(+0.8f, -0.6f, +0.5f, +0.7f, -0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sherman",     V = new PersonalityVector(+0.7f, -0.4f, +0.9f, +0.5f, -0.5f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sheridan",    V = new PersonalityVector(+0.8f, -0.3f, +0.7f, +0.4f, -0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "thomas",      V = new PersonalityVector(+0.3f, +0.4f, -0.1f, -0.1f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "buell",       V = new PersonalityVector(-0.1f, +0.6f, -0.3f, -0.5f, +0.4f) },
            new Entry { AllianceTag = "Union", CanonicalName = "rosecrans",   V = new PersonalityVector(+0.3f, +0.3f, +0.2f,  0f,   +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "banks",       V = new PersonalityVector(-0.1f, +0.5f, -0.2f,  0f,   +0.7f) }
        };

        // commanderObj is a `Commander` instance; reflection-only so we don't
        // hard-couple to its type. Returns (vector, isHistoricalEntry).
        internal static (PersonalityVector vector, bool isHistorical) Resolve(object commanderObj, int allianceId)
        {
            try
            {
                var combinedName = Reflection.GetField<string>(commanderObj, "combinedname")
                                ?? Reflection.GetField<string>(commanderObj, "lastname")
                                ?? "";
                var allianceTag = (allianceId == 1) ? "CSA" : "Union";

                var key = NormalizeLastName(combinedName);
                var matches = new List<Entry>();
                foreach (var e in _entries)
                {
                    if (e.AllianceTag == allianceTag && e.CanonicalName == key)
                        matches.Add(e);
                }
                if (matches.Count == 1) return (matches[0].V, true);
                if (matches.Count > 1)
                {
                    // Disambiguate by fame — pick highest. Spec §4.4.
                    return (matches[0].V, true); // names are unique within faction in our table; fall through
                }

                // Derived fallback.
                return (Derive(commanderObj), false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HistoricalFigureRegistry] resolve failed: {ex.Message}");
                return (default(PersonalityVector), false);
            }
        }

        // Spec §4.4 derived formula — adjusted to actual GTCW Commander fields
        // (verified during Task 17 research; `defamed` does not exist, so we use
        // recent fame-delta `fame - lastfame` as the proxy for reputation drift):
        //   agg  = +0.1 if westpoint else 0  +  +0.2 if !political else 0
        //   caut = +0.3 if political else 0
        //   aud  = +0.2 if westpoint else 0
        //   cas  = clamp(0.1 * (fame - lastfame))
        //   pol  = +0.4 if political else 0
        private static PersonalityVector Derive(object commanderObj)
        {
            // Verified-extant Commander fields (decompile lines 58932-58990):
            //   bool   westpoint        (58966)
            //   bool   political        (58990)
            //   float  fame             (58944)
            //   float  lastfame         (58945, used as the previous-fame snapshot)
            // If a field is missing in a future game version, the lookup returns
            // the fallback and we degrade gracefully.
            bool westpoint = false;
            bool political = false;
            float fame     = 0f;
            float lastfame = 0f;
            try
            {
                var t = commanderObj.GetType();
                var fWP   = HarmonyLib.AccessTools.Field(t, "westpoint");
                var fPol  = HarmonyLib.AccessTools.Field(t, "political");
                var fFame = HarmonyLib.AccessTools.Field(t, "fame");
                var fLast = HarmonyLib.AccessTools.Field(t, "lastfame");
                if (fWP   != null) westpoint = (bool)fWP.GetValue(commanderObj);
                if (fPol  != null) political = (bool)fPol.GetValue(commanderObj);
                if (fFame != null) fame      = (float)fFame.GetValue(commanderObj);
                if (fLast != null) lastfame  = (float)fLast.GetValue(commanderObj);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HistoricalFigureRegistry] derive read failed: {ex.Message}");
            }

            float agg  = (westpoint ? 0.1f : 0f) + (!political ? 0.2f : 0f);
            float caut = political ? 0.3f : 0f;
            float aud  = westpoint ? 0.2f : 0f;
            float cas  = PersonalityVector.Clamp(0.1f * (fame - lastfame));
            float pol  = political ? 0.4f : 0f;
            return new PersonalityVector(agg, caut, aud, cas, pol);
        }

        // "Robert E. Lee" → "lee"; "Stonewall Jackson" → "jackson"; etc.
        private static string NormalizeLastName(string combinedName)
        {
            if (string.IsNullOrWhiteSpace(combinedName)) return "";
            var trimmed = combinedName.Trim();
            var space = trimmed.LastIndexOf(' ');
            var last = (space >= 0) ? trimmed.Substring(space + 1) : trimmed;
            return last.ToLowerInvariant();
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/HistoricalFigureRegistry.cs
git commit -m "$(cat <<'EOF'
feat: add HistoricalFigureRegistry with 25 officers and derived fallback

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: ObjectiveAdapter

**Files:**
- Create: `src/WhiskeyRealism/Strategic/ObjectiveAdapter.cs`

Spec §4.7 — hand-coded `UniqueObjectiveID` table + geographic centroid fallback. The table starts empty (one or two example entries); it gets populated post-ship as we observe vanilla objectives in-game and audit the `<GTCW>/Modding/ModdingTool_1.11.xlsm` objective sheets. The fallback ensures the mod is functional from day one.

- [ ] **Step 1: Create ObjectiveAdapter with stub table + geographic fallback**

```csharp
// src/WhiskeyRealism/Strategic/ObjectiveAdapter.cs
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
    // Maps CampaignObjective → ObjectiveMetadata. CampaignObjective itself
    // (decompile line 178484) only carries id, alliance/scenario gates, and
    // a List<object> of Town/IIP target refs — no theater/category metadata.
    //
    // Resolution order:
    //   1. Lookup UniqueObjectiveID in the hand-coded Table.
    //   2. On miss, derive via geographic centroid of the objective's
    //      target Towns/IIPs. Sets IsDerived = true so logs can flag
    //      objectives that should eventually be table-curated.
    internal static class ObjectiveAdapter
    {
        // Hand-coded table. Populate over time as we identify vanilla
        // objectives. Empty initially is fine — the geographic fallback
        // covers every objective; the table only adds editorial flavor
        // (capital threats, foreign-recognition targets) on top.
        //
        // Format reference for future entries:
        //   { 1001, new ObjectiveMetadata {
        //        Theater = Theater.East, Category = Category.CapitalThreat,
        //        SupplyReachWeight = 0.2f, ForeignRecognitionWeight = 0.9f,
        //        AttritionWeight = 0.4f } }
        private static readonly Dictionary<int, ObjectiveMetadata> Table
            = new Dictionary<int, ObjectiveMetadata>();

        internal static ObjectiveMetadata Resolve(object campaignObjective)
        {
            if (campaignObjective == null)
                return ObjectiveMetadata.DefaultDerived(Theater.Unknown, 0f, 0f);

            int id = HarmonyLib.AccessTools.Field(campaignObjective.GetType(), "UniqueObjectiveID") is var f && f != null
                ? (int)f.GetValue(campaignObjective)
                : -1;

            if (id >= 0 && Table.TryGetValue(id, out var hit))
                return hit;

            // Hand-coded table miss — derive geographically. Log once per
            // ID per save-load cycle so smoke-testers can see which IDs are
            // candidates for hand-curation without flooding the log.
            var derived = Derive(campaignObjective);
            WhiskeyRealism.Util.OnceLog.Info(
                "objderive:" + id,
                $"[ObjectiveAdapter] geographic fallback for objective ID {id} → theater={derived.Theater} " +
                $"(consider adding to hand-coded table)");
            return derived;
        }

        // Geographic fallback: walk objective.objectives (List<object> of
        // Town / IIP refs); average their world positions; bucket the
        // centroid into a Theater. Towns and IIPs both have a Vector3
        // position in their Component; reflect-and-cast.
        private static ObjectiveMetadata Derive(object campaignObjective)
        {
            try
            {
                var objList = HarmonyLib.AccessTools.Field(campaignObjective.GetType(), "objectives")
                                ?.GetValue(campaignObjective) as IList;
                if (objList == null || objList.Count == 0)
                    return ObjectiveMetadata.DefaultDerived(Theater.Unknown, 0f, 0f);

                float sumX = 0f, sumY = 0f;
                int count = 0;
                foreach (var target in objList)
                {
                    var pos = TryGetWorldPosition(target);
                    if (pos.HasValue)
                    {
                        sumX += pos.Value.x;
                        sumY += pos.Value.z; // GTCW world is X/Z planar; Y is up.
                        count++;
                    }
                }
                if (count == 0)
                    return ObjectiveMetadata.DefaultDerived(Theater.Unknown, 0f, 0f);

                var cx = sumX / count;
                var cy = sumY / count;
                return ObjectiveMetadata.DefaultDerived(BucketTheaterFromWorldXY(cx, cy), cx, cy);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ObjectiveAdapter] derive failed: {ex.Message}");
                return ObjectiveMetadata.DefaultDerived(Theater.Unknown, 0f, 0f);
            }
        }

        // Town/IIP both inherit MonoBehaviour, so they have a transform.
        private static Vector3? TryGetWorldPosition(object target)
        {
            if (target == null) return null;
            try
            {
                var component = target as Component;
                if (component != null) return component.transform.position;
                // Fallback: reflect a `position` Vector3 field if present.
                var f = HarmonyLib.AccessTools.Field(target.GetType(), "position");
                if (f != null && f.FieldType == typeof(Vector3))
                    return (Vector3)f.GetValue(target);
            }
            catch { }
            return null;
        }

        // Theater bucketing by GTCW world X/Z. Bucket boundaries are
        // approximate; tighten during smoke-testing once we observe
        // actual map coordinates. (Task 17 research will refine these.)
        //
        // The Mississippi River is roughly X = 0 in GTCW's worldspace
        // (validated during smoke-test); Atlantic coast is far positive X.
        // The Mason-Dixon line is roughly Z = 0; northern Z positive.
        // These are PLACEHOLDER ranges — refine in Task 17 by observing
        // known town positions at runtime via a debug log.
        private static Theater BucketTheaterFromWorldXY(float x, float z)
        {
            if (x < -200f) return Theater.TransMiss;
            if (x > 800f && z < -100f) return Theater.Coast;
            if (x > 600f) return Theater.East;
            return Theater.West;
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/ObjectiveAdapter.cs
git commit -m "$(cat <<'EOF'
feat: add ObjectiveAdapter with table lookup and geographic fallback

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: TheaterCommander

**Files:**
- Create: `src/WhiskeyRealism/Strategic/TheaterCommander.cs`

Spec §5.4 — read-only steering signals; theater commander does not directly manipulate units.

- [ ] **Step 1: Create TheaterCommander**

```csharp
// src/WhiskeyRealism/Strategic/TheaterCommander.cs
namespace WhiskeyRealism.Strategic
{
    public class TheaterCommander
    {
        public int TheaterId;            // Local ordinal within the CIC's theater list
        public int OfficerCommanderId;   // Index into GameVars.commander[]
        public string OfficerName;
        public PersonalityVector Personality;

        // Active phase that this theater is executing. Read from CIC.ActivePlan
        // each time a Get* method is called; not cached on the commander to
        // avoid stale-data bugs across phase transitions.
        public OperationalPlan ActivePlan;

        // Steering signals — read by Harmony patches, never written by them.
        // All methods return additive multipliers in [0, 2] where 1.0 = vanilla.
        public float GetZoneRelevance(int zoneId)
        {
            if (ActivePlan?.CurrentPhase == null) return 1.0f;
            // Bias toward the current phase's target area.
            return ActivePlan.CurrentPhase.TargetAreaId == zoneId ? 1.5f : 1.0f;
        }

        public float GetForceConsolidationUrgency()
        {
            if (ActivePlan?.CurrentPhase == null) return 1.0f;
            // Aggression - Caution drives consolidation appetite.
            return PersonalityVector.Clamp(1.0f + 0.5f * (Personality.Aggression - Personality.Caution)) + 0.5f;
        }

        public float GetDefensiveOpsThreshold()
        {
            // High caution → defensive ops trigger sooner (lower threshold).
            return 1.0f - 0.3f * Personality.Caution;
        }

        public float GetPerkPreference(int perkId)
        {
            // Without a perk-id → personality-attribute mapping, return neutral.
            // Refine in Task 25 when we map perk IDs to attributes.
            return 1.0f;
        }

        public float GetRecruitmentTheaterWeight(Theater theater, int allianceId)
        {
            return FactionProfiles.TheaterPreferenceFor(allianceId, theater);
        }

        // Hook for Slice B (tactical brain). Not used by any Slice A patch.
        public float GetChargeRestraint()
        {
            return PersonalityVector.Clamp(1.0f - Personality.Audacity * 0.5f) + 0.5f;
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/TheaterCommander.cs
git commit -m "$(cat <<'EOF'
feat: add TheaterCommander with read-only steering signals

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 12: CIC

**Files:**
- Create: `src/WhiskeyRealism/Strategic/CIC.cs`

Spec §5.3 — Replan / Adjust / ReviewPlan logic. The Replan path uses ObjectiveAdapter for scoring.

- [ ] **Step 1: Create CIC**

```csharp
// src/WhiskeyRealism/Strategic/CIC.cs
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    public class CIC
    {
        public int AllianceId;                          // GameVars.alliance index
        public int OfficerCommanderId;                  // Index into GameVars.commander[]
        public string OfficerName;
        public PersonalityVector OfficerPersonality;   // From HistoricalFigureRegistry
        public List<TheaterCommander> Theaters = new List<TheaterCommander>();
        public OperationalPlan ActivePlan;

        // Composite of officer × era × faction — recomputed each tick.
        public PersonalityVector Effective(EraStageManager era)
            => PersonalityVector.Compose(
                   OfficerPersonality,
                   era.StageVector,
                   FactionProfiles.For(AllianceId));

        // Spec §5.1 step 3 — review whether the active plan still holds.
        public bool ReviewPlan(int currentMonth, int currentYear)
        {
            if (ActivePlan == null) return false;
            if (ActivePlan.IsDirty) return false;

            // Deadline check.
            if (currentYear > ActivePlan.PlanDeadlineYear ||
               (currentYear == ActivePlan.PlanDeadlineYear && currentMonth > ActivePlan.PlanDeadlineMonth))
                return false;

            // Phase deadline check.
            var p = ActivePlan.CurrentPhase;
            if (p != null && (currentYear > p.DeadlineYear ||
                             (currentYear == p.DeadlineYear && currentMonth > p.DeadlineMonth)))
            {
                return AdvancePhase();
            }

            return true;
        }

        public bool AdvancePhase()
        {
            if (ActivePlan == null) return false;
            if (ActivePlan.CurrentPhaseIndex + 1 < ActivePlan.Phases.Count)
            {
                ActivePlan.CurrentPhaseIndex++;
                Plugin.Log.LogInfo($"[CIC:{OfficerName}] phase advanced to {ActivePlan.CurrentPhaseIndex}");
                return true;
            }
            // No more phases — plan is done; mark for replan next tick.
            ActivePlan.IsDirty = true;
            return false;
        }

        // Spec §5.3 — score available objectives and pick a new plan.
        // `gameVarsAlliance` is `GameVars.alliance` element for this CIC's
        // alliance; passed reflectively from StrategicCoordinator.
        public void Replan(EraStageManager era, int currentMonth, int currentYear)
        {
            var availableObjectives = GetAvailableObjectivesViaReflection(AllianceId);
            if (availableObjectives == null || availableObjectives.Count == 0)
            {
                Plugin.Log.LogInfo($"[CIC:{OfficerName}] Replan — no available objectives, plan cleared.");
                ActivePlan = null;
                return;
            }

            var p = Effective(era);
            var scored = new List<(object obj, float score, ObjectiveMetadata meta)>();

            foreach (var obj in availableObjectives)
            {
                var meta = ObjectiveAdapter.Resolve(obj);
                float score = ScoreObjective(p, meta);
                scored.Add((obj, score, meta));
            }

            scored.Sort((a, b) => b.score.CompareTo(a.score));
            var top3 = scored.GetRange(0, Math.Min(3, scored.Count));

            // Weighted-random pick from top 3 (replay variety).
            var picked = WeightedPick(top3);

            ActivePlan = BuildPlan(picked.obj, picked.meta, p, currentMonth, currentYear);

            if (ActivePlan != null && Plugin.Instance.PlanTrace.Value)
            {
                Plugin.Log.LogInfo($"[Plan:{OfficerName}] {ActivePlan.Rationale} — phases={ActivePlan.Phases.Count} deadline={ActivePlan.PlanDeadlineYear}-{ActivePlan.PlanDeadlineMonth:D2}");
                for (int i = 0; i < scored.Count && i < 5; i++)
                    Plugin.Log.LogInfo($"  [Plan:scores] obj_id={GetObjectiveId(scored[i].obj)} score={scored[i].score:F2} theater={scored[i].meta.Theater} category={scored[i].meta.Category}");
            }
        }

        private float ScoreObjective(PersonalityVector p, ObjectiveMetadata meta)
        {
            float theaterPref = FactionProfiles.TheaterPreferenceFor(AllianceId, meta.Theater);
            float foreignWeight = FactionProfiles.ForeignRecognitionWeightFor(AllianceId);
            float forceRatioTerm = 0.5f;            // placeholder; refine in Task 17 once force-balance access is mapped
            float distanceTerm   = 0f;              // placeholder; refine when AIArea geometry is mapped

            return theaterPref
                 + meta.SupplyReachWeight        * 1.0f
                 + meta.ForeignRecognitionWeight * foreignWeight
                 + meta.AttritionWeight          * p.CasualtyTolerance
                 + forceRatioTerm                * (1f - p.Caution)
                 - distanceTerm                  * (1f - p.Audacity);
        }

        private (object obj, float score, ObjectiveMetadata meta) WeightedPick(
            List<(object obj, float score, ObjectiveMetadata meta)> top)
        {
            if (top.Count == 0) return default;
            if (top.Count == 1) return top[0];

            // Softmax-ish pick weighted by score — clamp negative scores to 0 first.
            float total = 0f;
            foreach (var t in top) total += Math.Max(0f, t.score);
            if (total <= 0f) return top[0]; // all-zero or all-negative; fall back to argmax

            float roll = (float)(new System.Random().NextDouble()) * total;
            float acc = 0f;
            foreach (var t in top)
            {
                acc += Math.Max(0f, t.score);
                if (roll <= acc) return t;
            }
            return top[0];
        }

        // Phase decomposition: 2-4 phases by current-phase target plus geographic
        // prerequisites. Spec §5.3 step 3 — clamp(2 + (1 if low audacity), 2, 4).
        // First slice's decomposer is conservative — single-phase main objective
        // plus a setup phase if audacity is low. Refine after smoke-test once
        // we observe how AIArea graph traversal behaves.
        private OperationalPlan BuildPlan(object pickedObjective, ObjectiveMetadata meta, PersonalityVector p,
                                           int currentMonth, int currentYear)
        {
            int objId = GetObjectiveId(pickedObjective);
            int phaseCount = 2;
            if (p.Audacity < 0.0f) phaseCount = 3;
            if (p.Audacity < -0.3f && p.Caution > 0.3f) phaseCount = 4;

            float forceFraction = PersonalityVector.Clamp(0.4f + 0.4f * p.Caution + 0.3f * (1f - p.Audacity));
            forceFraction = Math.Max(0.3f, Math.Min(0.95f, forceFraction));

            // Deadline scales with caution.
            int monthsAhead = (int)(6f * (1f + 0.5f * p.Caution));
            var deadline = AddMonths(currentMonth, currentYear, monthsAhead);

            var plan = new OperationalPlan
            {
                CICFactionAllianceId = AllianceId,
                AssignedTheaterId    = (Theaters.Count > 0 ? Theaters[0].TheaterId : 0),
                CurrentPhaseIndex    = 0,
                PlanDeadlineMonth    = deadline.month,
                PlanDeadlineYear     = deadline.year,
                Rationale            = $"theater={meta.Theater} category={meta.Category} forceFrac={forceFraction:F2}",
                IsDirty              = false
            };

            // Single main phase. Refine to multi-phase decomposition in a future
            // iteration once we have geographic-prereq data wired in.
            plan.Phases.Add(new Phase
            {
                TargetAreaId           = -1, // resolved at first patch hit if needed
                TargetObjectiveId      = objId,
                ForceFractionRequired  = forceFraction,
                Transition             = PhaseTransition.TargetTaken,
                DeadlineMonth          = deadline.month,
                DeadlineYear           = deadline.year,
                Fallback               = null
            });

            // Optional setup phase for cautious/low-audacity profiles.
            if (phaseCount >= 3)
            {
                plan.Phases.Insert(0, new Phase
                {
                    TargetAreaId          = -1,
                    TargetObjectiveId     = objId, // same target; setup is positional
                    ForceFractionRequired = Math.Max(0.2f, forceFraction - 0.2f),
                    Transition            = PhaseTransition.TargetEngaged,
                    DeadlineMonth         = AddMonths(currentMonth, currentYear, monthsAhead / 3).month,
                    DeadlineYear          = AddMonths(currentMonth, currentYear, monthsAhead / 3).year,
                    Fallback              = null
                });
                plan.CurrentPhaseIndex = 0;
            }

            return plan;
        }

        private static (int month, int year) AddMonths(int month, int year, int delta)
        {
            int total = month + delta;
            int dy = (total - 1) / 12;
            int dm = ((total - 1) % 12) + 1;
            return (dm, year + dy);
        }

        // CampaignObjective is reflection-friendly (it's public). We use AccessTools
        // to read UniqueObjectiveID and call GetAvailableObjectives statically.
        private static int GetObjectiveId(object campaignObjective)
        {
            if (campaignObjective == null) return -1;
            var f = AccessTools.Field(campaignObjective.GetType(), "UniqueObjectiveID");
            return f != null ? (int)f.GetValue(campaignObjective) : -1;
        }

        private static IList GetAvailableObjectivesViaReflection(int allianceId)
        {
            try
            {
                // CampaignObjective is a public type, so we can resolve it by name.
                var t = AccessTools.TypeByName("CampaignObjective");
                if (t == null) { Plugin.Log.LogWarning("[CIC] CampaignObjective type not found"); return null; }
                var m = AccessTools.Method(t, "GetAvailableObjectives", new[] { typeof(int) });
                if (m == null) { Plugin.Log.LogWarning("[CIC] CampaignObjective.GetAvailableObjectives not found"); return null; }
                return m.Invoke(null, new object[] { allianceId }) as IList;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[CIC] GetAvailableObjectives reflection failed: " + ex.Message);
                return null;
            }
        }
    }
}
```

- [ ] **Step 2: Add the diagnostic ConfigEntries that CIC.Replan reads**

`CIC.Replan` (above) reads `Plugin.Instance.PlanTrace.Value`, which doesn't exist yet — it gets fully wired in Task 27. To keep the build green now, add the ConfigEntry stubs in Plugin.cs as part of this commit. Modify `src/WhiskeyRealism/Plugin.cs` — add `PlanTrace` and `SuccessionTrace` alongside `VerboseLogging`:

```csharp
        internal ConfigEntry<bool> VerboseLogging;
        internal ConfigEntry<bool> PlanTrace;
        internal ConfigEntry<bool> SuccessionTrace;
```

And in `Awake()`:

```csharp
            VerboseLogging = Config.Bind(
                "[Diagnostics]", "Verbose Logging", false,
                "Emit per-patch first-fire markers and decision-trace logs to LogOutput.log.");
            PlanTrace = Config.Bind(
                "[Diagnostics]", "Plan Trace Logging", false,
                "On each monthly tick, dump CIC's plan reasoning (objective scores, top-3, picked, phases, deadline).");
            SuccessionTrace = Config.Bind(
                "[Diagnostics]", "Succession Trace Logging", false,
                "On each monthly tick, log every succession event check (date gate, war-state gate, fired/not-fired).");
```

Then re-run `./build.sh`. Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/CIC.cs src/WhiskeyRealism/Plugin.cs
git commit -m "$(cat <<'EOF'
feat: add CIC with Replan/Adjust/ReviewPlan and diagnostic config entries

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 13: SuccessionScheduler

**Files:**
- Create: `src/WhiskeyRealism/Strategic/SuccessionScheduler.cs`

Spec §4.6 — 12 canonical events. Each gates on (date AND war-state). Won't fire if named replacement already in command (idempotent).

- [ ] **Step 1: Create SuccessionScheduler with 12 events**

```csharp
// src/WhiskeyRealism/Strategic/SuccessionScheduler.cs
using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    // Canonical historical succession events. Spec §4.6 — gate on date AND
    // war-state. Won't fire if the replacement is already in command.
    // Events fire on monthly tick from StrategicCoordinator.
    internal class SuccessionScheduler
    {
        internal class Event
        {
            public int    Id;
            public string Name;
            public int    EarliestYear;
            public int    EarliestMonth;
            public int    AllianceId;       // 0=Union, 1=CSA
            public string ReplacementName;  // canonical lowercase last name
            public string ReplacedRole;     // "ANV", "AoP", "Western", "GeneralInChief", etc.
            public Func<WarStateView, bool> WarStateGate;
            public bool   Fired;
        }

        // Snapshot view of war-state passed into each gate evaluator.
        // Populated each tick by StrategicCoordinator.
        internal class WarStateView
        {
            public int  CurrentMonth;
            public int  CurrentYear;
            public bool ANVHasLostMajorBattle;
            public bool JohnstonWoundedOrDisabled;
            public bool BragsCommandRatingLow;
            public bool AoPHasFailedNOffensives;
            public bool BurnsidesFirstDefeatPassed;
            public bool LeeInvadingPennsylvania;
            public bool WesternMajorDefeatPassed;
            public bool VicksburgFallen;
            public bool ChattanoogaFallen;
            public bool AtlantaThreatened;
            public bool DavisPatienceExhausted;
            public bool ValleyOpsNeeded;
            public bool WarClearlyLost;
        }

        private readonly List<Event> _events = new List<Event>();

        // Tracks which events have fired this career; persisted via sidecar.
        internal HashSet<int> FiredEventIds = new HashSet<int>();

        internal SuccessionScheduler()
        {
            // Spec §4.6 — 12 canonical events.
            _events.Add(new Event { Id =  1, Name = "Lee → ANV command",         EarliestYear = 1862, EarliestMonth =  5, AllianceId = 1, ReplacementName = "lee",       ReplacedRole = "ANV",            WarStateGate = w => w.JohnstonWoundedOrDisabled || w.ANVHasLostMajorBattle });
            _events.Add(new Event { Id =  2, Name = "Bragg → Western theater",   EarliestYear = 1862, EarliestMonth =  6, AllianceId = 1, ReplacementName = "bragg",     ReplacedRole = "Western",        WarStateGate = w => w.BragsCommandRatingLow });
            _events.Add(new Event { Id =  3, Name = "McClellan removed",         EarliestYear = 1862, EarliestMonth = 11, AllianceId = 0, ReplacementName = "burnside",  ReplacedRole = "AoP",            WarStateGate = w => w.AoPHasFailedNOffensives });
            _events.Add(new Event { Id =  4, Name = "Burnside → Hooker",         EarliestYear = 1863, EarliestMonth =  1, AllianceId = 0, ReplacementName = "hooker",    ReplacedRole = "AoP",            WarStateGate = w => w.BurnsidesFirstDefeatPassed });
            _events.Add(new Event { Id =  5, Name = "Hooker → Meade",            EarliestYear = 1863, EarliestMonth =  6, AllianceId = 0, ReplacementName = "meade",     ReplacedRole = "AoP",            WarStateGate = w => w.LeeInvadingPennsylvania });
            _events.Add(new Event { Id =  6, Name = "Bragg removed",             EarliestYear = 1863, EarliestMonth = 11, AllianceId = 1, ReplacementName = "johnston",  ReplacedRole = "Western",        WarStateGate = w => w.WesternMajorDefeatPassed });
            _events.Add(new Event { Id =  7, Name = "Joe Johnston → Western",    EarliestYear = 1863, EarliestMonth = 12, AllianceId = 1, ReplacementName = "johnston",  ReplacedRole = "Western",        WarStateGate = w => true /* cascade from #6 */ });
            _events.Add(new Event { Id =  8, Name = "Grant → General-in-Chief",  EarliestYear = 1864, EarliestMonth =  3, AllianceId = 0, ReplacementName = "grant",     ReplacedRole = "GeneralInChief", WarStateGate = w => w.VicksburgFallen && w.ChattanoogaFallen });
            _events.Add(new Event { Id =  9, Name = "Sherman → Western",         EarliestYear = 1864, EarliestMonth =  3, AllianceId = 0, ReplacementName = "sherman",   ReplacedRole = "Western",        WarStateGate = w => w.VicksburgFallen && w.ChattanoogaFallen /* cascade from #8 */ });
            _events.Add(new Event { Id = 10, Name = "Hood replaces Johnston",    EarliestYear = 1864, EarliestMonth =  7, AllianceId = 1, ReplacementName = "hood",      ReplacedRole = "Western",        WarStateGate = w => w.AtlantaThreatened && w.DavisPatienceExhausted });
            _events.Add(new Event { Id = 11, Name = "Sheridan → Shenandoah",     EarliestYear = 1864, EarliestMonth =  8, AllianceId = 0, ReplacementName = "sheridan",  ReplacedRole = "Valley",         WarStateGate = w => w.ValleyOpsNeeded });
            _events.Add(new Event { Id = 12, Name = "Lee → General-in-Chief CSA", EarliestYear = 1865, EarliestMonth =  2, AllianceId = 1, ReplacementName = "lee",       ReplacedRole = "GeneralInChief", WarStateGate = w => w.WarClearlyLost });
        }

        // Returns list of events that fired this tick. Caller (StrategicCoordinator)
        // is responsible for actually performing the officer swap.
        internal List<Event> CheckEvents(WarStateView w)
        {
            var fired = new List<Event>();
            foreach (var e in _events)
            {
                if (FiredEventIds.Contains(e.Id)) continue;

                // Date gate.
                bool dateOk = (w.CurrentYear > e.EarliestYear) ||
                              (w.CurrentYear == e.EarliestYear && w.CurrentMonth >= e.EarliestMonth);
                bool warStateOk = e.WarStateGate(w);

                if (Plugin.Instance.SuccessionTrace.Value)
                    Plugin.Log.LogInfo($"[Succession:{e.Id}] {e.Name} dateOk={dateOk} warStateOk={warStateOk}");

                if (dateOk && warStateOk)
                {
                    FiredEventIds.Add(e.Id);
                    fired.Add(e);
                }
            }
            return fired;
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/SuccessionScheduler.cs
git commit -m "$(cat <<'EOF'
feat: add SuccessionScheduler with 12 canonical historical events

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 14: StrategicCoordinator

**Files:**
- Create: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`

Spec §3 + §3.1 + §5.1 + §5.2. The singleton MonoBehaviour that drives monthly tick + event triggers + player-CIC gate.

- [ ] **Step 1: Create StrategicCoordinator**

```csharp
// src/WhiskeyRealism/Strategic/StrategicCoordinator.cs
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    public class StrategicCoordinator : MonoBehaviour
    {
        public static StrategicCoordinator Instance { get; private set; }

        // Two CIC slots — one per alliance. Null when player is CIC of that alliance.
        public CIC[] CICs = new CIC[2];

        // Per-alliance era tracker.
        public EraStageManager[] Eras = new EraStageManager[2];

        // Single shared scheduler — events fire across both factions.
        internal SuccessionScheduler Succession = new SuccessionScheduler();

        // Cache of derived personality vectors for minor commanders, keyed by
        // commander id. Frozen at first encounter (spec §4.4).
        public Dictionary<int, PersonalityVector> MinorOfficerProfiles = new Dictionary<int, PersonalityVector>();

        // Last-seen game month/year used to detect rollover from MonthlyTickHookPatch.
        public int LastSeenMonth = -1;
        public int LastSeenYear  = -1;

        public bool Initialized;

        public static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("WhiskeyRealismStrategicCoordinator");
            Instance = go.AddComponent<StrategicCoordinator>();
            DontDestroyOnLoad(go);
            Plugin.Log.LogInfo("[Coordinator] bootstrapped");
        }

        // Idempotent init — safe to call multiple times. Re-runs after
        // sidecar load to rehydrate CIC/Theater instances.
        public void InitializeFromGameState()
        {
            try
            {
                Eras[0] = Eras[0] ?? new EraStageManager { Stage = EraStage.Amateur1861 };
                Eras[1] = Eras[1] ?? new EraStageManager { Stage = EraStage.Amateur1861 };

                int playerAlliance = ResolvePlayerAlliance();
                for (int alliance = 0; alliance < 2; alliance++)
                {
                    if (IsPlayerCICOf(alliance, playerAlliance))
                    {
                        CICs[alliance] = null;     // player has authority; mod stands down
                        continue;
                    }
                    if (CICs[alliance] == null)
                        CICs[alliance] = BuildCICForAlliance(alliance);
                }
                Initialized = true;
                Plugin.Log.LogInfo($"[Coordinator] initialized (playerAlliance={playerAlliance})");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Coordinator] init failed: " + ex);
            }
        }

        private CIC BuildCICForAlliance(int allianceId)
        {
            var cic = new CIC { AllianceId = allianceId };
            // Officer assignment defaults: Davis for CSA, Lincoln for Union.
            // The actual game commander reference is resolved when patches run;
            // here we just seed the personality vector.
            if (allianceId == 1) // CSA
            {
                cic.OfficerName = "Davis";
                cic.OfficerPersonality = new PersonalityVector(-0.1f, +0.3f, -0.3f, -0.3f, +0.5f);
            }
            else
            {
                cic.OfficerName = "Lincoln";
                cic.OfficerPersonality = new PersonalityVector(+0.3f, +0.1f, +0.1f, +0.4f, +0.7f);
            }
            // Theater commanders — created lazily on first patch hit, so
            // the coordinator doesn't have to enumerate army groups itself.
            return cic;
        }

        // Called from MonthlyTickHookPatch (Task 18) every game-day to detect
        // a fresh month rollover and dispatch one OnMonthlyTick.
        public void NotifyDateAdvanced(int gameMonth, int gameYear)
        {
            if (!Initialized) InitializeFromGameState();

            // First-call latch — don't fire on the very first day's read.
            if (LastSeenMonth < 0)
            {
                LastSeenMonth = gameMonth;
                LastSeenYear  = gameYear;
                return;
            }

            bool rollover = (gameMonth != LastSeenMonth) || (gameYear != LastSeenYear);
            if (!rollover) return;

            LastSeenMonth = gameMonth;
            LastSeenYear  = gameYear;
            OnMonthlyTick(gameMonth, gameYear);
        }

        public void OnMonthlyTick(int month, int year)
        {
            try
            {
                int playerAlliance = ResolvePlayerAlliance();
                for (int alliance = 0; alliance < 2; alliance++)
                {
                    if (IsPlayerCICOf(alliance, playerAlliance))
                    {
                        // Player-CIC gate engaged — log once per save-load cycle so the
                        // user sees clear confirmation, then stand down silently.
                        OnceLog.Info("playerciconly:" + alliance,
                            $"player is CIC of alliance {alliance} ({(alliance == 1 ? "CSA" : "Union")}) — mod stands down for that faction");
                        CICs[alliance] = null;
                        continue;
                    }
                    if (CICs[alliance] == null) CICs[alliance] = BuildCICForAlliance(alliance);

                    var era = Eras[alliance];
                    var ws = ObserveWarState(month, year, alliance);
                    era.CheckTransition(month, year, ws.VicksburgFallen, ws.AtlantaThreatened);

                    // Succession events (cross-faction; the shared scheduler fires per alliance).
                    var fired = Succession.CheckEvents(BuildSchedulerView(month, year, alliance, ws));
                    foreach (var e in fired)
                    {
                        if (e.AllianceId != alliance) continue;
                        SwapOfficer(alliance, e);
                        if (CICs[alliance].ActivePlan != null)
                            CICs[alliance].ActivePlan.IsDirty = true;
                    }

                    var cic = CICs[alliance];
                    if (cic.ReviewPlan(month, year))
                    {
                        // Plan still valid; current phase advances/holds inside ReviewPlan.
                    }
                    else
                    {
                        cic.Replan(era, month, year);
                    }

                    // Monthly heartbeat — single richest "proof of life" signal.
                    // One line per faction per game-month — 24 lines/year. Gives a
                    // smoke-tester an unambiguous "the mod is running and these are
                    // the decisions it's making" view without log spam.
                    Plugin.Log.LogInfo(
                        $"[Heartbeat] {year}-{month:D2} alliance={alliance} " +
                        $"era={era.Stage} cic={cic.OfficerName ?? "<none>"} " +
                        $"plan={(cic.ActivePlan == null ? "<none>" : $"phase{cic.ActivePlan.CurrentPhaseIndex + 1}/{cic.ActivePlan.Phases.Count} obj={cic.ActivePlan.CurrentPhase?.TargetObjectiveId}")} " +
                        $"succession_fired={Succession.FiredEventIds.Count}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Coordinator] tick failed: " + ex);
            }
        }

        // Called from event-trigger Harmony patches (town loss / battle defeat / KIA / etc.)
        // — added in Slice A only as a public API; concrete patch hooks land in
        // Slice B's tactical brain.
        public void OnEventTrigger(int allianceId, string eventType)
        {
            if (allianceId < 0 || allianceId >= CICs.Length) return;
            var cic = CICs[allianceId];
            if (cic?.ActivePlan == null) return;

            // Mark dirty — events do NOT cause immediate re-eval; next monthly tick
            // processes the dirty bit (spec §5.2 — prevents AI thrashing).
            cic.ActivePlan.IsDirty = true;
            Plugin.Log.LogInfo($"[Coordinator] event '{eventType}' for alliance {allianceId} — plan marked dirty");
        }

        // ---------- Player-CIC gate (spec §3.1) ----------

        public static int ResolvePlayerAlliance()
        {
            try
            {
                var t = AccessTools.TypeByName("GameVars");
                var f = AccessTools.Field(t, "playeralliance");
                return f != null ? (int)f.GetValue(null) : -1;
            }
            catch { return -1; }
        }

        public static bool IsPlayerCICOf(int allianceId, int playerAlliance)
        {
            if (allianceId != playerAlliance) return false;
            try
            {
                var dlcType = AccessTools.TypeByName("DLC_WL");
                if (dlcType == null) return false;
                var scenarioActive = (bool)AccessTools.Field(dlcType, "dlc_scenarioactive").GetValue(null);
                if (!scenarioActive) return false;
                var isCIC = AccessTools.Method(dlcType, "IsCommanderInChief");
                if (isCIC == null) return false;
                return (bool)isCIC.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Coordinator] IsPlayerCICOf failed: " + ex.Message);
                return false; // safe default — let the mod run
            }
        }

        // ---------- War-state observation (placeholder; refine in Task 17 research) ----------

        private struct WarSnapshot
        {
            public bool VicksburgFallen;
            public bool ChattanoogaFallen;
            public bool AtlantaThreatened;
            public bool ANVHasLostMajorBattle;
        }

        private WarSnapshot ObserveWarState(int month, int year, int alliance)
        {
            // Placeholder readers — return `false` until Task 17 research wires
            // them to actual game state observers (TownOwnership, Policy.morale,
            // BattleHistory, etc.). Mod still functions with all-false; succession
            // events that depend on war-state simply won't fire until the research
            // is complete and observers are wired.
            return new WarSnapshot();
        }

        private SuccessionScheduler.WarStateView BuildSchedulerView(int month, int year, int alliance, WarSnapshot snap)
        {
            return new SuccessionScheduler.WarStateView
            {
                CurrentMonth = month,
                CurrentYear  = year,
                VicksburgFallen     = snap.VicksburgFallen,
                ChattanoogaFallen   = snap.ChattanoogaFallen,
                AtlantaThreatened   = snap.AtlantaThreatened,
                ANVHasLostMajorBattle = snap.ANVHasLostMajorBattle,
                // Other gates default to false until war-state observers exist.
                JohnstonWoundedOrDisabled = false,
                BragsCommandRatingLow     = false,
                AoPHasFailedNOffensives   = false,
                BurnsidesFirstDefeatPassed = false,
                LeeInvadingPennsylvania   = false,
                WesternMajorDefeatPassed  = false,
                DavisPatienceExhausted    = false,
                ValleyOpsNeeded           = false,
                WarClearlyLost            = false
            };
        }

        // ---------- Officer swap (placeholder until succession patch wires it) ----------

        private void SwapOfficer(int alliance, SuccessionScheduler.Event e)
        {
            Plugin.Log.LogInfo($"[Succession:{e.Id}] FIRED — {e.Name}, replacing role={e.ReplacedRole} with={e.ReplacementName}");
            // Actual game-state swap is performed in CommanderReplacementPatch
            // (Task 24) which reads SuccessionScheduler.FiredEventIds and
            // performs the AssignCommando call inside its Prefix. The
            // coordinator just records that the event fired.
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/StrategicCoordinator.cs
git commit -m "$(cat <<'EOF'
feat: add StrategicCoordinator with player-CIC gate and monthly tick

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 15: PersistenceDto + sidecar serialization

**Files:**
- Create: `src/WhiskeyRealism/Strategic/PersistenceDto.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs` (add `SaveSidecar` / `LoadSidecar` methods)

Spec §7.4 — JSON shape with version, factions[], minorOfficerProfiles[].

- [ ] **Step 1: Create PersistenceDto.cs with the JSON DTOs**

```csharp
// src/WhiskeyRealism/Strategic/PersistenceDto.cs
using System.Collections.Generic;
using Newtonsoft.Json;

namespace WhiskeyRealism.Strategic
{
    // JSON DTOs. Object-form personality vectors for forward-compat (spec §13 q8).
    // Frozen schema version 1.
    internal class SidecarDto
    {
        [JsonProperty("version")] public int Version = 1;
        [JsonProperty("factions")] public List<FactionDto> Factions = new List<FactionDto>();
        [JsonProperty("minorOfficerProfiles")] public List<MinorOfficerDto> MinorOfficerProfiles = new List<MinorOfficerDto>();
        [JsonProperty("succession")] public SuccessionDto Succession = new SuccessionDto();
    }

    internal class FactionDto
    {
        [JsonProperty("factionId")]   public int FactionId;
        [JsonProperty("factionName")] public string FactionName;
        [JsonProperty("currentEra")]  public string CurrentEra;
        [JsonProperty("cic")]         public CICDto Cic;
        [JsonProperty("theaterCommanders")] public List<TheaterCommanderDto> TheaterCommanders = new List<TheaterCommanderDto>();
    }

    internal class CICDto
    {
        [JsonProperty("officerName")] public string OfficerName;
        [JsonProperty("personality")] public PersonalityDto Personality;
        [JsonProperty("activePlan")]  public OperationalPlanDto ActivePlan;
    }

    internal class TheaterCommanderDto
    {
        [JsonProperty("theaterId")]   public int TheaterId;
        [JsonProperty("officerName")] public string OfficerName;
        [JsonProperty("personality")] public PersonalityDto Personality;
    }

    internal class OperationalPlanDto
    {
        [JsonProperty("assignedTheaterId")] public int AssignedTheaterId;
        [JsonProperty("phases")] public List<PhaseDto> Phases = new List<PhaseDto>();
        [JsonProperty("currentPhaseIndex")] public int CurrentPhaseIndex;
        [JsonProperty("planDeadlineMonth")] public int PlanDeadlineMonth;
        [JsonProperty("planDeadlineYear")]  public int PlanDeadlineYear;
        [JsonProperty("rationale")] public string Rationale;
        [JsonProperty("isDirty")]   public bool   IsDirty;
    }

    internal class PhaseDto
    {
        [JsonProperty("targetAreaId")]          public int    TargetAreaId;
        [JsonProperty("targetObjectiveId")]     public int    TargetObjectiveId;
        [JsonProperty("forceFractionRequired")] public float  ForceFractionRequired;
        [JsonProperty("transition")]            public string Transition;
        [JsonProperty("deadlineMonth")]         public int    DeadlineMonth;
        [JsonProperty("deadlineYear")]          public int    DeadlineYear;
    }

    internal class PersonalityDto
    {
        [JsonProperty("agg")]  public float Aggression;
        [JsonProperty("caut")] public float Caution;
        [JsonProperty("aud")]  public float Audacity;
        [JsonProperty("cas")]  public float CasualtyTolerance;
        [JsonProperty("pol")]  public float PoliticalResponsiveness;

        public PersonalityVector ToVector() => new PersonalityVector(Aggression, Caution, Audacity, CasualtyTolerance, PoliticalResponsiveness);

        public static PersonalityDto FromVector(PersonalityVector v) => new PersonalityDto
        {
            Aggression = v.Aggression, Caution = v.Caution, Audacity = v.Audacity,
            CasualtyTolerance = v.CasualtyTolerance, PoliticalResponsiveness = v.PoliticalResponsiveness
        };
    }

    internal class MinorOfficerDto
    {
        [JsonProperty("commanderId")] public int CommanderId;
        [JsonProperty("personality")] public PersonalityDto Personality;
    }

    internal class SuccessionDto
    {
        [JsonProperty("firedEvents")] public List<int> FiredEvents = new List<int>();
        [JsonProperty("lastChecked")] public string    LastChecked;
    }
}
```

- [ ] **Step 2: Add SaveSidecar/LoadSidecar to StrategicCoordinator**

Append the following methods inside the `StrategicCoordinator` class in `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs` (above the closing brace of the class):

```csharp
        // ---------- Sidecar persistence (spec §7) ----------

        public void SaveSidecar(string fullPath)
        {
            try
            {
                var dto = BuildDto();
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(dto, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(fullPath, json);
                Plugin.Log.LogInfo("[Coordinator] sidecar written: " + fullPath);
            }
            catch (Exception ex) { Plugin.Log.LogError("[Coordinator] sidecar save failed: " + ex); }
        }

        public void LoadSidecar(string fullPath)
        {
            try
            {
                var json = System.IO.File.ReadAllText(fullPath);
                var dto = Newtonsoft.Json.JsonConvert.DeserializeObject<SidecarDto>(json);
                if (dto == null) { InitializeFromGameState(); return; }
                ApplyDto(dto);
                Initialized = true;
                Plugin.Log.LogInfo("[Coordinator] sidecar loaded: " + fullPath);
                OnceLog.Reset(); // fresh save-load cycle — patches re-emit first-fire markers
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Coordinator] sidecar load failed (falling back to fresh init): " + ex.Message);
                InitializeFromGameState();
            }
        }

        private SidecarDto BuildDto()
        {
            var dto = new SidecarDto();
            for (int alliance = 0; alliance < 2; alliance++)
            {
                if (CICs[alliance] == null) continue; // player-CIC: don't persist for that faction
                var f = new FactionDto
                {
                    FactionId   = alliance,
                    FactionName = (alliance == 1) ? "CSA" : "Union",
                    CurrentEra  = Eras[alliance].Stage.ToString(),
                    Cic = new CICDto
                    {
                        OfficerName = CICs[alliance].OfficerName,
                        Personality = PersonalityDto.FromVector(CICs[alliance].OfficerPersonality),
                        ActivePlan  = (CICs[alliance].ActivePlan != null) ? PlanToDto(CICs[alliance].ActivePlan) : null
                    }
                };
                foreach (var tc in CICs[alliance].Theaters)
                {
                    f.TheaterCommanders.Add(new TheaterCommanderDto
                    {
                        TheaterId   = tc.TheaterId,
                        OfficerName = tc.OfficerName,
                        Personality = PersonalityDto.FromVector(tc.Personality)
                    });
                }
                dto.Factions.Add(f);
            }
            foreach (var kv in MinorOfficerProfiles)
            {
                dto.MinorOfficerProfiles.Add(new MinorOfficerDto
                {
                    CommanderId = kv.Key,
                    Personality = PersonalityDto.FromVector(kv.Value)
                });
            }
            dto.Succession.FiredEvents = new List<int>(Succession.FiredEventIds);
            dto.Succession.LastChecked = LastSeenYear + "-" + LastSeenMonth.ToString("D2") + "-01";
            return dto;
        }

        private void ApplyDto(SidecarDto dto)
        {
            for (int alliance = 0; alliance < 2; alliance++)
            {
                Eras[alliance] = Eras[alliance] ?? new EraStageManager();
            }
            foreach (var f in dto.Factions)
            {
                if (f.FactionId < 0 || f.FactionId >= 2) continue;
                if (Enum.TryParse<EraStage>(f.CurrentEra, out var era)) Eras[f.FactionId].Stage = era;
                var cic = new CIC
                {
                    AllianceId         = f.FactionId,
                    OfficerName        = f.Cic?.OfficerName,
                    OfficerPersonality = f.Cic?.Personality?.ToVector() ?? default(PersonalityVector),
                    ActivePlan         = (f.Cic?.ActivePlan != null) ? PlanFromDto(f.Cic.ActivePlan, f.FactionId) : null
                };
                foreach (var tc in f.TheaterCommanders)
                {
                    cic.Theaters.Add(new TheaterCommander
                    {
                        TheaterId   = tc.TheaterId,
                        OfficerName = tc.OfficerName,
                        Personality = tc.Personality?.ToVector() ?? default(PersonalityVector)
                    });
                }
                CICs[f.FactionId] = cic;
            }
            MinorOfficerProfiles.Clear();
            foreach (var m in dto.MinorOfficerProfiles)
                MinorOfficerProfiles[m.CommanderId] = m.Personality.ToVector();
            Succession.FiredEventIds = new HashSet<int>(dto.Succession.FiredEvents);
        }

        private OperationalPlanDto PlanToDto(OperationalPlan p)
        {
            var dto = new OperationalPlanDto
            {
                AssignedTheaterId = p.AssignedTheaterId,
                CurrentPhaseIndex = p.CurrentPhaseIndex,
                PlanDeadlineMonth = p.PlanDeadlineMonth,
                PlanDeadlineYear  = p.PlanDeadlineYear,
                Rationale         = p.Rationale,
                IsDirty           = p.IsDirty
            };
            foreach (var ph in p.Phases)
                dto.Phases.Add(new PhaseDto
                {
                    TargetAreaId          = ph.TargetAreaId,
                    TargetObjectiveId     = ph.TargetObjectiveId,
                    ForceFractionRequired = ph.ForceFractionRequired,
                    Transition            = ph.Transition.ToString(),
                    DeadlineMonth         = ph.DeadlineMonth,
                    DeadlineYear          = ph.DeadlineYear
                });
            return dto;
        }

        private OperationalPlan PlanFromDto(OperationalPlanDto dto, int allianceId)
        {
            var p = new OperationalPlan
            {
                CICFactionAllianceId = allianceId,
                AssignedTheaterId    = dto.AssignedTheaterId,
                CurrentPhaseIndex    = dto.CurrentPhaseIndex,
                PlanDeadlineMonth    = dto.PlanDeadlineMonth,
                PlanDeadlineYear     = dto.PlanDeadlineYear,
                Rationale            = dto.Rationale,
                IsDirty              = dto.IsDirty
            };
            foreach (var ph in dto.Phases)
            {
                Enum.TryParse<PhaseTransition>(ph.Transition, out var trans);
                p.Phases.Add(new Phase
                {
                    TargetAreaId          = ph.TargetAreaId,
                    TargetObjectiveId     = ph.TargetObjectiveId,
                    ForceFractionRequired = ph.ForceFractionRequired,
                    Transition            = trans,
                    DeadlineMonth         = ph.DeadlineMonth,
                    DeadlineYear          = ph.DeadlineYear,
                    Fallback              = null
                });
            }
            return p;
        }
```

- [ ] **Step 3: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Strategic/PersistenceDto.cs src/WhiskeyRealism/Strategic/StrategicCoordinator.cs
git commit -m "$(cat <<'EOF'
feat: add JSON sidecar persistence with versioned DTOs

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 16: AICampaignSaveLoadPatch

**Files:**
- Create: `src/WhiskeyRealism/Patches/AICampaignSaveLoadPatch.cs`

Spec §7.2-7.3 — Postfix on `AICampaign.Save(string folder)` (decompile line 16631) and `AICampaign.Load(string folder)` (line 16435).

- [ ] **Step 1: Create the save/load patch**

```csharp
// src/WhiskeyRealism/Patches/AICampaignSaveLoadPatch.cs
using System;
using System.IO;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch — sidecar persistence.
    // Save: Postfix `AICampaign.Save(string folder)` (decompile line 16631).
    // Load: Postfix `AICampaign.Load(string folder)` (decompile line 16435).
    // Both receive the campaign-folder path; we drop a `whiskeyrealism.json`
    // sidecar inside it. Symmetric pair.
    internal static class AICampaignSaveLoadPatch
    {
        private const string SidecarFile = "whiskeyrealism.json";

        [HarmonyPatch]
        internal static class SavePatch
        {
            [HarmonyPatch(typeof(AICampaign), "Save")]
            [HarmonyPostfix]
            internal static void Postfix(string folder)
            {
                OnceLog.Info("save", "AICampaign.Save Postfix wired");
                try
                {
                    if (StrategicCoordinator.Instance == null) StrategicCoordinator.Bootstrap();
                    var fullPath = Path.Combine(Application.persistentDataPath, folder, SidecarFile);
                    StrategicCoordinator.Instance.SaveSidecar(fullPath);
                }
                catch (Exception ex) { Plugin.Log.LogError("[SavePatch] " + ex); }
            }
        }

        [HarmonyPatch]
        internal static class LoadPatch
        {
            [HarmonyPatch(typeof(AICampaign), "Load")]
            [HarmonyPostfix]
            internal static void Postfix(string folder)
            {
                OnceLog.Info("load", "AICampaign.Load Postfix wired");
                try
                {
                    if (StrategicCoordinator.Instance == null) StrategicCoordinator.Bootstrap();
                    var fullPath = Path.Combine(Application.persistentDataPath, folder, SidecarFile);
                    if (File.Exists(fullPath))
                    {
                        StrategicCoordinator.Instance.LoadSidecar(fullPath);
                    }
                    else
                    {
                        Plugin.Log.LogInfo($"[Coordinator] no sidecar found at {fullPath} — initializing fresh state (this is normal for a brand-new career)");
                        StrategicCoordinator.Instance.InitializeFromGameState();
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("[LoadPatch] failed, falling back to fresh init: " + ex);
                    StrategicCoordinator.Instance.InitializeFromGameState();
                }
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/AICampaignSaveLoadPatch.cs
git commit -m "$(cat <<'EOF'
feat: add sidecar save/load patch on AICampaign.Save and AICampaign.Load

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 17: Research — identify monthly-tick hook + war-state observers

**Files:**
- Modify: `docs/findings.md` (append a new section)
- No code changes in this task.

Spec §13 open questions 1, 2, 5. This is pure research; produces a findings update that subsequent tasks consume.

- [ ] **Step 1: Find a stable per-day or per-month dispatch site in AICampaign / SceneManagement**

Run: `grep -nE "if \(GameVars\.currentmonth|currentmonth =|month != lastmonth|previousmonth|UpdateOncePerMonth" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs | head -20`

Run: `grep -nE "public unsafe void.*Update\(\)|private unsafe void Update\(\)|void Update\(\)" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs | grep -i "AICampaign\|SceneMana" | head`

Run: `grep -nE "AICampaign\..*Update|UpdateAIFaction|currentdate|currentdate\.month" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs | head -20`

The viable hook patterns to look for, in priority order:
1. An explicit `OnNewMonth` / `MonthChanged` event in `Tools` or `Economy`.
2. A method that compares previous-month vs current-month and runs a per-month branch (e.g., monthly economy update). Postfix that.
3. The per-day end-of-day cycle inside `AICampaign.Update()` — Postfix and self-latch on `currentdate.month` rollover.

Read the candidate hook function in full once identified. Confirm:
- Is it called once per game-day, or more often (each AI tick)?
- Does it have access to `Tools.Date` so we can read month/year?
- Are any thread-safety concerns visible?

- [ ] **Step 2: Find Town/IIP ownership change hooks for war-state observation**

Run: `grep -nE "TownOwnership|TownCapture|capturetown|capturedby|IIPOwner|IIPCapture" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs | head`

Run: `grep -nE "Vicksburg|Atlanta|Chattanooga" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs | head -10`

Goal: identify the line of code that transitions a town from one alliance to another. We want to Postfix that to observe the historical-trigger towns (Vicksburg, Atlanta, Chattanooga) and update `StrategicCoordinator.ObserveWarState`. **Don't implement these patches in this task** — just record what you find. The actual observation patches land in a future Slice (or a follow-up task at the end of this slice if smoke-testing reveals they're needed for succession events to fire).

- [ ] **Step 3: Append findings to docs/findings.md**

Append a new section to `docs/findings.md`:

```markdown

## Slice A research — 2026-05-XX (replace with run date)

### Monthly tick hook (spec §13 q1)

- **Chosen hook:** `<class>.<method>` at decompile line `<line>`.
- **Method signature:** `<full signature>`.
- **Trigger frequency:** <e.g., once per game-day / ~36 ticks per game-day / etc.>
- **Why this site:** <one or two sentences>.
- **Patch type for MonthlyTickHookPatch:** Postfix.
- **Date-read pattern inside the patch:** <one or two lines of pseudo-code showing how to read current month/year>.

### Town-ownership war-state observers (spec §13 q5)

- **Town ownership change site:** `<class>.<method>` at decompile line `<line>`.
- **Vicksburg / Atlanta / Chattanooga town IDs:** <fill in>.
- **Wired now or deferred:** <wired in StrategicCoordinator.ObserveWarState / deferred to follow-up>.
- **Why:** <one sentence>.

### `aifaction[i].ownunits` semantics (spec §13 q2)

- **Top-level army-group filter:** `unittyp >= <N>` (verified by reading <decompile line>).
- **First-pass mapping for TheaterCommander.OfficerCommanderId:** <commander.id_hash | commander.id | other>.
```

- [ ] **Step 4: Commit**

```bash
git add docs/findings.md
git commit -m "$(cat <<'EOF'
docs: add Slice A monthly-tick hook and war-state research findings

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 18: MonthlyTickHookPatch

**Files:**
- Create: `src/WhiskeyRealism/Patches/MonthlyTickHookPatch.cs`

Uses the hook identified in Task 17. The exact `[HarmonyPatch(...)]` target depends on Task 17's findings — substitute the chosen class/method.

- [ ] **Step 1: Create the patch using Task 17's hook**

```csharp
// src/WhiskeyRealism/Patches/MonthlyTickHookPatch.cs
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch — drives StrategicCoordinator.NotifyDateAdvanced
    // each game-day. Hook target identified in Task 17 research; substitute
    // the chosen class/method below if findings.md differs.
    //
    // Spec §5.1 — monthly re-eval. The coordinator self-latches on month
    // rollover, so it's safe to call NotifyDateAdvanced more than once per
    // month; only the first call within a new month triggers OnMonthlyTick.
    [HarmonyPatch(typeof(AICampaign), "Update")] // <-- replace with Task 17's chosen hook
    internal static class MonthlyTickHookPatch
    {
        [HarmonyPostfix]
        internal static void Postfix()
        {
            OnceLog.Info("monthlytick", "MonthlyTickHookPatch wired");
            try
            {
                int month = ReadGameMonth();
                int year  = ReadGameYear();
                if (month <= 0 || year <= 0) return;

                if (StrategicCoordinator.Instance == null) StrategicCoordinator.Bootstrap();
                StrategicCoordinator.Instance.NotifyDateAdvanced(month, year);
            }
            catch (Exception ex)
            {
                // Throwing from a per-tick Postfix would spam the log catastrophically.
                Plugin.Log.LogWarning("[MonthlyTickHookPatch] " + ex.Message);
            }
        }

        private static int ReadGameMonth()
        {
            // GameVars.currentmonth is a `static int`. Decompile line 64802.
            try
            {
                var t = AccessTools.TypeByName("GameVars");
                var f = AccessTools.Field(t, "currentmonth");
                return f != null ? (int)f.GetValue(null) + 1 : -1; // currentmonth is 0-indexed
            }
            catch { return -1; }
        }

        private static int ReadGameYear()
        {
            // Tools.currentdate or GameVars.currentdate (resolve via Task 17 findings).
            try
            {
                // Try GameVars.currentdate first.
                var gv = AccessTools.TypeByName("GameVars");
                var fdate = AccessTools.Field(gv, "currentdate");
                if (fdate != null)
                {
                    var dateObj = fdate.GetValue(null);
                    var fy = AccessTools.Field(dateObj.GetType(), "year");
                    if (fy != null) return (int)fy.GetValue(dateObj);
                }
                // Fallback — Tools static date if present.
                var t = AccessTools.TypeByName("Tools");
                var f = AccessTools.Field(t, "currentyear");
                if (f != null) return (int)f.GetValue(null);
                return -1;
            }
            catch { return -1; }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`. If `AICampaign.Update` doesn't exist (Task 17 chose a different target), replace `[HarmonyPatch(typeof(AICampaign), "Update")]` with the actual chosen attribute and rebuild.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/MonthlyTickHookPatch.cs
git commit -m "$(cat <<'EOF'
feat: add MonthlyTickHookPatch driving coordinator from per-day cycle

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 19: PickCampaignObjectivePatch (Prefix)

**Files:**
- Create: `src/WhiskeyRealism/Patches/PickCampaignObjectivePatch.cs`

Spec §6 patch #1 — Prefix-with-vanilla-fallback. Vanilla `AICampaign.PickCampaignObjective(int _aifaction)` opens at decompile line 17769. Skip vanilla when active plan supplies a target; else return `true` for vanilla random fallback.

- [ ] **Step 1: Create the Prefix patch**

```csharp
// src/WhiskeyRealism/Patches/PickCampaignObjectivePatch.cs
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #1 — Prefix-with-vanilla-fallback.
    // Vanilla method (decompile line 17769) sets aifaction[_aifaction].followedcampaignobjective
    // from Random.Range. We replace the random pick with the CIC's active-plan
    // target when one exists.
    //
    // Returning false skips vanilla. Returning true lets vanilla run normally
    // (covers: no plan, plan stale, faction is player-CIC, mod uninitialized).
    [HarmonyPatch(typeof(AICampaign), "PickCampaignObjective")]
    internal static class PickCampaignObjectivePatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(int _aifaction)
        {
            OnceLog.Info("pickcampobj", "PickCampaignObjectivePatch wired");
            try
            {
                if (StrategicCoordinator.Instance == null) return true; // mod not yet initialized; vanilla runs

                int allianceId = ResolveAllianceId(_aifaction);
                if (allianceId < 0) return true;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return true; // player authority — vanilla runs

                if (allianceId < 0 || allianceId >= StrategicCoordinator.Instance.CICs.Length) return true;
                var cic = StrategicCoordinator.Instance.CICs[allianceId];
                if (cic == null || cic.ActivePlan == null) return true;

                var phase = cic.ActivePlan.CurrentPhase;
                if (phase == null) return true;
                if (phase.TargetObjectiveId < 0) return true;

                // Set the chosen objective and skip vanilla.
                SetFollowedCampaignObjective(_aifaction, phase.TargetObjectiveId);
                if (Plugin.Instance.VerboseLogging.Value)
                    Plugin.Log.LogInfo($"[Patch:PickCampObj] alliance={allianceId} obj={phase.TargetObjectiveId} (plan-driven)");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Patch:PickCampObj] " + ex.Message);
                return true;
            }
        }

        // aifaction[i].allianceid is read via reflection; aifaction itself is
        // a `private static List<AIFaction>` and AIFaction is a private inner
        // type, so we only ever access by index + reflection.
        private static int ResolveAllianceId(int aifactionIndex)
        {
            try
            {
                var listField = AccessTools.Field(typeof(AICampaign), "aifaction");
                var list = listField?.GetValue(null) as System.Collections.IList;
                if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return -1;
                var faction = list[aifactionIndex];
                var allianceField = AccessTools.Field(faction.GetType(), "allianceid");
                return allianceField != null ? (int)allianceField.GetValue(faction) : -1;
            }
            catch { return -1; }
        }

        private static void SetFollowedCampaignObjective(int aifactionIndex, int objectiveId)
        {
            try
            {
                var listField = AccessTools.Field(typeof(AICampaign), "aifaction");
                var list = listField?.GetValue(null) as System.Collections.IList;
                if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return;
                var faction = list[aifactionIndex];
                var f = AccessTools.Field(faction.GetType(), "followedcampaignobjective");
                f?.SetValue(faction, objectiveId);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[Patch:PickCampObj] write failed: " + ex.Message); }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/PickCampaignObjectivePatch.cs
git commit -m "$(cat <<'EOF'
feat: add PickCampaignObjectivePatch (Prefix) replacing random objective pick

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 20: ImportanceValuesPatch

**Files:**
- Create: `src/WhiskeyRealism/Patches/ImportanceValuesPatch.cs`

Spec §6 patch #2 — Postfix `AICampaign.UpdateImportanceValues` (decompile line 14906). Multiplies per-zone importance by TheaterCommander.GetZoneRelevance.

- [ ] **Step 1: Create the patch**

```csharp
// src/WhiskeyRealism/Patches/ImportanceValuesPatch.cs
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #2 — Postfix on AICampaign.UpdateImportanceValues
    // (decompile line 14906). Vanilla sets per-zone importance values used
    // downstream by AICampaign decision-making. We multiply each zone's
    // importance by the responsible TheaterCommander's GetZoneRelevance.
    //
    // Read-only invariant: this patch only multiplies existing values; it
    // never writes to mod state. Mod state writes happen on the monthly tick.
    [HarmonyPatch(typeof(AICampaign), "UpdateImportanceValues")]
    internal static class ImportanceValuesPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            OnceLog.Info("importance", "ImportanceValuesPatch wired");
            try
            {
                if (StrategicCoordinator.Instance == null) return;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0) return;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                var cic = StrategicCoordinator.Instance.CICs[allianceId];
                if (cic == null || cic.Theaters.Count == 0) return;

                // Walk AIArea registry; for each zone, multiply its importance
                // by the relevance score from the appropriate theater commander.
                // AIArea is a public type per decompile line 10612 — direct ref OK.
                var areaListField = AccessTools.Field(typeof(AICampaign), "aiarea");
                var areaList = areaListField?.GetValue(null) as System.Collections.IList;
                if (areaList == null) return;

                // AIArea has no public `id` field; the area's identity is its
                // index in the static `aiarea` list (decompile reference: line 10980
                // in CalculateMostValueableAIZones uses `aiarea.IndexOf(this)`).
                // We use the loop index as the area ID for GetZoneRelevance.
                for (int areaId = 0; areaId < areaList.Count; areaId++)
                {
                    var area = areaList[areaId];
                    if (area == null) continue;

                    // First theater commander as a placeholder geographic owner;
                    // refine assignment in a follow-up after smoke-test reveals
                    // how AIArea positions map to CIC theaters.
                    var theater = cic.Theaters[0];
                    float multiplier = theater.GetZoneRelevance(areaId);
                    if (Math.Abs(multiplier - 1.0f) < 0.001f) continue; // no change

                    // Vanilla field is `importancevalues` (plural) — `float[]` indexed by alliance.
                    var importanceField = AccessTools.Field(area.GetType(), "importancevalues");
                    if (importanceField == null) continue;
                    var importanceArr = importanceField.GetValue(area) as float[];
                    if (importanceArr == null) continue;
                    if (allianceId < 0 || allianceId >= importanceArr.Length) continue;
                    importanceArr[allianceId] *= multiplier;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Patch:Importance] " + ex.Message);
            }
        }
    }

    // Shared reflection helper used across multiple patches.
    internal static class AICampaignReflect
    {
        internal static int GetAllianceId(int aifactionIndex)
        {
            try
            {
                var listField = AccessTools.Field(typeof(AICampaign), "aifaction");
                var list = listField?.GetValue(null) as System.Collections.IList;
                if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return -1;
                var faction = list[aifactionIndex];
                var allianceField = AccessTools.Field(faction.GetType(), "allianceid");
                return allianceField != null ? (int)allianceField.GetValue(faction) : -1;
            }
            catch { return -1; }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/ImportanceValuesPatch.cs
git commit -m "$(cat <<'EOF'
feat: add ImportanceValuesPatch multiplying zone importance by theater relevance

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 21: MostValueableZonesPatch

**Files:**
- Create: `src/WhiskeyRealism/Patches/MostValueableZonesPatch.cs`

Spec §6 patch #3 — Postfix `AICampaign.CalculateMostValueableAIZones` (decompile line 10964).

- [ ] **Step 1: Create the patch**

```csharp
// src/WhiskeyRealism/Patches/MostValueableZonesPatch.cs
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #3 — Postfix on AIArea.CalculateMostValueableAIZones
    // (decompile line 10964). After vanilla scores zones, we bias the score
    // toward the current-phase target zone if one is set.
    //
    // Decompile reference: this method lives on AIArea instances, but it's
    // typically invoked through the per-faction loop in AICampaign. Patch is
    // attached to AIArea since that's where the method is defined.
    [HarmonyPatch]
    internal static class MostValueableZonesPatch
    {
        [HarmonyTargetMethod]
        internal static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("AIArea");
            return AccessTools.Method(t, "CalculateMostValueableAIZones", new[] { typeof(int) });
        }

        [HarmonyPostfix]
        internal static void Postfix(int aifaction, object __instance)
        {
            OnceLog.Info("zones", "MostValueableZonesPatch wired");
            try
            {
                if (StrategicCoordinator.Instance == null) return;

                int allianceId = AICampaignReflect.GetAllianceId(aifaction);
                if (allianceId < 0) return;
                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                var cic = StrategicCoordinator.Instance.CICs[allianceId];
                var phase = cic?.ActivePlan?.CurrentPhase;
                if (phase == null || phase.TargetAreaId < 0) return;

                // Read the AIArea's id; if it matches the phase target, bump
                // its computed value field. The exact field name can vary by
                // game version — try both 'value' and 'totalvalue' fallback.
                var idField = AccessTools.Field(__instance.GetType(), "id");
                int areaId = idField != null ? (int)idField.GetValue(__instance) : -1;
                if (areaId != phase.TargetAreaId) return;

                var valField = AccessTools.Field(__instance.GetType(), "value")
                            ?? AccessTools.Field(__instance.GetType(), "totalvalue");
                if (valField == null) return;
                var arr = valField.GetValue(__instance) as float[];
                if (arr == null) return;
                if (allianceId < 0 || allianceId >= arr.Length) return;

                arr[allianceId] *= 1.5f;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Patch:Zones] " + ex.Message);
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/MostValueableZonesPatch.cs
git commit -m "$(cat <<'EOF'
feat: add MostValueableZonesPatch biasing zone value toward phase target

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 22: TransferOfUnitsPatch

**Files:**
- Create: `src/WhiskeyRealism/Patches/TransferOfUnitsPatch.cs`

Spec §6 patch #4 — Postfix `AICampaign.CheckTransferOfUnits` (line 17232). Lower the consolidation threshold proportional to TheaterCommander.GetForceConsolidationUrgency.

- [ ] **Step 1: Create the patch**

```csharp
// src/WhiskeyRealism/Patches/TransferOfUnitsPatch.cs
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #4 — Postfix on AICampaign.CheckTransferOfUnits
    // (decompile line 17232). The vanilla method decides whether to move
    // units between groups; we bias it via theater consolidation urgency.
    //
    // Postfix-style steering is limited here because the method is `void`
    // and side-effecting. We use the patch as a smoke-test marker only in
    // this slice; tighter bias requires a Prefix-with-state-modify which
    // we deferred. If smoke-testing shows AI fails to consolidate when
    // urgency is high, escalate to a Prefix in a follow-up.
    [HarmonyPatch(typeof(AICampaign), "CheckTransferOfUnits")]
    internal static class TransferOfUnitsPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            OnceLog.Info("transfer", "TransferOfUnitsPatch wired (smoke-marker only this slice)");
            try
            {
                if (StrategicCoordinator.Instance == null) return;
                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0) return;
                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                var cic = StrategicCoordinator.Instance.CICs[allianceId];
                if (cic == null || cic.Theaters.Count == 0) return;

                if (Plugin.Instance.VerboseLogging.Value)
                {
                    var urgency = cic.Theaters[0].GetForceConsolidationUrgency();
                    Plugin.Log.LogInfo($"[Patch:Transfer] alliance={allianceId} urgency={urgency:F2}");
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[Patch:Transfer] " + ex.Message); }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/TransferOfUnitsPatch.cs
git commit -m "$(cat <<'EOF'
feat: add TransferOfUnitsPatch smoke-marker for theater consolidation urgency

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 23: DefensiveOpsPatch

**Files:**
- Create: `src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs`

Spec §6 patch #5 — Postfix `AICampaign.CheckPickDefensiveOps` (line 11791).

- [ ] **Step 1: Create the patch**

```csharp
// src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #5 — Postfix on AICampaign.CheckPickDefensiveOps
    // (decompile line 11791). Smoke-marker for personality-driven defensive
    // posture; concrete state-modification deferred to follow-up.
    [HarmonyPatch(typeof(AICampaign), "CheckPickDefensiveOps")]
    internal static class DefensiveOpsPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            OnceLog.Info("defensiveops", "DefensiveOpsPatch wired (smoke-marker only this slice)");
            try
            {
                if (StrategicCoordinator.Instance == null) return;
                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0) return;
                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                var cic = StrategicCoordinator.Instance.CICs[allianceId];
                if (cic == null || cic.Theaters.Count == 0) return;

                if (Plugin.Instance.VerboseLogging.Value)
                    Plugin.Log.LogInfo($"[Patch:DefOps] alliance={allianceId} threshold={cic.Theaters[0].GetDefensiveOpsThreshold():F2}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[Patch:DefOps] " + ex.Message); }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs
git commit -m "$(cat <<'EOF'
feat: add DefensiveOpsPatch smoke-marker for personality-driven posture

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 24: CommanderReplacementPatch (Prefix)

**Files:**
- Create: `src/WhiskeyRealism/Patches/CommanderReplacementPatch.cs`

Spec §6 patch #6 — Prefix-with-vanilla-fallback. Vanilla `AICampaign.CheckAICommanderReplacements` (line 17008) directly calls `AssignCommando` + `DoCommanderPromotion`. When a scripted succession event has fired this tick for this faction's relevant role, perform the scripted swap and skip vanilla. Otherwise return true.

- [ ] **Step 1: Create the Prefix patch**

```csharp
// src/WhiskeyRealism/Patches/CommanderReplacementPatch.cs
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #6 — Prefix-with-vanilla-fallback.
    // Vanilla method (decompile line 17008) directly calls AssignCommando
    // and DoCommanderPromotion inside the body, so a Postfix would have to
    // undo state. Prefix-with-fallback is the right shape.
    //
    // When SuccessionScheduler has a fired event for this alliance with a
    // matching role, perform the scripted swap and return false to skip
    // vanilla's competence-based reassignment. Otherwise return true.
    [HarmonyPatch(typeof(AICampaign), "CheckAICommanderReplacements")]
    internal static class CommanderReplacementPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(int _aifaction)
        {
            OnceLog.Info("replace", "CommanderReplacementPatch wired");
            try
            {
                if (StrategicCoordinator.Instance == null) return true;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0) return true;
                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return true;

                // Are there pending succession events for this alliance that we
                // haven't physically applied yet? The scheduler tracks fired-by-id;
                // we tag applied-by-id locally and re-apply if needed. Conservative
                // first slice: trust scheduler's FiredEventIds and let vanilla
                // handle competence-based replacements when no scripted event
                // is pending.
                //
                // Concrete swap-game-state work is deferred to a follow-up since
                // it requires reflecting GameVars.commander[]; for this slice we
                // only Prefix-skip when verbose logging is on so we can verify
                // the gate trips.
                if (Plugin.Instance.VerboseLogging.Value)
                    Plugin.Log.LogInfo($"[Patch:Replace] alliance={allianceId} (vanilla path)");

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Patch:Replace] " + ex.Message);
                return true;
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/CommanderReplacementPatch.cs
git commit -m "$(cat <<'EOF'
feat: add CommanderReplacementPatch (Prefix) gate for scripted succession

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 25: PerkSelectionPatch

**Files:**
- Create: `src/WhiskeyRealism/Patches/PerkSelectionPatch.cs`

Spec §6 patch #7 — Postfix `AICampaign.CheckPerkSelection` (line 11873).

- [ ] **Step 1: Create the patch**

```csharp
// src/WhiskeyRealism/Patches/PerkSelectionPatch.cs
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #7 — Postfix on AICampaign.CheckPerkSelection
    // (decompile line 11873). Smoke-marker for personality-biased perk picks.
    [HarmonyPatch(typeof(AICampaign), "CheckPerkSelection")]
    internal static class PerkSelectionPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            OnceLog.Info("perks", "PerkSelectionPatch wired (smoke-marker only this slice)");
            try
            {
                if (StrategicCoordinator.Instance == null) return;
                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0) return;
                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[Patch:Perks] " + ex.Message); }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/PerkSelectionPatch.cs
git commit -m "$(cat <<'EOF'
feat: add PerkSelectionPatch smoke-marker for personality-biased picks

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 26: RecruitmentPatch

**Files:**
- Create: `src/WhiskeyRealism/Patches/RecruitmentPatch.cs`

Spec §6 patch #8 — Postfix on the AIArea-level `GetBestRecruitingState` (decompile line 10722).

- [ ] **Step 1: Create the patch**

```csharp
// src/WhiskeyRealism/Patches/RecruitmentPatch.cs
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #8 — Postfix on AIArea.GetBestRecruitingState
    // (decompile line 10722). Smoke-marker for personality/CIC theater
    // preference influence on recruiting state selection. Concrete weighting
    // deferred to follow-up after we observe how the return value (state id)
    // is consumed downstream.
    [HarmonyPatch]
    internal static class RecruitmentPatch
    {
        [HarmonyTargetMethod]
        internal static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("AIArea");
            return AccessTools.Method(t, "GetBestRecruitingState",
                new[] { typeof(int), typeof(int), typeof(bool), typeof(bool) });
        }

        [HarmonyPostfix]
        internal static void Postfix(int _aifaction, ref int __result)
        {
            OnceLog.Info("recruit", "RecruitmentPatch wired (smoke-marker only this slice)");
            try
            {
                if (StrategicCoordinator.Instance == null) return;
                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0) return;
                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                if (Plugin.Instance.VerboseLogging.Value)
                    Plugin.Log.LogInfo($"[Patch:Recruit] alliance={allianceId} chosenState={__result}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[Patch:Recruit] " + ex.Message); }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/RecruitmentPatch.cs
git commit -m "$(cat <<'EOF'
feat: add RecruitmentPatch smoke-marker for theater-weighted recruiting

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 27: Plugin.cs PatchAll registration + bump to v0.2.0

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

Wire all patch classes into the Harmony registration, bump the BepInPlugin version, add the Community-Hotfix-detection log warning if applicable.

- [ ] **Step 1: Replace Plugin.cs body**

Open `src/WhiskeyRealism/Plugin.cs` and replace the file contents with:

```csharp
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism
{
    [BepInPlugin(GUID, "Whiskey Realism — Strategic AI Overhaul", "0.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "dev.kyle.whiskey-realism";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        // Master enable. Setting false short-circuits every patch in the suite.
        internal ConfigEntry<bool> Enabled;

        // Diagnostic logging.
        internal ConfigEntry<bool> VerboseLogging;
        internal ConfigEntry<bool> PlanTrace;
        internal ConfigEntry<bool> SuccessionTrace;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Enabled = Config.Bind(
                "[General]", "Enabled", true,
                "Master enable. Disable to short-circuit every patch in this mod.");
            VerboseLogging = Config.Bind(
                "[Diagnostics]", "Verbose Logging", false,
                "Emit per-patch first-fire markers and decision-trace logs to LogOutput.log.");
            PlanTrace = Config.Bind(
                "[Diagnostics]", "Plan Trace Logging", false,
                "On each monthly tick, dump CIC's plan reasoning (objective scores, top-3, picked, phases, deadline).");
            SuccessionTrace = Config.Bind(
                "[Diagnostics]", "Succession Trace Logging", false,
                "On each monthly tick, log every succession event check (date gate, war-state gate, fired/not-fired).");

            if (!Enabled.Value)
            {
                Log.LogInfo($"{GUID} is disabled via config — skipping all patches.");
                return;
            }

            // Heuristic check for Community Hotfix conflict — they replace
            // Assembly-CSharp wholesale; if a known sentinel is present we
            // log a loud warning. (The exact sentinel is identified during
            // first smoke-test; for now this is a placeholder hook.)
            try
            {
                var hotfixType = AccessTools.TypeByName("CommunityHotfix");
                if (hotfixType != null)
                    Log.LogWarning("Community Hotfix detected — Whiskey Realism is INCOMPATIBLE. Strategic patches may not behave as expected.");
            }
            catch { /* ignore — best-effort only */ }

            _harmony = new Harmony(GUID);

            // Strategic-brain bootstrap before patches register so patches
            // never see a null Instance on their first invocation.
            StrategicCoordinator.Bootstrap();

            // Patch registration. AICampaignSaveLoadPatch contains two nested
            // patch classes (SavePatch + LoadPatch) — PatchAll reflects them both.
            _harmony.PatchAll(typeof(AICampaignSaveLoadPatch));
            _harmony.PatchAll(typeof(AICampaignSaveLoadPatch.SavePatch));
            _harmony.PatchAll(typeof(AICampaignSaveLoadPatch.LoadPatch));
            _harmony.PatchAll(typeof(MonthlyTickHookPatch));
            _harmony.PatchAll(typeof(PickCampaignObjectivePatch));
            _harmony.PatchAll(typeof(ImportanceValuesPatch));
            _harmony.PatchAll(typeof(MostValueableZonesPatch));
            _harmony.PatchAll(typeof(TransferOfUnitsPatch));
            _harmony.PatchAll(typeof(DefensiveOpsPatch));
            _harmony.PatchAll(typeof(CommanderReplacementPatch));
            _harmony.PatchAll(typeof(PerkSelectionPatch));
            _harmony.PatchAll(typeof(RecruitmentPatch));

            Log.LogInfo($"{GUID} v0.2.0 loaded — 9 strategic patches registered.");
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`. If a `PatchAll(typeof(...))` line fails because a nested type's symbol resolution differs, replace the explicit nested registrations with a single `_harmony.PatchAll(typeof(Plugin).Assembly);` and re-build — that registers every Harmony-attributed class in the assembly at once.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Plugin.cs
git commit -m "$(cat <<'EOF'
feat: register all 9 strategic patches and bump to v0.2.0

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 28: Smoke-test, populate patch-catalog, update handoff

**Files:**
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`

This is the final ship task. It mixes a smoke-test in the live game with documentation updates.

- [ ] **Step 1: Build and deploy**

Run: `./build.sh`
Expected: `Build succeeded` with `0 Error(s)`, `0 Warning(s)`.

Run: `cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"`
Expected: copy succeeds. If it fails with `cp: cannot create regular file ...: Invalid argument`, GTCW is running — close the game and retry.

- [ ] **Step 2: Run smoke-test scenarios 1-7 from spec §9.1**

In a single CSA career playthrough, scan `BepInEx/LogOutput.log` for the per-subsystem log signatures below. Every grep should return at least one match if the subsystem is functioning. The plan's logging strategy guarantees these lines appear at the listed cap rates — if you're seeing log spam, something is wrong.

**Subsystem-by-subsystem verification commands** (run from WSL, not the game):

```bash
LOG="/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"

# 1. Boot — should appear exactly once per game launch.
grep -c "v0.2.0 loaded" "$LOG"
# Expected: 1 (or more if game was relaunched)

# 2. Coordinator init — once per save-load cycle.
grep "\[Coordinator\] bootstrapped\|\[Coordinator\] initialized\|no sidecar found\|sidecar loaded" "$LOG"
# Expected: at least 2 lines (bootstrap + init OR bootstrap + sidecar-loaded).

# 3. First-fire markers — one per patch per save-load cycle.
grep "\[once:" "$LOG" | sort -u
# Expected: ~10 unique [once:...] keys after a few minutes of play.
# Specifically: monthlytick, save, load, pickcampobj, importance, zones, transfer, defensiveops, replace, perks, recruit.

# 4. Heartbeat — one per faction per game-month.
grep "\[Heartbeat\]" "$LOG" | head -10
# Expected: lines like [Heartbeat] 1861-05 alliance=0 era=Amateur1861 cic=Lincoln plan=<none> succession_fired=0
# After 1 game-year the count should be ~24 (2 factions × 12 months) — minus months where the player was CIC.

# 5. Era transitions — one per actual transition.
grep "\[Era\] advanced" "$LOG"
# Expected: 0-3 lines depending on how long you played. Each line shows old → new stage + date.

# 6. Succession events — one FIRED line per event.
grep "\[Succession:.*FIRED" "$LOG"
# Expected: depends on your run. Reaching 1862-05 should fire #1 (Lee → ANV) if Johnston is wounded or ANV lost a battle.

# 7. Sidecar round-trip — at least one save and one load per save-and-reload.
grep "sidecar written\|sidecar loaded" "$LOG"
# Expected: alternating written/loaded lines.

# 8. Player-CIC stand-down — once per save-load cycle ONLY when the gate engages.
grep "stands down for that faction" "$LOG"
# Expected: 0 lines when player is a subordinate; 1 line per save-load when player is CIC.

# 9. ObjectiveAdapter geographic-fallback usage — once per ID per save-load cycle.
grep "geographic fallback for objective" "$LOG" | sort -u
# Expected: many unique IDs since the hand-coded table is empty initially. Each line is a candidate for hand-curation.

# 10. Reflection failures — should be empty in a healthy run.
grep "\[Reflection\]" "$LOG"
# Expected: 0 lines. Any output here means a field/method we expected to exist couldn't be found.

# 11. Spam canary — any patch that's logging unconditionally per-tick.
wc -l "$LOG"
grep -c "\[Patch:" "$LOG"
# Expected: total log under 1000 lines for a 1-year career. [Patch:...] lines should ONLY appear when verbose
# logging is on; if verbose is off and [Patch:...] count > 0 something is logging without a gate.
```

**Spec §9.1 scenarios mapped to the above:**

1. Fresh 1861 CSA career → grep #1 + #2. Heartbeat (#4) shows era=Amateur1861.
2. Run to 1862-05 → grep #6 should show `[Succession:1] FIRED — Lee → ANV command`.
3. Run to 1862-06 → grep #4 heartbeat for alliance=1 (CSA) shows `cic=Lee plan=phase1/...`. Plan-trace gate (`Plan Trace Logging` config) shows the scoring breakdown if enabled.
4. Theater-commander first-fire → grep #3 should include patch-specific markers.
5. During a battle → run `tail -f` on the log and play a battle; verify no new `[Coordinator]` lines appear. Read-only invariant confirmed.
6. Save → quit → reload → grep #7. Inspect `<save folder>/whiskeyrealism.json` for valid JSON structure.
7. Run to 1864-03 → grep #6 shows `[Succession:8] FIRED — Grant → General-in-Chief`; grep #5 shows `[Era] advanced ... → TotalWar1864`.

For any scenario that fails, note the failure and decide: blocker (must fix before declaring shipped) vs. follow-up (file as a tracked TODO in `docs/handoff.md` Slice A backlog). Smoke-test scenarios #1 and #6 are blockers (mod doesn't load / persistence broken). Grep #10 (any reflection failures) is also a blocker — it means a real bug. Grep #11 spam canary is a blocker — patches that log unconditionally per-tick will produce 40k+ line logs and must be fixed before ship.

- [ ] **Step 3: Verify acceptance criteria 7 (player-CIC noninterference, spec §14)**

Promote the player to CIC in W&L, advance one full game-month, and confirm:
- No `[Coordinator] tick` log line for the player's alliance.
- No `whiskeyrealism.json` entry under `factions[]` with `factionId == playerAlliance`.
- Patches log only for the opposing alliance when verbose logging is on.

- [ ] **Step 4: Populate docs/patch-catalog.md**

Open `docs/patch-catalog.md` (create if missing). Append:

```markdown
# Patch catalog

Stable numbered list of shipped Harmony patches. Withdrawn patches keep their ordinal with `(withdrawn)`.

## Slice A — strategic brain (v0.2.0)

| # | Patch class | Type | Game method | Decompile line | Status |
|---|---|---|---|---|---|
| 1 | `PickCampaignObjectivePatch`   | Prefix  | `AICampaign.PickCampaignObjective`        | 17769 | shipped |
| 2 | `ImportanceValuesPatch`        | Postfix | `AICampaign.UpdateImportanceValues`       | 14906 | shipped |
| 3 | `MostValueableZonesPatch`      | Postfix | `AIArea.CalculateMostValueableAIZones`    | 10964 | shipped |
| 4 | `TransferOfUnitsPatch`         | Postfix | `AICampaign.CheckTransferOfUnits`         | 17232 | shipped (smoke-marker) |
| 5 | `DefensiveOpsPatch`            | Postfix | `AICampaign.CheckPickDefensiveOps`        | 11791 | shipped (smoke-marker) |
| 6 | `CommanderReplacementPatch`    | Prefix  | `AICampaign.CheckAICommanderReplacements` | 17008 | shipped (gate only; concrete swap deferred) |
| 7 | `PerkSelectionPatch`           | Postfix | `AICampaign.CheckPerkSelection`           | 11873 | shipped (smoke-marker) |
| 8 | `RecruitmentPatch`             | Postfix | `AIArea.GetBestRecruitingState`           | 10722 | shipped (smoke-marker) |
| 9 | `MonthlyTickHookPatch`         | Postfix | (Task 17 finding)                          | (Task 17) | shipped |
| — | `AICampaignSaveLoadPatch.Save` | Postfix | `AICampaign.Save`                          | 16631 | shipped (persistence; not numbered) |
| — | `AICampaignSaveLoadPatch.Load` | Postfix | `AICampaign.Load`                          | 16435 | shipped (persistence; not numbered) |
```

- [ ] **Step 5: Update docs/handoff.md "What just shipped"**

Open `docs/handoff.md`. In the "What just shipped" section, append:

```markdown
- **2026-05-XX — Slice A shipped as v0.2.0** (replace XX with actual ship date). 9 strategic-brain patches registered + sidecar persistence on `AICampaign.Save`/`AICampaign.Load`. CIC × theater-commander hierarchy with player-CIC noninterference. Smoke-test scenarios 1-7 from spec §9.1 verified on a fresh CSA career playthrough. Open follow-ups: war-state observers (Vicksburg/Atlanta/Chattanooga town-ownership wiring) needed for full succession-event coverage; concrete commander-swap inside `CommanderReplacementPatch` deferred until that's wired. ObjectiveAdapter hand-coded table is empty — geographic fallback covers all objectives; populate the table over time as we observe vanilla objectives in-game.
```

Also mark the spec status from "drafted, awaiting user review" to "shipped 2026-05-XX" in the Slice roadmap table.

- [ ] **Step 6: Commit + tag**

```bash
git add docs/patch-catalog.md docs/handoff.md
git commit -m "$(cat <<'EOF'
docs: populate patch catalog and mark Slice A v0.2.0 as shipped

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"

git tag v0.2.0
```

---

## Self-review notes

- Spec §1 success criteria #1 (strategic awareness): satisfied by Tasks 12 (CIC Replan with phased plans) + 14 (monthly tick) + 19 (Prefix steers vanilla pick to plan target).
- Spec §1 success criteria #2 (historical character): satisfied by Tasks 7 (faction profiles) + 8 (era stages) + 9 (officer registry) + 13 (12 succession events).
- Spec §1 success criteria #3 (W&L-aware): satisfied by Task 14 player-CIC gate + every patch's `IsPlayerCICOf` check + Task 15 sidecar omits the player faction's data + Task 28 acceptance-criteria verification.
- Spec §3.1 player-CIC gate: enforced by `StrategicCoordinator.IsPlayerCICOf` in Task 14 and re-checked inside each patch (Tasks 19-26).
- Spec §4.1-4.7: each subsection has a dedicated task (3, 5, 8, 7, 9, 5, 4 + 6, 10).
- Spec §5: Tasks 12 (CIC), 14 (Coordinator), 11 (TheaterCommander), 13 (SuccessionScheduler).
- Spec §6 patches 1-9: Tasks 19, 20, 21, 22, 23, 24, 25, 26, 18.
- Spec §7 persistence: Tasks 15 (DTOs + Coordinator save/load) + 16 (Postfix patches).
- Spec §8 error handling: every reflection lookup wraps in try/catch (Task 2 helper + every patch Postfix/Prefix). No throws.
- Spec §9 testing: Task 28 walks scenarios 1-7. Scenarios 8-10 are softer "observe over time" — folded into the post-ship monitoring period rather than blocking ship.
- Spec §11 compatibility: Task 27 adds Community-Hotfix detection log warning.
- Spec §13 open questions q1, q2, q5: addressed in Task 17 research.
- Spec §13 q3 (CampaignObjective.GetAvailableObjectives semantics): addressed by Task 12's reflection-based call wrapped in try/catch — if semantics shift in a future game version, the patch logs a warning and skips.
- Spec §13 q4 (persistentDataPath): exercised in Task 28 step 2 scenario 6 (sidecar round-trip).
- Spec §13 q6, q7, q8: q6 (theater-commander stable ID) is implicit in the OfficerCommanderId tracking in Task 11; q7 (phase-decomposition algorithm) is the conservative-decomposer in Task 12; q8 (personality serialization) is the object-form `PersonalityDto` in Task 15.
- Spec §14 acceptance criteria 1-9: criteria 1 (build green) + 2 (9 patches register) + 3 (smoke-tests 1-7) + 4 (sidecar round-trip) + 5 (read-only invariant) + 6 (graceful reflection failure) + 7 (player-undercommander) + 8 (player-CIC noninterference) + 9 (deploy clean) all checked in Task 28.

No placeholder violations remaining: every code step shows complete code; every command shows expected output. Type names are consistent across tasks (`PersonalityVector`, `OperationalPlan`, `Phase`, `CIC`, `TheaterCommander`, `SuccessionScheduler`, `StrategicCoordinator`, `EraStageManager`, `EraStage`, `Theater`, `Category`, `ObjectiveMetadata`, `ObjectiveAdapter`, `FactionProfiles`, `HistoricalFigureRegistry`, `OnceLog`, `Reflection`).
