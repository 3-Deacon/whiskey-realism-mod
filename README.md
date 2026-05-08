# Whiskey Realism

A strategic-AI overhaul mod for **Grand Tactician: The Civil War (1861-1865)** focused on the **Whiskey & Lemons DLC** career mode.

In W&L the player starts at the bottom — commanding an artillery section, regiment, or brigade based on their questionnaire — and serves inside an AI-driven army. That puts a lot of weight on how good the AI playing your nation's Commander-in-Chief and the opposing CIC actually is. Vanilla picks campaign objectives with `Random.Range`. This mod replaces that with a thinking-but-not-historically-deterministic strategic engine for both factions.

## Status

**v0.2.2 released. Post-release tactical work is merged to `main`, built, and deployed locally with SHA-256 `a5a6e1fd099d11d2ff5dc6fd460d91e4e98a26a6f405df9d4b5dbfc808ed0d38` (663040 bytes) and console harness **517 PASS / 0 FAIL**. Runtime smoke confirms the W&L command picker works, time advances after command selection, normal campaign systems fire without #22 errors, project-doctrine selection fires, and the Slice B0/B2 tactical observer surfaces are readable in W&L battles. Current tactical work includes removable `[TacticalDecisionMatrix]` battle/group rows, #46 `BattleObjectiveChainWlGuardPatch`, B4/B5 default-off tactical macro/group stance scoring, B6c local reaction/reserve intent runtime context, B7+B8 artillery/withdrawal runtime wiring, #53 default-off `TacticalPathfinderDisciplinePatch`, #54 default-on W&L operation null guard, #55 default-off HQ auto-link guard, #56 default-off reserve order-delay guard, and #57 default-off reserve-list bias. B5 now keeps defensive weak-point/fix decisions in stance 2 under defensive macro and limits the local stance writer to battle brigade groups (`unittyp == 14`), leaving division/army-level organization to vanilla/B6 command intent. Focused B7+B8 smoke with the live config intentionally setting `Enable Tactical Artillery Doctrine = true` and `Enable Tactical Withdrawal Doctrine = true` confirmed `[once:b7-check-ai-bombardment]`, `[once:b7-counterbattery]`, `[once:b8-check-line-fallbacks]`, `[once:b8-morale-snapshot-sampler]`, `[once:b8-check-reserves]`, and `[once:b8-set-withdrawal]`, with no B7/B8 repeated exceptions, Harmony failures, missing-anchor warnings, or writer error markers. `[once:b8-microai-check-retreats]`, conditional `[once:b7-cancel-bombard]`, #53 enabled `[TacticalPathfinderDiscipline]`, B6c `[TacticalLocalReaction]`/`[TacticalReserveIntent]`, and #55/#56 guard smoke still need observation on the merged DLL. C# defaults keep behavior writer valves false except #54. Fresh runtime smoke is still needed for `[HistoricalOperation]`, `[CoordinatedOps]`, `[TacticalObjectiveGuard]`, B5 `defend-hold` / `command-scope` behavior, `[Patch:TacticalFallbackRetreatNullGuard]`, and the pending tactical conditional markers if those paths exercise.**

The mod replaces vanilla's random-objective AI with a personality-driven phased-plan strategic engine for both Confederate and Union AI. Released v0.2.2 includes battle-history observers, transfer/front-budget steering, capital-defense steering, fiscal economy/construction intent, recruitment state steering, policy/naval grand-strategy timing, default-on fast-forward AI catch-up, weekly CIC strategic review, grand-strategy objective/project steering, historical army operating areas, historical army-group steering using vanilla `ArmyGroup` APIs, and a formation-directive ledger for independent divisions/corps/armies. Post-release main adds role-aware campaign perk steering, routes locked-Hard difficulty into a small historical casualty-tolerance modifier, hardens the W&L command-selection retry around vanilla's frame-50 picker timing, includes construction steering Slice B: private-building steering plus optional default-off conservative telegraph AI, adds campaign-map town/state/fort/harbor awareness for strategic classification and capital-defense sizing, de-jitters daily strategic ledgers, caps high-speed vanilla campaign-AI passes, classifies dynamic vanilla-created commands by local area, protects threatened capital-priority recruitment areas, governs fort construction saturation, adds theater-integrity movement gates so local asset pressure does not strip distant theaters, and adds a strategic operational-probe/contact loop that uses vanilla `MoveUnitTo` plus `unitsinoffensiveoperations` for limited contact before mass commitment, paced by vanilla chapter, Whiskey era, season, faction, and CIC personality. 25 hand-coded historical-officer personalities, 12 canonical succession events with concrete `AssignCommando` swaps, 4-stage era progression, two-tier CIC + theater-commander hierarchy, player-CIC noninterference gate, startup heartbeat with deferred operational ledgers, W&L command-selection prompt retry, and town/battle war-state observers are in place. Locks the campaign-create menu's Aggressiveness / Historic / Difficulty settings + 5 realism checkboxes to coherent values, and caches hot-path reflection to avoid startup/menu lag.

Existing generated BepInEx config values take precedence over C# defaults. If `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg` already exists, review that file before expecting new default config descriptions or values to appear.

Tactical behavior valves are intentionally conservative. B3 odds telemetry is read-only, but B4 writes battle-level `macroai`, B5 writes group `ai_stance`, B7 can change artillery combat behavior, B8 can call vanilla `BattleUnits.SetWithdrawal`, and #53 can change private `Regiment.AddPath` outcomes. Their C# defaults stay false until focused in-game smoke proves bounded logs, stable Harmony anchors, no repeated exceptions, no player-subordinate retasking, and no unintended movement/reserve/artillery/fallback/charge/path side effects. The live B7/B8 config is currently enabled only because it is under active smoke observation.

Developer references for current strategic and tactical layers:

- [`docs/operational-tempo-doctrine.md`](docs/operational-tempo-doctrine.md)
- [`docs/wl-dispatch-objective-bridge.md`](docs/wl-dispatch-objective-bridge.md)
- [`docs/coordinated-operation-packages.md`](docs/coordinated-operation-packages.md)
- [`docs/historical-operation-doctrine.md`](docs/historical-operation-doctrine.md)
- [`docs/bug-fixes/vanilla-tactical-battlefield.md`](docs/bug-fixes/vanilla-tactical-battlefield.md)
- [`docs/superpowers/specs/2026-05-07-tactical-b3-b5-odds-macro-sector-design.md`](docs/superpowers/specs/2026-05-07-tactical-b3-b5-odds-macro-sector-design.md)
- [`docs/superpowers/plans/2026-05-07-tactical-b3-b5-odds-macro-sector.md`](docs/superpowers/plans/2026-05-07-tactical-b3-b5-odds-macro-sector.md)
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
