using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Smoke 2026-10-17: the allocator's Main, SupportMain and Fix reached a Defender's nodes as MainEffort, SupportingAttack and FixingForce (CSA 7th
// Brigade, node--706122, SupportingAttack), and its refused flank reached an Attacker's nodes as "Defender" (Union 2nd Brigade, 5th Massachusetts, "Defender
// HoldObjective"). The node role is a label that every reader of the command operations sees. A Defender holds: its main, support and fixing children are
// Defender nodes, the one exception being BC13's bounded Counterstroke, which does not go through this mapping (ExploitLifecycle gives its own nodes the role
// Defender as a counterstroke and SupportingAttack otherwise). An Attacker or a side in a meeting engagement guards a refused flank as a flank guard.
internal static class NodeRoleStanceTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("node role a Defender is given no attack side role", ADefenderGetsNoAttackRole),
            ("node role a Defender's attack side roles hold instead", ADefenderHoldsWhereItWouldHaveAttacked),
            ("node role an Attacker or a meeting engagement is never given the Defender role", AnAttackerIsNeverADefender),
            ("node role an Attacker's refused flank guards the flank and nothing else does", AnAttackersRefusedFlankGuardsTheFlank),
            ("node role the roles that mean the same on every side are unchanged", TheSharedRolesAreUnchanged),
        });
    }

    private static CommandNodeIntent[] Intents(params DirectChildRole[] roles)
    {
        var intents = new CommandNodeIntent[roles.Length];
        for (int i = 0; i < roles.Length; i++)
            intents[i] = new CommandNodeIntent("n" + i, "army", roles[i], DirectChildAxis.SectorAxis, 2, 80, 2);
        return intents;
    }

    private static System.Collections.Generic.IReadOnlyList<CommandNodeOperationalState> States(BattleStance stance, params DirectChildRole[] roles)
        => CommandNodeOperationsRuntime.Build(Intents(roles),
            new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Forming, "ridge-a", 0f),
            new[] { TestFixtures.ObjectiveRecordFor("ridge-a", enemyStrength: 900f, friendlyAssignedStrength: 100f, status: TacticalObjectiveStatus.WeaklyHeld) },
            stance);

    private static readonly DirectChildRole[] AttackSide = { DirectChildRole.Main, DirectChildRole.SupportMain, DirectChildRole.Fix };

    private static void ADefenderGetsNoAttackRole()
    {
        var states = States(BattleStance.Defender, AttackSide);
        for (int i = 0; i < states.Count; i++)
        {
            AssertTrue(states[i].Role != CommandNodeRole.MainEffort && states[i].Role != CommandNodeRole.SupportingAttack && states[i].Role != CommandNodeRole.FixingForce,
                AttackSide[i] + " reached a Defender as " + states[i].Role);
            AssertEqual(CommandNodeRole.Defender, states[i].Role, AttackSide[i] + " is a Defender node");
        }
    }

    private static void ADefenderHoldsWhereItWouldHaveAttacked()
    {
        var states = States(BattleStance.Defender, AttackSide);
        for (int i = 0; i < states.Count; i++)
            AssertTrue(states[i].Task != CommandTaskType.AttackObjective && states[i].Task != CommandTaskType.SupportAttack && states[i].Task != CommandTaskType.FixEnemy && states[i].Task != CommandTaskType.Probe,
                AttackSide[i] + " tasked " + states[i].Task);
    }

    private static void AnAttackerIsNeverADefender()
    {
        foreach (BattleStance stance in new[] { BattleStance.Attacker, BattleStance.Meeting })
        {
            var states = States(stance, DirectChildRole.Main, DirectChildRole.SupportMain, DirectChildRole.Fix, DirectChildRole.Screen, DirectChildRole.Reserve,
                DirectChildRole.Fallback, DirectChildRole.RefuseLeft, DirectChildRole.RefuseRight, DirectChildRole.Unknown);
            for (int i = 0; i < states.Count; i++) AssertTrue(states[i].Role != CommandNodeRole.Defender, stance + " node " + i + " is a Defender");
        }
    }

    private static void AnAttackersRefusedFlankGuardsTheFlank()
    {
        foreach (BattleStance stance in new[] { BattleStance.Attacker, BattleStance.Meeting })
        {
            var states = States(stance, DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.RefuseLeft, DirectChildRole.RefuseRight);
            AssertEqual(CommandNodeRole.FlankMarch, states[2].Role, stance + ": the refused left is the flank guard");
            AssertEqual(CommandNodeRole.FlankMarch, states[3].Role, stance + ": and the refused right");
            AssertEqual(CommandTaskType.GuardFlank, states[2].Task, stance + ": it guards the flank");
            AssertTrue(states[0].Task != CommandTaskType.GuardFlank && states[1].Task != CommandTaskType.GuardFlank, stance + ": nothing else is told to");
        }
        // A Defender's refused flank is the Defender node as before.
        var defending = States(BattleStance.Defender, DirectChildRole.RefuseLeft);
        AssertEqual(CommandNodeRole.Defender, defending[0].Role, "a Defender's refused flank is a Defender node");
    }

    private static void TheSharedRolesAreUnchanged()
    {
        foreach (BattleStance stance in new[] { BattleStance.Attacker, BattleStance.Meeting, BattleStance.Defender })
        {
            var states = States(stance, DirectChildRole.Screen, DirectChildRole.Reserve, DirectChildRole.Fallback, DirectChildRole.Unknown);
            AssertEqual(CommandNodeRole.ScreeningForce, states[0].Role, stance + " screen");
            AssertEqual(CommandNodeRole.Reserve, states[1].Role, stance + " reserve");
            AssertEqual(CommandNodeRole.FallbackGuard, states[2].Role, stance + " fallback");
            AssertEqual(CommandNodeRole.Unknown, states[3].Role, stance + " unknown");
        }
    }
}
