using System;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Util
{
    // Scope B alignment (2026-05-22). Canonical helper for issuing
    // vanilla ai_stance writes. Replaces the duplicated
    //   try { if (group.ai_stance != X) bunits.ChangeStance(...); } catch { }
    // pattern that appeared in 5+ patch files.
    //
    // Why a shared helper:
    //   - Single point to enforce the "skip if already correct" optimization
    //     (avoids redundant W&L queue entries that ChangeStance internally
    //     creates when useorderdelays is on)
    //   - Single point for telemetry (once-log on transitions)
    //   - Single point to fix bugs (e.g., if vanilla's ChangeStance signature
    //     changes in a game update, one file edit instead of 8)
    //   - Predictable parameters: immediate=false (respects vanilla queue
    //     semantics), overwriteaigroups=false (don't clobber AI grouping)
    //
    // Returns true when a ChangeStance call actually fired (caller can use
    // for telemetry / write-confirmation). Returns false when:
    //   - group is null or its GameObject is null
    //   - group.ai_stance already matches target (no-op for efficiency)
    //   - any exception thrown during the write (fail-open — caller can
    //     proceed with their waypoint/formation regardless)
    public static class TacticalStanceWriter
    {
        // Write the target stance directly. Use when the caller knows the
        // exact vanilla stance value (0/1/2/3/4) it wants.
        public static bool EnsureStance(BattleUnits bunits, Regiment group, int targetStance, string contextTag = null)
        {
            try
            {
                if (bunits == null || group == null) return false;
                if (!WhiskeyRealism.Tactical.TacticalWriteGuard.CanWrite(group)) return false;
                if (targetStance < 0 || targetStance > 4) return false;
                if (group.ai_stance == targetStance) return false;
                var gameObject = ((Component)group).gameObject;
                if (gameObject == null) return false;
                bunits.ChangeStance(gameObject, targetStance, immediate: false, overwriteaigroups: false);
                if (!string.IsNullOrEmpty(contextTag))
                {
                    OnceLog.Info(
                        "tactical-stance-write:" + contextTag + ":" + targetStance,
                        "[TacticalStanceWrite] " + contextTag + " stance="
                        + targetStance + " (was " + group.ai_stance + ")");
                }
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-stance-write:exception",
                    "[TacticalStanceWrite] EnsureStance threw: "
                    + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        // Write the stance mapped from a CommandTaskType. Use when the caller
        // has a doctrine task and wants the canonical vanilla stance for it.
        // Returns false on tasks that map to StanceNone (no opinion).
        public static bool EnsureStanceForTask(BattleUnits bunits, Regiment group, CommandTaskType task, string contextTag = null)
        {
            int stance = TacticalCommandTaskToStanceMapper.Map(task);
            if (stance == TacticalCommandTaskToStanceMapper.StanceNone) return false;
            return EnsureStance(bunits, group, stance, contextTag);
        }
    }
}
