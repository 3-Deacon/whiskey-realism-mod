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

internal static class TacticalB6Tests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("tactical b6b reserve aggregator emits relieve battered line when reserve safe", TacticalB6bReserveAggregatorEmitsRelieveBatteredLineWhenReserveSafe),
            ("tactical b6b reserve no reserve yields none", TacticalB6bReserveNoReserveYieldsNone),
            ("tactical b6b reserve flank risk with last reserve guards", TacticalB6bReserveFlankRiskWithLastReserveGuards),
            ("tactical b6b reserve flank risk with multiple reserves picks flank guard", TacticalB6bReserveFlankRiskWithMultipleReservesPicksFlankGuard),
            ("tactical b6b reserve single relief request prepares relief", TacticalB6bReserveSingleReliefRequestPreparesRelief),
            ("tactical b6b reserve exploit weak point picks exploit", TacticalB6bReserveExploitWeakPointPicksExploit),
            ("tactical b6b reserve wl ownership unsafe holds reserve", TacticalB6bReserveWlOwnershipUnsafeHoldsReserve),
            ("tactical b6b reserve stale order prepares without mutation", TacticalB6bReserveStaleOrderPreparesWithoutMutation),
            ("tactical b6c reaction context returns last decision per group", TacticalB6cReactionContextReturnsLastDecisionPerGroup),
            ("tactical b6c reaction context clear discards all entries", TacticalB6cReactionContextClearDiscardsAllEntries),
            ("tactical b6c reaction context missing key returns default maintain", TacticalB6cReactionContextMissingKeyReturnsDefaultMaintain),
            ("tactical b6a probe posture maps to probe intent", TacticalB6aProbePostureMapsToProbeIntent),
            ("tactical b6a concentrated attack maps to attack", TacticalB6aConcentratedAttackMapsToAttack),
            ("tactical b6a concentrated attack with weak point and high init upgrades to all out", TacticalB6aConcentratedAttackUpgradesToAllOut),
            ("tactical b6a exploit breakthrough downgrades on low confidence", TacticalB6aExploitDowngradesOnLowConfidence),
            ("tactical b6a counterstroke maps to defend", TacticalB6aCounterstrokeMapsToDefend),
            ("tactical b6a screen and delay maps to defend", TacticalB6aScreenAndDelayMapsToDefend),
            ("tactical b6a reinforce and hold maps to hold", TacticalB6aReinforceAndHoldMapsToHold),
            ("tactical b6a recover maps to hold to last", TacticalB6aRecoverMapsToHoldToLast),
            ("tactical b6a no plan falls back to macro", TacticalB6aNoPlanFallsBackToMacro),
            ("tactical b6a macro retreat falls to hold to last", TacticalB6aMacroRetreatFallsToHoldToLast),
            ("tactical b6a macro attack aggressive commander upgrades to all out", TacticalB6aMacroAttackAggressiveCommanderUpgradesToAllOut),
            ("tactical b6a macro attack cautious commander deescalates to probe", TacticalB6aMacroAttackCautiousCommanderDeescalatesToProbe),
            ("tactical b6a macro attack mid initiative preserves attack", TacticalB6aMacroAttackMidInitiativePreservesAttack),
            ("tactical b6a macro assault cautious commander downgrades", TacticalB6aMacroAssaultCautiousCommanderDowngrades),
            ("tactical b6a probe intent yields probe and fix", TacticalB6aProbeIntentYieldsProbeAndFix),
            ("tactical b6a defend with right flank risk yields refuse right", TacticalB6aDefendRightFlankYieldsRefuseRight),
            ("tactical b6a defend with left flank risk yields refuse left", TacticalB6aDefendLeftFlankYieldsRefuseLeft),
            ("tactical b6a defend with anchored flank does not refuse", TacticalB6aDefendAnchoredFlankDoesNotRefuse),
            ("tactical b6a attack with decisive sector yields weak point pressure", TacticalB6aAttackDecisiveYieldsWeakPointPressure),
            ("tactical b6a attack without decisive sector falls back to probe and fix", TacticalB6aAttackNoDecisiveFallsBack),
            ("tactical b6a main effort rejected when subordinate share over half", TacticalB6aMainEffortRejectedOnPlayerOwnership),
            ("tactical b6a hold to last yields line hold", TacticalB6aHoldToLastYieldsLineHold),
            ("tactical b6a empty sectors yields no-sectors decision", TacticalB6aEmptySectorsYieldsEmpty),
            ("tactical b6b probe intent denies charge", TacticalB6bProbeIntentDeniesCharge),
            ("tactical b6b hold to last blocks fallback pressure", TacticalB6bHoldToLastBlocksFallbackPressure),
            ("tactical b6b defend with weak exposed target permits limited counterstroke", TacticalB6bDefendWeakExposedTargetPermitsCounterstroke),
            ("tactical b6b defend against strongpoint denies counterstroke", TacticalB6bDefendStrongpointDeniesCounterstroke),
            ("tactical b6b attack permits charge against fresh target", TacticalB6bAttackPermitsChargeFreshTarget),
            ("tactical b6b attack with cooldown active denies charge", TacticalB6bAttackCooldownActiveDeniesCharge),
            ("tactical b6b attack with strongpoint target denies charge", TacticalB6bAttackStrongpointTargetDeniesCharge),
            ("tactical b6b stale order downgrades to maintain line", TacticalB6bStaleOrderDowngradesToMaintainLine),
            ("tactical b6b wl ownership unsafe forces maintain line", TacticalB6bWlOwnershipUnsafeForcesMaintainLine),
            ("tactical b6b path risk blocks runtime application", TacticalB6bPathRiskBlocksRuntimeApplication),
            ("tactical b6b battered frontline emits line relief request under hold", TacticalB6bBatteredFrontlineEmitsLineReliefRequest),
            ("tactical b6b hold with flank morale risk requests relief", TacticalB6bHoldWithFlankMoraleRiskRequestsRelief),
            ("tactical b6b path risk fix mission maintains line", TacticalB6bPathRiskFixMissionMaintainsLine),
            ("tactical b6b denied attack maintains line", TacticalB6bDeniedAttackMaintainsLine),
            ("tactical b6b denied fix mission screens without path risk", TacticalB6bDeniedFixMissionScreensWithoutPathRisk),
            ("tactical b6b attack fix mission screens even when charge ready", TacticalB6bAttackFixMissionScreensWhenChargeReady),
            ("tactical b6b attack economy mission screens even when charge ready", TacticalB6bAttackEconomyMissionScreensWhenChargeReady),
            ("tactical b6b attack hold mission maintains line when charge ready", TacticalB6bAttackHoldMissionMaintainsLineWhenChargeReady),
            ("tactical b6b attack weak point mission permits charge when ready", TacticalB6bAttackWeakPointMissionPermitsChargeWhenReady),
            ("tactical b6b conservative policy blocks weak point charge", TacticalB6bConservativePolicyBlocksWeakPointCharge),
            ("tactical b6b aggressive policy permits weak point charge", TacticalB6bAggressivePolicyPermitsWeakPointCharge),
            ("tactical b6b conservative policy blocks defend counterstroke", TacticalB6bConservativePolicyBlocksDefendCounterstroke),
            ("tactical b6b standard policy permits defend counterstroke", TacticalB6bStandardPolicyPermitsDefendCounterstroke),
        });
    }

    private static void TacticalB6aProbePostureMapsToProbeIntent()
    {
        var input = new TacticalIntentInput(
            operationPosture: WhiskeyRealism.Strategic.OperationPosture.ProbeAndDevelop,
            hasPlan: true,
            vanillaMacro: 1,
            commanderInitiative01: 0.5f,
            oddsConfidence: 0.7f,
            weakPointConfirmed: false);

        var decision = TacticalCommanderIntentResolver.Resolve(input);

        AssertTrue(decision.Intent == CommanderIntent.ProbeIntent, "Expected ProbeIntent, got " + decision.Intent);
        AssertTrue(!decision.AllowsCharge, "ProbeIntent must not allow charge");
    }

    private static void TacticalB6aConcentratedAttackMapsToAttack()
    {
        var input = new TacticalIntentInput(
            WhiskeyRealism.Strategic.OperationPosture.ConcentratedAttack,
            hasPlan: true, vanillaMacro: 1, commanderInitiative01: 0.5f,
            oddsConfidence: 0.7f, weakPointConfirmed: false);
        var d = TacticalCommanderIntentResolver.Resolve(input);
        AssertTrue(d.Intent == CommanderIntent.Attack, "Expected Attack, got " + d.Intent);
        AssertTrue(d.AllowsCharge, "Attack should allow charge");
    }

    private static void TacticalB6aConcentratedAttackUpgradesToAllOut()
    {
        var input = new TacticalIntentInput(
            WhiskeyRealism.Strategic.OperationPosture.ConcentratedAttack,
            hasPlan: true, vanillaMacro: 0, commanderInitiative01: 0.7f,
            oddsConfidence: 0.8f, weakPointConfirmed: true);
        var d = TacticalCommanderIntentResolver.Resolve(input);
        AssertTrue(d.Intent == CommanderIntent.AllOutAttack, "Expected AllOutAttack, got " + d.Intent);
    }

    private static void TacticalB6aExploitDowngradesOnLowConfidence()
    {
        var input = new TacticalIntentInput(
            WhiskeyRealism.Strategic.OperationPosture.ExploitBreakthrough,
            hasPlan: true, vanillaMacro: 0, commanderInitiative01: 0.7f,
            oddsConfidence: 0.4f, weakPointConfirmed: true);
        var d = TacticalCommanderIntentResolver.Resolve(input);
        AssertTrue(d.Intent == CommanderIntent.Attack, "Expected Attack on low confidence, got " + d.Intent);
    }

    private static void TacticalB6aCounterstrokeMapsToDefend()
    {
        var input = new TacticalIntentInput(
            WhiskeyRealism.Strategic.OperationPosture.Counterstroke,
            hasPlan: true, vanillaMacro: 2, commanderInitiative01: 0.5f,
            oddsConfidence: 0.6f, weakPointConfirmed: false);
        var d = TacticalCommanderIntentResolver.Resolve(input);
        AssertTrue(d.Intent == CommanderIntent.Defend, "Expected Defend, got " + d.Intent);
        AssertTrue(d.AllowsCharge, "Counterstroke must keep charge available for LimitedCounterstroke");
    }

    private static void TacticalB6aScreenAndDelayMapsToDefend()
    {
        var input = new TacticalIntentInput(
            WhiskeyRealism.Strategic.OperationPosture.ScreenAndDelay,
            hasPlan: true, vanillaMacro: 2, commanderInitiative01: 0.5f,
            oddsConfidence: 0.5f, weakPointConfirmed: false);
        var d = TacticalCommanderIntentResolver.Resolve(input);
        AssertTrue(d.Intent == CommanderIntent.Defend, "Expected Defend, got " + d.Intent);
        AssertTrue(!d.AllowsCharge, "ScreenAndDelay must not allow charge");
    }

    private static void TacticalB6aReinforceAndHoldMapsToHold()
    {
        var input = new TacticalIntentInput(
            WhiskeyRealism.Strategic.OperationPosture.ReinforceAndHold,
            hasPlan: true, vanillaMacro: 2, commanderInitiative01: 0.5f,
            oddsConfidence: 0.5f, weakPointConfirmed: false);
        var d = TacticalCommanderIntentResolver.Resolve(input);
        AssertTrue(d.Intent == CommanderIntent.Hold, "Expected Hold, got " + d.Intent);
    }

    private static void TacticalB6aRecoverMapsToHoldToLast()
    {
        var input = new TacticalIntentInput(
            WhiskeyRealism.Strategic.OperationPosture.Recover,
            hasPlan: true, vanillaMacro: 2, commanderInitiative01: 0.5f,
            oddsConfidence: 0.5f, weakPointConfirmed: false);
        var d = TacticalCommanderIntentResolver.Resolve(input);
        AssertTrue(d.Intent == CommanderIntent.HoldToLast, "Expected HoldToLast, got " + d.Intent);
    }

    private static void TacticalB6aNoPlanFallsBackToMacro()
    {
        var input = new TacticalIntentInput(
            WhiskeyRealism.Strategic.OperationPosture.Inherit,
            hasPlan: false, vanillaMacro: 2, commanderInitiative01: 0.5f,
            oddsConfidence: 0.5f, weakPointConfirmed: false);
        var d = TacticalCommanderIntentResolver.Resolve(input);
        AssertTrue(d.Intent == CommanderIntent.Defend, "Expected Defend from macro 2, got " + d.Intent);
    }

    private static void TacticalB6aMacroRetreatFallsToHoldToLast()
    {
        var input = new TacticalIntentInput(
            WhiskeyRealism.Strategic.OperationPosture.Inherit,
            hasPlan: false, vanillaMacro: 3, commanderInitiative01: 0.5f,
            oddsConfidence: 0.0f, weakPointConfirmed: false);
        var d = TacticalCommanderIntentResolver.Resolve(input);
        AssertTrue(d.Intent == CommanderIntent.HoldToLast, "Expected HoldToLast from macro 3, got " + d.Intent);
    }

    private static void TacticalB6aMacroAttackAggressiveCommanderUpgradesToAllOut()
    {
        // Hood-style high-initiative commander on macro=attack: upgrade to all-out.
        var input = new TacticalIntentInput(
            WhiskeyRealism.Strategic.OperationPosture.Inherit,
            hasPlan: false, vanillaMacro: 1, commanderInitiative01: 0.85f,
            oddsConfidence: 0.5f, weakPointConfirmed: false);
        var d = TacticalCommanderIntentResolver.Resolve(input);
        AssertTrue(d.Intent == CommanderIntent.AllOutAttack,
            "Aggressive commander on macro-attack should escalate to AllOutAttack, got " + d.Intent);
        AssertTrue(d.AllowsCharge, "AllOutAttack must allow charge");
    }

    private static void TacticalB6aMacroAttackCautiousCommanderDeescalatesToProbe()
    {
        // McClellan-style low-initiative commander on macro=attack: down-shift to probe.
        var input = new TacticalIntentInput(
            WhiskeyRealism.Strategic.OperationPosture.Inherit,
            hasPlan: false, vanillaMacro: 1, commanderInitiative01: 0.15f,
            oddsConfidence: 0.5f, weakPointConfirmed: false);
        var d = TacticalCommanderIntentResolver.Resolve(input);
        AssertTrue(d.Intent == CommanderIntent.ProbeIntent,
            "Cautious commander on macro-attack should de-escalate to ProbeIntent, got " + d.Intent);
        AssertTrue(!d.AllowsCharge, "ProbeIntent must not allow charge");
    }

    private static void TacticalB6aMacroAttackMidInitiativePreservesAttack()
    {
        // Regression: mid-band 0.5 initiative on macro=attack → still Attack (unchanged).
        var input = new TacticalIntentInput(
            WhiskeyRealism.Strategic.OperationPosture.Inherit,
            hasPlan: false, vanillaMacro: 1, commanderInitiative01: 0.50f,
            oddsConfidence: 0.5f, weakPointConfirmed: false);
        var d = TacticalCommanderIntentResolver.Resolve(input);
        AssertTrue(d.Intent == CommanderIntent.Attack,
            "Mid-band initiative on macro-attack should preserve Attack, got " + d.Intent);
    }

    private static void TacticalB6aMacroAssaultCautiousCommanderDowngrades()
    {
        // Macro=assault (0) with very cautious commander: downgrade to Attack (not all-out).
        var input = new TacticalIntentInput(
            WhiskeyRealism.Strategic.OperationPosture.Inherit,
            hasPlan: false, vanillaMacro: 0, commanderInitiative01: 0.15f,
            oddsConfidence: 0.5f, weakPointConfirmed: false);
        var d = TacticalCommanderIntentResolver.Resolve(input);
        AssertTrue(d.Intent == CommanderIntent.Attack,
            "Cautious commander on macro-assault should downgrade to Attack, got " + d.Intent);
    }

    private static void TacticalB6aProbeIntentYieldsProbeAndFix()
    {
        var sectorL = new TacticalPlaybookSectorView(0, TacticalSectorMission.Hold, TacticalSectorPosition.Left,  ownStrength: 1000f, enemyStrength: 800f, confidence: 0.4f, strongPoint: false, flankRisk: false, ownerSubordinateShare01: 0f);
        var sectorC = new TacticalPlaybookSectorView(1, TacticalSectorMission.Hold, TacticalSectorPosition.Center, ownStrength: 1500f, enemyStrength: 1200f, confidence: 0.4f, strongPoint: false, flankRisk: false, ownerSubordinateShare01: 0f);
        var sectorR = new TacticalPlaybookSectorView(2, TacticalSectorMission.Hold, TacticalSectorPosition.Right, ownStrength: 1000f, enemyStrength: 800f, confidence: 0.4f, strongPoint: false, flankRisk: false, ownerSubordinateShare01: 0f);

        var input = new TacticalPlaybookInput(
            CommanderIntent.ProbeIntent,
            decisiveSectorId: -1,
            sectors: new[] { sectorL, sectorC, sectorR },
            hasReserveAvailable: true,
            anchoredFlankLeft: false, anchoredFlankRight: false,
            stalenessPressure: 0f);

        var decision = TacticalPlaybookLedger.Decide(input);

        AssertTrue(decision.Playbook == TacticalPlaybookKind.ProbeAndFix, "Expected ProbeAndFix, got " + decision.Playbook);
        AssertTrue(decision.RefusedFlank == TacticalRefusedFlank.None, "Probe with no flank risk must not refuse");
    }

    private static TacticalPlaybookSectorView Sector(int id, TacticalSectorPosition pos, float own, float enemy, float conf, bool flankRisk = false, bool strongPoint = false, float share = 0f, TacticalSectorMission mission = TacticalSectorMission.Hold)
    {
        return new TacticalPlaybookSectorView(id, mission, pos, own, enemy, conf, strongPoint, flankRisk, share);
    }

    private static void TacticalB6aDefendRightFlankYieldsRefuseRight()
    {
        var sectors = new[]
        {
            Sector(0, TacticalSectorPosition.Left,   1000, 800, 0.7f),
            Sector(1, TacticalSectorPosition.Center, 1500, 1200, 0.7f),
            Sector(2, TacticalSectorPosition.Right,  900,  1500, 0.7f, flankRisk: true),
        };
        var input = new TacticalPlaybookInput(CommanderIntent.Defend, 1, sectors, true, false, false, 0f);
        var d = TacticalPlaybookLedger.Decide(input);
        AssertTrue(d.Playbook == TacticalPlaybookKind.RefuseRight, "Expected RefuseRight, got " + d.Playbook);
        AssertTrue(d.RefusedFlank == TacticalRefusedFlank.Right, "Refused flank mismatch");
    }

    private static void TacticalB6aDefendLeftFlankYieldsRefuseLeft()
    {
        var sectors = new[]
        {
            Sector(0, TacticalSectorPosition.Left,   900,  1500, 0.7f, flankRisk: true),
            Sector(1, TacticalSectorPosition.Center, 1500, 1200, 0.7f),
            Sector(2, TacticalSectorPosition.Right,  1000, 800,  0.7f),
        };
        var input = new TacticalPlaybookInput(CommanderIntent.Defend, 1, sectors, true, false, false, 0f);
        var d = TacticalPlaybookLedger.Decide(input);
        AssertTrue(d.Playbook == TacticalPlaybookKind.RefuseLeft, "Expected RefuseLeft, got " + d.Playbook);
    }

    private static void TacticalB6aDefendAnchoredFlankDoesNotRefuse()
    {
        var sectors = new[]
        {
            Sector(0, TacticalSectorPosition.Left,   1000, 800, 0.7f),
            Sector(1, TacticalSectorPosition.Center, 1500, 1200, 0.7f),
            Sector(2, TacticalSectorPosition.Right,  900,  1500, 0.7f, flankRisk: true),
        };
        var input = new TacticalPlaybookInput(CommanderIntent.Defend, 1, sectors, true, false, true, 0f);
        var d = TacticalPlaybookLedger.Decide(input);
        AssertTrue(d.RefusedFlank == TacticalRefusedFlank.None, "Anchored right flank must not be refused");
        AssertTrue(d.Playbook == TacticalPlaybookKind.CombinedArmsDefense, "Expected CombinedArmsDefense, got " + d.Playbook);
    }

    private static void TacticalB6aAttackDecisiveYieldsWeakPointPressure()
    {
        var sectors = new[]
        {
            Sector(0, TacticalSectorPosition.Left,   1000, 800, 0.7f),
            Sector(1, TacticalSectorPosition.Center, 1500, 800, 0.8f, mission: TacticalSectorMission.AttackWeakPoint),
            Sector(2, TacticalSectorPosition.Right,  1000, 800, 0.7f),
        };
        var input = new TacticalPlaybookInput(CommanderIntent.Attack, 1, sectors, true, false, false, 0f);
        var d = TacticalPlaybookLedger.Decide(input);
        AssertTrue(d.Playbook == TacticalPlaybookKind.WeakPointPressure, "Expected WeakPointPressure, got " + d.Playbook);
        AssertTrue(d.MainEffortSectorId == 1, "Main effort must be sector 1");
    }

    private static void TacticalB6aAttackNoDecisiveFallsBack()
    {
        var sectors = new[]
        {
            Sector(0, TacticalSectorPosition.Left,   1000, 800, 0.4f),
            Sector(1, TacticalSectorPosition.Center, 1500, 1200, 0.4f),
            Sector(2, TacticalSectorPosition.Right,  1000, 800, 0.4f),
        };
        var input = new TacticalPlaybookInput(CommanderIntent.Attack, -1, sectors, true, false, false, 0f);
        var d = TacticalPlaybookLedger.Decide(input);
        AssertTrue(d.Playbook == TacticalPlaybookKind.ProbeAndFix, "Expected ProbeAndFix fallback, got " + d.Playbook);
    }

    private static void TacticalB6aMainEffortRejectedOnPlayerOwnership()
    {
        var sectors = new[]
        {
            Sector(0, TacticalSectorPosition.Left,   1000, 800, 0.7f),
            Sector(1, TacticalSectorPosition.Center, 1500, 800, 0.8f, share: 0.6f, mission: TacticalSectorMission.AttackWeakPoint),
            Sector(2, TacticalSectorPosition.Right,  1000, 800, 0.7f),
        };
        var input = new TacticalPlaybookInput(CommanderIntent.Attack, 1, sectors, true, false, false, 0f);
        var d = TacticalPlaybookLedger.Decide(input);
        AssertTrue(d.MainEffortSectorId == -1, "Main effort must be rejected when subordinate share > 0.5");
        AssertTrue(d.Playbook == TacticalPlaybookKind.ProbeAndFix, "Expected ProbeAndFix fallback when main effort denied");
    }

    private static void TacticalB6aHoldToLastYieldsLineHold()
    {
        var sectors = new[]
        {
            Sector(0, TacticalSectorPosition.Left,   1000, 800, 0.7f),
            Sector(1, TacticalSectorPosition.Center, 1500, 1200, 0.7f),
            Sector(2, TacticalSectorPosition.Right,  1000, 800, 0.7f),
        };
        var input = new TacticalPlaybookInput(CommanderIntent.HoldToLast, -1, sectors, false, false, false, 0f);
        var d = TacticalPlaybookLedger.Decide(input);
        AssertTrue(d.Playbook == TacticalPlaybookKind.LineHold, "Expected LineHold with no crest share (BC12), got " + d.Playbook);
        AssertTrue(d.ReservePolicy == TacticalReservePolicy.HoldReserve, "HoldToLast must keep reserve");
    }

    private static void TacticalB6aEmptySectorsYieldsEmpty()
    {
        var input = new TacticalPlaybookInput(CommanderIntent.Attack, -1, System.Array.Empty<TacticalPlaybookSectorView>(), false, false, false, 0f);
        var d = TacticalPlaybookLedger.Decide(input);
        AssertTrue(d.Reason == "no-sectors", "Expected no-sectors reason, got " + d.Reason);
        AssertTrue(d.Confidence == 0f, "Empty decision must have zero confidence");
    }

    private static TacticalLocalReactionInput ReactionInput(
        CommanderIntent intent = CommanderIntent.Defend,
        TacticalLocalReactionPolicy playbookPolicy = TacticalLocalReactionPolicy.Standard,
        TacticalSectorMission sectorMission = TacticalSectorMission.Hold,
        float sectorOdds = 1.0f,
        float sectorConfidence = 0.7f,
        bool targetVisible = true,
        bool targetBroken = false,
        bool targetStrongPoint = false,
        float morale01 = 0.7f,
        float ammoRatio01 = 0.7f,
        float casualtyRatio01 = 0.1f,
        bool flankRisk = false,
        bool wlOwnershipSafe = true,
        bool chargeCooldownReady = true,
        bool stalenessActive = false,
        bool pathRiskActive = false)
    {
        return new TacticalLocalReactionInput(
            intent,
            playbookPolicy,
            sectorMission,
            sectorOdds,
            sectorConfidence,
            targetVisible,
            targetBroken,
            targetStrongPoint,
            morale01,
            ammoRatio01,
            casualtyRatio01,
            flankRisk,
            wlOwnershipSafe,
            chargeCooldownReady,
            stalenessActive,
            pathRiskActive);
    }

    private static void TacticalB6bProbeIntentDeniesCharge()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.ProbeIntent,
            sectorConfidence: 0.35f));

        AssertEqual(LocalReaction.ProbeRange, d.Reaction, "reaction");
        AssertTrue(d.Reaction != LocalReaction.PermitCharge, "probe intent must not permit charge");
        AssertTrue(d.Reaction != LocalReaction.LimitedCounterstroke, "probe intent must not counterstroke");
    }

    private static void TacticalB6bHoldToLastBlocksFallbackPressure()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.HoldToLast,
            casualtyRatio01: 0.65f,
            morale01: 0.2f,
            ammoRatio01: 0.05f));

        AssertEqual(LocalReaction.MaintainLine, d.Reaction, "reaction");
        AssertTrue(!d.ReliefRequested, "hold to last must not request relief");
    }

    private static void TacticalB6bDefendWeakExposedTargetPermitsCounterstroke()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Defend,
            sectorOdds: 1.25f,
            sectorConfidence: 0.65f,
            targetVisible: true,
            targetStrongPoint: false));

        AssertEqual(LocalReaction.LimitedCounterstroke, d.Reaction, "reaction");
        AssertTrue(!d.ReliefRequested, "fresh counterstroke should not request relief");
    }

    private static void TacticalB6bDefendStrongpointDeniesCounterstroke()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Defend,
            sectorOdds: 1.4f,
            targetStrongPoint: true));

        AssertEqual(LocalReaction.MaintainLine, d.Reaction, "reaction");
    }

    private static void TacticalB6bAttackPermitsChargeFreshTarget()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            sectorMission: TacticalSectorMission.AttackWeakPoint,
            sectorConfidence: 0.75f));

        AssertEqual(LocalReaction.PermitCharge, d.Reaction, "reaction");
    }

    private static void TacticalB6bAttackCooldownActiveDeniesCharge()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            chargeCooldownReady: false));

        AssertEqual(LocalReaction.MaintainLine, d.Reaction, "reaction");
    }

    private static void TacticalB6bAttackStrongpointTargetDeniesCharge()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.AllOutAttack,
            targetStrongPoint: true));

        AssertEqual(LocalReaction.MaintainLine, d.Reaction, "reaction");
    }

    private static void TacticalB6bStaleOrderDowngradesToMaintainLine()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            stalenessActive: true));

        AssertEqual(LocalReaction.MaintainLine, d.Reaction, "reaction");
        AssertEqual("request-new-intent", d.Reason, "reason");
    }

    private static void TacticalB6bWlOwnershipUnsafeForcesMaintainLine()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            wlOwnershipSafe: false));

        AssertEqual(LocalReaction.MaintainLine, d.Reaction, "reaction");
        AssertEqual("wl-ownership-blocked", d.Reason, "reason");
    }

    private static void TacticalB6bPathRiskBlocksRuntimeApplication()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            pathRiskActive: true));

        AssertEqual(LocalReaction.MaintainLine, d.Reaction, "reaction");
        AssertEqual("path-risk", d.Reason, "reason");
    }

    private static void TacticalB6bBatteredFrontlineEmitsLineReliefRequest()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Hold,
            casualtyRatio01: 0.42f));

        AssertEqual(LocalReaction.LineReliefRequest, d.Reaction, "reaction");
        AssertTrue(d.ReliefRequested, "battered hold line should request relief");
    }

    private static void TacticalB6bHoldWithFlankMoraleRiskRequestsRelief()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Hold,
            flankRisk: true,
            morale01: 0.55f,
            casualtyRatio01: 0.1f,
            ammoRatio01: 0.7f));

        AssertEqual(LocalReaction.LineReliefRequest, d.Reaction, "reaction");
        AssertTrue(d.ReliefRequested, "flank risk with low morale should request relief under hold");
    }

    private static void TacticalB6bPathRiskFixMissionMaintainsLine()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            sectorMission: TacticalSectorMission.Fix,
            pathRiskActive: true));

        AssertEqual(LocalReaction.MaintainLine, d.Reaction, "reaction");
        AssertTrue(d.Reaction != LocalReaction.Screen, "path risk must not screen");
        AssertTrue(d.Reaction != LocalReaction.PermitCharge, "path risk must not permit charge");
    }

    private static void TacticalB6bDeniedAttackMaintainsLine()
    {
        var cooldown = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            chargeCooldownReady: false));
        var strongpoint = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            targetStrongPoint: true));

        AssertEqual(LocalReaction.MaintainLine, cooldown.Reaction, "cooldown reaction");
        AssertEqual(LocalReaction.MaintainLine, strongpoint.Reaction, "strongpoint reaction");
        AssertTrue(cooldown.Reaction != LocalReaction.DenyCharge, "cooldown should not emit terminal deny");
        AssertTrue(strongpoint.Reaction != LocalReaction.DenyCharge, "strongpoint should not emit terminal deny");
    }

    private static void TacticalB6bDeniedFixMissionScreensWithoutPathRisk()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            sectorMission: TacticalSectorMission.Fix,
            chargeCooldownReady: false,
            pathRiskActive: false));

        AssertEqual(LocalReaction.Screen, d.Reaction, "reaction");
    }

    private static void TacticalB6bAttackFixMissionScreensWhenChargeReady()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            sectorMission: TacticalSectorMission.Fix,
            chargeCooldownReady: true,
            pathRiskActive: false));

        AssertEqual(LocalReaction.Screen, d.Reaction, "reaction");
        AssertTrue(d.Reaction != LocalReaction.PermitCharge, "fix mission must not permit charge");
    }

    private static void TacticalB6bAttackEconomyMissionScreensWhenChargeReady()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            sectorMission: TacticalSectorMission.EconomyOfForce,
            chargeCooldownReady: true,
            pathRiskActive: false));

        AssertEqual(LocalReaction.Screen, d.Reaction, "reaction");
        AssertTrue(d.Reaction != LocalReaction.PermitCharge, "economy mission must not permit charge");
    }

    private static void TacticalB6bAttackHoldMissionMaintainsLineWhenChargeReady()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            sectorMission: TacticalSectorMission.Hold,
            chargeCooldownReady: true,
            pathRiskActive: false));

        AssertEqual(LocalReaction.MaintainLine, d.Reaction, "reaction");
        AssertTrue(d.Reaction != LocalReaction.PermitCharge, "hold mission must not permit charge");
    }

    private static void TacticalB6bAttackWeakPointMissionPermitsChargeWhenReady()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            sectorMission: TacticalSectorMission.AttackWeakPoint,
            chargeCooldownReady: true,
            pathRiskActive: false));

        AssertEqual(LocalReaction.PermitCharge, d.Reaction, "reaction");
    }

    private static void TacticalB6bConservativePolicyBlocksWeakPointCharge()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            playbookPolicy: TacticalLocalReactionPolicy.Conservative,
            sectorMission: TacticalSectorMission.AttackWeakPoint,
            chargeCooldownReady: true,
            pathRiskActive: false));

        AssertEqual(LocalReaction.MaintainLine, d.Reaction, "reaction");
        AssertContains(d.Reason, "conservative", "reason");
    }

    private static void TacticalB6bAggressivePolicyPermitsWeakPointCharge()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Attack,
            playbookPolicy: TacticalLocalReactionPolicy.Aggressive,
            sectorMission: TacticalSectorMission.AttackWeakPoint,
            chargeCooldownReady: true,
            pathRiskActive: false));

        AssertEqual(LocalReaction.PermitCharge, d.Reaction, "reaction");
    }

    private static void TacticalB6bConservativePolicyBlocksDefendCounterstroke()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Defend,
            playbookPolicy: TacticalLocalReactionPolicy.Conservative,
            sectorOdds: 1.25f,
            sectorConfidence: 0.65f,
            targetVisible: true,
            targetStrongPoint: false));

        AssertEqual(LocalReaction.MaintainLine, d.Reaction, "reaction");
        AssertContains(d.Reason, "conservative", "reason");
    }

    private static void TacticalB6bStandardPolicyPermitsDefendCounterstroke()
    {
        var d = TacticalLocalReactionScorer.Score(ReactionInput(
            intent: CommanderIntent.Defend,
            playbookPolicy: TacticalLocalReactionPolicy.Standard,
            sectorOdds: 1.25f,
            sectorConfidence: 0.65f,
            targetVisible: true,
            targetStrongPoint: false));

        AssertEqual(LocalReaction.LimitedCounterstroke, d.Reaction, "reaction");
    }

    private static TacticalReserveAvailability ReserveAvailability(
        int reserveCount = 1,
        bool hasFlankRisk = false,
        bool lastReserveIsFlankGuard = false,
        bool wlOwnershipSafe = true,
        bool stalenessActive = false)
    {
        return new TacticalReserveAvailability(
            reserveCount,
            hasFlankRisk,
            lastReserveIsFlankGuard,
            wlOwnershipSafe,
            stalenessActive);
    }

    private static TacticalReserveIntentInput ReserveInput(
        TacticalReservePolicy playbookPolicy = TacticalReservePolicy.HoldReserve,
        TacticalLocalReactionDecision[] reactions = null,
        TacticalReserveAvailability? availability = null)
    {
        return new TacticalReserveIntentInput(
            playbookPolicy,
            reactions,
            availability ?? ReserveAvailability());
    }

    private static TacticalLocalReactionDecision ReactionDecision(
        LocalReaction reaction = LocalReaction.MaintainLine,
        bool reliefRequested = false)
    {
        return new TacticalLocalReactionDecision(reaction, reliefRequested, 0.75f, "test");
    }

    private static void TacticalB6bReserveAggregatorEmitsRelieveBatteredLineWhenReserveSafe()
    {
        var d = TacticalReservePolicyLedger.Decide(ReserveInput(
            playbookPolicy: TacticalReservePolicy.RelieveBatteredLine,
            reactions: new[]
            {
                ReactionDecision(reliefRequested: true),
                ReactionDecision(LocalReaction.LineReliefRequest),
            },
            availability: ReserveAvailability(reserveCount: 2)));

        AssertEqual(TacticalReserveIntent.RelieveBatteredLine, d.Intent, "intent");
        AssertTrue(d.AllowsRuntimeMutation, "safe battered line relief should allow mutation");
        AssertEqual("battered-line", d.Reason, "reason");
    }

    private static void TacticalB6bReserveNoReserveYieldsNone()
    {
        var d = TacticalReservePolicyLedger.Decide(ReserveInput(
            playbookPolicy: TacticalReservePolicy.RelieveBatteredLine,
            reactions: new[] { ReactionDecision(reliefRequested: true) },
            availability: ReserveAvailability(reserveCount: 0)));

        AssertEqual(TacticalReserveIntent.None, d.Intent, "intent");
        AssertTrue(!d.AllowsRuntimeMutation, "no reserve must not mutate");
        AssertEqual("no-reserve", d.Reason, "reason");
    }

    private static void TacticalB6bReserveFlankRiskWithLastReserveGuards()
    {
        var d = TacticalReservePolicyLedger.Decide(ReserveInput(
            playbookPolicy: TacticalReservePolicy.FlankGuard,
            availability: ReserveAvailability(reserveCount: 1, hasFlankRisk: true, lastReserveIsFlankGuard: true)));

        AssertEqual(TacticalReserveIntent.FlankGuard, d.Intent, "intent");
        AssertTrue(!d.AllowsRuntimeMutation, "last reserve flank guard should not mutate");
        AssertEqual("last-reserve-is-flank-guard", d.Reason, "reason");
    }

    private static void TacticalB6bReserveFlankRiskWithMultipleReservesPicksFlankGuard()
    {
        var d = TacticalReservePolicyLedger.Decide(ReserveInput(
            playbookPolicy: TacticalReservePolicy.FlankGuard,
            availability: ReserveAvailability(reserveCount: 2, hasFlankRisk: true)));

        AssertEqual(TacticalReserveIntent.FlankGuard, d.Intent, "intent");
        AssertTrue(d.AllowsRuntimeMutation, "multiple reserves can assign flank guard");
        AssertEqual("flank-guard", d.Reason, "reason");
    }

    private static void TacticalB6bReserveSingleReliefRequestPreparesRelief()
    {
        var d = TacticalReservePolicyLedger.Decide(ReserveInput(
            reactions: new[] { ReactionDecision(reliefRequested: true) },
            availability: ReserveAvailability(reserveCount: 1)));

        AssertEqual(TacticalReserveIntent.PrepareRelief, d.Intent, "intent");
        AssertTrue(!d.AllowsRuntimeMutation, "single relief request should prepare only");
        AssertEqual("prepare-relief", d.Reason, "reason");
    }

    private static void TacticalB6bReserveExploitWeakPointPicksExploit()
    {
        var d = TacticalReservePolicyLedger.Decide(ReserveInput(
            playbookPolicy: TacticalReservePolicy.ExploitWeakPoint,
            reactions: Array.Empty<TacticalLocalReactionDecision>(),
            availability: ReserveAvailability(reserveCount: 1)));

        AssertEqual(TacticalReserveIntent.ExploitWeakPoint, d.Intent, "intent");
        AssertTrue(d.AllowsRuntimeMutation, "exploit weak point should allow mutation");
        AssertEqual("exploit-weak-point", d.Reason, "reason");
    }

    private static void TacticalB6bReserveWlOwnershipUnsafeHoldsReserve()
    {
        var d = TacticalReservePolicyLedger.Decide(ReserveInput(
            playbookPolicy: TacticalReservePolicy.ExploitWeakPoint,
            availability: ReserveAvailability(reserveCount: 2, wlOwnershipSafe: false)));

        AssertEqual(TacticalReserveIntent.HoldReserve, d.Intent, "intent");
        AssertTrue(!d.AllowsRuntimeMutation, "unsafe W&L ownership must not mutate");
        AssertEqual("wl-ownership-blocked", d.Reason, "reason");
    }

    private static void TacticalB6bReserveStaleOrderPreparesWithoutMutation()
    {
        var d = TacticalReservePolicyLedger.Decide(ReserveInput(
            playbookPolicy: TacticalReservePolicy.RelieveBatteredLine,
            reactions: new[]
            {
                ReactionDecision(reliefRequested: true),
                ReactionDecision(reliefRequested: true),
            },
            availability: ReserveAvailability(reserveCount: 2, stalenessActive: true)));

        AssertEqual(TacticalReserveIntent.PrepareRelief, d.Intent, "intent");
        AssertTrue(!d.AllowsRuntimeMutation, "stale order should prepare without mutation");
        AssertEqual("stale-order", d.Reason, "reason");
    }

    private static void TacticalB6cReactionContextReturnsLastDecisionPerGroup()
    {
        var context = new TacticalReactionContext();
        var first = new TacticalLocalReactionDecision(LocalReaction.Screen, false, 0.5f, "first");
        var latest = new TacticalLocalReactionDecision(LocalReaction.PermitCharge, false, 0.8f, "latest");
        var other = new TacticalLocalReactionDecision(LocalReaction.LineReliefRequest, true, 0.7f, "other");

        context.SetReaction(12, first);
        context.SetReaction(99, other);
        context.SetReaction(12, latest);

        var d = context.GetReaction(12);
        var otherD = context.GetReaction(99);
        AssertEqual(LocalReaction.PermitCharge, d.Reaction, "latest reaction");
        AssertEqual("latest", d.Reason, "latest reason");
        AssertEqual(LocalReaction.LineReliefRequest, otherD.Reaction, "other reaction");
        AssertTrue(otherD.ReliefRequested, "other group should persist");
    }

    private static void TacticalB6cReactionContextClearDiscardsAllEntries()
    {
        var context = new TacticalReactionContext();
        context.SetReaction(12, new TacticalLocalReactionDecision(LocalReaction.Screen, false, 0.5f, "stored"));

        context.Clear();

        var reaction = context.GetReaction(12);
        AssertEqual(LocalReaction.MaintainLine, reaction.Reaction, "reaction");
        AssertEqual("no-decision", reaction.Reason, "reaction reason");
    }

    private static void TacticalB6cReactionContextMissingKeyReturnsDefaultMaintain()
    {
        var context = new TacticalReactionContext();

        var d = context.GetReaction(44);

        AssertEqual(LocalReaction.MaintainLine, d.Reaction, "reaction");
        AssertEqual("no-decision", d.Reason, "reason");
    }
}
