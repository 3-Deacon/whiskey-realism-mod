using System;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14 maneuver feasibility (docs/superpowers/specs/2026-10-15-battle-maneuver-feasibility-design.md): before the army picks a battle
    // plan every candidate play is timed over the map's rivers and crossings as the side perceives them, and the risk that a divided
    // army is beaten in detail is priced. Pure: no Unity or vanilla types. FeasibilityRuntime reads GamePrefs, the clock and NavMesh
    // into MarchRules, FeasibilityInput and IConfirmedLegs; this file turns them into verdicts and penalties.
    //
    // Units: world units ("meters" in Whiskey's naming), doctrine hours. A leg's cost is in speed-independent cost units
    // (units x terrain speed cost); a mover's hours are cost / (BasicUnitSpeed x its base speed) / its fatigue factor, as vanilla's
    // RegimentMove computes a march (decompile 133354, 133525, 133569, 133986).

    public enum FeasibilityVerdict : byte { Unknown = 0, Feasible = 1, Risky = 2, Impossible = 3 }

    /// <summary>
    /// The vanilla values the timing model reads, taken once per battle from GamePrefs by FeasibilityRuntime. No number of vanilla's own
    /// is typed anywhere else in the feasibility code (MARCH-RULES-PIN).
    /// </summary>
    public struct MarchRules
    {
        /// <summary>GamePrefs.basicunitspeed.</summary>
        public float BasicUnitSpeed;
        /// <summary>GamePrefs.unitbasicspeed: infantry (type 0, unmounted), mounted cavalry (type 1), limbered artillery (type 2).</summary>
        public float InfantryBase, CavalryMountedBase, ArtilleryLimberedBase;
        /// <summary>GamePrefs.horseartilleryspeedfactor.</summary>
        public float HorseArtilleryFactor;
        /// <summary>GamePrefs.fatiguefactorspeed.</summary>
        public float FatigueSpeedExponent;
        /// <summary>processingtimegroupstandard, processingtimegrouproute, buglestandardrecognitiontime (game hours), standardbuglerange (units).</summary>
        public float ProcessingHours, RouteProcessingHours, BugleHours, BugleRange;
        /// <summary>The courier's speed in cost units per hour: unitbasicspeed[5, 1] x runningspeedfact[5] x basicunitspeed.</summary>
        public float CourierSpeedPerHour;
        /// <summary>GameVars.useorderdelays: off makes every courier time 0, as vanilla's SetWaypoint then sends at once.</summary>
        public bool OrderDelays;
    }

    /// <summary>
    /// NavMesh results for the legs next to a route's end points, by (origin 50-unit bucket, target 50-unit bucket, crossing). The
    /// runtime answers from a cache filled one query per side per tick; <see cref="Request"/> queues a leg for confirmation.
    /// </summary>
    public interface IConfirmedLegs
    {
        /// <summary>True when the leg has a result. <paramref name="noPath"/>: the NavMesh found no complete path; <paramref name="cost"/> is then meaningless.</summary>
        bool TryGet(float x0, float z0, float x1, float z1, int crossing, out float cost, out bool noPath);

        void Request(float x0, float z0, float x1, float z1, int crossing);
    }

    /// <summary>What a route query found (CrossingGraphView.Route).</summary>
    internal struct RouteResult
    {
        /// <summary>A route exists. False with <see cref="Unknown"/> true: the map is not ready or an end point is off the land; false with <see cref="NoRoute"/>: no crossing joins the regions (or the NavMesh refused every one).</summary>
        public bool Found;
        public bool Unknown;
        public bool NoRoute;
        /// <summary>With <see cref="NoRoute"/>: a NavMesh result refused a leg next to an end point (no-route), as opposed to no chain of crossings at all (no-crossing).</summary>
        public bool Refused;
        public bool SameRegion;
        /// <summary>Some leg is a sampled estimate (it skirts a bend of the river).</summary>
        public bool Estimated;
        /// <summary>Some leg is a NavMesh result (<see cref="IConfirmedLegs"/>) in place of the sampled estimate.</summary>
        public bool Confirmed;
        /// <summary>Hours of the legs alone, at the mover's speed.</summary>
        public float LegHours;
        /// <summary>HeldCrossingHours for each crossing that costs the mover time (a Held one for the side, an own-covered one for the enemy).</summary>
        public float HeldHours;
        public float CostUnits;
        public int CrossingCount;
        public int HeldCount;
        /// <summary>The first crossing's link index, -1 when the route crosses nothing.</summary>
        public int FirstLink;
        /// <summary>The worst crossing-state value over the crossings passed (Held 1, Watched 0.5, Unknown 0.25, Clear 0).</summary>
        public float Contested;

        public float Hours => LegHours + HeldHours;
    }

    /// <summary>
    /// BC14 requirement-route-screen and requirement-link-legs: routes over the river regions' crossing graph. A route is the cheapest
    /// path, in the mover's hours, over the routed links (Dijkstra over at most MaxRoutedLinks nodes, pooled arrays); a Held crossing
    /// adds HeldCrossingHours to the edge through it (for the enemy: a crossing an own regiment covers). The legs at the two ends are
    /// priced by <see cref="LegSampler"/> (cached per 50-unit bucket) or taken from the NavMesh results of <see cref="Legs"/>; the legs
    /// between links come from the per-battle link table.
    /// </summary>
    internal sealed class CrossingGraphView
    {
        public RiverRegions Regions;
        public CrossingWatch Crossings;
        public BattleTerrainGrid Grid;
        public LegCostCache Cache;
        public IConfirmedLegs Legs;
        public MarchRules Rules;

        /// <summary>The side's own snapshot: an own regiment covering a crossing (CoveredCrossings.Covers, the own-side form) costs the enemy's shift HeldCrossingHours there.</summary>
        public BattleSnapshot OwnSnapshot;

        /// <summary>
        /// Per own record: left out of the cover test (a child bound to guard a crossing and its subtree, requirement-defender-exposure: the
        /// guard's own cover does not end the threat it was sent against). Null leaves every regiment in.
        /// </summary>
        public bool[] OwnExcluded;

        // Per link, read lazily: 0 not asked, 1 uncovered, 2 covered. Cleared by ResetOwnCovered when an evaluation starts.
        private readonly sbyte[] _ownCovered = new sbyte[RiverRegions.MaxCrossings];

        public void ResetOwnCovered() => Array.Clear(_ownCovered, 0, _ownCovered.Length);

        /// <summary>True when a live own regiment covers the link's crossing by the own-side form of rule (a).</summary>
        public bool IsOwnCovered(int link)
        {
            if (link < 0 || link >= _ownCovered.Length || OwnSnapshot == null || Regions == null) return false;
            if (_ownCovered[link] != 0) return _ownCovered[link] == 2;
            Regions.LinkCrossing(link, out float x, out float z, out _, out _);
            bool covered = false;
            UnitRecord[] own = OwnSnapshot.Own;
            for (int i = 0; i < OwnSnapshot.OwnCount && !covered; i++)
            {
                if (OwnExcluded != null && i < OwnExcluded.Length && OwnExcluded[i]) continue;
                covered = CoveredCrossings.Covers(in own[i], x, z);
            }
            _ownCovered[link] = (sbyte)(covered ? 2 : 1);
            return covered;
        }

        /// <summary>Legs sampled and path searches made (FEAS-TIME's counted bounds).</summary>
        public int LegsSampled, Searches;

        private const int MaxNodes = RiverRegions.MaxRoutedLinks;
        private readonly float[] _dist = new float[MaxNodes];
        private readonly float[] _entryCost = new float[MaxNodes];
        private readonly bool[] _entryEstimated = new bool[MaxNodes];
        private readonly int[] _prev = new int[MaxNodes];
        private readonly bool[] _done = new bool[MaxNodes];
        private bool _refused, _confirmed;

        /// <summary>The links of the last successful route, first crossing first; valid until the next <see cref="Route"/> call.</summary>
        public readonly int[] PathLinks = new int[MaxNodes];
        public int PathCount;
        /// <summary>The regions the last <see cref="Route"/> started and ended in (after snapping an end point to land); -1 when it did not place them.</summary>
        public int RouteSourceRegion, RouteTargetRegion;

        public bool Ready => Grid != null && Regions != null && Regions.Ready;

        public float SpeedDivisor(float baseSpeed, float meanFatigue)
            => Rules.BasicUnitSpeed * baseSpeed * ManeuverFeasibility.FatigueFactor(meanFatigue, in Rules);

        /// <summary>
        /// The region the last route continues in after its k-th crossing: the region that crossing shares with the next one (the end region
        /// after the last), other than <paramref name="near"/>, the region the route was in before it.
        /// </summary>
        public int FarRegionOf(int k, int near, int endRegion)
        {
            if (k < 0 || k >= PathCount) return -1;
            if (k == PathCount - 1) return endRegion;
            Span<int> here = stackalloc int[RiverRegions.MaxRegionsPerLink];
            Span<int> next = stackalloc int[RiverRegions.MaxRegionsPerLink];
            int nh = Regions.RegionsOfLink(PathLinks[k], here), nn = Regions.RegionsOfLink(PathLinks[k + 1], next);
            for (int i = 0; i < nh; i++)
            {
                if (here[i] == near) continue;
                for (int j = 0; j < nn; j++) if (here[i] == next[j]) return here[i];
            }
            return -1;
        }

        // Dijkstra over the routed links from a start point in region rs: the mover's hours to reach each link's crossing (_dist), a crossing
        // that costs time included once it is passed.
        private void Relax(float x0, float z0, int rs, float divisor, bool enemy)
        {
            int routed = Regions.RoutedCount;
            for (int i = 0; i < routed; i++)
            {
                _dist[i] = float.PositiveInfinity;
                _prev[i] = -1;
                _done[i] = false;
                if (!Regions.LinkTouches(i, rs)) continue;
                if (!LinkLeg(x0, z0, i, out float cost, out bool est)) continue;
                _dist[i] = cost / divisor + CrossingExtra(i, enemy);
                _entryCost[i] = cost;
                _entryEstimated[i] = est;
            }
            while (true)
            {
                int u = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < routed; i++)
                    if (!_done[i] && _dist[i] < best) { best = _dist[i]; u = i; }
                if (u < 0) break;
                _done[u] = true;
                for (int v = 0; v < routed; v++)
                {
                    if (_done[v]) continue;
                    float link = Regions.LinkLegCost(u, v);
                    if (float.IsNaN(link)) continue;
                    float through = _dist[u] + link / divisor + CrossingExtra(v, enemy);
                    if (through < _dist[v])
                    {
                        _dist[v] = through;
                        _prev[v] = u;
                    }
                }
            }
        }

        /// <summary>
        /// Runs the path search from a point over every routed link without a destination; <see cref="ArrivalAtLink"/> then reads the hours to
        /// reach each crossing. False when the map is not ready or the point lies on no land within the snap reach.
        /// </summary>
        public bool ArrivalsFrom(float x0, float z0, float baseSpeed, float meanFatigue, bool enemy)
        {
            PathCount = 0;
            _refused = false;
            _confirmed = false;
            if (!Ready) return false;
            Searches++;
            int rs = Regions.RegionAt(x0, z0);
            if (rs < 0 && Regions.TrySnapToLand(x0, z0, -1, out float sx, out float sz)) { x0 = sx; z0 = sz; rs = Regions.RegionAt(x0, z0); }
            float divisor = SpeedDivisor(baseSpeed, meanFatigue);
            if (rs < 0 || !(divisor > 0f)) return false;
            _arrivalEnemy = enemy;
            Relax(x0, z0, rs, divisor, enemy);
            return true;
        }

        private bool _arrivalEnemy;

        /// <summary>The hours from the last <see cref="ArrivalsFrom"/> start to the link's crossing point (the crossing itself not yet carried); infinity when unreachable.</summary>
        public float ArrivalAtLink(int link)
        {
            if (link < 0 || link >= Regions.RoutedCount) return float.PositiveInfinity;
            float arrival = _dist[link];
            return float.IsInfinity(arrival) ? arrival : arrival - CrossingExtra(link, _arrivalEnemy);
        }

        /// <summary>
        /// The cheapest route from one point to another for a mover with the given base speed and fatigue. <paramref name="enemy"/>
        /// prices crossings the way the enemy meets them (an own-covered crossing costs time); otherwise a Held one does.
        /// </summary>
        public RouteResult Route(float x0, float z0, float x1, float z1, float baseSpeed, float meanFatigue, bool enemy)
        {
            var result = new RouteResult { FirstLink = -1 };
            PathCount = 0;
            RouteSourceRegion = -1;
            RouteTargetRegion = -1;
            _refused = false;
            _confirmed = false;
            if (!Ready)
            {
                result.Unknown = true;
                return result;
            }
            Searches++;
            int rs = Regions.RegionAt(x0, z0), rt = Regions.RegionAt(x1, z1);
            if (rs < 0 && Regions.TrySnapToLand(x0, z0, rt, out float sx0, out float sz0)) { x0 = sx0; z0 = sz0; rs = Regions.RegionAt(x0, z0); }
            if (rt < 0 && Regions.TrySnapToLand(x1, z1, rs, out float sx1, out float sz1)) { x1 = sx1; z1 = sz1; rt = Regions.RegionAt(x1, z1); }
            if (rs < 0 || rt < 0)
            {
                result.Unknown = true;
                return result;
            }
            RouteSourceRegion = rs;
            RouteTargetRegion = rt;
            float divisor = SpeedDivisor(baseSpeed, meanFatigue);
            if (!(divisor > 0f))
            {
                result.Unknown = true;
                return result;
            }

            if (Regions.Frozen || rs == rt)
            {
                result.SameRegion = true;
                if (!PointLeg(x0, z0, x1, z1, out float cost, out bool est, out bool noPath))
                {
                    result.NoRoute = noPath;
                    result.Refused = noPath;
                    return result;
                }
                result.Found = true;
                result.Estimated = est;
                result.Confirmed = _confirmed;
                result.CostUnits = cost;
                result.LegHours = cost / divisor;
                return result;
            }

            int routed = Regions.RoutedCount;
            Relax(x0, z0, rs, divisor, enemy);

            int last = -1;
            float total = float.PositiveInfinity, exitCost = 0f;
            bool exitEstimated = false;
            for (int i = 0; i < routed; i++)
            {
                if (float.IsInfinity(_dist[i]) || !Regions.LinkTouches(i, rt)) continue;
                if (!LinkLeg(x1, z1, i, out float cost, out bool est)) continue;
                float through = _dist[i] + cost / divisor;
                if (through < total)
                {
                    total = through;
                    last = i;
                    exitCost = cost;
                    exitEstimated = est;
                }
            }
            if (last < 0)
            {
                result.NoRoute = true;
                result.Refused = _refused;
                return result;
            }

            // Reconstruct, first crossing first.
            int count = 0;
            for (int n = last; n >= 0 && count < MaxNodes; n = _prev[n]) PathLinks[count++] = n;
            Array.Reverse(PathLinks, 0, count);
            PathCount = count;

            float cost0 = _entryCost[PathLinks[0]] + exitCost;
            bool estimated = _entryEstimated[PathLinks[0]] || exitEstimated;
            for (int k = 1; k < count; k++)
            {
                cost0 += Regions.LinkLegCost(PathLinks[k - 1], PathLinks[k]);
                estimated |= Regions.LinkLegEstimated(PathLinks[k - 1], PathLinks[k]);
            }
            float contested = 0f;
            int held = 0;
            for (int k = 0; k < count; k++)
            {
                if (CrossingExtra(PathLinks[k], enemy) > 0f) held++;
                float v = StateValue(PathLinks[k]);
                if (v > contested) contested = v;
            }
            result.Found = true;
            result.Estimated = estimated;
            result.Confirmed = _confirmed;
            result.CostUnits = cost0;
            result.LegHours = cost0 / divisor;
            result.HeldCount = held;
            result.HeldHours = held * ManeuverFeasibility.HeldCrossingHours;
            result.CrossingCount = count;
            result.FirstLink = PathLinks[0];
            result.Contested = contested;
            return result;
        }

        /// <summary>The blood value of a crossing's state (requirement-held-crossing): Held 1, Watched 0.5, Unknown 0.25, Clear 0.</summary>
        public float StateValue(int link)
        {
            CrossingState state = Crossings != null ? Crossings.StateOf(link) : CrossingState.Unknown;
            switch (state)
            {
                case CrossingState.Held: return 1f;
                case CrossingState.Watched: return 0.5f;
                case CrossingState.Clear: return 0f;
                default: return 0.25f;
            }
        }

        // The time it costs to take a crossing: HeldCrossingHours when the crossing is Held (the side's own movers), or covered by an
        // own regiment (the enemy's).
        private float CrossingExtra(int link, bool enemy)
        {
            bool costs = enemy ? IsOwnCovered(link) : (Crossings != null && Crossings.StateOf(link) == CrossingState.Held);
            return costs ? ManeuverFeasibility.HeldCrossingHours : 0f;
        }

        // A leg between two points of one region: a NavMesh result, else the cached or sampled estimate.
        private bool PointLeg(float x0, float z0, float x1, float z1, out float cost, out bool estimated, out bool noPath)
        {
            noPath = false;
            if (Legs != null && Legs.TryGet(x0, z0, x1, z1, -1, out float confirmed, out bool refused))
            {
                if (refused)
                {
                    cost = 0f;
                    estimated = false;
                    noPath = true;
                    return false;
                }
                cost = confirmed;
                estimated = false;
                _confirmed = true;
                return true;
            }
            int a = LegCostCache.BucketKey(x0, z0), b = LegCostCache.BucketKey(x1, z1);
            if (a > b) { int t = a; a = b; b = t; }
            if (Cache != null && Cache.TryGet(a, b, out cost, out estimated)) return true;
            LegsSampled++;
            cost = LegSampler.Cost(Grid, x0, z0, x1, z1, false, 0f, 0f, 0, false, 0f, 0f, 0, out estimated);
            if (Cache != null) Cache.Put(a, b, cost, estimated);
            return true;
        }

        // A leg from a point to a link's crossing point, whose footprint is the crossing's own.
        private bool LinkLeg(float px, float pz, int link, out float cost, out bool estimated)
        {
            Regions.LinkCrossing(link, out float lx, out float lz, out int gridIndex, out int crossingId);
            if (Legs != null && Legs.TryGet(px, pz, lx, lz, gridIndex, out float confirmed, out bool refused))
            {
                cost = confirmed;
                estimated = false;
                if (refused) _refused = true;
                else _confirmed = true;
                return !refused;
            }
            int a = LegCostCache.BucketKey(px, pz), b = LegCostCache.LinkKey(link);
            if (Cache != null && Cache.TryGet(a, b, out cost, out estimated)) return true;
            LegsSampled++;
            cost = LegSampler.Cost(Grid, px, pz, lx, lz, false, 0f, 0f, 0, true, lx, lz, crossingId, out estimated);
            if (Cache != null) Cache.Put(a, b, cost, estimated);
            return true;
        }
    }

    public static partial class ManeuverFeasibility
    {
        /// <summary>The time it costs to carry a Held crossing, in game hours (Burnside's bridge, spec § 3).</summary>
        public const float HeldCrossingHours = 1.0f;

        /// <summary>
        /// A mover's hours for a leg of <paramref name="cost"/> cost units: cost / (BasicUnitSpeed x base) / fatigueFactor
        /// (requirement-march-time).
        /// </summary>
        public static float MarchHours(float cost, float baseSpeed, float meanFatigue, in MarchRules r)
        {
            float divisor = r.BasicUnitSpeed * baseSpeed * FatigueFactor(meanFatigue, in r);
            return divisor > 0f ? cost / divisor : 0f;
        }

        /// <summary>Max(1 - mean fatigue, 0.01) ^ fatiguefactorspeed: vanilla's fatigue term of RegimentMove (133569).</summary>
        public static float FatigueFactor(float meanFatigue, in MarchRules r)
        {
            float rested = 1f - meanFatigue;
            if (!(rested > 0.01f)) rested = 0.01f;
            return (float)Math.Pow(rested, r.FatigueSpeedExponent);
        }

        /// <summary>
        /// The hours of one courier hop (requirement-courier-time; WlCourierDelay.BattleHours' rule with a terrain-priced ride). Within
        /// bugle range the hop is a bugle: the recognition time, and the first hop at least the processing time. Beyond it, the first
        /// hop pays the processing time and a relay hop the route processing time, plus the ride: the leg's cost units over the
        /// courier's speed. Order delays off: 0.
        /// </summary>
        public static float CourierLegHours(in MarchRules r, bool firstHop, float straightMeters, float rideCostUnits)
        {
            if (!r.OrderDelays) return 0f;
            if (straightMeters < r.BugleRange) return firstHop ? Math.Max(r.ProcessingHours, r.BugleHours) : r.BugleHours;
            float ride = r.CourierSpeedPerHour > 0f ? rideCostUnits / r.CourierSpeedPerHour : 0f;
            return (firstHop ? r.ProcessingHours : r.RouteProcessingHours) + ride;
        }

        /// <summary>
        /// The common plan (requirement-strike-time). Member i strikes if ordered now after <paramref name="strikeHours"/>[i]; member
        /// kinds are <see cref="MemberEnvelops"/> (offset from the fix's contact), <see cref="MemberFix"/> (a fixing member of the main
        /// body) and <see cref="MemberOther"/> (a reserve member; it pins nothing and has no release). C* = max(the latest fixing T, the
        /// latest wing T - offset); a wing strikes at C* + its offset, a fixing member at C*; each is released at its strike minus its
        /// own T. A fixing member in contact has T 0, a release of 0 and no planned release (<paramref name="hasRelease"/> false).
        /// </summary>
        public static float PlanReleases(ReadOnlySpan<float> strikeHours, ReadOnlySpan<float> offsetHours, ReadOnlySpan<byte> kind,
            ReadOnlySpan<bool> inContact, Span<float> desiredStrike, Span<float> release, Span<bool> hasRelease)
        {
            int n = strikeHours.Length;
            // A main body already in contact (any fixing member in it) has T_main 0: it makes contact as soon as it likes, and C* is the
            // wings' own pace.
            bool mainInContact = false;
            for (int i = 0; i < n; i++) if (kind[i] == MemberFix && inContact[i]) mainInContact = true;
            float cStar = 0f;
            for (int i = 0; i < n; i++)
            {
                if (kind[i] == MemberFix && !mainInContact) cStar = Math.Max(cStar, strikeHours[i]);
                else if (kind[i] == MemberEnvelops) cStar = Math.Max(cStar, strikeHours[i] - offsetHours[i]);
            }
            for (int i = 0; i < n; i++)
            {
                hasRelease[i] = false;
                release[i] = 0f;
                desiredStrike[i] = cStar;
                if (kind[i] == MemberOther) continue;
                if (kind[i] == MemberEnvelops)
                {
                    desiredStrike[i] = cStar + offsetHours[i];
                    release[i] = Math.Max(0f, desiredStrike[i] - strikeHours[i]);
                    hasRelease[i] = true;
                }
                else if (!inContact[i])
                {
                    release[i] = Math.Max(0f, cStar - strikeHours[i]);
                    hasRelease[i] = true;
                }
            }
            return cStar;
        }

        public const byte MemberEnvelops = 0, MemberFix = 1, MemberOther = 2;

        /// <summary>A release as the absolute doctrine hour the ledger publishes (requirement-strike-time): the evaluation's hour plus R.</summary>
        public static double AbsoluteHours(double evaluatedHours, float releaseHours) => evaluatedHours + releaseHours;

        /// <summary>
        /// DeployHours: the order processing time plus the Turn phase BC11 runs before its Attack (EnvelopmentManeuver.TurnHoldHours).
        /// </summary>
        public static float DeployHours(in MarchRules r) => r.ProcessingHours + EnvelopmentManeuver.TurnHoldHours;

        /// <summary>
        /// T (requirement-strike-time): the hours until a body launches its attack from its anchor if ordered now: its courier hours
        /// (0 when engaged), its march leg with the Held crossings on it, DeployHours, and the inward surplus of an enveloping body.
        /// </summary>
        internal static float StrikeHours(float courierHours, in RouteResult march, float inwardSurplusHours, in MarchRules r)
            => courierHours + march.Hours + DeployHours(in r) + inwardSurplusHours;

        /// <summary>
        /// The inward surplus of an enveloping body: what the inward leg's crossing route costs over the straight line to the line end
        /// (the straight leg's open-river samples are bends at the swamp cost), never less than 0, plus HeldCrossingHours for each Held
        /// crossing on the route. A detour to a crossing delays the strike and the synchronization must see it.
        /// </summary>
        internal static float InwardSurplusHours(CrossingGraphView graph, float anchorX, float anchorZ, float endX, float endZ,
            float baseSpeed, float meanFatigue, out RouteResult route)
        {
            route = graph.Route(anchorX, anchorZ, endX, endZ, baseSpeed, meanFatigue, false);
            if (!route.Found) return 0f;
            float straightCost = LegSampler.Cost(graph.Grid, anchorX, anchorZ, endX, endZ, false, 0f, 0f, 0, false, 0f, 0f, 0, out _);
            float straightHours = straightCost / graph.SpeedDivisor(baseSpeed, meanFatigue);
            return Math.Max(0f, route.LegHours - straightHours) + route.HeldHours;
        }

        // ---- courier time (requirement-courier-time) -------------------------------------------------------------

        private const int MaxCourierDepth = 6;
        private static readonly int[][] CourierChildren = BuildCourierBuffers();

        private static int[][] BuildCourierBuffers()
        {
            var buffers = new int[MaxCourierDepth][];
            for (int i = 0; i < buffers.Length; i++) buffers[i] = new int[128];
            return buffers;
        }

        /// <summary>
        /// The hours an order from the army headquarters takes to reach a direct child and everything below it: the hop from the army
        /// record (the root of the child's parent chain) to the child's record, then the longest branch down the parent map to the
        /// child's brigades, because vanilla starts the group only when its last courier has delivered. The ride of a hop is the straight
        /// headquarters-to-headquarters leg priced on the grid.
        /// </summary>
        internal static float ChildCourierHours(BattleSnapshot s, int childIndex, BattleTerrainGrid grid, in MarchRules r)
        {
            if (!r.OrderDelays || s == null || childIndex < 0 || childIndex >= s.OwnCount) return 0f;
            int army = childIndex;
            for (int steps = 0; steps < s.OwnCount && s.Own[army].ParentIndex >= 0 && s.Own[army].ParentIndex < s.OwnCount; steps++)
                army = s.Own[army].ParentIndex;
            return Hop(s, army, childIndex, true, grid, in r) + LongestBranchBelow(s, childIndex, 0, grid, in r);
        }

        private static float LongestBranchBelow(BattleSnapshot s, int node, int depth, BattleTerrainGrid grid, in MarchRules r)
        {
            // A brigade's regiments are inside its bugle range of its headquarters and are not couriered to.
            if (depth >= MaxCourierDepth || s.Own[node].UnitTyp <= TacticalUnitType.BattleGroupBrigade) return 0f;
            int[] buffer = CourierChildren[depth];
            int count = s.DirectChildren(node, buffer);
            float longest = 0f;
            for (int i = 0; i < count; i++)
            {
                int child = buffer[i];
                if ((s.Own[child].Flags & UnitFlags.IsGroup) == 0) continue;
                float branch = Hop(s, node, child, false, grid, in r) + LongestBranchBelow(s, child, depth + 1, grid, in r);
                if (branch > longest) longest = branch;
            }
            return longest;
        }

        private static float Hop(BattleSnapshot s, int from, int to, bool firstHop, BattleTerrainGrid grid, in MarchRules r)
        {
            ref UnitRecord a = ref s.Own[from];
            ref UnitRecord b = ref s.Own[to];
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float straight = (float)Math.Sqrt(dx * dx + dz * dz);
            float ride = straight;
            if (grid != null && straight >= r.BugleRange)
                ride = LegSampler.Cost(grid, a.X, a.Z, b.X, b.Z, false, 0f, 0f, 0, false, 0f, 0f, 0, out _);
            return CourierLegHours(in r, firstHop, straight, ride);
        }
    }
}
