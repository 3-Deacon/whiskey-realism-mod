using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The value the objective executor (CampaignSteeringRuntime) last wrote into
    // AIFaction.followedcampaignobjective, per alliance, -1 for none or after a release. SteeringRules.ObjectiveWrite
    // reads it to tell a value this mod wrote (released to -1 so vanilla re-picks, 17780-17786) from vanilla's own pick
    // (left alone). Persisted in the sidecar (FactionDto.modWrittenObjective) so a mod-written objective is still
    // recognised after a reload; a sidecar without the field restores -1. Per campaign: cleared by CampaignScope.
    internal static class ModWrittenObjective
    {
        private static readonly int[] Written = { -1, -1 };

        static ModWrittenObjective()
        {
            CampaignScope.Register("campaign-planner-mod-written-objective", Reset);
        }

        private static void Reset()
        {
            for (int a = 0; a < Written.Length; a++) Written[a] = -1;
        }

        // The executor records the value it wrote (-1 after a release).
        internal static void Note(int alliance, int objectiveId)
        {
            if (alliance >= 0 && alliance < Written.Length) Written[alliance] = objectiveId;
        }

        internal static int Get(int alliance)
        {
            return alliance >= 0 && alliance < Written.Length ? Written[alliance] : -1;
        }

        // The sidecar's values: null for an alliance that wrote none that is known to the file.
        internal static void Restore(int?[] persisted)
        {
            for (int a = 0; a < Written.Length; a++)
                Written[a] = persisted != null && a < persisted.Length && persisted[a].HasValue ? persisted[a].Value : -1;
        }
    }
}
