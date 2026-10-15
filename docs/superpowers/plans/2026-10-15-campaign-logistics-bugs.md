# Campaign Logistics Bug Fixes Plan

> Branch `fix/campaign-logistics-bugs` (worktree `.worktrees/campaign-logistics-bugs`), base `main` `a5cce64`. Bug entries, evidence and verdicts: [`../../bug-fixes/vanilla-campaign-logistics.md`](../../bug-fixes/vanilla-campaign-logistics.md) (`BUG-LOG-001`..`014`). Anchors: [`../../findings.md`](../../findings.md) § Campaign logistics anchors.

**Goal.** Fix the confirmed vanilla campaign-AI logistics bugs from the 2026-10-15 deploy/supply research (ranked items #2, #3, #5, #6, #7, #11) to vanilla's evident intent, and record the ones whose intent is ambiguous.

**User direction (2026-10-15):** "okay, fix all of this correctly", in line with vanilla's intent. Transpilers: the user approved both (`OffensiveWeaponTrimPatch`, `RandomCandidatePickPatch`) on 2026-10-15 via the coordinator: "Approve both (Recommended)". The coordinator accepted the extras beyond the brief (duplicate builders per depot, the three naval `Count - 1` sites, destroyed-building removal) and added two items: fix `BUG-LOG-002a` if embarkation has a gameplay effect (it does), and remove a destroyed unit's camp (`BUG-LOG-014`). Patch ordinals #81-#91, assigned by the integrator (#80 is BC9's `SkirmisherScreenDistancePatch`, which merges first). Verify round 1 (2026-10-15) integrator decisions: leave `BUG-LOG-005` as vanilla (capture-for-supply) and move every patch decision and restore into code the harness compiles.

## Constraints

- Prefix-snapshot / Finalizer-restore or Postfix wherever a vanilla input or result can carry the fix; a Transpiler only for a local (two: `OffensiveWeaponTrimPatch`, `RandomCandidatePickPatch`). Each Transpiler inserts a hook that reads its config flag live, and leaves the method as vanilla unless the IL pattern matches the expected number of times.
- One flag per coherent fix, section `Campaign Bug Fixes`, all default on, honest descriptions (config-audit 0).
- Every Prefix/Postfix/Finalizer in `TelemetryPerf.Scope("campaign.patch.<name>.<method>", ...)` with `PatchFirstFire.Mark` first; Transpiler hooks mark first fire so a smoke log shows the rewritten IL ran.
- W&L: the player's own command is never moved. Vanilla's own move guards (14765, 14381-14456, 14034/14052) are untouched; the raid fix skips raid groups under the player's command; the camp fix never touches player-alliance units (never fogged on the campaign map, 127211-127215). Every fix applies to all AI factions alike.
- Out of scope: depot placement (stage G G5c), the capital garrison loop (11742-11750) and the 13610 index mismatch (stage G spec C-items), fleet models in fog (naval stage).

## Items

| Item (research #) | Verdict | Patch / helper | Flag |
|---|---|---|---|
| #2 unreachable 0.75 supply threshold | Fixed (raid search); micro-move gate and 0.25 consumers recorded, left | #81 `RaidSupplyThresholdPatch` / `RaidSupplyThreshold` | `Enable Raid Supply Threshold Fix` |
| #3 fogged AI units never camp | Fixed | #82 `FoggedCampPatch` / `FoggedModelRule` | `Enable Fogged AI Camp Fix` |
| `BUG-LOG-002a` fogged AI units never embarked (same fog gate; the model is the embarked state: speed, night halt, transport capacity, consumption, embarkation delay, rations, reinforcements) | Fixed | #83 `FoggedEmbarkationPatch` (+ `SharedFiringSounds`) / `FoggedModelRule.ShouldTreatAsVisibleForEmbarkation`, `ShouldShareFiringSounds`, `VanillaRecomputedFog` | `Enable Fogged AI Embarkation Fix` |
| `BUG-LOG-014` destroyed unit leaves its camp on the map | Fixed (all units) | #84 `OrphanCampCleanupPatch` | `Enable Orphan Camp Cleanup` |
| #5 AI area membership frozen | Fixed | #85 `AIAreaMembershipRefreshPatch` / `AIAreaMembership` | `Enable AI Area Membership Refresh` |
| #6 winter offensive modifier unread | Fixed (vanilla + #38; only the roll's time term is scaled) | #86 `WinterOffensiveModifierPatch`, `CoordinatedOffensiveOperationsPatch.WinterModifier` / `WinterOperationsModifier` | `Enable Winter Offensive Modifier Fix` |
| #7a enemy depot as resupply source | Recorded, not a bug: capture-for-supply (14740 admits an undefended depot; frontline 87208-87250 → `CheckNewOwner` 95810 → `ChangeOwnership` keeps the stock 95402-95452 → `UpdateIIPSupplyLink` 114296-114306). The fix and its flag were removed after verify round 1 | — | — |
| #7b skip near any own-started depot | Fixed | #87 `SupplyDepotAiFixPatch.DepotJob` Prefix/Finalizer / `ShouldMaskFinishedOwnDepot` | `Enable Supply Depot Builder Fix` |
| #7c depot placement | Recorded, deferred to stage G G5c | — | — |
| #7d builder radius (+ duplicate builders, vanilla-supply B16) | Fixed | #87 `SupplyDepotAiFixPatch.DepotJob` Postfix / `SelectBuilders` | `Enable Supply Depot Builder Fix` |
| #11 `GrabSubordinateByStrength` accumulator | Fixed | #88 `SubordinateStrengthGrabPatch` / `SubordinateStrengthSelector` | `Enable Subordinate Strength Grab Fix` |
| #11 weapon factor squared on trim | Fixed (Transpiler, user-approved 2026-10-15) | #89 `OffensiveWeaponTrimPatch` / `OffensiveWeaponTrimIl` | `Enable Offensive Weapon Trim Fix` |
| #11 `Random.Range(0, Count - 1)` | Fixed (Transpiler, user-approved 2026-10-15), 8 sites in 7 methods (the research listed 5; the 3 naval ones are the same bug) | #90 `RandomCandidatePickPatch` / `RandomPickRangeIl` | `Enable AI Random Candidate Pick Fix` |
| #11 Confiscation factor inverted | Fixed | #91 `ConfiscationSupplyFlowPatch` / `ConfiscationSupplyFlow` | `Enable Confiscation Supply Flow Fix` |
| #11 owner-blind offensive objective | Recorded, not a bug (roll exemption for own-state objectives, 14336) | — | — |

## Verification commands

```bash
./build.sh                                                               # 0 warnings
dotnet build tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj      # 0 warnings
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj   # exit 0
python3 -I tools/config-audit.py .                                       # 0
python3 -I tools/orphan-sweep.py .                                       # 0
python3 -I tools/orphan-sweep.py . --include-test-only                   # 29 (= base)
python3 -I tools/test_smoke_check.py                                     # OK
```

## As built

- Pure helpers `src/WhiskeyRealism/Strategic/BugFixes/` (`RaidSupplyThreshold`, `FoggedModelRule`, `AIAreaMembership`, `WinterOperationsModifier`, `SupplyDepotAiRules`, `SubordinateStrengthSelector`, `OffensiveWeaponTrimIl`, `RandomPickRangeIl`, `ConfiscationSupplyFlow`), all in the test csproj. Patches in `src/WhiskeyRealism/Patches/` (eleven files, #81-#91) plus `LogisticsSwaps.cs` (no `[HarmonyPatch]`: `ValueSwap`, `UnitFogFlip`, `ModelActiveSync`, `ListContents`, `FieldMask`, `SharedArrayIndex`), which every patch saves, writes and restores through and which the test csproj compiles. Flags in `Plugin.cs` (eleven).
- Tests `tests/WhiskeyRealism.Tests/CampaignLogisticsFixTests.cs`, 28 tests (27 at `593db20`, + the embarkation rule, red first), written red first (the harness did not compile without the helpers). Both IL rewrites run against the real vanilla method bodies read from `refs/Assembly-CSharp.dll` (`PatchProcessor.GetOriginalInstructions`): exactly one trim site in `CheckOffensiveMovements`, none in `CalculateZoneRecruitingProbs`, 8 `Count - 1` sites across the 7 methods, and a count mismatch leaves the IL untouched. `GetGroupSupplyState`'s two `× 0.5` terms and every patched method's parameter names (Harmony binds by name) are pinned too.
- Mutation evidence (scratch copy, 18 plants, each turned the harness red, exit 134; the trim outer-op check survived the first run and was killed once the synthetic shape test was added): raid scale removed; camp water and player guards; area run boundary and duplicate check; winter offensive branch; depot friendly-source, finished-mask, bugle radius and dedupe; grab accumulator; trim left operand, trim outer-op pattern check and trim fail-safe; random offset and random fail-safe; confiscation reciprocal; then embarkation fleet and player guards (2 more, both red).
- Verify round 1 (2026-10-15) revisions: `BUG-LOG-005` left as vanilla (fix, `EnemyHeldDepot`, `IsFriendlySupplySource` and `Enable Supply Depot Friendly Source Fix` removed). Every Prefix/Finalizer decision and restore moved into harness-compiled code (`LogisticsSwaps`, `RaidSupplyThreshold.ShouldRescale`, `FoggedModelRule.VanillaRecomputedFog` / `ShouldShareFiringSounds`, `RandomPickRangeIl.Transpile`, `OffensiveWeaponTrimIl.Transpile`, after the `PerkSwaps.cs` precedent); wiring that cannot move is pinned in the sources by `CampaignLogisticsGlueTests` (18 tests: every Finalizer restores first inside a try, Prefixes write only through the swaps, decisions get vanilla's own state). Embarkation: no fog restore when vanilla re-ran `CheckRegimentVisibility` during the call (model removed); hidden AI transport models share one ship-firing sound array per `SoundEngine` (vanilla adds 20 never-freed sound objects per model; land transports never fire). Raid: the W&L guard is `ShouldRescale`; vanilla never raises raid forces for a CIC player's faction (job skip 11620-11628), so every faction vanilla raids for is covered. `RandomCandidatePickPatch` logs through static per-site keys; every Finalizer catches. Winter wording corrected (only the time term of the roll is scaled).
- Mutation re-run after the revisions (scratch copy, clean build per plant): the 9 surviving plants from the verify (raid scale and W&L guard, camp restore, camp player and water guards, embarkation restore and fleet guard, winter swap, depot mask restore, transpiler fail-safes), each planted both in the patch and in the helper where it now lives, plus embark-fleet, nested-swap and direct-camp-write plants: 18 plants, all red.
- Gates at the first code commit `593db20`: build 0 warnings / 0 errors; test build 0 warnings; harness exit 0, 1988 PASS (base 1961); config-audit 0 over 112 entries; orphan-sweep 0, `--include-test-only` 29 (base 29, nothing added); `test_smoke_check.py` OK.
- Gates after merging `main` (`1be391d`, stage E) with the embarkation and orphan-camp fixes: build 0 warnings / 0 errors; test build 0 warnings; harness exit 0, 2031 PASS (main 2003 + 28); config-audit 0 over 115 entries; orphan-sweep 0, `--include-test-only` 29; `test_smoke_check.py` OK.
- Gates after merging `main` (`4ba3805`, stages E and F, perk-id fixes #71-#79) with the verify revisions: build 0 warnings / 0 errors; test build 0 warnings; harness exit 0, 2149 PASS; config-audit 0 over 119 entries; orphan-sweep 0, `--include-test-only` 29; `test_smoke_check.py` OK.
- Shared files touched (minimal): `Plugin.cs` (eleven binds), `Patches/CoordinatedOffensiveOperationsPatch.cs` (#38 winter mirror), `docs/patch-catalog.md`, `docs/handoff.md`, `docs/findings.md`, `tools/smoke-situational.txt`.
- Not adopted: the `&&` reading of the depot test 14740 (would make near-front units ignore their own depots and wish new ones each cycle); reviving the micro-move gate (double gate with stage G's `Recover` withholding); rescaling the 0.25 consumers (ambiguous; constant available).

PROVE-MODE: harness for every pure decision and for both IL rewrites against the real vanilla IL; mutation-checked. Runtime proof (Harmony applies the eleven patches and the shared-sounds pair, the transpilers find their patterns in the shipped DLL, the camp and transport models stay hidden in fog, destroyed units leave no camp, no warnings) is the in-game smoke below; nothing here is deployed yet.

## Smoke items

Run after merge and deploy (`python3 -I tools/smoke-check.py "<GTCW>/BepInEx/LogOutput.log"` first; it must exit 0).

1. Startup, before any campaign: `[once:offensive-weapon-trim:applied] [Patch:OffensiveWeaponTrim] CheckOffensiveMovements trim loop now uses the blended weapon factor`, and seven `[once:random-candidate-pick:applied:<site-key>] [Patch:RandomPick] <Method> candidate pick now includes the last candidate` lines (site keys raise-fleet, grab-fleet, grab-ship-type, grab-existing-group, zone-iip, raise-new-group, raid-town). Zero `[once:offensive-weapon-trim:pattern]`, `[once:random-candidate-pick:pattern:<site-key>]`, `…:transpiler` or `random-candidate-pick:missing` warnings.
2. A few campaign days: `[first-fire]` for `campaign.patch.fogged-camp.prefix`, `.finalizer`, `campaign.patch.area-membership.prefix`, `.postfix`, `campaign.patch.winter-offensive-modifier.prefix`, `.finalizer`, `campaign.patch.supply-depot-fix.prefix`, `.postfix`, `.finalizer`, `campaign.patch.confiscation-supply-flow.postfix`; `[once:winter-offensive-modifier:active] [Patch:WinterOffensive] vanilla's winter offensive roll reads winteroffensiveoperationsmodifier`.
3. Fogged camps: `[once:fogged-camp:kept] [Patch:FoggedCamp] a fogged AI unit kept its camp (model hidden from the player)` and `[once:fogged-camp:hidden] [Patch:FoggedCamp] hid a camp or entrenchment model of an AI unit the player cannot see`. Visual: no tents or earthworks where no AI army is visible; scouting an idle AI army reveals its camp (`[once:fogged-camp:shown] …`). Player armies encamp exactly as before. No encamp bugle from empty map areas.
4. Area membership (once the AI builds a depot, fort or company after its second AI run): `[once:area-membership:building] [Patch:AreaMembership] registered a building created after vanilla's second AI run into its AI area`.
5. Fogged embarkation: `[first-fire] campaign.patch.fogged-embarkation.prefix`, `.finalizer`, `.shared-sounds.prefix` and `.shared-sounds.postfix`; once a hidden AI army uses a railroad or river, `[once:fogged-embarkation:kept] [Patch:FoggedEmbarkation] a fogged AI unit is embarked (transport model hidden from the player)` and `[once:fogged-embarkation:hidden] …`. Visual: no trains or boats where no AI unit is visible; a revealed embarked AI unit shows its model (`[once:fogged-embarkation:shown] …`). Player units embark as before. Once a hidden AI unit embarks, `[once:fogged-embarkation:shared-sounds] [Patch:FoggedEmbarkation] a hidden AI transport model reuses one ship-firing sound array`; naval combat sounds unchanged.
6. Orphan camps: when a unit is merged or destroyed, `[first-fire] campaign.patch.orphan-camp-cleanup.postfix` (situational) and, if it had a camp, `[once:orphan-camp-cleanup:removed] [Patch:OrphanCamp] removed the camp or entrenchment of a destroyed unit`; no tents left behind where a merged brigade stood.
7. Situational, may not appear in a short session (listed in `tools/smoke-situational.txt`): `campaign.patch.raid-supply-threshold.*` and `[once:raid-supply-threshold:target] [Patch:RaidSupply] a raid force passed the rescaled supply gate and picked a target` (needs a raid force; then cavalry raid groups should move on enemy buildings and depots, state 3, and return); `[once:supply-depot-fix:masked] …` and `[once:supply-depot-fix:builders] …`; `campaign.patch.subordinate-strength-grab.postfix` and `[once:subordinate-strength-grab:trimmed] …`; `campaign.patch.offensive-weapon-trim.hook`; `campaign.patch.random-candidate-pick.hook`; `[once:confiscation-supply-flow:applied] …` (Union, once the Confiscation Act is active).
8. Must not appear: any `[Patch:RaidSupply]`, `[Patch:FoggedCamp]`, `[Patch:FoggedEmbarkation]`, `[Patch:OrphanCamp]`, `[Patch:AreaMembership]`, `[Patch:WinterOffensive]`, `[Patch:SupplyDepotFix]`, `[Patch:SubordinateGrab]`, `[Patch:Confiscation]` "failed" warning.
9. Behaviour watch: AI offensive tempo in November-March drops (the roll's time term × 0.05 instead of 0.2; the idle term is unscaled, so long-idle armies still attack); raids now happen (watch for raid spam or raid groups stuck in state 5/6); hidden AI armies keep winter-quarter readiness and now move by rail at rail speed (and pay transport capacity and locomotives); low-supply AI units next to their own finished depots now move to a town or other depot instead of idling; low-supply AI units still walk to undefended enemy depots to capture them (vanilla).

## Rollback

Each flag off restores vanilla for its fix (the transpiler hooks read their flags live). `Enabled = false` skips every patch.

## Stage G reuse points

- G5a pointer redirect: `AIAreaMembership` keeps `supplycapacity` and area objectives current; the redirect's supply gate now sees new depots.
- Snapshot supply metric: `GetGroupSupplyState` is on a halved scale; use `RaidSupplyThreshold.GroupSupplyStateScale` (research #2: `Recover` at 0.4 on this scale = 80 % real supply).
- SPEC 154 delegates supply-base attacks to vanilla raid forces; with `BUG-LOG-001` fixed they actually search for targets, so budget for that behaviour.
- G5c depot tasks: vanilla job 19 still admits undefended enemy depots (capture-for-supply) and now pins one builder per depot inside bugle range.
