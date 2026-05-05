# Test Harness Instructions

These rules apply to `tests/WhiskeyRealism.Tests/`.

## Project Shape

- The test project uses explicit `<Compile Include>` entries. It does not glob source files.
- When adding a new strategic/tactical source file used by tests, add a matching `<Compile Include="..\..\src\WhiskeyRealism\...">` entry to `WhiskeyRealism.Tests.csproj`.
- When deleting or moving a source file, remove or update the corresponding compile entry.

## Test Style

- Use the existing console harness style in `Program.cs`.
- Prefer focused fixture tests for pure ledger/scorer behavior.
- Name tests after the behavior they protect, not the implementation detail.
- Cover regression cases from decompile or smoke-test findings.

## Verification

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

For DLL-affecting changes, the test harness is not enough. Also build, deploy, and verify the DLL hash per root `AGENTS.md`.
