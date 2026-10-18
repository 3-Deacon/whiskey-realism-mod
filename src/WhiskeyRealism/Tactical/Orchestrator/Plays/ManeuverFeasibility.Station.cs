using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14W requirement-defender-works-reserve (spec § 7A), the ledger's half: a Defender whose own line holds works keeps a reserve behind its most
    // threatened focal point. Built in work units after the Defender's crossing picture, from the own works picture the evaluation made
    // (WorksPicture.BuildOwn): the own focal points (the weakest few, apart from one another), the earliest enemy arrival at each by the shift-time
    // model (the courier ride from the body plus its march), the most threatened one within FocalThreatHours, the station behind it along the own front
    // axis on own land, and every child's time to the station. The allocator only compares (DirectChildAllocator.StationReserveBehindFocalPoint).
    public static partial class ManeuverFeasibility
    {
        /// <summary>The own focal points considered for the station (the weakest, at least <see cref="WorksRules.FrontageReachMeters"/> apart) and the enemy bodies priced against each.</summary>
        public const int MaxStationFocals = 4, StationBodies = 3;

        /// <summary>Two arrivals this close are a tie, which the larger enemy strength near the focal point breaks.</summary>
        private const float StationTieHours = 0.001f;

        /// <summary>Prepares the station's work: false when there is nothing to do (a monitor run, no perceived enemy, no held works of its own).</summary>
        internal static bool BeginStation(FeasibilityJob job, DefenderPicture d)
        {
            d.HasStation = false;
            d.StationChildCount = 0;
            d.StationFocals = 0;
            if (job.Input.SkipLooks || !d.Perceived || d.BodyCount == 0 || !job.Works.HasHeldWorks) return false;
            PickStationFocals(job, d);
            if (d.StationFocals == 0) return false;
            d.StationUnit = 0;
            d.StationRouting = true;
            return true;
        }

        // The lowest-scored focal points (the weakest places), each at least FrontageReachMeters from the ones already kept, and the bodies nearest each.
        private static void PickStationFocals(FeasibilityJob job, DefenderPicture d)
        {
            WorksPicture works = job.Works;
            float apart2 = WorksRules.FrontageReachMeters * WorksRules.FrontageReachMeters;
            d.StationNearCount = Math.Min(StationBodies, d.BodyCount);
            for (int kept = 0; kept < MaxStationFocals; kept++)
            {
                int pick = -1;
                for (int i = 0; i < works.FocalCount; i++)
                {
                    ref readonly FocalPoint candidate = ref works.Focal(i);
                    bool crowded = false;
                    for (int k = 0; k < kept && !crowded; k++)
                    {
                        float dx = candidate.X - d.StationFocalX[k], dz = candidate.Z - d.StationFocalZ[k];
                        crowded = dx * dx + dz * dz < apart2;
                    }
                    if (crowded || (pick >= 0 && !(candidate.Score < works.Focal(pick).Score))) continue;
                    pick = i;
                }
                if (pick < 0) break;
                d.StationFocalX[kept] = works.Focal(pick).X;
                d.StationFocalZ[kept] = works.Focal(pick).Z;
                d.StationFocalArrival[kept] = float.PositiveInfinity;
                d.StationFocals = kept + 1;
                NearestBodies(d, kept);
            }
        }

        private static void NearestBodies(DefenderPicture d, int focal)
        {
            for (int k = 0; k < d.StationNearCount; k++)
            {
                int best = -1;
                float bestDistance = float.PositiveInfinity;
                for (int b = 0; b < d.BodyCount; b++)
                {
                    bool taken = false;
                    for (int j = 0; j < k && !taken; j++) taken = d.StationNear[focal * StationBodies + j] == b;
                    if (taken) continue;
                    float distance = Distance(d.BodyX[b], d.BodyZ[b], d.StationFocalX[focal], d.StationFocalZ[focal]);
                    if (distance < bestDistance) { bestDistance = distance; best = b; }
                }
                d.StationNear[focal * StationBodies + k] = best;
            }
        }

        /// <summary>One work unit of the station: one body's arrival at one focal point (a courier ride and a route), the choice, then one child's time to the station (a route).</summary>
        internal static bool StepStation(FeasibilityJob job, DefenderPicture d)
        {
            if (d.StationRouting)
            {
                int near = d.StationNearCount;
                int unit = d.StationUnit++;
                int f = unit / near, body = d.StationNear[f * StationBodies + unit % near];
                float fx = d.StationFocalX[f], fz = d.StationFocalZ[f];
                float ride = EnemyCourierHours(job, d.BodyX[body], d.BodyZ[body], fx, fz);
                RouteResult route = job.Graph.Route(d.BodyX[body], d.BodyZ[body], fx, fz, job.Input.Rules.InfantryBase, 0f, true);
                if (route.Found)
                {
                    float hours = (ride + route.Hours) * d.PassiveFactor;
                    if (hours < d.StationFocalArrival[f]) d.StationFocalArrival[f] = hours;
                }
                if (d.StationUnit < d.StationFocals * near) return false;
                d.StationRouting = false;
                d.StationUnit = 0;
                if (!ChooseStation(job, d)) { d.Stage = DefenderStage.Done; return true; }
                return false;
            }

            if (d.StationUnit >= job.ChildCount)
            {
                d.StationChildCount = job.ChildCount;
                d.Stage = DefenderStage.Done;
                return true;
            }
            int c = d.StationUnit++;
            ChildFact child = job.Children[c];
            if (child.HasPlayerOwned || child.OwnIndex < 0)
            {
                d.StationShift[c] = float.NaN;
                return false;
            }
            RouteResult leg = job.Graph.Route(child.X, child.Z, d.Station.X, d.Station.Z, child.BaseSpeed, child.MeanFatigue, false);
            d.StationShift[c] = leg.Found ? child.CourierHours + leg.Hours : float.PositiveInfinity;
            return false;
        }

        // The focal point the enemy reaches first within FocalThreatHours (a tie to the larger enemy strength near it) and the station behind it.
        private static bool ChooseStation(FeasibilityJob job, DefenderPicture d)
        {
            int best = -1;
            float bestStrength = 0f;
            for (int f = 0; f < d.StationFocals; f++)
            {
                float arrival = d.StationFocalArrival[f];
                if (!(arrival <= WorksRules.FocalThreatHours)) continue;
                float strength = EnemyStrengthNear(d, d.StationFocalX[f], d.StationFocalZ[f]);
                bool better = best < 0 || arrival < d.StationFocalArrival[best] - StationTieHours
                    || (arrival <= d.StationFocalArrival[best] + StationTieHours && strength > bestStrength);
                if (!better) continue;
                best = f;
                bestStrength = strength;
            }
            if (best < 0) return false;

            TacticalFrontAxis axis = job.Input.Axis;
            RiverRegions regions = job.Input.Regions;
            float fx = d.StationFocalX[best], fz = d.StationFocalZ[best];
            float x = fx - WorksRules.ReserveStationMeters * axis.ForwardX, z = fz - WorksRules.ReserveStationMeters * axis.ForwardZ;
            if (d.OwnRegion >= 0 && regions.RegionAt(x, z) != d.OwnRegion && regions.TrySnapToRegion(x, z, d.OwnRegion, out float sx, out float sz)) { x = sx; z = sz; }
            d.Station = new StationPoint
            {
                Has = true, FocalX = fx, FocalZ = fz, X = x, Z = z, Heading = PostureSense.HeadingTo(x, z, fx, fz), ArrivalHours = d.StationFocalArrival[best],
            };
            d.HasStation = true;
            return true;
        }

        private static float EnemyStrengthNear(DefenderPicture d, float x, float z)
        {
            float r = OpeningDetector.LocalEnemyValueRadiusMeters, sum = 0f;
            for (int b = 0; b < d.BodyCount; b++)
                if (Distance(d.BodyX[b], d.BodyZ[b], x, z) <= r) sum += d.BodyWeight[b];
            return sum;
        }
    }
}
