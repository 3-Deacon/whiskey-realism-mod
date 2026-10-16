using System;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14 requirement-route-screen, requirement-strike-time and requirement-verdict for one candidate side: every member's march leg
    // and strike or inward leg are routed over the crossing graph, each body gets its strike time, the common plan sets the releases,
    // and the candidate gets its verdict.

    public static partial class ManeuverFeasibility
    {
        // Folds one routed leg into the member's march (or inward) total.
        private static void Fold(ref RouteResult total, in RouteResult leg)
        {
            total.Found = true;
            total.LegHours += leg.LegHours;
            total.HeldHours += leg.HeldHours;
            total.CostUnits += leg.CostUnits;
            total.CrossingCount += leg.CrossingCount;
            total.HeldCount += leg.HeldCount;
            total.Estimated |= leg.Estimated;
            total.Confirmed |= leg.Confirmed;
            if (leg.Contested > total.Contested) total.Contested = leg.Contested;
            if (total.FirstLink < 0) total.FirstLink = leg.FirstLink;
        }

        // Routes one leg. A leg with no path sets the verdict flag its reason names (march: no-crossing; inward: river-on-leg; a leg the
        // NavMesh refused: no-route); a leg whose end points the map cannot place counts at its straight cost and makes the verdict
        // Unknown.
        private static void RouteLeg(FeasibilityJob job, CandidateScratch w, float x0, float z0, float x1, float z1, in ChildFact child,
            bool inward, ref RouteResult total)
        {
            CrossingGraphView graph = job.Graph;
            RouteResult leg = graph.Route(x0, z0, x1, z1, child.BaseSpeed, child.MeanFatigue, false);
            if (leg.Found) { Fold(ref total, in leg); return; }
            if (leg.NoRoute)
            {
                if (leg.Refused) w.NoRoute = true;
                else if (inward) w.RiverOnLeg = true;
                else w.NoCrossing = true;
                return;
            }
            w.RouteUnknown = true;
            float cost = LegSampler.Cost(graph.Grid, x0, z0, x1, z1, false, 0f, 0f, 0, false, 0f, 0f, 0, out bool estimated);
            var straight = new RouteResult { Found = true, LegHours = cost / graph.SpeedDivisor(child.BaseSpeed, child.MeanFatigue), CostUnits = cost, Estimated = estimated, FirstLink = -1 };
            Fold(ref total, in straight);
        }

        private static void ScreenRoutes(FeasibilityJob job, CandidateScratch w)
        {
            TacticalFrontAxis axis = job.Input.Axis;
            bool defender = job.Input.Stance == BattleStance.Defender;
            MarchRules rules = job.Input.Rules;
            for (int m = 0; m < w.MemberCount; m++)
            {
                ref MemberEstimate member = ref w.Members[m];
                ref BodyEstimate body = ref w.Bodies[member.Body];
                ChildFact child = job.Children[job.SlotOfChild(member.ChildId)];
                float ax = member.TargetX, az = member.TargetZ;

                // The march leg: the child's position to its rect anchor, a DeepRaid by its outflank point first.
                var march = new RouteResult { FirstLink = -1 };
                bool deepRaid = member.Kind == MemberEnvelops && body.Kind == PlayRectKind.DeepRaid;
                float outflankX = ax, outflankZ = az;
                if (deepRaid)
                {
                    bool right = EnvelopmentManeuver.RectIsRight(in axis, ax, az);
                    float depth = (ax - axis.OriginX) * axis.ForwardX + (az - axis.OriginZ) * axis.ForwardZ;
                    if (!EnvelopmentManeuver.OutflankPoint(in axis, depth, job.PressureLeft, job.PressureRight, right, out outflankX, out outflankZ))
                    {
                        outflankX = ax;
                        outflankZ = az;
                    }
                    RouteLeg(job, w, child.X, child.Z, outflankX, outflankZ, in child, false, ref march);
                    RouteLeg(job, w, outflankX, outflankZ, ax, az, in child, false, ref march);
                }
                else RouteLeg(job, w, child.X, child.Z, ax, az, in child, false, ref march);
                member.MarchVia = march.FirstLink;
                member.MarchHours = march.Hours;
                member.CourierHours = child.CourierHours;
                float contested = march.Contested;
                float inwardSurplus = 0f;

                if (member.Kind == MemberEnvelops)
                {
                    // The inward leg BC11 attacks along: the anchor to the enemy line end (a DeepRaid: its outflank point).
                    float sx, sz;
                    if (deepRaid) { sx = outflankX; sz = outflankZ; }
                    else
                    {
                        bool right = EnvelopmentManeuver.RectIsRight(in axis, ax, az);
                        if (!EnvelopmentManeuver.EnemyLineEnd(in axis, job.EnemyX, job.EnemyZ, right, out sx, out sz)) { sx = job.EnemyX; sz = job.EnemyZ; }
                    }
                    body.StrikeX = sx;
                    body.StrikeZ = sz;
                    inwardSurplus = InwardSurplusHours(job.Graph, ax, az, sx, sz, child.BaseSpeed, child.MeanFatigue, out RouteResult inward);
                    if (inward.NoRoute)
                    {
                        if (inward.Refused) w.NoRoute = true;
                        else w.RiverOnLeg = true;
                    }
                    else if (inward.Unknown) w.RouteUnknown = true;
                    member.AttackVia = inward.FirstLink;
                    if (inward.Contested > contested) contested = inward.Contested;
                    body.InwardSurplusHours = Math.Max(body.InwardSurplusHours, inwardSurplus);
                    body.AttackVia = inward.FirstLink;
                    body.AttackCrossing = StateAt(job, inward.FirstLink);
                }
                else if (member.Kind == MemberFix && !defender)
                {
                    // A fixing member's strike leg: its anchor to the main body's strike point, the play's enemy anchor.
                    body.StrikeX = job.EnemyX;
                    body.StrikeZ = job.EnemyZ;
                    RouteResult strike = job.Graph.Route(ax, az, job.EnemyX, job.EnemyZ, child.BaseSpeed, child.MeanFatigue, false);
                    float strikeContested = 0f;
                    if (strike.NoRoute) { body.NoStrikePath = true; strikeContested = 1f; }
                    else if (strike.Found) strikeContested = strike.Contested * StrikeLegWeight;
                    if (strikeContested > body.StrikeContested) body.StrikeContested = strikeContested;
                    if (strikeContested > contested) contested = strikeContested;
                }

                float courierForTime = member.InContact ? 0f : child.CourierHours;
                member.StrikeHours = StrikeHours(courierForTime, in march, inwardSurplus, in rules);
                // The main body's terms are its fixing members' worst: a reserve member's route only has to exist.
                if (member.Kind != MemberOther && contested > body.Contested) body.Contested = contested;
                if (march.CostUnits > body.RouteCost) body.RouteCost = march.CostUnits;
                body.RouteEstimated |= march.Estimated;
                body.RouteConfirmed |= march.Confirmed;
                if (member.Kind == MemberEnvelops)
                {
                    body.MarchVia = march.FirstLink;
                    body.MarchCrossing = StateAt(job, march.FirstLink);
                }
            }
        }

        private static CrossingState StateAt(FeasibilityJob job, int link)
            => link >= 0 && job.Input.Crossings != null ? job.Input.Crossings.StateOf(link) : CrossingState.Unknown;

        // The common plan (requirement-strike-time): C*, each body's strike S* and every member's release.
        private static void PlanTimes(FeasibilityJob job, CandidateScratch w)
        {
            int n = w.MemberCount;
            Span<float> strike = stackalloc float[n];
            Span<float> offset = stackalloc float[n];
            Span<byte> kind = stackalloc byte[n];
            Span<bool> contact = stackalloc bool[n];
            Span<float> desired = stackalloc float[n];
            Span<float> release = stackalloc float[n];
            Span<bool> hasRelease = stackalloc bool[n];
            for (int m = 0; m < n; m++)
            {
                strike[m] = w.Members[m].StrikeHours;
                offset[m] = w.Members[m].Kind == MemberEnvelops ? w.Bodies[w.Members[m].Body].OffsetHours : 0f;
                kind[m] = w.Members[m].Kind;
                contact[m] = w.Members[m].InContact;
            }
            float cStar = PlanReleases(strike, offset, kind, contact, desired, release, hasRelease);
            bool mainInContact = false;
            for (int m = 0; m < n; m++) if (kind[m] == MemberFix && contact[m]) mainInContact = true;
            for (int m = 0; m < n; m++)
            {
                ref MemberEstimate member = ref w.Members[m];
                member.DesiredStrikeHours = desired[m];
                member.ReleaseHours = release[m];
                member.HasRelease = hasRelease[m];
                ref BodyEstimate body = ref w.Bodies[member.Body];
                if (member.Kind == MemberEnvelops)
                {
                    body.StrikeHours = member.StrikeHours;
                    body.DesiredStrikeHours = desired[m];
                    body.ReleaseHours = release[m];
                    body.CourierHours = member.CourierHours;
                    body.MarchHours = member.MarchHours;
                }
                else if (member.Kind == MemberFix)
                {
                    // T_main is the latest fixing member's T, or 0 for a main body already in contact; the fix strikes at C*.
                    if (!mainInContact && member.StrikeHours > body.StrikeHours) body.StrikeHours = member.StrikeHours;
                    body.DesiredStrikeHours = cStar;
                    if (release[m] > body.ReleaseHours) body.ReleaseHours = release[m];
                    if (member.CourierHours > body.CourierHours) body.CourierHours = member.CourierHours;
                    if (member.MarchHours > body.MarchHours) body.MarchHours = member.MarchHours;
                }
            }
        }

        // requirement-verdict: the first match wins. The Unknown cases that precede the layout were taken before it; the route screen's
        // flags give the Impossible reasons in their order; otherwise the penalty decides Feasible or Risky.
        private static void Conclude(FeasibilityJob job, CandidateScratch w)
        {
            bool defender = job.Input.Stance == BattleStance.Defender;
            if (w.WingUnfilled) { Finish(w, FeasibilityVerdict.Impossible, "wing-unfilled"); return; }
            if (w.NoCrossing) { Finish(w, FeasibilityVerdict.Impossible, "no-crossing"); return; }
            if (w.RiverOnLeg) { Finish(w, FeasibilityVerdict.Impossible, "river-on-leg"); return; }
            if (w.NoRoute) { Finish(w, FeasibilityVerdict.Impossible, "no-route"); return; }
            if (w.RouteUnknown) { Finish(w, FeasibilityVerdict.Unknown, "map-unknown"); return; }

            float worst = 0f;
            string reason = null;
            for (int b = 0; b < w.BodyCount; b++)
            {
                ref BodyEstimate body = ref w.Bodies[b];
                if (defender) body.Contested = 0f;
                body.Penalty = CombinePenalty(in body);
                body.Reason = ReasonOf(in body);
                // The candidate fails at its weakest body; the first of equals names the reason.
                if (b == 0 || body.Penalty > worst)
                {
                    worst = body.Penalty;
                    reason = body.Reason;
                }
            }
            w.Result.Penalty = worst;
            w.Result.Verdict = VerdictOf(worst);
            w.Result.Reason = reason;
        }

        /// <summary>The name of a body's largest weighted term above <see cref="ReasonThreshold"/>, or null (requirement-penalty).</summary>
        internal static string ReasonOf(in BodyEstimate b)
        {
            string reason = null;
            float largest = ReasonThreshold;
            if (b.Daylight > largest) { largest = b.Daylight; reason = "after-dark"; }
            if (b.Detail > largest) { largest = b.Detail; reason = "detail"; }
            if (0.6f * b.Contested > largest) { largest = 0.6f * b.Contested; reason = b.NoStrikePath && b.Contested >= b.StrikeContested ? "no-strike-crossing" : "contested"; }
            if (0.5f * b.Approach > largest) { largest = 0.5f * b.Approach; reason = "approach"; }
            if (0.4f * b.Exposure > largest) { reason = "exposure"; }
            return reason;
        }

        public const float ReasonThreshold = 0.2f;

        static partial void ComputeRisk(FeasibilityJob job, PlayTemplate template, CandidateScratch w);
    }
}
