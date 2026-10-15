using UnityEngine;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical
{
    // Single gate for every Whiskey write into vanilla battle state
    // (stance, formation, combat behavior, waypoints, macroai, withdrawal).
    //
    // Vanilla anchors (decompile, build 14703111):
    //   AIBattle.SetSide (3408): isplayeraiorfeud = 0 for the enemy AI side,
    //     1 for the player's side, 2 for the feud instance.
    //   AIBattle.UpdateAITasks (5857): runs only when isplayeraiorfeud == 0
    //     or GameVars.ai_vs_ai. W&L sets ai_vs_ai = true, so the player-side
    //     instance (1) and the feud instance (2) also run the macro cycle there.
    //   BattleUnits.ChangeStance (90772): any stance > 0 sets
    //     permanentlydetached and, for the player alliance outside ai_vs_ai,
    //     hands the unit to aibattle[1]'s pool — so player-owned units must
    //     never reach a Whiskey stance write.
    //
    // Contract:
    //   - Tactical Commander Mode must be Active (MonitorOnly/Off write nothing).
    //   - The unit must be AI-controlled: enemy alliance, or ai_vs_ai /
    //     debug_allarmiesai for the player's alliance.
    //   - W&L: the player's directly-commanded unit, its command chain, any
    //     unit with dlcw_isundercommander attached, and DLC-taken-over units
    //     are protected.
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

        public static bool IsAiControlled(Regiment unit)
        {
            try
            {
                if ((Object)unit == null) return false;
                if (unit.alliance != GameVars.playeralliance) return true;
                return GameVars.ai_vs_ai || GameVars.debug_allarmiesai;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsPlayerProtected(Regiment group)
        {
            return IsPlayerProtected(group, out _);
        }

        public static bool IsPlayerProtected(Regiment group, out string reason)
        {
            reason = null;
            try
            {
                if ((Object)group == null) { reason = "null"; return true; }
                if (!IsAiControlled(group)) { reason = "player-owned"; return true; }
                if (!WlOwnershipSafe(group)) { reason = "wl-ownership"; return true; }
                if (IsWlCurrentCommandOrChain(group)) { reason = "wl-current-command"; return true; }
                if (DLC_WL.dlc_scenarioactive && DLC_WL.IsUnitTakenOver(group)) { reason = "dlc-taken-over"; return true; }
                return false;
            }
            catch
            {
                reason = "exception";
                return true;
            }
        }

        public static bool CanWrite(Regiment unit)
        {
            return ModeAllowsWrites() && !IsPlayerProtected(unit);
        }

        private static bool WlOwnershipSafe(Regiment group)
        {
            // dlcw_isundercommander marks the W&L player's own command and its
            // attached units (DLC_WL.AppointCommand 44269, 45827). The prior
            // copy in #61 returned "safe" whenever ai_vs_ai was set, but W&L
            // always sets ai_vs_ai (29789), so this check never ran in W&L.
            if (!DLC_WL.dlc_scenarioactive) return true;
            if (group.dlcw_isundercommander) return false;
            if (group.allattachedunits == null) return true;

            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                Regiment unit = group.allattachedunits[i];
                if ((Object)unit != null && unit.dlcw_isundercommander) return false;
            }

            return true;
        }

        private static bool IsWlCurrentCommandOrChain(Regiment group)
        {
            if (!DLC_WL.dlc_scenarioactive) return false;
            if (DLC_WL.dlc_chosencommander < 0) return false;
            if (GameVars.commander == null || DLC_WL.dlc_chosencommander >= GameVars.commander.Count) return true;

            Regiment current = GameVars.commander[DLC_WL.dlc_chosencommander].currentcommand;
            if ((Object)current == null) return false;
            if ((Object)current == (Object)group) return true;

            Transform currentTransform = current.transform;
            Transform groupTransform = group.transform;
            if ((Object)currentTransform == null || (Object)groupTransform == null) return true;

            return currentTransform.IsChildOf(groupTransform) || groupTransform.IsChildOf(currentTransform);
        }
    }
}
