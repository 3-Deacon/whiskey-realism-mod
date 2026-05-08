# Whiskey Realism — Agent Instructions

> **Project:** Whiskey Realism (Grand Tactician: The Civil War — Strategic AI Overhaul)
> **Repo:** GitHub public repo: `3-Deacon/whiskey-realism-mod`
> **URL:** `https://github.com/3-Deacon/whiskey-realism-mod`
> **Remote:** `origin` = `git@github.com:3-Deacon/whiskey-realism-mod.git`
> **Default branch:** `main`
> **Path:** `~/Projects/whiskey-realism-mod/`
> **Stack:** BepInEx 5.4.x x64 + HarmonyX, C# netstandard2.1, targeting Grand Tactician: The Civil War (Unity 2021.3.16f1, Mono x64, Steam)

> **Quick reference:**
> - **Build:** `./build.sh` → `dist/WhiskeyRealism.dll`
> - **Deploy:** `cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"` (game must be closed — Windows holds an exclusive lock on loaded DLLs)
> - **Test:** `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` (console harness; pure strategic logic only)
> - **Required for every DLL-affecting change:** build, deploy, then verify the deployed DLL matches `dist/WhiskeyRealism.dll` by timestamp/size and `sha256sum`. Do not report an implementation as ready from build output alone.
> - **Agent instructions file:** `AGENTS.md` is the source. `CLAUDE.md` at the repo root is a symlink to it so Claude Code and Codex pick up the same content. Edit `AGENTS.md`; never write into `CLAUDE.md` directly.
> - **Source-of-truth order:** shipped code > [`docs/patch-catalog.md`](docs/patch-catalog.md) > per-patch design doc > umbrella spec > archived plan
> - **Master handoff:** [`docs/handoff.md`](docs/handoff.md) — read first at session start
> - **Repository memory:** [`MEMORY.md`](MEMORY.md) — short durable state/index; read after `AGENTS.md` when resuming or updating project context
> - **Decompile:** `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (266k lines; regenerate with the steps in [`docs/findings.md`](docs/findings.md) if `/tmp` was wiped)
> - **Parallel sessions are normal.** Another agent may be working concurrently — run `git log --oneline -10` and `git status` before committing to detect parallel work.

> **Current state:** see `docs/handoff.md` for the deployed DLL hash, post-release deltas, and active workstream. AGENTS.md intentionally does not duplicate volatile state — it churns every commit and went stale repeatedly when carried inline here.

---

## What this project is

A BepInEx plugin for Grand Tactician: The Civil War (1861-1865) that layers surgical Harmony patches on top of vanilla. **Slice A** (strategic-brain overhaul: era × faction × officer-personality scoring replacing the vanilla random-objective picker) shipped at v0.2.2. Tactical AI work follows the same shipped-code/decompile-first rule; see `docs/handoff.md` for the current shipped state, deployed DLL hash, and active workstream.

Six locked design choices (Slice A umbrella spec, archived after ship: [`docs/superpowers/specs/archive/2026-05-02-strategic-brain-design.md`](docs/superpowers/specs/archive/2026-05-02-strategic-brain-design.md)):
1. Slice A — strategic brain (campaign layer first; tactical layer is a later slice)
2. Tier 3 scope — replace existing weak decisions + extend + net-new operational plans
3. Era × faction × officer (full personality stack)
4. Triggered-scripted officer succession (~12 historical events; ~60-80% historical fidelity)
5. Phased operational plans (2-4 phases per plan, one active per side)
6. Daily + event-triggered AI cadence; adjust-current-plan by default, replan only on assumption-invalidating events. Monthly is a visible heartbeat/checkpoint boundary only. (Migrated from weekly to daily on 2026-05-04 by the Defense Intent Ledger slice; `DefenseCooldownTable` per-cycle idempotency and `FrontSectorRuntime.Signature` 0.5-bucket ratios make daily safe from thrash.)

Design architecture: two-tier hierarchy (CIC + theater commanders), startup heartbeat plus daily front/army-area/formation-directive/fiscal/construction/defense ledgers after vanilla `AICampaign.aifaction` initializes, historical army-group steering through vanilla `ArmyGroup` APIs, additive personality composition with `[-1, 1]` clamp, JSON sidecar persistence next to game saves, read-only mod state from Harmony patches.

---

## Build & install

```bash
./build.sh                      # dotnet restore + build → dist/WhiskeyRealism.dll
```

Install: drop `dist/WhiskeyRealism.dll` into `<GTCW>/BepInEx/plugins/`. Requires **BepInEx 5.4.x x64 UnityMono** to be installed in the game folder first.

GTCW install path (WSL):

```
/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/
```

Deploy command (WSL):

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

After every DLL-affecting implementation, run `./build.sh`, deploy the DLL, then verify the deployed file:

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

The two SHA-256 hashes must match before asking the user to smoke-test.

The `cp` will fail with `cp: cannot create regular file ...: Invalid argument` if GTCW is currently running — Windows holds an exclusive lock on loaded DLLs. Close the game first, then redeploy.

Smoke-test paths:

- **Log:** `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log` — truncated on each game launch.
- **Config:** `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/config/dev.kyle.whiskey-realism.cfg` — created on first plugin load. **Once this file exists, its values take precedence over C# defaults** — bumping a default in `Plugin.cs` will NOT affect a user who has already run the plugin once.
- **Mod state sidecar:** `<GTCW>/Campaigns/<level>/<sublevel>/<save>/whiskeyrealism.json` — JSON sidecar with active plans, era stage, succession state. Vanilla save paths are CWD-relative to the game install, not `Application.persistentDataPath`.

---

## Compatibility notes

Whiskey Realism expects the stock game `Assembly-CSharp.dll`. Mods that replace the game's managed DLL directly are unsupported; this project layers Harmony patches over vanilla instead.

---

## Repo layout

```
whiskey-realism-mod/
├── AGENTS.md                       ← this file
├── MEMORY.md                       ← durable repo memory index (not a substitute for docs/handoff.md)
├── CLAUDE.md                       ← symlink to AGENTS.md
├── README.md                       ← user-facing
├── LICENSE                         ← MIT
├── NuGet.config                    ← BepInEx package feed
├── build.sh
├── refs/                           ← symlinks to GTCW DLLs (gitignored)
├── src/WhiskeyRealism/
│   ├── WhiskeyRealism.csproj
│   ├── Plugin.cs                   ← BepInEx entry, ConfigEntry definitions
│   ├── Strategic/                  ← strategic-brain core types: coordinator, CIC + theater commanders, era/personality/succession, per-cadence ledgers (front, army-area, formation-directive, fiscal, construction, defense intent), and supporting catalogs/runtimes. See files in directory; `docs/patch-catalog.md` is the canonical patch ordinal map.
│   ├── Patches/                    ← Harmony patches; one concern per file
│   │   └── See docs/patch-catalog.md for the canonical numbered ordinal map; coordinator-driven runtimes are listed there too without ordinals.
│   └── Util/                       ← shared infrastructure
│       └── OnceLog / reflection helpers
├── docs/
│   ├── handoff.md                  ← session-start master plan
│   ├── findings.md                 ← decompile coordinates + reflection gotchas
│   ├── patch-catalog.md            ← canonical numbered catalog of shipped patches
│   ├── bug-fixes/                  ← cross-cutting vanilla bug-fix workstream and backlog
│   └── superpowers/
│       ├── README.md               ← lifecycle + layout
│       ├── specs/                  ← active design specs (current/upcoming slices)
│       │   └── archive/            ← shipped specs (frozen; see archive/README.md)
│       └── plans/                  ← active implementation plans
│           └── archive/            ← shipped plans (frozen; see archive/README.md)
├── dist/                           ← build output (gitignored)
└── .claude/settings.json           ← project-local Claude Code permissions
```

---

## Operating rules

### Agent workflow modules

Codex supports layered `AGENTS.md` files. It loads this root file first, then any more specific `AGENTS.md` files on the path to the current working directory. If a session starts at repo root, use this section as the index and read the relevant nested guidance before specialized work:

- Patch work: [`src/WhiskeyRealism/Patches/AGENTS.md`](src/WhiskeyRealism/Patches/AGENTS.md)
- Pure strategic/tactical logic: [`src/WhiskeyRealism/Strategic/AGENTS.md`](src/WhiskeyRealism/Strategic/AGENTS.md)
- Tests: [`tests/WhiskeyRealism.Tests/AGENTS.md`](tests/WhiskeyRealism.Tests/AGENTS.md)
- Bug fixes: [`docs/bug-fixes/AGENTS.md`](docs/bug-fixes/AGENTS.md)
- Specs/plans/reviews: [`docs/superpowers/AGENTS.md`](docs/superpowers/AGENTS.md)
- Review checklist: [`docs/agent-code-review.md`](docs/agent-code-review.md)

Repeatable workflows should become repo skills under `.agents/skills/` instead of more root `AGENTS.md` text.

### Superpowers workflow

Codex uses native skill discovery. Do not paste legacy Superpowers bootstrap blocks into `AGENTS.md`; keep this file as repo policy and use skills for repeatable workflows. Superpowers is installed as a Codex plugin, and Codex can load skills from the plugin, `$HOME/.agents/skills`, and repo-local `.agents/skills`.

Use the relevant Superpowers or repo skill before acting when the task matches its description, or when the user explicitly invokes `$skill-name`. Treat the order below as mandatory, not a menu:

1. Review-only / critique work: use `whiskey-spec-adversarial-review` and/or `whiskey-vanilla-anchor-review`; lead with findings and cite shipped-code/decompile anchors. Do not create a worktree unless edits will be made.
2. New feature or behavior design: use `brainstorming` before implementation; commit durable slice specs under `docs/superpowers/specs/`.
3. After design/spec approval and before writing or executing an implementation plan: use `using-git-worktrees`. Detect whether the session is already in an isolated worktree, prefer any native worktree mechanism if available, otherwise use the git worktree fallback; if sandboxing blocks worktree creation, state that and continue only with the user's current-workspace preference.
4. Approved multi-step work: use `writing-plans`; save plans under `docs/superpowers/plans/` with exact file paths, patch surfaces, verification commands, smoke expectations, and rollback/defer boundaries.
5. Before implementation code: use `test-driven-development` for testable strategic/tactical logic. For Harmony/runtime-only changes, add harness coverage appropriate to the risk and changed behavior when feasible and always follow the DLL build/deploy/hash verification gates above.
6. Plan execution: only after the `using-git-worktrees` gate, use `subagent-driven-development` when the active Codex session exposes multi-agent support and the user has requested or permitted subagents. Use `executing-plans` when subagents are unavailable or the user wants inline execution.
7. Independent investigations: use `dispatching-parallel-agents` only for 2+ genuinely independent domains; keep write scopes disjoint and do not bypass the worktree gate for implementation work.
8. Bugs or unexpected behavior at any point: use `systematic-debugging` before proposing fixes, then return to the appropriate ordered step above.
9. Completion: use `verification-before-completion`; for DLL-affecting changes also use `whiskey-dll-deploy-smoke`, and for shipped release closeout use `whiskey-release-closeout`.

### References

- `refs/` holds symlinks into the Steam install. **Do not check binary DLLs into git.**
- `refs/` is gitignored, so `git worktree add` does **not** carry the symlinks into a new worktree. After creating a worktree, re-link from the main repo: `cd <worktree> && ln -s ../../refs refs`. Without this, `./build.sh` and `dotnet run --project tests/...` fail with `Assembly-CSharp` / `UnityEngine` / `Newtonsoft.Json` resolve errors.
- The HarmonyX runtime is pulled from the BepInEx NuGet feed (so the build works without BepInEx installed in the game). At runtime, BepInEx-provided HarmonyX is what executes patches.
- The decompiled source for `Assembly-CSharp.dll` lives at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (regenerate with the steps in `docs/findings.md` if `/tmp` was wiped). It's the primary source of truth for method signatures.

### Decompilation workflow (when adding new patches)

1. Identify the behavior you want to change. Search strings in `Assembly-CSharp.dll` with `strings -n 6 <dll> | grep -iE …` to find class/method names.
2. Decompile with ilspycmd (see `docs/findings.md` §Decompilation) and grep for the method.
3. Read the decompiled implementation before writing a Harmony patch — never patch a method you haven't read.
4. Prefer Postfix patches over Prefix or Transpiler. Use AccessTools/reflection for fields/properties whose names might shift across game updates, so renames downgrade to a logged warning rather than a crash.

### Patch hygiene

- Every Harmony patch class lives under `src/WhiskeyRealism/Patches/`. One concern per file.
- Wrap reflection lookups in try/catch and log via `Plugin.Log.LogWarning(...)` on failure. **Never throw from a patch.** A single throw on every Postfix tick produces 40k log lines per session.
- Strategic mod state is **read-only** to Harmony patches. Patches read CIC / TheaterCommander / ledger state and steer existing AI methods. State writes happen only on daily strategic review and event-trigger handlers.
- New tactical behavior patches that write vanilla battle state (`macroai`, `ai_stance`, movement, reserves, artillery, fallback, retreat, or charge state) must ship behind explicit default-off config until a focused in-game smoke proves bounded logs, stable Harmony anchors, no repeated exceptions, no player-subordinate retasking, and no unintended side effects. Read-only telemetry may be enabled separately.
- Add a header comment explaining what the vanilla method does and what the patch changes.

### Build

- `netstandard2.1` is mandatory — Unity 2021 Mono runtime.
- Reference GTCW/Unity DLLs with `<Private>false</Private>` so we don't ship copies; Mono finds them in Managed at runtime.
- BepInEx + HarmonyX packages come from `https://nuget.bepinex.dev/v3/index.json` (configured in `NuGet.config`).

### Testing

Use the console harness for pure strategic logic when touched:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

For DLL-affecting changes: build, deploy, verify deployed DLL hash, run GTCW, start a career, observe. After deploy, tail `BepInEx/LogOutput.log` and scan for the per-patch first-fire markers.

The test project at `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` uses **explicit `<Compile Include>` entries** for each strategic source file it consumes — not glob patterns. When you add a new file under `src/WhiskeyRealism/Strategic/` (or a subfolder), add a matching `<Compile Include="..\..\src\WhiskeyRealism\Strategic\<File>.cs" Link="<File>.cs" />` line to the csproj or the test project will not see the new type. Same applies to file deletions (remove the corresponding entry).

### Game updates

When GTCW patches: re-decompile `Assembly-CSharp.dll`, diff our patch sites, rebuild if signatures unchanged, update `[HarmonyPatch(...)]` attributes if renamed, re-read the new implementation if behavior shifted.

### Doc lifecycle

- **Living docs** churn with shipped state: `docs/handoff.md`, `docs/patch-catalog.md`, `docs/findings.md`, `MEMORY.md`, this `AGENTS.md`, and `README.md`. Update them as work ships.
- **Specs** under `docs/superpowers/specs/` are point-in-time design artifacts. Move a spec to `docs/superpowers/specs/archive/` once its corresponding patches ship and are smoke-verified. Do not mutate archived specs after shipping; record deltas in `docs/handoff.md` "What just shipped" and the archive `README.md` index.
- **Plans** under `docs/superpowers/plans/` are execution artifacts. Move them to `docs/superpowers/plans/archive/` once their patches ship.
- When archiving, rewrite internal `docs/superpowers/{specs,plans}/2026-…` cross-references to the corresponding archive paths so historical traceability stays intact.

---

## What NOT to do

- Don't edit the game install (`/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/`) directly. Mod via plugin only.
- Don't ship copies of `0Harmony.dll` or any GTCW/Unity DLL with our plugin — BepInEx loads HarmonyX itself, the game loads Unity itself.
- Don't add Prefix-blocking or Transpiler patches without consulting the user — they're brittle to game updates and easy to get wrong.
- Don't write Harmony patches that mutate strategic mod state. State writes happen ONLY on daily strategic review and event-trigger handlers. Patches READ; they don't WRITE. (Targeted candidate-list filtering via Prefix-snapshot/Postfix-restore is permitted as the spec'd Slice 2 enforcement surface — see #25 — but the snapshot/restore must be try/finally-safe.)
- Don't size per-alliance state arrays to 2 without bound-checking. `AICampaign.aifaction` includes alliance 2 (Europe) — `AICampaignReflect.GetAllianceId(_aifaction)` can return 2, so any `someArray[allianceId]` access where `someArray.Length == 2` must guard with `if (allianceId < 0 || allianceId >= someArray.Length) return;` (or short-circuit alliance > 1 entirely if Europe shouldn't get the treatment).
- Don't expand workstream scope without an aligned spec/plan. Use `docs/handoff.md` for the current active workstream; AGENTS.md must not duplicate volatile slice status. Confirmed vanilla bug fixes may be tracked under `docs/bug-fixes/`, but do not treat broad doctrine or feature work as a bug fix.

---

## Useful references

- BepInEx 5 docs: https://docs.bepinex.dev/v5-lts/
- HarmonyX (we use the BepInEx-provided runtime): https://github.com/BepInEx/HarmonyX
- Grand Tactician modding (community): https://steamcommunity.com/app/654890/discussions/3/
- Vanilla Modding Tool (Excel-driven data): `<GTCW>/Modding/ModdingTool_1.11.xlsm`
