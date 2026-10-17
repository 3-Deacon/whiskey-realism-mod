using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    public struct GarrisonPostRecord
    {
        public GarrisonPostKind Kind;
        public float X, Z;
        public string AssignedUnitKey;
        public int QuietSinceDay;
    }

    public enum GarrisonPostState { Moving = 0, Holding = 1, Uncovered = 2, Released = 3 }

    public struct GarrisonTransition
    {
        public string Name;
        public GarrisonPostKind Kind;
        public string Unit;
        public GarrisonPostState State;
        public float Need;
    }

    public struct GarrisonSyncResult
    {
        public int Posts, Manned, Released, Uncovered;
    }

    public sealed class GarrisonLedger
    {
        public IReadOnlyList<GarrisonPostRecord> Posts => new List<GarrisonPostRecord>();
        public IReadOnlyList<GarrisonTransition> Transitions => new List<GarrisonTransition>();

        public void Restore(IEnumerable<GarrisonPostRecord> posts)
        {
        }

        public GarrisonSyncResult Sync(IReadOnlyList<GarrisonPost> posts, Allocation allocation, CampaignSnapshot s)
        {
            return default(GarrisonSyncResult);
        }

        public static string NameOf(GarrisonPost post)
        {
            return "";
        }
    }

    public static class GarrisonLedgers
    {
        public static GarrisonLedger For(int alliance)
        {
            return null;
        }
    }
}

namespace WhiskeyRealism.Strategic.Planner
{
    public static class GarrisonRows
    {
        public static string Summary(int alliance, GarrisonSyncResult r) { return ""; }
        public static string Transition(int alliance, GarrisonTransition t) { return ""; }
    }
}
