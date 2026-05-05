---
name: whiskey-spec-adversarial-review
description: Use when the user asks for adversarial review, deep dive, spec review, plan review, tactical AI review, or asks whether a Whiskey Realism design is grounded in shipped code and vanilla anchors.
---

# Whiskey Spec Adversarial Review

Use this skill for findings-first review of specs and plans.

## Workflow

1. Read the named spec/plan plus referenced docs.
2. Check shipped code and `docs/patch-catalog.md` before trusting prose.
3. Verify vanilla anchors in `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.
4. Separate shipped behavior, planned behavior, and unverified inference.
5. Look for wrong method ownership, missing side effects, unsafe Prefixes, stale line anchors, unsupported runtime assumptions, and missing tests/smoke gates.
6. Recommend approve, change-required, or defer.

## Output

- Findings first, ordered by severity.
- Include exact file/line references.
- Keep summary secondary.
- Include "not verified" items when runtime proof is still needed.
