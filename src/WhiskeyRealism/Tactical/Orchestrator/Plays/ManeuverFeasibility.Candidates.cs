using System;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14 requirement-candidate-set, requirement-hypothetical-layout, requirement-bodies and requirement-verdict: each candidate play is
    // laid out the way the executor would lay it out, its children are assigned to rects, the bodies and their routes are timed over
    // the crossing graph, and the candidate gets a verdict. The risk terms of requirement-detail-risk and requirement-penalty are
    // filled by ManeuverFeasibility.Risk.cs.

    /// <summary>The templates the evaluation lays out, looked up once (PlayTemplateCatalog.Lookup builds a new template on every call).</summary>
    internal static class FeasibilityTemplates
    {
        private static readonly PlayTemplate[] ByPlan = new PlayTemplate[ManeuverFeasibility.PlanSlots];
        private static readonly bool[] TwoSided = new bool[ManeuverFeasibility.PlanSlots];

        static FeasibilityTemplates()
        {
            foreach (object value in Enum.GetValues(typeof(BattlePlanId)))
            {
                var plan = (BattlePlanId)value;
                int slot = (int)plan;
                if (slot < 0 || slot >= ByPlan.Length) continue;
                PlayTemplate template = PlayTemplateCatalog.Lookup(plan);
                ByPlan[slot] = template;
                if (template == null) continue;
                int envelops = 0;
                bool offCentre = false;
                for (int i = 0; i < template.Rects.Count; i++)
                {
                    if (!template.Rects[i].Envelops) continue;
                    envelops++;
                    if (Math.Abs(template.Rects[i].RelativeAnchorX) > 0.001f) offCentre = true;
                }
                // requirement-candidate-set: exactly one enveloping rect off the centre line is evaluated on both sides.
                TwoSided[slot] = envelops == 1 && offCentre;
            }
        }

        public static PlayTemplate Lookup(BattlePlanId plan)
        {
            int slot = (int)plan;
            return slot >= 0 && slot < ByPlan.Length ? ByPlan[slot] : null;
        }

        public static bool EvaluatedOnBothSides(BattlePlanId plan)
        {
            int slot = (int)plan;
            return slot >= 0 && slot < TwoSided.Length && TwoSided[slot];
        }
    }

    public static partial class ManeuverFeasibility
    {
        public const int MaxBodies = 6, MaxChildren = 64;
        public const float RiskyPenalty = 0.35f, StrikeLegWeight = 0.5f;
        internal const int MaxRects = 8;
        /// <summary>Slots of the per-plan tables: BattlePlanId values are 0 to 18, with room for the later slices' plans.</summary>
        internal const int PlanSlots = 24;

        // ---- entry points --------------------------------------------------------------------------------------

        /// <summary>
        /// Every candidate of the input, to completion (a forced evaluation: before the initial pick and the night replan). Publishes
        /// the ledger whole.
        /// </summary>
        public static void Evaluate(in FeasibilityInput input, FeasibilityLedger into)
        {
            FeasibilityJob job = into.Job;
            job.Abort();
            EvaluateSlice(in input, job, into, long.MaxValue, null);
        }

        /// <summary>
        /// One slice of the sliced evaluation: starts a new evaluation when none is running, evaluates whole candidates until the deadline
        /// passes, and publishes when the last one is done (returns true).
        /// </summary>
        public static bool EvaluateSlice(in FeasibilityInput input, FeasibilityJob job, FeasibilityLedger into, long deadlineTicks, Func<long> clock)
        {
            if (!job.Running) job.Begin(in input, into);
            return job.Step(into, deadlineTicks, clock);
        }

        /// <summary>The verdict a penalty gives (requirement-verdict): at least <see cref="RiskyPenalty"/> is Risky.</summary>
        public static FeasibilityVerdict VerdictOf(float penalty) => penalty >= RiskyPenalty ? FeasibilityVerdict.Risky : FeasibilityVerdict.Feasible;

        /// <summary>
        /// A body's penalty (requirement-penalty): <c>1 - (1 - daylight)(1 - detail)(1 - 0.6 contested)(1 - 0.5 approach)(1 - 0.4 exposure)</c>.
        /// </summary>
        public static float CombinePenalty(in BodyEstimate b)
            => 1f - (1f - b.Daylight) * (1f - b.Detail) * (1f - 0.6f * b.Contested) * (1f - 0.5f * b.Approach) * (1f - 0.4f * b.Exposure);

        // ---- one candidate -------------------------------------------------------------------------------------

        internal static void EvaluateCandidate(FeasibilityJob job, int planIndex, FeasibilityLedger ledger)
        {
            BattlePlanId plan = job.Plans[planIndex];
            PlayTemplate template = FeasibilityTemplates.Lookup(plan);
            if (template == null) return;
            CandidateScratch best = job.SideA;
            EvaluateSide(job, template, plan, false, job.SideA);
            int sides = 1;
            if (FeasibilityTemplates.EvaluatedOnBothSides(plan))
            {
                EvaluateSide(job, template, plan, true, job.SideB);
                sides = 2;
                if (BetterSide(job.SideB.Result, job.SideA.Result)) best = job.SideB;
            }
            job.SidesEvaluated[planIndex] = sides;
            ledger.AddBack(in best.Result, new ReadOnlySpan<BodyEstimate>(best.Bodies, 0, best.BodyCount),
                new ReadOnlySpan<MemberEstimate>(best.Members, 0, best.MemberCount));
        }

        // requirement-candidate-set: sides rank by verdict first (Feasible and Risky, then Unknown, then Impossible), then the lower
        // penalty; a tie keeps the authored side.
        internal static bool BetterSide(in CandidateResult challenger, in CandidateResult incumbent)
        {
            int cr = RankOf(challenger.Verdict), ir = RankOf(incumbent.Verdict);
            if (cr != ir) return cr < ir;
            return challenger.Penalty < incumbent.Penalty;
        }

        private static int RankOf(FeasibilityVerdict v)
        {
            switch (v)
            {
                case FeasibilityVerdict.Feasible:
                case FeasibilityVerdict.Risky: return 0;
                case FeasibilityVerdict.Unknown: return 1;
                default: return 2;
            }
        }

        private static void Finish(CandidateScratch w, FeasibilityVerdict verdict, string reason)
        {
            w.Result.Verdict = verdict;
            w.Result.Reason = reason;
            w.Result.Penalty = verdict == FeasibilityVerdict.Impossible ? 1f : 0f;
        }

        private static bool MapKnown(FeasibilityJob job)
            => job.Graph.Ready && job.Input.Picture != null && job.Input.Picture.Ready;

        private static bool IsReserveRect(PlayRectKind kind) => kind == PlayRectKind.Reserve || kind == PlayRectKind.ReserveLine;

        private static void EvaluateSide(FeasibilityJob job, PlayTemplate template, BattlePlanId plan, bool mirrored, CandidateScratch w)
        {
            w.Reset(plan, mirrored);
            if (!job.Input.Axis.IsValid || !job.FrameValid) { Finish(w, FeasibilityVerdict.Unknown, "no-axis"); return; }
            if (job.OfferedCount == 0) { Finish(w, FeasibilityVerdict.Unknown, "no-children"); return; }
            if (!MapKnown(job)) { Finish(w, FeasibilityVerdict.Unknown, "map-unknown"); return; }

            // The hypothetical layout, exactly as ComputeZoneAssignments would project and assign it.
            int rectCount = template.Rects.Count;
            WorldPlayRect[] rects = job.RectsFor(plan, rectCount);
            PlayProjection.ProjectInto(template, job.ArmyX, job.ArmyZ, job.EnemyX, job.EnemyZ, job.Frontage, in job.Input.Crest, mirrored, rects);
            int assigned = PlayCorpsAssignment.AssignInto(rects, job.Offered, FeasibilityJob.NoObjectives, job.Assignments);

            for (int r = 0; r < rectCount; r++)
            {
                if (!rects[r].Envelops) continue;
                bool filled = false;
                for (int a = 0; a < assigned; a++) if (job.Assignments[a].RectIndex == r) filled = true;
                if (!filled) w.WingUnfilled = true;
            }

            BuildMembers(job, w, assigned);
            ScreenRoutes(job, w);
            PlanTimes(job, w);
            ComputeRisk(job, template, w);
            Conclude(job, w);
        }

        // ---- members and bodies (requirement-bodies) -----------------------------------------------------------

        private static void BuildMembers(FeasibilityJob job, CandidateScratch w, int assigned)
        {
            int envelopers = 0;
            bool anyFixing = false;
            for (int a = 0; a < assigned; a++)
            {
                if (job.Assignments[a].Envelops) envelopers++;
                else if (!IsReserveRect(job.Assignments[a].RectKind)) anyFixing = true;
            }
            // The bodies: each enveloping child on its own, then the main body last. A reserve member of a template without a fixing
            // member joins no body: a reserve strikes nothing and pins nothing.
            int mainBody = anyFixing ? envelopers : -1;
            w.BodyCount = Math.Min(envelopers + (anyFixing ? 1 : 0), w.Bodies.Length);
            for (int b = 0; b < w.BodyCount; b++)
                w.Bodies[b] = new BodyEstimate { Body = b, Main = b == mainBody, Envelops = b != mainBody, MarchVia = -1, AttackVia = -1 };

            bool mainNamed = false;
            int nextEnvelop = 0;
            w.MemberCount = 0;
            for (int a = 0; a < assigned && w.MemberCount < w.Members.Length; a++)
            {
                CorpsAssignment ca = job.Assignments[a];
                int slot = job.SlotOfChild(ca.ChildId);
                if (slot < 0) continue;
                byte kind = ca.Envelops ? MemberEnvelops : (IsReserveRect(ca.RectKind) ? MemberOther : MemberFix);
                int bodyIndex = kind == MemberEnvelops ? nextEnvelop++ : mainBody;
                if (bodyIndex < 0 || bodyIndex >= w.BodyCount) continue;
                ChildFact child = job.Children[slot];
                w.Members[w.MemberCount++] = new MemberEstimate
                {
                    ChildId = ca.ChildId, Body = bodyIndex, Kind = kind, InContact = child.InContact,
                    TargetX = ca.RectAnchorX, TargetZ = ca.RectAnchorZ, MarchVia = -1, AttackVia = -1,
                };

                ref BodyEstimate body = ref w.Bodies[bodyIndex];
                float before = body.Strength;
                body.ChildCount++;
                body.Strength = before + child.Strength;
                body.Morale = body.Strength > 0f ? (body.Morale * before + child.Morale * child.Strength) / body.Strength : child.Morale;
                if (kind == MemberEnvelops)
                {
                    body.ChildId = ca.ChildId;
                    body.Kind = ca.RectKind;
                    body.OffsetHours = ca.StartTimeOffsetSeconds / 3600f;
                    body.TargetX = ca.RectAnchorX;
                    body.TargetZ = ca.RectAnchorZ;
                }
                else if (kind == MemberFix && !mainNamed)
                {
                    // The main body is named by its highest-priority fixing member (assignments run by priority).
                    mainNamed = true;
                    body.ChildId = ca.ChildId;
                    body.Kind = ca.RectKind;
                    body.TargetX = ca.RectAnchorX;
                    body.TargetZ = ca.RectAnchorZ;
                }
            }
        }
    }
}
