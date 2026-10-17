namespace WhiskeyRealism.Strategic.Planner
{
    // The states the CSA can win by secession, and vanilla's own rule for when the chance is live (smoke amendments 2026-10-16, D8).
    //   Objective 15 (state 14, Kentucky) and 16 (state 22, Missouri) are CSA objectives. CampaignObjective.IsDeactivated (178966-178973)
    //   switches each off while the state already belongs to the CSA (nation.alliance == 1) or the CSA's support there is below 0.45
    //   (nation.support[1] < 0.45). CheckIfAccomplished (179259-179288) accomplishes it when the CSA owns Louisville / St.Louis AND
    //   nation.alliancetroops[0] is false. That flag is set by the monthly alliance-data pass (81610 clears it, 81654-81660 sets it) for every
    //   Union unit with unittyp > 13 and not a fleet (17) whose map-symbol visibility is above 0 and whose point lies in the state:
    //   GetUnitVisibilityStateOnCampaignMap (82940-82943) returns 2 for a garrison unit with its garrisonreference, so a garrison alone sets
    //   it, as does a field army; "visible" is the symbol state, not campaign fog. The state then flips to the CSA, CSA support jumps to
    //   secedecsasupport and Union national morale takes secedeunionmorale. So any Union group in the state, garrisons included, denies the
    //   secession by its mere presence; the flag lags the groups by one alliance-data pass.
    // Fog-honest: the owner and the CSA support are public map facts. The Union reads the presence flag and its own formations' positions
    // (its own side's information); the CSA never reads the flag.
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
