# Vanilla Hotfix-Parity Bugs

This queue records old Community Hotfix/QOL claims that still mapped to narrow current-vanilla bugs after checking `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`. Stale or fixed claims are not carried here.

| ID | Status | Failure mode | Vanilla / patch anchor | Evidence | Current action |
|---|---|---|---|---|---|
| `BUG-CMD-001` | In progress | Assigning an officer to a new command can leave their previous unit still pointing at the same commander. | Vanilla `GameVars.Commander.AssignCommando` starts at 60343; #30 `CommanderAssignmentPreviousCommandPatch` owns the guard. | Vanilla clears the target unit's old commander but does not clear `this.currentcommand` before assigning the new command at 60380-60390. | Build/deploy/hash verified in DLL `7da618bf...`; fresh-launch smoke pending. |
| `BUG-FLT-001` | In progress | AI fleets can stay unavailable in patrol-loop order state. | Vanilla `AICampaign.CheckFleetMovements` starts at 13148; `Regiment.StopRegiment` patrol restore branch is at 132392; #33 owns the guard. | AI fleet movement only selects `fleetorders == 0`, while stopped patrol fleets restore waypoints instead of returning to idle. | Build/deploy/hash verified in DLL `7da618bf...`; fresh-launch smoke pending. |
| `BUG-BAT-001` | In progress | Combining artillery units can transfer crews without preserving the source unit's organic guns on the target. | Vanilla `BattleUnits.CombineUnits` starts at 93153; #34 owns the guard. | Vanilla returns source guns to weapon stock and re-equips through `unitto.weapon`, but never increments `unitto.guns`. | Build/deploy/hash verified in DLL `7da618bf...`; fresh-launch smoke pending. |

## Smoke Boundary

- Look for first-fire markers: `[Patch:CommanderAssignment]`, `[Patch:FleetPatrol]`, and `[Patch:ArtilleryCombine]`.
- These patches are conditional; silence is acceptable if the relevant vanilla action does not occur during the smoke run.
- Any repeated warning from these patch keys is a regression and should be investigated before marking shipped.
