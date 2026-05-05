# Specs And Plans Instructions

These rules apply to `docs/superpowers/`.

## Source Of Truth

- Shipped code beats `docs/patch-catalog.md`.
- `docs/patch-catalog.md` beats per-patch design docs.
- Per-patch design docs beat umbrella specs.
- Umbrella specs beat archived plans.
- If docs disagree with shipped code, update the living docs or flag the drift before planning new code.

## Specs

- Specs describe design and boundaries. They are not implementation plans.
- Keep confirmed vanilla behavior separate from Whiskey doctrine.
- For decompile-backed specs, include exact method anchors and a "not verified" section for runtime-only claims.
- Update active specs when adversarial review finds material omissions.
- Do not mutate archived specs after ship; record deltas in living docs instead.

## Plans

- Plans are execution artifacts. Split large slices into bounded plans.
- Each plan must state patch surfaces, verification commands, smoke expectations, and rollback/defer boundaries.
- Do not implement from an umbrella spec alone when the work spans multiple patch surfaces.

## Reviews

- Lead with findings, not summary.
- Verify referenced methods, docs, and shipped code before approving a spec or plan.
- Use `docs/agent-code-review.md` as the checklist for implementation review.
