---
name: whiskey-dll-deploy-smoke
description: Use after any Whiskey Realism DLL-affecting code change, deploy request, smoke-test request, or claim that a built plugin is ready for in-game testing.
---

# Whiskey DLL Deploy Smoke

Use this skill for build/deploy/hash proof and smoke boundaries.

## Workflow

1. Run the focused test harness when pure logic changed:
   `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
2. Run `./build.sh`.
3. Copy `dist/WhiskeyRealism.dll` to the GTCW BepInEx plugins folder.
4. Run `stat` on both DLLs.
5. Run `sha256sum` on both DLLs and confirm the hashes match.
6. If deploy fails with `Invalid argument`, the game is probably running. Tell the user to close it and redeploy.
7. For runtime smoke, inspect `BepInEx/LogOutput.log` for first-fire markers, bounded behavior logs, and warnings/errors.

## Rule

Do not report implementation readiness from build output alone. Deployed hash proof is required for DLL-affecting work.
