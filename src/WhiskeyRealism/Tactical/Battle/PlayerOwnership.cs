// W&L / player ownership of battle units (battle-core spec §3; chain-of-command
// spec §A). Pure: the snapshot builder (BC1) and TacticalWriteGuard's Regiment
// fallback capture the vanilla bits; this file turns them into UnitFlags.
//
// Vanilla anchors (decompile, build 14703111):
//   BattleUnits.IsUnitUnderCommander 91168-91222: the player's command and its
//     transform-descendants (91203), units whose detachment parent is under the
//     player (91207-91216), take-overs (IsUnitTakenOver 45840), every unit of the
//     alliance for a CIC player (91187) and the player's army group (91191).
//     The result is cached in Regiment.dlcw_isundercommander on the slow tick
//     (112240) — ungated on the career, and with checkfordlc it returns
//     "alliance == player alliance" outside a career (91170-91176). So the
//     cached flag only means ownership while W&L is active.
//   AIBattle.SetSide 3408: the player-side AIBattle runs the macro AI only under
//     ai_vs_ai / debug_allarmiesai; otherwise the player alliance is the human's.
//   Regiment.ai_feudstance: −1 = no feud (112841, 116559); CalculateFeud sets
//     0..3 (116613); feuding groups run in the feud AIBattle (UpdateMicroAI 5549).
//   Deployment detach 85321-85336 and TakeOverUnitBattle 164455: vanilla detaches
//     the player's command and taken-over units (permanentlydetached);
//     ChangeStance(…, 0) clears it (90786-90793).
//   BattleUnits.SetGroupFormation (91988-92030) builds each level's hierarchy tree
//     with excludedechainedunits, so GetAttachedUnitsReg drops a detached child
//     (119865) together with everything below it; the per-node moves (92071-92111)
//     skip only detached nodes. There is no dlcw filter on that path.
//
// Ancestors of the player's command are AI and are never PlayerOwned. A group
// order is safe exactly when no player-owned unit below the group is exposed —
// owned, and with no detached node on its path below the group. Otherwise the
// order must fan out (TacticalChainOrderPlanner); writers that cannot fan out
// must not write the group at all.

namespace WhiskeyRealism.Tactical.Battle
{
    public struct PlayerOwnershipInput
    {
        public bool WlActive;
        public bool AiVsAi;
        public bool DebugAllArmiesAi;
        public int PlayerAlliance;
        public int PlayerCommandInstanceId; // 0 = none
        public bool PlayerIsCic;
    }

    public static class PlayerOwnership
    {
        private const UnitFlags ChainFlags =
            UnitFlags.PlayerCommand | UnitFlags.AncestorOfPlayerCommand | UnitFlags.PlayerCommandDetached;

        // PlayerOwned and Feud bits for one unit. dlcwUnderCommander, takenOver and
        // aiFeudStance are the raw vanilla values (Regiment.dlcw_isundercommander,
        // DLC_WL.IsUnitTakenOver, Regiment.ai_feudstance).
        public static UnitFlags Compute(in PlayerOwnershipInput input, int alliance, bool dlcwUnderCommander, bool takenOver, int aiFeudStance)
        {
            UnitFlags flags = UnitFlags.None;
            bool owned;
            if (input.WlActive)
            {
                // The CIC bit covers the slow-tick lag of the cached dlcw flag (91187).
                owned = dlcwUnderCommander || takenOver ||
                    (input.PlayerIsCic && alliance == input.PlayerAlliance);
            }
            else
            {
                owned = alliance == input.PlayerAlliance && !input.AiVsAi && !input.DebugAllArmiesAi;
            }

            if (owned) flags |= UnitFlags.PlayerOwned;
            if (aiFeudStance >= 0) flags |= UnitFlags.Feud;
            return flags;
        }

        // Sets PlayerCommand on the player's current command, AncestorOfPlayerCommand
        // on every command-tree ancestor (ParentIndex chain, D5), and
        // PlayerCommandDetached on all of them when the command carries
        // PermanentlyDetached. Clears stale chain flags first, so it is safe to call
        // every tick and when the player changes command.
        public static void ApplyChainFlags(UnitRecord[] own, int count, in PlayerOwnershipInput input)
        {
            if (own == null) return;
            if (count > own.Length) count = own.Length;

            int player = -1;
            for (int i = 0; i < count; i++)
            {
                own[i].Flags &= ~ChainFlags;
                if (input.WlActive && input.PlayerCommandInstanceId != 0 && own[i].InstanceId == input.PlayerCommandInstanceId)
                    player = i;
            }
            if (player < 0) return;

            UnitFlags detached = (own[player].Flags & UnitFlags.PermanentlyDetached) != 0
                ? UnitFlags.PlayerCommandDetached
                : UnitFlags.None;
            own[player].Flags |= UnitFlags.PlayerCommand | detached;

            // Bounded walk: a malformed parent map can never loop forever.
            int ancestor = own[player].ParentIndex;
            for (int steps = 0; ancestor >= 0 && ancestor < count && steps < count; steps++)
            {
                own[ancestor].Flags |= UnitFlags.AncestorOfPlayerCommand | detached;
                ancestor = own[ancestor].ParentIndex;
            }
        }

        // The write guard over snapshot flags: Commander Mode Active, not
        // player-owned, not feuding. Ancestors pass — the chain planner decides how
        // they are ordered.
        public static bool CanWrite(UnitFlags flags, bool modeAllowsWrites, out SkipReason reason)
        {
            if (!modeAllowsWrites) { reason = SkipReason.ModeNotActive; return false; }
            if ((flags & UnitFlags.PlayerOwned) != 0) { reason = SkipReason.PlayerOwned; return false; }
            if ((flags & UnitFlags.Feud) != 0) { reason = SkipReason.Feud; return false; }
            reason = SkipReason.None;
            return true;
        }

        // A descendant a group order would reach although it is player-owned:
        // owned, not detached itself, and no detached node between it and the group.
        public static bool IsExposedOwned(UnitFlags descendant, bool detachedNodeBetween)
        {
            return (descendant & UnitFlags.PlayerOwned) != 0 &&
                (descendant & UnitFlags.PermanentlyDetached) == 0 &&
                !detachedNodeBetween;
        }

        // A group order on own[groupIndex] would reach an exposed player-owned
        // descendant (the player's attached command, or an attached take-over), so
        // it must fan out. The group's own detach state does not matter: an order
        // given to the group itself always reaches it.
        public static bool NeedsChainFanOut(UnitRecord[] own, int count, int groupIndex)
        {
            if (own == null) return false;
            if (count > own.Length) count = own.Length;
            if (groupIndex < 0 || groupIndex >= count) return false;

            for (int u = 0; u < count; u++)
            {
                if (u == groupIndex || (own[u].Flags & UnitFlags.PlayerOwned) == 0) continue;
                bool detachedBetween = false;
                int node = own[u].ParentIndex;
                for (int steps = 0; node >= 0 && node < count && steps < count; steps++)
                {
                    if (node == groupIndex)
                    {
                        if (IsExposedOwned(own[u].Flags, detachedBetween)) return true;
                        break;
                    }
                    if ((own[node].Flags & UnitFlags.PermanentlyDetached) != 0) detachedBetween = true;
                    node = own[node].ParentIndex;
                }
            }
            return false;
        }

        // The decision behind TacticalWriteGuard's Regiment overloads, for writers
        // that cannot fan out: a player-owned unit, a group with an exposed
        // player-owned descendant, or a feuding unit (vanilla's feud instance owns
        // its moves) is protected.
        public static bool DirectWriteProtected(UnitFlags flags, bool exposedOwnedDescendant, out SkipReason reason)
        {
            if ((flags & UnitFlags.PlayerOwned) != 0 || exposedOwnedDescendant)
            {
                reason = SkipReason.PlayerOwned;
                return true;
            }
            if ((flags & UnitFlags.Feud) != 0)
            {
                reason = SkipReason.Feud;
                return true;
            }
            reason = SkipReason.None;
            return false;
        }

        public static bool NeedsChainFanOut(BattleSnapshot s, int groupIndex)
        {
            return s != null && NeedsChainFanOut(s.Own, s.OwnCount, groupIndex);
        }

        public static void ApplyChainFlags(BattleSnapshot s, in PlayerOwnershipInput input)
        {
            if (s == null) return;
            ApplyChainFlags(s.Own, s.OwnCount, input);
        }
    }
}
