using UnityEngine;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical
{
    // Single gate for every Whiskey write into vanilla battle state (stance,
    // formation, combat behavior, waypoints, macroai, withdrawal). A thin reader of
    // the battle-core ownership model (Tactical/Battle/PlayerOwnership.cs).
    //
    // Vanilla anchors (decompile, build 14703111):
    //   AIBattle.SetSide (3408): isplayeraiorfeud = 0 for the enemy AI side,
    //     1 for the player's side, 2 for the feud instance.
    //   AIBattle.UpdateAITasks (5857): runs only when isplayeraiorfeud == 0
    //     or GameVars.ai_vs_ai. W&L sets ai_vs_ai = true, so the player-side
    //     instance (1) and the feud instance (2) also run the macro cycle there.
    //   BattleUnits.IsUnitUnderCommander (91168-91222) → Regiment.dlcw_isundercommander:
    //     the player's command, its descendants, take-overs (IsUnitTakenOver 45840),
    //     CIC/army-group cases. That is the W&L ownership set; ancestors are not in it.
    //   AIBattle.PerformAIActionDLCWL (5101): vanilla's own micro-AI gate refuses
    //     dlcw_isundercommander units and nothing else.
    //   Deployment 85321-85336 detaches the player's command, TakeOverUnitBattle
    //     164455 a taken-over unit; ChangeStance(…, 0) (90786-90793) clears it.
    //     SetGroupFormation (91988-92030) skips a detached subtree (119865) and has
    //     no dlcw filter.
    //
    // Contract:
    //   - Tactical Commander Mode must be Active (MonitorOnly/Off write nothing).
    //   - PlayerOwned units (W&L player's command + descendants, take-overs, CIC;
    //     outside W&L the player alliance unless ai_vs_ai/debug_allarmiesai) and
    //     feuding units (ai_feudstance >= 0) are never written.
    //   - Ancestors of the player's command are AI and are not PlayerOwned. The
    //     battle-core executor resolves orders on them through
    //     TacticalChainOrderPlanner. The Regiment overloads serve writers that
    //     cannot fan out (#61 until BC5a, deployment #60, inline paths): both
    //     refuse a group with an exposed player-owned descendant — the player's
    //     attached command or an attached take-over — because a group write there
    //     would reach player-owned units.
    //   - Fail closed: any exception means "do not write".
    public static class TacticalWriteGuard
    {
        public static bool ModeAllowsWrites()
        {
            try
            {
                var plugin = Plugin.Instance;
                return plugin != null && TacticalCommanderModePolicy.AllowsWrites(plugin.TacticalCommanderModeValue);
            }
            catch
            {
                return false;
            }
        }

        // The feud AIBattle (isplayeraiorfeud == 2) duplicates side 0's macro
        // cycle under W&L. Battle-level writers (macroai, posture executor,
        // orchestrator-driven orders) must skip it.
        public static bool IsFeudInstance(AIBattle battle)
        {
            try { return battle == null || battle.isplayeraiorfeud == 2; }
            catch { return true; }
        }

        // Snapshot path (battle-core executor): flags were computed by the builder.
        public static bool CanWrite(in UnitRecord unit, out SkipReason reason)
        {
            return PlayerOwnership.CanWrite(unit.Flags, ModeAllowsWrites(), out reason);
        }

        // Direct writers and paths without a snapshot (deployment #60, inline
        // patch writes).
        public static bool CanWrite(Regiment unit)
        {
            if (!ModeAllowsWrites()) return false;
            try
            {
                if ((Object)unit == null) return false;
                PlayerOwnershipInput input = ReadOwnershipInput();
                UnitFlags flags = OwnershipFlags(unit, input);
                return PlayerOwnership.CanWrite(flags, modeAllowsWrites: true, out _) &&
                    !PlayerOwnership.DirectWriteProtected(flags, HasExposedOwnedDescendant(unit, input), out _);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsPlayerProtected(Regiment unit)
        {
            return IsPlayerProtected(unit, out _);
        }

        // PlayerOwned units, feuding units, and groups whose order would reach an
        // exposed player-owned descendant. An ancestor of a detached player command
        // is not protected.
        public static bool IsPlayerProtected(Regiment unit, out string reason)
        {
            reason = null;
            try
            {
                if ((Object)unit == null) { reason = "null"; return true; }
                PlayerOwnershipInput input = ReadOwnershipInput();
                UnitFlags flags = OwnershipFlags(unit, input);
                bool exposed = (flags & UnitFlags.PlayerOwned) == 0 && HasExposedOwnedDescendant(unit, input);
                if (!PlayerOwnership.DirectWriteProtected(flags, exposed, out SkipReason skip)) return false;
                if (skip == SkipReason.Feud) reason = "feud";
                else if (exposed) reason = "owned-descendant-attached";
                else if (!input.WlActive) reason = "player-side";
                else if (unit.dlcw_isundercommander) reason = "wl-under-commander";
                else if (input.PlayerIsCic) reason = "wl-cic";
                else reason = "dlc-taken-over";
                return true;
            }
            catch
            {
                reason = "exception";
                return true;
            }
        }

        // Per-battle-tick ownership context; BC1's snapshot builder passes it to
        // PlayerOwnership.Compute/ApplyChainFlags.
        internal static PlayerOwnershipInput ReadOwnershipInput()
        {
            bool wl = DLC_WL.dlc_scenarioactive;
            Regiment current = wl ? PlayerCurrentCommand() : null;
            return new PlayerOwnershipInput
            {
                WlActive = wl,
                AiVsAi = GameVars.ai_vs_ai,
                DebugAllArmiesAi = GameVars.debug_allarmiesai,
                PlayerAlliance = GameVars.playeralliance,
                PlayerCommandInstanceId = (Object)current != null ? current.gameObject.GetInstanceID() : 0,
                // IsCommanderInChief (47475) indexes GameVars.commander unchecked.
                PlayerIsCic = wl && HasValidChosenCommander() && DLC_WL.IsCommanderInChief(),
            };
        }

        // PlayerOwned / Feud for one unit; the snapshot builder captures the same bits.
        internal static UnitFlags OwnershipFlags(Regiment unit, in PlayerOwnershipInput input)
        {
            bool takenOver = input.WlActive && DLC_WL.IsUnitTakenOver(unit);
            return PlayerOwnership.Compute(input, unit.alliance, unit.dlcw_isundercommander, takenOver, unit.ai_feudstance);
        }

        // Whether a group order on `group` would reach a player-owned descendant
        // (PlayerOwnership.IsExposedOwned). Descendants come from allattachedunits
        // (all descendants, the D5 command-tree source), never transform.parent; a
        // detached node shields a descendant when that descendant is in the node's
        // own allattachedunits. Outside a career ownership is alliance-wide, so a
        // descendant is never owned unless the group is.
        private static bool HasExposedOwnedDescendant(Regiment group, in PlayerOwnershipInput input)
        {
            if (!input.WlActive) return false;
            Regiment[] attached = group.allattachedunits;
            if (attached == null) return false;
            for (int i = 0; i < attached.Length; i++)
            {
                Regiment d = attached[i];
                if ((Object)d == null || (Object)d == (Object)group) continue;
                UnitFlags flags = OwnershipFlags(d, input) & UnitFlags.PlayerOwned;
                if (flags == UnitFlags.None) continue;
                if (d.permanentlydetached) flags |= UnitFlags.PermanentlyDetached;
                if (PlayerOwnership.IsExposedOwned(flags, DetachedNodeBetween(group, attached, d))) return true;
            }
            return false;
        }

        // groupDescendants includes the group itself (GetComponentsInChildren,
        // 129574); the group's own detach never shields its subtree.
        private static bool DetachedNodeBetween(Regiment group, Regiment[] groupDescendants, Regiment descendant)
        {
            for (int i = 0; i < groupDescendants.Length; i++)
            {
                Regiment node = groupDescendants[i];
                if ((Object)node == null || (Object)node == (Object)group || (Object)node == (Object)descendant || !node.permanentlydetached) continue;
                Regiment[] below = node.allattachedunits;
                if (below == null) continue;
                for (int j = 0; j < below.Length; j++)
                    if ((Object)below[j] == (Object)descendant) return true;
            }
            return false;
        }

        private static Regiment PlayerCurrentCommand()
        {
            return HasValidChosenCommander() ? GameVars.commander[DLC_WL.dlc_chosencommander].currentcommand : null;
        }

        private static bool HasValidChosenCommander()
        {
            int chosen = DLC_WL.dlc_chosencommander;
            // A null entry would throw in the snapshot builder and empty the snapshot.
            return chosen >= 0 && GameVars.commander != null && chosen < GameVars.commander.Count &&
                GameVars.commander[chosen] != null;
        }
    }
}
