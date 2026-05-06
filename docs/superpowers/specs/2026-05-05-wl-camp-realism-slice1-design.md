# W&L Camp Realism Slice 1 Design

Status: design approved for spec drafting on 2026-05-05; pending user spec review before implementation plan.
Date: 2026-05-05
Scope: Whiskey & Lemons camp accounting and first-order payoff tuning only. This is not the full W&L hierarchy AI slice.

## Goal

Make the Whiskey & Lemons camp allocation system credit time correctly and make player camp choices feel material without replacing the vanilla career loop.

The first slice has three jobs:

- fix a vanilla short-camp accounting bug that undercredits mandatory camp time, especially Rest while wounded or sick;
- reduce over-smoothing for safe camp stations so allocation changes are visible before the full 30-day average settles;
- make unit-facing camp investment less over-diluted for larger commands so Drill, Motivate, Recruitment, and Readiness produce visible results.

This slice should not add new UI, new save state, new camp stations, new action lines, or direct edits to the installed game data. It should preserve vanilla station history, event triggers, diary thresholds, companion assignment, action gating, and W&L save/load behavior.

## Current Vanilla Behavior

Camp allocation is a 24-hour assignment budget. The UI stores assigned hours per station and computes free time as:

```text
available free time = 24 - sum(station.hoursassigned)
```

Relevant anchors:

- `CampStation.ChangeTimeForStation(float)` stages panel edits: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172916`
- `CampStation.CloseStationPanel()` commits staged hours to `campstationref.hoursassigned`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172908`
- `Camp.UpdateAvailableFreeTime(...)` recomputes the 24-hour remainder: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172163`

Assigned hours are not the same as credited daily station time. Each campaign day, `Camp.UpdateCamp()` calls `EvaluateCampTime()`, then updates station averages, station side effects, and event triggers:

- `Camp.UpdateCamp()`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:171954`
- daily call order: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172008`
- `Camp.EvaluateCampTime()`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172034`

The station bonus uses average credited time plus average companion time:

```text
bonus = clamp((averageHours + companionAverage - minTimeBonus)
              / max(0.001, maxTimeBonus - minTimeBonus), -1, 1)
```

Anchor: `Camp.Station.GetCurrentBonus(bool)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:171473`

Runtime modifiers then use:

```text
modifier = max(0, 1 + bonus * maxbonusmalus / divisor)
```

Anchor: `Camp.GetModifier(int, bool)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172522`

The history window is config-driven through `GamePrefs.camptimehistorydays`. The native fallback default is `50f`, but the installed W&L config imports `30`:

- native default: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:48396`
- config import: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:58781`
- installed value: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/Config/dlcwl_config.dat:456`

For unit-facing station effects, vanilla passes `dividebycommandedunits: true` for stations:

- `6` Drill the Troops: training gain, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:115246`
- `7` Motivate the Men: morale recovery, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:127877`
- `8` Recruitment: replacement inflow, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:114577`
- `11` Inspect Readiness: readiness recovery, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:115279`

Those effects are divided by `DLC_WL.GetNumberOfCommandedUnits()`, which makes positive investment increasingly hard to feel as command size grows.

## Confirmed Vanilla Bug

When actual camp time is less than the total minimum time required by stations, vanilla enters the short-camp branch:

```csharp
float num2 = Station.MinimumTimeAllStations();
if (camptimehistory[^1] < num2 && num2 > 0f)
{
    float num3 = camptimehistory[^1] / num2;
    for (int j = 0; j < stations.Count; j++)
    {
        if (stations[j].GetMinTime() > 0f)
        {
            stations[j].AddTimeHistory(camptimehistory[^1] * num3);
        }
        else
        {
            stations[j].AddTimeHistory(0f);
        }
    }
    return;
}
```

Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172085`

The credited line uses:

```text
actualCampTime * (actualCampTime / minimumTotal)
```

That undercredits time. It should credit each minimum station proportionally:

```text
stationMinimum * (actualCampTime / minimumTotal)
```

With current installed data, the normal Rest minimum is 3h. If the player has 2h actual camp time, vanilla credits Rest as:

```text
2 * (2 / 3) = 1.33h
```

The proportional minimum allocation should credit:

```text
3 * (2 / 3) = 2h
```

For a wounded or sick commander, `Station.GetMinTime()` adds 6h to Rest, making the Rest minimum 9h:

- Rest minimum logic: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:171428`
- WIA/sick forced Rest assignment: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172080`

With 2h actual camp time while wounded/sick, vanilla credits only:

```text
2 * (2 / 9) = 0.44h
```

The proportional allocation should credit:

```text
9 * (2 / 9) = 2h
```

This is a bugfix, not a balance buff. The player should not get more credited time than actual camp time; the credited minimum-station split should sum to the actual available camp time.

## Design Choice

Use targeted Harmony patches around `Camp.EvaluateCampTime()`, `Camp.Station.GetCurrentBonus(bool)`, and `Camp.GetModifier()`.

Do not patch camp UI allocation controls for this slice. UI methods are only allocation input; daily effects consume station histories and modifiers. UI patching would not fix the accounting bug and would increase risk.

Do not edit `Config/camp.dat`, `Config/actions.dat`, or `Config/dlcwl_config.dat` in the installed game folder. Data-only tuning remains available later, but this slice needs code because the bug and command-size dilution are code-owned.

## Feature 1: Short-Camp Minimum Allocation Fix

Patch surface:

- `Camp.EvaluateCampTime()` Prefix: refresh `Camp.currentstatus` from `Camp.PlayerUnitStatus()` before vanilla applies siege/field battle caps.
- `Camp.EvaluateCampTime()` Postfix: detect the vanilla short-camp branch and replace the just-added station history entries with proportional minimum-station credits.

The Prefix should:

- call `Camp.PlayerUnitStatus()` only after `Camp.UpdateCamp()` has refreshed `playercampaignunit` / `armygrp` for the day;
- guard against missing `battlefieldsetupref`, because `PlayerUnitStatus()` calls `battlefieldsetupref.SearchAutocalcFromMonument(...)` while checking battle status;
- write the result to `Camp.currentstatus`;
- catch and log reflection/runtime failures once, then allow vanilla behavior.

The Postfix should:

1. Read the latest actual camp hours from `Camp.camptimehistory`.
2. Compute current total minimum time from `Camp.stations[*].GetMinTime()`.
3. If `actualCampHours >= minimumTotal` or `minimumTotal <= 0`, do nothing.
4. For each station:
   - if station minimum is positive, corrected credit is `stationMin * actualCampHours / minimumTotal`;
   - otherwise corrected credit is `0`.
5. Replace only the last station `timehistory` entry added by vanilla. Do not add or remove history entries; preserve vanilla rolling-history length.
6. Leave companion time history untouched. Vanilla already updates companion time before station credited time, and companion assignment is a separate daily choice.

Invariant:

```text
sum(corrected station credits) == actual camp hours
```

This preserves vanilla's intent for scarce camp time: mandatory minimums receive the available time proportionally.

## Feature 2: Responsive Bonus Weighting

Patch surface:

- `Camp.Station.GetCurrentBonus(bool useaverage)` Postfix.

The installed config uses a 30-day average from `GamePrefs.camptimehistorydays`, which makes camp changes slow to feel. Slice 1 should blend vanilla's long average with a short recent average for safe station IDs:

```text
effectiveStationHours = longStationAverage * (1 - recentWeight) + recentStationAverage * recentWeight
effectiveCompanionHours = longCompanionAverage * (1 - recentWeight) + recentCompanionAverage * recentWeight
```

`longStationAverage` and `longCompanionAverage` should use vanilla's already-computed `averagetimespent` and `companionaveragetimespent`. `recentStationAverage` and `recentCompanionAverage` should sum the latest available entries and divide by `CampRecentBonusWindowDays`, not by entry count, so missing companion days still count as zero.

Defaults:

```text
CampRecentBonusWindowDays = 7
CampRecentBonusWeight = 0.35
```

The recomputed bonus uses the same vanilla thresholds and clamp:

```text
bonus = clamp((effectiveStationHours + effectiveCompanionHours - minTimeBonus)
              / max(0.001, maxTimeBonus - minTimeBonus), -1, 1)
```

Apply only when `useaverage == true`. Immediate UI comparison mode, which passes `useaverage == false`, remains vanilla.

Implementation must resolve the station ID from the native list with `Camp.stations.IndexOf(__instance)` and fall back to the vanilla result if the station cannot be resolved. The nested `Camp.Station` type has no explicit station ID field.

Apply only to station IDs:

- `0` Planning with Staff
- `1` Consult Subordinates
- `3` Military Studies
- `4` Leisure Time
- `6` Drill the Troops
- `7` Motivate the Men
- `8` Recruitment
- `10` Exercise Troops
- `11` Inspect Readiness

Do not apply recent weighting to:

- `2` Consult Companions, because it is a companion-time amplifier rather than a direct station payoff;
- `5` Inspect Logistics, because supply polarity needs runtime proof first;
- `9` Engage in Politics, because prestige scaling is already high leverage;
- `12` Rest, because health/fitness scaling is already high leverage and the accounting fix already improves short-camp Rest credit.

Responsive weighting must not change vanilla diary/event threshold firing in Slice 1. `Camp.Station.CheckEventTriggers()` and `Diary.UpdateEvents()` both call `GetCurrentBonus(true)` for camp-station threshold checks, so the implementation must suppress responsive weighting while those methods are running and return vanilla `__result` in that scope.

Call-site intent:

| Native caller | Anchor | Slice 1 behavior |
|---|---:|---|
| `Camp.Station.UpdateAllStations()` direct daily effects | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:171715` | responsive for included stations only |
| `Camp.GetModifier()` modifier consumers | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172545` | responsive for included stations only |
| camp panel/status icons and tooltips | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172843`, `:173071`, `:223546` | responsive display for included stations |
| immediate assignment comparison | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:173091` | vanilla because `useaverage == false` |
| `Camp.Station.CheckEventTriggers()` | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:171548` | vanilla long average |
| `Diary.UpdateEvents()` | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:183155` | vanilla long average |

## Feature 3: Less Over-Diluted Unit Payoff

Patch surface:

- `Camp.GetModifier(int stationid, bool dividebycommandedunits)` Postfix.

Apply only to station IDs `6`, `7`, `8`, and `11` when `dividebycommandedunits == true`.

Vanilla uses the raw commanded-unit count as the divisor. Slice 1 should use a softer divisor for these unit-facing camp systems:

```text
effectiveDivisor = max(1, pow(commandedUnitCount, CampUnitEffectDivisorPower))
```

The commanded-unit count comes from `DLC_WL.GetNumberOfCommandedUnits()`, which returns the cached `numberofcommandedunits` value:

- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:47890`

Default:

```text
CampUnitEffectDivisorPower = 0.5
```

That means a 9-unit command behaves like divisor 3 instead of divisor 9. A 4-unit command behaves like divisor 2 instead of divisor 4. A 1-unit command is unchanged.

The recomputed modifier remains clamped at zero:

```text
modifier = max(0, 1 + bonus * maxbonusmalus / effectiveDivisor)
```

This keeps command-size dilution, but stops large commands from making camp investment feel pointless.

Do not change station IDs `9` Politics or `12` Rest in this feature. They already have large multipliers and direct daily effects:

- Politics `maxbonusmalus = 1000`
- Rest `maxbonusmalus = 5`

Do not change station `5` Inspect Logistics in this feature. Decompile review suggests the formula polarity may be counterintuitive because the modifier is applied to a denominator-like supply variable. That needs a separate runtime proof before tuning.

## Configuration

Add bounded config entries in `Plugin.cs`:

```text
Enable W&L Camp Accounting Fix = true
Enable W&L Camp Responsive Bonus Weighting = true
W&L Camp Recent Bonus Window Days = 7
W&L Camp Recent Bonus Weight = 0.35
Enable W&L Camp Unit Payoff Tuning = true
W&L Camp Unit Effect Divisor Power = 0.5
Enable W&L Camp Verbose Trace = false
```

Bounds:

- recent bonus window minimum: `3`
- recent bonus window maximum: `14`
- recent bonus weight minimum: `0.0`
- recent bonus weight maximum: `0.5`
- divisor power minimum: `0.5`
- divisor power maximum: `1.0`

At `1.0`, the unit payoff tuning becomes vanilla-equivalent. At `0.5`, the default uses square-root command-size scaling.

The accounting fix, responsive weighting, and unit payoff tuning should be independently disableable. If a user reports compatibility trouble, they can turn off the tuning without disabling the vanilla bugfix.

## Logging

Logging must be bounded.

Required markers:

- one first-fire line when the camp patch initializes;
- one bounded warning if required camp reflection fails;
- optional verbose trace only when `Enable W&L Camp Verbose Trace` is true.

Verbose trace may include:

- actual camp hours;
- minimum total;
- station ID;
- vanilla credited last-history value;
- corrected credited value;
- long-average bonus and responsive bonus when responsive weighting changes the result materially;
- commanded-unit count and effective divisor for stations `6`, `7`, `8`, `11`.

Do not log every frame. `EvaluateCampTime()` is daily, but modifier calls can be frequent. Modifier trace must be signature-gated or config-only.

## Testing

Add a pure helper under `src/WhiskeyRealism/Strategic/` for the arithmetic, then add explicit compile includes to `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`.

Test cases:

- normal Rest short-camp: actual 2h, minimum 3h -> credit 2h;
- WIA/sick Rest short-camp: actual 2h, minimum 9h -> credit 2h;
- multiple minimum stations: credits sum to actual camp hours and preserve minimum proportions;
- enough-time branch: helper reports no correction needed;
- zero minimum total: no correction;
- recent weighting: 7-day improved allocation changes bonus more than vanilla 30-day average for included stations;
- excluded stations: `2`, `5`, `9`, and `12` remain vanilla in the responsive weighting helper;
- commanded-unit divisor: count 1 unchanged, count 4 divisor 2, count 9 divisor 3 at default power;
- modifier clamp: negative modifiers still clamp at zero;
- power `1.0` is vanilla-equivalent for unit payoff tuning.

Verification commands:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

For DLL-affecting implementation, also deploy and verify matching hashes before asking for runtime smoke:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Runtime smoke:

- start a W&L career;
- assign Rest while healthy and while wounded/sick if a test save is available;
- assign Drill, Motivate, Recruitment, and Readiness across a multi-unit command;
- advance several days;
- confirm no patch exceptions or log spam;
- confirm station bonuses and unit-facing effects move in the expected direction.

## Not In Scope

- no new camp UI;
- no new station definitions;
- no installed game config edits;
- no action research redesign;
- no companion system redesign;
- no logistics polarity fix until runtime supply proof exists;
- no changes to Politics prestige scaling;
- no changes to Rest/health scaling beyond correcting credited time;
- no W&L superior-officer order generation;
- no tactical battle AI changes.

## Runtime Claims Not Yet Verified

These findings are decompile-backed but still need in-game smoke before we treat the shipped patch as proven:

- whether refreshing `Camp.currentstatus` before `EvaluateCampTime()` changes field/siege cap behavior in normal play;
- whether station `5` Inspect Logistics is truly inverted or whether the surrounding supply method gives `num15` inverse semantics before the observed division;
- whether camp payoff changes are noticeable over a short W&L career smoke without becoming too strong over a month-long run.

## Acceptance Criteria

Implementation can be considered ready for user smoke only when:

- pure helper tests pass;
- full console harness passes;
- build succeeds;
- deployed DLL hash matches `dist/WhiskeyRealism.dll`;
- first-fire camp patch log appears in a W&L career;
- no repeated Harmony exceptions appear in `BepInEx/LogOutput.log`;
- short-camp correction telemetry, when verbose trace is enabled, shows corrected station credits summing to actual camp hours.
