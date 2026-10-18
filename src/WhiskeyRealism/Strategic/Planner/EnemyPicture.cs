using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Strategic.Planner
{
    // What kind of mass a picture element is (D11 requirement-picture-partition, Elements): the live strength of a belief, the faded rest of its
    // last-seen strength (kept where it was last seen), the same two for a group seen afloat, and an anchor's share of the unaccounted men.
    internal enum PictureElementKind
    {
        Live = 0,
        Faded = 1,
        AfloatLive = 2,
        AfloatFaded = 3,
        Anchor = 4
    }

    // One mass at one point. AssignedSector and Weight are written by EnemyPicture.AssignSectors (-1 and 0 until then, and for an afloat kind).
    internal sealed class PictureElement
    {
        public PictureElementKind Kind;
        public float X, Z, Mass;
        public int AssignedSector = -1;
        public float Weight;
    }

    // A place the unaccounted men are presumed to be: the enemy capital, or an objective record the major enemy holds (D11). Share is its
    // mass: the unaccounted men spread over the undisplaced anchors by vanilla's importance (the capital at 1; decided 2026-10-18, a fixed
    // share of the capital is a tuning constant). Displaced: scouted (D1's rule).
    internal sealed class PictureAnchor
    {
        public float X, Z, Importance01, Share;
        public bool IsCapital, Displaced;
        public string Label;
        public int ObjectiveId = -1;
    }

    // The fog-honest picture of the enemy's whole army (foresight F3, D11 requirements enemy-total and picture-partition). Every man of the
    // enemy's public total (troopsfielded, which the strategy screen shows for both sides) is placed exactly once: believed (live strength of
    // a belief), faded (the rest of a belief's last-seen strength, where it was last seen), or unaccounted (total less every belief's
    // last-seen strength, spread over the anchors). The planner presumes the unaccounted men at the anchors that are not scouted, instead of
    // presuming a floor under every unscouted sector. Pure; built once a review by TheaterPlanner.Plan.
    // Limit (named, out of scope): EnemyBelief has no alliance field, so a belief about a European group at war with the side is subtracted
    // from the major enemy's total too.
    internal sealed class EnemyPicture
    {
        public float Total = float.NaN;                    // the enemy's troopsfielded, NaN when unknown
        public float Believed, Faded, Unaccounted, Unplaced;
        public float CapitalShareProp;                      // the fraction of the unaccounted men the capital anchor took
        public int DisplacedCount;
        public int Seen, Marks;                             // the review's sightings and applied marks (the [EnemyBeliefs] counts, foresight F4)
        public string EnemyCapitalName;
        public bool HasEnemyCapital;
        public float EnemyCapitalX, EnemyCapitalZ;
        // Estimate(enemy capital, RelocationRange): the believed army near it plus the men presumed there; set by TheaterPlanner.Plan.
        public float EnemyCapitalEstProp;
        public readonly List<PictureAnchor> Anchors = new List<PictureAnchor>();
        public readonly List<PictureElement> Elements = new List<PictureElement>();
        private IReadOnlyList<CrossingRecord> _crossings;

        // isScouted is TheaterPlanner.IsScouted (D1's rule): marks are excluded from it from foresight F4 on.
        internal static EnemyPicture Build(CampaignSnapshot s, Func<float, float, bool> isScouted)
        {
            var p = new EnemyPicture { _crossings = s.Crossings, Seen = s.DetectionsSeen, Marks = s.DetectionsMarked };
            int enemy = s.Alliance == 0 ? 1 : s.Alliance == 1 ? 0 : -1;
            bool known = enemy >= 0 && IsFinite(s.EnemyTroopsFielded) && s.EnemyTroopsFielded >= 0f;
            if (known) p.Total = s.EnemyTroopsFielded;

            float lastSeen = 0f;
            for (int i = 0; i < s.Beliefs.Count; i++)
            {
                var b = s.Beliefs[i];
                if (b.Confidence01 <= 0f) continue;   // expired: the model drops a belief after EnemyArmyModel.ExpireDays; its men return to the pool
                float strength = Math.Max(0f, b.Strength);
                float live = ReserveRules.Live(b);
                float faded = Math.Max(0f, strength - live);
                bool afloat = ReserveRules.Afloat(b);
                lastSeen += strength;
                p.Believed += live;
                p.Faded += faded;
                if (live > 0f) p.Elements.Add(new PictureElement { Kind = afloat ? PictureElementKind.AfloatLive : PictureElementKind.Live, X = b.X, Z = b.Z, Mass = live });
                if (faded > 0f) p.Elements.Add(new PictureElement { Kind = afloat ? PictureElementKind.AfloatFaded : PictureElementKind.Faded, X = b.X, Z = b.Z, Mass = faded });
            }

            if (enemy >= 0) p.PlaceAnchors(s, enemy, isScouted);
            float unaccounted = known ? Math.Max(0f, p.Total - lastSeen) : 0f;
            if (p.Anchors.Count == 0)
            {
                p.Unplaced = unaccounted;
                return p;
            }
            p.Unaccounted = unaccounted;
            var shares = new float[p.Anchors.Count];
            Distribute(p.Anchors, unaccounted, shares);
            for (int k = 0; k < shares.Length; k++) p.Anchors[k].Share = shares[k];
            for (int k = 0; k < p.Anchors.Count; k++)
            {
                var a = p.Anchors[k];
                if (a.IsCapital && unaccounted > 0f) p.CapitalShareProp = a.Share / unaccounted;
                if (a.Share > 0f) p.Elements.Add(new PictureElement { Kind = PictureElementKind.Anchor, X = a.X, Z = a.Z, Mass = a.Share });
            }
            return p;
        }

        // The enemy capital first (its importance is always 1), then every record the major enemy holds, not accomplished, in snapshot order; a
        // record within TheaterPlanner.SameTargetRadius of an earlier anchor is merged into it keeping the largest importance (the capital's
        // stays 1). A record Europe holds is none.
        private void PlaceAnchors(CampaignSnapshot s, int enemy, Func<float, float, bool> isScouted)
        {
            HasEnemyCapital = s.HasEnemyCapital;
            EnemyCapitalName = s.EnemyCapitalName;
            EnemyCapitalX = s.EnemyCapitalX;
            EnemyCapitalZ = s.EnemyCapitalZ;
            if (s.HasEnemyCapital)
                Anchors.Add(new PictureAnchor { X = s.EnemyCapitalX, Z = s.EnemyCapitalZ, Importance01 = 1f, IsCapital = true, Label = string.IsNullOrEmpty(s.EnemyCapitalName) ? "capital" : s.EnemyCapitalName });
            for (int i = 0; i < s.Objectives.Count; i++)
            {
                var o = s.Objectives[i];
                if (o.HolderAlliance != enemy || o.Accomplished) continue;
                PictureAnchor merged = null;
                for (int k = 0; k < Anchors.Count && merged == null; k++)
                    if (Math.Abs(Anchors[k].X - o.X) <= TheaterPlanner.SameTargetRadius && Math.Abs(Anchors[k].Z - o.Z) <= TheaterPlanner.SameTargetRadius) merged = Anchors[k];
                if (merged != null)
                {
                    if (!merged.IsCapital) merged.Importance01 = Math.Max(merged.Importance01, o.Importance01);
                    continue;
                }
                Anchors.Add(new PictureAnchor { X = o.X, Z = o.Z, Importance01 = o.Importance01, Label = "obj" + o.ObjectiveId.ToString(CultureInfo.InvariantCulture), ObjectiveId = o.ObjectiveId });
            }
            for (int k = 0; k < Anchors.Count; k++)
            {
                Anchors[k].Displaced = isScouted != null && isScouted(Anchors[k].X, Anchors[k].Z);
                if (Anchors[k].Displaced) DisplacedCount++;
            }
        }

        // Spreads u over the undisplaced anchors by importance (the capital at 1); with every anchor displaced the capital takes it (where
        // vanilla keeps its capital defenders), with no capital the objective anchors take it, displaced or not when every one is.
        private static void Distribute(List<PictureAnchor> anchors, float u, float[] into)
        {
            for (int k = 0; k < into.Length; k++) into[k] = 0f;
            if (u <= 0f || anchors.Count == 0) return;
            int capital = -1;
            var allObjectives = new List<int>();
            var undisplaced = new List<int>();
            for (int k = 0; k < anchors.Count; k++)
            {
                if (anchors[k].IsCapital) capital = k;
                else allObjectives.Add(k);
                if (!anchors[k].Displaced) undisplaced.Add(k);
            }
            var pool = undisplaced.Count > 0 ? undisplaced : capital >= 0 ? new List<int> { capital } : allObjectives;
            SpreadByImportance(anchors, pool, u, into);
        }

        private static void SpreadByImportance(List<PictureAnchor> anchors, List<int> pool, float u, float[] into)
        {
            float weights = 0f;
            for (int k = 0; k < pool.Count; k++) weights += Math.Max(0f, anchors[pool[k]].Importance01);
            for (int k = 0; k < pool.Count; k++)
                into[pool[k]] += weights > 0f ? u * Math.Max(0f, anchors[pool[k]].Importance01) / weights : u / pool.Count;
        }

        // The Faded and Anchor elements within straight distance r of (x, z), each weighted min(1, r / effective distance) exactly as
        // TheaterPlanner.LandThreatNear weights a belief (a river delays it, never removes it). Afloat kinds add no land mass.
        internal float Presumed(CampaignSnapshot s, float x, float z, float r, ITerrainLineProbe probe)
        {
            float total = 0f;
            for (int i = 0; i < Elements.Count; i++)
            {
                var e = Elements[i];
                if (e.Kind != PictureElementKind.Faded) continue;
                total += e.Mass * WeightWithin(e.X, e.Z, x, z, r, probe);
            }
            for (int i = 0; i < Anchors.Count; i++)
            {
                var a = Anchors[i];
                if (a.Share > 0f) total += a.Share * WeightWithin(a.X, a.Z, x, z, r, probe);
            }
            return total;
        }

        // min(1, r / effective distance) for a point within straight distance r of (x, z), else 0.
        internal float WeightWithin(float ex, float ez, float x, float z, float r, ITerrainLineProbe probe)
        {
            if (RiverLines.Distance(ex, ez, x, z) > r) return 0f;
            float effective = RiverLines.EffectiveDistance(ex, ez, x, z, probe, _crossings);
            return effective > r ? r / effective : 1f;
        }

        // The largest importance of an undisplaced anchor within r (straight): the floor's I, 0 when there is none.
        internal float UndisplacedImportance(float x, float z, float r)
        {
            float importance = 0f;
            for (int i = 0; i < Anchors.Count; i++)
            {
                var a = Anchors[i];
                if (!a.Displaced && RiverLines.Distance(a.X, a.Z, x, z) <= r) importance = Math.Max(importance, a.Importance01);
            }
            return importance;
        }

        // The undisplaced anchors holding men (a share above 0) within straight distance r: the trace's anchors=.
        internal int AnchorsNear(float x, float z, float r)
        {
            int count = 0;
            for (int i = 0; i < Anchors.Count; i++)
            {
                var a = Anchors[i];
                if (!a.Displaced && a.Share > 0f && RiverLines.Distance(a.X, a.Z, x, z) <= r) count++;
            }
            return count;
        }

        // Whether an anchor element (an anchor with a share above 0) lies within r (straight) of (x, z): a sector that has one faces enemy ground.
        internal bool AnchorElementWithin(float x, float z, float r)
        {
            for (int i = 0; i < Anchors.Count; i++)
                if (Anchors[i].Share > 0f && RiverLines.Distance(Anchors[i].X, Anchors[i].Z, x, z) <= r) return true;
            return false;
        }

        // How many elements of any kind lie within straight distance r of (x, z): the [CapitalThreat] row's elements=.
        internal int ElementsWithin(float x, float z, float r)
        {
            int count = 0;
            for (int i = 0; i < Elements.Count; i++)
                if (RiverLines.Distance(Elements[i].X, Elements[i].Z, x, z) < r) count++;
            return count;
        }

        // Assigns each land element (Live, Faded, Anchor) to the nearest facing sector by centroid among the sectors whose reach holds it
        // (straight centroid distance <= radius + relocationRange), the lower sector id on a tie, with the weight min(1, reach / effective
        // distance from that centroid) under this pass's probe, and writes the facts' Assigned: the Faded and Anchor mass assigned to each
        // sector. The presumed sum is conserved: an element inside some facing sector's reach lands in exactly one need. Called at the end of
        // the sector facts' first loop (Facing and Radius set), and re-run, overwriting, in every Evaluate pass.
        internal void AssignSectors(TheaterPlanner.SectorFacts facts, SectorModel sectors, float relocationRange, ITerrainLineProbe probe)
        {
            int n = sectors.Count;
            for (int k = 0; k < n; k++) facts.Assigned[k] = 0f;
            for (int i = 0; i < Elements.Count; i++)
            {
                var e = Elements[i];
                e.AssignedSector = -1;
                e.Weight = 0f;
                if (e.Kind == PictureElementKind.AfloatLive || e.Kind == PictureElementKind.AfloatFaded) continue;
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int k = 0; k < n; k++)
                {
                    if (!facts.Facing[k]) continue;
                    float d = RiverLines.Distance(facts.CX[k], facts.CZ[k], e.X, e.Z);
                    if (d > facts.Radius[k] + relocationRange || d >= bestDistance) continue;   // the lower id wins a tie: the scan is in id order
                    bestDistance = d;
                    best = k;
                }
                if (best < 0) continue;
                float reach = facts.Radius[best] + relocationRange;
                e.AssignedSector = best;
                e.Weight = WeightWithin(e.X, e.Z, facts.CX[best], facts.CZ[best], reach, probe);
                if (e.Kind != PictureElementKind.Live) facts.Assigned[best] += e.Mass * e.Weight;
            }
        }

        // ---- rows ----

        // "[EnemyPicture]" (State): the partition and its anchors, sums with one decimal, on change of signature (the runtime dates it).
        // seen counts the sightings and marks the applied spotting marks of the review (foresight F4).
        internal static string Row(EnemyPicture p, CampaignSnapshot s)
        {
            var inv = CultureInfo.InvariantCulture;
            return "[EnemyPicture] alliance=" + s.Alliance
                + " total=" + (float.IsNaN(p.Total) ? "unknown" : p.Total.ToString("0", inv))
                + " believed=" + p.Believed.ToString("0.0", inv) + " faded=" + p.Faded.ToString("0.0", inv)
                + " unaccounted=" + p.Unaccounted.ToString("0.0", inv) + " unplaced=" + p.Unplaced.ToString("0.0", inv)
                + " anchors=" + p.Anchors.Count + " displaced=" + p.DisplacedCount
                + " capitalShareProp=" + p.CapitalShareProp.ToString("0.00", inv)
                + " seen=" + p.Seen + " marks=" + p.Marks
                + " enemyCapital=" + (p.HasEnemyCapital ? TelemetryLegacyParser.Quote(p.EnemyCapitalName ?? "capital") : "none")
                + " enemyCapitalEstProp=" + p.EnemyCapitalEstProp.ToString("0", inv)
                + " top=" + TopAnchors(p);
        }

        // The three anchors holding the most men, "label:mass;..." quoted as one value ("-" with none).
        private static string TopAnchors(EnemyPicture p)
        {
            var order = new List<PictureAnchor>();
            for (int k = 0; k < p.Anchors.Count; k++)
                if (p.Anchors[k].Share > 0f) order.Add(p.Anchors[k]);
            order.Sort((a, b) => b.Share != a.Share ? b.Share.CompareTo(a.Share) : string.CompareOrdinal(a.Label, b.Label));
            var sb = new StringBuilder();
            for (int k = 0; k < order.Count && k < 3; k++)
            {
                if (k > 0) sb.Append(';');
                sb.Append(order[k].Label).Append(':').Append(order[k].Share.ToString("0.0", CultureInfo.InvariantCulture));
            }
            return TelemetryLegacyParser.Quote(sb.Length == 0 ? null : sb.ToString());
        }

        // "[CapitalThreat]" (State): the capital reach and what the threat weighs, from the plan's published numbers.
        internal static string CapitalRow(TheaterPlan plan, EnemyPicture p, CampaignSnapshot s)
        {
            var inv = CultureInfo.InvariantCulture;
            int elements = s.HasCapital ? p.ElementsWithin(s.CapitalX, s.CapitalZ, plan.CapitalReach) : 0;
            return "[CapitalThreat] alliance=" + plan.Alliance
                + " reach=" + plan.CapitalReach.ToString("0", inv) + " relief=" + plan.CapitalReliefDays.ToString("0.0", inv)
                + " corpsPerDay=" + plan.CapitalCorpsPerDay.ToString("0.0", inv) + " weighted=" + plan.CapitalWeighted.ToString("0", inv)
                + " floor=" + plan.CapitalFloorStrength.ToString("0", inv) + " threat=" + plan.CapitalThreat01.ToString("0.00", inv)
                + " elements=" + elements;
        }

        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }
}
