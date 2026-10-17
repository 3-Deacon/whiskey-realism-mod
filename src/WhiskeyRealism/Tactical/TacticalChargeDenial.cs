using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical
{
    public readonly struct ChargeDenialInput
    {
        public ChargeDenialInput(
            bool denialEnabled,
            bool orchestratorArmyOn,
            DoctrineConsumerAction doctrineAction,
            bool legacyReactionDeniesCharge,
            bool fixEnemyTask = false)
        {
            DenialEnabled = denialEnabled;
            OrchestratorArmyOn = orchestratorArmyOn;
            DoctrineAction = doctrineAction;
            LegacyReactionDeniesCharge = legacyReactionDeniesCharge;
            FixEnemyTask = fixEnemyTask;
        }

        // Enable Tactical Charge Denial.
        public bool DenialEnabled { get; }
        // Enable Tactical Orchestrator Army.
        public bool OrchestratorArmyOn { get; }
        // DoctrineConsumerDecisions.DecideStance for the group, Observe when the
        // doctrine resolver has no order for it.
        public DoctrineConsumerAction DoctrineAction { get; }
        // The legacy B6c local-reaction producer marked the group DenyCharge
        // (and its producer flags are on).
        public bool LegacyReactionDeniesCharge { get; }
        // The doctrine order's task is FixEnemy. Unit churn: a fixing force pins the enemy; the task maps to the attack stance (3) and never to the
        // assault (4), which vanilla's own strength test escalates a strong line group to (AdjustGroupAIStance 4240).
        public bool FixEnemyTask { get; }
    }

    public readonly struct ChargeDenialDecision
    {
        public ChargeDenialDecision(bool demotes, int demoteStance, string reason)
        {
            Demotes = demotes;
            DemoteStance = demotes ? demoteStance : -1;
            Reason = reason;
        }

        public bool Demotes { get; }
        // ai_stance to write through StanceWriteSource.ChargeDemotion, -1 when preserving.
        public int DemoteStance { get; }
        public string Reason { get; }
    }

    // What to do with a vanilla assault (ai_stance 4) on an AI group.
    //
    // Vanilla anchors (decompile build 14703111):
    //   AdjustGroupAIStance 4240: vanilla escalates a strong line group to 4
    //     under macroai 0/1.
    //   Assault vs attack (3): the micro reposition toward the enemy uses
    //     distanceofmaxformicroaiplacement[4] and skipcover: true (5694-5728)
    //     and skirmishers are reattached / not deployed (5363, 5374).
    //   AdjustCavalryBehavior 5047: cavalry charge targets only while
    //     ai_stance != 1 && != 2, so a group at 3 still charges with its
    //     cavalry and a group at 2 does not.
    //   Vanilla infantry charges are not stance-driven: MicroAICheckForCharges
    //     is dead (4910 compares the groupaiobject GameObject with the aigroup
    //     Regiment), UpdateChargeTarget needs combatbehavior 3 (122214) which in
    //     vanilla only the UI sets (203769), and melee comes from contact
    //     (MoveIntoMelee 125990 -> AssignChargeTarget). The battle-core charge
    //     doctrine (BC6b) arms behavior 3 only in assault stance, so a demotion
    //     also makes it hand its charges back.
    //
    // So the denial demotes the assault stance itself. The doctrine's Deny
    // (reserve held, fallback to line) demotes to its own defend stance (2),
    // which also ends cavalry charge eligibility and the cover-skipping
    // assault approach. Doctrine Allow (attack) agrees with the escalation and
    // keeps it, as TacticalStanceOwnership's assault yield does. A FixEnemy task
    // (no doctrine opinion on its stance) is taken back to the stance it maps to,
    // attack (3): a fixing force pins the enemy and does not close (unit churn,
    // 2026-10-17: a halted fixing brigade at assault with nine thousand enemy
    // near). The legacy
    // local-reaction DenyCharge (only produced with the orchestrator army off)
    // keeps its historical demotion to attack (3).
    public static class TacticalChargeDenial
    {
        public const int DoctrineDemoteStance = 2;
        public const int LegacyDemoteStance = 3;

        public static ChargeDenialDecision DecideForVanillaAssault(in ChargeDenialInput input)
        {
            if (!input.DenialEnabled)
                return Preserve("vanilla-charge-preserved");
            if (input.DoctrineAction == DoctrineConsumerAction.Deny)
                return new ChargeDenialDecision(true, DoctrineDemoteStance, "charge-demote-doctrine");
            if (input.DoctrineAction == DoctrineConsumerAction.Allow)
                return Preserve("doctrine-attack-agrees");
            if (input.FixEnemyTask)
                return new ChargeDenialDecision(true, TacticalCommandTaskToStanceMapper.Map(CommandTaskType.FixEnemy), "charge-demote-fix");
            if (!input.OrchestratorArmyOn && input.LegacyReactionDeniesCharge)
                return new ChargeDenialDecision(true, LegacyDemoteStance, "charge-demote");
            return Preserve("vanilla-charge-preserved");
        }

        // The charge-demotion intent (battle core, ChargeDemotionIntentProducer):
        // an AI brigade that vanilla holds in assault (ai_stanceordered 4) and
        // whose cached answer (TacticalBattleCoordinatorRuntime
        // .PopulateResolvedDecisionsForSide, this class's decision) is
        // DemoteToHold gets a Safety intent on the Stance channel to the cached
        // stance (1-3, else LegacyDemoteStance). Safety outranks every other
        // stance source on the arbiter, so it also beats the posture executor's
        // assault and denies the infantry charge doctrine (BC6b reverts its
        // charge once the group is out of assault). Not gated on the group being
        // settled: a demotion that waits for the march to end is too late.
        internal static bool TryDemotionIntent(in UnitRecord group, in TacticalResolvedGroupDecision resolved, out BattleOrderIntent intent)
        {
            intent = default(BattleOrderIntent);
            if ((group.Flags & UnitFlags.IsGroup) == 0 || !TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(group.UnitTyp)) return false;
            if ((group.Flags & UnitFlags.PlayerOwned) != 0) return false;
            if (group.AiStanceOrdered != TacticalCommandTaskToStanceMapper.StanceAssault) return false;
            if (resolved.ChargeAction != ChargeOverrideAction.DemoteToHold) return false;

            int stance = resolved.FinalStance >= 1 && resolved.FinalStance <= 3 ? resolved.FinalStance : LegacyDemoteStance;
            intent = new BattleOrderIntent
            {
                GroupId = group.InstanceId,
                Channel = OrderChannel.Stance,
                Value = stance,
                Heading = -1f,
                Priority = IntentPriority.Safety,
                Source = IntentSource.ChargeDemotion,
                Reason = "charge-demotion",
            };
            return true;
        }

        private static ChargeDenialDecision Preserve(string reason)
        {
            return new ChargeDenialDecision(false, -1, reason);
        }
    }
}
