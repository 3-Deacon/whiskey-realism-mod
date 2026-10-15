using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WhiskeyRealism;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Util;

using static TestFixtures;
using static TestHarness;

internal static class PlayerOrderTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("player order priority ranks retreat above fallback", PlayerOrderPriorityRanksRetreatAboveFallback),
            ("player order mapper keeps type fifteen retreat only", PlayerOrderMapperKeepsTypeFifteenRetreatOnly),
            ("player order mapper maps fallback to type twelve", PlayerOrderMapperMapsFallbackToTypeTwelve),
            ("player order mapper maps tactical support attack and movement lanes", PlayerOrderMapperMapsTacticalSupportAttackAndMovementLanes),
            ("player order mapper maps campaign build rows", PlayerOrderMapperMapsCampaignBuildRows),
            ("player order active priority is conservative for ambiguous vanilla types", PlayerOrderActivePriorityIsConservativeForAmbiguousVanillaTypes),
            ("player order dedupe blocks tactical over active type fifteen", PlayerOrderDedupeBlocksTacticalOverActiveTypeFifteen),
            ("player order dedupe protects vanilla transition orders", PlayerOrderDedupeProtectsVanillaTransitionOrders),
            ("player order dedupe blocks hold over active attack per vanilla", PlayerOrderDedupeBlocksHoldOverActiveAttackPerVanilla),
            ("player order dedupe protects higher same scope priority", PlayerOrderDedupeProtectsHigherSameScopePriority),
            ("player order dedupe preserves vanilla type eleven unless higher priority", PlayerOrderDedupePreservesVanillaTypeElevenUnlessHigherPriority),
            ("player order dedupe blocks ambiguous unknown type seven", PlayerOrderDedupeBlocksAmbiguousUnknownTypeSeven),
            ("player order dedupe blocks ambiguous unknown type twelve", PlayerOrderDedupeBlocksAmbiguousUnknownTypeTwelve),
            ("player order dedupe allows emergency retreat over ambiguous tactical order", PlayerOrderDedupeAllowsEmergencyRetreatOverAmbiguousTacticalOrder),
            ("player order dedupe stale session shadow does not prove ambiguous active", PlayerOrderDedupeStaleSessionShadowDoesNotProveAmbiguousActive),
            ("player order dedupe mismatched target shadow does not prove ambiguous active", PlayerOrderDedupeMismatchedTargetShadowDoesNotProveAmbiguousActive),
            ("player order dedupe mismatched intent shadow does not prove ambiguous active", PlayerOrderDedupeMismatchedIntentShadowDoesNotProveAmbiguousActive),
            ("player order dedupe stale active retreat allows next tactical candidate", PlayerOrderDedupeStaleActiveRetreatAllowsNextTacticalCandidate),
            ("player order dedupe battle ended active retreat allows next tactical candidate", PlayerOrderDedupeBattleEndedActiveRetreatAllowsNextTacticalCandidate),
            ("player order dedupe stale active transition allows retreat", PlayerOrderDedupeStaleActiveTransitionAllowsRetreat),
            ("player order dedupe stale same type does not vanilla block", PlayerOrderDedupeStaleSameTypeDoesNotVanillaBlock),
            ("player order dedupe enforces campaign tactical scope gap", PlayerOrderDedupeEnforcesCampaignTacticalScopeGap),
            ("player order dedupe allows emergency retreat over invalid campaign order", PlayerOrderDedupeAllowsEmergencyRetreatOverInvalidCampaignOrder),
            ("player order dedupe allows emergency retreat over actionable campaign order", PlayerOrderDedupeAllowsEmergencyRetreatOverActionableCampaignOrder),
            ("player order dedupe blocks campaign over fresh tactical without gap", PlayerOrderDedupeBlocksCampaignOverFreshTacticalWithoutGap),
            ("player order dedupe allows campaign over stale tactical", PlayerOrderDedupeAllowsCampaignOverStaleTactical),
            ("player order dedupe allows campaign over battle ended tactical", PlayerOrderDedupeAllowsCampaignOverBattleEndedTactical),
            ("player order dedupe allows campaign over tactical with priority gap", PlayerOrderDedupeAllowsCampaignOverTacticalWithPriorityGap),
            ("player order dedupe blocks type fourteen unless active hold", PlayerOrderDedupeBlocksTypeFourteenUnlessActiveHold),
            ("player order dedupe allows type fourteen past vanilla preflight on active hold", PlayerOrderDedupeAllowsTypeFourteenPastVanillaPreflightOnActiveHold),
            ("player order dedupe type fourteen does not permanently yield", PlayerOrderDedupeTypeFourteenDoesNotPermanentlyYield),
            ("player order cache clears by battle identity", PlayerOrderCacheClearsByBattleIdentity),
            ("player order cache clears old battle at new boundary", PlayerOrderCacheClearsOldBattleAtNewBoundary),
            ("player order cache command change clears all units", PlayerOrderCacheCommandChangeClearsAllUnits),
            ("player order dedupe decide does not record shadow", PlayerOrderDedupeDecideDoesNotRecordShadow),
            ("player order dedupe preview classifies while writes disabled", PlayerOrderDedupePreviewClassifiesWhileWritesDisabled),
            ("player order accepted shadow tracks request and vanilla active", PlayerOrderAcceptedShadowTracksRequestAndVanillaActive),
            ("player order signature cache suppresses repeated material match", PlayerOrderSignatureCacheSuppressesRepeatedMaterialMatch),
            ("player order issuance throttle suppresses rapid changed candidates", PlayerOrderIssuanceThrottleSuppressesRapidChangedCandidates),
            ("player order failed bridge attempt throttles rejected candidate", PlayerOrderFailedBridgeAttemptThrottlesRejectedCandidate),
            ("player order diagnostics signatures cap repeated logs", PlayerOrderDiagnosticsSignaturesCapRepeatedLogs),
            ("player order composer returns none without orchestrator", PlayerOrderComposerReturnsNoneWithoutOrchestrator),
            ("player order composer returns none for player cic", PlayerOrderComposerReturnsNoneForPlayerCic),
            ("player order composer prefers retreat from withdrawal with valid exit", PlayerOrderComposerPrefersRetreatFromWithdrawalWithValidExit),
            ("player order composer maps fallback role to fallback line", PlayerOrderComposerMapsFallbackRoleToFallbackLine),
            ("player order composer maps hold task to hold objective", PlayerOrderComposerMapsHoldTaskToHoldObjective),
            ("player order composer maps hold intent without objective to hold position", PlayerOrderComposerMapsHoldIntentWithoutObjectiveToHoldPosition),
            ("player order composer maps support role to support main effort", PlayerOrderComposerMapsSupportRoleToSupportMainEffort),
            ("player order composer maps probe task to probe objective", PlayerOrderComposerMapsProbeTaskToProbeObjective),
            ("player order composer returns none without concrete target", PlayerOrderComposerReturnsNoneWithoutConcreteTarget),
            ("player order target policy falls back to visible enemy for attack", PlayerOrderTargetPolicyFallsBackToVisibleEnemyForAttack),
            ("player order composer emits reason for diagnostics", PlayerOrderComposerEmitsReasonForDiagnostics),
            ("player order runtime classifies vanilla order scene activity", PlayerOrderRuntimeClassifiesVanillaOrderSceneActivity),
            ("tactical wl guard allows non wl action", TacticalWlGuardAllowsNonWlAction),
            ("tactical wl guard allows when config disabled", TacticalWlGuardAllowsWhenConfigDisabled),
            ("tactical wl guard denies player subordinate feud movement", TacticalWlGuardDeniesPlayerSubordinate),
            ("tactical wl guard denies feud move with attached subordinate", TacticalWlGuardDeniesFeudMoveWithAttachedSubordinate),
            ("tactical wl guard allows ai chain feud move", TacticalWlGuardAllowsAiChainFeudMove),
        });
    }

    private static void TacticalWlGuardAllowsNonWlAction()
    {
        var decision = TacticalWlActionGuard.Decide(
            configEnabled: true,
            dlcScenarioActive: false,
            unitUnderCommander: true,
            groupUnderCommander: true,
            attachedUnitUnderCommander: true);

        AssertTrue(decision.Allow, "non-W&L scenarios must remain vanilla");
        AssertEqual("wl-inactive", decision.Reason, "reason");
    }

    private static void TacticalWlGuardAllowsWhenConfigDisabled()
    {
        var decision = TacticalWlActionGuard.Decide(
            configEnabled: false,
            dlcScenarioActive: true,
            unitUnderCommander: true,
            groupUnderCommander: false,
            attachedUnitUnderCommander: false);

        AssertTrue(decision.Allow, "disabled config must leave vanilla behavior alone");
        AssertEqual("config-disabled", decision.Reason, "reason");
    }

    private static void TacticalWlGuardDeniesPlayerSubordinate()
    {
        var decision = TacticalWlActionGuard.Decide(
            configEnabled: true,
            dlcScenarioActive: true,
            unitUnderCommander: true,
            groupUnderCommander: false,
            attachedUnitUnderCommander: false);

        AssertTrue(!decision.Allow, "player-subordinate feud movement should be denied");
        AssertEqual("player-subordinate", decision.Reason, "reason");
    }

    private static void TacticalWlGuardDeniesFeudMoveWithAttachedSubordinate()
    {
        var decision = TacticalWlActionGuard.Decide(
            configEnabled: true,
            dlcScenarioActive: true,
            unitUnderCommander: false,
            groupUnderCommander: false,
            attachedUnitUnderCommander: true);

        AssertTrue(!decision.Allow, "feud movement should be denied when the group contains a player-subordinate unit");
        AssertEqual("player-subordinate-attached", decision.Reason, "reason");
    }

    private static void TacticalWlGuardAllowsAiChainFeudMove()
    {
        var decision = TacticalWlActionGuard.Decide(
            configEnabled: true,
            dlcScenarioActive: true,
            unitUnderCommander: false,
            groupUnderCommander: false,
            attachedUnitUnderCommander: false);

        AssertTrue(decision.Allow, "AI-chain feud movement should remain vanilla");
        AssertEqual("ai-chain", decision.Reason, "reason");
    }

    private static void PlayerOrderPriorityRanksRetreatAboveFallback()
    {
        AssertTrue(
            PlayerOrderPriority.ForIntent(PlayerOrderIntent.RetreatToExit) >
            PlayerOrderPriority.ForIntent(PlayerOrderIntent.FallBackToLine),
            "retreat priority must exceed fallback");
    }

    private static void PlayerOrderMapperKeepsTypeFifteenRetreatOnly()
    {
        AssertEqual(15, PlayerOrderVanillaMapper.Map(PlayerOrderIntent.RetreatToExit).Type, "retreat type");
        AssertTrue(PlayerOrderVanillaMapper.Map(PlayerOrderIntent.FallBackToLine).Type != 15, "fallback is not retreat type");
        AssertTrue(PlayerOrderVanillaMapper.Map(PlayerOrderIntent.HoldObjective).Type != 15, "hold is not retreat type");
        AssertTrue(PlayerOrderVanillaMapper.Map(PlayerOrderIntent.RecoverFromCombat).Type != 15, "recover is not retreat type");
    }

    private static void PlayerOrderMapperMapsFallbackToTypeTwelve()
    {
        var mapped = PlayerOrderVanillaMapper.Map(PlayerOrderIntent.FallBackToLine);
        AssertEqual(12, mapped.Type, "fallback type");
        AssertEqual(PlayerOrderIntent.FallBackToLine, mapped.Intent, "fallback intent");
    }

    private static void PlayerOrderMapperMapsTacticalSupportAttackAndMovementLanes()
    {
        AssertEqual(11, PlayerOrderVanillaMapper.Map(PlayerOrderIntent.SupportMainEffort).Type, "support main effort tactical type");
        AssertEqual(11, PlayerOrderVanillaMapper.Map(PlayerOrderIntent.SupportMainEffort, PlayerOrderScope.Campaign).Type, "support main effort campaign type");
        AssertEqual(0, PlayerOrderVanillaMapper.Map(PlayerOrderIntent.AttackObjective).Type, "attack objective tactical type");
        AssertEqual(1, PlayerOrderVanillaMapper.Map(PlayerOrderIntent.AdvanceToAssemblyArea).Type, "advance movement tactical type");
        AssertEqual(50, PlayerOrderPriority.ForIntent(PlayerOrderIntent.AdvanceToAssemblyArea), "advance movement priority");
    }

    private static void PlayerOrderMapperMapsCampaignBuildRows()
    {
        AssertEqual(8, PlayerOrderVanillaMapper.Map(PlayerOrderIntent.DefendCapital, PlayerOrderScope.Campaign).Type, "defend capital type");
        AssertEqual(9, PlayerOrderVanillaMapper.Map(PlayerOrderIntent.BuildFort, PlayerOrderScope.Campaign).Type, "build fort type");
        AssertEqual(10, PlayerOrderVanillaMapper.Map(PlayerOrderIntent.BuildSupplyDepot, PlayerOrderScope.Campaign).Type, "supply depot type");
    }

    private static void PlayerOrderActivePriorityIsConservativeForAmbiguousVanillaTypes()
    {
        AssertEqual(100, PlayerOrderPriority.ForActiveVanillaType(13, PlayerOrderScope.Tactical, PlayerOrderProvenance.Vanilla), "vanilla recover");
        AssertEqual(20, PlayerOrderPriority.ForActiveVanillaType(14, PlayerOrderScope.Tactical, PlayerOrderProvenance.Unknown), "unknown clear hold");
        AssertEqual(20, PlayerOrderPriority.ForActiveVanillaType(13, PlayerOrderScope.Tactical, PlayerOrderProvenance.WhiskeyTactical), "whiskey recover");
        AssertEqual(50, PlayerOrderPriority.ForActiveVanillaType(1, PlayerOrderScope.Tactical, PlayerOrderProvenance.WhiskeyTactical), "movement type one");
        AssertEqual(50, PlayerOrderPriority.ForActiveVanillaType(6, PlayerOrderScope.Tactical, PlayerOrderProvenance.WhiskeyTactical), "movement type six");
        AssertEqual(60, PlayerOrderPriority.ForActiveVanillaType(16, PlayerOrderScope.Tactical, PlayerOrderProvenance.WhiskeyTactical), "attack type sixteen");
        AssertEqual(int.MaxValue, PlayerOrderPriority.ForActiveVanillaType(99, PlayerOrderScope.Tactical, PlayerOrderProvenance.Unknown), "unknown type");
    }

    private static void PlayerOrderDedupeBlocksTacticalOverActiveTypeFifteen()
    {
        var active = PlayerOrderActive("u1", 15, provenance: PlayerOrderProvenance.WhiskeyTactical);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective);
        AssertTrue(PlayerOrderDedupe.VanillaWouldBlock(active, candidate, campaignGroupFlag: false), "vanilla retreat blocks tactical");
        AssertEqual(
            PlayerOrderDedupeDecisionKind.BlockedByVanillaDedupe,
            PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1).Kind);
    }

    private static void PlayerOrderDedupeProtectsVanillaTransitionOrders()
    {
        var active = PlayerOrderActive("u1", 13, provenance: PlayerOrderProvenance.Vanilla);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.RetreatToExit, validExitPoint: true);
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.YieldVanillaTransition, decision.Kind, "transition protection");
    }

    private static void PlayerOrderDedupeBlocksHoldOverActiveAttackPerVanilla()
    {
        var active = PlayerOrderActive("u1", 1);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.HoldObjective);
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.BlockedByVanillaDedupe, decision.Kind, "hold over attack");
    }

    private static void PlayerOrderDedupeProtectsHigherSameScopePriority()
    {
        var active = PlayerOrderActive("u1", 12, provenance: PlayerOrderProvenance.WhiskeyTactical);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective);
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.BlockedByScopePriority, decision.Kind, "higher same-scope active order blocks lower candidate");
    }

    private static void PlayerOrderDedupePreservesVanillaTypeElevenUnlessHigherPriority()
    {
        var active = PlayerOrderActive("u1", 11, provenance: PlayerOrderProvenance.Vanilla);
        var equal = PlayerOrderCandidateFor("u1", PlayerOrderIntent.SupportMainEffort);
        var higher = PlayerOrderCandidateFor("u1", PlayerOrderIntent.HoldObjective);

        AssertEqual(
            PlayerOrderDedupeDecisionKind.BlockedByVanillaDedupe,
            PlayerOrderDedupe.Decide(equal, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1).Kind,
            "equal priority support must preserve vanilla type eleven");
        AssertEqual(
            PlayerOrderDedupeDecisionKind.Issue,
            PlayerOrderDedupe.Decide(higher, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1).Kind,
            "higher priority candidate may replace vanilla type eleven after preflight");
    }

    private static void PlayerOrderDedupeBlocksAmbiguousUnknownTypeSeven()
    {
        var active = PlayerOrderActive("u1", 7, provenance: PlayerOrderProvenance.Unknown);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective);
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.BlockedByUnknownActiveOrder, decision.Kind, "unknown type 7 is ambiguous without shadow");
    }

    private static void PlayerOrderDedupeBlocksAmbiguousUnknownTypeTwelve()
    {
        var active = PlayerOrderActive("u1", 12, provenance: PlayerOrderProvenance.Unknown);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective);
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.BlockedByUnknownActiveOrder, decision.Kind, "unknown type 12 is ambiguous without shadow");
    }

    private static void PlayerOrderDedupeAllowsEmergencyRetreatOverAmbiguousTacticalOrder()
    {
        var activeHold = PlayerOrderActive("u1", 12, provenance: PlayerOrderProvenance.Unknown);
        var activeEngage = PlayerOrderActive("u1", 7, provenance: PlayerOrderProvenance.Unknown);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.RetreatToExit, validExitPoint: true, objectiveKey: "exit-a");

        AssertEqual(
            PlayerOrderDedupeDecisionKind.Issue,
            PlayerOrderDedupe.Decide(candidate, activeHold, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1).Kind,
            "valid emergency retreat can override ambiguous tactical hold");
        AssertEqual(
            PlayerOrderDedupeDecisionKind.Issue,
            PlayerOrderDedupe.Decide(candidate, activeEngage, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1).Kind,
            "valid emergency retreat can override ambiguous tactical engage");
    }

    private static void PlayerOrderDedupeStaleSessionShadowDoesNotProveAmbiguousActive()
    {
        var state = new PlayerOrderDedupeState();
        var shadow = PlayerOrderCandidateFor("u1", PlayerOrderIntent.HoldObjective, objectiveKey: "ridge-a", givenOrderSession: 1);
        state.Record(shadow, tick: 10);
        var active = PlayerOrderActive(
            "u1",
            12,
            provenance: PlayerOrderProvenance.Unknown,
            givenOrderSession: 2,
            objectiveKey: "ridge-a");
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-b");
        var decision = PlayerOrderDedupe.Decide(candidate, active, state, PlayerOrderDedupeOptions.Default, tick: 200);
        AssertEqual(PlayerOrderDedupeDecisionKind.BlockedByUnknownActiveOrder, decision.Kind, "stale session shadow must not prove active intent");
    }

    private static void PlayerOrderDedupeMismatchedTargetShadowDoesNotProveAmbiguousActive()
    {
        var state = new PlayerOrderDedupeState();
        var shadow = PlayerOrderCandidateFor(
            "u1",
            PlayerOrderIntent.HoldObjective,
            objectiveKey: "ridge-a",
            targetPoint: new PlayerOrderPoint(100f, 200f),
            rotation: 45f);
        state.Record(shadow, tick: 10);
        var active = PlayerOrderActive(
            "u1",
            12,
            provenance: PlayerOrderProvenance.Unknown,
            objectiveKey: "ridge-b",
            targetPoint: new PlayerOrderPoint(180f, 260f),
            rotation: 45f);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-c");
        var decision = PlayerOrderDedupe.Decide(candidate, active, state, PlayerOrderDedupeOptions.Default, tick: 200);
        AssertEqual(PlayerOrderDedupeDecisionKind.BlockedByUnknownActiveOrder, decision.Kind, "mismatched target shadow must not prove active intent");
    }

    private static void PlayerOrderDedupeMismatchedIntentShadowDoesNotProveAmbiguousActive()
    {
        var state = new PlayerOrderDedupeState();
        var shadow = PlayerOrderCandidateFor(
            "u1",
            PlayerOrderIntent.HoldObjective,
            objectiveKey: "ridge-a",
            targetPoint: new PlayerOrderPoint(100f, 200f),
            rotation: 45f);
        state.Record(shadow, tick: 10);
        var active = PlayerOrderActive(
            "u1",
            12,
            provenance: PlayerOrderProvenance.Unknown,
            activeIntent: PlayerOrderIntent.FallBackToLine,
            objectiveKey: "ridge-a",
            targetPoint: new PlayerOrderPoint(100f, 200f),
            rotation: 45f);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-b");
        var decision = PlayerOrderDedupe.Decide(candidate, active, state, PlayerOrderDedupeOptions.Default, tick: 200);
        AssertEqual(PlayerOrderDedupeDecisionKind.BlockedByUnknownActiveOrder, decision.Kind, "mismatched intent shadow must not prove active intent");
    }

    private static void PlayerOrderDedupeStaleActiveRetreatAllowsNextTacticalCandidate()
    {
        var active = PlayerOrderActive("u1", 15, stale: true);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-a");
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue, decision.Kind, "stale active type 15 should not block next tactical order");
    }

    private static void PlayerOrderDedupeBattleEndedActiveRetreatAllowsNextTacticalCandidate()
    {
        var active = PlayerOrderActive("u1", 15, battleEnded: true);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-a");
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue, decision.Kind, "battle-ended active type 15 should not block next tactical order");
    }

    private static void PlayerOrderDedupeStaleActiveTransitionAllowsRetreat()
    {
        var active = PlayerOrderActive("u1", 13, stale: true);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.RetreatToExit, validExitPoint: true, objectiveKey: "exit-a");
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue, decision.Kind, "stale active transition must not vanilla-block retreat");
    }

    private static void PlayerOrderDedupeStaleSameTypeDoesNotVanillaBlock()
    {
        var active = PlayerOrderActive("u1", 1, provenance: PlayerOrderProvenance.WhiskeyTactical, stale: true);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-a");
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue, decision.Kind, "stale same-type active order must not vanilla-block");
    }

    private static void PlayerOrderDedupeEnforcesCampaignTacticalScopeGap()
    {
        var active = PlayerOrderActive(
            "u1",
            7,
            scope: PlayerOrderScope.Campaign,
            provenance: PlayerOrderProvenance.WhiskeyCampaign,
            activeCampaignActionable: true);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective);
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.BlockedByScopePriority, decision.Kind, "active actionable campaign blocks tactical");
    }

    private static void PlayerOrderDedupeAllowsEmergencyRetreatOverInvalidCampaignOrder()
    {
        var active = PlayerOrderActive(
            "u1",
            7,
            scope: PlayerOrderScope.Campaign,
            provenance: PlayerOrderProvenance.WhiskeyCampaign,
            activeCampaignActionable: false);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.RetreatToExit, validExitPoint: true);
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue, decision.Kind, "emergency retreat can replace invalid campaign order");
    }

    private static void PlayerOrderDedupeAllowsEmergencyRetreatOverActionableCampaignOrder()
    {
        var active = PlayerOrderActive(
            "u1",
            7,
            scope: PlayerOrderScope.Campaign,
            provenance: PlayerOrderProvenance.WhiskeyCampaign,
            activeCampaignActionable: true);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.RetreatToExit, validExitPoint: true);
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue, decision.Kind, "valid tactical emergency retreat can replace actionable campaign order");
    }

    private static void PlayerOrderDedupeBlocksCampaignOverFreshTacticalWithoutGap()
    {
        var active = PlayerOrderActive("u1", 1, provenance: PlayerOrderProvenance.WhiskeyTactical);
        var candidate = PlayerOrderCandidateFor(
            "u1",
            PlayerOrderIntent.SupportMainEffort,
            scope: PlayerOrderScope.Campaign,
            objectiveKey: "campaign-a");
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.BlockedByScopePriority, decision.Kind, "campaign order lacks +40 over fresh tactical");
    }

    private static void PlayerOrderDedupeAllowsCampaignOverStaleTactical()
    {
        var active = PlayerOrderActive("u1", 1, provenance: PlayerOrderProvenance.WhiskeyTactical, stale: true);
        var candidate = PlayerOrderCandidateFor(
            "u1",
            PlayerOrderIntent.SupportMainEffort,
            scope: PlayerOrderScope.Campaign,
            objectiveKey: "campaign-a");
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue, decision.Kind, "stale tactical order allows campaign replacement");
    }

    private static void PlayerOrderDedupeAllowsCampaignOverBattleEndedTactical()
    {
        var active = PlayerOrderActive("u1", 1, provenance: PlayerOrderProvenance.WhiskeyTactical, battleEnded: true);
        var candidate = PlayerOrderCandidateFor(
            "u1",
            PlayerOrderIntent.SupportMainEffort,
            scope: PlayerOrderScope.Campaign,
            objectiveKey: "campaign-a");
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue, decision.Kind, "battle ended tactical order allows campaign replacement");
    }

    private static void PlayerOrderDedupeAllowsCampaignOverTacticalWithPriorityGap()
    {
        var active = PlayerOrderActive("u1", 8, provenance: PlayerOrderProvenance.WhiskeyTactical);
        var candidate = PlayerOrderCandidateFor(
            "u1",
            PlayerOrderIntent.SupportMainEffort,
            scope: PlayerOrderScope.Campaign,
            objectiveKey: "campaign-a");
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue, decision.Kind, "campaign priority gap allows replacement");
    }

    private static void PlayerOrderDedupeBlocksTypeFourteenUnlessActiveHold()
    {
        var active = PlayerOrderActive("u1", 1, provenance: PlayerOrderProvenance.WhiskeyTactical);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.ClearHoldTransition, objectiveKey: "clear-a");
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.BlockedByVanillaDedupe, decision.Kind, "type 14 is blocked unless active type is 12");
    }

    private static void PlayerOrderDedupeAllowsTypeFourteenPastVanillaPreflightOnActiveHold()
    {
        var active = PlayerOrderActive("u1", 12, provenance: PlayerOrderProvenance.WhiskeyTactical);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.ClearHoldTransition, objectiveKey: "clear-a");
        var decision = PlayerOrderDedupe.Decide(candidate, active, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1);
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue, decision.Kind, "type 14 may clear active type 12");
    }

    private static void PlayerOrderDedupeTypeFourteenDoesNotPermanentlyYield()
    {
        var sameCycle = PlayerOrderActive("u1", 14, provenance: PlayerOrderProvenance.Vanilla);
        var laterCycle = PlayerOrderActive("u1", 14, provenance: PlayerOrderProvenance.Unknown);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-a");

        AssertEqual(
            PlayerOrderDedupeDecisionKind.YieldVanillaTransition,
            PlayerOrderDedupe.Decide(candidate, sameCycle, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 1).Kind,
            "same-cycle vanilla type 14 should get one transition yield");
        AssertEqual(
            PlayerOrderDedupeDecisionKind.Issue,
            PlayerOrderDedupe.Decide(candidate, laterCycle, new PlayerOrderDedupeState(), PlayerOrderDedupeOptions.Default, tick: 2).Kind,
            "later unknown type 14 must not permanently freeze Whiskey candidates");
    }

    private static void PlayerOrderCacheClearsByBattleIdentity()
    {
        var state = new PlayerOrderDedupeState();
        var active = PlayerOrderActive("u1", -1, battleIdentity: "battle-a");
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, battleIdentity: "battle-a", objectiveKey: "ridge-a");
        AssertPlayerOrderIssueAndRecord(candidate, active, state, tick: 10, "first issue");
        AssertEqual(PlayerOrderDedupeDecisionKind.SuppressSignature,
            PlayerOrderDedupe.Decide(candidate, active, state, PlayerOrderDedupeOptions.Default, tick: 200).Kind,
            "same battle cached");
        state.ClearForBattleBoundary("battle-a");
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue,
            PlayerOrderDedupe.Decide(candidate, active, state, PlayerOrderDedupeOptions.Default, tick: 201).Kind,
            "battle boundary clears cached signature");
    }

    private static void PlayerOrderCacheClearsOldBattleAtNewBoundary()
    {
        var state = new PlayerOrderDedupeState();
        var active = PlayerOrderActive("u1", -1, battleIdentity: "battle-a");
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, battleIdentity: "battle-a", objectiveKey: "ridge-a");
        AssertPlayerOrderIssueAndRecord(candidate, active, state, tick: 10, "first issue");
        state.ClearForBattleBoundary("battle-b");
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue,
            PlayerOrderDedupe.Decide(candidate, active, state, PlayerOrderDedupeOptions.Default, tick: 200).Kind,
            "new battle boundary clears old battle signature");
    }

    private static void PlayerOrderCacheCommandChangeClearsAllUnits()
    {
        var state = new PlayerOrderDedupeState();
        var active1 = PlayerOrderActive("u1", -1);
        var active2 = PlayerOrderActive("u2", -1);
        var candidate1 = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-a");
        var candidate2 = PlayerOrderCandidateFor("u2", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-b");
        AssertPlayerOrderIssueAndRecord(candidate1, active1, state, tick: 10, "unit one first issue");
        AssertPlayerOrderIssueAndRecord(candidate2, active2, state, tick: 11, "unit two first issue");
        state.ClearForPlayerCommandChange();
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue,
            PlayerOrderDedupe.Decide(candidate1, active1, state, PlayerOrderDedupeOptions.Default, tick: 200).Kind,
            "command change clears unit one");
        AssertEqual(PlayerOrderDedupeDecisionKind.Issue,
            PlayerOrderDedupe.Decide(candidate2, active2, state, PlayerOrderDedupeOptions.Default, tick: 201).Kind,
            "command change clears unit two");
    }

    private static void PlayerOrderDedupeDecideDoesNotRecordShadow()
    {
        var state = new PlayerOrderDedupeState();
        var active = PlayerOrderActive("u1", -1);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-a");

        var decision = PlayerOrderDedupe.Decide(candidate, active, state, PlayerOrderDedupeOptions.Default, tick: 1);

        AssertEqual(PlayerOrderDedupeDecisionKind.Issue, decision.Kind, "preflight should allow issue");
        AssertTrue(!state.TryGetShadow("u1", out _), "preflight decision must not record Whiskey provenance");
    }

    private static void PlayerOrderDedupePreviewClassifiesWhileWritesDisabled()
    {
        var active = PlayerOrderActive("u1", 13, provenance: PlayerOrderProvenance.Vanilla);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.RetreatToExit, validExitPoint: true);

        var disabled = PlayerOrderDedupe.Decide(
            candidate,
            active,
            new PlayerOrderDedupeState(),
            new PlayerOrderDedupeOptions(writesEnabled: false, throttleTicks: 120),
            tick: 1);
        var preview = PlayerOrderDedupe.Preview(candidate, active, new PlayerOrderDedupeState(), throttleTicks: 120, tick: 1);

        AssertEqual(PlayerOrderDedupeDecisionKind.BlockedByDisabledWrites, disabled.Kind, "write gate still blocks issue path");
        AssertEqual(PlayerOrderDedupeDecisionKind.YieldVanillaTransition, preview.Kind, "diagnostic preview should classify the real suppression reason");
    }

    private static void PlayerOrderAcceptedShadowTracksRequestAndVanillaActive()
    {
        var state = new PlayerOrderDedupeState();
        var request = PlayerOrderCandidateFor(
            "u1",
            PlayerOrderIntent.HoldObjective,
            objectiveKey: "ridge-a",
            givenOrderSession: 2,
            rotation: 45f);
        var accepted = PlayerOrderCandidateFor(
            "parent-u1",
            PlayerOrderIntent.HoldObjective,
            objectiveKey: "ridge-a",
            givenOrderSession: 2,
            rotation: 225f);

        state.RecordAccepted(request, accepted, tick: 10);

        AssertEqual(
            PlayerOrderDedupeDecisionKind.SuppressSignature,
            PlayerOrderDedupe.Decide(request, PlayerOrderActive("u1", -1), state, PlayerOrderDedupeOptions.Default, tick: 200).Kind,
            "request signature should suppress repeated Whiskey candidate");

        var active = PlayerOrderActive(
            "parent-u1",
            12,
            provenance: PlayerOrderProvenance.Unknown,
            givenOrderSession: 2,
            objectiveKey: "ridge-a",
            targetPoint: new PlayerOrderPoint(100f, 200f),
            rotation: 225f,
            activeIntent: PlayerOrderIntent.HoldObjective);
        var changed = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-b");

        AssertTrue(state.TryGetShadow("parent-u1", out var shadow), "accepted active unit key should resolve the shadow");
        AssertTrue(shadow.ActiveSignature.MatchesActiveOrder(active), "accepted signature should match vanilla active order");
        AssertEqual(
            PlayerOrderDedupeDecisionKind.BlockedByScopePriority,
            PlayerOrderDedupe.Decide(changed, active, state, PlayerOrderDedupeOptions.Default, tick: 200).Kind,
            "accepted shadow should prove active provenance before priority comparison");
    }

    private static void PlayerOrderSignatureCacheSuppressesRepeatedMaterialMatch()
    {
        var state = new PlayerOrderDedupeState();
        var active = PlayerOrderActive("u1", -1);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-a");
        AssertPlayerOrderIssueAndRecord(candidate, active, state, tick: 1, "first issue");
        AssertEqual(PlayerOrderDedupeDecisionKind.SuppressSignature,
            PlayerOrderDedupe.Decide(candidate, active, state, PlayerOrderDedupeOptions.Default, tick: 200).Kind,
            "material match suppressed");
    }

    private static void PlayerOrderIssuanceThrottleSuppressesRapidChangedCandidates()
    {
        var state = new PlayerOrderDedupeState();
        var active = PlayerOrderActive("u1", -1);
        var first = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-a");
        var changed = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-b");
        AssertPlayerOrderIssueAndRecord(first, active, state, tick: 10, "first issue");
        AssertEqual(PlayerOrderDedupeDecisionKind.SuppressThrottle,
            PlayerOrderDedupe.Decide(changed, active, state, PlayerOrderDedupeOptions.Default, tick: 20).Kind,
            "changed candidate throttled");
    }

    private static void PlayerOrderFailedBridgeAttemptThrottlesRejectedCandidate()
    {
        var state = new PlayerOrderDedupeState();
        var active = PlayerOrderActive("u1", -1);
        var candidate = PlayerOrderCandidateFor("u1", PlayerOrderIntent.AttackObjective, objectiveKey: "ridge-a");

        state.RecordAttempt(candidate, tick: 10);

        AssertEqual(
            PlayerOrderDedupeDecisionKind.SuppressSignature,
            PlayerOrderDedupe.Decide(candidate, active, state, PlayerOrderDedupeOptions.Default, tick: 20).Kind,
            "rejected vanilla bridge candidate should be signature-suppressed");
    }

    private static void AssertPlayerOrderIssueAndRecord(
        PlayerOrderCandidate candidate,
        PlayerOrderActiveSnapshot active,
        PlayerOrderDedupeState state,
        long tick,
        string message)
    {
        AssertEqual(
            PlayerOrderDedupeDecisionKind.Issue,
            PlayerOrderDedupe.Decide(candidate, active, state, PlayerOrderDedupeOptions.Default, tick).Kind,
            message);
        state.Record(candidate, tick);
    }

    private static void PlayerOrderDiagnosticsSignaturesCapRepeatedLogs()
    {
        var diagnostics = new PlayerOrderDiagnostics(capacity: 2);
        AssertTrue(diagnostics.ShouldLog("u1", "sig-a"), "first sig-a logs");
        AssertTrue(!diagnostics.ShouldLog("u1", "sig-a"), "repeat sig-a suppressed");
        AssertTrue(diagnostics.ShouldLog("u1", "sig-b"), "sig-b logs");
        AssertTrue(diagnostics.ShouldLog("u1", "sig-c"), "sig-c evicts oldest");
        AssertTrue(diagnostics.ShouldLog("u1", "sig-a"), "sig-a logs after cap eviction");
        AssertEqual("u1|sig-a", PlayerOrderDiagnostics.FormatSignature("u1", "sig-a"), "formatted signature");
    }

    private static void PlayerOrderComposerReturnsNoneWithoutOrchestrator()
    {
        var input = PlayerOrderComposerInputFor(role: "Main", task: "AttackObjective", hasSideOrchestrator: false);

        var candidate = PlayerOrderComposer.Compose(input);

        AssertTrue(!candidate.HasCandidate, "missing side orchestrator must not emit");
    }

    private static void PlayerOrderComposerReturnsNoneForPlayerCic()
    {
        var input = PlayerOrderComposerInputFor(role: "Main", task: "AttackObjective", isCommanderInChief: true);

        var candidate = PlayerOrderComposer.Compose(input);

        AssertTrue(!candidate.HasCandidate, "player CIC must not receive subordinate doctrine orders");
    }

    private static void PlayerOrderComposerPrefersRetreatFromWithdrawalWithValidExit()
    {
        var input = PlayerOrderComposerInputFor(
            role: "Fallback",
            axis: "Withdraw",
            task: "FallBackToLine",
            operationPhase: "Aborting",
            hasValidExitPoint: true,
            exitPoint: new PlayerOrderPoint(800f, 900f, validExitPoint: true));

        var candidate = PlayerOrderComposer.Compose(input);

        AssertEqual(PlayerOrderIntent.RetreatToExit, candidate.Intent, "withdrawal + valid exit should retreat");
        AssertEqual(15, candidate.VanillaType, "retreat vanilla type");
        AssertEqual(800f, candidate.TargetPoint.X, "exit x");
        AssertEqual("exit", candidate.ObjectiveKey, "exit key");
    }

    private static void PlayerOrderComposerMapsFallbackRoleToFallbackLine()
    {
        var input = PlayerOrderComposerInputFor(role: "Fallback", axis: "Withdraw", fallbackPoint: new PlayerOrderPoint(300f, 400f));

        var candidate = PlayerOrderComposer.Compose(input);

        AssertEqual(PlayerOrderIntent.FallBackToLine, candidate.Intent, "fallback role should map to fallback line");
        AssertEqual(12, candidate.VanillaType, "fallback vanilla type");
        AssertEqual(300f, candidate.TargetPoint.X, "fallback x");
        AssertEqual("fallback-line", candidate.ObjectiveKey, "fallback key");
    }

    private static void PlayerOrderComposerMapsHoldTaskToHoldObjective()
    {
        var input = PlayerOrderComposerInputFor(role: "Defender", task: "HoldObjective");

        var candidate = PlayerOrderComposer.Compose(input);

        AssertEqual(PlayerOrderIntent.HoldObjective, candidate.Intent, "hold task should map to hold objective");
        AssertEqual(12, candidate.VanillaType, "hold vanilla type");
        AssertEqual("ridge-a", candidate.ObjectiveKey, "objective key");
    }

    private static void PlayerOrderComposerMapsHoldIntentWithoutObjectiveToHoldPosition()
    {
        var input = new PlayerOrderComposerInput(
            hasSideOrchestrator: true,
            isPlayerSubordinate: true,
            isCommanderInChief: false,
            unitKey: "u1",
            battleIdentity: "battle-a",
            givenOrderSession: 12,
            commandIntent: new CommandIntentResolutionSnapshot(
                true,
                new CommandNodeIntentSnapshot("node-u1", "node-parent", "RefuseLeft", "Hold", 0, 30, 0.4f, 1),
                "inherited-defensive-intent"),
            commandTask: new CommandTaskSnapshot(false, "Defender", string.Empty, default(PlayerOrderPoint), default(PlayerOrderPoint), string.Empty, "intent-only"),
            doctrine: new ArmyDoctrineSnapshot("Committed", "DefensiveNetwork", "road-approach", "runtime"),
            unitPosition: new PlayerOrderPoint(512f, 768f),
            objectivePoint: default(PlayerOrderPoint),
            fallbackPoint: default(PlayerOrderPoint),
            exitPoint: default(PlayerOrderPoint),
            hasValidObjective: false,
            hasValidFallback: false,
            hasValidExitPoint: false);

        var candidate = PlayerOrderComposer.Compose(input);

        AssertTrue(candidate.HasCandidate, "hold position candidate");
        AssertEqual(PlayerOrderIntent.HoldObjective, candidate.Intent, "hold intent");
        AssertEqual(12, candidate.VanillaType, "hold vanilla type");
        AssertEqual(512f, candidate.TargetPoint.X, "hold x");
        AssertEqual(768f, candidate.TargetPoint.Z, "hold z");
        AssertEqual("hold-position", candidate.ObjectiveKey, "hold position objective");
        AssertContains(candidate.Reason, "hold-position", "reason");
    }

    private static void PlayerOrderComposerMapsSupportRoleToSupportMainEffort()
    {
        var input = PlayerOrderComposerInputFor(role: "SupportMain", task: "SupportAttack");

        var candidate = PlayerOrderComposer.Compose(input);

        AssertEqual(PlayerOrderIntent.SupportMainEffort, candidate.Intent, "support role should map to support main effort");
        AssertEqual(11, candidate.VanillaType, "support vanilla type");
        AssertEqual("ridge-a", candidate.ObjectiveKey, "objective key");
        AssertTrue(candidate.Priority > 0, "support candidate should have priority");
    }

    private static void PlayerOrderComposerMapsProbeTaskToProbeObjective()
    {
        var input = PlayerOrderComposerInputFor(role: "Screen", task: "Probe");

        var candidate = PlayerOrderComposer.Compose(input);

        AssertEqual(PlayerOrderIntent.ProbeObjective, candidate.Intent, "probe task should map to probe objective");
        AssertEqual(5, candidate.VanillaType, "probe vanilla type");
    }

    private static void PlayerOrderComposerReturnsNoneWithoutConcreteTarget()
    {
        var input = new PlayerOrderComposerInput(
            hasSideOrchestrator: true,
            isPlayerSubordinate: true,
            isCommanderInChief: false,
            unitKey: "u1",
            battleIdentity: "battle-a",
            givenOrderSession: 12,
            commandIntent: new CommandIntentResolutionSnapshot(
                true,
                new CommandNodeIntentSnapshot("n1", "s1", "Main", "Forward", 0, 0, 0.5f, 1),
                "intent-only"),
            commandTask: new CommandTaskSnapshot(false, string.Empty, string.Empty, default(PlayerOrderPoint), default(PlayerOrderPoint), string.Empty, string.Empty),
            doctrine: new ArmyDoctrineSnapshot("forming", "parallel", string.Empty, "runtime"),
            unitPosition: new PlayerOrderPoint(10f, 20f),
            objectivePoint: default(PlayerOrderPoint),
            fallbackPoint: default(PlayerOrderPoint),
            exitPoint: default(PlayerOrderPoint),
            hasValidObjective: false,
            hasValidFallback: false,
            hasValidExitPoint: false);

        AssertEqual(false, PlayerOrderComposer.Compose(input).HasCandidate, "intent-only command without a target must fail closed");
    }

    private static void PlayerOrderTargetPolicyFallsBackToVisibleEnemyForAttack()
    {
        var point = PlayerOrderTargetPolicy.ResolveObjectivePoint(
            commandTarget: default(PlayerOrderPoint),
            currentObjective: default(PlayerOrderPoint),
            closestVisibleEnemy: new PlayerOrderPoint(876f, -1864f),
            allowVisibleEnemyFallback: true,
            source: out string source);

        AssertEqual(876f, point.X, "visible enemy x");
        AssertEqual(-1864f, point.Z, "visible enemy z");
        AssertEqual("visible-enemy", source, "target source");

        var blocked = PlayerOrderTargetPolicy.ResolveObjectivePoint(
            commandTarget: default(PlayerOrderPoint),
            currentObjective: default(PlayerOrderPoint),
            closestVisibleEnemy: new PlayerOrderPoint(876f, -1864f),
            allowVisibleEnemyFallback: false,
            source: out source);

        AssertEqual(false, IsPlayerOrderPointValid(blocked), "non-attack intents must still fail closed without a concrete target");
        AssertEqual("none", source, "blocked source");
    }

    private static void PlayerOrderComposerEmitsReasonForDiagnostics()
    {
        var input = PlayerOrderComposerInputFor(role: "SupportMain", task: "SupportAttack");

        var candidate = PlayerOrderComposer.Compose(input);

        AssertTrue(!string.IsNullOrWhiteSpace(candidate.Reason), "candidate reason must be non-empty");
        AssertTrue(candidate.Reason.IndexOf("support", StringComparison.OrdinalIgnoreCase) >= 0, "reason should identify support source");
    }

    private static bool IsPlayerOrderPointValid(PlayerOrderPoint point)
    {
        return !float.IsNaN(point.X) &&
            !float.IsNaN(point.Z) &&
            !float.IsInfinity(point.X) &&
            !float.IsInfinity(point.Z) &&
            (Math.Abs(point.X) > 0.001f || Math.Abs(point.Z) > 0.001f || point.ValidExitPoint);
    }

    private static void PlayerOrderRuntimeClassifiesVanillaOrderSceneActivity()
    {
        AssertEqual(true, PlayerOrderVanillaScene.IsGivenOrderActiveForScene(0, currentOperation: 1), "type 0 is active in battle");
        AssertEqual(false, PlayerOrderVanillaScene.IsGivenOrderActiveForScene(0, currentOperation: 0), "type 0 is inactive outside battle");
        AssertEqual(false, PlayerOrderVanillaScene.IsGivenOrderActiveForScene(8, currentOperation: 1), "campaign type 8 is inactive in battle");
        AssertEqual(true, PlayerOrderVanillaScene.IsGivenOrderActiveForScene(8, currentOperation: 0), "campaign type 8 is active outside battle");
    }

    private static PlayerOrderComposerInput PlayerOrderComposerInputFor(
        string role,
        string axis = "SectorAxis",
        string task = "",
        bool hasSideOrchestrator = true,
        bool isCommanderInChief = false,
        string operationPhase = "Committed",
        bool hasValidExitPoint = false,
        PlayerOrderPoint? objectivePoint = null,
        PlayerOrderPoint? fallbackPoint = null,
        PlayerOrderPoint? exitPoint = null)
    {
        return new PlayerOrderComposerInput(
            hasSideOrchestrator: hasSideOrchestrator,
            isPlayerSubordinate: true,
            isCommanderInChief: isCommanderInChief,
            unitKey: "u1",
            battleIdentity: "battle-a",
            givenOrderSession: 3,
            commandIntent: new CommandIntentResolutionSnapshot(
                found: true,
                intent: new CommandNodeIntentSnapshot(
                    nodeId: "node-u1",
                    sourceNodeId: "node-parent",
                    role: role,
                    axis: axis,
                    primarySector: 2,
                    supportPriority: 70,
                    aggressionBias01: 0.6f,
                    depth: 1),
                reason: "test-intent"),
            commandTask: new CommandTaskSnapshot(
                found: !string.IsNullOrWhiteSpace(task),
                role: role,
                task: task,
                targetPoint: objectivePoint ?? new PlayerOrderPoint(100f, 200f),
                fallbackTarget: fallbackPoint ?? new PlayerOrderPoint(300f, 400f),
                objectiveKey: "ridge-a",
                reason: "test-task"),
            doctrine: new ArmyDoctrineSnapshot(
                operationPhase: operationPhase,
                operationShape: "SingleMainEffort",
                objectiveKey: "ridge-a",
                reason: "test-doctrine"),
            unitPosition: new PlayerOrderPoint(10f, 20f),
            objectivePoint: objectivePoint ?? new PlayerOrderPoint(100f, 200f),
            fallbackPoint: fallbackPoint ?? new PlayerOrderPoint(300f, 400f),
            exitPoint: exitPoint ?? new PlayerOrderPoint(800f, 900f, hasValidExitPoint),
            hasValidObjective: true,
            hasValidFallback: true,
            hasValidExitPoint: hasValidExitPoint);
    }

    private static PlayerOrderCandidate PlayerOrderCandidateFor(
        string unitKey,
        PlayerOrderIntent intent,
        string battleIdentity = "battle-a",
        string objectiveKey = "",
        bool validExitPoint = false,
        PlayerOrderScope scope = PlayerOrderScope.Tactical,
        int givenOrderSession = 1,
        PlayerOrderPoint? targetPoint = null,
        float rotation = 45f)
    {
        var point = targetPoint ?? new PlayerOrderPoint(100f, 200f, validExitPoint);
        return new PlayerOrderCandidate(
            scope: scope,
            intent: intent,
            vanillaType: PlayerOrderVanillaMapper.Map(intent, scope).Type,
            priority: PlayerOrderPriority.ForIntent(intent),
            unitKey: unitKey,
            battleIdentity: battleIdentity,
            givenOrderSession: givenOrderSession,
            targetPoint: point,
            rotation: rotation,
            objectiveKey: objectiveKey,
            reason: "test",
            activeCampaignActionable: false,
            campaignGroupFlag: scope == PlayerOrderScope.Campaign,
            validExitPoint: validExitPoint);
    }
}
