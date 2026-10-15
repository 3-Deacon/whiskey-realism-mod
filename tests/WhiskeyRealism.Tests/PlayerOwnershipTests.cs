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
    private const int OwnSide = 1;
    private const int EnemySide = 0;

    // Literal flag shorthands for the fixtures.
    private const UnitFlags PO = UnitFlags.PlayerOwned;
    private const UnitFlags PD = UnitFlags.PermanentlyDetached;
    private const UnitFlags Anc = UnitFlags.AncestorOfPlayerCommand;
    private const UnitFlags Cmd = UnitFlags.PlayerCommand;
    private const UnitFlags CD = UnitFlags.PlayerCommandDetached;
    private const UnitFlags Chain = Anc | Cmd | CD;

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
            ("battle-core ownership group with an attached player-owned descendant needs fan-out", GroupWithAttachedOwnedDescendantNeedsFanOut),
            ("battle-core ownership detached node shields its owned subtree", DetachedNodeShieldsOwnedSubtree),
            ("battle-core ownership direct-write decision for regiment overloads", DirectWriteDecisionForRegimentOverloads),
            ("battle-core ownership feuding unit protected from direct writers", FeudUnitProtectedFromDirectWriters),
            ("battle-core ownership feud is the AI group's state for direct writers", DirectWriterFeudFollowsAiGroup),
            ("battle-core ownership per-unit guard refuses owned regiments under a writable group", PerUnitGuardRefusesOwnedRegimentsUnderWritableGroup),
            ("battle-core ownership group's own detach never shields its descendants", GroupOwnDetachNeverShieldsDescendants),
            ("battle-core chain detached orders group", ChainDetachedOrdersGroup),
            ("battle-core chain not detached fans out skipping player child", ChainNotDetachedFansOutSkippingPlayerChild),
            ("battle-core chain player at root fans out nothing", ChainPlayerAtRootFansOutNothing),
            ("battle-core chain player two levels down fans out at each level", ChainPlayerTwoLevelsDownFansOutAtEachLevel),
            ("battle-core chain non-ancestor targets itself", ChainNonAncestorTargetsItself),
            ("battle-core chain fan-out skips player-owned children", ChainFanOutSkipsPlayerOwnedChildren),
            ("battle-core chain take-over parent fans out around the attached take-over", ChainTakeoverParentFansOut),
            ("battle-core dispatch mapper table", DispatchMapperTable),
            ("battle-core dispatch mapper never emits 13 14 15", DispatchMapperNeverEmits13_14_15),
            ("battle-core dispatch dedupe same type within 110m", DispatchDedupeSameTypeWithin110m),
            ("battle-core dispatch dedupe re-arms when the given order is cleared or differs", DispatchDedupeRearmsWhenGivenOrderClearedOrDiffers),
            ("battle-core dispatch dedupe never suppresses a type-11 refresh", DispatchDedupeNeverSuppressesReserveRefresh),
        });
    }

    // ---- fixtures (literal flags; no rule under test builds them) --------

    private static PlayerOwnershipInput WlInput(int playerCommandId, bool cic = false)
    {
        return new PlayerOwnershipInput
        {
            WlActive = true,
            AiVsAi = true, // W&L forces ai_vs_ai (29787-29793, 23755-23768)
            DebugAllArmiesAi = false,
            PlayerAlliance = OwnSide,
            PlayerCommandInstanceId = playerCommandId,
            PlayerIsCic = cic,
        };
    }

    private static int IdOf(int index) => 100 + index;

    private static UnitRecord[] Records(params UnitFlags[] flags)
    {
        var records = new UnitRecord[FixtureParents.Length];
        for (int i = 0; i < records.Length; i++)
        {
            records[i] = new UnitRecord
            {
                InstanceId = IdOf(i),
                Alliance = OwnSide,
                ParentIndex = FixtureParents[i],
                Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup | flags[i],
            };
        }
        return records;
    }

    // Brigade A1a (5) is the player's command: it and its regiments 7, 8 carry
    // dlcw_isundercommander (91203). Raw = as the builder captures it; Chained =
    // with the chain flags written out by hand.
    private static UnitRecord[] BrigadePlayerRaw(bool detached)
    {
        UnitFlags d = detached ? PD : UnitFlags.None;
        return Records(0, 0, 0, 0, 0, PO | d, 0, PO, PO, 0);
    }

    private static UnitRecord[] BrigadePlayerChained(bool detached)
    {
        UnitFlags d = detached ? PD : UnitFlags.None;
        UnitFlags c = detached ? CD : UnitFlags.None;
        return Records(Anc | c, Anc | c, 0, Anc | c, 0, PO | Cmd | c | d, 0, PO, PO, 0);
    }

    private static bool Has(UnitRecord r, UnitFlags flag) => (r.Flags & flag) == flag;

    private static void AssertFlags(UnitRecord[] expected, UnitRecord[] actual, string label)
    {
        for (int i = 0; i < expected.Length; i++)
            AssertEqual(expected[i].Flags, actual[i].Flags, label + " flags " + i);
    }

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
        bool[] dlcw = { false, false, false, false, false, true, false, true, true, false };
        for (int i = 0; i < dlcw.Length; i++)
            AssertEqual(dlcw[i] ? PO : UnitFlags.None, PlayerOwnership.Compute(input, OwnSide, dlcw[i], false, -1), "owned " + i);

        foreach (bool detached in new[] { true, false })
        {
            var t = BrigadePlayerRaw(detached);
            PlayerOwnership.ApplyChainFlags(t, t.Length, input);
            AssertFlags(BrigadePlayerChained(detached), t, detached ? "detached" : "attached");
        }
    }

    private static void OwnershipDivisionCorpsArmyPlayer()
    {
        // (player index, owned subtree, ancestors) written out per echelon.
        var cases = new (int player, int[] owned, int[] ancestors)[]
        {
            (3, new[] { 3, 5, 6, 7, 8, 9 }, new[] { 0, 1 }),
            (1, new[] { 1, 3, 4, 5, 6, 7, 8, 9 }, new[] { 0 }),
            (0, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, new int[0]),
        };
        foreach (var c in cases)
        {
            var raw = new UnitFlags[FixtureParents.Length];
            foreach (int o in c.owned) raw[o] = PO;
            var t = Records(raw);
            PlayerOwnership.ApplyChainFlags(t, t.Length, WlInput(IdOf(c.player)));
            for (int i = 0; i < t.Length; i++)
            {
                AssertEqual(Array.IndexOf(c.owned, i) >= 0, Has(t[i], PO), "player " + c.player + " owns " + i);
                AssertEqual(Array.IndexOf(c.ancestors, i) >= 0, Has(t[i], Anc), "player " + c.player + " ancestor " + i);
                AssertEqual(i == c.player, Has(t[i], Cmd), "player " + c.player + " command " + i);
            }
        }
    }

    private static void OwnershipCicPlayerOwnsAll()
    {
        // Vanilla 91187: a CIC player owns every unit of the alliance. The cached
        // dlcw flag can lag a refresh, so the CIC bit alone owns.
        var input = WlInput(IdOf(0), cic: true);
        AssertEqual(PO, PlayerOwnership.Compute(input, OwnSide, false, false, -1), "CIC owns own-alliance unit without dlcw");
        AssertEqual(UnitFlags.None, PlayerOwnership.Compute(input, EnemySide, false, false, -1), "CIC never owns the enemy");
    }

    private static void OwnershipTakeoverUnit()
    {
        var wl = WlInput(IdOf(5));
        AssertEqual(PO, PlayerOwnership.Compute(wl, OwnSide, false, takenOver: true, aiFeudStance: -1), "W&L take-over is owned");
        var nonWl = wl;
        nonWl.WlActive = false;
        AssertEqual(UnitFlags.None, PlayerOwnership.Compute(nonWl, OwnSide, false, takenOver: true, aiFeudStance: -1),
            "take-over list is ignored outside a career");
    }

    private static void OwnershipFeudUnitFlagged()
    {
        var input = WlInput(IdOf(5));
        AssertEqual(UnitFlags.Feud, PlayerOwnership.Compute(input, EnemySide, false, false, aiFeudStance: 2), "feud stance 2");
        AssertEqual(UnitFlags.Feud, PlayerOwnership.Compute(input, EnemySide, false, false, aiFeudStance: 0), "feud stance 0");
        AssertEqual(UnitFlags.None, PlayerOwnership.Compute(input, EnemySide, false, false, aiFeudStance: -1), "no feud (vanilla default -1, 112841)");
        AssertFalse(PlayerOwnership.CanWrite(UnitFlags.Feud, modeAllowsWrites: true, out SkipReason reason), "feud refused");
        AssertEqual(SkipReason.Feud, reason, "feud reason");
    }

    private static void OwnershipNonWlPlayerSideOwnedUnlessAiVsAi()
    {
        var input = new PlayerOwnershipInput { WlActive = false, AiVsAi = false, PlayerAlliance = OwnSide };
        AssertEqual(PO, PlayerOwnership.Compute(input, OwnSide, false, false, -1), "player side owned");
        AssertEqual(UnitFlags.None, PlayerOwnership.Compute(input, EnemySide, false, false, -1), "enemy not owned");

        input.AiVsAi = true;
        // Outside a career vanilla still caches dlcw = (alliance == player alliance)
        // (Regiment initialize 113065 -> 91170-91176), so dlcw must not own here.
        AssertEqual(UnitFlags.None, PlayerOwnership.Compute(input, OwnSide, dlcwUnderCommander: true, false, -1),
            "ai_vs_ai: player side AI-run even with stale dlcw");

        input.AiVsAi = false;
        input.DebugAllArmiesAi = true;
        AssertEqual(UnitFlags.None, PlayerOwnership.Compute(input, OwnSide, false, false, -1), "debug all-armies AI");
    }

    private static void AncestorsNotProtected()
    {
        foreach (bool detached in new[] { true, false })
        {
            var t = BrigadePlayerChained(detached);
            foreach (int a in new[] { 0, 1, 3 })
            {
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
        var t = BrigadePlayerChained(detached: false);
        PlayerOwnership.ApplyChainFlags(t, t.Length, WlInput(IdOf(2)));
        AssertFlags(Records(Anc, 0, Cmd, 0, 0, PO, 0, PO, PO, 0), t, "command moved to corps B");

        PlayerOwnership.ApplyChainFlags(t, t.Length, WlInput(0));
        for (int i = 0; i < t.Length; i++)
            AssertEqual(UnitFlags.None, t[i].Flags & Chain, "no command clears chain flags " + i);
    }

    private static void GroupWithAttachedOwnedDescendantNeedsFanOut()
    {
        var t = BrigadePlayerChained(detached: false);
        foreach (int g in new[] { 0, 1, 3 })
            AssertTrue(PlayerOwnership.NeedsChainFanOut(t, t.Length, g), "attached player below " + g);
        foreach (int g in new[] { 2, 4, 6, 9 })
            AssertFalse(PlayerOwnership.NeedsChainFanOut(t, t.Length, g), "no owned descendant below " + g);
        AssertFalse(PlayerOwnership.NeedsChainFanOut(t, t.Length, 7), "a leaf has no descendants");
        AssertFalse(PlayerOwnership.NeedsChainFanOut(t, t.Length, -1), "invalid index");
    }

    private static void DetachedNodeShieldsOwnedSubtree()
    {
        // Vanilla group orders build each level's hierarchy tree with
        // excludedechainedunits (SetGroupFormation 91988-92030 -> 119865), so a
        // detached node and everything below it are skipped.
        var detachedPlayer = BrigadePlayerChained(detached: true);
        foreach (int g in new[] { 0, 1, 3 })
            AssertFalse(PlayerOwnership.NeedsChainFanOut(detachedPlayer, detachedPlayer.Length, g), "detached player shields " + g);

        // Player brigade attached, but its AI division detached: the division
        // itself must fan out, the corps and army above it need not.
        var detachedDivision = Records(Anc, Anc, 0, Anc | PD, 0, PO | Cmd, 0, PO, PO, 0);
        AssertTrue(PlayerOwnership.NeedsChainFanOut(detachedDivision, detachedDivision.Length, 3), "division orders reach the player");
        AssertFalse(PlayerOwnership.NeedsChainFanOut(detachedDivision, detachedDivision.Length, 1), "corps order skips the detached division");
        AssertFalse(PlayerOwnership.NeedsChainFanOut(detachedDivision, detachedDivision.Length, 0), "army order skips the detached division");
    }

    private static void DirectWriteDecisionForRegimentOverloads()
    {
        AssertTrue(PlayerOwnership.IsExposedOwned(PO, detachedNodeBetween: false), "attached owned descendant exposed");
        AssertFalse(PlayerOwnership.IsExposedOwned(PO | PD, false), "detached owned descendant shielded");
        AssertFalse(PlayerOwnership.IsExposedOwned(PO, detachedNodeBetween: true), "detached node between shields");
        AssertFalse(PlayerOwnership.IsExposedOwned(UnitFlags.None, false), "AI descendant never exposes");

        AssertTrue(PlayerOwnership.DirectWriteProtected(PO, false, out SkipReason owned), "owned unit protected");
        AssertEqual(SkipReason.PlayerOwned, owned, "owned reason");
        AssertTrue(PlayerOwnership.DirectWriteProtected(Anc, exposedOwnedDescendant: true, out SkipReason exposed), "attached ancestor protected");
        AssertEqual(SkipReason.PlayerOwned, exposed, "exposed reason");
        AssertFalse(PlayerOwnership.DirectWriteProtected(Anc | CD, exposedOwnedDescendant: false, out SkipReason none), "detached ancestor writable");
        AssertEqual(SkipReason.None, none, "writable reason");
    }

    private static void FeudUnitProtectedFromDirectWriters()
    {
        // #61 gates on IsPlayerProtected only, so a feuding group (vanilla's feud
        // instance moves it, UpdateMicroAI 5549 / CheckForFeudGroupActions 5871)
        // must read as protected there too.
        AssertTrue(PlayerOwnership.DirectWriteProtected(UnitFlags.Feud, false, out SkipReason reason), "feud protected");
        AssertEqual(SkipReason.Feud, reason, "feud reason");
        AssertTrue(PlayerOwnership.DirectWriteProtected(UnitFlags.Feud | PO, false, out SkipReason both), "owned feud");
        AssertEqual(SkipReason.PlayerOwned, both, "player ownership reported first");
    }

    // ActivateFeud (116613) sets ai_feudstance on the group and AddUnitToPool(overwriteaigroups)
    // points every descendant's groupaiobject at it (7637-7648); MicroAICheckForCharges and the
    // artillery micro test aigroup.ai_feudstance (4917/4922, 3789/3834). TacticalWriteGuard's
    // Regiment overloads apply the same group rule.
    private static void DirectWriterFeudFollowsAiGroup()
    {
        AssertEqual(UnitFlags.None, PlayerOwnership.DirectFeudFlags(-1, -1, snapshotFeud: false), "no feud anywhere");
        AssertEqual(UnitFlags.Feud, PlayerOwnership.DirectFeudFlags(-1, 2, snapshotFeud: false), "regiment of a feuding AI group");
        AssertEqual(UnitFlags.Feud, PlayerOwnership.DirectFeudFlags(1, -1, snapshotFeud: false), "the feuding group itself");
        AssertEqual(UnitFlags.Feud, PlayerOwnership.DirectFeudFlags(-1, -1, snapshotFeud: true), "snapshot propagation (ancestor feud)");
        AssertTrue(PlayerOwnership.DirectWriteProtected(PlayerOwnership.DirectFeudFlags(-1, 0, false), false, out SkipReason reason), "refused");
        AssertEqual(SkipReason.Feud, reason, "feud reason");
    }

    private static void PerUnitGuardRefusesOwnedRegimentsUnderWritableGroup()
    {
        // Detached player brigade (5): its AI division (3) passes the group guard,
        // but a Whiskey loop over the division's allattachedunits (every transform
        // descendant, UpdateAllAttachedUnits 129574) reaches the player's
        // regiments, which are never detached themselves (85316 needs !ai_vs_ai).
        // The per-unit decision must refuse them.
        var t = BrigadePlayerChained(detached: true);
        AssertFalse(PlayerOwnership.DirectWriteProtected(t[3].Flags, PlayerOwnership.NeedsChainFanOut(t, t.Length, 3), out _),
            "division writable as a group");
        foreach (int u in new[] { 7, 8 })
        {
            AssertTrue(PlayerOwnership.DirectWriteProtected(t[u].Flags, PlayerOwnership.NeedsChainFanOut(t, t.Length, u), out SkipReason r),
                "player regiment " + u + " refused per unit");
            AssertEqual(SkipReason.PlayerOwned, r, "per-unit reason");
        }
        AssertFalse(PlayerOwnership.DirectWriteProtected(t[9].Flags, PlayerOwnership.NeedsChainFanOut(t, t.Length, 9), out _),
            "AI regiment writable");
    }

    private static void GroupOwnDetachNeverShieldsDescendants()
    {
        // allattachedunits includes the group itself (GetComponentsInChildren,
        // 129574). An AI group with a stance is detached itself (ChangeStance
        // 90786-90793), but an order given to it still reaches its attached subtree.
        var t = Records(Anc, Anc, 0, Anc | PD, 0, PO | Cmd, 0, PO, PO, 0);
        AssertTrue(PlayerOwnership.NeedsChainFanOut(t, t.Length, 3), "detached division with attached player");
    }

    // ---- chain resolution (chain-of-command spec §B) ---------------------

    private static void ChainDetachedOrdersGroup()
    {
        var result = Resolve(BrigadePlayerChained(detached: true), 3);
        AssertTargets(result, new[] { 3 }, "detached");
        AssertFalse(result.FannedOut, "detached group order not fanned out");
        AssertTrue(result.PlayerShareNeedsDispatch, "ancestor order needs a player dispatch");
    }

    private static void ChainNotDetachedFansOutSkippingPlayerChild()
    {
        var result = Resolve(BrigadePlayerChained(detached: false), 3);
        AssertTargets(result, new[] { 6 }, "fan-out at the player's parent");
        AssertTrue(result.FannedOut, "fanned out");
        AssertTrue(result.PlayerShareNeedsDispatch, "player share dispatched");
    }

    private static void ChainPlayerAtRootFansOutNothing()
    {
        var t = Records(PO | Cmd, PO, PO, PO, PO, PO, PO, PO, PO, PO);
        var root = Resolve(t, 0);
        AssertEqual(0, root.Count, "player command itself is never a target");
        AssertFalse(root.FannedOut, "nothing fanned out");
        AssertFalse(root.PlayerShareNeedsDispatch, "no dispatch");
        AssertEqual(0, Resolve(t, 3).Count, "owned subordinate never a target");
    }

    private static void ChainPlayerTwoLevelsDownFansOutAtEachLevel()
    {
        var t = BrigadePlayerChained(detached: false);
        var corps = Resolve(t, 1);
        AssertTargets(corps, new[] { 4, 6 }, "corps: division A2, then brigade A1b");
        AssertTrue(corps.FannedOut, "corps fanned out");

        var army = Resolve(t, 0);
        AssertTargets(army, new[] { 2, 4, 6 }, "army: corps B, division A2, brigade A1b");
        AssertTrue(army.PlayerShareNeedsDispatch, "army order dispatched");
    }

    private static void ChainNonAncestorTargetsItself()
    {
        var t = BrigadePlayerChained(detached: false);
        var result = Resolve(t, 6);
        AssertTargets(result, new[] { 6 }, "peer brigade");
        AssertFalse(result.FannedOut, "no fan-out");
        AssertFalse(result.PlayerShareNeedsDispatch, "no dispatch for a peer");

        AssertEqual(0, Resolve(t, -1).Count, "invalid index");
    }

    private static void ChainFanOutSkipsPlayerOwnedChildren()
    {
        var t = BrigadePlayerChained(detached: false);
        t[6].Flags |= PO; // brigade A1b taken over (45827)
        t[9].Flags |= PO;
        var result = Resolve(t, 3);
        AssertEqual(0, result.Count, "taken-over peer is not ordered");
        AssertTrue(result.FannedOut, "still a fan-out");
    }

    private static void ChainTakeoverParentFansOut()
    {
        // Player commands corps B (2, detached). Brigade A1b (6) and its regiment
        // were taken over (45827); the player has re-attached it.
        var attached = Records(Anc | CD, 0, PO | Cmd | CD | PD, 0, 0, 0, PO, 0, 0, PO);
        var corps = Resolve(attached, 1);
        AssertTargets(corps, new[] { 4, 5 }, "corps A: division A2, then brigade A1a");
        AssertTrue(corps.FannedOut, "fanned out around the take-over");
        AssertFalse(corps.PlayerShareNeedsDispatch, "not the player's chain: no dispatch");

        // TakeOverUnitBattle detaches the unit (164455): the group order is safe.
        attached[6].Flags |= PD;
        var detached = Resolve(attached, 1);
        AssertTargets(detached, new[] { 1 }, "detached take-over");
        AssertFalse(detached.FannedOut, "group order");
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

    private static GivenOrderView Given(int type, float x, float z, int session) => new GivenOrderView(true, type, x, z, session);

    private static void DispatchDedupeSameTypeWithin110m()
    {
        var state = new PlayerOrderDedupeState();
        AssertFalse(state.IsDuplicateDispatch(0, 0f, 0f, Given(0, 0f, 0f, 5), meters: 110f), "nothing recorded");

        state.RecordDispatch(0, 0f, 0f, givenOrderSession: 5);
        var current = Given(0, 0f, 0f, 5);
        AssertTrue(state.IsDuplicateDispatch(0, 100f, 0f, current, 110f), "same type within 110 m");
        AssertFalse(state.IsDuplicateDispatch(0, 0f, 120f, current, 110f), "same type beyond 110 m");
        AssertFalse(state.IsDuplicateDispatch(12, 10f, 0f, current, 110f), "different type");
        AssertFalse(state.IsDuplicateDispatch(0, 10f, 0f, Given(0, 0f, 0f, 6), 110f), "a newer given order (session moved) re-arms the dispatch");

        state.ClearForBattleBoundary("next");
        AssertFalse(state.IsDuplicateDispatch(0, 10f, 0f, current, 110f), "battle boundary clears the record");
    }

    private static void DispatchDedupeRearmsWhenGivenOrderClearedOrDiffers()
    {
        var state = new PlayerOrderDedupeState();
        state.RecordDispatch(0, 0f, 0f, givenOrderSession: 5);

        // Vanilla nulls givenorder without moving the session: end of day
        // (CheckClearingBattleOrdersEOD 47989) and zone completion (123533).
        AssertFalse(state.IsDuplicateDispatch(0, 0f, 0f, default(GivenOrderView), 110f), "cleared given order re-arms");

        // Our attempt was refused and an older order is still active.
        AssertFalse(state.IsDuplicateDispatch(0, 0f, 0f, Given(2, 500f, 500f, 5), 110f), "different active type re-arms");
        AssertFalse(state.IsDuplicateDispatch(0, 0f, 0f, Given(0, 400f, 0f, 5), 110f), "active target elsewhere re-arms");
    }

    private static void DispatchDedupeNeverSuppressesReserveRefresh()
    {
        // A type-11 order onto an active type 11 is vanilla's in-place refresh
        // (flag4, no session bump); vanilla's own 8643 test keeps it from repeating.
        var state = new PlayerOrderDedupeState();
        state.RecordDispatch(11, 0f, 0f, givenOrderSession: 5);
        AssertFalse(state.IsDuplicateDispatch(11, 0f, 0f, Given(11, 0f, 0f, 5), 110f), "type 11 always reaches vanilla");
    }
}
