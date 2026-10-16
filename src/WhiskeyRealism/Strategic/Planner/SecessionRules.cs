namespace WhiskeyRealism.Strategic.Planner
{
    // The states the CSA can win by secession, and vanilla's own rule for when the chance is live (smoke amendments 2026-10-16, D8).
    //   Objective 15 (state 14, Kentucky) and 16 (state 22, Missouri) are CSA objectives. CampaignObjective.IsDeactivated (178966-178973)
    //   switches each off while the state already belongs to the CSA (nation.alliance == 1) or the CSA's support there is below 0.45
    //   (nation.support[1] < 0.45). CheckIfAccomplished (179259-179288) accomplishes it when the CSA owns Louisville / St.Louis AND
    //   nation.alliancetroops[0] is false, i.e. no visible Union campaign group (unittyp 14-16, not a fleet, not a garrison) stands in the
    //   state (BattleUnits alliance data, 81654-81660); the state then flips to the CSA, CSA support jumps to secedecsasupport and Union
    //   national morale takes secedeunionmorale. So a Union formation standing in the state denies the secession by its mere presence.
    // Fog-honest: the state's owner and the CSA support are public map facts, the presence is the side's own.
    internal static class SecessionRules
    {
        public const float SupportFloor = 0.45f;   // IsDeactivated 178966-178973
        public const int CsaAlliance = 1;

        public struct Rule
        {
            public int ObjectiveId, StateId;
            public string StateName;
        }

        public static readonly Rule[] Rules =
        {
            new Rule { ObjectiveId = 15, StateId = 14, StateName = "Kentucky" },
            new Rule { ObjectiveId = 16, StateId = 22, StateName = "Missouri" }
        };

        // Vanilla's IsDeactivated is false: the state is not the CSA's yet and the CSA's support there has not fallen below the floor.
        public static bool IsLive(int stateAlliance, float csaSupport)
        {
            return stateAlliance != CsaAlliance && csaSupport >= SupportFloor;
        }

        public static string NameOf(int stateId)
        {
            for (int i = 0; i < Rules.Length; i++)
                if (Rules[i].StateId == stateId) return Rules[i].StateName;
            return "state" + stateId;
        }
    }
}
