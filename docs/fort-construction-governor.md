# Fort Construction Governor

Living note for #27 `FortConstructionGovernorPatch`. Source of truth remains shipped code and `docs/patch-catalog.md`; this file explains the vanilla fort-spam cause and the current guard.

## Vanilla Behavior

Vanilla fort construction lives in `AICampaign.CheckFortConstruction(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:16347`.

The method is called from `AICampaign.UpdateUnitAI()` job 21. It scans `fortconstructionsites` for the current faction/unit and creates a nested `AICampaign.FortConstructionOrder` when a site passes vanilla gates.

Confirmed vanilla gates:

- `FortConstructionOrder.IsFactionAlreadyConstructingFort(allianceid)` allows only one active fort order per faction.
- The current unit must be idle enough, supplied enough, not retreating, not in battle, not in offensive/defensive/supply-depot operations, not a raid force, and not already in a fort-construction order.
- Candidate sites must be close enough to the unit, close enough to the frontline, on the unit's side of the front, and not near an existing fort by `GamePrefs.maxdistancetootherfort`.
- Capital-defense units are treated specially: vanilla uses `GamePrefs.maxdistancetootherfort * 0.6f` for the existing-fort spacing check, so defenders around Washington/Richmond can build denser fort clusters than normal units.

The missing vanilla guard is area memory. Once the one active order finishes, the next pass can start another nearby valid static site. Over a long campaign this can create dense local rings, especially around capitals where multiple defenders sit near many valid sites.

## Whiskey Guard

#27 patches `AICampaign.CheckFortConstruction` with a Prefix/Postfix/Finalizer wrapper. It does not create forts and does not replace vanilla construction. It temporarily filters `AICampaign.fortconstructionsites` for the current call, then restores the original list.

The pure decision lives in `Strategic/Construction/FortConstructionGovernor.cs`:

- normal local area soft cap: 2 forts/orders;
- normal local area hard cap: 4 forts/orders;
- capital area soft cap: 4 forts/orders;
- capital area hard cap: 7 forts/orders;
- threat threshold: `enemyStrength / friendlyStrength >= 0.35`.

Behavior:

- If a local area is at or above soft cap and local threat is low, the site is hidden from vanilla for that call.
- If local threat is high, vanilla may continue until the hard cap.
- Hard cap blocks even under threat.
- Capital areas get higher caps, but still cannot grow indefinitely.

## Boundaries

Preserved vanilla ownership:

- unit eligibility;
- one-active-order-per-faction rule;
- movement to the fort site;
- `AIBattle.CheckCurrentOrderUpdate` order emission;
- `CBuilding.AddConstructionWish`;
- final `CBuilding.WorkDownConstructionWishes` placement search;
- construction cost/timer/building side effects.

Whiskey only adds a site-saturation policy before vanilla sees the candidate list.

## Runtime Proof

Expected first/suppression logs after a fresh restart:

```text
FortConstructionGovernorPatch wired (CheckFortConstruction site filter)
[Patch:FortGovernor] alliance=<0|1> reason=<saturated-low-threat|hard-cap> forts=<n> orders=<n> soft=<n> hard=<n> threat=<ratio> nearCapital=<bool>
```

Suppression logs are bounded to avoid spam. No suppression line is expected if no saturated site is presented to vanilla.

Current deployed DLL with #27: `82cb336603df08b3879b3a9873dcb13f48cad6b92ae830dd2bad2e4bb9dfec04`.
