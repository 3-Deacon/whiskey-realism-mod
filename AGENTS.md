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
> - **Required for every DLL-affecting change:** build, deploy, then verify the deployed DLL matches `dist/WhiskeyRealism.dll` by timestamp/size and `sha256sum`. Do not report an implementation as ready from build output alone.
> - **Source-of-truth order:** shipped code > [`docs/patch-catalog.md`](docs/patch-catalog.md) > per-patch design doc > umbrella spec > archived plan
> - **Master handoff:** [`docs/handoff.md`](docs/handoff.md) — read first at session start
> - **Repository memory:** [`MEMORY.md`](MEMORY.md) — short durable state/index; read after `AGENTS.md` when resuming or updating project context
> - **Decompile:** `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (266k lines; regenerate with the steps in [`docs/findings.md`](docs/findings.md) if `/tmp` was wiped)
> - **Parallel sessions are normal.** Another agent may be working concurrently — run `git log --oneline -10` and `git status` before committing to detect parallel work.

> **Current release:** v0.2.2. **Main current:** post-release main includes locked-Hard casualty-tolerance integration, #7 role-aware campaign perk steering, #21 high-speed AI throttle/diagnostics, construction steering Slice B, and a hardened #22 W&L command-selection retry on top of v0.2.2, built and deployed (`dist`/BepInEx SHA-256 `24bc91a17512f53a4724bcbbd006550b6e8630e1d79d2bbec828c83a8392b243`). Current main includes fiscal economy AI, recruitment state steering, policy/naval grand-strategy timing, default-on fast-forward catch-up, hot-path reflection caching for startup/menu lag, W&L command-selection prompt retry, historical-Hard casualty tolerance routed into `CIC.Effective`, campaign army/fleet perk scoring, private-building steering, and optional default-off telegraph AI. #22 must not invoke `CareerInformationPanel.ShowStartUnitSelectionList(true)` before vanilla frame 50, and must not treat the outer picker panel as usable unless `unitlineappointcommand` rows exist. Fresh W&L picker smoke is required after the latest #22 usable-row retry change; actual #7 perk assignment remains conditional on vanilla perk timing. `FormationOffensiveSafetyPatch` remains intentionally deferred until the directive ledger has more runtime soak.

---

## What this project is

A BepInEx plugin for Grand Tactician: The Civil War (1861-1865) that layers surgical Harmony patches on top of vanilla. Initial focus: **Slice A — strategic-brain overhaul** for the Whiskey & Lemons DLC career mode. Replaces the vanilla random-objective campaign AI with an era × faction × officer-personality scoring system that gives both Confederate and Union AI campaigns historical character without scripting them deterministically.

Six locked design choices (see `docs/superpowers/specs/`):
1. Slice A — strategic brain (campaign layer first; tactical layer is a later slice)
2. Tier 3 scope — replace existing weak decisions + extend + net-new operational plans
3. Era × faction × officer (full personality stack)
4. Triggered-scripted officer succession (~12 historical events; ~60-80% historical fidelity)
5. Phased operational plans (2-4 phases per plan, one active per side)
6. Weekly + event-triggered AI cadence; adjust-current-plan by default, replan only on assumption-invalidating events. Monthly is a visible heartbeat/checkpoint boundary only.

Design architecture: two-tier hierarchy (CIC + theater commanders), startup heartbeat plus weekly front/army-area/formation-directive ledgers after vanilla `AICampaign.aifaction` initializes, historical army-group steering through vanilla `ArmyGroup` APIs, additive personality composition with `[-1, 1]` clamp, JSON sidecar persistence next to game saves, read-only mod state from Harmony patches.

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

**Explicitly incompatible with the "Community Hotfixes / Quality of Life Mod"** distributed via Steam. That mod replaces `Assembly-CSharp.dll` wholesale; this mod is a BepInEx plugin that patches the vanilla DLL via Harmony. They cannot coexist. Long-term: extract Community Hotfix's behavior fixes into our patch suite so users only need this mod.

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
│   ├── Strategic/                  ← strategic-brain core types
│   │   ├── StrategicCoordinator.cs ← weekly strategic review + monthly heartbeat dispatcher
│   │   ├── CIC.cs                  ← per-faction Commander-in-Chief
│   │   ├── TheaterCommander.cs     ← per-army-group execution layer
│   │   ├── OperationalPlan.cs      ← phased plan + phase transitions
│   │   ├── PersonalityVector.cs    ← 5-dim struct + composition helpers
│   │   ├── EraStageManager.cs      ← era progression + war-state overrides
│   │   ├── SuccessionScheduler.cs  ← canonical historical events
│   │   ├── HistoricalFigureRegistry.cs ← ~25 hand-coded officer profiles
│   │   ├── FrontSectorLedger.cs / FrontSectorRuntime.cs
│   │   ├── ArmyAreaDoctrine.cs / ArmyAreaLedger.cs / ArmyAreaRuntime.cs
│   │   └── ArmyGroupDoctrine.cs
│   ├── Patches/                    ← Harmony patches; one concern per file
│   │   └── Harmony patches #1-#16 (see docs/patch-catalog.md)
│   └── Util/                       ← shared infrastructure
│       └── OnceLog / reflection helpers
├── docs/
│   ├── handoff.md                  ← session-start master plan
│   ├── findings.md                 ← decompile coordinates + reflection gotchas
│   ├── patch-catalog.md            ← canonical numbered catalog of shipped patches
│   └── superpowers/                ← per-feature specs + per-plan implementation plans
│       ├── specs/
│       └── plans/
├── dist/                           ← build output (gitignored)
└── .claude/settings.json           ← project-local Claude Code permissions
```

---

## Operating rules

### References

- `refs/` holds symlinks into the Steam install. **Do not check binary DLLs into git.**
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
- Strategic mod state is **read-only** to Harmony patches. Patches read CIC / TheaterCommander / ledger state and steer existing AI methods. State writes happen only on weekly strategic review and event-trigger handlers.
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

### Game updates

When GTCW patches: re-decompile `Assembly-CSharp.dll`, diff our patch sites, rebuild if signatures unchanged, update `[HarmonyPatch(...)]` attributes if renamed, re-read the new implementation if behavior shifted.

---

## What NOT to do

- Don't edit the game install (`/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/`) directly. Mod via plugin only.
- Don't ship copies of `0Harmony.dll` or any GTCW/Unity DLL with our plugin — BepInEx loads HarmonyX itself, the game loads Unity itself.
- Don't add Prefix-blocking or Transpiler patches without consulting the user — they're brittle to game updates and easy to get wrong.
- Don't write Harmony patches that mutate strategic mod state. State writes happen ONLY on weekly strategic review and event-trigger handlers. Patches READ; they don't WRITE.
- Don't expand workstream scope without an aligned spec. The current single workstream is Slice A (strategic brain). Slices B (tactical brain), C (W&L hierarchy AI), and D (additional historical flavor) are explicitly deferred.

---

## Useful references

- BepInEx 5 docs: https://docs.bepinex.dev/v5-lts/
- HarmonyX (we use the BepInEx-provided runtime): https://github.com/BepInEx/HarmonyX
- Grand Tactician modding (community): https://steamcommunity.com/app/654890/discussions/3/
- Vanilla Modding Tool (Excel-driven data): `<GTCW>/Modding/ModdingTool_1.11.xlsm`
