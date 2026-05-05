# Historical Army Areas Implementation Notes

**Goal:** Make campaign armies behave in their historical operating areas while preserving vanilla's existing campaign army/corps/division machinery.

## Findings

- Vanilla campaign formations are `Regiment` objects with higher `unittyp`, not separate Army/Corps/Division classes.
- Campaign hierarchy uses `unittyp`: 13 Regiment, 14 Brigade, 15 Division, 16 Corps, 17 Fleet, 18 Army/ArmyGroup command layer in W&L data.
- Vanilla already uses `Regiment.theaterposition` and `AICampaign.IsWithinOperationsTheater` to limit where a formation operates.
- `AICampaign.UpdateCampaignTheaters` sets `theaterposition`; `CheckOffensiveMovements`, `CheckForDefensiveOperations`, `CheckTransferOfUnits`, and `CheckArmyGroupManagement` consume it downstream.
- Game data already has historical army names in `Config/armynames0.dat` and `Config/armynames1.dat`.
- W&L scenario `002/A` has reliable eastern coordinate anchors in `Save/IIPsTowns.dat` for Washington, Richmond, Shenandoah, B&O, Maryland/Pennsylvania, and Virginia coast corridors.
- Western/Gulf coordinate anchors are incomplete in the W&L save inspected; use state/objective IDs for doctrine now and populate runtime coordinates later from a loaded full-map campaign.

## Implemented

- `Strategic/ArmyAreaDoctrine.cs`
- `Strategic/HistoricalArmyAreaRegistry.cs`
- `Strategic/ArmyAreaLedger.cs`
- `Strategic/ArmyAreaRuntime.cs`
- `Strategic/ArmyGroupDoctrine.cs`
- `Patches/ArmyAreaTheaterPatch.cs`
- `Patches/ArmyGroupManagementPatch.cs`

The coordinator now builds `ArmyAreas[alliance]` weekly next to `Fronts[alliance]`. The new patch runs after vanilla `UpdateCampaignTheaters` and, only for idle AI top-level strategic formations, nudges out-of-area units back toward their historical operating-area anchor using vanilla `AICampaign.MoveUnitTo`.

`ArmyGroupManagementPatch` runs after vanilla `CheckArmyGroupManagement`. It groups committed top formations by `ArmyAreaDoctrine.PrimaryAreaKey`, lets vanilla's own pass run first, then uses vanilla `ArmyGroup.AddUnit`, `ArmyGroup.CreateNewArmyGroup`, and `ArmyGroup.AppointCommander` to attach/create historically coherent operating commands and appoint preferred commanders when they are already part of that command or currently unassigned.

## Logging

- `[once:army-area]` confirms the patch first-fired.
- `[ArmyArea] alliance=...` logs only when the weekly assignment signature changes or verbose logging is enabled.
- `[Patch:ArmyArea] alliance=... unit=... action=return-area area=... reason=...` logs once per unit/area correction.
- `[once:armygroup]` confirms the army-group steering patch first-fired.
- `[Patch:ArmyGroup] alliance=... area=... action=create|attach|appoint ...` logs only on concrete hierarchy/commander changes.

## Verification

- `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` passes.
- `./build.sh` passes with 0 warnings / 0 errors.
- DLL deployed to `<GTCW>/BepInEx/plugins/WhiskeyRealism.dll` and verified by SHA-256: `f0b0bdc853d55e4230a876cd98b5dd783f8a0531ed1d500740c46313564c8de1`.
- Follow-up army-group steering DLL deployed and verified by SHA-256: `1602c0ca07f9b0c11d12fd4f9ed0117cc7a2ff882f73af3b38aff2e9275d9246`.
- Runtime smoke was later confirmed in the 2026-05-04 v0.2.2 run: `[once:army-area]`, `[once:armygroup]`, `[ArmyArea]`, and `[FormationDirective]` all appeared after vanilla AI initialized. Return-area and army-group create/attach lines remain conditional on in-game state.

## Next

- Populate `ObjectiveAdapter` with the concrete W&L objective IDs and coordinate anchors:
  - 3 Richmond
  - 4 Washington
  - 17 Mississippi River
  - 29 West Virginia Union
  - 30 West Virginia CSA
  - 31 Shenandoah Valley
  - 32 B&O lines
  - 33 Maryland
  - 34 Pennsylvania
  - 35 Coastal NC
  - 36 Saltville
  - 37 Norfolk / Portsmouth / Suffolk
- Add full-map runtime coordinate capture for Vicksburg, Memphis, Baton Rouge, New Orleans, Louisville, St. Louis, Chattanooga, Atlanta, Nashville, and Corinth.
