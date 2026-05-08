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
- Catch reflection failures and log one bounded warning. Never throw from a hot patch.
- Use `OnceLog` or signature-gated logging for first-fire and behavioral proof.
- Preserve vanilla side effects unless the spec explicitly says to replace them.
- Bound-check alliance indexes. `AICampaign.aifaction` can include alliance `2` for Europe.
- Do not mutate strategic mod state from a patch. Patches may read ledgers and steer vanilla behavior only.
- If a Prefix filters a vanilla list, snapshot and restore it in Postfix even on failure.

## Required Verification

- For DLL-affecting changes, run `./build.sh`.
- Deploy `dist/WhiskeyRealism.dll` to the BepInEx plugins folder.
- Verify deployed and dist DLLs by timestamp/size and `sha256sum`.
- Do not report runtime readiness from build output alone.
