# Strategic Project Doctrine AI Design

Date: 2026-05-06
Status: active design spec, ready for adversarial review before implementation planning
Scope: Slice A strategic/economy enrichment for Grand Tactician Projects. This spec expands project selection from static era weights into historically influenced, win-seeking Civil/Military project doctrine for both Union and CSA AI. It also records vanilla project bugs and bug candidates found during the decompile pass.

## Adversarial Review Corrections

Review pass on 2026-05-06 tightened this spec in several material ways:

- Bug-candidate suppression must be enforceable, not just a "do not positively score" hint.
- Date windows must receive an explicit out-of-window scoring penalty because vanilla `IsAppointable` does not enforce `datefrom`.
- `CheckProjectUnlocks` is demoted from a bug to init-only seeding behavior unless runtime or a second call site proves otherwise.
- Doctrine is keyed to `EraStage` first. Calendar dates only support date-window penalties and runtime observations.
- Dynamic signals need derivation formulas or explicit defer/default behavior.
- Queued project replacement needs hysteresis so daily dynamic scoring does not churn lane commitments.
- Lane starvation must be acknowledged as read-only in this slice unless a later fiscal lane nudge is explicitly planned.

## Goal

Make both AI sides use the vanilla Projects system as a strategic war economy lever, not just as a weighted random research list.

The AI should remain historically influenced:

- Union should lean into blockade, rivers, logistics, industry, mass recruitment, and late-war pressure.
- CSA should lean into imports, diplomacy, blockade-running, asymmetric naval defense, local arms production, manpower preservation, and protraction.

The AI should also try to win the dynamic game state:

- If the Union has a blockade gap, it should accelerate naval and trade-warfare projects.
- If the CSA is under blockade and short on rifles, it should favor imports, Confederate rifles, trade warfare, and cheap asymmetric naval tools.
- If either side is credit-stressed, manpower-starved, logistically stalled, or losing civil order, project doctrine should react.
- If construction is starving a critical project lane, the fiscal layer should know that before broad subsidy or construction overrides are considered.

## Source Of Truth And Anchors

Confirmed vanilla anchors:

- `Projects.LoadedProjects.Import(string filename)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:212585`
- Project cost formula: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:212635`
- `AICampaign.UpdateProjects(int alliance)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17487`
- `AICampaign.UseSubsidyForPurpose(int alliance, int subsidytype)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17529`
- `GameVars.Alliance.AIPersonality.GetNextProjectRandom(int alliance, int subsidytype)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62048`
- `GameVars.import_nations(...)` AI personality import block: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:66141`
- Subsidy lane names: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:64301`
- `Projects.UpdateOneTimeProjectsEffects(int projectid, int alliance)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:212936`
- `Projects.UpdateProjectEffects()`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213039`
- `Projects.IsAppointable(LoadedProjects project, int alliance, bool useprestige=false, bool usesubsidies=true)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213338`
- `Projects.AppointProject(LoadedProjects project, int alliance, bool manualappointment=false)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213384`
- `Projects.CheckProjectUnlocks(int alliance)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213569`
- New-campaign call site for `CheckProjectUnlocks`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:30088`
- Campaign economy tick ordering around subsidy accumulation, project selection, policy research, finance AI: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:81700`

Current Whiskey anchors:

- `ProjectSelectionPatch`: `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs`
- `ProjectSelectionScorer`: `src/WhiskeyRealism/Strategic/ProjectSelectionScorer.cs`
- `GrandStrategyRegistry`: `src/WhiskeyRealism/Strategic/GrandStrategyRegistry.cs`
- `FiscalPolicyScorer.ProjectWeight(...)`: `src/WhiskeyRealism/Strategic/Fiscal/FiscalPolicyScorer.cs`
- `FinancialAIPatch`: `src/WhiskeyRealism/Patches/FinancialAIPatch.cs`
- `FiscalIntentLedger`: `src/WhiskeyRealism/Strategic/Fiscal/FiscalIntentLedger.cs`

## Confirmed Vanilla Project Model

### Data fields

`Config/projects.dat` has 125 rows. IDs are contiguous `0-124`. IDs `20-29` and `42-87` are literal `PLACEHOLDER` rows with no usable doctrine value.

The loader reads, in order:

- project id,
- project name,
- alliance applicability,
- required policy IDs,
- subsidy type,
- first-level subsidy cost,
- repeating flag,
- `usedgroup`,
- UI images,
- prose/effect text,
- date-from entries for Union and CSA,
- applicable scenarios,
- DLC requirements.

Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:212585`.

`usedgroup` controls the left/right Project UI bucket through `ContentFoldersLeftRight[loadedproject.usedgroup]` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213490`. The observed mapping is:

- `usedgroup=0`: military-side UI bucket.
- `usedgroup=1`: civil-side UI bucket.

Subsidy lane is independent of UI side. `GameVars.subsidynames` maps:

- `0`: Politics
- `1`: Economy
- `2`: Agriculture
- `3`: Industry
- `4`: Military
- `5`: Diplomacy
- `6-7`: N/A

Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:64301`.

Parsed `Config/projects.dat` contains no projects with `subsidytype` `6` or `7`. `ProjectSelectionPatch` may loop all eight `nextprojecttoresearch` slots, but doctrine should produce no candidates for lanes `6/7` and should short-circuit them.

Doctrine must never run for alliance `2` or any higher alliance. Existing `ProjectSelectionPatch` already gates to `alliance < 2`; the doctrine layer must keep that boundary because `AICampaign.aifaction` includes Europe and other intervention alliances.

### Appointment and spending

Vanilla project cost:

```text
pow(level, GamePrefs.subsidylevelcostincreaseexp) * subsidycostfirstlevel
```

Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:212635`.

`AICampaign.UpdateProjects` loops each subsidy lane. If `UseSubsidyForPurpose(alliance, lane) == 0`, it seeds `nextprojecttoresearch[lane]` from the AI personality list when empty. It appoints the queued project only when `subsidyfunding[lane] >= next project cost`. Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17487`.

`Projects.AppointProject` subtracts subsidy funding for AI or non-W&L manual appointment, adds the project ID to `GameVars.alliance[alliance].projects`, records `lastmonthprojectsresearched`, runs one-time effects, then runs persistent project effects. Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213384`.

Repeating project levels are duplicate project IDs in the alliance `projects` list. Non-repeating projects return level `1` when present. Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62584`.

### Construction-vs-project arbitration

`UseSubsidyForPurpose` is the core construction/project gate:

- If there is no best construction candidate in a subsidy lane, project selection may proceed.
- If there is a construction candidate and no queued project in that lane, construction wins.
- If there is a queued project, vanilla compares cumulative project spending against cumulative project plus construction spending and the personality `projectspendingratio`.

Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17529`.

Design consequence: project doctrine cannot be only candidate scoring. It must also observe lane starvation and project-vs-construction contention.

### AI personality input

`aipersonalities.dat` gives each side ten profiles in W&L 1861 scenario `002/A`: eight normal profiles and two emergency profiles. The import block reads:

- alliance,
- personality id,
- name,
- description,
- four tax focus values into a five-slot array,
- eight subsidy focus values,
- 20 prewar policy slots,
- 80 policy slots,
- 100 project/probability pairs,
- `projectspendingratio`.

Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:66141`.

Historical AI personality defaults to profile id `0` unless historical personality randomization is disabled. Emergency profile selection is runtime:

- credit emergency returns `GamePrefs.emergencyaipolicycredit`,
- recruit emergency returns `GamePrefs.emergencyaipolicyrecruiting`.

Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62718`.

### Vanilla selection gap

`GetNextProjectRandom` filters the active AI personality project list to:

- project matches the requested subsidy lane,
- project is appointable with `usesubsidies:false`,
- project applies to alliance,
- project applies to scenario,
- project fulfills DLC requirements.

It then builds a repeated-ID list using `projectsprob` and selects one random entry. Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62048`.

This is historically flavored by data but not state-aware. It does not evaluate current weapon stock, enemy advantage, blockade pressure, port viability, credit, manpower, logistics, operational tempo, construction starvation, or opponent project level.

## Current Whiskey Coverage

`ProjectSelectionPatch` is currently the right narrow behavior surface. It runs before vanilla spending and only replaces `nextprojecttoresearch[subsidy]` when Whiskey has a clear scoring margin. It leaves funding, appointment, project effects, and vanilla availability gates to vanilla.

Current scoring:

```text
vanillaWeight + GrandStrategyProfile.ProjectWeightFor(projectId) + FiscalPolicyScorer.ProjectWeight(...)
```

Current strengths:

- respects player automanage and player-CIC boundaries,
- stays alliance `0/1`,
- does not directly mutate project effects,
- uses the active grand-strategy era profile,
- already has some fiscal pressure scoring.

Current gaps:

- no project taxonomy,
- no Civil/Military doctrine layer,
- no dynamic advantage-seeking,
- no opponent-response scoring,
- no lane funding time-to-complete signal,
- no project-vs-construction starvation signal,
- no date-window/timing policy of its own,
- no telemetry for actual project appointments or date-unlock behavior.

## Project Taxonomy

### Inactive projects

Do not score:

- `20-29`
- `42-87`

These are `PLACEHOLDER` rows and have no alliance/scenario doctrine surface.

### Arms imports

These consume the Diplomacy lane but are military-side UI projects.

| IDs | Project names | Doctrine meaning |
|---|---|---|
| `0` | Austrian Rifles | cheap foreign rifle import bridge |
| `1` | British Rifles | Enfield/Whitworth import route |
| `2` | British Artillery | imported rifled artillery |
| `3` | French Weapons | CSA-gated French arms, policy `144` |
| `4` | Prussian Weapons | expensive high-tech arms import |

Effect anchors: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213047` through `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213069`.

Doctrine:

- Union uses imports only as a deficit bridge.
- CSA uses imports as a core 1861-63 survival path, especially under domestic weapons deficits.

### Domestic weapons and production

| IDs | Project names | Doctrine meaning |
|---|---|---|
| `5` | Hall's Carbines | Union one-time stock grant |
| `6` | Confederate Rifles | CSA domestic rifle production |
| `7` | Cast Artillery | early artillery production |
| `8` | Rifled Artillery | artillery modernization |
| `9` | Parrott Rifles | rifled artillery pressure |
| `10` | Machineguns | Union late/special weapon stock and tech |
| `11` | Confederate Guns | CSA artillery production and stock |
| `12` | Rebore Muskets | low-cost musket upgrade |
| `13` | Legacy Rifles | older rifle production |
| `14` | Cavalry Carbines | cavalry weapon modernization |
| `15` | Medium Range Carbines | cavalry weapon modernization |
| `16` | Sharps Rifles | strong 1863+ rifles/carbines |
| `17` | Repeating Rifles | expensive high-end firepower |
| `18` | CSA Springfield Rifles | CSA-gated Springfield path |
| `19` | USA Springfield Rifles | Union Springfield path |
| `102` | Weapon Production | repeatable weapon production efficiency |

Effect anchors:

- weapon unlock switch: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213047`
- one-time stock grants: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:212940`
- weapon production adjustment: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213217`

Doctrine:

- Union should convert industrial power into domestic weapons, logistics, and production scaling.
- CSA should mix imports with `6`, `11`, and cheap stopgaps when blockade pressure or industry weakness limits procurement.

### Naval, blockade, and river warfare

| IDs | Project names | Doctrine meaning |
|---|---|---|
| `30` | Ironclad Monitors | Union coastal/river ironclad unlock |
| `31` | Ironclad Gunboats | Union river ironclad unlock |
| `32` | Union Rebuilt Ironclads | Union rebuilt ironclads |
| `33` | CSA Rebuilt Ironclads | CSA casemate rams |
| `34` | CSA Ironclad Gunboats | CSA ironclad gunboat rams |
| `35` | Modern Warships | expensive ocean-going modern hulls |
| `36` | Confederate Gunboats | CSA gunboats/cottonclad rams |
| `37` | Armored Gunboats | cheaper armored gunboat path |
| `38` | British Warships | foreign warship import |
| `39` | French Warships | foreign warship import |
| `40` | Gloire Class | CSA/policy-gated foreign ironclad |
| `41` | Warrior Class | very expensive foreign ironclad |
| `106` | Trade Warfare | blockade and blockade-running efficiency |
| `120` | Improvised Shipyards | CSA late improvised shipyard exception |

Effect anchors:

- ship unlock switch: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213129`
- trade warfare multipliers: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:104128`
- improvised shipyards exception: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:66675`

Doctrine:

- Union should prioritize blockade, river control, and pressure. Expensive imports are lower priority unless naval advantage is not materializing.
- CSA should prioritize cheap river/port defense, blockade-running efficiency, and improvised capacity when ports remain viable. Expensive foreign warships should be rare and conditional.

### Logistics, supply, and rail

| IDs | Project names | Doctrine meaning |
|---|---|---|
| `99` | Infrastructure Reform | IIP transport capacity |
| `100` | Logistics Reforms | transport upkeep and supply cost |
| `101` | Military Railroad | rail speed/upkeep |
| `115` | Supply Reform | supply depot upgrade gate |
| `119` | Railroad Construction | railroad construction speed |

Effect anchors:

- `99`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:104694`
- `100`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62972`, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:96996`, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:114426`
- `101`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213213`
- `115`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:98163`
- `119`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:77970`

Doctrine:

- Union uses logistics to convert manpower and industry into sustained offensive tempo.
- CSA uses logistics to preserve interior defense and late-war endurance.

### Finance, admin, credit, and markets

| IDs | Project names | Doctrine meaning |
|---|---|---|
| `95` | Administration Reform | policy research speed |
| `96` | Subsidize Banks | bank funding improvement |
| `97` | Improve Credit Rating | credit rating notch adjustment |
| `98` | Market Reform | vanilla bug candidate, likely no-op |

Effect anchors:

- `95`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:209402`, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:209453`
- `96`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:31979`, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62648`
- `97`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:32567`
- `98`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:31985`

Doctrine:

- `97` is emergency/near-emergency doctrine for both sides.
- `96` is a pre-positioning project before credit collapse.
- `95` matters when policy throughput is the real bottleneck.
- `98` should not receive positive Whiskey scoring until the bug candidate is resolved or disproven.

### Agriculture, industry, and production base

| IDs | Project names | Doctrine meaning |
|---|---|---|
| `104` | Subsidize Industry | factory/ironworks/foundry productivity |
| `105` | Subsidize Agriculture | farm/plantation productivity |
| `113` | Farm Mechanization | crop production and recruitment modifier |
| `114` | Plantation Mechanization | CSA cotton production |

Effect anchors:

- `104`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:97381`
- `105`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:97386`
- `113`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:97401`, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:63666`
- `114`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:97405`

Doctrine:

- Union industry is a late-war pressure multiplier and an early economic profile option.
- CSA agriculture and cotton projects are historical but must be conditional on port/trade viability and manpower/food pressure.
- CSA industrialization is counterfactual but valid as a win-seeking adaptation when imports are failing.

### Diplomacy, recognition, and trade

| IDs | Project names | Doctrine meaning |
|---|---|---|
| `103` | Send Envoys | European relations/intervention |
| `116` | Cotton is King | CSA European relations |
| `117` | Corn is King | Union European relations |
| `121` | Trade Deals | trade volume |

Effect anchors:

- `103`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:212995`
- `116`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213030`
- `117`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213033`
- `121`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:104380`

Doctrine:

- CSA should push these early and mid-war when recognition and trade can still matter.
- Union should use them as counter-diplomacy when CSA recognition or trade pressure rises, not as its main war-winning lever.

### Manpower, training, command, and civil order

| IDs | Project names | Doctrine meaning |
|---|---|---|
| `88` | Command Reform | W&L army group gate |
| `89` | Organization Reform, W&L scenario | army group/org gate |
| `90` | Organization Reform, base scenario | org reform/grand army structure |
| `91` | Propaganda | own loyal-state support |
| `92` | Counter-propaganda | enemy loyal-state support |
| `93` | Occupation Administration | occupied-state support |
| `94` | Suppress Population | occupied enemy support suppression |
| `107` | Civil Order | casualty support modifier, raiding modifier bug candidate |
| `108` | Recruit Agents | intelligence multiplier |
| `109` | Recruitment Offices | recruitment target factor |
| `110` | Cavalry Reform | cavalry formation gate |
| `111` | Cavalry Reform II | horse artillery gate, scenario 001 |
| `112` | Artillery Reform | artillery formation gate, scenario 001 |
| `118` | Training Manuals | drill/training progress |
| `122` | Military Education | military school count |
| `123` | Horse Artillery | horse artillery gate, scenario 002 |
| `124` | 6-gun Batteries | scenario 002 artillery reform equivalent |

Effect anchors:

- one-time support and unit-symbol effects: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:212951`
- organization gates: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62545`, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17166`
- civil order casualty modifier: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:70920`
- civil order raiding bug candidate: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:113751`
- recruit agents: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:120626`
- recruitment offices: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:63251`
- cavalry/artillery restrictions and conversion: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:93814`, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:212998`, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213014`
- training manuals: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:115235`
- military education: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:15196`, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:170040`

Doctrine:

- Union should use these to scale mass army professionalism and sustain pressure.
- CSA should use these as late-war survival and protraction tools.
- Projects `89` and `90` should share one logical `OrganizationReform` doctrine entry. Vanilla scenario applicability selects the correct concrete project.
- `91-94` are civil-side projects and should be driven by state-support, occupation, and morale pressure, not generic politics funding.

## Side Doctrine

Doctrine tables below are keyed to the shipped Whiskey `EraStage` enum:

- `EraStage.Amateur1861`
- `EraStage.Operational1862`
- `EraStage.Decisive1863`
- `EraStage.TotalWar1864`

Calendar project dates from `projects.dat` are not the primary doctrine state. They feed the out-of-window penalty in `ProjectDoctrineScorer`. If a later review finds `EraStage` progression is too slow or too fast for project doctrine, the signal builder may add a calendar-date adjustment, but it must not silently mix two clocks.

### Union

Union doctrine should treat history as a baseline: blockade, river control, industrial mobilization, rail/logistics, mass manpower, and late-war army destruction.

`EraStage.Amateur1861`:

- baseline: `19`, `124`, `112`, `110`,
- if naval pressure is weak: `30`, `37`,
- if credit is already stressed: `97`, then `96`,
- if arms stock is weak: `0`, `1`, `2`, `4` only as a bridge.

`EraStage.Operational1862`:

- logistics and river control: `100`, `31`, `37`, `115`,
- weapons: `7`, `8`, `9`, `14`,
- industry: `102`, `104`,
- manpower/training: `109`, `118`, `122`,
- civil/occupation if advancing deep: `93`, `94`, `107`.

`EraStage.Decisive1863`:

- offensive tempo: `100`, `102`, `16`, `17`, `118`, `122`,
- blockade/naval tightening: `31`, `32`, `35`, `106`,
- industry/logistics: `104`, `99`,
- credit defense if gates near failure: `97`.

`EraStage.TotalWar1864`:

- sustain simultaneous pressure: `100`, `101`, `104`, `102`, `118`,
- civil/occupation and order: `93`, `94`, `107`,
- manpower: `109`, `122`,
- imports only if weapon deficit is severe.

Dynamic Union triggers:

- `BlockadeWeak`: score `30`, `31`, `32`, `35`, `37`, `106`.
- `RiverCampaignActive`: score `31`, `37`, `100`, `115`.
- `WeaponDeficit`: score `19`, `7`, `8`, `9`, `16`, `17`, `102`; use imports only if domestic unlocks are unavailable or too slow.
- `CreditStress`: score `97`, `96`, `95`; suppress expensive warship imports.
- `ManpowerStress`: score `109`, `113`, `118`, `122`.
- `OffensiveTempoNeed`: score `100`, `101`, `102`, `118`, `122`.
- `OccupationPressure`: score `93`, `94`, `107`.

### CSA

CSA doctrine should treat history as asymmetric survival: independence, protraction, foreign recognition, imports, blockade-running, local arms production, river/port defense, and manpower preservation.

`EraStage.Amateur1861`:

- military reforms: `110`, `112`, `123`, `89`, `90`,
- imports when available: `0`, `1`, `2`, `4`,
- domestic survival: `6`, `7`, `12`, `13`, `18` when legal,
- diplomacy/trade: `103`, `116`, `121`,
- river/port defense: `33`, `36`, `37`.

`EraStage.Operational1862`:

- arms/imports: `0`, `1`, `2`, `4`, `6`, `7`, `8`, `9`,
- naval asymmetry: `33`, `34`, `36`, `37`, `106`,
- logistics: `100`, `115`,
- manpower/training: `109`, `118`, `122`,
- agriculture/trade: `105`, `113`, `114`, `116`, `121`.

`EraStage.Decisive1863`:

- if still competitive: `103`, `116`, `121`, `106`, `6`, `11`,
- if under blockade: `106`, `34`, `36`, `37`; imported warships only with port viability and adequate credit,
- if manpower is slipping: `109`, `113`, `118`, `122`, `107`,
- if credit is slipping: `97`, `96`, then cheap military essentials.

`EraStage.TotalWar1864`:

- survival stack: `109`, `118`, `107`, `97`, `100`, `113`,
- late naval/port defense only if ports matter: `120`, `34`, `36`, `37`,
- expensive foreign warships `38-41` only if the AI has money, port access, and a realistic strategic use.

Dynamic CSA triggers:

- `BlockadePressure`: score `106`, `36`, `34`, `33`, `37`, `120`.
- `PortViabilityLow`: suppress `38`, `39`, `40`, `41`, `120`; redirect to `100`, `109`, `118`, `97`.
- `WeaponDeficit`: score `0`, `1`, `2`, `4`, `6`, `11`, `12`, `13`, `102`.
- `RecognitionWindowOpen`: score `103`, `116`, `121`, and policy-linked imports.
- `CreditStress`: score `97`, `96`, suppress expensive naval imports.
- `ManpowerStress`: score `109`, `113`, `118`, `122`, `107`.
- `OffensiveOpportunity`: score `6`, `11`, `14`, `15`, `100`, `118`, `110`, `112`, `123`.
- `LateWarCollapseRisk`: suppress prestige projects and favor survival projects.

## Dynamic Doctrine Architecture

### ProjectDoctrineCatalog

Create a pure catalog under `src/WhiskeyRealism/Strategic/Projects/`.

Responsibility:

- map project ID to doctrine bucket,
- expose UI side and subsidy lane,
- expose side/era fit,
- expose bug/suppression flags,
- keep hardcoded vanilla IDs in one place.

Proposed types:

```csharp
namespace WhiskeyRealism.Strategic.Projects
{
    public enum ProjectDoctrineBucket
    {
        None,
        ArmsImport,
        DomesticWeapons,
        NavalBlockade,
        LogisticsRail,
        FinanceCreditAdmin,
        AgricultureIndustry,
        DiplomacyTradeRecognition,
        ManpowerTrainingCivilOrder
    }

    public enum ProjectUiSide
    {
        Military,
        Civil
    }

    public enum ProjectBugReviewState
    {
        None,
        FullyBrokenUntilReviewed,
        PartiallyBrokenUntilReviewed
    }

    public sealed class ProjectDoctrineEntry
    {
        public int ProjectId;
        public ProjectDoctrineBucket Bucket;
        public ProjectUiSide UiSide;
        public int SubsidyLane;
        public bool IsPlaceholder;
        public ProjectBugReviewState BugReviewState;
        public string ShortName;
    }
}
```

Bug review states have scoring meaning:

- `FullyBrokenUntilReviewed`: candidate gets a strong negative score such as `-1000f`. If vanilla queued it, Whiskey must replace it whenever any non-placeholder, non-suppressed candidate exists in the same lane.
- `PartiallyBrokenUntilReviewed`: candidate may still score for verified working effects, but the broken effect must not add score. Project `107` can score for casualty/civil-order pressure, but not for raiding mitigation until that vanilla bug is fixed or disproven.

### ProjectDoctrineSignals

Create pure per-alliance signals from existing ledgers and vanilla state.

Inputs should be cheap and already available:

- era stage from `StrategicCoordinator.Instance.Eras[alliance]`,
- fiscal posture from `FiscalOutput`,
- current profile from `GrandStrategyRegistry`,
- `DirectorPosture` / campaign pace if available,
- `CampaignMapLedger` town/port/rail/harbor awareness,
- `FormationDirectiveLedger` pressure and readiness/supply stress,
- `WarStateObserver` strength and battle-history signals,
- opponent project levels via `GameVars.alliance[enemy].GetProjectLevel(id)`,
- own project levels,
- subsidy funding and queued project per lane,
- construction/project contention through `UseSubsidyForPurpose` observation.

Signals:

```csharp
public sealed class ProjectDoctrineSignals
{
    public int Alliance;
    public EraStage Era;
    public FiscalPosture FiscalPosture;
    public float WeaponDeficit;
    public float ArtilleryDeficit;
    public float NavalDeficit;
    public float BlockadePressure;
    public float PortViability;
    public float CreditStress;
    public float ManpowerStress;
    public float LogisticsTempoNeed;
    public float IndustryGap;
    public float AgricultureFoodStress;
    public float CivilOrderRisk;
    public float RecognitionWindow;
    public float OffensiveTempoNeed;
    public float LateWarCollapseRisk;
}
```

All values should be clamped `0..1`.

Signal derivation map:

| Signal | Phase-1 source and formula | Notes |
|---|---|---|
| `WeaponDeficit` | `max(enemyBestAverageRifles - ownAverageRifles, 0) / max(enemyBestAverageRifles, 0.01)`, using the same average-strength fields used by `AICampaign.CheckPurchaseWeapons` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:16259` | Rifle/project scoring signal, not direct procurement doctrine. |
| `ArtilleryDeficit` | `max(enemyBestAverageGuns - ownAverageGuns, 0) / max(enemyBestAverageGuns, 0.01)`, same anchor as above | Drives artillery unlocks/imports. |
| `NavalDeficit` | `max(enemyTotalTonnage - ownTotalTonnage, 0) / max(enemyTotalTonnage, 1)`, using alliance `totaltonnage` updated near `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:81684` | Refine later with fleet condition and port mission value. |
| `BlockadePressure` | For CSA: `GameVars.alliance[1].averageblockaderatio`. For Union blockade weakness: `1 - GameVars.alliance[1].averageblockaderatio`. Field anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62110`, update anchor `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:81684` | The scorer must interpret this by side. |
| `PortViability` | If `AICampaign.aifaction` port lists are populated, use `1 - average(ownportsblockedsea, ownportsblockedriver)` from port blocked fields around `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:11961`. Otherwise default to `0.5` and log `unverified-port-signal`. | Needs runtime validation because `aifaction` snapshots may be unavailable during fiscal/project cadence. |
| `CreditStress` | Map `FiscalOutput.Posture`: `Expansion=0`, `BalancedWar=0.25`, `CreditDefense=0.75`, `EmergencySolvency=1`. | Avoid duplicating fiscal rating logic. |
| `ManpowerStress` | Reuse the vanilla recruitment-emergency shape from `GetAIPersonality`: recruitable-state count vs `GamePrefs.emergencyairecruitingtrigger`, plus `AICampaign.GetStrengthRatio(alliance) < 0.8`. Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62718` | Clamp by strength-ratio severity. |
| `LogisticsTempoNeed` | Use existing formation/supply pressure if available; otherwise `max(transportcapacitypeak[0..2])` pressure from alliance transport peak updates around `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:81700`. | Prefer current Whiskey formation/supply ledgers over reflection-heavy vanilla scans. |
| `IndustryGap` | Phase 1: compare own vs enemy levels of `102` and `104`; `clamp((enemyLevel - ownLevel) / 3)`. | This is a proxy until a production-value ledger exists. |
| `AgricultureFoodStress` | Phase 1: `max(ManpowerStress * 0.5, supplyPressure * 0.5)` if supply pressure exists; otherwise compare own vs enemy levels of `105/113/114/117/116`. | Needs later validation against actual crop/food economy fields. |
| `CivilOrderRisk` | Count occupied enemy/own states with low support when cheap state iteration is safe; otherwise derive from recent casualty/support pressure and only score `91-94/107` conservatively. | State-support iteration must be bounded. |
| `RecognitionWindow` | Use `GameVars.Alliance.GetInterventionProbability(2/3/4)` from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62823`; for CSA use max British/French probability; for Union use risk of British/French CSA recognition or US counter-recognition probability. | This is a real vanilla source, but policy/chapter gates inside the method must be respected. |
| `OffensiveTempoNeed` | Use Strategic Resilience Director or Operational Probe posture when available; otherwise derive from favorable strength ratio and low recent contact as `clamp((strengthRatio - 1) * 0.5)`. | Must remain cheap; no new map-wide scan. |
| `LateWarCollapseRisk` | `EraStage.TotalWar1864` plus `CreditStress`, `ManpowerStress`, and negative strength ratio. Formula: `TotalWar ? clamp(0.4*Credit + 0.4*Manpower + 0.2*max(1-strengthRatio,0)) : 0`. | Stateless unless a later plan adds persisted hysteresis. |

Signals not safely derivable from existing ledgers in implementation must default to `0` or the documented neutral value and emit a once-per-session warning. Do not invent expensive reflection scans in the scorer.

### ProjectDoctrineScorer

Extend `ProjectSelectionScorer` by adding doctrine score as a new additive term:

```text
vanillaWeight
+ static grand-strategy project weight
+ fiscal project weight
+ project doctrine score
```

Keep the existing clear-margin replacement principle. Increase the margin if observer data shows overactive churn.

Doctrine scoring rules:

- Score bucket first, then project-specific refinements.
- Do not score placeholder rows.
- Fully suppress bug-candidate projects where the whole useful effect is likely no-op unless a separate bug-fix slice proves or fixes them. Suppression must be strong enough to replace a vanilla-selected suppressed project whenever any non-suppressed candidate exists.
- Partially suppress only broken sub-effects. Project `107` must not receive raiding-mitigation score, but may still receive casualty/civil-order score.
- Apply an out-of-window penalty when project `datefrom[alliance]` is materially later than the current campaign date or `EraStage`. The penalty must be at least the replacement margin, and should be stronger for expensive projects. Blank date fields mean no date penalty.
- If vanilla already queued an out-of-window project, Whiskey should replace it whenever an in-window candidate in the same lane is within the normal replacement margin.
- Do not score a starved lane down just because construction currently wins. That only swaps projects inside the same starved lane. Lane starvation is logged and handed to fiscal intent; it is not fixed by project scoring alone.
- Penalize expensive projects under `CreditDefense` or `EmergencySolvency`.
- Penalize foreign warship imports when port viability is low.
- Penalize arms imports for Union when domestic production and weapon production are healthy.
- Allow CSA counterfactual industrialization only when imports/ports are failing and credit can support it.
- Preserve historical side preference as a bias, not as a hard lock.

Replacement hysteresis:

- If `nextprojecttoresearch[lane]` is already queued and `subsidyfunding[lane] >= 0.5 * cost(nextProject)`, do not replace it unless the new candidate beats the queued project by at least `2 * ReplacementMargin`.
- Suppressed or out-of-window projects bypass this protection when a valid replacement exists.
- This hysteresis is stateless and uses current lane funding and current queued project cost. No sidecar persistence is needed for Slice 1.

### ProjectLaneIntent

Add a read-only project lane intent output before any fiscal mutation:

```csharp
public sealed class ProjectLaneIntent
{
    public int Alliance;
    public int SubsidyLane;
    public int QueuedProjectId;
    public float FundingAvailable;
    public float FundingNeeded;
    public float NetFundingPerDay;
    public float TimeToFundEstimateDays;
    public bool ConstructionCurrentlyWins;
    public bool CriticalDoctrineProject;
}
```

Formula:

```text
costToGo = max(0, FundingNeeded - FundingAvailable)
netFundingPerDay = max(0, observedDelta(subsidyfunding[lane]) / observedDays)
TimeToFundEstimateDays = netFundingPerDay > 0 ? costToGo / netFundingPerDay : +infinity
```

The first implementation may not have a stable observed delta until after runtime telemetry. In that case it should omit the estimate or log `rate=unknown`, not fabricate a value.

Slice 1 action boundary:

- Project doctrine logs lane starvation and exposes `ProjectLaneIntent`.
- It may feed this as read-only input into fiscal intent.
- It does not patch `UseSubsidyForPurpose`.
- It does not solve starvation by picking a different project in the same lane.
- A later fiscal-lane plan may allow `FinancialAIPatch` to nudge the needed subsidy lane when a doctrine-critical project is starved and credit posture allows it.

## Telemetry Requirements

Add bounded telemetry before behavior expansion.

Expected once/signature keys:

| Key | Message purpose |
|---|---|
| `project-selection` | Existing once key: `ProjectSelectionPatch wired (grand-strategy project steering)` |
| `project-doctrine-catalog` | Catalog initialized and active |
| `project-doctrine-selection` | Project-selection replacement or retained-critical decision |
| `project-doctrine-starved-lane` | Critical project lane is blocked by construction or has unknown funding rate |
| `project-appoint-observer` | `Projects.AppointProject` observer wired |
| `project-unlock-observer` | `Projects.CheckProjectUnlocks` observer wired |

Smoke greps should include:

```bash
rg "\\[once:project-(selection|doctrine-catalog|doctrine-selection|doctrine-starved-lane|appoint-observer|unlock-observer)\\]|\\[ProjectDoctrine\\]|\\[ProjectAppointed\\]|\\[ProjectUnlock\\]" BepInEx/LogOutput.log
```

### UpdateProjects observer

Surface:

- `AICampaign.UpdateProjects(int alliance)`

Log on signature change or low heartbeat:

- alliance,
- era,
- lane,
- vanilla queued project id/name,
- Whiskey selected project id/name,
- doctrine bucket,
- funding available,
- funding needed,
- construction gate result,
- score components,
- replacement reason.

Expected replacement log shape:

```text
[ProjectDoctrine] alliance=1 lane=5 old=1 new=0 bucket=ArmsImport era=Operational1862 funding=1250000/2500000 score=2.10/1.20 reason=weapon-deficit
```

### AppointProject observer

Surface:

- `Projects.AppointProject(LoadedProjects project, int alliance, bool manualappointment=false)`

Log:

- alliance,
- project id/name,
- lane,
- UI side,
- doctrine bucket,
- previous level,
- new level,
- subsidy cost,
- funding before/after when readable,
- current fiscal posture.

Expected log shape:

```text
[ProjectAppointed] alliance=0 project=19 name="Springfield Rifles" lane=4 side=Military bucket=DomesticWeapons level=0->1 cost=750000
```

### CheckProjectUnlocks observer

Surface:

- `Projects.CheckProjectUnlocks(int alliance)`

Log:

- alliance,
- project id/name auto-added,
- date gate,
- funding available,
- whether subsidy was spent,
- project level after add.

Expected log shape:

```text
[ProjectUnlock] alliance=1 project=89 name="Organization Reform" lane=4 date=01-03-1862 level=0->1 source=CheckProjectUnlocks
```

This telemetry is necessary because `CheckProjectUnlocks` bypasses `AppointProject` and currently appears to run only during new-campaign frame-32 setup.

## Vanilla Bugs And Bug Candidates

These should be tracked as vanilla project/economy bugs. They may inform doctrine scoring, but bug fixes should be separate from doctrine patches unless a fix is required to keep doctrine safe.

### BUG-PROJ-001: Weighted project random excludes final weighted entry

Classification: confirmed call-shape bug, low/medium gameplay impact.

Evidence:

- `GetNextProjectRandom` builds a repeated-ID weighted list.
- It returns `list[Random.Range(0, list.Count - 1)]`.
- Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62067`.

Unity integer `Random.Range(min, max)` excludes `max`, so the last list index is not selectable. This biases every vanilla project random selection away from whichever repeated entry lands last.

Spec handling:

- Whiskey bypasses this bug whenever it wins the scoring contest in `ProjectSelectionPatch`, because the Prefix writes `nextprojecttoresearch[lane]` before vanilla seeds an empty slot.
- The bug matters only when Whiskey has no preferred candidate above margin and vanilla falls back to random choice.
- A future vanilla bug-fix patch can replace only the index selection with `Random.Range(0, list.Count)` if patching that method is acceptable.
- If not patched, the doctrine scorer still reduces impact by replacing bad `nextprojecttoresearch` choices when it has a clear margin.

### BUG-PROJ-002: Market Reform likely no-op

Classification: confirmed decompile no-op candidate, needs runtime or IL verification before patching.

Evidence:

- In market painting, vanilla reads `GetProjectLevel(98)` and `GamePrefs.project_marketeffects`.
- It does not multiply the market capacity paint value by the computed project effect.
- Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:31985`.

Spec handling:

- `ProjectDoctrineCatalog` should mark project `98` as `FullyBrokenUntilReviewed`.
- Score project `98` with a strong negative value such as `-1000f`.
- If vanilla queues project `98`, replace it whenever any non-placeholder, non-suppressed Economy-lane candidate exists.

### BUG-PROJ-003: Civil Order raiding modifier likely no-op

Classification: confirmed decompile no-op candidate for raiding half; casualty half works.

Evidence:

- Casualty support modifier uses project `107`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:70920`.
- Raiding support code computes `Mathf.Max(0.01f, 1f + level * project_civilorderraids)` twice and discards the result before support adjustment.
- Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:113751`.

Spec handling:

- `ProjectDoctrineCatalog` should mark project `107` as `PartiallyBrokenUntilReviewed`.
- Doctrine may score `107` for casualty/civil-order pressure.
- Do not claim `107` reduces raiding support effects until the bug is fixed or IL proves decompile lost an assignment.

### BUG-PROJ-004: Date gates are not appointment gates

Classification: confirmed vanilla behavior; likely design gap, not necessarily a crash bug.

Evidence:

- `LoadedProjects.Import` reads `datefrom[0]` and `datefrom[1]`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:212615`.
- `Projects.IsAppointable` checks level cap, non-repeating status, policies, scenario, DLC, subsidies/prestige. It does not check `datefrom`.
- Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213338`.
- `CheckProjectUnlocks` is the only found date gate use and is called during new campaign frame 32.
- Call site: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:30088`.

Spec handling:

- Whiskey doctrine must implement historical timing itself because vanilla appointability does not.
- Do not assume vanilla date fields block early project appointment.
- Early project scoring should use `EraStage`, chapter, and `datefrom` fields as doctrine weights.
- The scorer must apply an out-of-window penalty at least equal to the replacement margin when a project is materially early for the current campaign date or era.
- This spec does not authorize patching `IsAppointable` to add a hard date gate.

### OBS-PROJ-005: CheckProjectUnlocks appears to be init-only project seeding

Classification: confirmed call-path behavior, currently demoted from bug.

Evidence:

- Full grep found one call site plus the method definition.
- The call site is during new-campaign frame `32`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:30088`.
- `CheckProjectUnlocks` checks date, scenario, DLC, not already researched, and `IsAppointable(...)`.
- If true, it directly adds the project ID to `GameVars.alliance[alliance].projects`.
- It does not call `AppointProject`, does not subtract subsidy funding, does not update `lastmonthprojectsresearched`, and only calls `UpdateOneTimeProjectsEffects`.
- Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:213578`.

Spec handling:

- Treat this as scenario-start seeding unless runtime proves another path.
- Add observer telemetry to confirm what it adds in fresh starts.
- Do not patch it in the doctrine slice.
- Doctrine scorer should not rely on `CheckProjectUnlocks` to handle timing or spending.

### BUG-PROJ-006: Credit emergency personality is blunt and project-only

Classification: confirmed vanilla limitation, not necessarily a bug.

Evidence:

- `GetAIPersonality` returns emergency credit personality when recruitment emergency rating gate fails.
- Anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62718`.
- W&L emergency credit profile project list is only `97 Improve Credit Rating`.

Spec handling:

- Whiskey should preserve the emergency fallback but add pre-emergency doctrine so `97`, `96`, and fiscal lane changes happen before hard gates close.

## Persistence And State

Slice 1 doctrine should be stateless except for bounded observer signatures and optional in-memory previous subsidy funding samples used for `ProjectLaneIntent.NetFundingPerDay`.

No sidecar persistence is required for:

- catalog entries,
- signal computation,
- score output,
- replacement hysteresis based on current lane funding,
- observer once/signature logs.

If a later plan adds debounced collapse risk, multi-day funding-rate history, or remembered project commitments that must survive save/load, it must extend the existing Whiskey sidecar persistence contract and add save/load tests.

## Not Verified Yet

- `usedgroup=0` as military-side UI and `usedgroup=1` as civil-side UI is inferred from parsed project rows plus `ContentFoldersLeftRight[usedgroup]`; runtime UI screenshot proof is not captured in this spec.
- `PortViability` can probably use `AICampaign.aifaction` port blocked fields, but the snapshot lifetime during project/fiscal cadence needs runtime validation.
- `RecognitionWindow` has a real vanilla source through `GameVars.Alliance.GetInterventionProbability`, but the correct side-specific interpretation still needs log validation.
- `CheckProjectUnlocks` currently has only one found call site. Runtime should confirm it fires only during campaign initialization and what projects it adds.
- `Market Reform` and Civil Order raiding no-op findings are based on decompile shape. IL/runtime validation should happen before bug-fix patches, but doctrine can safely suppress broken sub-effects now.

## Patch Boundaries

Allowed initial surfaces:

- Extend pure scoring under `src/WhiskeyRealism/Strategic/Projects/`.
- Extend `ProjectSelectionPatch` only enough to pass doctrine inputs into the scorer.
- Add observer-only patches for `Projects.AppointProject` and `Projects.CheckProjectUnlocks`.
- Add bounded logging in existing `ProjectSelectionPatch`.
- Feed project lane intent into fiscal intent as read-only data. This slice may log starved lanes but does not yet act on them.

Deferred surfaces:

- Do not patch `Projects.UpdateProjectEffects` in the doctrine slice.
- Do not patch `Projects.AppointProject` to change spending in the doctrine slice.
- Do not patch `CheckProjectUnlocks` behavior until observer data confirms the scope and impact.
- Do not patch `AICampaign.UseSubsidyForPurpose` until lane starvation is observed and a narrower fiscal-lane nudge is insufficient.
- Do not add fiscal subsidy-lane mutation in this spec unless the implementation plan explicitly owns the `FiscalIntent -> FinancialAIPatch` path with tests.
- Do not add direct weapon-order doctrine in this slice; rifle/cannon procurement lives under `AICampaign.CheckPurchaseWeapons` and `WeaponList.PlaceWeaponOrder`.

## Verification Requirements

Console harness:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Build:

```bash
./build.sh
```

Runtime smoke expectations:

- `[once:project-selection]` still fires.
- `[once:project-doctrine-catalog]`, `[once:project-appoint-observer]`, and `[once:project-unlock-observer]` fire when the relevant systems initialize.
- `[ProjectDoctrine]` replacement/retain logs appear only on signature change or bounded heartbeat.
- `[ProjectAppointed]` appears when a project is actually appointed.
- `[ProjectUnlock]` appears when `CheckProjectUnlocks` seeds a project.
- New observer logs appear for both alliance `0` and `1`.
- Logs show lane, bucket, queued project, funding, and score components.
- No repeated patch exceptions.
- No project-effect mutation occurs from observers.
- Any behavior changes should appear only through `nextprojecttoresearch[lane]`.

DLL-affecting closeout must follow root `AGENTS.md`: build, deploy, and hash-verify deployed DLL before reporting ready for smoke.

## Success Criteria

Functional:

- Both AI sides score projects using history plus dynamic campaign state.
- Union can pursue blockade/river/logistics/industry pressure when winning and stabilize via credit/manpower/logistics when losing.
- CSA can pursue imports/diplomacy/asymmetric naval defense/protraction when viable and pivot to survival under blockade, credit, or manpower collapse.
- Project choices remain bounded and vanilla-compatible.

Evidence:

- Console tests cover catalog classification, side/era doctrine weights, fiscal stress scoring, blockade/port gating, bug suppression, and lane intent.
- Runtime logs show both sides selecting or retaining projects with intelligible reasons.
- Vanilla bug candidates are logged in the spec and not silently treated as working mechanics.

Non-goals:

- No direct edits to `Config/projects.dat`.
- No direct game-install edits.
- No hidden economic parity bonus for CSA.
- No broad rewrite of vanilla project appointment or effects.
- No tactical weapon procurement changes in this slice.

## Implementation Plan Shape

The implementation plan should be split into these tasks:

0. Resolve adversarial-review issues and update the spec before plan-writing.
1. Add `ProjectDoctrineCatalog` and tests.
2. Add `ProjectDoctrineSignals` and cheap signal builder tests.
3. Add `ProjectDoctrineScorer` and side/era/dynamic tests, including strong suppression, date penalties, and commitment hysteresis.
4. Wire scorer into `ProjectSelectionPatch` without new behavior surfaces.
5. Add project observer telemetry for selection and appointment.
6. Add `CheckProjectUnlocks` observer telemetry.
7. Add fiscal lane intent output and optional non-mutating fiscal telemetry.
8. Run console harness and build.
9. Deploy/hash-verify and runtime smoke.

An adversarial review should be run before writing the detailed implementation plan.
