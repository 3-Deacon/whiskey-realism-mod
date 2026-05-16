# Full Telemetry Framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the complete Whiskey Realism telemetry framework for tactical and campaign tuning while keeping customer logging quiet by default.

**Architecture:** Add a dedicated `WhiskeyRealism.Telemetry` subsystem with profile gates, typed event envelopes, bounded queue, writer thread, JSONL sidecars, budgets, summaries, issue bundle manifest, and session validation. Migrate tactical/campaign diagnostic tags through the router so tuning evidence leaves `LogOutput.log` and lands in structured sidecars with performance and failure visibility.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x, HarmonyX, Newtonsoft.Json already referenced from `refs/`, existing console harness under `tests/WhiskeyRealism.Tests`, no new packages.

---

## Source Anchors

- Design spec: `docs/superpowers/specs/2026-05-15-full-telemetry-framework-design.md`
- Repo policy: `AGENTS.md`
- Specs/plans policy: `docs/superpowers/AGENTS.md`
- Test harness policy: `tests/WhiskeyRealism.Tests/AGENTS.md`
- Current config owner: `src/WhiskeyRealism/Plugin.cs`
- Existing bounded once-log helper: `src/WhiskeyRealism/Util/OnceLog.cs`
- Tactical firehose center: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
- Tactical typed formatters: `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`, `src/WhiskeyRealism/Tactical/Operations/TacticalOperationsTelemetry.cs`
- Campaign cadence/logging center: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Campaign operation runtime logging: `src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs`, `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`, `src/WhiskeyRealism/Strategic/CIC.cs`
- Current local runtime proof from spec: stale `LogOutput.log` had `251020` lines, about `133 MB`, more than `134000` `[TacticalDecisionMatrix]` rows, more than `26000` `[TacticalPlayerOrder]` rows, and more than `15000` `[TacticalFormationChange]` rows.

## Handoff Checkpoint - 2026-05-15 After Task 6

Resume from worktree branch `telemetry-framework-plan` at commit `e617d2eaac70fcd54a0702d1362b50499456ef2b` or later.

Completed and reviewed:

- Task 1, event schema/contracts: complete and approved.
- Task 2, session/budget/retention/issue bundle core: complete and approved.
- Task 3, profile config/router/writer lifecycle: complete and approved at `48be43d`.
- Task 4, performance telemetry scopes/failure isolation: complete and approved at `357c3c9`.
- Task 5, legacy tag policy/parser and production deployment-routing safeguards: complete and approved at `bbe02c1`.
- Task 6, tactical migration: complete and approved at `e617d2e`.

Task 6 closeout facts:

- Tactical firehose rows now route through typed `TelemetryRouter.Emit(...)` where Task 6 required typed decision paths, or through legacy sidecar routing for remaining diagnostic rows.
- Typed Task 6 paths exist for command assignment, posture execution, reserve commit gate, charge gate, W&L/player order, and decision matrix rows. They include stable `inputSignature` values and the minimum decision-analysis fields required by the design (`confidence`, `score`, `selectedTarget`, `gateResult`, `gateReason`, `writeAction`, `writeResult`) where applicable.
- Hot gate telemetry remains bounded: charge-gate typed denial rows are once-per-unit bounded, and reserve commit-gate rows are once-per-key bounded.
- `TacticalDecisionMatrix` typed rows remain `TelemetryCategory.State`, and command-assignment rows preserve the legacy operations-ledger analysis fields.
- The legacy `missingParents` command-tree field name is preserved. `TelemetryTagPolicy.IsSeriousLine` no longer treats all `missing` substrings as serious, so benign rows like `missingParents=0` remain sidecar-only.
- The closure command still lists existing `OnceLog.Info(...)` sites and campaign/strategic `Plugin.Log.LogInfo(...)` sites. `OnceLog` routes through telemetry from Task 5, and the remaining campaign/strategic migration is Task 7. Focused direct tactical high-volume `Plugin.Log.LogInfo` search for `Tactical`, `TacDeploy`, and `PlayerOrderIntent` returned no matches.
- Latest high spec review and xhigh code review both approved Task 6 at `e617d2e`.

Verification evidence at the checkpoint:

- `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` passed after Task 6 changes. Existing CS0649 DTO warnings remain.
- `./build.sh` passed after Task 6 changes with `0 Warning(s)` and `0 Error(s)`.
- `git diff --check` passed.
- No deploy or in-game runtime smoke has been performed for this telemetry branch; Task 10 owns deploy, hash verification, and runtime smoke.

Next context starts at Task 7:

- Do not restart Tasks 1-6. Re-run `git status --short --branch`, `git rev-parse HEAD`, the harness, and build before editing.
- Use high-effort implementation and review for Task 7, matching the current session's standard.
- Task 7 should migrate campaign, W&L, and strategic tuning logs to typed `TelemetryRouter.Emit(...)` where signatures exist, or legacy routing where they do not.
- For direct production log sites that still need compatibility main-log fallback, prefer the established `TelemetryRouter.LegacyInfoToMainLogIfAllowed(...)` pattern instead of direct `Plugin.Log.LogInfo(...)`.
- Watch item: `TelemetryTagPolicy.IsSeriousLine` still treats broad `error` and failure words as serious. Future broad migrations should avoid promoting benign high-volume field names to the main log.

## Worktree Gate

- [ ] Confirm isolated workspace state:

```bash
git status --short --branch
git rev-parse --git-dir
git rev-parse --git-common-dir
git worktree list
```

Expected: implementation runs from a linked worktree branch, not the primary `main` checkout.

- [ ] Ensure the assembly-reference symlink exists:

```bash
ls -l refs
```

Expected: `refs -> ../../refs` or equivalent worktree-safe symlink.

- [ ] Keep unrelated untracked files out of all staging. In the primary checkout, `654890_47.jpg` and `notperfect.jpg` were present before this plan branch.

## Baseline

- [ ] Run the console harness before code edits:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: exits `0`. Existing compiler warnings are acceptable only if the run prints PASS lines and no unhandled exception.

- [ ] Run the plugin build before code edits:

```bash
./build.sh
```

Expected: `dist/WhiskeyRealism.dll` is produced with `0 Error(s)`.

## File Map

Create:

- `src/WhiskeyRealism/Telemetry/TelemetryContracts.cs`
- `src/WhiskeyRealism/Telemetry/TelemetryFields.cs`
- `src/WhiskeyRealism/Telemetry/TelemetryJson.cs`
- `src/WhiskeyRealism/Telemetry/TelemetryBudget.cs`
- `src/WhiskeyRealism/Telemetry/TelemetryQueue.cs`
- `src/WhiskeyRealism/Telemetry/TelemetrySession.cs`
- `src/WhiskeyRealism/Telemetry/TelemetryManifest.cs`
- `src/WhiskeyRealism/Telemetry/TelemetrySummary.cs`
- `src/WhiskeyRealism/Telemetry/TelemetryRouter.cs`
- `src/WhiskeyRealism/Telemetry/TelemetryWriter.cs`
- `src/WhiskeyRealism/Telemetry/TelemetryRuntime.cs`
- `src/WhiskeyRealism/Telemetry/TelemetryPerf.cs`
- `src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs`
- `src/WhiskeyRealism/Telemetry/TelemetryLegacyParser.cs`
- `src/WhiskeyRealism/Telemetry/TelemetryIssueBundle.cs`
- `src/WhiskeyRealism/Telemetry/TelemetrySessionValidator.cs`
- `docs/telemetry.md`

Modify:

- `src/WhiskeyRealism/Plugin.cs`
- `src/WhiskeyRealism/Util/OnceLog.cs`
- `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
- `src/WhiskeyRealism/Patches/TacticalDeploymentObserverPatch.cs`
- `src/WhiskeyRealism/Patches/TacticalDeploymentTerrainDisciplinePatch.cs`
- `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs`
- `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`
- `src/WhiskeyRealism/Patches/BattleReserveCommitGatePatch.cs`
- `src/WhiskeyRealism/Patches/TacticalReserveOrderDelayGuardPatch.cs`
- `src/WhiskeyRealism/Patches/TacticalPathfinderDisciplinePatch.cs`
- `src/WhiskeyRealism/Patches/TacticalHqAutoLinkGuardPatch.cs`
- `src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs`
- `src/WhiskeyRealism/Patches/PlayerSubordinateOrderPatch.cs`
- `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- `src/WhiskeyRealism/Strategic/CIC.cs`
- `src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs`
- `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`
- `src/WhiskeyRealism/Strategic/SuccessionScheduler.cs`
- `src/WhiskeyRealism/Strategic/Construction/ConstructionRuntime.cs`
- `src/WhiskeyRealism/Strategic/Fiscal/FiscalRuntime.cs`
- `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- `tests/WhiskeyRealism.Tests/Program.cs`
- `docs/patch-catalog.md`
- `docs/handoff.md`
- `MEMORY.md`

Only include pure telemetry files in the console harness. Keep `TelemetryRuntime.cs`, `TelemetryWriter.cs`, and plugin lifecycle code out of tests if they depend on BepInEx or Unity runtime types.

## Required Routing Closure

Every Whiskey tuning/diagnostic tag must end in one of three states: sidecar-routed, main-log allowlisted, or removed as obsolete.

- [ ] Capture the pre-migration tag inventory:

```bash
rg -o "\[[A-Za-z0-9:& _.-]+\]" src/WhiskeyRealism | sort -u > /tmp/wr-telemetry-tags-before.txt
cat /tmp/wr-telemetry-tags-before.txt
```

- [ ] Capture all direct info/warning/error log sites:

```bash
rg -n "Plugin\.Log\.LogInfo|Plugin\.Log\.LogWarning|Plugin\.Log\.LogError|OnceLog\.Info|OnceLog\.Warning" src/WhiskeyRealism > /tmp/wr-log-sites-before.txt
```

- [ ] After migration, rerun the two commands and verify no high-volume tactical/campaign tuning tag writes directly to `Plugin.Log.LogInfo`.

## Task 1: Core Schema, Fields, And JSONL Serializer

**Files:**
- Create: `src/WhiskeyRealism/Telemetry/TelemetryContracts.cs`
- Create: `src/WhiskeyRealism/Telemetry/TelemetryFields.cs`
- Create: `src/WhiskeyRealism/Telemetry/TelemetryJson.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] Add compile entries to `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` near the existing tactical pure-file entries:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetryContracts.cs" Link="Telemetry\TelemetryContracts.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetryFields.cs" Link="Telemetry\TelemetryFields.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetryJson.cs" Link="Telemetry\TelemetryJson.cs" />
```

- [ ] Add `using WhiskeyRealism.Telemetry;` to `tests/WhiskeyRealism.Tests/Program.cs`.

- [ ] Register these tests in the top `tests` array:

```csharp
("telemetry json writes required fields", TelemetryJsonWritesRequiredFields),
("telemetry json escapes strings safely", TelemetryJsonEscapesStringsSafely),
("telemetry fields sanitize nulls and nonfinite numbers", TelemetryFieldsSanitizeNullsAndNonfiniteNumbers),
("telemetry decision requires input signature", TelemetryDecisionRequiresInputSignature),
```

- [ ] Add test methods before the assertion helpers:

```csharp
private static void TelemetryJsonWritesRequiredFields()
{
    var ev = TelemetryEvent.Create(
        "session-1", TelemetryProfile.TacticalTuning, TelemetryLayer.Tactical,
        TelemetryCategory.Decision, "CommandAssignment", TelemetrySeverity.Info)
        .WithCampaignDate("1861-06-01")
        .WithBattleId("battle-1")
        .WithSide(0)
        .WithAlliance(1)
        .WithDecision("Probe", "support-required", "sig-123");
    string json = TelemetryJson.ToJsonLine(ev);
    AssertContains(json, "\"schema\":\"wr.telemetry.v1\"", "schema");
    AssertContains(json, "\"sessionId\":\"session-1\"", "session");
    AssertContains(json, "\"layer\":\"Tactical\"", "layer");
    AssertContains(json, "\"inputSignature\":\"sig-123\"", "input signature");
}

private static void TelemetryJsonEscapesStringsSafely()
{
    var ev = TelemetryEvent.Create(
        "s", TelemetryProfile.CampaignTuning, TelemetryLayer.Campaign,
        TelemetryCategory.Failure, "Serializer", TelemetrySeverity.Warning)
        .WithField("message", "quote=\" slash=\\ newline=\n tab=\t");
    string json = TelemetryJson.ToJsonLine(ev);
    AssertContains(json, "quote=\\\"", "quote escaped");
    AssertContains(json, "slash=\\\\", "slash escaped");
    AssertContains(json, "newline=\\n", "newline escaped");
    AssertContains(json, "tab=\\t", "tab escaped");
}

private static void TelemetryFieldsSanitizeNullsAndNonfiniteNumbers()
{
    var fields = new TelemetryFields()
        .Add("missing", (string)null)
        .Add("nan", float.NaN)
        .Add("inf", float.PositiveInfinity)
        .Add("ok", 1.25f);
    AssertEqual("-", fields.GetString("missing"), "null string");
    AssertEqual(0.0, fields.GetDouble("nan"), "nan");
    AssertEqual(0.0, fields.GetDouble("inf"), "inf");
    AssertEqual(1.25, fields.GetDouble("ok"), "finite");
    AssertTrue(fields.GetBool("invalidFloat"), "invalid float marker");
}

private static void TelemetryDecisionRequiresInputSignature()
{
    bool threw = false;
    try
    {
        TelemetryEvent.Create(
            "s", TelemetryProfile.FullTuning, TelemetryLayer.Tactical,
            TelemetryCategory.Decision, "DecisionWithoutSignature", TelemetrySeverity.Info)
            .WithDecision("Attack", "test", "");
    }
    catch (ArgumentException)
    {
        threw = true;
    }
    AssertTrue(threw, "decision rows must reject blank input signatures");
}
```

- [ ] Run the targeted harness and confirm it fails because the new types do not exist yet:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure naming `TelemetryEvent` or `TelemetryProfile`.

- [ ] Create `TelemetryContracts.cs` with enums and event envelope:

```csharp
using System;

namespace WhiskeyRealism.Telemetry
{
    internal enum TelemetryProfile { Off = 0, TacticalTuning = 1, CampaignTuning = 2, FullTuning = 3 }
    internal enum TelemetryLayer { System = 0, Tactical = 1, Campaign = 2 }
    internal enum TelemetryCategory { Health = 0, Failure = 1, Performance = 2, Decision = 3, Gate = 4, Write = 5, State = 6, Trace = 7 }
    internal enum TelemetrySeverity { Debug = 0, Info = 1, Warning = 2, Error = 3 }

    internal sealed class TelemetryEvent
    {
        internal const string Schema = "wr.telemetry.v1";

        internal string SessionId { get; private set; }
        internal TelemetryProfile Profile { get; private set; }
        internal TelemetryLayer Layer { get; private set; }
        internal TelemetryCategory Category { get; private set; }
        internal string EventName { get; private set; }
        internal TelemetrySeverity Severity { get; private set; }
        internal DateTime Utc { get; private set; }
        internal string BattleId { get; private set; }
        internal string CampaignDate { get; private set; }
        internal int Side { get; private set; }
        internal int Alliance { get; private set; }
        internal string Unit { get; private set; }
        internal string Phase { get; private set; }
        internal string Decision { get; private set; }
        internal string Reason { get; private set; }
        internal double DurationMs { get; private set; }
        internal TelemetryFields Fields { get; private set; }

        private TelemetryEvent() { }

        internal static TelemetryEvent Create(string sessionId, TelemetryProfile profile, TelemetryLayer layer, TelemetryCategory category, string eventName, TelemetrySeverity severity)
        {
            return new TelemetryEvent
            {
                SessionId = SafeText(sessionId),
                Profile = profile,
                Layer = layer,
                Category = category,
                EventName = SafeText(eventName),
                Severity = severity,
                Utc = DateTime.UtcNow,
                BattleId = "-",
                CampaignDate = "-",
                Side = -1,
                Alliance = -1,
                Unit = "-",
                Phase = "-",
                Decision = "-",
                Reason = "-",
                DurationMs = 0.0,
                Fields = new TelemetryFields()
            };
        }

        internal TelemetryEvent WithBattleId(string value) { BattleId = SafeText(value); return this; }
        internal TelemetryEvent WithCampaignDate(string value) { CampaignDate = SafeText(value); return this; }
        internal TelemetryEvent WithSide(int value) { Side = value; return this; }
        internal TelemetryEvent WithAlliance(int value) { Alliance = value; return this; }
        internal TelemetryEvent WithUnit(string value) { Unit = SafeText(value); return this; }
        internal TelemetryEvent WithPhase(string value) { Phase = SafeText(value); return this; }
        internal TelemetryEvent WithDurationMs(double value) { DurationMs = TelemetryFields.SafeNumber(value); return this; }
        internal TelemetryEvent WithField(string key, string value) { Fields.Add(key, value); return this; }
        internal TelemetryEvent WithField(string key, int value) { Fields.Add(key, value); return this; }
        internal TelemetryEvent WithField(string key, double value) { Fields.Add(key, value); return this; }
        internal TelemetryEvent WithField(string key, bool value) { Fields.Add(key, value); return this; }

        internal TelemetryEvent WithDecision(string decision, string reason, string inputSignature)
        {
            if (string.IsNullOrWhiteSpace(inputSignature))
                throw new ArgumentException("Decision telemetry requires inputSignature.", nameof(inputSignature));
            Decision = SafeText(decision);
            Reason = SafeText(reason);
            Fields.Add("inputSignature", inputSignature);
            return this;
        }

        internal static string SafeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
    }
}
```

- [ ] Create `TelemetryFields.cs` as a deterministic field bag with safe numeric handling:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;

namespace WhiskeyRealism.Telemetry
{
    internal sealed class TelemetryFields
    {
        private readonly SortedDictionary<string, object> _values = new SortedDictionary<string, object>(StringComparer.Ordinal);
        internal IEnumerable<KeyValuePair<string, object>> Values => _values;

        internal TelemetryFields Add(string key, string value)
        {
            _values[SafeKey(key)] = string.IsNullOrWhiteSpace(value) ? "-" : value;
            return this;
        }

        internal TelemetryFields Add(string key, int value) { _values[SafeKey(key)] = value; return this; }
        internal TelemetryFields Add(string key, bool value) { _values[SafeKey(key)] = value; return this; }

        internal TelemetryFields Add(string key, double value)
        {
            double safe = SafeNumber(value);
            if (safe != value) _values["invalidFloat"] = true;
            _values[SafeKey(key)] = safe;
            return this;
        }

        internal string GetString(string key) => _values.TryGetValue(SafeKey(key), out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : "-";
        internal double GetDouble(string key) => _values.TryGetValue(SafeKey(key), out var value) ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : 0.0;
        internal bool GetBool(string key) => _values.TryGetValue(SafeKey(key), out var value) && value is bool b && b;

        internal static double SafeNumber(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;
        }

        private static string SafeKey(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? "field" : key.Trim();
        }
    }
}
```

- [ ] Create `TelemetryJson.cs` with manual JSONL serialization and newline termination:

```csharp
using System;
using System.Globalization;
using System.Text;

namespace WhiskeyRealism.Telemetry
{
    internal static class TelemetryJson
    {
        internal static string ToJsonLine(TelemetryEvent ev)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            WritePair(sb, "schema", TelemetryEvent.Schema, first: true);
            WritePair(sb, "ts", ev.Utc.ToString("o", CultureInfo.InvariantCulture));
            WritePair(sb, "sessionId", ev.SessionId);
            WritePair(sb, "profile", ev.Profile.ToString());
            WritePair(sb, "layer", ev.Layer.ToString());
            WritePair(sb, "category", ev.Category.ToString());
            WritePair(sb, "event", ev.EventName);
            WritePair(sb, "severity", ev.Severity.ToString());
            WritePair(sb, "battleId", ev.BattleId);
            WritePair(sb, "campaignDate", ev.CampaignDate);
            WritePair(sb, "side", ev.Side);
            WritePair(sb, "alliance", ev.Alliance);
            WritePair(sb, "unit", ev.Unit);
            WritePair(sb, "phase", ev.Phase);
            WritePair(sb, "decision", ev.Decision);
            WritePair(sb, "reason", ev.Reason);
            WritePair(sb, "durationMs", ev.DurationMs);
            sb.Append(",\"fields\":{");
            bool firstField = true;
            foreach (var pair in ev.Fields.Values)
            {
                if (!firstField) sb.Append(',');
                firstField = false;
                WriteString(sb, pair.Key);
                sb.Append(':');
                WriteValue(sb, pair.Value);
            }
            sb.Append("}}\n");
            return sb.ToString();
        }

        private static void WritePair(StringBuilder sb, string key, string value, bool first = false)
        {
            if (!first) sb.Append(',');
            WriteString(sb, key);
            sb.Append(':');
            WriteString(sb, value);
        }

        private static void WritePair(StringBuilder sb, string key, int value)
        {
            sb.Append(',');
            WriteString(sb, key);
            sb.Append(':').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void WritePair(StringBuilder sb, string key, double value)
        {
            sb.Append(',');
            WriteString(sb, key);
            sb.Append(':').Append(TelemetryFields.SafeNumber(value).ToString("0.###", CultureInfo.InvariantCulture));
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            if (value is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (value is int i) { sb.Append(i.ToString(CultureInfo.InvariantCulture)); return; }
            if (value is double d) { sb.Append(TelemetryFields.SafeNumber(d).ToString("0.###", CultureInfo.InvariantCulture)); return; }
            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            value = value ?? "-";
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c < 32 ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture) : c.ToString()); break;
                }
            }
            sb.Append('"');
        }
    }
}
```

- [ ] Run the harness:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: new telemetry schema tests pass.

- [x] Commit:

```bash
git add src/WhiskeyRealism/Telemetry/TelemetryContracts.cs src/WhiskeyRealism/Telemetry/TelemetryFields.cs src/WhiskeyRealism/Telemetry/TelemetryJson.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add telemetry event schema"
```

## Task 2: Budget, Queue, Session, Manifest, And Issue Bundle Core

**Files:**
- Create: `src/WhiskeyRealism/Telemetry/TelemetryBudget.cs`
- Create: `src/WhiskeyRealism/Telemetry/TelemetryQueue.cs`
- Create: `src/WhiskeyRealism/Telemetry/TelemetrySession.cs`
- Create: `src/WhiskeyRealism/Telemetry/TelemetryManifest.cs`
- Create: `src/WhiskeyRealism/Telemetry/TelemetryIssueBundle.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] Add compile entries:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetryBudget.cs" Link="Telemetry\TelemetryBudget.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetryQueue.cs" Link="Telemetry\TelemetryQueue.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetrySession.cs" Link="Telemetry\TelemetrySession.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetryManifest.cs" Link="Telemetry\TelemetryManifest.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetryIssueBundle.cs" Link="Telemetry\TelemetryIssueBundle.cs" />
```

- [ ] Add tests:

```csharp
("telemetry budget drops trace first", TelemetryBudgetDropsTraceFirst),
("telemetry queue preserves failure under pressure", TelemetryQueuePreservesFailureUnderPressure),
("telemetry session id sorts by utc milliseconds", TelemetrySessionIdSortsByUtcMilliseconds),
("telemetry manifest redacts user paths", TelemetryManifestRedactsUserPaths),
```

- [ ] Add these test methods:

```csharp
private static void TelemetryBudgetDropsTraceFirst()
{
    var budget = new TelemetryBudget(totalBytes: 1000, rotateBytes: 250);
    AssertTrue(budget.Allow(TelemetryCategory.Trace, 900), "trace allowed before cap");
    budget.RecordBytes(TelemetryCategory.Trace, 900);
    AssertFalse(budget.Allow(TelemetryCategory.Trace, 200), "trace cut first near cap");
    AssertTrue(budget.Allow(TelemetryCategory.Failure, 200), "failure protected near cap");
}

private static void TelemetryQueuePreservesFailureUnderPressure()
{
    var queue = new TelemetryQueue(capacity: 2);
    queue.TryEnqueue(EventForQueue(TelemetryCategory.Trace, "trace-a"));
    queue.TryEnqueue(EventForQueue(TelemetryCategory.State, "state-a"));
    queue.TryEnqueue(EventForQueue(TelemetryCategory.Failure, "failure-a"));
    var drained = queue.Drain(10);
    AssertEqual(2, drained.Count, "queue count");
    AssertTrue(drained.Exists(e => e.Category == TelemetryCategory.Failure), "failure preserved");
    AssertTrue(queue.DroppedCount > 0, "dropped counted");
}

private static TelemetryEvent EventForQueue(TelemetryCategory category, string name)
{
    return TelemetryEvent.Create("s", TelemetryProfile.FullTuning, TelemetryLayer.System, category, name, TelemetrySeverity.Info);
}

private static void TelemetrySessionIdSortsByUtcMilliseconds()
{
    string a = TelemetrySession.CreateSessionId(new DateTime(2026, 5, 15, 12, 0, 0, 1, DateTimeKind.Utc), 12, "abcdef1234567890");
    string b = TelemetrySession.CreateSessionId(new DateTime(2026, 5, 15, 12, 0, 0, 2, DateTimeKind.Utc), 12, "abcdef1234567890");
    AssertTrue(string.CompareOrdinal(a, b) < 0, "session ids sort by start time");
    AssertContains(a, "p12", "pid");
    AssertContains(a, "abcdef123456", "hash prefix");
}

private static void TelemetryManifestRedactsUserPaths()
{
    string redacted = TelemetryIssueBundle.Redact(@"C:\Users\Kyle\AppData\Roaming\test token=secret");
    AssertContains(redacted, @"C:\Users\<redacted>", "user path redacted");
    AssertFalse(redacted.Contains("secret"), "token redacted");
}
```

- [ ] Implement `TelemetryBudget` with explicit cut order: `Trace`, low-priority `State`, low-priority `Decision`, low-priority `Gate`/`Write`; `Failure` and `Health` remain protected. Track emitted bytes, dropped counts by category, and rotation decisions.

- [ ] Implement `TelemetryQueue` as a lock-protected multi-producer/single-consumer queue:

```csharp
internal sealed class TelemetryQueue
{
    private readonly object _lock = new object();
    private readonly Queue<TelemetryEvent> _events = new Queue<TelemetryEvent>();
    private readonly int _capacity;

    internal int DroppedCount { get; private set; }
    internal int Count { get { lock (_lock) return _events.Count; } }

    internal TelemetryQueue(int capacity) { _capacity = Math.Max(1, capacity); }

    internal bool TryEnqueue(TelemetryEvent ev)
    {
        lock (_lock)
        {
            if (_events.Count < _capacity) { _events.Enqueue(ev); return true; }
            if (ev.Category == TelemetryCategory.Failure || ev.Category == TelemetryCategory.Health)
            {
                DropOneDroppableLocked();
                if (_events.Count < _capacity) { _events.Enqueue(ev); return true; }
            }
            DroppedCount++;
            return false;
        }
    }

    internal List<TelemetryEvent> Drain(int max)
    {
        var batch = new List<TelemetryEvent>();
        lock (_lock)
        {
            while (batch.Count < max && _events.Count > 0) batch.Add(_events.Dequeue());
        }
        return batch;
    }
}
```

- [ ] Implement `TelemetrySession` with path creation under `<gameRoot>/BepInEx/WhiskeyRealism/tuning-logs/<session-id>/`, retention sort by `manifest.startUtc`, directory name, then mtime, and newest-two retention including current session.

- [ ] Implement `TelemetryManifest` with plugin version, runtime assembly SHA-256, profile, config snapshot, start/end UTC, files, cap/rotation state, dropped counters, and unflushed count.

- [ ] Implement `TelemetryIssueBundle.Redact` and manifest creation. It must not copy DLLs, save files, tokens, unrelated BepInEx logs, or raw usernames into `issue-bundle.json`.

- [ ] Run tests and commit:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git add src/WhiskeyRealism/Telemetry tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add telemetry session budget and manifest"
```

## Task 3: Writer Thread, Router, Profiles, And Plugin Lifecycle

**Files:**
- Create: `src/WhiskeyRealism/Telemetry/TelemetryRouter.cs`
- Create: `src/WhiskeyRealism/Telemetry/TelemetryWriter.cs`
- Create: `src/WhiskeyRealism/Telemetry/TelemetryRuntime.cs`
- Modify: `src/WhiskeyRealism/Plugin.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] Add pure compile entries for router helpers that do not require BepInEx:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetryRouter.cs" Link="Telemetry\TelemetryRouter.cs" />
```

- [ ] Add profile tests:

```csharp
("telemetry profile off does not allocate session", TelemetryProfileOffDoesNotAllocateSession),
("telemetry profile parses unknown as off", TelemetryProfileParsesUnknownAsOff),
("telemetry behavior gates are independent from profile", TelemetryBehaviorGatesAreIndependentFromProfile),
```

- [ ] Implement `TelemetryRouter.ParseProfile(string value)` and `TelemetryRouter.ShouldEmit(profile, layer, category)` so `Off` rejects decision/state/trace/write/gate/performance rows and accepts only bounded health/failure rows.

- [ ] Modify `Plugin.cs` fields:

```csharp
internal ConfigEntry<string> TelemetryLoggingProfileRaw;
internal ConfigEntry<int> TelemetryMaxTuningLogMb;
internal ConfigEntry<int> TelemetryFileRotateMb;
internal ConfigEntry<int> TelemetryRetainedSessions;
internal ConfigEntry<bool> TelemetryEmitHumanSummary;
internal ConfigEntry<bool> TelemetryPerformanceWarnings;
internal ConfigEntry<bool> TelemetryCreateIssueBundleOnShutdown;
private TelemetryRuntime _telemetryRuntime;
```

- [ ] Bind them in the existing `[Telemetry]` section after `DirectorVerboseTrace`:

```csharp
TelemetryLoggingProfileRaw = Config.Bind("Telemetry", "Logging Profile", "Off", "Off, TacticalTuning, CampaignTuning, or FullTuning.");
TelemetryMaxTuningLogMb = Config.Bind("Telemetry", "Max Tuning Log MB", 250, new ConfigDescription("Maximum bytes across one tuning session before low-priority detail is cut.", new AcceptableValueRange<int>(25, 2000)));
TelemetryFileRotateMb = Config.Bind("Telemetry", "Tuning Log File Rotate MB", 25, new ConfigDescription("Approximate JSONL file size before rotation.", new AcceptableValueRange<int>(1, 250)));
TelemetryRetainedSessions = Config.Bind("Telemetry", "Tuning Log Retained Sessions", 2, new ConfigDescription("Newest tuning session directories retained.", new AcceptableValueRange<int>(1, 10)));
TelemetryEmitHumanSummary = Config.Bind("Telemetry", "Emit Human Summary", true, "Write summary.md on shutdown for tuning profiles.");
TelemetryPerformanceWarnings = Config.Bind("Telemetry", "Telemetry Performance Warnings", true, "Emit bounded main-log warnings for slow telemetry sinks and dropped rows.");
TelemetryCreateIssueBundleOnShutdown = Config.Bind("Telemetry", "Create Issue Bundle On Shutdown", false, "Create a redacted issue bundle manifest/archive in the session folder on clean shutdown.");
```

- [ ] Start telemetry before behavior patches register and shut it down cleanly:

```csharp
_telemetryRuntime = TelemetryRuntime.Start(new TelemetryRuntimeConfig(
    TelemetryLoggingProfileRaw.Value,
    TelemetryMaxTuningLogMb.Value,
    TelemetryFileRotateMb.Value,
    TelemetryRetainedSessions.Value,
    TelemetryEmitHumanSummary.Value,
    TelemetryPerformanceWarnings.Value,
    TelemetryCreateIssueBundleOnShutdown.Value,
    typeof(Plugin).Assembly.Location));
TelemetryRouter.Attach(_telemetryRuntime);
```

Add methods at the end of `Plugin`:

```csharp
private void OnDestroy()
{
    TelemetryRouter.Shutdown("plugin-destroy");
}

private void OnApplicationQuit()
{
    TelemetryRouter.Shutdown("application-quit");
}
```

- [ ] Implement `TelemetryWriter` as one background thread with flush every `250 ms` or `256` rows, append-only JSONL, timeout-bounded shutdown flush, and sink self-failure rows.

- [ ] Ensure `Logging Profile = Off` starts no writer thread and creates no session folder.

- [ ] Run tests, build, and commit:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
git add src/WhiskeyRealism/Telemetry src/WhiskeyRealism/Plugin.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: wire telemetry profiles and writer runtime"
```

## Task 4: Performance Scope Helpers And Failure Isolation

**Files:**
- Create: `src/WhiskeyRealism/Telemetry/TelemetryPerf.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Modify: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`

- [ ] Add compile entry:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetryPerf.cs" Link="Telemetry\TelemetryPerf.cs" />
```

- [ ] Add tests:

```csharp
("telemetry perf scope emits slow event", TelemetryPerfScopeEmitsSlowEvent),
("telemetry perf scope swallows sink failure", TelemetryPerfScopeSwallowsSinkFailure),
```

- [ ] Implement `TelemetryPerf.Scope(string scope, TelemetryLayer layer, TelemetryCategory category, double thresholdMs)` returning `IDisposable`. On dispose, it emits a `Performance` event with `scope`, `durationMs`, `slow`, `thresholdMs`, `queueDepth`, emitted/dropped counters, and layer/category fields.

- [ ] Instrument required scopes:

```csharp
using (TelemetryPerf.Scope("campaign.daily-review", TelemetryLayer.Campaign, TelemetryCategory.Performance, 4.0))
{
    // existing daily review body
}
```

```csharp
using (TelemetryPerf.Scope("tactical.observer-pass", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
{
    // existing tactical observer pass body
}
```

- [ ] Add comparable scopes for tactical orchestrator tick, command assignment, posture executor, coordinated operations, defense intent, fiscal, construction, JSON serialization, file write, flush, and summary generation.

- [ ] Run tests/build and commit:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
git add src/WhiskeyRealism/Telemetry src/WhiskeyRealism/Strategic/StrategicCoordinator.cs src/WhiskeyRealism/Patches/TacticalObserverPatch.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add telemetry performance scopes"
```

## Task 5: Legacy Tag Policy, Parser, And Main-Log Allowlist

**Files:**
- Create: `src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs`
- Create: `src/WhiskeyRealism/Telemetry/TelemetryLegacyParser.cs`
- Modify: `src/WhiskeyRealism/Util/OnceLog.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] Add compile entries:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetryTagPolicy.cs" Link="Telemetry\TelemetryTagPolicy.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetryLegacyParser.cs" Link="Telemetry\TelemetryLegacyParser.cs" />
```

- [ ] Add routing tests:

```csharp
("telemetry tag policy routes tactical matrix to state", TelemetryTagPolicyRoutesTacticalMatrixToState),
("telemetry tag policy keeps startup allowlisted", TelemetryTagPolicyKeepsStartupAllowlisted),
("telemetry legacy parser extracts key values", TelemetryLegacyParserExtractsKeyValues),
("telemetry legacy parser creates coarse signature", TelemetryLegacyParserCreatesCoarseSignature),
```

- [ ] Implement `TelemetryTagPolicy` with a closed map for the known tactical and campaign tags listed in the spec. Unknown `[Tactical...]`, `[DefenseIntent]`, `[Construction...]`, `[Fiscal...]`, `[DailyOps...]`, `[CoordinatedOps...]`, `[Director...]`, `[W&L...]`, `[Project...]`, `[Campaign...]`, `[FrontLedger]`, `[ArmyArea]`, `[FormationDirective]`, `[OperationalProbe]`, and `[HistoricalOperation]` tags route to sidecar by default unless they are on the main-log allowlist.

- [ ] Implement `TelemetryLegacyParser.Parse(string line, TelemetryProfile profile, string sessionId)`:

```csharp
internal static bool TryParse(string line, TelemetryProfile profile, string sessionId, out TelemetryEvent ev)
{
    ev = null;
    var route = TelemetryTagPolicy.Route(line);
    if (!route.RouteToSidecar) return false;
    var fields = ParseKeyValues(line);
    ev = TelemetryEvent.Create(sessionId, profile, route.Layer, route.Category, route.EventName, route.Severity);
    foreach (var pair in fields) ev.WithField(pair.Key, pair.Value);
    if (route.Category == TelemetryCategory.Decision)
        ev.WithDecision(Get(fields, "decision", route.EventName), Get(fields, "reason", "legacy"), BuildInputSignature(route.EventName, fields));
    else
        ev.WithField("inputSignature", BuildInputSignature(route.EventName, fields)).WithField("inputSignatureSource", "coarse");
    return true;
}
```

- [ ] Modify `OnceLog.Info` and `OnceLog.Warning` so once-logged tuning tags still route through `TelemetryRouter.LegacyInfo` / `TelemetryRouter.LegacyWarning`. If profile is `Off` and the tag is sidecar-only, `OnceLog` must emit only the single compatibility warning/counter, not the original high-volume line.

- [ ] Run tests and commit:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git add src/WhiskeyRealism/Telemetry src/WhiskeyRealism/Util/OnceLog.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: route legacy telemetry tags"
```

## Task 6: Tactical Migration

**Files:**
- Modify tactical patch files listed in the File Map
- Modify tactical formatter files only where typed input signatures are already available

- [ ] Replace direct high-volume tactical info writes with `TelemetryRouter.LegacyInfo(line, TelemetryLayer.Tactical)` or typed `TelemetryRouter.Emit(...)`. Examples:

```csharp
TelemetryRouter.LegacyInfo("[TacticalDecisionMatrix] event=" + eventType + " side=" + side + " ...", TelemetryLayer.Tactical);
```

```csharp
TelemetryRouter.Emit(
    TelemetryEvent.Create(TelemetryRouter.SessionId, TelemetryRouter.Profile, TelemetryLayer.Tactical, TelemetryCategory.Decision, "CommandAssignment", TelemetrySeverity.Info)
        .WithSide(side)
        .WithDecision(order.Task.ToString(), order.Reason, TacticalOperationsTelemetry.CommandAssignmentSignature(side, state, operation, order))
        .WithField("node", state.NodeId)
        .WithField("role", state.Role.ToString())
        .WithField("writeResult", "not-attempted"));
```

- [ ] Migrate these tactical groups in one pass:

```text
TacticalDecisionMatrix, TacticalPlayerOrder, TacticalOrder, TacticalFormationChange,
TacticalCommandAssignment, TacticalCommandPosture, TacticalPostureSummary,
TacticalOpsLedger, TacticalCommandTree, TacticalCommanderRoster, TacticalPlan,
TacticalPlaybook, TacticalIntent, TacticalLocalReaction, TacticalReplan,
TacticalCascade, TacticalMacro, TacticalGroup, TacticalSector, TacticalOdds,
TacticalPathShape, TacticalWaypointDrift, TacticalCourierQueue,
TacticalReserve*, TacticalCharge*, TacticalFeud*, TacticalDirectChild*,
TacticalDeploymentPhase, TacticalRegimentDiagnostics, TacticalRegimentTrace,
TacticalObjective*, TacticalOrchestrator, TacticalPathfinderDiscipline,
TacticalHqLinkGuard, TacticalDiagnostic
```

- [ ] Preserve behavior gates exactly. Logging profile must not change `TacticalCommanderMode`, orchestrator gates, W&L behavior gates, guard gates, or tactical writer gates.

- [ ] Add one typed decision path each for command assignment, posture execution, reserve commit gate, charge gate, W&L player order, and decision matrix. Legacy parser is acceptable for the remaining sidecar-routed tactical diagnostic rows in this implementation, but every decision row must include `inputSignature`.

- [ ] Run closure checks:

```bash
rg -n "Plugin\.Log\.LogInfo|OnceLog\.Info" src/WhiskeyRealism/Patches src/WhiskeyRealism/Tactical | tee /tmp/wr-tactical-log-sites-after.txt
```

Expected: remaining direct `LogInfo` calls are main-log allowlisted startup/serious-warning style messages or are converted before commit.

- [ ] Run tests/build and commit:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
git add src/WhiskeyRealism/Patches src/WhiskeyRealism/Tactical src/WhiskeyRealism/Telemetry
git commit -m "feat: migrate tactical tuning logs to sidecars"
```

## Task 7: Campaign, W&L, And Strategic Migration

**Files:**
- Modify campaign/strategic files listed in the File Map

- [ ] Replace direct campaign tuning logs with telemetry routing. Use typed events for campaign objective/historical operation, defense intent, fiscal intent, construction intent, coordinated operation, operational probe, director trace, W&L camp, and project doctrine rows.

- [ ] Example typed campaign decision:

```csharp
TelemetryRouter.Emit(
    TelemetryEvent.Create(TelemetryRouter.SessionId, TelemetryRouter.Profile, TelemetryLayer.Campaign, TelemetryCategory.Decision, "HistoricalOperation", TelemetrySeverity.Info)
        .WithAlliance(AllianceId)
        .WithCampaignDate(year + "-" + month.ToString("D2") + "-" + day.ToString("D2"))
        .WithDecision("select", bestMatch.Reason, "alliance=" + AllianceId + "|objective=" + objectiveId + "|operation=" + bestMatch.Profile.OperationId + "|score=" + Math.Round(bestMatch.Score * 2.0) / 2.0)
        .WithField("operation", bestMatch.Profile.OperationId)
        .WithField("objective", objectiveId)
        .WithField("score", bestMatch.Score));
```

- [ ] Migrate these campaign groups in one pass:

```text
Heartbeat, DailyOps, DailyOps:Perf, HistoricalOperation, DefenseIntent,
DefenseIntent:asset, FiscalIntent, FiscalTelemetry, ConstructionIntent,
ConstructionTelemetry, FormationDirective, FrontLedger, ArmyArea, CampaignMap,
CampaignPace, CollapseRisk, Director, Director:trace, OperationalProbe,
CoordinatedOps, CoordinatedOps:Perf, ProjectDoctrine, ProjectAppointed,
ProjectUnlock, W&LStartSelection, W&LCamp, Succession, Plan
```

- [ ] Preserve behavior gates exactly. `Logging Profile = Off` must not affect strategic brain enablement, construction steering, defense ledger, historical operation doctrine, performance governors, W&L camp realism, settings locks, or `ForceAllSuccessionEvents`.

- [ ] Run closure:

```bash
rg -n "Plugin\.Log\.LogInfo|OnceLog\.Info" src/WhiskeyRealism/Strategic src/WhiskeyRealism/Patches | tee /tmp/wr-campaign-log-sites-after.txt
```

Expected: remaining direct info logs are allowlisted startup/sidecar/save-load/serious warning messages, or they are converted before commit.

- [ ] Run tests/build and commit:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
git add src/WhiskeyRealism/Strategic src/WhiskeyRealism/Patches src/WhiskeyRealism/Telemetry
git commit -m "feat: migrate campaign tuning logs to sidecars"
```

## Task 8: Summary, Session Validator, And Issue Bundle Output

**Files:**
- Create: `src/WhiskeyRealism/Telemetry/TelemetrySummary.cs`
- Create: `src/WhiskeyRealism/Telemetry/TelemetrySessionValidator.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] Add compile entries:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetrySummary.cs" Link="Telemetry\TelemetrySummary.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Telemetry\TelemetrySessionValidator.cs" Link="Telemetry\TelemetrySessionValidator.cs" />
```

- [ ] Add tests:

```csharp
("telemetry summary includes drops failures and slow scopes", TelemetrySummaryIncludesDropsFailuresAndSlowScopes),
("telemetry validator tolerates partial final line", TelemetryValidatorToleratesPartialFinalLine),
("telemetry issue bundle writes redacted manifest", TelemetryIssueBundleWritesRedactedManifest),
```

- [ ] Implement `TelemetrySummary` aggregation fields: session id, profile, start/end, plugin version, runtime assembly hash, campaign dates, battles, counts by layer/category/event, top decisions/reasons, denied gates, write results, slowest scopes, queue drops, cap transitions, repeated failures, missing anchors, and recommended inspection queries.

- [ ] Implement `TelemetrySessionValidator.ValidateDirectory(path)` so runtime smoke can verify JSONL without choking on a partial final line. It must report invalid JSON rows with file and line number.

- [ ] Change test harness `Main` signature to accept validation mode:

```csharp
static int Main(string[] args)
{
    if (args != null && args.Length == 2 && args[0] == "--validate-telemetry")
    {
        var result = TelemetrySessionValidator.ValidateDirectory(args[1]);
        Console.WriteLine(result.Summary);
        return result.Success ? 0 : 1;
    }

    var tests = new (string name, Action run)[]
    {
        // existing tests
    };
}
```

- [ ] Run tests and commit:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git add src/WhiskeyRealism/Telemetry tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add telemetry summary and validator"
```

## Task 9: Runtime Smoke Controls And Documentation

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`
- Create: `docs/telemetry.md`
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`
- Modify: `MEMORY.md`

- [x] Add smoke-only config controls under `[Telemetry]` if they are not already represented by production config:

```ini
Telemetry Queue Capacity = 8192
Telemetry Flush Milliseconds = 250
Telemetry Flush Rows = 256
```

Use bounded `AcceptableValueRange` values in `Plugin.cs`. `Telemetry Queue Capacity` is the detail queue capacity; protected health/failure rows have a separate reserve and can temporarily exceed the detail cap.

- [x] Write `docs/telemetry.md` with:

```markdown
# Whiskey Realism Telemetry

Default profile is `Off`; customer installs should not create tuning sidecars.
Tuning profiles write under `<GTCW>/BepInEx/WhiskeyRealism/tuning-logs/<session-id>/`.

Profiles:
- `Off`
- `TacticalTuning`
- `CampaignTuning`
- `FullTuning`

Retained sessions: newest two.
Session cap: 250 MB.
JSONL rotation: about 25 MB per file.

Validation:
`dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj -- --validate-telemetry "<session-dir>"`

Issue bundles contain redacted telemetry files only. They do not contain save files, copied game DLLs, tokens, unrelated plugin logs, or raw Windows usernames.
```

- [x] Update `docs/patch-catalog.md` for telemetry framework ownership and migrated log policy.

- [x] Update `docs/handoff.md` with current profile defaults, smoke checklist, and runtime evidence boundary. No deployed DLL hash was added because Task 10 owns deploy/hash verification.

- [x] Update `MEMORY.md` with durable telemetry routing/default facts and the Task 10 pending boundary. No runtime smoke facts were claimed.

- [x] Run documentation scan:

```bash
rg -n "LogOutput\.log|TacticalDecisionMatrix|Telemetry|tuning-logs" docs README.md MEMORY.md
```

Expected: docs explain that tuning rows route to sidecars and customer profile is quiet by default.

- [x] Verification evidence for Task 9:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
git diff --check
rg -n "LogOutput\.log|TacticalDecisionMatrix|Telemetry|tuning-logs" docs README.md MEMORY.md
```

Results: console harness exited 0; `./build.sh` exited 0 with 0 warnings / 0 errors; `git diff --check` exited 0; docs scan exited 0 and includes the new sidecar/default-off routing references in `docs/telemetry.md`, `docs/patch-catalog.md`, `docs/handoff.md`, and `MEMORY.md`.

- [x] Commit:

```bash
git commit -m "feat: add telemetry runtime controls and docs"
```

Committed as `c8db29a feat: add telemetry runtime controls and docs`.

## Task 10: Final Verification, Deploy, And Runtime Smoke

**Files:**
- Build output: `dist/WhiskeyRealism.dll`
- Deployed output: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll`
- Runtime outputs: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/WhiskeyRealism/tuning-logs/`

- [x] Run full harness:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: exits `0`.

- [x] Run build:

```bash
./build.sh
```

Expected: `dist/WhiskeyRealism.dll` exists and build exits `0`.

- [x] Deploy with game closed:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

If Windows holds the DLL lock, close GTCW and rerun the same command.

- [x] Verify local/deployed DLL identity:

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: sizes and SHA-256 hashes match.

Task 10 deploy/hash evidence from controller closeout on 2026-05-15:

- Task 9 commit: `c8db29a feat: add telemetry runtime controls and docs`.
- Full harness: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` exited `0`; only existing CS0649 warnings were reported.
- Build: `./build.sh` exited `0` with `0 Warning(s)` and `0 Error(s)` and produced `dist/WhiskeyRealism.dll`.
- Deploy: `cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"` succeeded.
- `stat`: `dist/WhiskeyRealism.dll` was `2026-05-15 19:39:43.472409696 -0500`, `1245184` bytes; deployed plugin was `2026-05-15 19:39:59.237663600 -0500`, `1245184` bytes.
- `sha256sum`: both local and deployed DLLs matched `f1ace5cf26567cd018b35fb1bdd5987c3869232a49a209287717a6770db91866`.

Runtime smoke boundary:

- Current `BepInEx/LogOutput.log` is stale for this deploy: `2026-05-15 07:54:28.457939800 -0500`, `133252053` bytes, which predates the `19:39` deployed DLL.
- No `BepInEx/WhiskeyRealism/tuning-logs/<session-id>/` directory/session was present in the read-only check.
- Current config has a `[Telemetry]` section but only the old `Director Verbose Trace`; the new telemetry config entries will not be proven until the freshly deployed plugin is launched.
- Therefore `Off`, `TacticalTuning`, `CampaignTuning`, `FullTuning`, telemetry validator on a real session, cap-transition smoke, and production-cap restore remain pending and require a fresh GTCW launch after deploy.

- [ ] `Off` smoke:

```ini
[Telemetry]
Logging Profile = Off
```

Launch GTCW, start or load a campaign, then inspect:

```bash
rg -n "\[TacticalDecisionMatrix\]|\[TacticalPlayerOrder\]|\[DailyOps\]|\[DefenseIntent\]" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
find "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/WhiskeyRealism/tuning-logs" -maxdepth 1 -type d
```

Expected: no tactical/campaign tuning firehose in `LogOutput.log`; no new tuning session directory for `Off`.

- [ ] `TacticalTuning` smoke:

```ini
[Telemetry]
Logging Profile = TacticalTuning
```

Start a battle. Confirm `health.jsonl`, `performance.jsonl`, `tactical.jsonl`, `failures.jsonl`, `manifest.json`, and `summary.md` exist. Validate:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj -- --validate-telemetry "<session-dir>"
```

Expected: validator exits `0`; tactical decision/gate/write rows have `inputSignature`.

- [ ] `CampaignTuning` smoke:

```ini
[Telemetry]
Logging Profile = CampaignTuning
```

Advance campaign cadence. Confirm campaign rows exist, tactical rows stay quiet unless a cross-layer failure occurs, and `summary.md` lists campaign decisions and performance.

- [ ] `FullTuning` budget-path smoke:

Set temporary smoke values:

```ini
[Telemetry]
Logging Profile = FullTuning
Max Tuning Log MB = 5
Tuning Log File Rotate MB = 1
```

Run enough campaign/battle activity to trigger at least one rotation and one cap transition. Confirm main-log warnings are bounded, protected failure/performance summaries survive, and validation passes.

- [ ] Restore production tuning caps after smoke:

```ini
[Telemetry]
Max Tuning Log MB = 250
Tuning Log File Rotate MB = 25
Tuning Log Retained Sessions = 2
```

- [x] Final repo checks:

```bash
git status --short --branch
git log --oneline -10
rg -n "T[B]D|T[O]DO|FIXM[E]|placehold[e]r|implement late[r]" docs/superpowers/plans/2026-05-15-full-telemetry-framework-implementation-plan.md docs/superpowers/specs/2026-05-15-full-telemetry-framework-design.md docs/telemetry.md
git diff --check
```

Expected: no unresolved markers, no whitespace errors, only intentional changes staged/committed.

Final repo-check evidence before closeout commit:

- `git status --short --branch` showed branch `telemetry-framework-plan` with only closeout doc edits pending.
- `git log --oneline -10` showed Task 6-9 telemetry commits through `c8db29a`.
- Unresolved-marker scan returned no matches.
- `git diff --check` exited `0`.

## Rollback And Scope Boundary

- Runtime rollback: set `[Telemetry] Logging Profile = Off`. This stops sidecar creation and leaves behavior gates untouched.
- Deployment rollback: redeploy the last known-good `WhiskeyRealism.dll` and verify SHA-256 against the prior handoff hash.
- Behavior rollback is not tied to telemetry profile. Tactical commander mode, W&L gates, construction/defense ledgers, and performance governors keep their own config ownership.
- Every core element in the spec ships in this implementation session: profile config, writer thread, sidecars, queue/budget/rotation/retention, summaries, performance telemetry, failure visibility, issue bundle manifest, validator, tactical migration, and campaign migration.

## Plan Self-Review

- Spec coverage: Tasks 1-3 cover schema, profiles, writer, durability, session, manifest, retention, cap/rotation; Tasks 4-7 cover performance and migration; Task 8 covers summary, validator, and issue bundle; Task 10 covers runtime smoke, cap transition, deploy, and hash verification.
- Behavior-gate separation: Tasks 3, 6, and 7 explicitly preserve behavior gates independent of telemetry profile.
- Read-only patch carve-out: telemetry state is isolated under `src/WhiskeyRealism/Telemetry/` and does not feed strategic or tactical doctrine decisions.
- No scope shrink: all full-framework items are included in this plan and none are moved outside the implementation session.
