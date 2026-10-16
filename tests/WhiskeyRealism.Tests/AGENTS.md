# Test Harness Instructions

These rules apply to `tests/WhiskeyRealism.Tests/`.

## Project Shape

- Source files under `src/` are pulled in with explicit `<Compile Include>` entries; the csproj does not glob `src/`.
- When adding a new strategic/tactical source file used by tests, add a matching `<Compile Include="..\..\src\WhiskeyRealism\...">` entry to `WhiskeyRealism.Tests.csproj`.
- When deleting or moving a source file, remove or update the corresponding compile entry.
- Test files in this directory are compiled by the SDK's default glob. A new `*.cs` file here needs no csproj entry.

## Adding Tests

There is no registration list. `Program.Main` discovers every top-level `static class` whose name ends in `Tests` and runs its `public static void Run()`, in ordinal order of the class name. A class named `*Tests` without that `Run()` fails the run.

- Add the test to the per-area class that owns the behavior (`StrategicDefenseTests`, `TacticalDoctrineTests`, `TelemetrySessionTests`, ...), or create a new `<Area>Tests.cs` with an `internal static class <Area>Tests` and a `Run()`.
- Two `Run()` styles are in use; follow the file you are editing:
  - a `(name, Action)[]` list passed to `TestHarness.RunAll`, which prints `PASS <name>` after each test;
  - direct calls to private test methods that print their own `Console.WriteLine("PASS ...")`.
- Every test prints exactly one `PASS <name>` line. Names are unique across the harness.
- Do not name a helper or fixture class `*Tests`.
- Tests must not depend on run order. Classes run in name order and tests within a class run in list order; if a test reads static state (`OnceLog`, `Plugin.Log` counters, `TelemetryRouter`, scope registries), reset it at the start of that test.

## Shared Helpers

- Assertions (`AssertEqual`, `AssertTrue`, `AssertFalse`, `AssertNear`, `AssertContains`, `AssertFinite`, `AssertThrows`) and `RunAll` live in `TestHarness.cs`.
- Builders and fixtures used by more than one test class live in `TestFixtures.cs`. A helper used by a single class stays private in that class.
- Per-area files import both with `using static TestHarness;` and `using static TestFixtures;`. Do not add a second copy of a helper in a test class.
- `TestPluginLogStub.cs` stands in for `Plugin` (log + the config entries pure code reads).
- Allocation tests measure with `TestFixtures.BeginAllocationWindow()` / `AllocatedSince(mark)`, never with `GC.GetAllocatedBytesForCurrentThread()` directly: a background GC ending its mark phase adds up to ~8 KB of phantom bytes to the raw counter (see the helper's comment). `AllocationMeasurementTests` fails any test file that reads the raw counter.

## Test Style

- Prefer focused fixture tests for pure ledger/scorer behavior.
- Name tests after the behavior they protect, not the implementation detail.
- Cover regression cases from decompile or smoke-test findings.

## Verification

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

A failing assertion throws out of the harness; the process exits non-zero. `--validate-telemetry <session-dir>` validates a tuning-log session instead of running tests.

For DLL-affecting changes, the test harness is not enough. Also build, deploy, and verify the DLL hash per root `AGENTS.md`.
