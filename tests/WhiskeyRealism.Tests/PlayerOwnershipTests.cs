using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.PlayerOrders;
using static TestHarness;

// BC3 (docs/superpowers/plans/2026-10-15-battle-core.md): W&L ownership flags,
// chain target resolution (chain-of-command spec §A/§B) and the dispatch type
// mapping + emitter dedupe (§C).
internal static class PlayerOwnershipTests
{
    private const int Own = 1;
    private const int Enemy = 0;

    // Fixture command tree (own side), indices into the record array:
    //   0 army
    //   ├─ 1 corps A
    //   │   ├─ 3 division A1
    //   │   │   ├─ 5 brigade A1a ── 7, 8 regiments
    //   │   │   └─ 6 brigade A1b ── 9 regiment
    //   │   └─ 4 division A2
    //   └─ 2 corps B
    private static readonly int[] FixtureParents = { -1, 0, 0, 1, 1, 3, 3, 5, 5, 6 };

    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("battle-core ownership brigade player owns descendants only", OwnershipBrigadePlayerOwnsDescendantsOnly),
            ("battle-core ownership division corps army player", OwnershipDivisionCorpsArmyPlayer),
            ("battle-core ownership cic player owns all", OwnershipCicPlayerOwnsAll),
            ("battle-core ownership takeover unit", OwnershipTakeoverUnit),
            ("battle-core ownership feud unit flagged", OwnershipFeudUnitFlagged),
            ("battle-core ownership non-wl player side owned unless ai_vs_ai", OwnershipNonWlPlayerSideOwnedUnlessAiVsAi),
            ("battle-core ownership ancestors not protected", AncestorsNotProtected),
            ("battle-core ownership chain flags cleared when the player command changes", ChainFlagsResetWhenPlayerCommandChanges),
            ("battle-core ownership direct writes refused on an undetached ancestor", DirectWriteRefusedOnUndetachedAncestor),
            ("battle-core chain detached orders group", ChainDetachedOrdersGroup),
            ("battle-core chain not detached fans out skipping player child", ChainNotDetachedFansOutSkippingPlayerChild),
            ("battle-core chain player at root fans out nothing", ChainPlayerAtRootFansOutNothing),
            ("battle-core chain player two levels down fans out at each level", ChainPlayerTwoLevelsDownFansOutAtEachLevel),
            ("battle-core chain non-ancestor targets itself", ChainNonAncestorTargetsItself),
            ("battle-core chain fan-out skips player-owned children", ChainFanOutSkipsPlayerOwnedChildren),
            ("battle-core dispatch mapper table", DispatchMapperTable),
            ("battle-core dispatch mapper never emits 13 14 15", DispatchMapperNeverEmits13_14_15),
            ("battle-core dispatch dedupe same type within 110m", DispatchDedupeSameTypeWithin110m),
        });
    }

    // ---- fixtures -------------------------------------------------------

    private static PlayerOwnershipInput WlInput(int playerCommandId, bool cic = false)
    {
        return new PlayerOwnershipInput
        {
            WlActive = true,
            AiVsAi = true, // W&L forces ai_vs_ai (29787-29793, 23755-23768)
            DebugAllArmiesAi = false,
            PlayerAlliance = Own,
            PlayerCommandInstanceId = playerCommandId,
            PlayerIsCic = cic,
        };
    }

    private static int IdOf(int index) => 100 + index;

    // Builds the fixture tree with ownership computed the way vanilla caches
    // dlcw_isundercommander: the player's command and its descendants (91203).
    private static UnitRecord[] Tree(PlayerOwnershipInput input, int playerIndex, bool playerDetached)
    {
        var records = new UnitRecord[FixtureParents.Length];
        for (int i = 0; i < records.Length; i++)
        {
            bool dlcw = playerIndex >= 0 && IsSelfOrDescendant(i, playerIndex);
            records[i] = new UnitRecord
            {
                InstanceId = IdOf(i),
                Alliance = Own,
                ParentIndex = FixtureParents[i],
                Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup |
                    PlayerOwnership.Compute(input, Own, dlcw, takenOver: false, aiFeudStance: -1),
            };
        }
        if (playerIndex >= 0 && playerDetached) records[playerIndex].Flags |= UnitFlags.PermanentlyDetached;
        PlayerOwnership.ApplyChainFlags(records, records.Length, input);
        return records;
    }

    private static bool IsSelfOrDescendant(int index, int ancestor)
    {
        for (int i = index; i >= 0; i = FixtureParents[i])
            if (i == ancestor) return true;
        return false;
    }

    private static bool Has(UnitRecord r, UnitFlags flag) => (r.Flags & flag) == flag;

    private static ChainResolution Resolve(UnitRecord[] records, int groupIndex)
    {
        var result = new ChainResolution { TargetIndices = new int[16] };
        TacticalChainOrderPlanner.ResolveTargets(records, records.Length, groupIndex, new int[16], ref result);
        return result;
    }

    private static void AssertTargets(ChainResolution result, int[] expected, string label)
    {
        AssertEqual(expected.Length, result.Count, label + " count");
        for (int i = 0; i < expected.Length; i++)
            AssertEqual(expected[i], result.TargetIndices[i], label + " target " + i);
    }

    // ---- ownership ------------------------------------------------------

    private static void OwnershipBrigadePlayerOwnsDescendantsOnly()
    {
        var input = WlInput(IdOf(5));
        var t = Tree(input, playerIndex: 5, playerDetached: true);
        int[] owned = { 5, 7, 8 };
        for (int i = 0; i < t.Length; i++)
        {
            bool expectOwned = Array.IndexOf(owned, i) >= 0;
            AssertEqual(expectOwned, Has(t[i], UnitFlags.PlayerOwned), "player-owned " + i);
        }
        AssertTrue(Has(t[5], UnitFlags.PlayerCommand), "brigade is the player command");
        foreach (int a in new[] { 0, 1, 3 })
            AssertTrue(Has(t[a], UnitFlags.AncestorOfPlayerCommand), "ancestor " + a);
        foreach (int n in new[] { 2, 4, 5, 6, 7, 8, 9 })
            AssertFalse(Has(t[n], UnitFlags.AncestorOfPlayerCommand), "not an ancestor " + n);
        AssertTrue(Has(t[3], UnitFlags.PlayerCommandDetached), "ancestor carries the detach state");
    }

    private static void OwnershipDivisionCorpsArmyPlayer()
    {
        foreach (int player in new[] { 3, 1, 0 })
        {
            var input = WlInput(IdOf(player));
            var t = Tree(input, player, playerDetached: true);
            for (int i = 0; i < t.Length; i++)
            {
                bool inSubtree = IsSelfOrDescendant(i, player);
                bool isAncestor = !inSubtree && IsSelfOrDescendant(player, i);
                AssertEqual(inSubtree, Has(t[i], UnitFlags.PlayerOwned), "player " + player + " owns " + i);
                AssertEqual(isAncestor, Has(t[i], UnitFlags.AncestorOfPlayerCommand), "player " + player + " ancestor " + i);
            }
        }
    }

    private static void OwnershipCicPlayerOwnsAll()
    {
        // Vanilla 91185-91188: a CIC player owns every unit of the alliance. The
        // cached dlcw flag lags a slow cycle (112240), so the CIC bit alone owns.
        var input = WlInput(IdOf(0), cic: true);
        AssertTrue(Has(new UnitRecord { Flags = PlayerOwnership.Compute(input, Own, false, false, -1) }, UnitFlags.PlayerOwned),
            "CIC owns own-alliance unit without dlcw");
        AssertFalse(Has(new UnitRecord { Flags = PlayerOwnership.Compute(input, Enemy, false, false, -1) }, UnitFlags.PlayerOwned),
            "CIC never owns the enemy");
    }

    private static void OwnershipTakeoverUnit()
    {
        var wl = WlInput(IdOf(5));
        AssertEqual(UnitFlags.PlayerOwned, PlayerOwnership.Compute(wl, Own, false, takenOver: true, aiFeudStance: -1),
            "W&L take-over is owned");
        var nonWl = wl;
        nonWl.WlActive = false;
        AssertEqual(UnitFlags.None, PlayerOwnership.Compute(nonWl, Own, false, takenOver: true, aiFeudStance: -1),
            "take-over list is ignored outside a career");
    }

    private static void OwnershipFeudUnitFlagged()
    {
        var input = WlInput(IdOf(5));
        AssertEqual(UnitFlags.Feud, PlayerOwnership.Compute(input, Enemy, false, false, aiFeudStance: 2), "feud stance 2");
        AssertEqual(UnitFlags.Feud, PlayerOwnership.Compute(input, Enemy, false, false, aiFeudStance: 0), "feud stance 0");
        AssertEqual(UnitFlags.None, PlayerOwnership.Compute(input, Enemy, false, false, aiFeudStance: -1), "no feud (vanilla default -1, 112841)");
        AssertFalse(PlayerOwnership.CanWrite(UnitFlags.Feud, modeAllowsWrites: true, out SkipReason reason), "feud refused");
        AssertEqual(SkipReason.Feud, reason, "feud reason");
    }

    private static void OwnershipNonWlPlayerSideOwnedUnlessAiVsAi()
    {
        var input = new PlayerOwnershipInput { WlActive = false, AiVsAi = false, PlayerAlliance = Own };
        AssertEqual(UnitFlags.PlayerOwned, PlayerOwnership.Compute(input, Own, false, false, -1), "player side owned");
        AssertEqual(UnitFlags.None, PlayerOwnership.Compute(input, Enemy, false, false, -1), "enemy not owned");

        input.AiVsAi = true;
        // Outside a career vanilla's slow tick still caches dlcw = (alliance ==
        // player alliance) (112240 -> 91170-91176), so dlcw must not own here.
        AssertEqual(UnitFlags.None, PlayerOwnership.Compute(input, Own, dlcwUnderCommander: true, false, -1),
            "ai_vs_ai: player side AI-run even with stale dlcw");

        input.AiVsAi = false;
        input.DebugAllArmiesAi = true;
        AssertEqual(UnitFlags.None, PlayerOwnership.Compute(input, Own, false, false, -1), "debug all-armies AI");
    }

    private static void AncestorsNotProtected()
    {
        var input = WlInput(IdOf(5));
        foreach (bool detached in new[] { true, false })
        {
            var t = Tree(input, 5, detached);
            foreach (int a in new[] { 0, 1, 3 })
            {
                AssertFalse(Has(t[a], UnitFlags.PlayerOwned), "ancestor " + a + " not owned");
                AssertTrue(PlayerOwnership.CanWrite(t[a].Flags, true, out SkipReason r), "ancestor " + a + " writable");
                AssertEqual(SkipReason.None, r, "ancestor reason");
            }
            AssertFalse(PlayerOwnership.CanWrite(t[5].Flags, true, out SkipReason own), "player command refused");
            AssertEqual(SkipReason.PlayerOwned, own, "player command reason");
            AssertFalse(PlayerOwnership.CanWrite(t[7].Flags, true, out _), "player regiment refused");
        }
        AssertFalse(PlayerOwnership.CanWrite(UnitFlags.None, modeAllowsWrites: false, out SkipReason mode), "mode off");
        AssertEqual(SkipReason.ModeNotActive, mode, "mode reason");
    }

    private static void ChainFlagsResetWhenPlayerCommandChanges()
    {
        var input = WlInput(IdOf(5));
        var t = Tree(input, 5, playerDetached: false);
        input.PlayerCommandInstanceId = IdOf(2);
        PlayerOwnership.ApplyChainFlags(t, t.Length, input);
        AssertFalse(Has(t[5], UnitFlags.PlayerCommand), "old command cleared");
        AssertFalse(Has(t[3], UnitFlags.AncestorOfPlayerCommand), "old ancestor cleared");
        AssertTrue(Has(t[2], UnitFlags.PlayerCommand), "new command flagged");
        AssertTrue(Has(t[0], UnitFlags.AncestorOfPlayerCommand), "new ancestor flagged");
        AssertFalse(Has(t[1], UnitFlags.AncestorOfPlayerCommand), "sibling corps not an ancestor");

        input.PlayerCommandInstanceId = 0;
        PlayerOwnership.ApplyChainFlags(t, t.Length, input);
        for (int i = 0; i < t.Length; i++)
            AssertEqual(UnitFlags.None, t[i].Flags & (UnitFlags.PlayerCommand | UnitFlags.AncestorOfPlayerCommand | UnitFlags.PlayerCommandDetached),
                "no command clears chain flags " + i);
    }

    private static void DirectWriteRefusedOnUndetachedAncestor()
    {
        var input = WlInput(IdOf(5));
        var undetached = Tree(input, 5, playerDetached: false);
        AssertTrue(PlayerOwnership.NeedsChainFanOut(undetached[3].Flags), "undetached parent needs fan-out");
        AssertTrue(PlayerOwnership.NeedsChainFanOut(undetached[0].Flags), "undetached root ancestor needs fan-out");
        AssertFalse(PlayerOwnership.NeedsChainFanOut(undetached[6].Flags), "peer brigade orderable as a group");

        var detached = Tree(input, 5, playerDetached: true);
        AssertFalse(PlayerOwnership.NeedsChainFanOut(detached[3].Flags), "detached: vanilla skips the player's subtree");
    }

    // ---- chain resolution (chain-of-command spec §B) ---------------------

    private static void ChainDetachedOrdersGroup()
    {
        var t = Tree(WlInput(IdOf(5)), 5, playerDetached: true);
        var result = Resolve(t, 3);
        AssertTargets(result, new[] { 3 }, "detached");
        AssertFalse(result.FannedOut, "detached group order not fanned out");
        AssertTrue(result.PlayerShareNeedsDispatch, "ancestor order needs a player dispatch");
    }

    private static void ChainNotDetachedFansOutSkippingPlayerChild()
    {
        var t = Tree(WlInput(IdOf(5)), 5, playerDetached: false);
        var result = Resolve(t, 3);
        AssertTargets(result, new[] { 6 }, "fan-out at the player's parent");
        AssertTrue(result.FannedOut, "fanned out");
        AssertTrue(result.PlayerShareNeedsDispatch, "player share dispatched");
    }

    private static void ChainPlayerAtRootFansOutNothing()
    {
        var t = Tree(WlInput(IdOf(0)), 0, playerDetached: false);
        for (int i = 0; i < t.Length; i++)
            AssertFalse(Has(t[i], UnitFlags.AncestorOfPlayerCommand), "no ancestors " + i);

        var root = Resolve(t, 0);
        AssertEqual(0, root.Count, "player command itself is never a target");
        AssertFalse(root.FannedOut, "nothing fanned out");
        AssertFalse(root.PlayerShareNeedsDispatch, "no dispatch");
    }

    private static void ChainPlayerTwoLevelsDownFansOutAtEachLevel()
    {
        var t = Tree(WlInput(IdOf(5)), 5, playerDetached: false);
        var corps = Resolve(t, 1);
        AssertTargets(corps, new[] { 4, 6 }, "corps: division A2, then brigade A1b");
        AssertTrue(corps.FannedOut, "corps fanned out");

        var army = Resolve(t, 0);
        AssertTargets(army, new[] { 2, 4, 6 }, "army: corps B, division A2, brigade A1b");
        AssertTrue(army.PlayerShareNeedsDispatch, "army order dispatched");
    }

    private static void ChainNonAncestorTargetsItself()
    {
        var t = Tree(WlInput(IdOf(5)), 5, playerDetached: false);
        var result = Resolve(t, 6);
        AssertTargets(result, new[] { 6 }, "peer brigade");
        AssertFalse(result.FannedOut, "no fan-out");
        AssertFalse(result.PlayerShareNeedsDispatch, "no dispatch for a peer");

        var empty = Resolve(t, -1);
        AssertEqual(0, empty.Count, "invalid index");
    }

    private static void ChainFanOutSkipsPlayerOwnedChildren()
    {
        var input = WlInput(IdOf(5));
        var t = Tree(input, 5, playerDetached: false);
        t[6].Flags |= PlayerOwnership.Compute(input, Own, false, takenOver: true, aiFeudStance: -1);
        var result = Resolve(t, 3);
        AssertEqual(0, result.Count, "taken-over peer is not ordered");
        AssertTrue(result.FannedOut, "still a fan-out");
    }

    // ---- dispatch (chain-of-command spec §C) -----------------------------

    private static void DispatchMapperTable()
    {
        int None = TacticalDispatchTypeMapper.None;
        AssertEqual(0, TacticalDispatchTypeMapper.Map(CommandTaskType.AttackObjective, targetEnemyHeld: true, chargeSupported: false), "attack enemy-held");
        AssertEqual(12, TacticalDispatchTypeMapper.Map(CommandTaskType.AttackObjective, false, false), "attack own objective (vanilla 7002)");
        AssertEqual(4, TacticalDispatchTypeMapper.Map(CommandTaskType.AttackObjective, true, true), "charge-supported attack");
        AssertEqual(4, TacticalDispatchTypeMapper.Map(CommandTaskType.AttackObjective, false, true), "assault");
        AssertEqual(2, TacticalDispatchTypeMapper.Map(CommandTaskType.SupportAttack, true, false), "support");
        AssertEqual(12, TacticalDispatchTypeMapper.Map(CommandTaskType.HoldObjective, false, false), "hold");
        AssertEqual(12, TacticalDispatchTypeMapper.Map(CommandTaskType.HoldChoke, false, false), "defend choke");
        AssertEqual(12, TacticalDispatchTypeMapper.Map(CommandTaskType.FallBackToLine, false, false), "fall back to line");
        AssertEqual(11, TacticalDispatchTypeMapper.Map(CommandTaskType.ReserveWait, false, false), "reserve wait");
        AssertEqual(11, TacticalDispatchTypeMapper.Map(CommandTaskType.ReleaseReserve, false, false), "release reserve");
        AssertEqual(1, TacticalDispatchTypeMapper.Map(CommandTaskType.Screen, false, false), "screen");
        AssertEqual(1, TacticalDispatchTypeMapper.Map(CommandTaskType.Probe, false, false), "probe");
        AssertEqual(1, TacticalDispatchTypeMapper.Map(CommandTaskType.GuardFlank, false, false), "guard flank");
        AssertEqual(3, TacticalDispatchTypeMapper.Map(CommandTaskType.FixEnemy, true, false), "fix enemy");
        foreach (var task in new[] { CommandTaskType.None, CommandTaskType.Scout, CommandTaskType.FormUp,
                     CommandTaskType.AdvanceToAssembly, CommandTaskType.Delay, CommandTaskType.Consolidate,
                     CommandTaskType.RecoverStuckOrder })
            AssertEqual(None, TacticalDispatchTypeMapper.Map(task, true, false), "no dispatch for " + task);
    }

    private static void DispatchMapperNeverEmits13_14_15()
    {
        foreach (CommandTaskType task in Enum.GetValues(typeof(CommandTaskType)))
        foreach (bool held in new[] { false, true })
        foreach (bool charge in new[] { false, true })
        {
            int type = TacticalDispatchTypeMapper.Map(task, held, charge);
            AssertFalse(type == 13 || type == 14 || type == 15, "vanilla-owned type for " + task);
            AssertTrue(type == TacticalDispatchTypeMapper.None || type == 0 || type == 1 || type == 2 ||
                type == 3 || type == 4 || type == 11 || type == 12, "battle type only for " + task);
        }
    }

    private static void DispatchDedupeSameTypeWithin110m()
    {
        var state = new PlayerOrderDedupeState();
        AssertFalse(state.IsDuplicateDispatch(0, 0f, 0f, givenOrderSession: 5, meters: 110f), "nothing recorded");

        state.RecordDispatch(0, 0f, 0f, givenOrderSession: 5);
        AssertTrue(state.IsDuplicateDispatch(0, 100f, 0f, 5, 110f), "same type within 110 m");
        AssertFalse(state.IsDuplicateDispatch(0, 0f, 120f, 5, 110f), "same type beyond 110 m");
        AssertFalse(state.IsDuplicateDispatch(12, 10f, 0f, 5, 110f), "different type");
        AssertFalse(state.IsDuplicateDispatch(0, 10f, 0f, 6, 110f), "a newer given order (session moved) re-arms the dispatch");

        state.ClearForBattleBoundary("next");
        AssertFalse(state.IsDuplicateDispatch(0, 10f, 0f, 5, 110f), "battle boundary clears the record");
    }
}
