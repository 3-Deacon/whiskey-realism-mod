# Patch Instructions

These rules apply to Harmony patches under `src/WhiskeyRealism/Patches/`.

## Before Patching

- Re-read the current vanilla method body in `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.
- Confirm the exact owner type, method name, overload shape, and side effects.
- Check `docs/patch-catalog.md` for existing patch ownership before adding another patch on the same surface.
- Prefer Postfix. Use Prefix only for explicit gates, snapshot/filter/restore, or cases where vanilla exposes no safe Postfix surface.
- Do not use Transpilers without explicit user approval.

## Patch Rules

- One concern per patch file.
- Add a short header comment naming the vanilla method and what the patch changes.
- Wrap every static Prefix/Postfix/Finalizer body in `TelemetryPerf.Scope("<layer>.patch.<short-name>", TelemetryLayer.<Layer>, TelemetryCategory.Performance, 2.0) { ... }` per root `AGENTS.md` § "Diagnostic scope on every patch entry" (non-negotiable per 2026-05-19 user direction). Use threshold 5.0ms for known-heavy paths. Transpilers exempt (cost is JIT-emit, not per-call).
- First statement inside that scope: `PatchFirstFire.Mark("<same scope id>");` (a string literal). It logs one `[first-fire] <id>` line per session; `python3 -I tools/smoke-check.py <LogOutput.log>` reports every declared id that never fired. A patch that a normal campaign + battle session may not reach goes in `tools/smoke-situational.txt` with the reason.
- Catch reflection failures and log one bounded warning. Never throw from a hot patch.
- Use `OnceLog` or signature-gated logging for first-fire and behavioral proof.
- Preserve vanilla side effects unless the spec explicitly says to replace them.
- Bound-check alliance indexes. `AICampaign.aifaction` can include alliance `2` for Europe.
- Do not mutate strategic mod state from a patch. Patches may read ledgers and steer vanilla behavior only. The one exception is W&L order bookkeeping (#62 shadow, #69 pending moves and dispatch history, player-compliance observations); see root `AGENTS.md` § Patch hygiene.
- If a Prefix filters or temporarily rewrites vanilla state, restore it in a `[HarmonyFinalizer]` so the restore runs even when vanilla throws.
- Battle writes: a patch does not call vanilla movement/stance/formation/facing/combat-behavior writers itself. It either steers vanilla's own inputs, or builds a `BattleOrderIntent` and runs it through `OrderExecutorRuntime.TryPlanInline` / `ExecutePlannedInline` (#42, #56); new Whiskey movers are battle-core producers, not patches. Any remaining direct write (#59 rollback, #60 deployment, #42's no-snapshot fallback) checks `TacticalWriteGuard.CanWrite(unit)` (and skips the W&L feud instance for battle-level writes). Write `ai_stance` only through `Util/TacticalStanceWriter`; only #44 writes `macroai`. Under `Tactical Commander Mode` `MonitorOnly`/`Off` nothing is written.
- Vanilla battle bug fixes (BC13: #119 stance cooldown re-stamp, #122 macro assault upgrade, #123 artillery chain side, #124 reserve support pick) run in every Commander Mode behind their own flags. The three Transpilers (#122-#124) are narrow, count-checked IL rewrites with a pure rewriter in `Tactical/Battle/BugFixes/*Il.cs`: copy with `new CodeInstruction(ci)`, match exactly one site or return vanilla's own list with one OnceLog, a live hook that reads its flag on every call, a `TranspilerLabelTests` entry, no `TelemetryPerf` scope. `CavalryStrikeDisciplinePatch` (#120) and `SeparatedEnemyOwnershipPatch` (#121) are steers that serve Whiskey's command and run only in Active.
- Battle doctrine timers use `TacticalDoctrineTime` (battle game-hours), never wall-clock time.
- Per-battle / per-campaign static state in a patch registers its reset with `BattleScope.Register` / `CampaignScope.Register` (`Util/ScopedState.cs`) from the patch's static constructor. OnceLog keys are static strings (no instance ids).
- Every config flag defaults on and is a working rollback; match `Plugin.cs` descriptions to the code.

## Required Verification

- For DLL-affecting changes, run `./build.sh`.
- Deploy `dist/WhiskeyRealism.dll` to the BepInEx plugins folder.
- Verify deployed and dist DLLs by timestamp/size and `sha256sum`.
- Do not report runtime readiness from build output alone.
- Run `python3 -I tools/config-audit.py .` and `python3 -I tools/orphan-sweep.py .` (both must report 0 findings) and update `docs/patch-catalog.md` for any added, removed or re-owned patch.
