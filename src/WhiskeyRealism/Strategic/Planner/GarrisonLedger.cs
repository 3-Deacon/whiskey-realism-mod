using System;
using System.Collections.Generic;
using System.Globalization;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // One garrison post as the sidecar keeps it (plan G8; sidecar v3, PlannerStateDto.garrisonPosts): where it is, who holds it, and from
    // which review day nothing has threatened it. The holder is a unit key (name:commander), never a Unity instance id; the first review
    // after a load finds the formation by it (GarrisonPlanner.PlanPosts, CampaignSnapshot.IndexOfKeyOrName).
    public struct GarrisonPostRecord
    {
        public GarrisonPostKind Kind;
        public float X, Z;
        public string AssignedUnitKey;   // null: the post stood but no formation held it
        public int QuietSinceDay;        // -1 while a live land belief lies within the relocation range of the post
    }

    public enum GarrisonPostState { Moving = 0, Holding = 1, Uncovered = 2, Released = 3 }

    // One change of a post's state, for the [GarrisonPost] row.
    public struct GarrisonTransition
    {
        public string Name;
        public GarrisonPostKind Kind;
        public string Unit;              // the holder's name, "none" without one
        public GarrisonPostState State;
        public float Need;
        public float Existing;           // what already stood there (the need is net of it)
        public float Short;              // the part of the need the seated formations do not meet
    }

    public struct GarrisonSyncResult
    {
        public int Posts;                // every post of the plan, D8's secession posts and the ordinary ones
        public int Manned;               // posts a formation is seated at
        public int Released;             // posts of the ledger the plan no longer names
        public int Uncovered;            // ordinary posts with a need and no formation seated
        public int Short;                // ordinary posts manned by formations that together do not meet the need
    }

    // The one ledger of garrison posts, D8's secession posts and G8's ordinary ones alike (one per alliance, GarrisonLedgers). Written on the
    // daily review only (Sync, after the allocation); the plan's posts are recomputed each day from the snapshot, so what the ledger keeps is
    // what cannot be: who holds a post (so the same formation keeps it), since when it has been quiet (so it is released after a fortnight),
    // and the last state of each post (so a row is written when it changes, not every day).
    public sealed class GarrisonLedger
    {
        private struct Entry
        {
            public GarrisonPostRecord Record;
            public string Name;
            public string Unit;
            public GarrisonPostState? State;   // null until the first Sync after a load: every post then announces itself
        }

        private List<Entry> _entries = new List<Entry>();
        private readonly List<GarrisonTransition> _transitions = new List<GarrisonTransition>();

        public IReadOnlyList<GarrisonPostRecord> Posts
        {
            get
            {
                var records = new List<GarrisonPostRecord>(_entries.Count);
                for (int i = 0; i < _entries.Count; i++) records.Add(_entries[i].Record);
                return records;
            }
        }

        // The transitions of the last Sync, in post order, the released ones last.
        public IReadOnlyList<GarrisonTransition> Transitions => _transitions;

        public void Restore(IEnumerable<GarrisonPostRecord> posts)
        {
            _entries = new List<Entry>();
            _transitions.Clear();
            if (posts == null) return;
            foreach (var record in posts)
                _entries.Add(new Entry { Record = record, Name = NameOf(record.Kind, record.X, record.Z, null), Unit = record.AssignedUnitKey != null ? EnemyArmyModel.NameOf(record.AssignedUnitKey) : "none" });
        }

        // Records the review's posts and who holds them (the allocation's seats), and what changed since the last review.
        public GarrisonSyncResult Sync(IReadOnlyList<GarrisonPost> posts, Allocation allocation, CampaignSnapshot s)
        {
            _transitions.Clear();
            var result = new GarrisonSyncResult();
            var next = new List<Entry>();
            var matched = new bool[_entries.Count];
            for (int g = 0; g < posts.Count; g++)
            {
                var post = posts[g];
                string name = NameOf(post);
                GarrisonPostState state = GarrisonPostState.Uncovered;
                string unit = "none", key = null;
                float seated = 0f;
                int members = 0;
                bool allThere = true;
                for (int k = 0; k < allocation.Seats.Count; k++)
                {
                    if (allocation.Seats[k].PostIndex != g) continue;
                    int i = s.IndexOfOwn(allocation.Seats[k].InstanceId);
                    if (i >= 0)
                    {
                        unit = members == 0 ? s.Own[i].Name : unit + "+" + s.Own[i].Name;
                        if (key == null) key = s.Own[i].UnitKey;
                        seated += Math.Max(0f, s.Own[i].Strength);
                    }
                    members++;
                    allThere &= allocation.Seats[k].AtPost;
                }
                if (members > 0) state = allThere ? GarrisonPostState.Holding : GarrisonPostState.Moving;
                bool ordinary = post.Kind != GarrisonPostKind.Secession;
                float shortfall = ordinary && members > 0 ? Math.Max(0f, post.Need - seated) : 0f;
                result.Posts++;
                if (members > 0) result.Manned++;
                else if (ordinary && post.Need > 0f) result.Uncovered++;
                if (shortfall > 0f) result.Short++;

                int previous = Find(post.Kind, post.X, post.Z, matched);
                if (previous >= 0) matched[previous] = true;
                if (previous < 0 || _entries[previous].State != state || !string.Equals(_entries[previous].Unit, unit, StringComparison.Ordinal))
                    _transitions.Add(new GarrisonTransition { Name = name, Kind = post.Kind, Unit = unit, State = state, Need = post.Need, Existing = post.ExistingStrength, Short = shortfall });
                next.Add(new Entry
                {
                    Record = new GarrisonPostRecord { Kind = post.Kind, X = post.X, Z = post.Z, AssignedUnitKey = key, QuietSinceDay = post.QuietSinceDay },
                    Name = name, Unit = unit, State = state
                });
            }
            for (int e = 0; e < _entries.Count; e++)
            {
                if (matched[e]) continue;
                result.Released++;
                _transitions.Add(new GarrisonTransition { Name = _entries[e].Name, Kind = _entries[e].Record.Kind, Unit = _entries[e].Unit, State = GarrisonPostState.Released });
            }
            _entries = next;
            return result;
        }

        private int Find(GarrisonPostKind kind, float x, float z, bool[] taken)
        {
            for (int e = 0; e < _entries.Count; e++)
            {
                var r = _entries[e].Record;
                if (!taken[e] && r.Kind == kind && RiverLines.Distance(r.X, r.Z, x, z) <= GarrisonPlanner.SiteRadius) return e;
            }
            return -1;
        }

        // A secession post by its state, any other by its kind and place ("Port@700,0").
        public static string NameOf(GarrisonPost post)
        {
            return NameOf(post.Kind, post.X, post.Z, post.StateName);
        }

        private static string NameOf(GarrisonPostKind kind, float x, float z, string stateName)
        {
            if (kind == GarrisonPostKind.Secession && !string.IsNullOrEmpty(stateName)) return stateName;
            return kind + "@" + Math.Round(x).ToString("0", CultureInfo.InvariantCulture) + "," + Math.Round(z).ToString("0", CultureInfo.InvariantCulture);
        }
    }

    // The ledger of each AI alliance (0 and 1; Europe has none). Per campaign: reset with the rest of the planner state, restored from the sidecar.
    public static class GarrisonLedgers
    {
        private static readonly GarrisonLedger[] Ledgers = { new GarrisonLedger(), new GarrisonLedger() };

        static GarrisonLedgers()
        {
            CampaignScope.Register("campaign-planner-garrison-posts", Reset);
        }

        private static void Reset()
        {
            for (int a = 0; a < Ledgers.Length; a++) Ledgers[a] = new GarrisonLedger();
        }

        public static GarrisonLedger For(int alliance)
        {
            return alliance >= 0 && alliance < Ledgers.Length ? Ledgers[alliance] : null;
        }
    }

    // The two telemetry rows (plan G8): the summary on change of its counts, one row per post per transition.
    public static class GarrisonRows
    {
        public static string Summary(int alliance, GarrisonSyncResult r)
        {
            return "[Garrison] alliance=" + alliance + " posts=" + r.Posts + " manned=" + r.Manned + " released=" + r.Released + " uncovered=" + r.Uncovered + " short=" + r.Short;
        }

        public static string Transition(int alliance, GarrisonTransition t)
        {
            return "[GarrisonPost] alliance=" + alliance + " post=" + t.Name + " kind=" + t.Kind + " unit=" + t.Unit + " state=" + t.State
                + " need=" + t.Need.ToString("0", CultureInfo.InvariantCulture) + " existing=" + t.Existing.ToString("0", CultureInfo.InvariantCulture)
                + " short=" + t.Short.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
