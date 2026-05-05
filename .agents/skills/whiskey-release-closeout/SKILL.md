---
name: whiskey-release-closeout
description: Use when closing out a Whiskey Realism implementation, release, tag, handoff, or documentation sweep after patches ship.
---

# Whiskey Release Closeout

Use this skill after implementation is verified and ready to document or release.

## Workflow

1. Confirm `git status`, recent commits, and branch.
2. Confirm tests/build/deploy/hash proof for DLL-affecting work.
3. Update `docs/patch-catalog.md` for shipped patch ordinals and runtime helpers.
4. Update `docs/handoff.md` with current state, deployed hash, smoke evidence, and remaining follow-ups.
5. Update README or other living docs only when user-facing behavior changed.
6. Archive shipped specs/plans after smoke verification and fix cross-references.
7. Commit related docs/code together unless the user asks for a different checkpoint.

## Rule

Do not tag or publish a release unless the deployed DLL hash and smoke boundary are explicit.
