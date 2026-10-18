# Strategic Foresight Test Plan

Produced-by: claude-opus-5-5 (high)

Frozen at the plan's gate. `prove` fills the Result column and may add cases; it removes or narrows none without a logged reason. Spec: D11 in `docs/superpowers/specs/2026-10-14-campaign-planner-design.md`; plan: `docs/superpowers/plans/2026-10-18-strategic-foresight.md`. Harness cases are the test methods the plan names (the harness file is the slice's `*Tests.cs`); smoke cases count rows in the tuning log `campaign.jsonl` of the session named in each scenario.

## Fixtures

- **FX-002A**: the investigation's 002/A geometry (Richmond 1263.6,-1010.9; Washington 1349.9,-631.6; Manassas Junction 1245.1,-680.8; Fredericksburg 1247.4,-802.6; Fairfax C.H. 1298.3,-660.6; King George C.H. 1326.2,-818.6; Bowling Green 1285.0,-874.6; Winchester 1107,-555; Romney 982.6,-515.7), relocation range 200, minimum strength 2000, hold ratio 0.6; rail counts assumed (Manassas Junction 2, Fredericksburg 1, Fairfax C.H. 0, King George C.H. 0, Bowling Green 0) and named as assumptions in the fixture.
- **FX-BASE** (the definition site of the baseline numbers; extracted from the 2026-10-18 W&L smoke's tuning log, deployed `11c2eae4`, 002/A 1861-06-02 to 06-27, `Logging Profile = FullTuning`, a session file not in the repo; `docs/loop/foresight/investigation.md` cites the rest): `[CampaignPlan]` rows with `switch=no-candidate`: Union 4, CSA 2 (06-24 with `cap=0`, 06-27); CSA decisions `initial` 06-22 and 06-26; #25 `filter-candidates` rows: alliance 1, 70, every one with `removed` equal to `forbidden` (6 to 9, the CSA's whole list); alliance 0, 70, likewise (8 to 16); `CampaignPlan:trace` `capitalThreat=0.00 floor=0` on every row of both alliances; `[CapitalFloor] alliance=0 floor=0 vanillaNeed=-42005`; `[Recon] tasks=6 unfilled=6 why=-`; the CSA's beliefs on 06-23: 2 groups (Union 2nd Division 6,653 near Winchester, Department of Virginia 5,214 at Fort Monroe) against about 15 Union groups.
- **SMOKE-30**: a fresh 002/A W&L career from 1861-06-02, the player below map level, 30 game days, `Logging Profile = FullTuning` (as FX-BASE; `TacticalTuning` keeps Tactical-layer rows only), after F10. A review day is a `[Reserves]` row of the alliance; a row is in force on a day when it is that alliance's last row of its tag on or before the day.

## Scenarios and cases

### S1 Enemy picture (F3)
- TC-PIC-ONCE `EveryManIsPlacedOnce`; TC-PIC-FADE `AFadingBeliefKeepsItsStrengthWhereLastSeen`; TC-PIC-EXPIRE `AnExpiredBeliefReturnsItsMenToTheAnchors`; TC-PIC-ANCHORS `CapitalTakesItsShareWhenUnscouted`, `ObjectiveAnchorsShareByImportance`, `AfloatBeliefsAddNoLandMass`, `AlliancesTwoToFourIgnored`; TC-PIC-DISPLACE `AScoutedAnchorPassesItsShareOn`, `EveryAnchorScoutedLeavesTheRestAtTheCapital`; TC-PIC-SUM `EstimateIsBelievedPlusPresumed`, `AScoutedTargetStillCarriesUnscoutedAnchorsNearIt`, `BalanceStillRanksByTheBelievedLayer`; TC-PIC-UNKNOWN `UnknownTotalSpreadsNothing`, `NoAnchorMeansUnplacedNotUnaccounted`; TC-PIC-BUILD `PictureBuiltOncePerReview`, `CapitalAnchorImportanceIsOne`; flags `FlagOffRestoresTheD2Presumption`, `FlagOffRestoresTheDepartmentSumAndTheOldScoutTest`.
- TC-PIC-CONSERVE (SMOKE-30): every `[EnemyPicture]` row with `total` known has `believed + faded + unaccounted + unplaced` within 2 of `total`, or `unaccounted=0` with the last-seen sum above `total`. Count: `grep -c '"event":"EnemyPicture"'` against the rows passing that test (a `python3 -I` one-liner over the `fields`).

### S2 Hold need, release, eligibility (F3)
- TC-HOLD-RATIO `HoldNeedHoldsUnseenMenAtTheHoldRatio`; TC-HOLD-165 `ASightingAt165DoesNotSwitchOffWashington` (FX-002A); TC-HOLD-FLOOR `FloorUsesOnlyUndisplacedImportance`; TC-HOLD-FACE `AnAnchorAloneMakesASectorFace`; TC-HOLD-DEPT `DepartmentNeedCountsEachElementOnce`, `DepartmentNeedLandKindsWeightedTieToLowerSector`; TC-HOLD-D9 `ASectorHoldingAgainstABelievedArmyStillRefusesAScout`.
- TC-REL-EVERY `AHoldIsReleasedOnlyWhenEveryElementItFacesIsAtTheTarget` (FX-002A: Romney does not release Manassas); TC-REL-NOTHING `ASectorFacingNothingReleasesNothing`.
- TC-ELIG-MIN `ACapacityBelowTheLiveMinimumIsIneligible`; TC-ELIG-ODDS `OddsRuleUnchangedAboveTheMinimum`, `RetainNeverReenablesBelowTheMinimum`.

### S3 Capital reach and floor (F3)
- TC-CAP-REACH `CapitalReachIsTheLargerOfRangeAndTenRoadDays`; TC-CAP-WASH `WashingtonCountsTowardRichmondsFloor` (FX-002A); TC-CAP-RAILS `RailsNotCreditedInTheReach`, `MissingSpeedArrayFallsBackToTheRange`; TC-CAP-BASE `FloorHasVanillasBaseline`; TC-CAP-THREAT `ThreatExcludesTheBaseline`; TC-CAP-VSFOUR `FloorIsNeverBelowFourVanillaNeed`.
- TC-CAP-SMOKE (SMOKE-30): `[CapitalThreat] alliance=1` rows with `weighted>0` and `floor>=2000` on 100 % of review days while the Union holds Washington (baseline FX-BASE: `capitalThreat=0.00 floor=0` in every `CampaignPlan:trace` row); `switch=capital-threat` at most once per latch.

### S4 Spotting marks (F4)
- TC-MARK-BAND `BandRoundingMatchesVanillasBands`; TC-MARK-NEWEST `PlayerSideReadsOnlyTheNewestMark`, `PlayerAllianceUnitsNeverRegisteredByVanilla`, `GarrisonsAndFleetsGetNoMark`; TC-MARK-CHANCE `CreationChanceCappedAtVanillasMaximum`, `BugIntel001NegativeTimeConstantFloorsAtAThousandthOfAYear`; TC-MARK-50 `NoMarkUntilTheGroupMovesFiftyUnits`; TC-MARK-OFFSET `OffsetPointsAwayFromTheClosestEnemy`, `OffsetBlendsWithThePreviousByAge`, `MarkRadiusFromStoredIntelligence`, `MarkRadiusGrowsWithDrift`; TC-MARK-SEED `SeededRollsAreReproducible`, `NoUnityRandomInTheMarkChannel`; TC-MARK-KEY `MarkMatchesByExactKeyOnly`; TC-MARK-NORETIRE `MarkNeverRetiresAnotherBelief`; TC-MARK-SAMEDAY `SameDaySightingBeatsAMark`, `OlderMarkIgnored`, `AStaleNewestMarkKeepsNoBeliefAlive`, `MarkDayDecodesTheCalendarDate`; TC-MARK-REVERSE `ASightingNeverRetiresAMarkBelief`, `AMarkBeliefReKeysWithinItsBaseRadius`, `AMarkCarriesNoEmbarkedTrait`, `SeenCountExcludesMarks`; TC-MARK-FIDELITY `FirstMarkComesAtTheCap`, `AGridSeenMarkIsRefusedAndDropsTheMarks`; TC-MARK-NOSCOUT `MarkBeliefNeverScouts`, `MarkBeliefCountsInTheTotals`; TC-MARK-SIDECAR `MarkSourceRoundTripsInTheSidecar`, `AnOlderFileReadsBaseRadiusZero`.
- TC-MARK-SMOKE (SMOKE-30): on both alliances, `[EnemyBeliefs]` with `marks>0` on at least one review in every 7 game days.

### S5 Covering force and Redeploy (F6)
- TC-COV-CORRIDOR `CorridorBandAndBetweenness`, `NoForwardPostPostsAtTheRear`; TC-COV-FWD `ForwardPostIsManassasJunctionIn002A`, `FairfaxInsideTheStandoffIsNotForward`; TC-COV-REAR `RearPostIsFredericksburgIn002A`, `KingGeorgeWithoutRailIsNotRear`, `ScenarioOneRangeMovesTheRearPost`; TC-COV-NONE `NoCorridorPlaceNoCoveringForce`, `CicSideHasNoCoveringForce`; TC-COV-NEED `NeedIsTheHoldRatioOfTheEnemyCapitalsEstimate`; TC-COV-MIN `NeedNeverBelowTheLiveMinimum`; TC-COV-INCUMBENT `IncumbentsKeepTheirPlace`; TC-COV-NEAREST `MembersNearestThePostWithTheHomeDiscount`, `PoolLimitedToTenDaysMarch`, `ACapitalGuardIsNeverACoveringMember`; TC-COV-WL `MapGroupPlayerNeverTheOnlyHolder`, `PlayerCommandNeverAMemberAndCountsNothing`, `CarryingGroupMayBeAMember`; TC-COV-BUSY `BusyFormationsAtThePostCount`; TC-COV-SECTOR `MembersCountInThePostsSectorOnly`, `RedeployingMemberInThePostSectorCountsInG4`; TC-COV-VERDICT `ForwardWhenMetElseRear`, `VerdictNeedsThreeReviews`, `ForwardVerdictOnCountedStrength`, `EnemyCapitalPushLeavesTheCoveringStateUntouched`, `LostCapitalWritesNoneKey`; TC-COV-MASSFOR `MassForExcludesMembersExceptAgainstTheEnemyCapital`; TC-COV-POOLS `ScoutDrawDepotAndPostsNeverTakeAMember`, `HeldBuildersAndGiveUpsNotInThePool`; TC-COV-ROLE `MemberAtPostHoldsWithheldElseRedeploys`; TC-COV-DEFENCE `DefenceMayTakeAMemberAgainstAnEnemyNearThePost`, `OffenceNeverTakesAMember`; TC-COV-UNBIND (unbind cases of requirement-covering-lifecycle: capital lost, no place, flag off, target the enemy capital); TC-COV-SIDECAR `CoveringStateRoundTripsInTheSidecar`; `UnionSideCoversToo`.
- TC-RED-PRIORITY `RedeployMovePriorityOrder`; TC-RED-TEMPO `RedeployNeedsNoTempoRelease`; TC-RED-WITHHOLD (a `return-home` and a `department-reinforcement` formation are withheld from offense only; a `covering-force` member from both pools with its exception).
- TC-COV-CENSUS (SMOKE-30, first review): `[CoveringCorridor] alliance=1` names `forward=Manassas Junction rear=Fredericksburg`; otherwise `question-corridor-posts` is raised and the run stops.
- TC-COV-SMOKE (SMOKE-30): criterion-covering-held (at least 90 % of review days after the third with a post and `members>=1`; `short` reported); criterion-axis-not-stripped (0 covering members in `MainEffort`, `Support` or `DepotBuilder` issue rows while `short>0`); guardrail A/B with `Enable Covering Force` off on the same save.

### S6 Commitment and return home (F7)
- TC-COM-DIP `ADispatchedMemberStaysMassedThroughAReadinessDip`, `AnUncommittedFormationStillNeedsStepTwo`; TC-COM-RELIEF `RetreatOrLowMoraleStillRelieves`, `LowSupplyStillRecovers`; TC-COM-CHAIN `TheCsaMainEffortSurvivesTheMarch`; TC-COM-NOMARCH `NoNewMarchAtStepOne`; TC-COM-WAIT `AnIssuedMoveIsNotJudgedWhileReadinessIsLow`, `TheReadinessWaitEndsAfterFourteenDays`; flag `FlagOffRestoresD5`.
- TC-COM-KEY `CommitmentSurvivesANoCandidateReview`, `TheReinforcementHasASortie`, `HoldNeverReservesACommittedFormation`, `AnAppointmentRekeysTheSortie`; TC-COM-DELIST `RedeployIsTakenOffVanillasOffensiveList`; TC-RET-SEND `ACommittedFormationTheMassLeftGoesHome`, `SortieRecordedAtFirstDispatchOnly`, `OriginKeptAcrossReissues`, `AnEndedDecisionSendsItsSortiesHome`, `TheNewMassTakesFirst`; TC-RET-LIFE `ArrivedSortieIsDeleted`, `GaveUpSortieHoldsInPlace`, `AReserveMayTakeAReturningFormation`, `ReturningCountsInNoHold`, `PlayerCommandNeverHasASortie`, `SortiesRoundTripInTheSidecar`; TC-RET-CAP `ReturnHomeNeverCrowdsOutTheMass`.
- TC-COM-SMOKE (SMOKE-30): criterion-mass-kept (0 `switch=no-candidate` while `[Reserves] committed>0`; baseline FX-BASE: CSA 2 `no-candidate` rows in 06-22..06-27) and its guardrail (0 `MainEffort` issue rows at `readiness<2`).
- TC-RET-SMOKE (SMOKE-30): criterion-return-home (100 % within 2 reviews) and its guardrail.

### S7 Department reinforcement (F8)
- TC-DEP-ONE `TheShortestDepartmentGetsOneFormation`, `OneInFlightPerReceivingDepartment`, `AtMostOnePickPerReview`, `WorksWithNoMainEffort`; TC-DEP-DONOR `TheDonorsHoldStaysMet`, `SoonestWithinTenDaysRailCredited`, `NeverAReserveMassCoveringGarrisonOrBuilder`, `NeverThePlayersCommandOrMapGroup`, `AMarchingArmyDoesNotHoldItsDepartment`, `NoReturningOrCommittedDonor`; TC-DEP-EXPIRE `ARecordExpiresAfterTwentyDays`; TC-DEP-LIFE `ThePickTransfersTheDepartmentAtOnce`, `ArrivalClearsTheRecord`, `GiveUpTransfersBack`, `PrunedFormationClearsTheRecord`, `DepartmentInFlightRoundTripsInTheSidecar`.
- TC-DEP-SMOKE (SMOKE-30): criterion-department-reinforced and its guardrail.

### S8 Depot builders (F9)
- TC-DEPOT-READY `AnUnreadyFormationNeverBuilds`; TC-DEPOT-SURPLUS `ABuilderLeavesItsSectorsHoldMet`, `TheMassGivesABuilderOnlyWhenNoSurplusExists`, `TheMassKeepsItsLastBody`, `NeitherHoldReserveNorCoveringBuilds`; TC-DEPOT-AOV `TheInvestigationsArmyOfVirginiaIsNotPicked`.
- TC-DEPOT-SMOKE (SMOKE-30): criterion-builders-fit and its guardrail.

### S9 Live minimum strength (F5)
- TC-MIN-PUBLISH `PlanPublishesTheScenariosMinimum`, `FallbackOnlyWhenTheReadIsNotPositive`, `HoldTargetIsTheLiveMinimumInAQuietSector`; TC-MIN-ONESITE `EveryFloorDerivesFromThePlan`, `ReinforcementRoutingReadsThePlansMinimum`.
- TC-MIN-SMOKE (SMOKE-30): `[CampaignPlanner] first review` with `minStrength=2000` for both alliances.

### S10 Interception (F2)
- TC-INT-MODE `ObservesWhileThePlannerRunsForTheAlliance`, `FiltersWhenTheFlagIsOff`, `FiltersWhenThePlanIsStale`, `FiltersForEurope`, `FiltersForACicSide`, `FiltersWhenThePlannerOrItsExecutorsAreOff`; TC-INT-NOREMOVE `ObservingPrefixRemovesNothingAndPostfixRevertsNothing`, `ObservedRowCountsWhatWouldBeRemoved`, `ObservedRowNotGatedOnVerbose`.
- TC-INT-SMOKE guard: the count is valid only with at least one observed row per alliance in the run (a run with none is inconclusive, not a pass).
- TC-INT-SMOKE (SMOKE-30): criterion-interception-restored (every alliance-0/1 #25 row `filter-observed`; baseline FX-BASE: 70 alliance-1 rows, every one `removed=forbidden`) and its guardrail (`[Reserves] vanillaDefWithheld=0`).

### S11 Telemetry (F1)
- TC-TEL-ISSUE `IssueRowNamesUnitRoleTargetObjectiveAndOrigin`, `IssueRowsNeverExceedTheDailyCap`; TC-TEL-TRACE `TraceCarriesEachCandidatesCoordinates`; TC-TEL-RESERVES `ReservesRowNamesCapitalAndHoldShares`, `ReservesRowSignatureUnchangedWhenNothingChanges`; TC-TEL-REASONS `DefenseFilterReasonsAreTalliedAndSorted`; TC-TEL-TAGS `NewRowsAreRegisteredInTheTagPolicy`, `RowNamesAreQuoted`, `ARefusedRetryWritesNoIssueRow`, `ReservesRowEveryReview`, `ReservesDefenceCountsReadYesterdaysAllocation`.

### S12 Budget (all)
- criterion-review-budget (SMOKE-30 at 20x): `campaign.planner.review` under 4 ms and `campaign.planner.detect` under 2 ms per alliance (their scope thresholds); a Performance row over either is a failure.

## Coverage matrix (derived from the spec's requirement lines by `python3 -I docs/loop/foresight/gen_matrix.py <worktree>`; regenerate, never edit)

<!-- matrix:begin -->
| Requirement | Spec | Test cases | Result | Defect or concession |
|---|---|---|---|---|
| requirement-enemy-total | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:271 | TC-PIC-UNKNOWN, TC-PIC-CONSERVE |  |  |
| requirement-picture-partition | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:272 | TC-PIC-ONCE, TC-PIC-BUILD, TC-PIC-FADE, TC-PIC-EXPIRE, TC-PIC-ANCHORS, TC-PIC-DISPLACE, TC-PIC-SUM, TC-PIC-CONSERVE |  |  |
| requirement-hold-need | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:283 | TC-HOLD-RATIO, TC-HOLD-165, TC-HOLD-FLOOR, TC-HOLD-FACE, TC-HOLD-DEPT, TC-HOLD-D9 |  |  |
| requirement-hold-release | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:284 | TC-REL-EVERY, TC-REL-NOTHING |  |  |
| requirement-candidate-floor-capacity | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:285 | TC-ELIG-MIN, TC-ELIG-ODDS |  |  |
| requirement-marks-player-side | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:289 | TC-MARK-NEWEST, TC-MARK-BAND, TC-MARK-SMOKE |  |  |
| requirement-marks-ai-side | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:290 | TC-MARK-CHANCE, TC-MARK-50, TC-MARK-OFFSET, TC-MARK-SEED, TC-MARK-FIDELITY, TC-MARK-SMOKE |  |  |
| requirement-mark-beliefs | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:291 | TC-MARK-KEY, TC-MARK-NORETIRE, TC-MARK-SAMEDAY, TC-MARK-REVERSE, TC-MARK-NOSCOUT, TC-MARK-SIDECAR |  |  |
| requirement-capital-window | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:295 | TC-CAP-REACH, TC-CAP-WASH, TC-CAP-RAILS, TC-CAP-SMOKE |  |  |
| requirement-capital-floor | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:296 | TC-CAP-BASE, TC-CAP-THREAT, TC-CAP-VSFOUR, TC-CAP-SMOKE |  |  |
| requirement-covering-post | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:301 | TC-COV-CORRIDOR, TC-COV-FWD, TC-COV-REAR, TC-COV-NONE, TC-COV-CENSUS |  |  |
| requirement-covering-size | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:302 | TC-COV-NEED, TC-COV-MIN |  |  |
| requirement-covering-members | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:303 | TC-COV-INCUMBENT, TC-COV-NEAREST, TC-COV-WL, TC-COV-BUSY, TC-COV-SECTOR |  |  |
| requirement-covering-post-choice | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:304 | TC-COV-VERDICT |  |  |
| requirement-covering-hold | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:305 | TC-COV-MASSFOR, TC-COV-POOLS, TC-COV-ROLE, TC-COV-DEFENCE, TC-COV-SMOKE |  |  |
| requirement-covering-lifecycle | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:306 | TC-COV-UNBIND, TC-COV-SIDECAR |  |  |
| requirement-redeploy-role | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:310 | TC-RED-PRIORITY, TC-RED-TEMPO, TC-RED-WITHHOLD |  |  |
| requirement-department-reinforcement | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:314 | TC-DEP-ONE, TC-DEP-DONOR, TC-DEP-LIFE, TC-DEP-EXPIRE, TC-DEP-SMOKE |  |  |
| requirement-commitment | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:318 | TC-COM-DIP, TC-COM-KEY, TC-COM-RELIEF, TC-COM-CHAIN, TC-COM-SMOKE |  |  |
| requirement-commitment-mover | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:319 | TC-COM-NOMARCH, TC-COM-WAIT, TC-COM-DELIST |  |  |
| requirement-return-home | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:320 | TC-RET-SEND, TC-RET-LIFE, TC-RET-CAP, TC-RET-SMOKE |  |  |
| requirement-depot-builder | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:324 | TC-DEPOT-READY, TC-DEPOT-SURPLUS, TC-DEPOT-AOV, TC-DEPOT-SMOKE |  |  |
| requirement-live-min-strength | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:328 | TC-MIN-PUBLISH, TC-MIN-ONESITE, TC-MIN-SMOKE |  |  |
| requirement-interception | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:332 | TC-INT-MODE, TC-INT-NOREMOVE, TC-INT-SMOKE |  |  |
| requirement-telemetry | docs/superpowers/specs/2026-10-14-campaign-planner-design.md:336 | TC-TEL-ISSUE, TC-TEL-TRACE, TC-TEL-RESERVES, TC-TEL-REASONS, TC-TEL-TAGS |  |  |
<!-- matrix:end -->
