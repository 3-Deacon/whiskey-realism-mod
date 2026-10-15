# Vanilla W&L Career Bugs

Whiskey & Lemons career defects found while mapping the player model (`../wl-player-model.md`, build 14703111). Campaign AI or dispatch behavior that changes when a career is active.

## Findings

| ID | Status | Failure mode | Vanilla anchor | Evidence | Current action |
|---|---|---|---|---|---|
| `BUG-WL-001` | Confirmed (decompile); fix designed | When the W&L player is commander-in-chief, the enemy faction stops building ships and managing army groups. | `AICampaign.CheckShipConstruction` (11998) returns at 12012 on `DLC_WL.dlc_scenarioactive && DLC_WL.IsCommanderInChief()`; `AICampaign.CheckArmyGroupManagement` (17705) returns at 17723 on the same test. `IsCommanderInChief()` (47475) asks only whether the chosen commander is CIC of their own alliance; neither guard compares it with the faction being processed. Both run inside the per-faction job loop of `UpdateUnitAI` (job 12 at 11481, job 32 at 11583) for every faction. | Code-level. Vanilla already skips the CIC player's own faction correctly at 11620 (`IsCommanderInChief() && aifaction[currentfaction].allianceid == player alliance`), so the method-local guards only ever affect the enemy faction (alliance ≥ 2 is already excluded from ships at 12012). Not reproduced in-game. | Planned: chain-of-command spec `../superpowers/specs/2026-10-14-wl-chain-of-command-design.md` §F (Prefix/Finalizer pair that makes `IsCommanderInChief()` return false while a non-player faction is processed). Read both vanilla bodies fully before implementing. |
| `BUG-WL-002` | Confirmed (decompile); no fix | Completing a type-16 "Offensive" campaign dispatch pays no prestige, and the order is cleared anyway. | `Regiment.CheckDLCWLTopics` (123468) calls `DLC_WL.EarnPrestige(16, ...)` at 123513 and sets `flag` (123514), which clears `DLC_WL.givenorder` at 123533. `EarnPrestige` (46601) has no `case 16` in its switch (46627-46673, cases 0-10), so the gain stays 0 and it returns false at 46702 (`0 < dlcwl_prestigegain_minimumprestigetoshow`, default 1, 48652). Types 5/6/7 on the same path pay. | Code-level; matches `../wl-player-model.md` § Orders from superiors. Latent: `collectedprestige` has 11 slots (43305), so with that threshold configured ≤ 0 the write at 46727 (battle) or 46731 (campaign) would index 16 and throw. | None planned. A fix would have to pay type 16 through an existing gain (for example the type-5/6 offensive values) without indexing `collectedprestige[16]`. |

## Not Verified

- In-game reproduction of `BUG-WL-001` (a CIC career long enough for the enemy to need new ships or army groups).
- The shipped value of `dlcwl_prestigegain_minimumprestigetoshow` for `BUG-WL-002` (only the C# default at 48652 was read; the game install was rewritten by the Steam update).
