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
- `Patches/ArmyAreaTheaterPatch.cs`

The coordinator now builds `ArmyAreas[alliance]` monthly next to `Fronts[alliance]`. The new patch runs after vanilla `UpdateCampaignTheaters` and, only for idle AI top-level strategic formations, nudges out-of-area units back toward their historical operating-area anchor using vanilla `AICampaign.MoveUnitTo`.

## Logging

- `[once:army-area]` confirms the patch first-fired.
- `[ArmyArea] alliance=...` logs only when the monthly assignment signature changes or verbose logging is enabled.
- `[Patch:ArmyArea] alliance=... unit=... action=return-area area=... reason=...` logs once per unit/area correction.

## Verification

- `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` passes.
- `./build.sh` passes with 0 warnings / 0 errors.
- DLL deployed to `<GTCW>/BepInEx/plugins/WhiskeyRealism.dll` and verified by SHA-256: `5c377369bf8a0b03c61c88c1dbd8a823f29ed97815355b88063abe98d6930f59`.
- Runtime smoke still needs a GTCW restart and a campaign AI tick that hits `UpdateCampaignTheaters`.

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
