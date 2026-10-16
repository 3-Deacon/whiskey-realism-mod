using WhiskeyRealism.Patches;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // AIFaction index -> alliance id for the planner's patches (plan G5a). The availability tests and the importance Postfix run
    // per unit or per AI area per pass; AICampaignReflect.GetAllianceId walks the faction list through reflection, so a valid
    // answer is cached for the campaign (the faction list is built once per campaign, and the index is not the alliance: the
    // faction index != alliance id rule, P1). An index with no faction is -1 and is not cached.
    internal static class PlannerFactionMap
    {
        private static readonly int[] AllianceByFaction = { -2, -2, -2, -2 };

        static PlannerFactionMap()
        {
            CampaignScope.Register("campaign-planner-faction-map", Reset);
        }

        private static void Reset()
        {
            for (int i = 0; i < AllianceByFaction.Length; i++) AllianceByFaction[i] = -2;
        }

        internal static int AllianceOf(int factionIndex)
        {
            if (factionIndex < 0 || factionIndex >= AllianceByFaction.Length) return AICampaignReflect.GetAllianceId(factionIndex);
            int alliance = AllianceByFaction[factionIndex];
            if (alliance != -2) return alliance;
            alliance = AICampaignReflect.GetAllianceId(factionIndex);
            if (alliance >= 0) AllianceByFaction[factionIndex] = alliance;
            return alliance;
        }
    }
}
