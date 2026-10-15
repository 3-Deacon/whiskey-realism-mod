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
//   Deployment detach 85321-85336: vanilla detaches the player's command
//     (permanentlydetached); ChangeStance(…, 0) clears it (90786-90793), and
//     GetAttachedUnitsReg(excludedechainedunits) skips detached children (119865).
//
// Ancestors of the player's command are AI and are never PlayerOwned. Whether a
// group order on one may be issued directly or must fan out is the chain
// planner's call (TacticalChainOrderPlanner), driven by the chain flags below.

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

        // A group order on this unit would reach the player's attached command:
        // it is an ancestor and the player's command is not detached, so vanilla's
        // group orders would not skip the player's subtree (119865).
        public static bool NeedsChainFanOut(UnitFlags flags)
        {
            return (flags & UnitFlags.AncestorOfPlayerCommand) != 0 &&
                (flags & UnitFlags.PlayerCommandDetached) == 0;
        }

        public static void ApplyChainFlags(BattleSnapshot s, in PlayerOwnershipInput input)
        {
            if (s == null) return;
            ApplyChainFlags(s.Own, s.OwnCount, input);
        }
    }
}
