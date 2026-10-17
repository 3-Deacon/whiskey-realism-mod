using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // Garrison posts beyond the capital (plan G8, spec component 9). Vanilla defends only the capital proactively
    // (AssignUnitToDefendCapital 11668-11869); job 29 (CheckOccupationOfLostCities 17392-17485) only retakes a lost town, and no AICampaign
    // code mans a fort or town. So key towns, hubs, ports, depots and forts away from the capital are held by whatever happens to stand there.
    // This is the pure half: which places want a garrison (PlanPosts) and which formation holds one (PickFormation). ForceAllocator seats
    // the picks under the same FormationRole.Garrison that D8's secession garrison uses; GarrisonLedger remembers who holds what.
    //
    // A post is a place of the side's own: an own objective town, rail hub or port (an ObjectiveRecord the side holds), an own built supply
    // depot or an own fort. The capital is excluded (the capital floor owns it), and so is any site D8 already posts (Louisville is an own
    // objective town and a secession post: one post, not two). A post stands only where an army is believed to threaten it: its need is
    // ThreatShare x vanilla's own threat weighting of the beliefs near it (ReserveRules.VanillaThreat, the capital formula) less what is
    // already there (the fort's garrison dummy and the side's own AI formations at the post, the mass and the post's own incumbent
    // excluded). Beliefs only: the planner never reads vanilla's enemy lists.
    //
    // The plan's "lies in a Hold, Screen or Support sector or within relocation range of a belief" reduces to the second clause: the need
    // is beliefs only (NeedFromBeliefsOnly), so a sector's posture adds nothing a belief does not, and the sector clause is not coded.
    public static class GarrisonPlanner
    {
        public const float ThreatShare = 0.5f;          // posted strength >= ThreatShare x post threat - existing strength
        public const int MaxPostsPerAlliance = 6;       // ordinary posts (D8's secession posts are bounded by the two states)
        public const int ReleaseQuietDays = 14;         // a manned post is released after this many review days with no belief in range
        // Two sites closer than this are one post: a garrison holds from within bugle range, so a fort beside its town guards both.
        public const float SiteRadius = 25f;

        // Appends the ordinary posts of this review to `into`: sorted manned posts first, then largest need first, at most MaxPostsPerAlliance.
        // `retained` is the ledger's records; a record whose formation is still on the map keeps its post (and names it as the incumbent)
        // until the post has been quiet for ReleaseQuietDays.
        public static void PlanPosts(CampaignSnapshot s, TheaterPlan plan, IReadOnlyList<GarrisonPostRecord> retained, List<GarrisonPost> into)
        {
            if (s == null || plan == null || into == null || s.Alliance < 0 || s.Alliance > 1) return;
            var sites = new List<Site>();
            CollectSites(s, plan, sites);
            var posts = new List<GarrisonPost>();
            for (int k = 0; k < sites.Count; k++)
            {
                var post = Plan(s, plan, sites[k], retained);
                if (post != null) posts.Add(post);
            }
            posts.Sort(CompareForCap);
            for (int k = 0; k < posts.Count && k < MaxPostsPerAlliance; k++) into.Add(posts[k]);
        }

        // The formation (index into s.Own) that holds the post, -1 for none. `pool` marks the formations the allocator has left free for it
        // (never the mass, the capital guard, a recon picket, a recovering or a busy formation, or a formation another post holds). The
        // incumbent keeps the post while it is still fit and free; otherwise, when the post has a need, the smallest formation that covers it,
        // nearest first, within the relocation range of the post. A need nobody covers is left unmanned: a partial garrison is not one.
        public static int PickFormation(CampaignSnapshot s, GarrisonPost post, bool[] pool)
        {
            if (s == null || post == null || pool == null || pool.Length < s.Own.Count) return -1;
            if (post.InstanceIds.Count > 0)
            {
                int incumbent = s.IndexOfOwn(post.InstanceIds[0]);
                if (incumbent >= 0 && pool[incumbent] && Seatable(s, s.Own[incumbent])) return incumbent;
            }
            if (post.Need <= 0f) return -1;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (!pool[i] || !Seatable(s, f) || f.Strength < post.Need) continue;
                float d = RiverLines.Distance(f.X, f.Z, post.X, post.Z);
                if (d > s.RelocationRange) continue;
                if (best >= 0)
                {
                    var b = s.Own[best];
                    if (f.Strength > b.Strength) continue;
                    if (f.Strength == b.Strength && (d > bestDistance || (d == bestDistance && f.InstanceId > b.InstanceId))) continue;
                }
                best = i;
                bestDistance = d;
            }
            return best;
        }

        // Whether the formation stands at the post: within its bugle range (never less than the mover's arrival radius), the distance it
        // holds a place from. Shared by the existing-strength count and by the allocator's hold-in-place test.
        public static bool AtPost(in FormationRecord f, float x, float z)
        {
            return RiverLines.Distance(f.X, f.Z, x, z) <= Math.Max(f.BugleRange, CampaignMover.ArrivalRadius);
        }

        private static bool Seatable(CampaignSnapshot s, in FormationRecord f)
        {
            return f.UnitTyp > 13 && TheaterPlanner.MassEligible(f, s.MinMoraleForMovement);
        }

        // ---- sites ----

        private struct Site
        {
            public GarrisonPostKind Kind;
            public float X, Z;
            public float Garrison;   // the fort garrison dummy at the place
        }

        // The preference between two kinds at one place: the more specific wins the name (a port that is also a town is a port).
        private static int Rank(GarrisonPostKind kind)
        {
            switch (kind)
            {
                case GarrisonPostKind.Port: return 0;
                case GarrisonPostKind.RailHub: return 1;
                case GarrisonPostKind.ObjectiveTown: return 2;
                case GarrisonPostKind.Fort: return 3;
                default: return 4;
            }
        }

        private static void CollectSites(CampaignSnapshot s, TheaterPlan plan, List<Site> sites)
        {
            for (int i = 0; i < s.Objectives.Count; i++)
            {
                var o = s.Objectives[i];
                if (o.HolderAlliance != s.Alliance || (o.Roles & ObjectiveRole.Capital) != 0) continue;
                var kind = (o.Roles & ObjectiveRole.Port) != 0 ? GarrisonPostKind.Port
                    : (o.Roles & ObjectiveRole.RailHub) != 0 ? GarrisonPostKind.RailHub : GarrisonPostKind.ObjectiveTown;
                Add(s, plan, sites, kind, o.X, o.Z, o.GarrisonStrength);
            }
            for (int i = 0; i < s.Forts.Count; i++)
                Add(s, plan, sites, GarrisonPostKind.Fort, s.Forts[i].X, s.Forts[i].Z, s.Forts[i].GarrisonStrength);
            for (int i = 0; i < s.Supply.Count; i++)
            {
                var d = s.Supply[i];
                if (d.Kind == SupplySourceKind.Depot && d.Owner == s.Alliance && d.Built && !d.Invisible)
                    Add(s, plan, sites, GarrisonPostKind.Depot, d.X, d.Z, 0f);
            }
        }

        private static void Add(CampaignSnapshot s, TheaterPlan plan, List<Site> sites, GarrisonPostKind kind, float x, float z, float garrison)
        {
            if (s.HasCapital && RiverLines.Distance(x, z, s.CapitalX, s.CapitalZ) <= SiteRadius) return;
            for (int g = 0; g < plan.Garrisons.Count; g++)
                if (RiverLines.Distance(x, z, plan.Garrisons[g].X, plan.Garrisons[g].Z) <= SiteRadius) return;
            for (int k = 0; k < sites.Count; k++)
            {
                if (RiverLines.Distance(x, z, sites[k].X, sites[k].Z) > SiteRadius) continue;
                var site = sites[k];
                site.Garrison = Math.Max(site.Garrison, garrison);
                if (Rank(kind) < Rank(site.Kind)) site.Kind = kind;
                sites[k] = site;
                return;
            }
            sites.Add(new Site { Kind = kind, X = x, Z = z, Garrison = garrison });
        }

        // ---- one post ----

        // The post at a site, null when the site wants none: no need and no manned post still inside its quiet fortnight.
        private static GarrisonPost Plan(CampaignSnapshot s, TheaterPlan plan, in Site site, IReadOnlyList<GarrisonPostRecord> retained)
        {
            float threat = ReserveRules.VanillaThreat(s, site.X, site.Z, s.RelocationRange, null);
            bool threatened = threat > 0f;

            int incumbent = -1, quietSince = -1;
            string incumbentKey = null;
            if (retained != null)
            {
                for (int r = 0; r < retained.Count; r++)
                {
                    if (RiverLines.Distance(retained[r].X, retained[r].Z, site.X, site.Z) > SiteRadius) continue;
                    incumbentKey = retained[r].AssignedUnitKey;
                    quietSince = retained[r].QuietSinceDay;
                    incumbent = s.IndexOfKeyOrName(incumbentKey);
                    if (incumbent >= 0 && !WlChainRules.MayMove(s.Own[incumbent].Chain)) incumbent = -1;
                    break;
                }
            }
            int incumbentId = incumbent >= 0 ? s.Own[incumbent].InstanceId : -1;

            float existing = site.Garrison;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (f.InstanceId == incumbentId || !WlChainRules.MayMove(f.Chain) || (f.Flags & FormationFlags.FightingForce) == 0) continue;
                if (plan.MassInstanceIds.Contains(f.InstanceId) || !AtPost(f, site.X, site.Z)) continue;
                existing += Math.Max(0f, f.Strength);
            }
            float need = Math.Max(0f, ThreatShare * threat - existing);

            int since = threatened ? -1 : (quietSince >= 0 && quietSince <= s.DaySerial ? quietSince : s.DaySerial);
            bool held = incumbentId >= 0 && (since < 0 || s.DaySerial - since < ReleaseQuietDays);
            if (need <= 0f && !held) return null;

            var post = new GarrisonPost
            {
                Kind = site.Kind, X = site.X, Z = site.Z, Need = need, ExistingStrength = existing, QuietSinceDay = since
            };
            if (incumbentId >= 0) post.InstanceIds.Add(incumbentId);
            return post;
        }

        // Manned posts first, then the larger need, then the more specific kind, then the place: a total order, so the cap is deterministic.
        private static int CompareForCap(GarrisonPost a, GarrisonPost b)
        {
            int c = (b.InstanceIds.Count > 0).CompareTo(a.InstanceIds.Count > 0);
            if (c != 0) return c;
            c = b.Need.CompareTo(a.Need);
            if (c != 0) return c;
            c = Rank(a.Kind).CompareTo(Rank(b.Kind));
            if (c != 0) return c;
            c = a.X.CompareTo(b.X);
            return c != 0 ? c : a.Z.CompareTo(b.Z);
        }
    }
}
