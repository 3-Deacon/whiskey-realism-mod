# Whiskey Realism

A strategic-AI overhaul mod for **Grand Tactician: The Civil War (1861-1865)** focused on the **Whiskey & Lemons DLC** career mode.

In W&L the player starts at the bottom — commanding an artillery section, regiment, or brigade based on their questionnaire — and serves inside an AI-driven army. That puts a lot of weight on how good the AI playing your nation's Commander-in-Chief and the opposing CIC actually is. Vanilla picks campaign objectives with `Random.Range`. This mod replaces that with a thinking-but-not-historically-deterministic strategic engine for both factions.

## Status

**v0.2.2 released. Post-release `project-doctrine` is built and deployed locally with SHA-256 `dc028bae2169ca4de00e5af6209f868ae1a3421f3cac6f9bba6cf12743edd8db` (501248 bytes). Runtime smoke confirms the W&L command picker works, time advances after command selection, normal campaign systems fire without #22 errors, project-doctrine selection fires, and the Slice B0 tactical observer control surface is readable in W&L battles. Construction steering Slice B is deployed; optional telegraph AI remains default-off and still needs an enabled smoke run. The high-speed campaign AI governor is default-on at 20x/50x and caps vanilla `AICampaign.UpdateUnitAI` work while preserving vanilla construction side effects. Campaign-map awareness now captures active towns, represented states, forts, sea harbors, and river harbors, and capital-defense reinforcement uses proportional force sizing so small threats do not automatically pull oversized armies. Recruitment now protects threatened capital-priority areas before broader theater steering, the fort-construction governor prevents either side from stacking unlimited local/capital forts without local enemy threat, strategic anti-zerg theater-integrity gates prevent asset-proximity threats from custom-ordering whole theaters across the map, and the operational probe/contact loop can commit one bounded same-area probe, pause on enemy reaction, withdraw when overmatched, or escalate after favorable contact using chapter/era/season tempo doctrine across the war. The local `project-doctrine` branch also includes W&L dispatch/current-order bridging, coordinated operation packages, default-on historical operation doctrine, Slice B1 W&L charge/feud guards, and Slice B2 read-only command/order-friction telemetry; fresh runtime smoke is still needed after restart for `[HistoricalOperation]`, `[CoordinatedOps]`, B1 denial markers, and B2 `[TacticalCommand]` / friction markers.**

The mod replaces vanilla's random-objective AI with a personality-driven phased-plan strategic engine for both Confederate and Union AI. Released v0.2.2 includes battle-history observers, transfer/front-budget steering, capital-defense steering, fiscal economy/construction intent, recruitment state steering, policy/naval grand-strategy timing, default-on fast-forward AI catch-up, weekly CIC strategic review, grand-strategy objective/project steering, historical army operating areas, historical army-group steering using vanilla `ArmyGroup` APIs, and a formation-directive ledger for independent divisions/corps/armies. Post-release main adds role-aware campaign perk steering, routes locked-Hard difficulty into a small historical casualty-tolerance modifier, hardens the W&L command-selection retry around vanilla's frame-50 picker timing, includes construction steering Slice B: private-building steering plus optional default-off conservative telegraph AI, adds campaign-map town/state/fort/harbor awareness for strategic classification and capital-defense sizing, de-jitters daily strategic ledgers, caps high-speed vanilla campaign-AI passes, classifies dynamic vanilla-created commands by local area, protects threatened capital-priority recruitment areas, governs fort construction saturation, adds theater-integrity movement gates so local asset pressure does not strip distant theaters, and adds a strategic operational-probe/contact loop that uses vanilla `MoveUnitTo` plus `unitsinoffensiveoperations` for limited contact before mass commitment, paced by vanilla chapter, Whiskey era, season, faction, and CIC personality. 25 hand-coded historical-officer personalities, 12 canonical succession events with concrete `AssignCommando` swaps, 4-stage era progression, two-tier CIC + theater-commander hierarchy, player-CIC noninterference gate, startup heartbeat with deferred operational ledgers, W&L command-selection prompt retry, and town/battle war-state observers are in place. Locks the campaign-create menu's Aggressiveness / Historic / Difficulty settings + 5 realism checkboxes to coherent values, and caches hot-path reflection to avoid startup/menu lag.

Existing generated BepInEx config values take precedence over C# defaults. If `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg` already exists, review that file before expecting new default config descriptions or values to appear.

Developer references for current strategic and tactical layers:

- [`docs/operational-tempo-doctrine.md`](docs/operational-tempo-doctrine.md)
- [`docs/wl-dispatch-objective-bridge.md`](docs/wl-dispatch-objective-bridge.md)
- [`docs/coordinated-operation-packages.md`](docs/coordinated-operation-packages.md)
- [`docs/historical-operation-doctrine.md`](docs/historical-operation-doctrine.md)
- [`docs/superpowers/plans/2026-05-05-tactical-brain-master-sequencing.md`](docs/superpowers/plans/2026-05-05-tactical-brain-master-sequencing.md)
- [`docs/superpowers/plans/2026-05-07-tactical-b2-command-order-friction.md`](docs/superpowers/plans/2026-05-07-tactical-b2-command-order-friction.md)

Latest release: [v0.2.2](https://github.com/3-Deacon/whiskey-realism-mod/releases/tag/v0.2.2) — drop the attached `WhiskeyRealism.dll` into your `<GTCW>/BepInEx/plugins/` folder. Requires BepInEx 5.4.x x64 UnityMono.

## Goals

- **Strategic awareness.** AI commits to phased operational plans and reviews them weekly instead of randomly picking objectives turn by turn.
- **Historical character without scripting.** Era-based doctrine progression (1861 amateur → 1864-65 total war), faction profiles (CSA defensive-aggressive, Union slow-coordinated-pressure), and ~25 hand-coded historical commander personalities (Lee, McClellan, Grant, Sherman, Hood, Johnston, etc.) — composed additively so the same engine produces both Lee's army and Hood's army with the right feel.
- **Recognizable but not deterministic succession.** ~12 canonical historical events (Lee taking ANV command, Grant rising to General-in-Chief, McClellan's removal, Hood replacing Johnston, etc.) gated on date AND war-state. They fire when conditions reasonably hold; if your campaign has gone unusually, alternate histories emerge.
- **W&L-aware.** Player-commanded units are not steamrolled by the AI's strategic decisions — the existing `DLC_WL.dlc_scenarioactive` gate is respected and extended.

## Requirements

1. **Grand Tactician: The Civil War** with the **Whiskey & Lemons DLC**.
2. **BepInEx 5.4.x x64 UnityMono** installed in your game folder.
   - Download: <https://github.com/BepInEx/BepInEx/releases> → `BepInEx_unitymono_x64_5.4.x`
   - Extract into the game folder so that `<GTCW>/BepInEx/` exists.
   - Run the game once to generate the BepInEx config + plugin folders.

## Install

1. Download the latest `WhiskeyRealism.dll` from the [Releases page](https://github.com/3-Deacon/whiskey-realism-mod/releases).
2. Drop it into `<GTCW>/BepInEx/plugins/`.
3. Launch the game. On first run a config file is generated at `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg`.

To uninstall: delete `WhiskeyRealism.dll` from `BepInEx/plugins/`. Saves remain compatible with vanilla — mod state is stored in a separate JSON sidecar that vanilla GTCW ignores.

## Compatibility

Whiskey Realism expects the stock game `Assembly-CSharp.dll`. Mods that replace the game's managed DLL directly are unsupported; this mod layers Harmony patches over vanilla instead.

## License

MIT. See [LICENSE](LICENSE).

## Building from source

```bash
git clone git@github.com:3-Deacon/whiskey-realism-mod.git
cd whiskey-realism-mod

# Symlink GTCW DLLs (Linux/WSL):
GT="/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/The Civil War (1861-1865)_Data/Managed"
for f in Assembly-CSharp.dll Assembly-CSharp-firstpass.dll \
         UnityEngine.dll UnityEngine.CoreModule.dll UnityEngine.AIModule.dll \
         UnityEngine.AnimationModule.dll UnityEngine.UI.dll \
         UnityEngine.ParticleSystemModule.dll UnityEngine.PhysicsModule.dll \
         UnityEngine.JSONSerializeModule.dll UnityEngine.AudioModule.dll \
         UnityEngine.TextRenderingModule.dll UnityEngine.IMGUIModule.dll \
         Newtonsoft.Json.dll Unity.TextMeshPro.dll; do
    ln -sf "$GT/$f" "refs/$f"
done

./build.sh
# → dist/WhiskeyRealism.dll
```

## Acknowledgements

Inspired by the architecture of the [UBoatCrewMod](https://github.com/3-Deacon/uboat-crew-mod) project (BepInEx + HarmonyX patching of Unity Mono games).
