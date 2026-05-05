---
name: whiskey-vanilla-anchor-review
description: Use when reviewing or planning Whiskey Realism changes against Grand Tactician vanilla code, especially requests mentioning decompile anchors, vanilla confirmation, method ownership, overload signatures, Harmony patch surfaces, battle AI, campaign AI, or "confirm in vanilla code".
---

# Whiskey Vanilla Anchor Review

Use this skill to verify claims against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

## When NOT to use

- Spec or plan-level critique → use `whiskey-spec-adversarial-review`.
- Build/deploy/runtime smoke evidence → use `whiskey-dll-deploy-smoke`.

## Workflow

1. If `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` is missing, regenerate per `docs/findings.md` §Decompilation before reporting "not found".
2. Check `AGENTS.md`, `docs/handoff.md`, and `docs/patch-catalog.md` for current ownership.
3. Locate the vanilla method with `rg`, then read the full method body with surrounding context.
4. Record owner type, method name, overload/signature shape, line anchor, and side effects.
5. Search nearby call sites and common guard patterns: action-router dispatchers (e.g. `PerformAIActionDLCWL`), waypoint setters (e.g. `SetWaypoint`), order-delay flags, and list mutations.
6. Classify each claim as confirmed, partial, not found, or Whiskey doctrine.
7. Push back on any spec or plan that treats an unverified inference as confirmed vanilla behavior.

## Output

- Findings first.
- Include exact file/line anchors.
- State what was not verified.
- Recommend the narrowest patch surface only after evidence supports it.
