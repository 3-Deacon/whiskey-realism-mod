---
name: whiskey-vanilla-anchor-review
description: Use when reviewing or planning Whiskey Realism changes against Grand Tactician vanilla code, especially requests mentioning decompile anchors, vanilla confirmation, Harmony patch surfaces, battle AI, campaign AI, or "confirm in vanilla code".
---

# Whiskey Vanilla Anchor Review

Use this skill to verify claims against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

## Workflow

1. Check `AGENTS.md`, `docs/handoff.md`, and `docs/patch-catalog.md` for current ownership.
2. Locate the vanilla method with `rg`, then read the full method body with surrounding context.
3. Record owner type, method name, overload/signature shape, line anchor, and side effects.
4. Search nearby call sites and guards, especially `PerformAIActionDLCWL`, `SetWaypoint`, order-delay flags, and list mutations.
5. Classify each claim as confirmed, partial, not found, or Whiskey doctrine.
6. Push back on any spec or plan that treats an unverified inference as confirmed vanilla behavior.

## Output

- Findings first.
- Include exact file/line anchors.
- State what was not verified.
- Recommend the narrowest patch surface only after evidence supports it.
