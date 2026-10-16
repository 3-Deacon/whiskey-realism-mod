using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14 requirement-enemy-picture, requirement-shift-time, requirement-detail-risk, requirement-route-exposure, requirement-approach and
    // requirement-daylight: the risk terms of a candidate's bodies. A divided army is beaten in detail when the enemy can bring its masses
    // to a body before the others can support it (Jomini, Art. XXI): the enemy's shift time is set against the window a body stands alone.

    /// <summary>
    /// The enemy as the side perceives it, built once per evaluation from the snapshot's observations only (requirement-enemy-picture):
    /// the samples BC13 counts, their perceived strength and morale, and the fog premium on the effective strength N.
    /// </summary>
    internal sealed class EnemyPicture
    {
        private float[] _x = new float[64];
        private float[] _z = new float[64];
        private float[] _w = new float[64];
        private int[] _type = new int[64];

        public int Count { get; private set; }

        /// <summary>The perceived strength: Visible in full, Remembered times its confidence.</summary>
        public float P { get; private set; }

        /// <summary>The strength-weighted mean of the samples' last seen morale, 1 with no samples.</summary>
        public float Morale { get; private set; }

        /// <summary>The effective strength: the larger of P and the ledger's estimate, times one plus the fog premium.</summary>
        public float N { get; private set; }

        /// <summary>The strength-weighted centroid of the samples, or the assumed enemy point with none.</summary>
        public float MassX { get; private set; }

        public float MassZ { get; private set; }

        public bool HasSamples => Count > 0;

        /// <summary>The i-th sample's position and weight (Visible strength in full, Remembered times its confidence).</summary>
        public void Sample(int i, out float x, out float z, out float weight)
        {
            x = _x[i];
            z = _z[i];
            weight = _w[i];
        }

        public void Build(BattleSnapshot s, float estimate, float confidence, float caution01, bool hasAssumed, float assumedX, float assumedZ,
            float fallbackX, float fallbackZ)
        {
            Count = 0;
            float sumW = 0f, sumX = 0f, sumZ = 0f, sumMorale = 0f;
            EnemyObservation[] obs = s != null ? s.Observations : null;
            int n = s != null ? s.ObservationCount : 0;
            for (int i = 0; i < n; i++)
            {
                if (!EnemySamples.Accepts(in obs[i])) continue;
                float w = obs[i].State == ObservationState.Visible ? obs[i].Strength : obs[i].Strength * obs[i].Confidence01;
                if (!(w > 0f)) continue;
                if (Count == _x.Length) Grow();
                _x[Count] = obs[i].X;
                _z[Count] = obs[i].Z;
                _w[Count] = w;
                _type[Count] = obs[i].UnitTyp;
                Count++;
                sumW += w;
                sumX += obs[i].X * w;
                sumZ += obs[i].Z * w;
                sumMorale += (obs[i].Morale > 0f ? obs[i].Morale : 1f) * w;
            }
            P = sumW;
            Morale = sumW > 0f ? sumMorale / sumW : 1f;
            N = Math.Max(P, estimate) * (1f + ManeuverFeasibility.FogPremium(confidence, caution01));
            if (Count > 0)
            {
                MassX = sumX / sumW;
                MassZ = sumZ / sumW;
            }
            else
            {
                MassX = hasAssumed ? assumedX : fallbackX;
                MassZ = hasAssumed ? assumedZ : fallbackZ;
            }
        }

        /// <summary>The sampled strength within <paramref name="radius"/> of a point.</summary>
        public float Near(float x, float z, float radius)
        {
            float sum = 0f, r2 = radius * radius;
            for (int i = 0; i < Count; i++)
            {
                float dx = _x[i] - x, dz = _z[i] - z;
                if (dx * dx + dz * dz <= r2) sum += _w[i];
            }
            return sum;
        }

        /// <summary>True when a sample's own reach (by unit type) covers the point: where that regiment would see a column.</summary>
        public bool Sees(float x, float z, float[] reachByType)
        {
            if (reachByType == null) return false;
            for (int i = 0; i < Count; i++)
            {
                int t = _type[i];
                float reach = t >= 0 && t < reachByType.Length ? reachByType[t] : 0f;
                float dx = _x[i] - x, dz = _z[i] - z;
                if (dx * dx + dz * dz <= reach * reach) return true;
            }
            return false;
        }

        private void Grow()
        {
            int size = _x.Length * 2;
            Array.Resize(ref _x, size);
            Array.Resize(ref _z, size);
            Array.Resize(ref _w, size);
            Array.Resize(ref _type, size);
        }
    }

    public static partial class ManeuverFeasibility
    {
        public const float DaylightMarginHours = 0.25f, DetailSlope = 0.25f, BaseDefenseRatio = 1.5f, PassiveShiftFactor = 1.5f,
            AnchoredEndPenalty = 0.5f;

        /// <summary>The radius around a strike point whose enemy opposes a body there (BC13's OddsRadiusMeters).</summary>
        internal const float OddsRadiusMeters = OpeningDetector.LocalEnemyValueRadiusMeters;

        private const float ExposureStepMeters = 20f;

        /// <summary>
        /// The fog premium on the perceived enemy: <c>0.5 (1 - confidence)(0.5 + caution01)</c>. A cautious commander assumes more of the
        /// unseen; Perryville is the weight (spec § 3).
        /// </summary>
        public static float FogPremium(float confidence, float caution01) => 0.5f * (1f - confidence) * (0.5f + caution01);

        /// <summary>
        /// The commander's bar the isolated odds are held to (requirement-detail-risk step 5): caution raises it, audacity and aggression lower it,
        /// clamped to 0.6 to 1.4.
        /// </summary>
        public static float Bar(in OpportunityPersonality p)
        {
            float bar = 1f + 0.3f * (p.Caution01 - 0.5f) - 0.3f * (p.Audacity01 - 0.5f) - 0.2f * (p.Agg01 - 0.5f);
            return bar < 0.6f ? 0.6f : (bar > 1.4f ? 1.4f : bar);
        }

        /// <summary>The detail penalty (step 6): <c>clamp01((bar - odds) / (bar x DetailSlope))</c>; odds a quarter below the bar give the full penalty.</summary>
        public static float DetailPenalty(float odds, float bar)
        {
            float v = (bar - odds) / (bar * DetailSlope);
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>
        /// The share of the exposure window the enemy's shift fills (step 3): 0 when its time reaches the window, 1 when the window is twice
        /// the time, linear between.
        /// </summary>
        public static float ShiftShare(float windowHours, float shiftHours)
        {
            float v = (windowHours - shiftHours) / Math.Max(shiftHours, 0.1f);
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        // ---- the evaluation's risk pass --------------------------------------------------------------------------

        // Called by EvaluateSide after the common plan: fills each body's risk terms. A Defender's candidates carry only the turning term (BC14's
        // Defender pass), so the attacker's terms are skipped for them.
        private static void ComputeRisk(FeasibilityJob job, PlayTemplate template, CandidateScratch w)
        {
            if (job.Input.Stance == BattleStance.Defender) return;
            bool dividing = false;
            for (int b = 0; b < w.BodyCount; b++) if (w.Bodies[b].Envelops) dividing = true;

            for (int b = 0; b < w.BodyCount; b++)
            {
                ref BodyEstimate body = ref w.Bodies[b];
                body.DefenseRatio = DefenseRatioAt(job, body.StrikeX, body.StrikeZ, body.TargetX, body.TargetZ);
                if (body.DesiredStrikeHours > job.Input.DaylightHours - DaylightMarginHours) body.Daylight = 1f;
                body.Approach = ApproachOf(job, w, b);
                if (body.Envelops) body.Exposure = ExposureOf(job, w, b);
            }
            if (!dividing) return;
            Span<float> strikeHours = stackalloc float[w.BodyCount];
            for (int b = 0; b < w.BodyCount; b++) strikeHours[b] = w.Bodies[b].DesiredStrikeHours;
            DetailRisk(new Span<BodyEstimate>(w.Bodies, 0, w.BodyCount), strikeHours, job, w, Bar(in job.Input.Army));
        }

        // 1.5 + TerrainStrength at a strike point on its approach axis (from the strike point back toward the body's anchor); 1.5 when the
        // picture is not ready.
        private static float DefenseRatioAt(FeasibilityJob job, float strikeX, float strikeZ, float anchorX, float anchorZ)
        {
            TerrainPicture picture = job.Input.Picture;
            if (picture == null || !picture.Ready) return BaseDefenseRatio;
            ApproachAxis(job, strikeX, strikeZ, anchorX, anchorZ, out float ax, out float az);
            return BaseDefenseRatio + picture.TerrainStrength(strikeX, strikeZ, ax, az);
        }

        // The direction the ground is crossed: from the strike point toward the rect anchor; the way back along the front axis when they coincide.
        private static void ApproachAxis(FeasibilityJob job, float strikeX, float strikeZ, float anchorX, float anchorZ, out float ax, out float az)
        {
            ax = anchorX - strikeX;
            az = anchorZ - strikeZ;
            float len = (float)Math.Sqrt(ax * ax + az * az);
            if (len > 1f) { ax /= len; az /= len; return; }
            ax = -job.Input.Axis.ForwardX;
            az = -job.Input.Axis.ForwardZ;
        }

        /// <summary>
        /// Steps 1 to 6 of requirement-detail-risk for every body: the pinned enemy, the exposure window, the enemy against the body, the
        /// isolated odds against <paramref name="bar"/> and the detail penalty. The strike hours are an input (one per body): Evaluate passes the
        /// synchronized S*, a harness can pass each body's own T to measure a plan without the common release.
        /// </summary>
        internal static void DetailRisk(Span<BodyEstimate> bodies, ReadOnlySpan<float> strikeHours, FeasibilityJob job, CandidateScratch members, float bar)
        {
            EnemyPicture enemy = job.Enemy;
            for (int j = 0; j < bodies.Length; j++)
            {
                ref BodyEstimate b = ref bodies[j];
                // 1. the pinned enemy: the other bodies that have struck by this body's strike (support out of earshot is not support).
                float pin = 0f;
                float latest = strikeHours[j];
                for (int k = 0; k < bodies.Length; k++)
                {
                    if (k == j) continue;
                    if (strikeHours[k] > latest) latest = strikeHours[k];
                    if (strikeHours[k] <= strikeHours[j] && bodies[k].DefenseRatio > 0f) pin += bodies[k].PinStrength / bodies[k].DefenseRatio;
                }
                b.Pin = pin;

                // 2. the exposure window: the wait for the last body plus the own support time.
                float sigma = SupportHours(job, members, bodies, j);
                b.SupportHours = sigma;
                b.ExposureWindowHours = Math.Max(0f, latest - strikeHours[j]) + sigma;
                b.EnemyShiftHours = ShiftHours(job, in b);

                // 3. the enemy against this body.
                float local = enemy.P > 0f ? enemy.Near(b.StrikeX, b.StrikeZ, OddsRadiusMeters) * (enemy.N / enemy.P)
                    : enemy.N / (bodies.Length + 1);
                b.LocalEnemy = local;
                float share = ShiftShare(b.ExposureWindowHours, b.EnemyShiftHours);
                b.EnemyAgainst = local + share * Math.Max(0f, enemy.N - pin - local);

                // 4. the isolated odds: the main body fights from its ground, a wing attacks.
                b.OwnRatio = b.Main ? MainOwnRatio(job, in b) : 1f;
                b.IsolatedOdds = b.EnemyAgainst > 0f
                    ? b.Strength * b.Morale * b.OwnRatio / (b.EnemyAgainst * enemy.Morale)
                    : EnemyVisibleSector.MaxKnowableOdds;

                // 5. and 6. the bar and the penalty.
                b.Detail = DetailPenalty(b.IsolatedOdds, bar);
            }
        }

        // The own ratio of a main body that fixes and holds: BaseDefenseRatio + TerrainStrength at its highest-priority fixing member's anchor on
        // the axis toward the enemy anchor.
        private static float MainOwnRatio(FeasibilityJob job, in BodyEstimate b)
        {
            TerrainPicture picture = job.Input.Picture;
            if (picture == null || !picture.Ready) return BaseDefenseRatio;
            float ax = job.EnemyX - b.TargetX, az = job.EnemyZ - b.TargetZ;
            float len = (float)Math.Sqrt(ax * ax + az * az);
            if (len > 1f) { ax /= len; az /= len; }
            else { ax = job.Input.Axis.ForwardX; az = job.Input.Axis.ForwardZ; }
            return BaseDefenseRatio + picture.TerrainStrength(b.TargetX, b.TargetZ, ax, az);
        }

        // The own support time sigma of body j: the least over the other bodies of a courier from the army HQ plus the march over the crossing
        // graph from where that body stands when it has struck to this body's strike point. A wing stands on its strike point, a fix on its
        // anchors; with no other body no support comes.
        private static float SupportHours(FeasibilityJob job, CandidateScratch w, Span<BodyEstimate> bodies, int j)
        {
            float best = float.PositiveInfinity;
            for (int m = 0; m < w.MemberCount; m++)
            {
                MemberEstimate member = w.Members[m];
                if (member.Body == j || member.Kind == MemberOther) continue;
                ref BodyEstimate from = ref bodies[member.Body];
                float fx = from.Envelops ? from.StrikeX : member.TargetX, fz = from.Envelops ? from.StrikeZ : member.TargetZ;
                ChildFact child = job.Children[job.SlotOfChild(member.ChildId)];
                RouteResult route = job.Graph.Route(fx, fz, bodies[j].StrikeX, bodies[j].StrikeZ, child.BaseSpeed, child.MeanFatigue, false);
                if (route.NoRoute) continue;
                float march = route.Found ? route.Hours : StraightHours(job, fx, fz, bodies[j].StrikeX, bodies[j].StrikeZ, in child);
                float hours = member.CourierHours + march;
                if (hours < best) best = hours;
            }
            return best;
        }

        private static float StraightHours(FeasibilityJob job, float x0, float z0, float x1, float z1, in ChildFact child)
        {
            float cost = LegSampler.Cost(job.Graph.Grid, x0, z0, x1, z1, false, 0f, 0f, 0, false, 0f, 0f, 0, out _);
            return cost / job.Graph.SpeedDivisor(child.BaseSpeed, child.MeanFatigue);
        }

        // requirement-shift-time: the enemy's courier plus its march (infantry, unfatigued, over the same crossing graph, an own-covered crossing
        // costing time) from its mass, or from its line end on the side a strike point beyond that end lies, to the body's strike point.
        private static float ShiftHours(FeasibilityJob job, in BodyEstimate b)
        {
            EnemyPicture enemy = job.Enemy;
            TacticalFrontAxis axis = job.Input.Axis;
            float ox = enemy.MassX, oz = enemy.MassZ;
            if (axis.HasEnemyExtent)
            {
                float lateral = axis.LateralOf(b.StrikeX, b.StrikeZ);
                if (lateral > axis.EnemyLateralMax + 1f) EnvelopmentManeuver.EnemyLineEnd(in axis, enemy.MassX, enemy.MassZ, true, out ox, out oz);
                else if (lateral < axis.EnemyLateralMin - 1f) EnvelopmentManeuver.EnemyLineEnd(in axis, enemy.MassX, enemy.MassZ, false, out ox, out oz);
            }
            MarchRules rules = job.Input.Rules;
            float courier = 0f;
            if (rules.OrderDelays)
            {
                float ride = LegSampler.Cost(job.Graph.Grid, ox, oz, b.StrikeX, b.StrikeZ, false, 0f, 0f, 0, false, 0f, 0f, 0, out _);
                courier = rules.ProcessingHours + rules.RouteProcessingHours + (rules.CourierSpeedPerHour > 0f ? ride / rules.CourierSpeedPerHour : 0f);
            }
            RouteResult route = job.Graph.Route(ox, oz, b.StrikeX, b.StrikeZ, rules.InfantryBase, 0f, true);
            float march;
            if (route.Found) march = route.Hours;
            else if (route.NoRoute) march = float.PositiveInfinity;
            else
            {
                float cost = LegSampler.Cost(job.Graph.Grid, ox, oz, b.StrikeX, b.StrikeZ, false, 0f, 0f, 0, false, 0f, 0f, 0, out _);
                march = cost / job.Graph.SpeedDivisor(rules.InfantryBase, 0f);
            }
            float tau = courier + march;
            TacticalIntentModel intent = job.Input.EnemyIntent;
            if (intent.PrimaryIntent == InferredIntent.Defend && intent.Confidence01 >= ArmyIntentInference.ConfidenceFloor) tau *= PassiveShiftFactor;
            return tau;
        }

        // requirement-approach: half the approach difficulty of the ground to cross plus half the enemy's terrain strength where it stands near
        // the strike point, plus AnchoredEndPenalty for an enveloping body whose line end rests on a river or a crest. The main body takes its
        // worst fixing member's.
        private static float ApproachOf(FeasibilityJob job, CandidateScratch w, int body)
        {
            TerrainPicture picture = job.Input.Picture;
            if (picture == null || !picture.Ready) return 0f;
            ref BodyEstimate b = ref w.Bodies[body];
            float worst = 0f;
            for (int m = 0; m < w.MemberCount; m++)
            {
                MemberEstimate member = w.Members[m];
                if (member.Body != body || member.Kind == MemberOther) continue;
                ApproachAxis(job, b.StrikeX, b.StrikeZ, member.TargetX, member.TargetZ, out float ax, out float az);
                float difficulty = picture.ApproachDifficulty(b.StrikeX, b.StrikeZ, ax, az);
                float ground = job.Enemy.Near(b.StrikeX, b.StrikeZ, OddsRadiusMeters) > 0f ? picture.TerrainStrength(b.StrikeX, b.StrikeZ, ax, az) : 0f;
                float approach = 0.5f * difficulty + 0.5f * ground;
                if (b.Envelops)
                {
                    TacticalFrontAxis axis = job.Input.Axis;
                    bool right = EnvelopmentManeuver.RectIsRight(in axis, member.TargetX, member.TargetZ);
                    float ox = right ? axis.RightX : -axis.RightX, oz = right ? axis.RightZ : -axis.RightZ;
                    if (TacticalTerrainLinePlanner.FlankAnchored(picture, b.StrikeX, b.StrikeZ, ox, oz)) approach += AnchoredEndPenalty;
                }
                if (approach > 1f) approach = 1f;
                if (approach > worst) worst = approach;
            }
            return worst;
        }

        // requirement-route-exposure: the share of an enveloping body's route, outside its last ContactMeters, that lies within a perceived
        // regiment's reach; forest samples count at half weight because woods screen a column.
        private static float ExposureOf(FeasibilityJob job, CandidateScratch w, int body)
        {
            ref BodyEstimate b = ref w.Bodies[body];
            MemberEstimate member = default(MemberEstimate);
            for (int m = 0; m < w.MemberCount; m++) if (w.Members[m].Body == body) member = w.Members[m];
            ChildFact child = job.Children[job.SlotOfChild(member.ChildId)];
            RiverRegions regions = job.Input.Regions;

            // The route's corners: the child, the march crossing, the anchor, the attack crossing, the strike point.
            Span<float> px = stackalloc float[5];
            Span<float> pz = stackalloc float[5];
            int n = 0;
            px[n] = child.X; pz[n++] = child.Z;
            if (member.MarchVia >= 0 && regions != null)
            {
                regions.LinkCrossing(member.MarchVia, out float vx, out float vz, out _, out _);
                px[n] = vx; pz[n++] = vz;
            }
            px[n] = member.TargetX; pz[n++] = member.TargetZ;
            if (member.AttackVia >= 0 && regions != null)
            {
                regions.LinkCrossing(member.AttackVia, out float vx, out float vz, out _, out _);
                px[n] = vx; pz[n++] = vz;
            }
            px[n] = b.StrikeX; pz[n++] = b.StrikeZ;

            float total = 0f;
            for (int i = 1; i < n; i++) total += Distance(px[i - 1], pz[i - 1], px[i], pz[i]);
            float counted = 0f, exposed = 0f, walked = 0f;
            BattleTerrainGrid grid = job.Input.Grid;
            for (int i = 1; i < n; i++)
            {
                float leg = Distance(px[i - 1], pz[i - 1], px[i], pz[i]);
                int steps = (int)(leg / ExposureStepMeters);
                for (int s = 0; s < steps; s++)
                {
                    float t = leg > 0f ? s * ExposureStepMeters / leg : 0f;
                    float x = px[i - 1] + (px[i] - px[i - 1]) * t, z = pz[i - 1] + (pz[i] - pz[i - 1]) * t;
                    float along = walked + s * ExposureStepMeters;
                    if (total - along <= EnvelopmentManeuver.ContactMeters) continue;
                    counted += 1f;
                    if (job.Enemy.Sees(x, z, job.Input.ReachByUnitType))
                        exposed += grid != null && grid.IdAt(x, z) == BattleTerrainGrid.ForestId ? 0.5f : 1f;
                }
                walked += leg;
            }
            return counted > 0f ? exposed / counted : 0f;
        }

        private static float Distance(float x0, float z0, float x1, float z1)
        {
            float dx = x1 - x0, dz = z1 - z0;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
