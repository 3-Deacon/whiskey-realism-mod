# Archived Plans

Implementation plans whose patches have shipped and been verified. Frozen artifacts — kept for traceability, not maintained.

**Source-of-truth order:** shipped code > [`docs/patch-catalog.md`](../../../patch-catalog.md) > per-patch design doc > umbrella spec > archived plan. If the archive disagrees with shipped code or the patch catalog, trust the code.

| Plan | Slice / patches shipped |
|---|---|
| [`2026-05-03-strategic-brain-implementation.md`](2026-05-03-strategic-brain-implementation.md) | Slice A v0.2.0 / v0.2.1 / v0.2.1.1 — patches #1, #2, #6, #9, #10–#14, persistence pair |
| [`2026-05-03-front-sector-ledger-implementation.md`](2026-05-03-front-sector-ledger-implementation.md) | `FrontSectorLedger` (transfer-budget guard for #3) |
| [`2026-05-03-historical-army-areas-implementation.md`](2026-05-03-historical-army-areas-implementation.md) | `ArmyAreaLedger` + #15 |
| [`2026-05-03-grand-strategy-project-selection-implementation.md`](2026-05-03-grand-strategy-project-selection-implementation.md) | `GrandStrategyRegistry` + objective tagging + #17 |
| [`2026-05-04-fiscal-economy-ai-implementation.md`](2026-05-04-fiscal-economy-ai-implementation.md) | Fiscal posture/intent + #18, #20 (initial) |
| [`2026-05-04-formation-directive-implementation.md`](2026-05-04-formation-directive-implementation.md) | `FormationDirectiveLedger` |
| [`2026-05-04-construction-intent-ledger-implementation.md`](2026-05-04-construction-intent-ledger-implementation.md) | `ConstructionIntentLedger` + #23 observer |
| [`2026-05-04-construction-steering-slice-b-implementation.md`](2026-05-04-construction-steering-slice-b-implementation.md) | Construction steering Slice B (#20 site bias, #24 telegraph runtime) |
| [`2026-05-04-perk-selection-steering-implementation.md`](2026-05-04-perk-selection-steering-implementation.md) | #7 role-aware perk steering |
| [`2026-05-05-defense-intent-ledger-implementation.md`](2026-05-05-defense-intent-ledger-implementation.md) | Defense Intent Ledger Slice 1+2 (#25, `CoastalDefenseCustomOrderRunner`, daily cadence) |
| [`2026-05-05-campaign-ai-performance-governor.md`](2026-05-05-campaign-ai-performance-governor.md) | Strategic cadence de-jitter + #26 `CampaignAiUpdateGovernorPatch` |
| [`2026-05-05-strategic-anti-zerg-theater-integrity.md`](2026-05-05-strategic-anti-zerg-theater-integrity.md) | Strategic anti-zerg / theater-integrity: `StrategicMovementBudget`, `DefenseCustomOrderPolicy`, `DefenseThreat.SourceKind`, asset-proximity local-only response, capital-defense package cap, #25 filter extension, #15 return-area front-budget gate |
| [`2026-05-05-strategic-operational-probe-contact.md`](2026-05-05-strategic-operational-probe-contact.md) | One-formation operational probe loop (`OperationalProbeLedger`, `OperationalProbeRuntime`, `OperationalTempoDoctrine`); enemy-reaction pause, overmatch withdraw, favorable-contact escalate; chapter/era/season/faction/personality tempo |
| [`2026-05-05-strategic-resilience-director.md`](2026-05-05-strategic-resilience-director.md) | Strategic Resilience Director (22-task slice + perf hotfix): pure ledgers (`PhaseTruthLedger`, `ContactEvidenceLedger`, `CampaignPaceLedger`, `BattleHistoryQuery`, `TheaterPressureView`), `OffensiveAvailabilityWrapper`, `StrategicResilienceDirector`, `DirectorPublishClamp`, `DirectorMemory` persistence, `CicReviewRouter`, plus posture-modulated probe/transfer/formation/fiscal/construction/defense thresholds; perf hotfix with typed Regiment access + same-area bucket |
| [`2026-05-05-wl-camp-realism-slice1.md`](2026-05-05-wl-camp-realism-slice1.md) | Patch #29 `WlCampRealismPatch` — short-camp accounting fix, station 12 Rest reward retune, responsive bonus weighting (safe scopes), command-count dilution softening |
