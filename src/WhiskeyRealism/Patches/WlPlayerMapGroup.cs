using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // The W&L player's campaign-map group: BattleUnits.GetCampaignGroup of the
    // chosen commander's current command (DC:82991), the group vanilla itself
    // treats as the player's campaign unit (Camp.UpdateUnitRef DC:172032,
    // DLC_WL.IsPlayerPartOfUnit DC:47578, Messages.Add DC:196771-196774).
    // GetCampaignGroup walks the parents, so the result is cached per frame.
    // Shared by the W&L staff-scope patches; no [HarmonyPatch].
    internal static class WlPlayerMapGroup
    {
        private static int _frame = -1;
        private static Regiment _group;

        static WlPlayerMapGroup()
        {
            CampaignScope.Register("wl-player-map-group", () =>
            {
                _frame = -1;
                _group = null;
            });
        }

        internal static bool Is(Regiment group)
        {
            if ((Object)group == null) return false;
            int frame = Time.frameCount;
            if (frame != _frame)
            {
                _frame = frame;
                _group = null;
                int chosen = DLC_WL.dlc_chosencommander;
                if (GameVars.commander != null && chosen >= 0 && chosen < GameVars.commander.Count)
                {
                    Regiment command = GameVars.commander[chosen].currentcommand;
                    if ((Object)command != null)
                        _group = BattleUnits.GetCampaignGroup(command);
                }
            }
            return (Object)_group != null && ReferenceEquals(_group, group);
        }
    }
}
