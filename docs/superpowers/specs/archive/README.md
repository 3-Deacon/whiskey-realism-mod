# Archived Specs

Design specs whose corresponding implementation has shipped. Frozen artifacts — kept for design rationale and historical context, not maintained against current code.

**Source-of-truth order:** shipped code > [`docs/patch-catalog.md`](../../../patch-catalog.md) > per-patch design doc > umbrella spec > archived plan. If an archived spec disagrees with shipped code or the patch catalog, trust the code.

Internal cross-references in these archived files have been rewritten to point at the corresponding archive paths.

| Spec | What it specified | Implementation |
|---|---|---|
| [`2026-05-02-strategic-brain-design.md`](2026-05-02-strategic-brain-design.md) | Slice A umbrella — six locked design choices, two-tier hierarchy, era × faction × officer personality, phased operational plans, weekly→daily cadence | Patches #1, #2, #6, #9, #10–#14 (v0.2.0–v0.2.1.1) plus all of v0.2.2 enrichment |
| [`2026-05-03-grand-strategy-and-research-tree-design.md`](2026-05-03-grand-strategy-and-research-tree-design.md) | Faction grand-strategy profiles, objective strategy tagging, research-tree project steering, policy timing | Patches #17 `ProjectSelectionPatch`, #19 `PolicySelectionPatch` |
| [`2026-05-04-fiscal-economy-ai-design.md`](2026-05-04-fiscal-economy-ai-design.md) | Fiscal posture/intent, treasury/debt/credit/supply-protection signals | Patches #18 `FinancialAIPatch`, #20 `EconomyConstructionPatch` (initial private-bias surface) |
| [`2026-05-04-formation-directive-design.md`](2026-05-04-formation-directive-design.md) | Independent-formation classification, division/corps/army snapshots, directive ledger | `FormationDirectiveLedger` + integration with #15/#16 |
| [`2026-05-04-construction-vanilla-deep-dive.md`](2026-05-04-construction-vanilla-deep-dive.md) | Vanilla construction-system research artifact (decompile pass) | Informed the construction intent ledger spec and later #27 fort-spam governor |
| [`2026-05-04-construction-intent-ledger-design.md`](2026-05-04-construction-intent-ledger-design.md) | Smart-building / IIP / telegraph / depot intent ledger | Patches #23 observer, #20 site bias, #24 telegraph runtime |
| [`2026-05-04-perk-selection-steering-design.md`](2026-05-04-perk-selection-steering-design.md) | Role-aware campaign army/fleet perk scoring | Patch #7 `PerkSelectionPatch` |
| [`2026-05-05-defense-intent-ledger-design.md`](2026-05-05-defense-intent-ledger-design.md) | Daily defense ledger; asset role classification; multi-unit aggregator; capital-coexistence with #4; three enforcement surfaces | Patch #25 candidate-filter, `CoastalDefenseCustomOrderRunner`, daily cadence migration, `AssetRoleCatalog`, `DefenseIntentRuntime` |
| [`2026-05-05-strategic-resilience-director-design.md`](2026-05-05-strategic-resilience-director-design.md) | War-length pressure model: posture publisher with personality-clamped (±50% of personality delta) threshold modifiers, vanilla-bound `CollapseRisk` thresholds, `PhaseTruthLedger`, `ContactEvidenceLedger`, `OffensiveAvailabilityWrapper` mirroring 13 vanilla offensive-availability gates | Strategic Resilience Director slice (`StrategicResilienceDirector`, `CampaignPaceLedger`, `BattleHistoryQuery`, `TheaterPressureView`, `DirectorPosture`, `DirectorMemory`, `DirectorPublishClamp`, `CicReviewRouter`); deletes unused `TheaterCommander`; modulates probe options, transfer/hold thresholds, formation directive gates, fiscal/construction scoring, defense budgets |
| [`2026-05-05-wl-camp-realism-slice1-design.md`](2026-05-05-wl-camp-realism-slice1-design.md) | Short-camp accounting fix, station 12 Rest reward retune (`6h/9h` → `3h/6h`), responsive recent-history weighting in safe payoff/UI scopes only (suppressed in diary/event threshold scopes), command-count dilution softening for Drill/Motivate/Recruitment/Readiness | Patch #29 `WlCampRealismPatch` |
