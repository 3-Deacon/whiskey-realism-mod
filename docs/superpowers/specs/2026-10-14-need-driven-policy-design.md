# Need-Driven Policy Selection — Design

**Status:** approved direction (user, 2026-10-14: "any eligible policy, personality weighted"). Applies to both sides and, in W&L careers, to the player's own side (policies are always AI-run there). Builds on the fixed `PolicySelectionPatch` / `FiscalPolicySelector` from WS C2b.

## Vanilla model (decompile, build 14703111)

- `Policy` objects live under "UI"; `Policy.allpolicies` includes grand policies (`Policy.grandpolicies`). `Config/policies.dat` (117 records, 23 lines each) supplies id, `validscenarios`, prerequisites, `ResearchDuration` (years), `IsAct`, `ReferedChapter`, texts — **no effect data**. Ids 0-99 Union, 100-199 CSA (`GetAllianceFromPolicy` 211392). `researchprogress` is per object, so `GetStatusByPolicyID(id)` is global.
- **Effects are hard-coded per id** in `Policies.LaunchPolicyEffect` (209681-210955); magnitudes come from `Config/campaignprefs.txt` 2641-3126 via GamePrefs. Running effects are re-applied each tick by `UpdatePolicyEffects` (209616).
- **Research:** `AddResearch` (209554) — no money cost; non-acts need a free slot (`maxpolicies` = 7 + round(5 × politics subsidy), 7-12); progress per tick ∝ `averagesupport / n^1.1 × (1 + 0.05 × project 95) / ResearchDuration` (209455) — **state support scales research speed**. Finished non-acts keep their slot forever; acts take no slot and are permanent. `UpdateSubsidyEffect` evicts non-acts over the cap and `RemoveResearch` cascades to dependents (209593).
- **Vanilla AI** (`CheckAIPolicyChange` 211017, every 0.5 game-days per alliance): researches **one** thing at a time; walks the ordered personality list (`Campaigns/<lvl>/<sub>/aipersonalities.dat`, ids 0-7 per side + 100 Recruit Emergency, 101 Credit Emergency) and takes the first passing: not active, `ReferedChapter <= CurrentChapter`, `HasPrePolicies`, `!IsDeactivated` (210957), `!blocked`, `IsAvailableInScenario`. Emergency personalities (`GetAIPersonality` 62716: credit rating > 0.6 of notches; ≤ 4 recruitable states + chapter ≥ 1 + strength ratio < 0.8) fire a newspaper event and cancel in-progress research not on the emergency list (211071).
- **Latent vanilla stall:** at the slot cap `AddResearch` no-ops but vanilla still marks the pick, retrying the same non-act forever.

## Goals

Both governments research what their war actually needs — credit when the treasury fails, manpower when the ranks thin, diplomacy when recognition is in reach, blockade measures when ports are closing — with each faction's historical personality as a strong preference rather than a cage, without ever triggering scripted/irreversible effects by accident.

## Design

### Hard filters (all must pass)

alliance ∈ {0,1}; `GetAllianceFromPolicy(p) == alliance`; not grand (`Policy.grandpolicies.Contains(p) || markedasgrandpolicy || ResearchDuration <= 0`); not active/done; `ReferedChapter <= CurrentChapter`; `HasPrePolicies`; `!IsDeactivated` (reflection); `!blocked`; `IsAvailableInScenario`; `CanBeActivated` (slot available for non-acts); prerequisites reachable (exclude branches that need an unheld grand act: Bread Basket/King Cotton III-IV ← 75/175, Industrialization III-IV ← 76/176, Union Diplomacy IV ← 74); denylist: Union 36 before the war (chapter 0 stays vanilla's script).

### `PolicyEffectCatalog` (pure, static)

id → effect vector over {Credit, Treasury, TaxCap, Manpower, ContractStability, Morale, Support, Intervention, Blockade, TradeWar, Naval, Industry, Agriculture, Transport, Supply, Command} + flags {Irreversible, SpawnsUnits, StartsWar, CostsSupport, CostsWealth, CostsMoneyPerRecruit}. Content from the `LaunchPolicyEffect` mapping (research table: Govt Funding 0-2/100-102 → credit/economy caps; Bread Basket/King Cotton → agriculture; Industrialization → industry + rail; Military 11-14/111-114 → manpower subsidy (13/113 unlock project 88; 12/112 gate manpower acts); Diplomacy → diplomacy caps; Print Notes → treasury −wealth; War Bonds/Bank Acts → credit (−support for Bank Acts); Tariff/Free Trade → tax caps ± intervention; Revenue I-III → income/corporate caps −support; Militia II-IV → contract length; Enrollment/Conscription → mobilization, drafts, −morale, contract reset; Bounties → volunteers + money per recruit; USCT/CSCT/Regulars → unit spawns; Emancipation/Abolition → intervention, morale; Legal Blockade/Letters of Marque/Org. Blockade Running → blockade/trade war; Civilian Ships → naval cost/transport; Northern Routes/Feed Europe/Nationalized Trade/Restrict/Burn Cotton → economy/diplomacy; Impressment → recruits + supply cost; Confiscation → supply; Lt Gen Act → command; Mexican Intervention → war with Mexico, French intervention). Where cheap, scale magnitudes from live GamePrefs fields so config mods flow through.

Irreversible/scripted items scored only when the gating need is high: 144 (war with Mexico), 134 (ends slavery), 42 (Lt Gen Act — interacts with succession), 130 (burn cotton), 33/133/48/147 (unit spawns), 39/40/139/140 (contract reset).

### `CampaignNeedBuilder` (pure + runtime reader) — shared with projects

Daily per-alliance `NeedVector` in [0,1] with ~14-day EMA: Credit (fiscal posture + distance to defended gate, reusing `FiscalIntentLedger`), Manpower (recruits available vs fielded, recruitable-state count vs 4, contract expiries / troops fielded), Morale (1 − nationalmorale), Support (1 − averagesupport), Recognition (CSA intervention probability; inverted for Union), Blockade (own `averageblockaderatio`), Industry/Weapons (weapon stock and industry gaps), Logistics (front supply, transport peak use), plus scripted objective needs (Union policy 11 until done). `ProjectSelectionPatch.BuildSignals` switches to this builder (it currently leaves ManpowerStress/CivilOrderRisk/AgricultureFoodStress/OffensiveTempo empty and reads `alliance[1]` blockade for both sides) — the old signal code is deleted.

### Score

```
S(p) = Σk need_k·eff[p,k]
     + Wpers·(1 − rank/len)   if p in the personality list, else 0     (Wpers ≈ 0.7)
     + Wprof·profilePrior(p)                                           (era/grand-strategy profile over effect kinds)
     + γ·max over dependents d of Σk need_k·eff[d,k] / (1 + depth)     (prerequisite look-ahead; project unlocks count)
     − λ·(usedSlots/maxpolicies)²   for non-acts
     − μ·(ResearchDuration)·maxNeed
     − SideCost(p)   (support loss × (1 − averagesupport) — support also slows all research; money per recruit × Credit; −intervention × Recognition; morale loss)
     − κ·Irreversible(p) unless its gating need ≥ 0.7
```
`FiscalPolicySelector`'s fiscal veto becomes part of `SideCost`; `GrandStrategyPolicyScorer`'s hand tags (several wrong: 103-106 are agriculture caps, 34/44 are diplomacy/morale, 135 is ship cost/transport) are replaced by `profilePrior` over effect kinds — the old scorer is deleted.

### Emergencies

Detected with vanilla's own `GetAIPersonality(alliance).id ∈ {100,101}` so recruiting/finance/projects stay consistent. Whiskey owns the branch: Credit or Manpower need forced to 1 and the emergency list used as the personality prior. Cancel in-progress research only when progress < 0.25, Whiskey added it, and it was not a player prestige purchase.

### Cadence & hysteresis

The daily strategic review computes a ranked candidate list per alliance (`PolicyPlanLedger`); the `PolicySelectionPatch` Prefix (0.5-day cadence) only consumes it: take the first cached candidate that still passes the hard filters and call `AddResearch`; return true (vanilla) when the cache is empty/stale or nothing passes. The cached top changes only when a challenger beats it by ≥ 0.25 or a need crosses its emergency threshold. Never cancel in-flight research outside an emergency. Gate: `automanagepolicies || ai_vs_ai` (the realism lock forces automanage).

### Telemetry

Top-3 candidates with need × effect breakdown on the existing fiscal decision event (signature-bounded).

## Components

| Unit | Kind |
|---|---|
| `Strategic/Policies/PolicyEffectCatalog.cs` | pure |
| `Strategic/Policies/PolicyEligibility.cs` | pure (hard filters over a snapshot) |
| `Strategic/Policies/PolicyNeedScorer.cs` | pure |
| `Strategic/Policies/PolicyPlanLedger.cs` | pure (ranking + hysteresis) |
| `Strategic/CampaignNeedBuilder.cs` | pure builder + runtime reader |
| `Patches/PolicySelectionPatch.cs` | patch (consumes ledger) |
| `Patches/ProjectSelectionPatch.cs` | switches signals to `CampaignNeedBuilder` |

Deleted: `GrandStrategyPolicyScorer` policy tagging, the per-call scoring inside `PolicySelectionPatch`, `ProjectSelectionPatch.BuildSignals`.

## Testing

Catalog completeness (every non-grand policy id in policies.dat has an entry — fixture from the real file layout); hard filters (grand/ownership/chapter/slot/unreachable-branch/denylist); scoring (credit crisis → War Bonds/Bank Act; manpower crisis → Conscription/Enrollment over Diplomacy; personality preference wins near ties; irreversible items suppressed unless needed; support-costly acts penalised when support is low); hysteresis (no flip on small changes; emergency switch); emergency cancellation rules.

## Out of scope

Changing policy effects or research mechanics; the vanilla slot-cap stall itself (avoided by the `CanBeActivated` filter); player W&L prestige purchases (untouched).
