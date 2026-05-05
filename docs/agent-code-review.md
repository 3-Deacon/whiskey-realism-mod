# Agent Code Review Checklist

Use this checklist for Whiskey Realism implementation reviews and adversarial spec reviews.

## Findings First

- Lead with bugs, regressions, unsafe assumptions, and missing verification.
- Cite exact files and line numbers when reviewing repo code.
- For vanilla behavior, cite `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` method anchors.
- Separate confirmed evidence from inference.

## Vanilla And Patch Safety

- Was the current vanilla method body re-read before patching?
- Is the patch on the narrowest stable surface?
- Does the patch preserve vanilla side effects, timers, list ownership, and return behavior?
- Are W&L/player-control gates checked where relevant?
- Are `PerformAIActionDLCWL` call sites checked before adding a new gate?
- Are alliance indexes bound-checked, including alliance `2`?
- Does any Prefix snapshot/restore mutated vanilla collections?
- Is logging bounded by `OnceLog`, signature change, or explicit config?

## Mod Architecture

- Does pure logic stay free of Unity/vanilla runtime references?
- Are strategic state writes limited to coordinator cadence/event handlers?
- Are Harmony patches read-only against mod strategic state?
- Does the change follow existing catalog, ledger, runtime, and patch patterns?
- Is the source-of-truth order respected when docs disagree?

## Verification

- For pure logic, did the console harness run?
- For DLL-affecting changes, did `./build.sh` run?
- Was the DLL deployed to the BepInEx plugins folder?
- Do `stat` and `sha256sum` prove deployed DLL equals `dist/WhiskeyRealism.dll`?
- Was runtime smoke performed or explicitly left for the user with exact log markers?

## Documentation

- Was `docs/patch-catalog.md` updated for new shipped patches?
- Was `docs/handoff.md` updated for current state and deployed hash?
- Were specs/plans archived only after smoke-verified ship?
- Are "not verified" claims still labeled as not verified?
