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
    //   Deployment 85321-85336 detaches the player's command; ChangeStance(…, 0)
    //     (90786-90793) clears that; group orders skip detached children (119865).
    //
    // Contract:
    //   - Tactical Commander Mode must be Active (MonitorOnly/Off write nothing).
    //   - PlayerOwned units (W&L player's command + descendants, take-overs, CIC;
    //     outside W&L the player alliance unless ai_vs_ai/debug_allarmiesai) and
    //     feuding units (ai_feudstance >= 0) are never written.
    //   - Ancestors of the player's command are AI and are not protected. The
    //     battle-core executor resolves orders on them through
    //     TacticalChainOrderPlanner. Direct writers without chain resolution
    //     (CanWrite(Regiment)) are refused on an ancestor whose player command is
    //     not detached, since a group write there would reach the player's units.
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
                    !PlayerOwnership.NeedsChainFanOut(flags | ChainFlags(unit));
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

        // PlayerOwned only; ancestors of the player's command are NOT protected.
        public static bool IsPlayerProtected(Regiment unit, out string reason)
        {
            reason = null;
            try
            {
                if ((Object)unit == null) { reason = "null"; return true; }
                PlayerOwnershipInput input = ReadOwnershipInput();
                if ((OwnershipFlags(unit, input) & UnitFlags.PlayerOwned) == 0) return false;
                if (!input.WlActive) reason = "player-side";
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

        private static UnitFlags OwnershipFlags(Regiment unit, in PlayerOwnershipInput input)
        {
            bool takenOver = input.WlActive && DLC_WL.IsUnitTakenOver(unit);
            return PlayerOwnership.Compute(input, unit.alliance, unit.dlcw_isundercommander, takenOver, unit.ai_feudstance);
        }

        // Chain bits for one group without a snapshot. Ancestry comes from the
        // group's allattachedunits (all descendants, the D5 command-tree source),
        // never transform.parent.
        private static UnitFlags ChainFlags(Regiment unit)
        {
            if (!DLC_WL.dlc_scenarioactive) return UnitFlags.None;
            Regiment current = PlayerCurrentCommand();
            if ((Object)current == null || (Object)current == (Object)unit) return UnitFlags.None;

            Regiment[] attached = unit.allattachedunits;
            if (attached == null) return UnitFlags.None;
            for (int i = 0; i < attached.Length; i++)
            {
                if ((Object)attached[i] != (Object)current) continue;
                return current.permanentlydetached
                    ? UnitFlags.AncestorOfPlayerCommand | UnitFlags.PlayerCommandDetached
                    : UnitFlags.AncestorOfPlayerCommand;
            }
            return UnitFlags.None;
        }

        private static Regiment PlayerCurrentCommand()
        {
            return HasValidChosenCommander() ? GameVars.commander[DLC_WL.dlc_chosencommander].currentcommand : null;
        }

        private static bool HasValidChosenCommander()
        {
            int chosen = DLC_WL.dlc_chosencommander;
            return chosen >= 0 && GameVars.commander != null && chosen < GameVars.commander.Count;
        }
    }
}
