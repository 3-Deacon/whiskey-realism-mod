# Perk Selection Steering Design

## Scope

Implement patch #7, `PerkSelectionPatch`, for campaign-level AI army-group and fleet perks. This does not touch battle-only single-brigade perk selection.

## Vanilla Behavior Verified

`AICampaign.CheckPerkSelection(int)` at decompile line 11871 loops over `aifaction[_aifaction].ownunits` and `ownfleets`. For each eligible AI army or fleet it calls `ChoosePerk(Random.Range(...))`. Army groups are skipped when they are under the player's W&L command; fleets are not W&L-gated. `Regiment.ChoosePerk(int)` keeps the important vanilla constraints: it only fills an available perk slot, rejects duplicate perks, resets perk experience, and applies any vanilla side effects.

## Design

Use a Prefix on `AICampaign.CheckPerkSelection(int)` that mirrors vanilla eligibility and then returns `false`. The patch still delegates final assignment to `Regiment.ChoosePerk(int)`, so it does not write the `perks` list directly.

Perk choice is driven by a pure scorer:

- Army groups favor role and theater intent: siege/fort pressure, maneuver, recovery, scouting, raid, river, and capital defense.
- Fleets favor faction grand strategy: Union blockade/amphibious/river pressure; CSA raiding, blockade-running, port defense, and torpedo/battery-running asymmetry.
- If no usable candidate exists, the patch does nothing and leaves the unit for a later vanilla-equivalent pass.

## Safety

The patch must not run for disabled plugin state, player-CIC factions, invalid AI faction indexes, missing `aifaction`, missing tooltip arrays, or W&L player-subordinate army groups. Reflection failures are one-time warnings. Logging is bounded: one first-fire marker and one replacement line per changed signature.

## Acceptance

- Console tests prove Union fleet scoring prefers blockade, CSA fleet scoring prefers raiding/blockade-running, siege-role army scoring prefers siege/sapper perks, raid-role army scoring prefers raid perks, and unavailable/duplicate candidates are skipped.
- `./build.sh` succeeds.
- Deployed DLL hash matches `dist/WhiskeyRealism.dll`.

## Result

Implemented on `main` in commit `2ccc743`. Console tests and `./build.sh` passed. `dist/WhiskeyRealism.dll` and the deployed BepInEx plugin DLL matched SHA-256 `5852e56aaa613aa636767fb96d75546f3ef4ee8ed1b99c016aff2a16483ec29b`. Runtime first-fire smoke is still pending.
