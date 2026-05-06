# Bug Fix Instructions

These rules apply to `docs/bug-fixes/`.

## Scope

- Use this area for confirmed or strongly suspected vanilla bugs, shipped runtime hazards, and narrow Whiskey guards for vanilla failure modes.
- Do not use this area for broad realism design, new doctrine, or feature slices. Those still belong under `docs/superpowers/specs/` and `docs/superpowers/plans/`.
- A bug-fix entry needs a vanilla/decompile anchor, runtime evidence, or an explicit `Needs repro` status.

## Entry Rules

- Lead with the failure mode and evidence.
- Separate confirmed vanilla behavior from Whiskey doctrine or inferred design gaps.
- Include exact method anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` when available.
- Mark shipped fixes by patch ordinal once they land in `docs/patch-catalog.md`.
- Do not call a fix shipped until the DLL is built, deployed, hash-verified, and smoke-tested when the change affects runtime behavior.

## Status Labels

- `Shipped` — patch/runtime guard is cataloged and smoke-verified.
- `In progress` — local code exists but is not fully verified/deployed.
- `Confirmed` — runtime/decompile evidence proves the bug, but no fix has shipped.
- `Backlog` — likely narrow bug or vanilla AI flaw, but implementation still needs bounded design.
- `Needs repro` — code hazard or prior log note exists, but exact current runtime proof is missing.
