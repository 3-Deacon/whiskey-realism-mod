# Patch Catalog

Canonical numbered catalog of all shipped Harmony patches. Each item has a stable ordinal (never re-used even after withdrawal) and a one-line description with file path + decompile coordinates.

| # | Patch | Type | File | Targets (decompile line) | Description |
|---|---|---|---|---|---|
| 1 | `PickCampaignObjectivePatch`   | Prefix  | `Patches/PickCampaignObjectivePatch.cs` | `AICampaign.PickCampaignObjective` (17769)   | Replace vanilla random pick with active CIC plan target; vanilla fallback when no plan / player-CIC. |
| 2 | `ImportanceValuesPatch`        | Postfix | `Patches/ImportanceValuesPatch.cs`      | `AICampaign.UpdateImportanceValues` (14906)  | Multiply per-zone `importancevalues[alliance]` by TheaterCommander.GetZoneRelevance — biases downstream zone-pick automatically. |
| 3 | *(reserved for v0.2.1)*        | —       | —                                       | —                                            | — |
| 4 | *(reserved for v0.2.1)*        | —       | —                                       | —                                            | — |
| 5 | *(reserved for v0.2.1)*        | —       | —                                       | —                                            | — |
| 6 | `CommanderReplacementPatch`    | Prefix  | `Patches/CommanderReplacementPatch.cs`  | `AICampaign.CheckAICommanderReplacements` (17008) | Gate-only this slice — concrete scripted-event swap deferred to v0.2.1. Player-CIC fallback to vanilla path. |
| 7 | *(reserved for v0.2.1)*        | —       | —                                       | —                                            | — |
| 8 | *(reserved for v0.2.1)*        | —       | —                                       | —                                            | — |
| 9 | `MonthlyTickHookPatch`         | Postfix | `Patches/MonthlyTickHookPatch.cs`       | `AICampaign.Update` (11159)                  | Drives `StrategicCoordinator.NotifyDateAdvanced` from the per-frame Update; coordinator self-latches on month rollover. |
| 10 | `CampaignParametersLockPatch` | Postfix | `Patches/CampaignParametersLockPatch.cs` | `MainMenu.SetCampaignParameters` (193675)   | Override `usedcampaignagressiveness` → 1.0 and `usehistoricaipersonality` → true after vanilla writes player menu picks. Final value-lock; difficulty intentionally untouched. Gated by `OverrideVanillaSettings` config (default true). |
| 11 | `AggressivenessSliderLockPatch` | Postfix | `Patches/AggressivenessSliderLockPatch.cs` | `MainMenu.SwitchAIMode(float)` (~193739) | UI grey-out for campaign-aggressiveness slider. Snaps `aimode = 1.0`, sets displayed text to "Aggressiveness: Locked by Whiskey Realism". Gates by gameObject name to avoid touching BattlePanel's per-battle "AI Mode" toggle. |
| 12 | `HistoricCheckboxLockPatch`   | Postfix | `Patches/HistoricCheckboxLockPatch.cs`  | `MainMenu.CheckForCheckBoxUpdates` (193612) | UI grey-out for Historic/Dynamic radio. Forces CheckBoxes[0]=true, [1]=false, syncs `lastcheckboxstates`, reverses `ChooseHistoricPolicies(false)` calls if player tried to flip to Dynamic. |

**Note on UI patches:** #11 and #12 patch `MainMenu` UI methods, which is the most volatile part of the codebase across game patches. If a future GTCW update breaks them, the OnceLog markers stay silent (no first-fire). Workaround for affected users: set `OverrideVanillaSettings = false` in `BepInEx/config/dev.kyle.whiskey-realism.cfg` and pick the recommended values manually (Mediocre aggression, Historic personality) until a mod update lands.

**Persistence patches (not numbered — symmetric pair):**

| Patch | Type | File | Targets (decompile line) | Description |
|---|---|---|---|---|
| `AICampaignSaveLoadPatch.SavePatch` | Postfix | `Patches/AICampaignSaveLoadPatch.cs` | `AICampaign.Save` (16631) | Writes `whiskeyrealism.json` sidecar inside the save folder. |
| `AICampaignSaveLoadPatch.LoadPatch` | Postfix | `Patches/AICampaignSaveLoadPatch.cs` | `AICampaign.Load` (16435) | Reads sidecar; falls back to fresh init with explicit log if missing. |

---

## Conventions

- **Ordinal stability:** Once assigned, a number is never re-used. Withdrawn patches keep their ordinal with a `(withdrawn)` note. This makes git-log and changelog references stable across time.
- **One concern per file:** Every patch class lives in its own `.cs` file under `src/WhiskeyRealism/Patches/`.
- **Targets column** lists `Class.Method` and the decompile line number from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.
- **Source-of-truth order:** shipped code > this catalog > per-patch design doc > umbrella spec > archived plan. If they disagree, trust the code.

## Pending (v0.2.1 backlog)

Reserved ordinals 3, 4, 5, 7, 8 are held for v0.2.1 patches that complement the v0.2.0 strategic core:

| # | Planned class | Target | Rationale for deferral |
|---|---|---|---|
| 3 | `MostValueableZonesPatch` (revised) | `AIArea.CalculateMostValueableAIZones` | Vanilla derives the pick live from `importancevalues + points + distancepoints`, so v0.2.0's patch #2 already biases this. v0.2.1 may add a Postfix that overrides `mostvalueableaiareaclose[aifaction]` directly when phase target diverges. |
| 4 | `TransferOfUnitsPatch` (concrete steering) | `AICampaign.CheckTransferOfUnits` | Needs Prefix-with-state-modify after smoke-test reveals consolidation thresholds. |
| 5 | `DefensiveOpsPatch` (concrete steering) | `AICampaign.CheckPickDefensiveOps` | Same — needs Prefix to override threshold. |
| 7 | `PerkSelectionPatch` (concrete steering) | `AICampaign.CheckPerkSelection` | Needs perk-id → personality-attribute mapping table. |
| 8 | `RecruitmentPatch` (concrete steering) | `AIArea.GetBestRecruitingState` | Needs concrete weighting strategy after observing how `__result` is consumed downstream. |

War-state observers (Vicksburg / Atlanta / Chattanooga town-ownership patches) and concrete commander-swap inside #6 also land in v0.2.1.
