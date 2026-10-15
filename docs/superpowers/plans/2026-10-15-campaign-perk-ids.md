# Campaign perk-id fixes (BUG-AI-003..010) — plan and as-built

Branch `fix/campaign-perk-ids`. A bug-fix slice: vanilla reads perk ids from the wrong perk table. The detailed audit, intent reasoning and decompile anchors are in [`../../bug-fixes/vanilla-perk-ids.md`](../../bug-fixes/vanilla-perk-ids.md); this file records what was built, what each check proves, and the smoke.

## Decisions on record

- Transpilers: `src/WhiskeyRealism/Patches/AGENTS.md` requires explicit user approval. User approval 2026-10-15 (question "five new Transpilers": `AutocalcPerkIdsPatch` ×2, `RegimentMovePerkPatch`, `CommanderFameReportersPatch`, `ColdSteelMoralePatch`; answer: "Approve all five (Recommended)").
- Cold Steel III at 128617 (no tooltip promise): user decision 2026-10-15, answer "Fix as Cold Steel III (Recommended)".
- Scope: the user's "Yes fix it, ensuer ambualnce and recon works correctly" (003-005), then "fix all of this correctly" (006-010).
- Every fix applies to every side, the W&L player included; nothing is W&L-specific.

## As built

Pure rules (in the harness): `Strategic/VanillaPerkTables.cs` (table by unit type; every patch/hook decision takes its flag), `Patches/PerkIdIlRewrites.cs` (the IL edits), `Patches/PerkSwaps.cs` (`PerkListSwap<,>`, `CampaignWoundedSwap<,>`, `CompiledFieldGetter`). Runtime: `Patches/PerkIdIlHooks.cs` (the static methods the IL calls), `Patches/PerkIdTranspiler.cs` (clone, rewrite, log, or hand vanilla back).

| Patch | Kind | Target | Flag | Bug |
|---|---|---|---|---|
| `FogPerkSightPatch` | Prefix / Finalizer (perks swap) | `FogOfWar.UpdateViewRange` | Fix Perk Id Mismatches | 003 |
| `FogPerkConcealmentPatch` | Prefix (regref → null) | `FogOfWar.UnitIsHidden` | Fix Perk Id Mismatches | 004 |
| `CampaignGroupPerkPatch` | Prefix / Finalizer (perks + factor arrays) | `Regiment.UpdateCampaignUnitStats` | Fix Perk Id Mismatches | 005 |
| `AutocalcPerkIdsPatch.FightUnitIds`, `.FatigueIds` | Transpiler (id remaps) | `Autocalc.FightUnit`, `Autocalc.UpdateFatigue` | Fix Perk Id Mismatches | 006, 007 |
| `RegimentMovePerkPatch` | Transpiler (call swaps) | `Regiment.RegimentMove` | both | 008, 009 |
| `SeaRaidersBlockadePatch` | Prefix / Finalizer (perks swap) | `Regiment.UpdateBlockades` | Fix Perk Id Mismatches | 008 |
| `BattleHqPerkPatch` (5 classes) | Postfix ×3, Prefix / Finalizer ×2 | `CheckAbilityToRun`, `Initialize(bool)`, `ChoosePerk`, `UpdateFatigue`, `GetCoverValueFromID` | Fix Perk Id Mismatches | 009 |
| `CommanderFameReportersPatch` | Transpiler (call swaps) | `BattleUnits.CalculateCommanderFame` | Fix Perk Effect Scope | 010 |
| `ColdSteelMoralePatch` | Transpiler (id remap) | `Regiment.UpdateMorale` | Fix Perk Id Mismatches | 010 |

Ordinals are assigned at merge (`docs/patch-catalog.md`). `FogPerkSightPatch` reads `FogOfWar.units` / `FOWUnit.reg` through getters compiled once (`CompiledFieldGetter`), not `FieldInfo.GetValue` per call. `RegimentMove` has no Prefix/Postfix; its hooks are timed under `campaign.patch.perk-ids.regiment-move.*` and `tactical.patch.perk-ids.regiment-move.zouave-sound`.

Not fixed, recorded: `IncreasePerkExp` 123361 can set an HQ's `isabletorun` only through the F5 debug key (189138, 189546).

## PROVE-MODE

PROVE-MODE: harness + IL verification; runtime unproven until the in-game smoke below.

- **The harness proves** (`PerkIdFixTests`, `PerkIdIlTests`, `PerkIdRuntimeTests`; mutation-checked): the table rule and every patch/hook decision, with each flag on and off; the wounded/sickness factors and their parent stacking; each IL rewrite on the real `refs/Assembly-CSharp.dll` IL (exact sites, neighbouring correct reads untouched, refusal and unchanged IL on a missing or duplicated site); the perks swap and the wounded swap hide and restore (twice, and leave a list vanilla replaced); the compiled getters read the real `FogOfWar.units` / `FOWUnit.reg`; by source scan, that every swap patch restores at the top of its Prefix and in its Finalizer, and that every hook and patch passes its flag to the pure decision.
- **IL verification proves** (scratch, not in the repo; method in `docs/findings.md` § Perk id tables): the five edited method bodies, replayed with Mono.Cecil at the same positions with calls into the real hooks, pass ILVerify 8.0.0; a wrong-signature hook is rejected.
- **Only the in-game smoke proves:** that HarmonyX on Unity's Mono emits the transpiled methods (HarmonyX 2.10 cannot patch under .NET 8 here); that the Prefix swaps and Finalizer restores run around the live vanilla calls without leaving a unit's perks or `GamePrefs` arrays swapped; that vanilla behaves as described with the corrected reads; and the per-frame cost of the `RegimentMove` hooks.

## Smoke

Build, deploy and hash-verify per `AGENTS.md`, then `python3 -I tools/smoke-check.py "<GTCW>/BepInEx/LogOutput.log"`.

1. Startup: `[first-fire] campaign.patch.perk-ids.autocalc.fight-unit.transpiler`, `campaign.patch.perk-ids.autocalc.fatigue.transpiler`, `campaign.patch.perk-ids.regiment-move.transpiler`, `tactical.patch.perk-ids.commander-fame.transpiler`, `tactical.patch.perk-ids.cold-steel-morale.transpiler`, each followed by `[once:perk-ids:il:<method>] [Patch:PerkIds] <method>: perk-id sites rewritten`. Fail: `IL pattern not found`, `transpiler failed`, or any `InvalidProgramException`.
2. Campaign: `[first-fire] campaign.patch.perk-ids.fog-sight.prefix` / `.finalizer`, `campaign.patch.perk-ids.fog-concealment`, `campaign.patch.perk-ids.group-perks.prefix` / `.finalizer`, `campaign.patch.perk-ids.sea-raiders.prefix` / `.finalizer`. No `[Patch:PerkIds] … failed`, `perks restore failed`, or `[Patch:PerkIds] FogOfWar.units / FOWUnit.reg not found` (the fog-sight fix stood down).
3. Battle: `[first-fire] tactical.patch.perk-ids.hq.ability-to-run`, `.hq.engineers-init`, `.hq.fatigue.prefix` / `.finalizer` (`.hq.cover.*`, `.hq.engineers-choose` situational).
4. Behaviour: an Ambulance Corps corps' fog reveal circle does not grow with the perk level; its regiments' wounded recover with fewer deaths out of battle; a Foot Cavalry corps is faster only on forced march; a Sea Raiders fleet is not faster; fleets' Blockading Squadron / Sea Raiders bars do not fill from sailing; battle HQs with Balloon Corps / Land Torpedoes do not run; Expert Scouts cavalry keep their sight and concealment; a corps under an army with its own Embedded Reporters logs `[once:perk-ids:fame-self]` after a battle.
5. Perf: PerformanceAggregate rows for `campaign.patch.perk-ids.regiment-move.*` stay far below the 2 ms threshold.
6. Rollback: with `Fix Perk Id Mismatches` and `Fix Perk Effect Scope` off, none of the `[once:perk-ids:*]` behaviour lines appear (the first-fire lines still do).
