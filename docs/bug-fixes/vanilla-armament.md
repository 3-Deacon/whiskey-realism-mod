# Vanilla armament bugs

Small arms and cannon: autoresolve damage, unit merges, AI purchasing, the W&L federal weapon budget, and Whiskey's own arming need. Decompile `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`, Steam build 14703111. Config values from `<GTCW>/Config/` (`campaignprefs.txt`, `weapons.txt`, `dlcwl_config.dat`). Research record: the 2026-10-15 armament investigation (vanilla economy of arms, AI decisions, W&L, Whiskey use). Plan: [`../superpowers/plans/2026-10-15-arms-bugs.md`](../superpowers/plans/2026-10-15-arms-bugs.md).

Ids continue past the highest used on `main` (`BUG-AI-016`, `BUG-WL-015`, `BUG-BAT-001`). Every fix applies to every side the way vanilla's own rule does, the W&L player included.

| ID | Status | Area | Fix |
|---|---|---|---|
| `BUG-BAT-002` | In progress | Autoresolve | #105 `AutoresolveStandardGunPatch` (Transpiler, user-approved) + one-shot count |
| `BUG-BAT-003` | In progress | Unit merge | `ArtilleryCombineGunTransferPatch` (#34 file): Prefix snapshot + Postfix settle |
| `BUG-WL-016` | In progress | W&L weapon budget | #106 `WlWeaponBudgetPatch.OrderScope`: Prefix + Finalizer |
| `BUG-WL-017` | In progress | W&L weapon budget | #106 `WlWeaponBudgetPatch.PurchaseGate`: Prefix swap + Finalizer restore |
| `BUG-AI-017` | In progress | AI purchasing | #107 `AiWeaponPurchaseOrderablePatch`: Prefix mask + Finalizer restore |
| `BUG-AI-018` | In progress | AI weapon factor | #108 `GroupWeaponStrengthGuardPatch`: Postfix |
| `BUG-AI-019` | Recorded | Import cost | Left as vanilla (needs a Transpiler; intent of the Union branch not certain) |
| `BUG-AI-020` | Recorded | Weapon -1 index | Left as vanilla (not reachable on shipped data) |

## BUG-BAT-002 — autoresolve batteries with the 6-pounder deal no damage

- **Failure.** `Autocalc.FightUnit` (21050) starts the casualty weapon factor `num5` at 0 (21084) and multiplies it into the casualties (21169). It sets it only when `unit0.weapon > 0`: line units 21085, artillery 21089.
- **Who.** Weapon id 0 is the 6-pounder Field Gun (`weapons.txt` record 0). It is the standard artillery weapon of both sides in every shipped campaign (`nations.dat` `standardweapons`: grand campaign 42/43/0 for both, campaign 002 Union 19/33/0, CSA 42/43/0). Every new battery gets it (`CreateUnitDefault` 89508), and a merge's fallback re-arm hands it out (BUG-BAT-003). Until the AI upgrade loop or the player re-arms them, those batteries inflict no casualties in autoresolved land battles, raids and skirmishes (types 0/1/6). Siege overwrites `num5` (21141-21148).
- **Intent.** The sibling `EnvolvedUnits.GetFortWeaponStrength` tests `weapon >= 0` (18482). Elsewhere -1 is "no weapon" (alliance tick 81576, `CombineUnits` 93203/93220, `UpgradeWeaponForUnit`).
- **Fix.** A narrow fail-safe Transpiler, approved by the user on 2026-10-15 ("Approve (Recommended)" for the autoresolve 6-pounder fix). It matches `ldarg.1; ldfld Regiment::weapon; ldc.i4.0; ble(.s)` and requires exactly two sites. The `ldc.i4.0` becomes `call AutoresolveStandardGunPatch.WeaponFloor()`, which returns -1 with the flag on and 0 with it off. The flag is read on every call. On a count mismatch the IL stays vanilla and one warning is logged. The pattern helper is `Strategic/BugFixes/AutoresolveWeaponGateIl.cs`.
  - It composes with the perk-id Transpiler on the same method (`AutocalcPerkIdsPatch`, #74): the harness applies both in either order on the real IL and gets the same body.
  - ILVerify 8.0.0 replay: the combined edit (2 hook replacements + 4 perk remap inserts) on a Mono.Cecil copy of `Assembly-CSharp.dll`, calling the real hooks in `dist/WhiskeyRealism.dll`, verifies clean. A deliberately wrong hook (a `(Regiment, int) -> bool` method in place of `WeaponFloor`) fails with `StackUnderflow` at offset 0xDD. Tooling as in [`vanilla-perk-ids.md`](vanilla-perk-ids.md) and [`../findings.md`](../findings.md) § Perk id tables; include pattern `-i "Autocalc.FightUnit"`.
- **Diagnostic.** `AutoresolveStandardGunCountPatch` (Postfix on `Autocalc.StartLandBattle`, 22118) logs once per campaign (a `CampaignScope` reset), at the first autoresolved land battle, each side's battery count and how many carry weapon 0. Raids and skirmishes are not counted.
- **Flag.** `Campaign Bug Fixes / Enable Autoresolve Standard Gun Fix`.

## BUG-BAT-003 — unit merges leak guns into stock and downgrade the target

`BattleUnits.CombineUnits` (93153) moves weapons through `Alliance.weaponstock` (62244). That stock is a running total that is saved (68087), so errors persist. `weaponunitsequipped` is rebuilt every alliance tick (81520) and self-heals.

- **Source over-return.** The source puts `num5` back in stock (93211-93217). For artillery `num5` is its whole `statsguns` even when only part of the crew moves, so the guns it keeps are counted twice.
- **Target over-demand.** The target draws `num5` of its own model when `stock >= num5` (93222-93228). For artillery that is the source's whole gun count, not the guns the target gains, so a covered merge can fail the check.
- **Men returned as guns.** When the target's model is short, vanilla returns `num6 = sick + strength + losses[0]` (93232), which is men, to stock. It then re-arms the target with the free standard weapon (93240). Artillery stock counts guns: `DeleteUnit` 90631 and the tick 81586 use `statsguns`. So each such merge adds about 100-240 phantom guns of the target's model.
- **Standard target.** A target that already carries the standard weapon goes through the same branch. It adds its whole holding to stock and is "re-armed" with the same model, for nothing.
- **Callers.** AI merges of understrength brigades below 35 % (`CheckCombinationOfBrigades` 17615-17645). Player drag-merges (93117-93119).
- **Fix.** It lives in the #34 file because it is the same call and concern.
  - The Prefix snapshots both units' weapons and pieces (guns for batteries, men otherwise), the standard weapon and the stock cells of those models.
  - The Postfix runs after #34's gun sync. It sets those stock cells and the target's weapon to the pure `Strategic/BugFixes/CombineWeaponLedger.Settle` outcome:
    - only the pieces that change hands move: source gives `before - after`, target draws `after - before`;
    - the target keeps its model when the stock covers what it gains, when both units carry the same model, or when it already carries the standard weapon (new men get it free, as recruits do, 89508);
    - a real shortfall still re-arms it with the standard weapon (vanilla's rule, its own log text at 93239) and returns its guns, not its men.
  - Every early return of vanilla moves nothing and settles to the snapshot. A cross-alliance merge or an out-of-range id keeps vanilla's stock.
- **Not handled: a negative stock cell.** On a merge that moves nothing, a negative stock cell would make `Settle` see the gain (0) as uncovered, downgrade the target and clamp the cell to 0. This cannot happen: vanilla never makes a stock cell negative (31556-31559, 93226, 227121-227125; the upgrade draw at 16188 sits behind `GetBestWeapon`'s stock check). Left as is.
- **Flag.** `Campaign Bug Fixes / Enable Combine Weapon Stock Fix`. #34's gun sync keeps its own flag.

## BUG-WL-016 — every alliance's weapon orders spend the career player's budget

- **Failure.** `WeaponList.PlaceWeaponOrder` (227136) adds each order's cost (negative) to the static `DLC_WL.accumulatedweaponbudget` for any alliance (227157-227161), clamped at 0.
- **Whose budget.** The budget grows from the career player's alliance rating (`GetWeaponBudgetGrowthPerAnnum`, 47496-47518, called from the tick 81839). It gates the player's order screen (226965, player alliance). So enemy and European AI orders drained it.
- **Fix.** The Prefix snapshots the budget. The Finalizer restores it for any `alliance != GameVars.playeralliance`. Nothing else in the method writes the field, so the restore is exact.
- **Kept as vanilla.** Orders of the player's own alliance still spend the budget. That covers the AI ordering for his alliance under `ai_vs_ai` (`CheckPurchaseWeapons` 16263: non-CIC careers always, CIC with automanage on), which is the shared federal budget.
- **Flag.** `W&L / Enable W&L Weapon Budget Scope Fix`.

## BUG-WL-017 — CIC can buy weapon budget into negative prestige

- **Failure.** `WeaponList.IncreaseFiscalBudget` (226654) charges `AdjustPrestige(0 - cost, 2)`. With `cost = dlcwl_weaponbudgetprestigecost = -500` (`dlcwl_config.dat` 380), type 2 does `prestige -= amount` (43471-43473), which is prestige - 500. That charge is correct. The gate is `!(prestige < cost)` (226656), i.e. prestige >= -500.
- **Fix.** When vanilla's gate passes but the prestige is below `-cost`, the Prefix swaps the pref to `float.MaxValue` for the call (`ValueSwap`). Vanilla's own gate then refuses. The Finalizer restores the pref unconditionally. No skip; the charge stays vanilla's.
- **Flag.** `W&L / Enable W&L Weapon Budget Prestige Gate Fix`.

## BUG-AI-017 — AI orders models nobody produces (dead machine-gun exclusion)

- **Failure.** `AICampaign.CheckPurchaseWeapons` (16259) takes as candidates every model that is researched OR imported with no open order (16296, 16313), then orders one at random (16328, 16338).
- **The player's screen is stricter.** `AbleToOrder` (63938) → `GetAvailability` (63962) also needs a researched model to be produced (`productionmultiplier > 0`). Projects 10 and 11 research Gatling 37, Coffee Mill 38 and Williams 12 at pm 0 (`UpdateProjectEffects` 213041: 37/38 at 213091-213092, case 10; 12 at 213096, case 11). They hand those models out as one-shot stock (212945-212950; `machinegunprojectstock`).
- **Cost to the AI.** It orders them with availability floored at 0.01 (`GetDeliveryTime` 64000) and pays the whole order up front (227162) for up to two years of delivery. Gatling WSF 0.434 out-ranks the 3-inch rifle (0.348), so it is a frequent pick.
- **Intent.** The discarded `_ = j == 37 || j == 38;` at 16289 is the remains of this exclusion.
- **Fix.** The order screen's rule, computed for the ordering alliance (`Strategic/WeaponOrderability.cs`: researched and produced, or imported; vanilla's `GetAvailability` always reads the player alliance, 63965-63982, harmless for its callers).
  - The coordinator preferred a Prefix on `PlaceWeaponOrder` that swaps an unorderable id for vanilla's next candidate. `PlaceWeaponOrder` does not see vanilla's candidate lists (they are locals of `CheckPurchaseWeapons`). With no orderable candidate it would have to skip the order, which would need approval.
  - Instead the Prefix on `CheckPurchaseWeapons` hides the ordering alliance from the research list (`techavailableforalliance`) of each model it researched but cannot order (`Patches/ArmsSwaps.cs` `ListMemberMask`). The Finalizer restores the lists.
  - Vanilla's candidate lists, coverage count (stock counts with or without research), random pick and `Count > 0` gates run unchanged over orderable models. Nothing is skipped.
  - Units still receive the project stock: `UpgradeWeaponForUnit` reads stock, not research (`GetBestWeapon` 16224).
- **Reuse.** `WeaponOrderability` is shared with stage G's G9 purchase planner.
- **Flag.** `Campaign Bug Fixes / Enable AI Weapon Purchase Orderable Fix`.

## BUG-AI-018 — group weapon factor 0/0 = NaN

- **Failure.** `AICampaign.GetWeaponStrengthFactor(Regiment)` (16195) returns `num / num2` (16211) with no zero guard. A group whose armed basic units have no men left gets NaN, and so does a group with no basic units (`allattachedunits` holds the HQ itself, 129574, which the `unittyp <= 13` test skips).
- **Effect.** NaN poisons the strength-weighted area means (13136-13143) and makes `CheckOffensiveMovements`' outgunned tests false (14257, 14283, 14299).
- **Fix.** A Postfix returns 0 for a NaN or infinite result: no armed men, no weapon strength.
- **Flag.** `Campaign Bug Fixes / Enable Group Weapon Strength Guard`.

## BUG-AI-019 — identical import-relations branches (recorded)

- **Failure.** `GameVars.Weapon.GetProductionFigures` (64074) computes the import relations factor as `(_alliance != 1) ? X : X` (64110) with identical branches: `X = (P(2) + P(3) - P(4)) / 3`, clamped to [0, 1], interpolated to the cost factor 1.5 → 0.7 (`weaponcostfactorrelations`, campaignprefs.txt 2075-2076). P(2)/P(3) are positive when Britain/France lean CSA and P(4) when Russia leans Union (`GetInterventionProbability` 62822-62870).
- **Effect.** The Union's import cost falls as Europe leans toward the CSA.
- **Intent.** A per-alliance branch was evidently meant (the W&L effect-34 adjustment at 62856-62864 flips the sign per side), but the Union's expression cannot be read from the code.
- **Why recorded.** The factor is a local inside the method, so a fix needs a Transpiler (not approved) or a re-implementation of the cost math. Left as vanilla. A fix would mirror the sign for alliance 0: `(P(4) - P(2) - P(3)) / 3`. It needs the user's approval of a Transpiler.

## BUG-AI-020 — weapon -1 index faults (recorded)

- **Unguarded reads.** These index arrays with `weapon` without the `>= 0` guard that sits next to them:
  - `GetWeaponStrengthFactor(unit.weapon)` in the alliance tick (81592, 81597);
  - `weaponstock[manualunit.weapon]` in `AssignWeapon` (227118; the equipped line 227127 is guarded);
  - `GetWeaponUpgradePrestigeCostGroup` (44004).
- **Reachability.** Only a basic unit with weapon -1 trips them.
  - Every created basic unit gets `standardweapons[unittyp]` (89508).
  - The new-campaign pass (`CheckWeaponUpgrades(isinitialization)` 16080-16151) sets weapon -1 and then restores the old weapon when no better one is in stock (16167-16170).
  - A thrown index in the half-day alliance tick would abort that alliance's purchases, upgrades and budget growth every tick. Nothing in the logs reported so far shows it.
- **Latent (recorded).** In that initialization pass the W&L removal loop (16137-16144) drops player-owned units from `list2` but not from the parallel old-weapon list. The removed units would keep weapon -1 and the later units would get misaligned old weapons. It runs at frame 32 of a new campaign, before the career's command is chosen (frame 50, `ShowStartUnitSelectionList` 182353). At that point no unit is `dlcw_isundercommander`, so it does not trigger.
- Left as vanilla.

## #14 and W&L automanage weapons (checked)

- **#14 does reach career creation.** `RealismCheckboxesLockPatch` (#14) forces `automanageweapons` on (`Patches/RealismCheckboxesLockPatch.cs` `SetAutomanage(true)`) from `MainMenu.CheckForCheckBoxUpdates` (193614). `MainMenu.Update` calls it every frame on the campaign setup screen, and W&L career creation starts from the same menu (`dlc_chosencommander == -2`, 29751), so a career starts with weapon automanage on. The value is saved (68345) and loaded (68862).
- **In a career it only matters for a CIC.** Vanilla's purchase gate (16263) skips the player alliance only when automanage is off and the player is not in a career, or is CIC. A non-CIC career's alliance is bought for regardless.
- **The player keeps vanilla's choice.** The weapon panel has its own automanage checkbox (`WeaponList.UpdateCheckBox` 227209-227212), never hidden, which writes the flag while the panel is open.
- **The one risk, now closed.** Vanilla syncs the menu boxes only at `currentoperation == 0` (193616), but #14's Postfix ran even when vanilla returned early. If a `MainMenu` ever updated during a running game it would re-force the flag over the player's panel choice. The Postfix now mirrors vanilla's guard (`SceneManagement.currentoperation > 0` → return).
- No other change: the default on matches the all-automanage setup the user chose for #14.

## Whiskey: Industry need (stage E follow-up, not a vanilla bug)

The stage E Industry need was `1 − Σ weaponstock / troopsinrecruitment` (`CampaignNeedBuilder.cs` 104-106). That is 0 whenever nothing recruits, and it counts muskets, siege guns and rifles as equal pieces. Recruits and replacements never draw per-model stock (`CreateUnitDefault` 89508; costs are generic goods, `Economy.GetUnitCost` 32665-32713).

The replacement is the own-side arming need, per lane, guns (artillery units) and other units, as the alliance tick splits them (81589-81605). Each lane's need is the larger of two gaps:

- **Coverage gap.** Vanilla's own purchase trigger: the equippable units are counted as in `CheckPurchaseWeapons` (16287-16312), and the gap is measured against `aiminnumberofequippableunits × units` (16325/16335; 0.15, campaignprefs.txt 1613).
- **Quality gap.** `1 − own average WSF / best orderable WSF`, using vanilla's WSF (16214).

How the lanes feed the signals:

- Industry, WeaponDeficit and IndustryGap take the worse lane.
- ArtilleryDeficit is the gun lane. Before, it was always 0.
- ArmsImport projects (0-4) scale by vanilla's blockade import factor, `1 → 0.1` (`GetProductionFigures` 64120-64124, campaignprefs.txt 2067).

Own alliance data only (fog-honest). Code:

- `Strategic/ArmsCoverage.cs` (pure);
- `CampaignNeedBuilder.LaneNeed` / `ArmsImportViability`;
- `CampaignNeedRuntime.ReadArms`;
- `ProjectDoctrineSignals.ArmsImportViability`.

## Smoke

Exact log lines are in the plan § Smoke. A `[Patch:AutoresolveGun] FightUnit weapon test found N times` warning means FightUnit runs vanilla (game update); report it.
