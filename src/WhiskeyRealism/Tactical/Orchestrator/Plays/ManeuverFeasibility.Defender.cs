using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14 requirement-defender-exposure: a Defender attacks no one, so its candidates carry one term, the chance its flank is turned before its
    // reserves can answer. The evaluation is run from the enemy's side against the line: every uncovered link crossing on the Defender's flank is
    // timed (the earliest enemy body's march to it and on to the line's flank end) against the Defender's fastest reserve. The selected
    // candidate's counted crossings also leave as the threat list the allocator's crossing release reads.

    /// <summary>A Defender's enemy bodies and counted crossings, prepared once per evaluation and shared by every candidate.</summary>
    internal sealed class DefenderPicture
    {
        public const int Links = RiverRegions.MaxRoutedLinks;

        public int BodyCount;
        public readonly float[] BodyX = new float[ManeuverFeasibility.MaxEnemyBodies];
        public readonly float[] BodyZ = new float[ManeuverFeasibility.MaxEnemyBodies];
        public readonly float[] BodyWeight = new float[ManeuverFeasibility.MaxEnemyBodies];
        public readonly int[] BodyRegion = new int[ManeuverFeasibility.MaxEnemyBodies];
        /// <summary>Hours from each body to each routed link's crossing (infinity: unreachable), body-major.</summary>
        public readonly float[] BodyArrival = new float[ManeuverFeasibility.MaxEnemyBodies * Links];

        public int ThreatCount;
        public readonly CrossingThreat[] Threats = new CrossingThreat[ManeuverFeasibility.MaxThreatCrossings];
        /// <summary>The fastest deployed reserve's time to each threat (courier plus march); infinity with none. Candidate-independent: the layout never claims a deployed reserve.</summary>
        public readonly float[] DeployedShift = new float[ManeuverFeasibility.MaxThreatCrossings];
        public float PassiveFactor = 1f;
        /// <summary>The bodies come from perceived samples; false for the one body at the assumed enemy point (the penalty uses it, a crossing guard does not).</summary>
        public bool Perceived;

        // per candidate: the enemy's legs from each flank end back to every link, then the reserve rects' time to each threat
        public readonly float[] FlankLegs = new float[2 * Links];
        public readonly bool[] HasFlankEnd = new bool[2];
        public readonly float[] RectShift = new float[ManeuverFeasibility.MaxThreatCrossings];

        public readonly float[] OwnShiftByChild = new float[ManeuverFeasibility.MaxThreatCrossings * ManeuverFeasibility.MaxChildren];
        public int OwnShiftChildCount;

        public readonly SingleLinkClusters Clusters = new SingleLinkClusters();
        public float[] SampleX = new float[64], SampleZ = new float[64], SampleW = new float[64];
        public int[] Labels = new int[64];
        /// <summary>Every counted crossing before the earliest <see cref="ManeuverFeasibility.MaxThreatCrossings"/> are kept.</summary>
        public readonly CrossingThreat[] Staged = new CrossingThreat[Links];

        public void Reset()
        {
            BodyCount = 0;
            ThreatCount = 0;
            OwnShiftChildCount = 0;
            PassiveFactor = 1f;
        }
    }

    public static partial class ManeuverFeasibility
    {
        /// <summary>A reserve's travel time to a crossing is compared with the enemy's arrival plus this lead.</summary>
        public const float ThreatLeadHours = 0.25f;
        /// <summary>A bound guard stays while the enemy's arrival is within its own shift at release plus the lead plus this margin.</summary>
        public const float ThreatHoldMarginHours = 0.25f;
        /// <summary>A released reserve stands this far behind the crossing along the own front axis.</summary>
        public const float GuardStandOffMeters = 100f;
        public const int MaxThreatCrossings = 8, MaxEnemyBodies = 16;
        /// <summary>The weight of the turning term in a Defender's penalty.</summary>
        public const float TurnWeight = 0.6f;

        private static float DepthOf(in TacticalFrontAxis axis, float x, float z)
            => (x - axis.OriginX) * axis.ForwardX + (z - axis.OriginZ) * axis.ForwardZ;

        // requirement-shift-time's courier: processing plus route processing plus the ride from where the enemy commander's order starts.
        private static float EnemyCourierHours(FeasibilityJob job, float fromX, float fromZ, float toX, float toZ)
        {
            MarchRules rules = job.Input.Rules;
            if (!rules.OrderDelays) return 0f;
            float ride = LegSampler.Cost(job.Graph.Grid, fromX, fromZ, toX, toZ, false, 0f, 0f, 0, false, 0f, 0f, 0, out _);
            return rules.ProcessingHours + rules.RouteProcessingHours + (rules.CourierSpeedPerHour > 0f ? ride / rules.CourierSpeedPerHour : 0f);
        }

        // ---- the Defender's picture, once per evaluation ---------------------------------------------------------

        internal static void PrepareDefender(FeasibilityJob job)
        {
            DefenderPicture d = job.Defender;
            d.Reset();
            if (job.Input.Stance != BattleStance.Defender || !job.FrameValid || !MapKnown(job)) return;
            BuildEnemyBodies(job, d);

            CrossingGraphView graph = job.Graph;
            RiverRegions regions = job.Input.Regions;
            MarchRules rules = job.Input.Rules;
            TacticalFrontAxis axis = job.Input.Axis;
            TacticalIntentModel intent = job.Input.EnemyIntent;
            d.PassiveFactor = intent.PrimaryIntent == InferredIntent.Defend && intent.Confidence01 >= ArmyIntentInference.ConfidenceFloor ? PassiveShiftFactor : 1f;
            int routed = regions.RoutedCount;

            for (int b = 0; b < d.BodyCount; b++)
            {
                d.BodyRegion[b] = regions.RegionAt(d.BodyX[b], d.BodyZ[b]);
                bool ok = graph.ArrivalsFrom(d.BodyX[b], d.BodyZ[b], rules.InfantryBase, 0f, true);
                for (int l = 0; l < DefenderPicture.Links; l++)
                    d.BodyArrival[b * DefenderPicture.Links + l] = ok && l < routed ? graph.ArrivalAtLink(l) : float.PositiveInfinity;
            }

            int ownRegion = regions.RegionAt(job.ArmyX, job.ArmyZ);
            float centre = (axis.LateralMin + axis.LateralMax) * 0.5f;
            float enemyDepth = DepthOf(in axis, job.EnemyX, job.EnemyZ);
            int candidates = 0;
            for (int link = 0; link < routed; link++)
            {
                regions.LinkCrossing(link, out float lx, out float lz, out _, out _);
                // On the flank: the outer quarter of the own line or beyond it, between one frontage behind the centroid and the enemy anchor.
                float lateral = axis.LateralOf(lx, lz);
                float depth = DepthOf(in axis, lx, lz);
                if (!CountsAsFlankCrossing(lateral, depth, axis.LateralMin, axis.LateralMax, job.Frontage, enemyDepth)) continue;
                if (graph.IsOwnCovered(link)) continue;

                int bank = OwnBankOf(regions, link, ownRegion, lx, lz);
                float all = float.PositiveInfinity;
                int allBody = -1;
                for (int b = 0; b < d.BodyCount; b++)
                {
                    float arrival = d.BodyArrival[b * DefenderPicture.Links + link];
                    if (float.IsInfinity(arrival)) continue;
                    float hours = (EnemyCourierHours(job, d.BodyX[b], d.BodyZ[b], lx, lz) + arrival) * d.PassiveFactor;
                    if (hours < all) { all = hours; allBody = b; }
                }
                if (allBody < 0 || all >= job.Input.DaylightHours) continue;

                // The guard stands behind the crossing along the front axis, on own-bank land.
                float gx = lx - GuardStandOffMeters * axis.ForwardX, gz = lz - GuardStandOffMeters * axis.ForwardZ;
                if (bank >= 0 && regions.RegionAt(gx, gz) != bank && regions.TrySnapToRegion(gx, gz, bank, out float sx, out float sz)) { gx = sx; gz = sz; }
                d.Staged[candidates++] = new CrossingThreat
                {
                    Link = link, X = lx, Z = lz, Left = lateral < centre, Sector = axis.SectorOfLateral(lateral), OwnBankRegion = bank,
                    GuardX = gx, GuardZ = gz, GuardHeading = PostureSense.HeadingTo(gx, gz, lx, lz),
                    EnemyAtCrossing = all, EnemyAcross = bank >= 0 && d.BodyRegion[allBody] == bank,
                    EnemyAtFlank = all, CandidateOwnShift = float.PositiveInfinity,
                };
            }
            // Earliest arrival first (a stable insertion sort; the links are few), at most MaxThreatCrossings kept.
            for (int i = 1; i < candidates; i++)
            {
                CrossingThreat held = d.Staged[i];
                int at = i;
                while (at > 0 && d.Staged[at - 1].EnemyAtCrossing > held.EnemyAtCrossing) { d.Staged[at] = d.Staged[at - 1]; at--; }
                d.Staged[at] = held;
            }
            d.ThreatCount = Math.Min(candidates, MaxThreatCrossings);
            Array.Copy(d.Staged, d.Threats, d.ThreatCount);

            // The deployed reserves' time to each threat: the layout never claims them, so they are a reserve under every candidate.
            for (int t = 0; t < d.ThreatCount; t++) d.DeployedShift[t] = float.PositiveInfinity;
            for (int c = 0; c < job.ChildCount; c++)
            {
                ChildFact child = job.Children[c];
                if (!child.DeployedReserve || child.HasPlayerOwned || child.OwnIndex < 0) continue;
                if (!graph.ArrivalsFrom(child.X, child.Z, child.BaseSpeed, child.MeanFatigue, false)) continue;
                for (int t = 0; t < d.ThreatCount; t++)
                {
                    float hours = child.CourierHours + graph.ArrivalAtLink(d.Threats[t].Link);
                    if (hours < d.DeployedShift[t]) d.DeployedShift[t] = hours;
                }
            }
        }

        /// <summary>
        /// A crossing on the Defender's flank (requirement-defender-exposure): its lateral lies in the outer quarter of the own line or beyond it
        /// and its depth between one frontage behind the own centroid and the enemy anchor. A crossing in front of the line's middle half is a
        /// frontal one, which the line already faces; one far behind the line is not on the way to a flank.
        /// </summary>
        public static bool CountsAsFlankCrossing(float lateral, float depth, float lateralMin, float lateralMax, float frontage, float enemyDepth)
        {
            float width = lateralMax - lateralMin;
            bool flank = lateral < lateralMin + 0.25f * width || lateral > lateralMax - 0.25f * width;
            return flank && depth >= -frontage && depth <= enemyDepth;
        }

        // The own bank of a crossing: the linked region holding the own centroid, else the linked region whose nearest land is nearest to it.
        private static int OwnBankOf(RiverRegions regions, int link, int ownRegion, float lx, float lz)
        {
            if (ownRegion >= 0 && regions.LinkTouches(link, ownRegion)) return ownRegion;
            Span<int> linked = stackalloc int[RiverRegions.MaxRegionsPerLink];
            int n = regions.RegionsOfLink(link, linked);
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                if (!regions.TrySnapToRegion(lx, lz, linked[i], out float sx, out float sz)) continue;
                float distance = Distance(lx, lz, sx, sz);
                if (distance < bestDistance) { bestDistance = distance; best = linked[i]; }
            }
            return best;
        }

        // requirement-enemy-picture: the samples linked singly at 150 units; beyond MaxEnemyBodies the weakest merge into the nearest kept body.
        private static void BuildEnemyBodies(FeasibilityJob job, DefenderPicture d)
        {
            EnemyPicture enemy = job.Enemy;
            int count = enemy.Count;
            d.Perceived = enemy.HasSamples;
            if (!enemy.HasSamples)
            {
                // No samples: the enemy mass is BC10's assumed enemy point, and it is the one body.
                d.BodyX[0] = enemy.MassX;
                d.BodyZ[0] = enemy.MassZ;
                d.BodyWeight[0] = enemy.N;
                d.BodyCount = 1;
                return;
            }
            if (d.SampleX.Length < count)
            {
                int size = Math.Max(count, d.SampleX.Length * 2);
                d.SampleX = new float[size];
                d.SampleZ = new float[size];
                d.SampleW = new float[size];
                d.Labels = new int[size];
            }
            for (int i = 0; i < count; i++) enemy.Sample(i, out d.SampleX[i], out d.SampleZ[i], out d.SampleW[i]);
            int clusters = d.Clusters.Label(d.SampleX, d.SampleZ, count, OpeningDetector.ClusterLinkMeters, d.Labels);

            // Accumulate per cluster into the body arrays when they fit, else into a temporary larger set and merge down.
            float[] bx = clusters <= MaxEnemyBodies ? d.BodyX : new float[clusters];
            float[] bz = clusters <= MaxEnemyBodies ? d.BodyZ : new float[clusters];
            float[] bw = clusters <= MaxEnemyBodies ? d.BodyWeight : new float[clusters];
            for (int k = 0; k < clusters; k++) { bx[k] = 0f; bz[k] = 0f; bw[k] = 0f; }
            for (int i = 0; i < count; i++)
            {
                int k = d.Labels[i];
                bx[k] += d.SampleX[i] * d.SampleW[i];
                bz[k] += d.SampleZ[i] * d.SampleW[i];
                bw[k] += d.SampleW[i];
            }
            for (int k = 0; k < clusters; k++) { bx[k] /= bw[k]; bz[k] /= bw[k]; }
            int n = clusters;
            while (n > MaxEnemyBodies)
            {
                int weakest = 0;
                for (int k = 1; k < n; k++) if (bw[k] < bw[weakest]) weakest = k;
                int nearest = -1;
                float nearestDistance = float.PositiveInfinity;
                for (int k = 0; k < n; k++)
                {
                    if (k == weakest) continue;
                    float distance = Distance(bx[k], bz[k], bx[weakest], bz[weakest]);
                    if (distance < nearestDistance) { nearestDistance = distance; nearest = k; }
                }
                float total = bw[nearest] + bw[weakest];
                bx[nearest] = (bx[nearest] * bw[nearest] + bx[weakest] * bw[weakest]) / total;
                bz[nearest] = (bz[nearest] * bw[nearest] + bz[weakest] * bw[weakest]) / total;
                bw[nearest] = total;
                n--;
                bx[weakest] = bx[n]; bz[weakest] = bz[n]; bw[weakest] = bw[n];
            }
            if (!ReferenceEquals(bx, d.BodyX))
            {
                Array.Copy(bx, d.BodyX, n);
                Array.Copy(bz, d.BodyZ, n);
                Array.Copy(bw, d.BodyWeight, n);
            }
            d.BodyCount = n;
        }

        // ---- one candidate ---------------------------------------------------------------------------------------

        // The candidate's flank ends, the enemy's legs from them, and the side's reserves (deployed ones, the candidate's Reserve rects, else
        // the nearest line child): each counted crossing gets its enemyAtFlank and ownShift; the body's turn is the worst of them.
        internal static void DefenderTurn(FeasibilityJob job, CandidateScratch w)
        {
            DefenderPicture d = job.Defender;
            w.Turn = 0f;
            if (d.ThreatCount == 0) return;
            CrossingGraphView graph = job.Graph;
            MarchRules rules = job.Input.Rules;
            TacticalFrontAxis axis = job.Input.Axis;

            // The flank ends: the filled, non-Reserve rect anchor with the outermost lateral on each side; a tie goes to the smaller depth.
            Span<float> endX = stackalloc float[2];
            Span<float> endZ = stackalloc float[2];
            d.HasFlankEnd[0] = d.HasFlankEnd[1] = false;
            for (int side = 0; side < 2; side++)
            {
                float bestLateral = 0f, bestDepth = 0f;
                for (int m = 0; m < w.MemberCount; m++)
                {
                    if (w.Members[m].Kind == MemberOther) continue;
                    float lateral = axis.LateralOf(w.Members[m].TargetX, w.Members[m].TargetZ);
                    float depth = DepthOf(in axis, w.Members[m].TargetX, w.Members[m].TargetZ);
                    bool better = !d.HasFlankEnd[side]
                        || (side == 0 ? lateral < bestLateral - 0.5f : lateral > bestLateral + 0.5f)
                        || (Math.Abs(lateral - bestLateral) <= 0.5f && depth < bestDepth);
                    if (!better) continue;
                    d.HasFlankEnd[side] = true;
                    bestLateral = lateral;
                    bestDepth = depth;
                    endX[side] = w.Members[m].TargetX;
                    endZ[side] = w.Members[m].TargetZ;
                }
                for (int l = 0; l < DefenderPicture.Links; l++) d.FlankLegs[side * DefenderPicture.Links + l] = float.PositiveInfinity;
                if (d.HasFlankEnd[side] && graph.ArrivalsFrom(endX[side], endZ[side], rules.InfantryBase, 0f, true))
                    for (int l = 0; l < graph.Regions.RoutedCount && l < DefenderPicture.Links; l++)
                        d.FlankLegs[side * DefenderPicture.Links + l] = graph.ArrivalAtLink(l);
            }

            // The candidate's Reserve and ReserveLine rects are reserves too, each from its anchor (infantry, unfatigued) after its child's courier.
            for (int t = 0; t < d.ThreatCount; t++) d.RectShift[t] = float.PositiveInfinity;
            bool anySource = false;
            for (int t = 0; t < d.ThreatCount; t++) if (!float.IsInfinity(d.DeployedShift[t])) anySource = true;
            for (int m = 0; m < w.MemberCount; m++)
            {
                if (w.Members[m].Kind != MemberOther) continue;
                anySource = true;
                ChildFact child = job.Children[job.SlotOfChild(w.Members[m].ChildId)];
                if (!graph.ArrivalsFrom(w.Members[m].TargetX, w.Members[m].TargetZ, rules.InfantryBase, 0f, false)) continue;
                for (int t = 0; t < d.ThreatCount; t++)
                {
                    float hours = child.CourierHours + graph.ArrivalAtLink(d.Threats[t].Link);
                    if (hours < d.RectShift[t]) d.RectShift[t] = hours;
                }
            }
            // With no reserve source at all, a line child would have to leave its place: the nearest filled rect anchor answers.
            if (!anySource)
            {
                int nearest = -1;
                float nearestDistance = float.PositiveInfinity;
                for (int m = 0; m < w.MemberCount; m++)
                {
                    float distance = Distance(w.Members[m].TargetX, w.Members[m].TargetZ, d.Threats[0].X, d.Threats[0].Z);
                    if (distance < nearestDistance) { nearestDistance = distance; nearest = m; }
                }
                if (nearest >= 0)
                {
                    ChildFact child = job.Children[job.SlotOfChild(w.Members[nearest].ChildId)];
                    if (graph.ArrivalsFrom(w.Members[nearest].TargetX, w.Members[nearest].TargetZ, rules.InfantryBase, 0f, false))
                        for (int t = 0; t < d.ThreatCount; t++) d.RectShift[t] = child.CourierHours + graph.ArrivalAtLink(d.Threats[t].Link);
                }
            }

            float turn = 0f;
            for (int t = 0; t < d.ThreatCount; t++)
            {
                ref CrossingThreat threat = ref d.Threats[t];
                int side = threat.Left ? 0 : 1;
                float legs = d.HasFlankEnd[side] ? d.FlankLegs[side * DefenderPicture.Links + threat.Link] : 0f;
                threat.EnemyAtFlank = float.IsInfinity(legs) ? float.PositiveInfinity : threat.EnemyAtCrossing + legs * d.PassiveFactor;
                threat.CandidateOwnShift = Math.Min(d.DeployedShift[t], d.RectShift[t]);
                if (float.IsInfinity(threat.EnemyAtFlank)) continue;
                float lead = float.IsInfinity(threat.CandidateOwnShift) ? LateHours : (threat.CandidateOwnShift - threat.EnemyAtFlank) / LateHours;
                float term = lead < 0f ? 0f : (lead > 1f ? 1f : lead);
                if (float.IsInfinity(threat.CandidateOwnShift)) term = 1f;
                if (term > turn) turn = term;
            }
            w.Turn = turn;
            for (int b = 0; b < w.BodyCount; b++) w.Bodies[b].Turn = turn;
        }

        // The selected candidate's threats leave with the ledger: each direct child's time to each counted crossing (courier plus march from its
        // current position; NaN for a child holding player-owned strength) so the allocator only compares.
        internal static void CaptureThreats(FeasibilityJob job)
        {
            DefenderPicture d = job.Defender;
            CrossingGraphView graph = job.Graph;
            int children = job.ChildCount;
            d.OwnShiftChildCount = children;
            for (int c = 0; c < children; c++)
            {
                ChildFact child = job.Children[c];
                bool ok = !child.HasPlayerOwned && child.OwnIndex >= 0 && graph.ArrivalsFrom(child.X, child.Z, child.BaseSpeed, child.MeanFatigue, false);
                for (int t = 0; t < d.ThreatCount; t++)
                    d.OwnShiftByChild[t * children + c] = child.HasPlayerOwned || child.OwnIndex < 0 ? float.NaN
                        : (ok ? child.CourierHours + graph.ArrivalAtLink(d.Threats[t].Link) : float.PositiveInfinity);
            }
            // With no enemy body perceived there is nothing to guard against: an empty list ends any binding.
            job.Target.SetThreatsBack(d.Threats, d.Perceived ? d.ThreatCount : 0, d.OwnShiftByChild, children);
        }
    }
}
