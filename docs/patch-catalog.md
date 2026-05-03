# Patch Catalog

Canonical numbered catalog of all shipped Harmony patches. Each item has a stable ordinal (never re-used even after withdrawal) and a one-line description with file path + decompile coordinates.

| # | Patch | File | Targets (file:line) | Description |
|---|---|---|---|---|
| | | | | *(none yet — v0.1.0 is scaffold only)* |

---

## Conventions

- **Ordinal stability:** Once assigned, a number is never re-used. Withdrawn patches keep their ordinal with a `(withdrawn)` note. This makes git-log and changelog references stable across time.
- **One concern per file:** Every patch class lives in its own `.cs` file under `src/WhiskeyRealism/Patches/`.
- **Targets column** lists `Class.Method` and the absolute decompile line number from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.
- **Source-of-truth order:** shipped code > this catalog > per-patch design doc > umbrella spec > archived plan. If they disagree, trust the code.

## Pending (designed but not yet shipped)

The strategic-brain spec is in progress at `docs/superpowers/specs/`. When it ships, the patches it produces will be assigned ordinals here.
