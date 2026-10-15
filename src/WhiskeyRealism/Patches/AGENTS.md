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
- Catch reflection failures and log one bounded warning. Never throw from a hot patch.
- Use `OnceLog` or signature-gated logging for first-fire and behavioral proof.
- Preserve vanilla side effects unless the spec explicitly says to replace them.
- Bound-check alliance indexes. `AICampaign.aifaction` can include alliance `2` for Europe.
- Do not mutate strategic mod state from a patch. Patches may read ledgers and steer vanilla behavior only.
- If a Prefix filters or temporarily rewrites vanilla state, restore it in a `[HarmonyFinalizer]` so the restore runs even when vanilla throws.
- Battle writes: check `TacticalWriteGuard.CanWrite(unit)` (and skip the W&L feud instance for battle-level writes); write `ai_stance` only through `Util/TacticalStanceWriter`; only #44 writes `macroai`. Under `Tactical Commander Mode` `MonitorOnly`/`Off` nothing is written.
- Battle doctrine timers use `TacticalDoctrineTime` (battle game-hours), never wall-clock time.
- Per-battle / per-campaign static state in a patch registers its reset with `BattleScope.Register` / `CampaignScope.Register` (`Util/ScopedState.cs`) from the patch's static constructor. OnceLog keys are static strings (no instance ids).
- Every config flag defaults on and is a working rollback; match `Plugin.cs` descriptions to the code.

## Required Verification

- For DLL-affecting changes, run `./build.sh`.
- Deploy `dist/WhiskeyRealism.dll` to the BepInEx plugins folder.
- Verify deployed and dist DLLs by timestamp/size and `sha256sum`.
- Do not report runtime readiness from build output alone.
- Run `python3 -I tools/config-audit.py .` and `python3 -I tools/orphan-sweep.py .` (both must report 0 findings) and update `docs/patch-catalog.md` for any added, removed or re-owned patch.
