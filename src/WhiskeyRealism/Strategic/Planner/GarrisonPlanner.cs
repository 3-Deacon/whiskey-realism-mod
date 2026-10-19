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
        public const int MaxFormationsPerPost = 2;      // a need no single formation covers is met by the nearest two together, short or not
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
                var post = PlanOne(s, plan, sites[k], retained);
                if (post != null) posts.Add(post);
            }
            posts.Sort(CompareForCap);
            for (int k = 0; k < posts.Count && k < MaxPostsPerAlliance; k++) into.Add(posts[k]);
        }

        // The post's incumbents (indexes into s.Own, in the post's order): every member the ledger remembers that is still fit and free in `pool`.
        // The allocator claims them before the depot and recon steps, so neither can take a seated garrison; their hysteresis does not depend on
        // the post's need.
        public static void Incumbents(CampaignSnapshot s, GarrisonPost post, bool[] pool, List<int> into)
        {
            if (s == null || post == null || pool == null || into == null || pool.Length < s.Own.Count) return;
            for (int k = 0; k < post.InstanceIds.Count; k++)
            {
                int at = s.IndexOfOwn(post.InstanceIds[k]);
                if (at >= 0 && pool[at] && Seatable(s, s.Own[at])) into.Add(at);
            }
        }

        // Adds to `into` the formations (indexes into s.Own) that hold the rest of the post's need, `have` being the strength already seated
        // there (the incumbent's) and `slots` the formations still allowed (MaxFormationsPerPost less those seated). `pool` marks the
        // formations the allocator has left free for it (never the mass, the capital guard, a recon picket, a recovering or busy formation,
        // or a formation another post holds). The smallest single formation that covers the rest, nearest first, within the relocation range
        // of the post; when none does, the nearest eligible formations together until the need is met or the slots are used, as D8's
        // flag-alone posts do. What they still fall short of is the post's shortfall (GarrisonLedger.Sync). A post with no need asks nobody.
        public static void PickFormations(CampaignSnapshot s, GarrisonPost post, bool[] pool, float have, int slots, List<int> into)
        {
            if (s == null || post == null || pool == null || into == null || pool.Length < s.Own.Count || slots <= 0) return;
            float remaining = post.Need - have;
            if (remaining <= 0f) return;
            var near = new List<int>();
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (!pool[i] || !Seatable(s, f)) continue;
                float d = RiverLines.Distance(f.X, f.Z, post.X, post.Z);
                if (d > s.RelocationRange) continue;
                near.Add(i);
                if (f.Strength < remaining) continue;
                if (best >= 0)
                {
                    var b = s.Own[best];
                    if (f.Strength > b.Strength) continue;
                    if (f.Strength == b.Strength && (d > bestDistance || (d == bestDistance && f.InstanceId > b.InstanceId))) continue;
                }
                best = i;
                bestDistance = d;
            }
            if (best >= 0)
            {
                into.Add(best);
                return;
            }
            near.Sort((a, b) =>
            {
                int c = RiverLines.Distance(s.Own[a].X, s.Own[a].Z, post.X, post.Z).CompareTo(RiverLines.Distance(s.Own[b].X, s.Own[b].Z, post.X, post.Z));
                return c != 0 ? c : s.Own[a].InstanceId.CompareTo(s.Own[b].InstanceId);
            });
            float seated = 0f;
            for (int k = 0; k < near.Count && k < slots && seated < remaining; k++)
            {
                into.Add(near[k]);
                seated += Math.Max(0f, s.Own[near[k]].Strength);
            }
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
        private static GarrisonPost PlanOne(CampaignSnapshot s, TheaterPlan plan, in Site site, IReadOnlyList<GarrisonPostRecord> retained)
        {
            // No terrain probe: vanilla's own weighting (11735) has no river term, and a post's threat is the straight-line one it states. The capital
            // floor passes the review's probe, so a river detour discounts the capital's threat and not a post's: a post is cheaper to man than to miss.
            float threat = ReserveRules.VanillaThreat(s, site.X, site.Z, s.RelocationRange, null);
            bool threatened = threat > 0f;

            // The record's members that are still on the map and the planner may move: the post's incumbents, in seat order.
            var incumbents = new List<int>();
            int quietSince = -1;
            if (retained != null)
            {
                for (int r = 0; r < retained.Count; r++)
                {
                    if (RiverLines.Distance(retained[r].X, retained[r].Z, site.X, site.Z) > SiteRadius) continue;
                    quietSince = retained[r].QuietSinceDay;
                    RecordMembers(s, retained[r], incumbents);
                    break;
                }
            }

            float existing = site.Garrison;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (incumbents.Contains(i) || !WlChainRules.MayMove(f.Chain) || (f.Flags & FormationFlags.FightingForce) == 0) continue;
                if (ReserveRules.Recovers(f, s.MinMoraleForMovement)) continue;   // leaving for supply: no garrison (as the hold sums count it)
                if (plan.MassInstanceIds.Contains(f.InstanceId) || !AtPost(f, site.X, site.Z)) continue;
                existing += Math.Max(0f, f.Strength);
            }
            float need = Math.Max(0f, ThreatShare * threat - existing);

            int since = QuietSince(s, threatened, quietSince);
            bool held = incumbents.Count > 0 && IsHeld(s, since);
            if (need <= 0f && !held) return null;

            var post = new GarrisonPost
            {
                Kind = site.Kind, X = site.X, Z = site.Z, Need = need, ExistingStrength = existing, QuietSinceDay = since
            };
            for (int k = 0; k < incumbents.Count; k++) post.InstanceIds.Add(s.Own[incumbents[k]].InstanceId);
            return post;
        }

        // The record's members that are still on the map and the planner may move (at most MaxFormationsPerPost, in seat order), as indexes into
        // s.Own. The one reading of a ledger record, shared by the post's planning and by RetainedIncumbents.
        private static void RecordMembers(CampaignSnapshot s, in GarrisonPostRecord record, List<int> into)
        {
            var keys = record.AssignedUnitKeys;
            for (int k = 0; keys != null && k < keys.Length && into.Count < MaxFormationsPerPost; k++)
            {
                int at = s.IndexOfKeyOrName(keys[k]);
                if (at >= 0 && WlChainRules.MayMove(s.Own[at].Chain) && !into.Contains(at)) into.Add(at);
            }
        }

        // The day a post has been quiet since: -1 while a belief threatens it, else the recorded day (today when none is recorded or it lies ahead).
        private static int QuietSince(CampaignSnapshot s, bool threatened, int recorded)
        {
            return threatened ? -1 : (recorded >= 0 && recorded <= s.DaySerial ? recorded : s.DaySerial);
        }

        // A manned post is kept until it has been quiet ReleaseQuietDays.
        private static bool IsHeld(CampaignSnapshot s, int quietSince)
        {
            return quietSince < 0 || s.DaySerial - quietSince < ReleaseQuietDays;
        }

        // The instance ids of the formations the ledger's ordinary posts still hold as of this review (foresight F8, D11 requirement-claims): the
        // members of a record that is threatened or has not been quiet ReleaseQuietDays, which PlanPosts will name as the post's incumbents. PlanOne's
        // retention without the mass term, so the plan's department pick can leave them where they stand before the posts are planned (the posts
        // are planned after the plan). A D8 secession record is not read: its members are the plan's own pledges. flagOn is Enable Garrison
        // Manning: with it off no ordinary post enters the plan and the ledger releases its posts, so nothing is retained.
        public static void RetainedIncumbents(CampaignSnapshot s, IReadOnlyList<GarrisonPostRecord> retained, bool flagOn, List<int> into)
        {
            if (!flagOn || s == null || retained == null || into == null || s.Alliance < 0 || s.Alliance > 1) return;
            var members = new List<int>();
            for (int r = 0; r < retained.Count; r++)
            {
                if (retained[r].Kind == GarrisonPostKind.Secession) continue;
                members.Clear();
                RecordMembers(s, retained[r], members);
                if (members.Count == 0) continue;
                bool threatened = ReserveRules.VanillaThreat(s, retained[r].X, retained[r].Z, s.RelocationRange, null) > 0f;
                if (!IsHeld(s, QuietSince(s, threatened, retained[r].QuietSinceDay))) continue;
                for (int k = 0; k < members.Count; k++) into.Add(s.Own[members[k]].InstanceId);
            }
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
