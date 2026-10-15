using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical
{
    public readonly struct ChargeDenialInput
    {
        public ChargeDenialInput(
            bool denialEnabled,
            bool orchestratorArmyOn,
            DoctrineConsumerAction doctrineAction,
            bool legacyReactionDeniesCharge)
        {
            DenialEnabled = denialEnabled;
            OrchestratorArmyOn = orchestratorArmyOn;
            DoctrineAction = doctrineAction;
            LegacyReactionDeniesCharge = legacyReactionDeniesCharge;
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
    //   Infantry charges are not stance-driven: MicroAICheckForCharges is dead
    //     (4910 compares the groupaiobject GameObject with the aigroup
    //     Regiment), UpdateChargeTarget needs combatbehavior 3 (122214) which
    //     only the UI sets (203769), and melee comes from contact
    //     (MoveIntoMelee 125990 -> AssignChargeTarget).
    //
    // So the denial demotes the assault stance itself. The doctrine's Deny
    // (reserve held, fallback to line) demotes to its own defend stance (2),
    // which also ends cavalry charge eligibility and the cover-skipping
    // assault approach. Doctrine Allow (attack) agrees with the escalation and
    // keeps it, as TacticalStanceOwnership's assault yield does. The legacy
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
            if (!input.OrchestratorArmyOn && input.LegacyReactionDeniesCharge)
                return new ChargeDenialDecision(true, LegacyDemoteStance, "charge-demote");
            return Preserve("vanilla-charge-preserved");
        }

        private static ChargeDenialDecision Preserve(string reason)
        {
            return new ChargeDenialDecision(false, -1, reason);
        }
    }
}
