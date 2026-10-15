# Battle Core — Snapshot, Perception, Ownership, Order Arbiter — Design

**Status:** approved direction (user, 2026-10-15: "fix ALL of it", everything default on, no dead code). Roadmap stage D (`docs/superpowers/plans/2026-10-15-full-overhaul-roadmap.md`). Vanilla references: `docs/vanilla-vision-and-movement.md` (vision/movement, R1-R5), `docs/wl-player-model.md` (W&L). Absorbs §A-§C of `2026-10-14-wl-chain-of-command-design.md` (battle side); that spec keeps §D-§F (campaign dispatches, compliance, CIC bug).

## Problems this fixes (from the 2026-10-14/15 reviews)

1. Every doctrine reads vanilla battle state itself: the enemy-strength builder runs up to 4× per side per tick, roster walks repeat, allocation churn feeds Boehm-GC hitches.
2. Perception is omniscient: AI `enemyinrangereg`/`enemyinfirerangereg` are range-only (122650/122699), slice/flank pressure and hotspots have no fog at all (122719-122733, 7975), `EnemyUnits`/play centroids use every enemy's true position. Vanilla already paints a real line-of-sight fog grid for the AI side every frame (`fows[1]`, 100675-100714, raycast 101098) and throws it away.
3. W&L ownership is decided four different ways (`TacticalWriteGuard.IsPlayerProtected`, `PlayerCommandProbeFilter`, per-patch checks, #46 guard) and protects the player's ancestors, so Whiskey never commands the player's brigade/division/corps.
4. Eight systems move or re-stance the same groups (#61 executor, plays, division play, B8 fallback, artillery applier/veto/B7, #59 reserves, #46 replay, #56) with separate guards, dedupe, cooldowns and waypoint handling; mod-generated waypoints can land in water/off-map (R4); `SetWaypoint` silently drops orders (91300-91362) and nobody notices.

## Architecture

```
per doctrine tick (TacticalDoctrineClock), main thread, per AI-run side:
  BattleSnapshotBuilder.Build(battle, side) ─► BattleSnapshot (pooled, immutable for the tick)
      ├─ own units, command tree (vanilla allattachedunits parent map), ownership flags
      ├─ enemy observations: Visible (fog grid) | Remembered (last-seen, decaying) 
      ├─ derived: front axis + bands, slice/flank pressure from VISIBLE enemies
  public terrain (outside the snapshot, cached per battle): BattleTerrainGrid (plan BC10) and TerrainPicture (plan BC12)
  doctrine (orchestrator, ledgers, executors, plays, artillery, reserves, fallback) reads BattleSnapshot only
      └─ submits BattleOrderIntent(s) to OrderArbiter
  OrderArbiter.Resolve(snapshot) ─► per group: one winning intent per channel
      └─ OrderExecutor: guard → chain target resolution → waypoint validation → vanilla call → outcome → W&L dispatch
```

### 1. `BattleSnapshot` (pure data) + `BattleSnapshotBuilder` (runtime)

- One roster pass over `bunits.completeunitlist` per tick per side, into pooled struct arrays (`UnitRecord`: instance id, unittyp, alliance, position, rotation, strength, `groupstrengthdirect`, morale/battlestartmorale, ammo (T1 rule), alive/active/routed/onretreat/inmelee, `regimentpaths`, stance, formation, command-parent id, group id, flags). No LINQ, no closures, no per-tick `new`; harness allocation test (same method as WS P) asserts zero bytes after warm-up.
- Command tree from the vanilla parent map (child → parent via `allattachedunits`/`GetAttachedUnitsReg`), never `transform.parent`.
- Snapshot is the only source for T1 evidence (`ArmyEvidenceBuilder`, `EnemyVisibleState`, `TacticalFrontAxis`), T2 operations director, T4 posture inputs, plays (`PlayExecutorRuntime` centroids), threat computer, reserve gate, B8, artillery doctrine. Their direct vanilla reads are deleted.
- Built once per tick even when several consumers need it (fixes the 3-4× rebuild).

### 2. Perception (`BattlePerception`, pure memory + runtime visibility probe)

- **Visible** = within the observing side's painted fog: for an AI-run side, `!ownUnit.fow.UnitIsHidden(enemy.GetPosition(), cunning(enemy.commander), enemy.strength/unittypesize[enemy.unittyp], enemy.liedown, enemy.coverobject, enemy.covervalue, enemy.unittyp, enemy)` — the exact arguments vanilla uses at 127225 (R1); `Regiment.fow` is the unit's own side grid (116505). One probe per enemy per tick (O(1) grid lookups, no raycasts). Melee always visible (127216). Commander index bounds-checked.
- **Fallback (required):** when `!GameVars.usefow`, or battle `hqinthesaddle` (101312 blanks the AI grid), or the grid has not painted yet (first `FogStartupSeconds` = 5 battle-seconds, 100785 transient), fall back to vanilla's range-only rule and tag the snapshot `PerceptionMode.RangeOnly` (telemetry).
- **Memory:** a hidden enemy stays as `Remembered` with last-seen position/strength/time; confidence decays with doctrine time (half-life `RememberedHalfLifeMinutes` = 20 battle-minutes) and is dropped after `RememberedMaxMinutes` = 90. Vanilla's 5-cycle hide hysteresis is mirrored with `HideConfirmTicks` = 2 doctrine ticks before a visible enemy becomes remembered.
- Slice/flank pressure, closest-enemy, enemy centroids and reinforcement estimates are recomputed from Visible (+ discounted Remembered) observations; vanilla `enemystrengthwithinangle`, `flankstrength`, `closestenemyunitfar*`, hotspot lists and `reinforcementarrivalswithin24hrs` are no longer read by doctrine. Enemy reinforcements count only when their entry point is visible (T1 rule).
- W&L own-side AI (player alliance, `ai_vs_ai`): the side's grid is `fows[0]`; visible = `!enemy.iswithinfog` (127225 semantics), same memory model.
- The player's own fog and vanilla AI decisions are untouched.

### 3. Ownership (`PlayerOwnership`, pure over snapshot flags)

Computed once per tick into `UnitRecord.Flags`:
- `PlayerOwned` = `dlcw_isundercommander` (player's command + descendants, take-overs, CIC/army-group cases per 91168) or DLC take-over, or player-alliance unit outside W&L when not `ai_vs_ai`.
- `AncestorOfPlayerCommand` = the player's current command is a descendant in the vanilla command tree (vanilla's own ancestry test 8272/91203).
- `PlayerCommandDetached` = the player's command has `permanentlydetached`.
- Feud instance (`isplayeraiorfeud == 2`) and feuding units are never commanded directly. Feud is the vanilla AI group's state: `ActivateFeud` (116613) sets `ai_feudstance >= 0` on the group and its `AddUnitToPool(overwriteaigroups: true)` points every descendant's `groupaiobject` at it (7637-7648); the micro AI tests `aigroup.ai_feudstance` per unit (4917/4922, 3789/3834/3890/3928). So a unit is feuding when it, its AI group (`groupaiobject`) or a command ancestor feuds (`PlayerOwnership.ApplyFeudGroups` on the snapshot, `DirectFeudFlags` in `TacticalWriteGuard`'s Regiment overloads); ancestors of a feuding group are not. No Whiskey write targets these units, and Whiskey's own per-descendant loops skip them; only #42's inline `FeudReplay` (vanilla's own feud move) is exempt. A Whiskey order to an AI parent group still reaches feuding descendants through vanilla's own cascades (`SetGroupFormation` 91988-91999, `PerformActionForGroupForAllUnitsOnLevel` 92572-92628), exactly as vanilla's own AI group orders do (integration verify 2026-10-15, F5: W&L behaves as vanilla allows).
`TacticalWriteGuard` becomes a thin reader of these flags (ancestors are NOT protected); `PlayerCommandProbeFilter` and per-patch W&L checks are deleted.

### 4. `OrderArbiter` + `OrderExecutor`

- `BattleOrderIntent { int GroupId; OrderChannel Channel (Move | Stance | Formation | Facing | CombatBehavior | Stop); Vector3 Target; float Heading; int Stance/Formation/Behavior; IntentPriority Priority; IntentSource Source; string Reason; bool UseOrderDelay; }`.
- Priority ladder (highest wins per group per channel; ties → earlier tick's winner keeps it, hysteresis): `Safety` (B8 fallback/withdrawal, artillery retreat, charge demotion) > `Executor` (#61 posture tasks) > `Play` (play executor, division play) > `Support` (#59 reserve release, #46 objective-chain replay, artillery placement) > `Scorer` (stance scorer, facing pulse). Macro AI (`macroai`, side level) stays with #44 and is not an arbiter channel.
- Executor pipeline per winning intent: (a) `TacticalWriteGuard` (mode Active, not PlayerOwned, not feud); (b) **chain resolution** (§A/B of the chain-of-command spec): a group that is `AncestorOfPlayerCommand` with the player's command not detached is not ordered as a group — the intent fans out to its direct child groups except the one containing the player; (c) **waypoint validation** (R4): clamp to [1, size−2] (131057), step back from terrain 4/8 with `BattlefieldSetup.CheckIfFinalWaypointIsOnTerrain(start, target, 4)` and `CheckTerrainLine`, skip targets inside a friendly group's footprint unless the intent is a relief/support move (friendly path-blocking rule), reject artillery fire positions whose line to the target passes through a friendly group within `FriendlyFireClearanceMeters` = 40 (friendly-fire rule); (d) pre-check vanilla's silent exits (`onretreat || isrouted` 91362, garrison 91331, campaign readiness 91338, W&L control 91300) and record `Skipped(reason)` instead of calling; (e) dedupe — skip when the group's current order already matches (target within `DedupeMeters` = 60 m, same stance/formation); (f) vanilla call with courier-delay parity (`useorderdelay` as vanilla uses it; in-contact withdrawal immediate like 3643/6235); (g) outcome next tick: `regimentpaths > 0` → Moved, else Failed → one retry on a rotated bearing (±30°), then give up and report to the source; (h) **W&L dispatch** (§C): when the ordered group is an ancestor of the player's command, call `AIBattle.CheckCurrentOrderUpdate(ancestor, type, pos, …, calledfromcampaign:false)` with `TacticalDispatchTypeMapper` (types 0/1/2/3/4/11/12 only; never 13/14/15).
- `TacticalStanceWriter` becomes the Stance channel executor (keeps the stance-ownership/carve-out logic from WS T4); `TacticalCombatBehaviorWriter` the CombatBehavior executor (deleted if no source remains).
- NavMesh probes (#61 path quality) set battle area costs explicitly first (Roads/Bridges 1, Fences 2, Rivers 500000) or use `CheckTerrainLine` (R5).
- Per-group cooldowns, the 0.25 h courier gate, the 30 s recent-order and 12 s task-hold gates now live only in the arbiter (expressed on the doctrine clock).

### 4b. Infantry charge doctrine (vanilla gap)

Vanilla AI infantry never charges on its own: `MicroAICheckForCharges` (4905/4910, sole caller 5647) compares a GameObject to a Regiment and never proceeds, and `Regiment.UpdateChargeTarget` (122214-122216) only enters charge mode 3 when `combatbehavior == 3`, which the micro AI never sets (5617-5638 sets 1/2; only the player UI sets 3 at 203769/167621). AI infantry reaches melee only by contact (`MoveIntoMelee` 125990 → 126053); cavalry charges via `AdjustCavalryBehavior` (5036/5056). Whiskey adds a `ChargeDoctrine` (pure) feeding the arbiter's CombatBehavior channel: an AI infantry group in assault stance sets `combatbehavior = 3` for its leading regiments when the target is visible, within charge distance (vanilla's own charge range constants — read them), the target's morale/cohesion is below ours by a margin or it is disordered/flanked, our fatigue and morale are above floors, and no friendly unit blocks the lane; it reverts to vanilla's behavior after the charge resolves or the conditions fail. Priority `Executor`; charge denial (#45 demotion) stays `Safety`. W&L: never on player-owned units. Tests: condition matrix, revert, denial precedence.

### 5. Migration (no dead code)

Each mover is rewritten to submit intents; its private guard/dedupe/cooldown/waypoint code is deleted: `BattleCommandPostureExecutorPatch`/`CommandPostureExecutor` (incl. the `transform.parent` leaf target at :2946 → command-parent map), `PlayExecutorRuntime`, division play, `B8CheckLineFallbacksObserverPatch` fallback, `TacticalArtilleryApplier`, `B7CheckAIBombardmentPatch`, `ArtillerySetWaypointVetoPatch` (becomes an arbiter Safety intent + validation; its SetWaypoint Prefix is removed if the arbiter covers it), #59 `TacticalReserveCommitGate` release, #46 replay, #56 `TacticalReserveOrderDelayGuardPatch`. Every remaining Prefix-skip or snapshot/restore battle patch is listed in the plan with keep/replace and the vanilla reason; target: only patches that steer vanilla's own inputs remain.

### 6. Follow-up slices (BC10-BC13, plan § BC10-BC12 and § BC13)

The battle core as built (BC0-BC9) still left findings from the 2026-10-15 deployment, flanking and terrain research. They are planned as three slices on the same write path (producers submit intents; the executor writes; terrain is public, enemies only as perceived; a Defender never gets attack doctrine):
- **BC10, deployment and movement honesty:** a per-battle `BattleTerrainGrid` over vanilla's 1 m terrain-id grid; covered crossings and outflanking computed from perceived enemies; a non-mutating NavMesh probe with honest road, bridge and dead-ground scores; road march routing around woods and swamp (march-column groups, one route plan per side per arbiter tick, at most 6 000 lattice cells); fog-honest AI deployment (at a `RangeOnly` deployment, which includes every day-1 deployment, the assumed enemy is the nearest enemy-owned entry point, never a range-list observation) and a deployment reserve echelon whose reserve role the allocator reads from the executor's recorded deployment outcomes (#60, now through the executor, acting only on groups vanilla moved in that call and never splitting the W&L player's command from its parent); an arrival guard against vanilla's entry re-path, sized by smoke first; honest avenue flags (the two vanilla setup bugs the research found shipped on `main` as #98 and #99).
- **BC11, flank response and envelopment:** facing toward the most-pressured perceived side; the refused flank reaching vanilla (wheel out of combat, in-place wing rotation in combat); a reserve released to a threatened wing; envelopment plays executed as timed flank marches that face inward and attack, with vanilla `FlankMove`'s river and high-ground checks, only for rects marked `Envelops` (the other wings stay role assignments, their offsets start delays); the play anchor offers direct children only; vanilla's `flanksthreated` tally fixed, with its effect on vanilla's own flank moves measured.
- **BC12, terrain picture:** crests, cover lines, river lines, crossings, junctions and towns built once per battle; objective types and terrain strength fed to the planners' terrain branches; defensive terrain lines and anchored flanks; terrain in enemy-intent inference; crest-based `ReverseSlopeDefense` and a `LineHold` label where the held line is not on high ground; the operation director's terrain sign set by stance (an attacker avoids strong enemy-held ground, a defender seeks strong own ground). The picture is built in time-sliced steps of at most 4 ms each.
- **BC13, battle AI level of detail:** each own group gets a tick tier from perceived and own state only: a group more than 1.5 km from every perceived enemy, settled, on a static task and with no pending order re-evaluates every fourth doctrine tick (0.06 battle-h); every other group, and any group whose perceived picture changes within 3 km, evaluates on every tick, promoted in the same tick the change enters perception. The arbiter carries a skipped group's claims forward (published, never re-executed), so D4 refusal, hysteresis and retries behave as at full rate; all intervals are on the doctrine clock.

## Config (all default ON)

`Enable Battle Fog-Honest Perception` (true; off = range-only fallback), `Enable Battle Order Arbiter` is not a flag — the arbiter is the only write path; rollback stays `Tactical Commander Mode = MonitorOnly/Off`. Removed: `EnableFriendlyFireDetection`, `EnableFriendlyPathBlockingGate`, `EnableRegimentalIntelligenceWrites` (replaced by always-on arbiter rules). New tunables are named constants pinned by tests, not config, unless a user-facing knob is needed.

## Testing

Pure, harness-tested: snapshot builder transforms over fixture rosters (incl. allocation test), perception memory (visible → hidden → remembered decay → dropped; fallback modes), slice/flank pressure from observations, ownership flags (brigade/division/corps/army/CIC player; take-over; feud; non-W&L player side), arbiter priority/hysteresis/dedupe, chain resolution (player detached / not detached / at root / two levels down), waypoint validation decisions (bounds, water, friendly footprint, friendly-fire line), silent-exit pre-checks, outcome/retry, dispatch type mapping. Runtime verified by build + smoke checklist (every patch first-fire; `PerceptionMode` telemetry; no `Failed` storms; W&L brigade career: player never moved, dispatches appear).

## Performance budget

Snapshot build ≤ 1.5 ms per side per tick at 20× on a 300-regiment battle; zero steady-state allocations; arbiter ≤ 0.5 ms. Scopes: `tactical.snapshot.build`, `tactical.perception`, `tactical.arbiter.resolve`, `tactical.arbiter.execute`.
