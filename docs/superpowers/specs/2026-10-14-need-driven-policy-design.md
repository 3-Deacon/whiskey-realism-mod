# Need-Driven Policy Selection — Design

**Status:** approved direction (user, 2026-10-14: "any eligible policy, personality weighted"; 2026-10-15: policies are need-driven and personality-weighted, history acts as a weight, everything defaults on, the player's own policy choices are never overridden where the player legitimately controls policy). Applies to both AI alliances and, in a W&L career, the player's own alliance at every rank — Commander-in-Chief included (user decision 2026-10-15, verify round 1). Replaces the C2b `PolicySelectionPatch` scoring (`FiscalPolicySelector`, `GrandStrategyPolicyScorer`).

## Vanilla and W&L model (decompile build 14703111; data from the GTCW install)

### Policies and research

- `Policy` objects are UI children of "UI" (`Policy.InitializeStatics` 211285). `Policy.allpolicies` holds every policy; `Policy.grandpolicies` holds the children of a "GrandPolicies" folder, filtered to the loaded scenario (211311-211319). `markedasgrandpolicy` is set only when the policies panel opens (`UpdateGrandStrategyIcons` 209337), so it is not a reliable filter; grand policies are `grandpolicies` members or `ResearchDuration <= 0` (ids 70-79 Union, 170-179 CSA, all duration 0 in `Config/policies.dat`).
- `Config/policies.dat` (117 records; `ImportPolicyData` 211717): id, `validscenarios`, prerequisite count + 5 slots, `ResearchDuration` (years), `IsAct`, `ReferedChapter`, headline, tooltip, newspaper flag, headlines, picture, content — **no effect data**. Ids 0-48 Union, 100-147 CSA (`GetAllianceFromPolicy` 211392 reads the parent folder name; ids below 100 are Union). Every non-grand policy is valid in scenarios 001 and 002. Prerequisites on grand acts: Bread Basket III (5) ← 75, Industrialization III (9) ← 76, Diplomacy IV (18) ← 74, King Cotton III (105) ← 175, Industrialization III (109) ← 176.
- `researchprogress` is per `Policy` object, and Union and CSA policies are different objects, so `GetStatusByPolicyID(id)` (211573) is effectively per alliance.
- **Effects are hard-coded per id** in `Policies.LaunchPolicyEffect` (209681-210955); magnitudes come from `Config/campaignprefs.txt` § "Policies & Subsidies" (lines ~2638-3126). Running effects are re-applied by `UpdatePolicyEffects` (209616) every alliance-data update. Intervention effects call `AdjustInterventionLevel` on the acting alliance (62887); `GetInterventionProbability(2|3)` (62822) is `alliance[1].level − alliance[0].level`, so a positive adjustment helps the actor on both sides (CSA: recognition up; Union: recognition down).
- **Research:** `AddResearch` (209554) costs no money; it no-ops unless `CanBeActivated` (209541): a non-act needs a free slot (`GetActivatedPolicies()` < `maxpolicies`; `maxpolicies` = 7 + round(5 × politics subsidy factor), `UpdateSubsidyEffect` 209512). Progress per update = `averagesupport / n^1.1 × (1 + 0.05 × project 95) / ResearchDuration` (`UpdatePolicyResearch` 209455-209472): state support scales research speed. Finished non-acts keep their slot; finished acts move to `activatedacts` (211484) and take none. `UpdateSubsidyEffect` (209516-209538) evicts the longest-remaining non-act while over the cap; `RemoveResearch` (209593) zeroes progress and cascades to dependents.

### Vanilla AI policy choice

- Cadence: `CampaignController.UpdateAllianceData` (81488) per alliance every `updaterhythmalliancedata` → `CheckAIPolicyChange` (81736), then `UpdateProjects`, `UpdatePolicyResearch`, `UpdateFinancialAI`.
- `CheckAIPolicyChange` (211017-211098): returns for alliance > 1 and for the player alliance unless `automanagepolicies || ai_vs_ai` (211019). Reads `GetAIPersonality(alliance)` (62716): the emergency **credit** personality (`emergencyaipolicycredit` = 101) when the rating is past `emergencyaicredittrigger` (0.4); the emergency **recruiting** personality (100) when ≤ 4 states can raise a max-size unit, chapter ≥ 1 and `AICampaign.GetStrengthRatio` < 0.8; otherwise `usedaipersonality`.
- On an emergency personality it fires a newspaper event (Union 116 credit / 115 recruiting, CSA 118 / 117), then **removes every in-progress policy not on the emergency list** (211065-211074).
- One thing at a time: it returns while anything is in research (211061; `GetActivatedPolicies(researchedonly: true)` counts everything in `activatedpolicies` below progress 1). The `| IsAct` clause at 211082 is therefore dead for the AI. Otherwise it researches the first policy of the personality list that is not active, `ReferedChapter <= CurrentChapter`, `HasPrePolicies`, `!IsDeactivated` (210957: Union 36 before 4 Mar 1861, CSA 170 when Virginia is Union), `!blocked`, `IsAvailableInScenario`.
- Latent stall: a non-act at the slot cap passes these filters, `AddResearch` no-ops, and vanilla retries the same pick forever.
- Personality data (`Campaigns/001|002/*/aipersonalities.dat`, identical in every sub-campaign; import 66144): ids 0-7 per side (Historical, Industrial, Expansionist, Diplomatic, Militaristic, Agricultural/King Cotton, Economic, Abolitionist) plus 100 Recruit Emergency and 101 Credit Emergency. The Union Historical list never contains 36 (Militia Act I is scripted by `CheckForChapterUpdate` 211606 on 15 Apr 1861), 43, 44, 48 or 30-32; the CSA Historical list never contains War Bonds (122), Bank Act (123), Tariff (124), 130, 133, 134, 141, 144 or 147. `HistoricCheckboxLockPatch` (#12) forces the historic personality.

### What Whiskey did before this slice

- `PolicySelectionPatch` (#19, Prefix on `CheckAIPolicyChange`) re-ranked only vanilla's personality list with `FiscalPolicyScorer.PolicyWeight` + `GrandStrategyPolicyScorer` hand tags (several wrong: 103-106 are agriculture caps, 34/44 diplomacy/morale, 135 ship cost/transport) and `FiscalPolicySelector`; no policy outside the list was ever considered, and an emergency fell through to vanilla.
- `ProjectSelectionPatch` (#20) built `ProjectDoctrineSignals` itself and left ManpowerStress, CivilOrderRisk, AgricultureFoodStress and OffensiveTempo at 0, PortViability at a constant 0.5, and computed IndustryGap from the **enemy's** project levels 102/104 (not fog-honest). Its blockade read of `alliance[1].averageblockaderatio` for both sides is **correct**, not a bug: `averageblockaderatio` is the blockade of the alliance's own harbors (`GetAverageBlockadeRatio` 93766), and the Union's pressure is the blockade still to do on the CSA (1 − CSA ratio).

### W&L (Whiskey and Lemons career)

- A career sets `GameVars.ai_vs_ai = true` (campaign 29787-29793, battle 23755-23768), so `CheckAIPolicyChange` runs for the player's own alliance at **every** rank, including Commander-in-Chief. Policies have no CIC exemption, unlike weapons (16263), fleets (12012) and army groups (17723). The automanage checkbox in the policies panel (209221-209227) is inert in a career.
- The player's only policy lever is the **prestige purchase**: clicking a policy pays `DLC_WL.CalculatePrestigeCostPolicy` (46081: `ResearchDuration / rank^dlcwl_prestigepolicycostmodifierexp × dlcwl_prestigepolicycostmodifier`, × incident multipliers 21, 23, 44/47) and calls `AddResearch` — it starts research (`Policy.CheckSelections` 211350-211362); any rank can buy. The career cannot cancel research (right-click cancel requires `!dlc_scenarioactive`, 211368). `Diary.influencingpolicies` (182490) logs the purchase.
- **Vanilla W&L bug A (fixed here):** the emergency branch (211065-211074) removes a prestige-bought policy that is still in research — the player loses the prestige and the research, with no refund and no way to have prevented it.
- **Vanilla W&L bug B (display, not fixed):** `Policies.UpdateActivations` (209248-209258) sets `flag = false` at the first policy whose prestige cost exceeds current prestige and never resets it, so every later policy in `allpolicies` order is greyed out even when affordable. Clicks still work (`CheckSelections` checks per policy).
- `GamePrefs.dlcwl_incidentradicalizationpolicycost` is loaded (58625) but never read.
- `LaunchPolicyEffect` 42 (Lieutenant General Act) promotes the alliance's current CIC to rank 8 (210348-210350) — in a career that can be the player.
- Not fixed (different vanilla path): `UpdateSubsidyEffect` can still evict a player's research when `maxpolicies` drops.
- Non-W&L player alliance: vanilla never chooses for it unless the player enables automanage in the policies panel (which overrides the main-menu realism lock at runtime). Whiskey's daily review does not run for that alliance (`CicEligibility.ShouldRunCic`), so Whiskey never chooses for it either. A hand-clicked policy is protected the same way as a career purchase.

## Goals

Each government researches what its war needs — credit when the treasury fails, manpower when the ranks thin, recognition when it is in reach or threatened, blockade measures when ports are closing — with the faction's vanilla personality list (history) as a strong preference rather than a cage, without triggering scripted or irreversible effects by accident, and without ever removing or replacing a policy the player chose.

## Design

### Who decides

| Alliance | Non-emergency pick | Emergency |
|---|---|---|
| AI alliance | Whiskey ranking (vanilla when the ranking is empty or nothing passes) | vanilla |
| W&L player alliance, any rank (CIC included) | Whiskey ranking | vanilla, with the alliance's in-progress research protected |
| Non-W&L player alliance, automanage on | vanilla | vanilla, protected |
| Non-W&L player alliance, automanage off | nobody (vanilla returns at 211019) | nobody |

Neither Whiskey nor vanilla starts research while anything is in research, so a player purchase is never replaced or cancelled. A CIC player's alliance is not run by Whiskey's CIC review, so the daily review computes its fiscal posture for the needs only (not published to `FiscalIntents`, which the fiscal, recruitment and construction patches read) and its policy plan. The emergency protection spares every in-progress policy of the player's alliance, AI-picked ones included (no provenance is kept).

### Emergency protection (bug A fix)

On an emergency personality for the player's alliance, the `PolicySelectionPatch` Prefix appends the ids of every in-progress policy of that alliance to the emergency personality's `policies` list and lets vanilla run; a `[HarmonyFinalizer]` removes exactly the appended tail. Vanilla's removal test (211069) then spares them, and they cannot be picked at 211082 because they are already in `activatedpolicies`. This is a snapshot/restore mutation of a vanilla list (the sanctioned #25 pattern), not a prefix-skip. Consequence: the emergency pick waits until the protected research finishes — the player's choice wins. It runs under the master switch only (it fixes a vanilla bug and is not part of the selection rollback). AI-alliance emergencies stay vanilla's (integrator decision; this reverses the spec's earlier "cancel only research Whiskey started", which needed persisted provenance).

### Hard filters (all must pass)

alliance ∈ {0, 1}; the id belongs to the alliance (< 100 Union, 100-199 CSA); not grand (`grandpolicies`, `ResearchDuration <= 0`); not active, done or in research; `ReferedChapter <= CurrentChapter`; `HasPrePolicies`; `!IsDeactivated` (cached reflection, fail closed); `!blocked`; `IsAvailableInScenario`; a free slot for non-acts (avoids the vanilla stall); no prerequisite chain through an unheld grand act; Union 36 never while `CurrentChapter == 0` (the war start stays vanilla's script).

### `PolicyEffectCatalog` (pure)

id → weights over `NeedKind` {Credit, Treasury, Manpower, ContractStability, Morale, Support, Recognition, Blockade, Industry, Agriculture, Logistics, Supply} in [0, 1], costs over the same kinds, and flags {Irreversible, SpawnsUnits, StartsWar, ContractReset, CostsMoneyPerRecruit}. Each entry cites its `LaunchPolicyEffect` case and is derived from the default `campaignprefs.txt` magnitudes. Irreversible: 33/133/48/147 (unit spawns), 39/40/139/140 (contract reset), 130 (burn cotton), 134 (ends slavery), 144 (war with Mexico). 42 is not flagged: its only lasting effects are CIC fame/rank and a battle-loss morale factor.

### `CampaignNeedBuilder` (pure) + `CampaignNeedRuntime`

Daily per alliance, own-alliance and public data only (fog-honest), each need in [0, 1], EMA with `EmaDays = 14`:

| Need | Source (anchor) |
|---|---|
| Credit | fiscal posture: Emergency 1, CreditDefense 0.75, BalancedWar 0.25, Expansion 0 |
| Treasury | annual deficit / annual spending (`GetBalance`, upkeep, recruitment, interest) |
| Manpower | max(recruits available / fielded — 0 at ≥ 25 %; states able to raise a max-size unit — 1 at ≤ 4, 0 at ≥ 8) (`volunteersavailable`, `draftsavailable`, `troopsfielded` 81515-81614; `HasEnoughRecruits` as in 62716) |
| ContractStability | contract expiries / fielded, 1 at ≥ 20 % (`recruitscontractexpiries`) |
| Morale | 1 − `nationalmorale` |
| Support | 1 − `averagesupport` (32481) |
| Recognition | max(`GetInterventionProbability(2)`, `(3)`) — opportunity for the CSA, threat for the Union |
| Blockade | CSA: own `averageblockaderatio`; Union: 1 − CSA ratio |
| Industry | 1 − weapons in stock / men to arm (`weaponstock`, `troopsinrecruitment`) |
| Agriculture | front supply pressure (`FiscalRuntime` front read) |
| Logistics | peak transport utilisation above 60 % (`transportcapacitypeak`) |
| Supply | front supply pressure |

Emergency personalities force Credit or Manpower to 1 (after smoothing). `ToProjectSignals` derives `ProjectDoctrineSignals` from the same vector; WeaponDeficit uses Industry; ArtilleryDeficit and NavalDeficit stay 0 (the only sources are enemy weapon/tonnage internals, which are not fog-honest and were never filled); OffensiveTempo stays 0 and the late-war collapse risk has no strength term: `AICampaign.GetStrengthRatio` (12366-12402) adds the enemy's `troopsinrecruitment` and `AIFaction.SeparateUnits` (10462) does not filter by fog, so it is not fog-honest. A fog-honest enemy strength is stage G's `EnemyArmyModel`; wire it into these signals when it exists. The review builds needs only once `AICampaign.aifaction` exists (vanilla `GetAIPersonality` can reach `GetStrengthRatio`, which indexes it).

### Score

```
S(p) = Σk need_k·w[p,k]                                           need
     + 0.7·(1 − rank/len)   if p is in the current personality list  history
     + 0.3·Σk profile(tag(k))·w[p,k]                               grand-strategy profile
     + 0.5·max over dependents d of Need(d)/(1 + depth)            prerequisite look-ahead (depth ≤ 3)
     − 0.4·(usedSlots/maxSlots)²   non-acts only                     slot pressure
     − 1.0·ResearchDuration(years)·max_k need_k                      urgency
     − Σk need_k·cost[p,k]                                          side costs
```

**Hard hold (irreversible acts).** An irreversible act whose largest need among the kinds it answers is below 0.7 is *held*: it never enters the ranking, so Whiskey never starts it at any score. When nothing else is eligible the patch lets vanilla run its fallback walk (211076-211090) with the held acts taken out of the personality list for that call (`PolicyListOverride.HeldIdsToRemove`, same `PolicyNeedScorer.IsHeld` test under the review's need vector; snapshot restored in the Finalizer), so vanilla does not start one either: Union Historical lists 33/39/40 and CSA Historical 139/140 near the head (verify round 2). Never on an emergency personality (vanilla's emergency list is walked as written) and never before the review has a need vector for the alliance. A soft penalty was not enough: with the non-act slots full, the ranking always picked something (verify round 1: CSA emancipation 134 at low need).

### Cadence & hysteresis

The daily strategic review builds the need vector and ranks every eligible policy per alliance (`PolicyPlanLedger`). The ledger keeps its pick unless a challenger beats it by ≥ 0.25 or a need crosses 0.85 upward. The Prefix (each alliance-data update) only consumes the ranking: when nothing is in research, it researches the first ranked id that still passes the live filters and returns false; otherwise it returns true. Need EMA and the pick persist in the sidecar.

### Telemetry

`FiscalIntent` decision events, `source=PolicySelection`, signature `alliance|pick|reason`, with the top-3 need/history/profile/cost breakdown; `decision=emergency-protect` with the protected count; `decision=hold-irreversible` (reason `vanilla-fallback`) with the held ids taken out of vanilla's fallback walk.

## Components

| Unit | Kind |
|---|---|
| `Strategic/Policies/PolicyEffectCatalog.cs` | pure |
| `Strategic/Policies/PolicyEligibility.cs` | pure |
| `Strategic/Policies/PolicyNeedScorer.cs` | pure |
| `Strategic/Policies/PolicyPlanLedger.cs` | pure |
| `Strategic/Policies/PolicyListOverride.cs` | pure (call decision `Decide`, emergency protected-id set, held-act fallback filter, snapshot edits and restore) |
| `Strategic/Policies/PolicyRuntime.cs` | runtime |
| `Strategic/CampaignNeedBuilder.cs` | pure |
| `Strategic/CampaignNeedRuntime.cs` | runtime |
| `Patches/PolicySelectionPatch.cs` | Prefix + Finalizer |
| `Patches/ProjectSelectionPatch.cs` | reads the shared need vector |

Deleted: `GrandStrategyPolicyScorer`, `FiscalPolicyScorer.PolicyWeight`, `PolicySelection`, `FiscalPolicySelector`, `ProjectDoctrineSignalBuilder`, `ProjectDoctrineSignalInput`, `ProjectSelectionPatch.BuildSignals/ReadRecognitionProbability`.

## Testing

Catalog completeness against a fixture of every non-grand id in `policies.dat`; hard filters; need builder; scoring (credit crisis → War Bonds / Bank Act; manpower crisis → drafts over Diplomacy; history wins near ties; irreversible hold (incl. the verifier's slots-full probe); support-costly acts penalised when support is low; slot pressure; look-ahead); hysteresis; emergency protected-id set and restore; sidecar round-trip of needs and pick.

## Out of scope

Changing policy effects or research mechanics; W&L bug B; `UpdateSubsidyEffect` eviction; W&L prestige purchases themselves (untouched).
