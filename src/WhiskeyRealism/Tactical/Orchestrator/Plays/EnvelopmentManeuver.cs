// BC11 decision 4 (docs/superpowers/plans/2026-10-15-battle-core.md § BC11): the envelopments the geometric
// plays choose are executed, not only relabelled. Pure: no Unity or vanilla types; the runtime half
// (PlayExecutorRuntime.StepManeuvers) reads the snapshot, the terrain grid and the terrain heights into a
// ManeuverInput and keeps one ManeuverState per child.
//
// Vanilla anchors (decompile, build 14703111):
//   FlankMove 116929-117108: a two-leg hook at +-45 degrees that ends 45 degrees inward; it aborts on river
//     id 4 on either leg (CheckTerrainLine(..., {4}, 2f), 117011-117036) or when the first enemy unit along
//     leg 2 (the leg plus 1.5 x fire range) stands at least heightdifferencetoenemyunitanchored (5 m) above
//     the turn point (117038-117050). CheckFlankMoves 6247-6312 is vanilla's own trigger; this is Whiskey's
//     plan-driven one, with the same two checks.
//   Whiskey's per-child state machine: Hold -> March -> Turn -> Attack, abandoned on a failed check, lost
//     strength, MaxManeuverHours or a plan change. Every timer reads the doctrine clock (NowHours), never
//     real time.

using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    public enum ManeuverPhase
    {
        None,
        Hold,
        March,
        Turn,
        Attack,
        Released,   // a start-delay wing back on its allocator role
        Abandoned,
    }

    /// <summary>The plan of a zone assignment that carries a maneuver (DirectChildIntent.Maneuver).</summary>
    public readonly struct ManeuverSpec
    {
        public ManeuverSpec(PlayRectKind kind, float anchorX, float anchorZ, float startOffsetSeconds, bool envelops,
            bool projectedMirrored = false, float authoredAnchorX = 0f, float authoredAnchorZ = 0f, bool holdsForRelease = false)
        {
            HasValue = true;
            HoldsForRelease = holdsForRelease;
            ProjectedMirrored = projectedMirrored;
            AuthoredAnchorX = authoredAnchorX;
            AuthoredAnchorZ = authoredAnchorZ;
            Kind = kind;
            AnchorX = anchorX;
            AnchorZ = anchorZ;
            StartOffsetSeconds = startOffsetSeconds;
            Envelops = envelops;
        }

        public bool HasValue { get; }
        public PlayRectKind Kind { get; }
        public float AnchorX { get; }
        public float AnchorZ { get; }
        public float StartOffsetSeconds { get; }
        public bool Envelops { get; }

        /// <summary>
        /// BC14 requirement-selected-side: the plan was projected mirrored (the feasibility evaluation chose that side). BC11's one
        /// mirrored try then goes to <see cref="AuthoredAnchorX"/>/<see cref="AuthoredAnchorZ"/>, the rect's un-mirrored anchor, and not to
        /// MirrorAcrossAxis: the projection frame and the front axis need not coincide.
        /// </summary>
        public bool ProjectedMirrored { get; }
        public float AuthoredAnchorX { get; }
        public float AuthoredAnchorZ { get; }

        /// <summary>
        /// BC14 requirement-synchronized-release: a fixing child of a template with an enveloping rect holds at its position until its planned
        /// release (a start-delay maneuver whose gate is the ledger's release, not the rect's offset).
        /// </summary>
        public bool HoldsForRelease { get; }
    }

    public struct ManeuverInput
    {
        public PlayRectKind Kind;
        public bool Envelops;
        public float AnchorX, AnchorZ, StartOffsetSeconds;
        public bool TemplateHasCenter;
        public double PlayActivatedHours, CenterContactHours;            // CenterContactHours < 0 until the Center child sees an enemy within ContactMeters
        public float ChildX, ChildZ;
        public int ChildStrengthBucket;
        public bool ChildArrived, ChildInContact;
        public bool HasEnemyExtent;
        public float EnemyEndX, EnemyEndZ, EnemyAnchorX, EnemyAnchorZ, OutflankX, OutflankZ;   // the rect side's perceived line end; DeepRaid's outflank point
        public bool InwardLegHitsRiver;                                  // BattleTerrainGrid samples, 2 m steps along the inward leg
        // The march leg is judged on the planned route, never on a straight line. MarchRouteKnown is false until the runtime has a
        // route result for this leg: the child then stays in its phase (a Hold waits, "await-route"); it is neither launched nor
        // abandoned. MarchRouteMissing: no complete NavMesh path (a result in its own right).
        public bool MarchRouteKnown, MarchLegHitsRiver, MarchRouteMissing;
        public float AnchorHeight;
        public int CorridorEnemyCount;                                    // perceived enemies (Visible, or Remembered at >= 0.5) on the inward corridor
        public float MaxCorridorEnemyHeight;
        public float HeightDifferenceAnchored;                            // GamePrefs.heightdifferencetoenemyunitanchored (5 m)
        public bool CanMirror;                                            // a lone enveloping rect: one mirrored try
        // Additive: how far the child's headquarters may stand from a point it was sent to and still have arrived. SetGroupFormation
        // paths the HQ groupdepth + groupunitdistancetoback behind the waypoint (92044, 92058), so the runtime passes those.
        public float ArrivalSlackMeters;
        public BattleStance Stance;
        public double NowHours;

        // BC14, each value beside its flag (a default input is "none"; 0 is a valid hour).
        /// <summary>The ledger's planned release, an absolute doctrine hour: the Hold ends at it in place of the Center's contact and the offset.</summary>
        public bool HasPlannedRelease;
        public double PlannedReleaseHours;
        /// <summary>The ledger's time budget (hours from the march): the timeout abandon counts it in place of MaxManeuverHours.</summary>
        public bool HasTimeBudget;
        public double TimeBudgetHours;
        /// <summary>The march via and the attack via the ledger chose (a link crossing). Latched at the March.</summary>
        public bool HasVia;
        public float ViaX, ViaZ;
        public bool HasAttackVia;
        public float AttackViaX, AttackViaZ;
        /// <summary>The child already stands in the far region of the via: the order carries it no more.</summary>
        public bool ViaPassed, AttackViaPassed;
        /// <summary>The spec's HoldsForRelease.</summary>
        public bool HoldsForRelease;
        /// <summary>
        /// BC14W (requirement-works-hold): the works hold is <c>hopeless-assault</c> and this body assaults works (or it is a start delay of the plan):
        /// the Hold is kept, its ceiling suppressed and its latched release and budget cleared. See <see cref="EnvelopmentManeuver.HoldForWorksFor"/>.
        /// </summary>
        public bool HoldForWorks;
    }

    public struct ManeuverState
    {
        public ManeuverPhase Phase;
        public double PhaseSinceHours;
        public bool Mirrored;
        public string AbandonReason;
        // Additive: when the march began (MaxManeuverHours counts from it) and whether a DeepRaid reached its outflank point.
        public double ActiveSinceHours;
        public bool OutflankReached;
        // Additive: why the Hold ended (static literal: center-contact, offset, planned-release, hold-ceiling), carried into the telemetry row.
        public string ProceedReason;

        // BC14, each value beside its flag: Step resets the state with default(ManeuverState), where 0 is a valid hour. The first planned
        // release is latched the first time the Hold sees one (the ceiling is anchored to it, so a release that keeps receding still ends);
        // the budget and the vias at the March, and they do not change after it.
        public bool HasFirstPlannedRelease;
        public double FirstPlannedReleaseHours;
        public bool HasBudget;
        public double BudgetHours;
        public bool HasVia;
        public float ViaX, ViaZ;
        public bool HasAttackVia;
        public float AttackViaX, AttackViaZ;

        /// <summary>BC14W: the hour the works hold last cleared (latched when <see cref="ManeuverInput.HoldForWorks"/> turns false after a hold), beside its flag.</summary>
        public bool HasWorksClear;
        public double WorksClearHours;
    }

    public struct ManeuverOrder
    {
        public ManeuverPhase Phase;
        public CommandTaskType Task;
        public float TargetX, TargetZ, Heading;   // Heading -1 = none
        public string Reason;                      // static literal

        /// <summary>BC14: the crossing the order is routed through (the March's or the Attack's via); HasVia false is none.</summary>
        public bool HasVia;
        public float ViaX, ViaZ;

        /// <summary>The phases that override the child's doctrine order.</summary>
        public bool Overrides => Phase == ManeuverPhase.Hold || Phase == ManeuverPhase.March || Phase == ManeuverPhase.Turn || Phase == ManeuverPhase.Attack;
    }

    /// <summary>One child's maneuver order with the lateral axis its nodes are laid out on.</summary>
    public readonly struct ChildManeuver
    {
        public ChildManeuver(string childId, ManeuverOrder order, float lateralX, float lateralZ)
        {
            ChildId = childId ?? string.Empty;
            Order = order;
            LateralX = lateralX;
            LateralZ = lateralZ;
        }

        public string ChildId { get; }
        public ManeuverOrder Order { get; }
        public float LateralX { get; }
        public float LateralZ { get; }
    }

    public static class EnvelopmentManeuver
    {
        public const float ArrivalMeters = 120f;
        public const float OutflankMarginMeters = 300f;
        public const float LaneMeters = 180f;
        public const float TurnHoldHours = 0.05f;
        public const float MaxManeuverHours = 1.0f;
        public const float ContactMeters = 250f;
        /// <summary>The inward leg's corridor is the leg plus this much, <see cref="CorridorWidthMeters"/> wide.</summary>
        public const float CorridorExtraMeters = 150f;
        public const float CorridorWidthMeters = 100f;
        /// <summary>A remembered enemy counts for the height check only at this confidence (the covered-crossing rule).</summary>
        public const float RememberedConfidence = 0.5f;
        /// <summary>FlankMove's river check step on the inward leg (CheckTerrainLine(..., 2f)).</summary>
        public const float InwardStepMeters = 2f;
        public const float MarchStepMeters = 10f;

        /// <summary>
        /// BC14W: whether the maneuver of a body holds for the works. Only the <c>hopeless-assault</c> hold sets it, for a body that assaults works and
        /// for a start delay of the plan (the focal assault's support); <c>turn-not-struck</c> never does: the turning wing must march.
        /// </summary>
        public static bool HoldForWorksFor(string worksHoldReason, bool assaultsWorks, bool startDelay) => false;

        // ---- the state machine ---------------------------------------------------------------

        public static ManeuverOrder Step(ref ManeuverState state, in ManeuverInput input)
        {
            // Offensive playbooks are never selected for a Defender and the maneuver never runs for one.
            if (input.Stance == BattleStance.Defender)
            {
                state = default(ManeuverState);
                return Done(ManeuverPhase.None, "defender");
            }
            if (state.Phase == ManeuverPhase.Abandoned) return Done(ManeuverPhase.Abandoned, state.AbandonReason ?? "abandoned");
            if (state.Phase == ManeuverPhase.Released) return Done(ManeuverPhase.Released, "start-delay-over");

            return input.Envelops ? StepEnvelopment(ref state, in input) : StepStartDelay(ref state, in input);
        }

        // A wing that only has a start delay (HesitatedCommit 600 battle-s, GenericMethodical 300): it holds for that
        // long after the play's activation and then returns to its allocator role. It never marches, turns or attacks.
        private static ManeuverOrder StepStartDelay(ref ManeuverState state, in ManeuverInput input)
        {
            if (input.HoldsForRelease) return StepFixHold(ref state, in input);
            if (!(input.StartOffsetSeconds > 0f))
            {
                state = default(ManeuverState);
                return Done(ManeuverPhase.None, "no-start-delay");
            }
            if (state.Phase == ManeuverPhase.None)
            {
                state.Phase = ManeuverPhase.Hold;
                state.PhaseSinceHours = input.NowHours;
            }
            double elapsedSeconds = (input.NowHours - input.PlayActivatedHours) * TacticalBattleClock.BattleSecondsPerHour;
            if (elapsedSeconds >= input.StartOffsetSeconds)
            {
                state.Phase = ManeuverPhase.Released;
                state.PhaseSinceHours = input.NowHours;
                return Done(ManeuverPhase.Released, "start-delay-over");
            }
            return HoldOrder(in input, "start-delay");
        }

        // BC14 requirement-synchronized-release: a fixing child with a release still ahead holds (FormUp at its position) until it, then
        // goes back to its allocator role; a fix in contact, a deployed reserve and a child with no ledger entry have no planned release and are
        // never held. The wings' ceiling stands for the fix too: it never waits for good on a stalled wing.
        private static ManeuverOrder StepFixHold(ref ManeuverState state, in ManeuverInput input)
        {
            if (state.Phase == ManeuverPhase.None)
            {
                if (!input.HasPlannedRelease || input.NowHours >= input.PlannedReleaseHours)
                {
                    state = default(ManeuverState);
                    return Done(ManeuverPhase.None, "no-planned-release");
                }
                state.Phase = ManeuverPhase.Hold;
                state.PhaseSinceHours = input.NowHours;
            }
            LatchFirstPlannedRelease(ref state, in input);
            bool ceiling = input.NowHours >= HoldCeilingHours(in state, in input);
            if (!ceiling && input.HasPlannedRelease && input.NowHours < input.PlannedReleaseHours) return HoldOrder(in input, "fix-planned-release");
            state.Phase = ManeuverPhase.Released;
            state.PhaseSinceHours = input.NowHours;
            return Done(ManeuverPhase.Released, ceiling ? "hold-ceiling" : "planned-release");
        }

        private static void LatchFirstPlannedRelease(ref ManeuverState state, in ManeuverInput input)
        {
            if (!input.HasPlannedRelease || state.HasFirstPlannedRelease) return;
            state.HasFirstPlannedRelease = true;
            state.FirstPlannedReleaseHours = input.PlannedReleaseHours;
        }

        // The Hold ceiling: an hour past the play's activation; with a planned release, never earlier than an hour past the release first
        // planned (the latched value, so a release that keeps receding still ends).
        private static double HoldCeilingHours(in ManeuverState state, in ManeuverInput input)
        {
            double ceiling = input.PlayActivatedHours + MaxManeuverHours;
            if (state.HasFirstPlannedRelease) ceiling = Math.Max(ceiling, state.FirstPlannedReleaseHours + MaxManeuverHours);
            return ceiling;
        }

        private static ManeuverOrder StepEnvelopment(ref ManeuverState state, in ManeuverInput input)
        {
            if (state.Phase == ManeuverPhase.None)
            {
                state.Phase = ManeuverPhase.Hold;
                state.PhaseSinceHours = input.NowHours;
            }

            if (state.Phase == ManeuverPhase.Hold)
            {
                // The ceiling: a Hold never outlasts MaxManeuverHours from the play's activation (a Center child that never makes
                // contact, or is destroyed, must not hold the wings for good); the wings then proceed on their own checks. BC14: with a
                // planned release it is never earlier than an hour past the release first planned.
                LatchFirstPlannedRelease(ref state, in input);
                bool ceiling = input.NowHours >= HoldCeilingHours(in state, in input);
                if (!ceiling)
                {
                    if (input.HasPlannedRelease)
                    {
                        // The ledger's release replaces both waits: the wing leaves when the common plan says so.
                        if (input.NowHours < input.PlannedReleaseHours) return HoldOrder(in input, "await-planned-release");
                    }
                    else
                    {
                        if (input.TemplateHasCenter && input.CenterContactHours < 0.0) return HoldOrder(in input, "await-center-contact");
                        double reference = input.TemplateHasCenter ? Math.Max(input.PlayActivatedHours, input.CenterContactHours) : input.PlayActivatedHours;
                        double goHours = reference + input.StartOffsetSeconds / (double)TacticalBattleClock.BattleSecondsPerHour;
                        if (input.NowHours < goHours) return HoldOrder(in input, "hold-offset");
                    }
                }

                // A wing is never launched (or abandoned) before its march leg has a route result: it holds one step more.
                if (!input.MarchRouteKnown) return HoldOrder(in input, "await-route");

                string failure = FlankMoveCheck(in input);
                if (failure != null)
                {
                    // One mirrored try: back to Hold on the mirrored side; the runtime supplies the mirrored anchor, and the March to it
                    // is ordered once that leg's route result exists. A second failure there is final.
                    if (input.CanMirror && !state.Mirrored) return MirrorHold(ref state, in input);
                    return Abandon(ref state, failure);
                }
                state.Phase = ManeuverPhase.March;
                state.PhaseSinceHours = input.NowHours;
                state.ActiveSinceHours = input.NowHours;
                state.ProceedReason = ceiling ? "hold-ceiling"
                    : (input.HasPlannedRelease ? "planned-release" : (input.TemplateHasCenter ? "center-contact" : "offset"));
                // Latched at the March and never changed after it (requirement-crossing-via, requirement-maneuver-time-budget).
                state.HasBudget = input.HasTimeBudget;
                state.BudgetHours = input.TimeBudgetHours;
                state.HasVia = input.HasVia;
                state.ViaX = input.ViaX;
                state.ViaZ = input.ViaZ;
                state.HasAttackVia = input.HasAttackVia;
                state.AttackViaX = input.AttackViaX;
                state.AttackViaZ = input.AttackViaZ;
            }

            // March, Turn and Attack share the abandon rules.
            if (input.ChildStrengthBucket <= 0) return Abandon(ref state, "lost-strength");
            if (input.NowHours - state.ActiveSinceHours >= (state.HasBudget ? state.BudgetHours : MaxManeuverHours)) return Abandon(ref state, "timeout");
            if (state.Phase != ManeuverPhase.Attack)
            {
                string failure = FlankMoveCheck(in input);
                if (failure != null)
                {
                    if (input.CanMirror && !state.Mirrored) return MirrorHold(ref state, in input);
                    return Abandon(ref state, failure);
                }
            }

            switch (state.Phase)
            {
                case ManeuverPhase.March:
                {
                    ManeuverOrder march = MarchOrder(ref state, in input);
                    if (input.ChildArrived && (input.Kind != PlayRectKind.DeepRaid || state.OutflankReached))
                    {
                        state.Phase = ManeuverPhase.Turn;
                        state.PhaseSinceHours = input.NowHours;
                        return TurnOrder(in input);
                    }
                    return march;
                }
                case ManeuverPhase.Turn:
                    if (input.ChildInContact || input.NowHours - state.PhaseSinceHours >= TurnHoldHours)
                    {
                        state.Phase = ManeuverPhase.Attack;
                        state.PhaseSinceHours = input.NowHours;
                        return AttackOrder(in state, in input);
                    }
                    return TurnOrder(in input);
                default:
                    return AttackOrder(in state, in input);
            }
        }

        // The one mirrored try: back to Hold on the mirrored side (the runtime now supplies the mirrored anchor and plans its route).
        // The March is ordered, to the mirrored anchor, at the first step the mirrored leg has a route result and passes its checks.
        private static ManeuverOrder MirrorHold(ref ManeuverState state, in ManeuverInput input)
        {
            state.Mirrored = true;
            state.OutflankReached = false;
            // The ledger's release, budget and vias were computed for the other side: BC11's own rule governs the child from here on.
            state.HasFirstPlannedRelease = false;
            state.HasBudget = false;
            state.HasVia = false;
            state.HasAttackVia = false;
            state.Phase = ManeuverPhase.Hold;
            state.PhaseSinceHours = input.NowHours;
            return HoldOrder(in input, "mirror-flank");
        }

        // The march target: the rect anchor; a DeepRaid goes first to its outflank point.
        private static ManeuverOrder MarchOrder(ref ManeuverState state, in ManeuverInput input)
        {
            bool raid = input.Kind == PlayRectKind.DeepRaid;
            if (raid && !state.OutflankReached && Arrived(input.ChildX, input.ChildZ, input.OutflankX, input.OutflankZ, input.ArrivalSlackMeters))
                state.OutflankReached = true;
            bool outflank = raid && !state.OutflankReached;
            // The via rides until the child stands in its far region; an order re-issued each step never walks it back to the crossing.
            bool via = state.HasVia && !input.ViaPassed;
            return new ManeuverOrder
            {
                Phase = ManeuverPhase.March, Task = CommandTaskType.AdvanceToAssembly,
                TargetX = outflank ? input.OutflankX : input.AnchorX, TargetZ = outflank ? input.OutflankZ : input.AnchorZ,
                Heading = -1f, Reason = outflank ? "march-outflank" : "march-anchor",
                HasVia = via, ViaX = via ? state.ViaX : 0f, ViaZ = via ? state.ViaZ : 0f,
            };
        }

        // On arrival: face the perceived enemy line end on the rect's side (the play's enemy anchor with no extent).
        private static ManeuverOrder TurnOrder(in ManeuverInput input)
        {
            LineEnd(in input, out float ex, out float ez);
            return new ManeuverOrder
            {
                Phase = ManeuverPhase.Turn, Task = CommandTaskType.AdvanceToAssembly, TargetX = input.AnchorX, TargetZ = input.AnchorZ,
                Heading = PostureSense.HeadingTo(input.ChildX, input.ChildZ, ex, ez), Reason = "turn-to-line-end",
            };
        }

        private static ManeuverOrder AttackOrder(in ManeuverState state, in ManeuverInput input)
        {
            LineEnd(in input, out float ex, out float ez);
            // The attack keeps its target, the line end; the attack via only routes the move to it.
            bool via = state.HasAttackVia && !input.AttackViaPassed;
            return new ManeuverOrder
            {
                Phase = ManeuverPhase.Attack, Task = CommandTaskType.AttackObjective, TargetX = ex, TargetZ = ez, Heading = -1f,
                Reason = "attack-line-end",
                HasVia = via, ViaX = via ? state.AttackViaX : 0f, ViaZ = via ? state.AttackViaZ : 0f,
            };
        }

        private static ManeuverOrder HoldOrder(in ManeuverInput input, string reason)
        {
            return new ManeuverOrder
            {
                Phase = ManeuverPhase.Hold, Task = CommandTaskType.FormUp, TargetX = input.ChildX, TargetZ = input.ChildZ, Heading = -1f, Reason = reason,
            };
        }

        private static ManeuverOrder Done(ManeuverPhase phase, string reason)
        {
            return new ManeuverOrder { Phase = phase, Task = CommandTaskType.None, Heading = -1f, Reason = reason };
        }

        private static ManeuverOrder Abandon(ref ManeuverState state, string reason)
        {
            state.Phase = ManeuverPhase.Abandoned;
            state.AbandonReason = reason;
            return Done(ManeuverPhase.Abandoned, reason);
        }

        private static void LineEnd(in ManeuverInput input, out float x, out float z)
        {
            x = input.HasEnemyExtent ? input.EnemyEndX : input.EnemyAnchorX;
            z = input.HasEnemyExtent ? input.EnemyEndZ : input.EnemyAnchorZ;
        }

        // FlankMove's two checks (116929-117108) plus the march leg: a river on either leg, or a perceived enemy on
        // the inward corridor standing at least heightdifferencetoenemyunitanchored above the anchor.
        private static string FlankMoveCheck(in ManeuverInput input)
        {
            // The march leg counts only once its route result exists (MarchRouteKnown); a launched child keeps marching until then.
            if (input.MarchRouteKnown && input.MarchRouteMissing) return "no-route";
            if ((input.MarchRouteKnown && input.MarchLegHitsRiver) || input.InwardLegHitsRiver) return "river-on-leg";
            Span<float> heights = stackalloc float[1];
            heights[0] = input.MaxCorridorEnemyHeight;
            if (AnchoredHighGround(input.AnchorHeight, heights, input.CorridorEnemyCount > 0 ? 1 : 0, input.HeightDifferenceAnchored))
                return "anchored-high-ground";
            return null;
        }

        /// <summary>
        /// A child has arrived when its headquarters stands within ArrivalMeters plus <paramref name="slackMeters"/> of the point it
        /// was sent to: the group formation paths the HQ groupdepth + groupunitdistancetoback behind the waypoint (SetGroupFormation
        /// 92044-92058), so the HQ of a child that has reached its anchor never stands on the anchor.
        /// </summary>
        public static bool Arrived(float childX, float childZ, float targetX, float targetZ, float slackMeters)
        {
            return Distance(childX, childZ, targetX, targetZ) <= ArrivalMeters + Math.Max(0f, slackMeters);
        }

        /// <summary>
        /// The id of the Center child the enveloping wings wait on, or null. A child counts only if the zone assignment gave it the
        /// Center rect AND its effective role came from that rect (<see cref="DirectChildIntent.ZoneRectKind"/>): BC10 keeps a
        /// deployed reserve off its zone role, and a child that does not act as the Center never makes contact for the Center.
        /// With none, the offset counts from the play's activation. (DoubleEnvelopment's wings outrank its Center, so with two
        /// corps nobody holds it.)
        /// </summary>
        public static string CenterChildId(System.Collections.Generic.IReadOnlyList<CorpsAssignment> assignments,
            System.Collections.Generic.IReadOnlyList<DirectChildIntent> intents)
        {
            if (assignments == null || intents == null) return null;
            for (int a = 0; a < assignments.Count; a++)
            {
                if (assignments[a].RectKind != PlayRectKind.Center) continue;
                for (int i = 0; i < intents.Count; i++)
                    if (string.Equals(intents[i].ChildId, assignments[a].ChildId, StringComparison.Ordinal) && intents[i].ZoneRectKind == PlayRectKind.Center)
                        return assignments[a].ChildId;
            }
            return null;
        }

        // ---- the checks ----------------------------------------------------------------------

        /// <summary>
        /// FlankMove's river check (CheckTerrainLine with {4} at 2 m steps, 117011-117036) over the terrain ids
        /// sampled along a leg: false when any sample is river id 4 (fords and bridges are other ids).
        /// </summary>
        public static bool InwardLegClear(ReadOnlySpan<int> riverSamples, int count)
        {
            int n = Math.Min(Math.Max(count, 0), riverSamples.Length);
            for (int i = 0; i < n; i++)
                if (riverSamples[i] == BattleTerrainGrid.RiverId) return false;
            return true;
        }

        /// <summary>
        /// FlankMove's river check on the inward leg (anchor to the enemy line end), BC14 form: without an attack via the straight leg is
        /// sampled and read by <see cref="InwardLegClear"/> exactly as before; with one the leg is two sub-legs, anchor to the via and the via
        /// to the line end, and a river sample within <paramref name="footprintMeters"/> of the via is the crossing's own footprint and does not
        /// count. True only when a sub-leg samples open river elsewhere (the wing is then abandoned river-on-leg).
        /// </summary>
        public static bool InwardLegHitsRiver(BattleTerrainGrid g, float ax, float az, bool hasVia, float vx, float vz, float ex, float ez,
            float footprintMeters, int[] scratch)
        {
            if (g == null || scratch == null) return false;
            if (!hasVia) return !InwardLegClear(scratch, g.SampleLine(ax, az, ex, ez, InwardStepMeters, scratch));
            return SubLegHitsRiver(g, ax, az, vx, vz, vx, vz, footprintMeters, scratch) || SubLegHitsRiver(g, vx, vz, ex, ez, vx, vz, footprintMeters, scratch);
        }

        private static bool SubLegHitsRiver(BattleTerrainGrid g, float x0, float z0, float x1, float z1, float viaX, float viaZ, float footprintMeters, int[] scratch)
        {
            int n = g.SampleLine(x0, z0, x1, z1, InwardStepMeters, scratch);
            float length = Distance(x0, z0, x1, z1);
            float ux = length > 0f ? (x1 - x0) / length : 0f, uz = length > 0f ? (z1 - z0) / length : 0f;
            for (int i = 0; i < n && i < scratch.Length; i++)
            {
                if (scratch[i] != BattleTerrainGrid.RiverId) continue;
                float t = i * InwardStepMeters;
                if (Distance(x0 + ux * t, z0 + uz * t, viaX, viaZ) <= footprintMeters) scratch[i] = 0;
            }
            return !InwardLegClear(scratch, n);
        }

        /// <summary>
        /// The march leg judged on the route the child will actually walk: the polyline of NavMesh corners
        /// (<paramref name="xs"/>, <paramref name="zs"/>), each segment sampled every <paramref name="stepMeters"/> on the terrain grid;
        /// true when any sample is river id 4. A straight line from the child to the anchor can cross a river that the planned route
        /// crosses on a bridge or ford (ids 6 and 5), which are not river. A single corner is sampled as a point.
        /// </summary>
        public static bool RouteHitsRiver(BattleTerrainGrid grid, float[] xs, float[] zs, int count, float stepMeters, int[] scratch)
        {
            if (grid == null || xs == null || zs == null || scratch == null || count <= 0) return false;
            count = Math.Min(count, Math.Min(xs.Length, zs.Length));
            if (count == 1) return grid.IdAt(xs[0], zs[0]) == BattleTerrainGrid.RiverId;
            for (int c = 0; c + 1 < count; c++)
            {
                int n = grid.SampleLine(xs[c], zs[c], xs[c + 1], zs[c + 1], stepMeters, scratch);
                if (!InwardLegClear(scratch, n)) return true;
            }
            return grid.IdAt(xs[count - 1], zs[count - 1]) == BattleTerrainGrid.RiverId;
        }

        /// <summary>The march-leg check is re-run only when it can have changed (see <see cref="NeedsRouteRecheck"/>).</summary>
        public const double RouteRecheckHours = 0.05;
        /// <summary>The anchor moving by more than this is a new leg.</summary>
        public const float RouteAnchorMoveMeters = 50f;

        /// <summary>The last march-leg river check of one child.</summary>
        public struct MarchRouteCheck
        {
            public bool Valid;
            public double CheckedHours;
            public float AnchorX, AnchorZ;
            public bool HitsRiver;
            /// <summary>No complete NavMesh path: a result in its own right (the leg cannot be walked).</summary>
            public bool NoPath;

            // BC14: a leg through a crossing is planned as two half-queries, child to via and via to anchor, one per step. The first half is
            // staged here; the published result above is replaced only when the second half completes it.
            public bool HasVia;
            public float ViaX, ViaZ;
            public bool FirstDone, FirstHitsRiver, FirstNoPath;
        }

        /// <summary>
        /// Which query the march leg's next route plan is: 0 the whole leg (no via), 1 the child to the via, 2 the via to the anchor (the
        /// first half is staged and still current for this via).
        /// </summary>
        public static int HalfToPlan(in MarchRouteCheck last, bool hasVia, float viaX, float viaZ)
        {
            if (!hasVia) return 0;
            return last.FirstDone && last.HasVia && Distance(last.ViaX, last.ViaZ, viaX, viaZ) <= RouteAnchorMoveMeters ? 2 : 1;
        }

        /// <summary>
        /// The check after one planned query. A whole leg (half 0) is the result; the first half is staged beside the old result, so a
        /// recheck never blinds a launched child; the second half completes the combined result (a river or a missing path on either half).
        /// </summary>
        public static MarchRouteCheck ApplyHalf(in MarchRouteCheck last, int half, bool hitsRiver, bool noPath, double nowHours,
            float anchorX, float anchorZ, bool hasVia, float viaX, float viaZ)
        {
            MarchRouteCheck check = last;
            check.AnchorX = anchorX;
            check.AnchorZ = anchorZ;
            check.HasVia = hasVia;
            check.ViaX = viaX;
            check.ViaZ = viaZ;
            if (half == 1)
            {
                check.FirstDone = true;
                check.FirstHitsRiver = hitsRiver;
                check.FirstNoPath = noPath;
                return check;
            }
            check.Valid = true;
            check.CheckedHours = nowHours;
            check.HitsRiver = hitsRiver || (half == 2 && last.FirstHitsRiver);
            check.NoPath = noPath || (half == 2 && last.FirstNoPath);
            check.FirstDone = false;
            check.FirstHitsRiver = false;
            check.FirstNoPath = false;
            return check;
        }

        /// <summary>Whether the check must be recomputed for this leg (<see cref="NeedsRouteRecheck(in MarchRouteCheck, double, float, float)"/>), a staged half or a moved via included.</summary>
        public static bool NeedsRouteRecheck(in MarchRouteCheck last, double nowHours, float anchorX, float anchorZ, bool hasVia, float viaX, float viaZ)
        {
            if (NeedsRouteRecheck(in last, nowHours, anchorX, anchorZ)) return true;
            if (last.HasVia != hasVia) return true;
            if (hasVia && (last.FirstDone || Distance(last.ViaX, last.ViaZ, viaX, viaZ) > RouteAnchorMoveMeters)) return true;
            return false;
        }

        /// <summary>The last result as it applies to this leg: one whose anchor or via moved on is no result.</summary>
        public static MarchRouteCheck CheckForLeg(in MarchRouteCheck last, float anchorX, float anchorZ, bool hasVia, float viaX, float viaZ)
        {
            MarchRouteCheck check = CheckForAnchor(in last, anchorX, anchorZ);
            if (check.HasVia != hasVia || (hasVia && Distance(check.ViaX, check.ViaZ, viaX, viaZ) > RouteAnchorMoveMeters)) return default(MarchRouteCheck);
            return check;
        }

        /// <summary>
        /// Whether the march-leg check must be recomputed: there is none yet, the anchor moved (a new leg: the first march, the
        /// mirrored side), or <see cref="RouteRecheckHours"/> of doctrine time passed (the route and the child's position drift).
        /// Not every tick: a route plan is a NavMesh query.
        /// </summary>
        public static bool NeedsRouteRecheck(in MarchRouteCheck last, double nowHours, float anchorX, float anchorZ)
        {
            if (!last.Valid) return true;
            if (nowHours < last.CheckedHours || nowHours - last.CheckedHours >= RouteRecheckHours) return true;
            return Distance(last.AnchorX, last.AnchorZ, anchorX, anchorZ) > RouteAnchorMoveMeters;
        }

        /// <summary>The last result as it applies to this anchor: a result for a leg whose anchor moved on is no result.</summary>
        public static MarchRouteCheck CheckForAnchor(in MarchRouteCheck last, float anchorX, float anchorZ)
        {
            if (last.Valid && Distance(last.AnchorX, last.AnchorZ, anchorX, anchorZ) > RouteAnchorMoveMeters) return default(MarchRouteCheck);
            return last;
        }

        /// <summary>The fair order of route plans: a child with no result yet goes before any re-check, then the stalest result first.</summary>
        public static bool PlansBefore(in MarchRouteCheck a, in MarchRouteCheck b)
        {
            if (a.Valid != b.Valid) return !a.Valid;
            return a.CheckedHours < b.CheckedHours;
        }

        /// <summary>
        /// The child that gets this step's single route plan (an index into <paramref name="checks"/>), or -1 when none needs one.
        /// Among the children with <paramref name="needs"/> set the first in <see cref="PlansBefore"/> order wins, ties to the lower
        /// index. The bound: a step serves exactly one child that has no result, and a served child stays served, so N enveloping
        /// children each get their first route plan within N steps (DoubleEnvelopment's two wings: at the latest the second step),
        /// whatever the re-checks queue behind them.
        /// </summary>
        public static int PickRoutePlan(MarchRouteCheck[] checks, bool[] needs, int count)
        {
            if (checks == null || needs == null) return -1;
            count = Math.Min(count, Math.Min(checks.Length, needs.Length));
            int best = -1;
            for (int i = 0; i < count; i++)
            {
                if (!needs[i]) continue;
                if (best < 0 || PlansBefore(in checks[i], in checks[best])) best = i;
            }
            return best;
        }

        /// <summary>
        /// FlankMove's anchored-enemy check (117038-117050): any perceived enemy height at least
        /// <paramref name="heightDifference"/> above the anchor (the turn point). At exactly the difference it fails.
        /// </summary>
        public static bool AnchoredHighGround(float anchorHeight, ReadOnlySpan<float> enemyHeights, int count, float heightDifference)
        {
            int n = Math.Min(Math.Max(count, 0), enemyHeights.Length);
            for (int i = 0; i < n; i++)
                if (enemyHeights[i] - anchorHeight >= heightDifference) return true;
            return false;
        }

        // ---- geometry (axis frame) -------------------------------------------------------------

        /// <summary>
        /// The anchor a maneuver reads this step: the spec's own; after BC11's one mirrored try (<paramref name="flipped"/>) the mirror across the
        /// front axis, or, for a spec the plan projected mirrored (BC14), the authored anchor: the projection frame and the front axis need not
        /// coincide, so the fallback goes to the stored anchor and not to <see cref="MirrorAcrossAxis"/>.
        /// </summary>
        public static void AnchorFor(in TacticalFrontAxis axis, in ManeuverSpec spec, bool flipped, out float x, out float z)
        {
            x = spec.AnchorX;
            z = spec.AnchorZ;
            if (!flipped) return;
            if (spec.ProjectedMirrored)
            {
                x = spec.AuthoredAnchorX;
                z = spec.AuthoredAnchorZ;
                return;
            }
            MirrorAcrossAxis(in axis, spec.AnchorX, spec.AnchorZ, out x, out z);
        }

        /// <summary>True when the rect anchor lies on the right of the army's centre line (the lateral centre of its extent).</summary>
        public static bool RectIsRight(in TacticalFrontAxis axis, float anchorX, float anchorZ)
        {
            float centre = axis.IsValid ? (axis.LateralMin + axis.LateralMax) * 0.5f : 0f;
            return axis.LateralOf(anchorX, anchorZ) >= centre;
        }

        /// <summary>
        /// The perceived enemy line end on a side, at the enemy's depth: the axis's EnemyLateralMax (right) or
        /// EnemyLateralMin (left) laid out from the origin at the depth of the play's enemy anchor. False with no enemy extent.
        /// </summary>
        public static bool EnemyLineEnd(in TacticalFrontAxis axis, float enemyAnchorX, float enemyAnchorZ, bool rightSide, out float x, out float z)
        {
            x = enemyAnchorX;
            z = enemyAnchorZ;
            if (!axis.IsValid || !axis.HasEnemyExtent) return false;
            float depth = Forward(in axis, enemyAnchorX, enemyAnchorZ);
            float lateral = rightSide ? axis.EnemyLateralMax : axis.EnemyLateralMin;
            Lay(in axis, depth, lateral, out x, out z);
            return true;
        }

        /// <summary>
        /// The DeepRaid's outflank point: OutflankMarginMeters beyond the perceived enemy line end on the
        /// less-pressured side (the enemy half with the smaller perceived strength; a tie keeps the rect's own side), at
        /// <paramref name="depth"/> along the forward axis.
        /// </summary>
        public static bool OutflankPoint(in TacticalFrontAxis axis, float depth, float leftPressure, float rightPressure, bool rectIsRight,
            out float x, out float z)
        {
            x = 0f;
            z = 0f;
            if (!axis.IsValid || !axis.HasEnemyExtent) return false;
            bool right = leftPressure == rightPressure ? rectIsRight : rightPressure < leftPressure;
            float lateral = right ? axis.EnemyLateralMax + OutflankMarginMeters : axis.EnemyLateralMin - OutflankMarginMeters;
            Lay(in axis, depth, lateral, out x, out z);
            return true;
        }

        /// <summary>The perceived enemy strength (Visible full, Remembered x confidence) on each half of the enemy extent.</summary>
        public static void EnemyPressureSplit(BattleSnapshot s, in TacticalFrontAxis axis, out float left, out float right)
        {
            left = 0f;
            right = 0f;
            if (s == null || !axis.IsValid || !axis.HasEnemyExtent) return;
            float mid = (axis.EnemyLateralMin + axis.EnemyLateralMax) * 0.5f;
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].IsReinforcement) continue;
                float weight = obs[k].State == ObservationState.Visible ? obs[k].Strength : obs[k].Strength * obs[k].Confidence01;
                if (axis.LateralOf(obs[k].X, obs[k].Z) < mid) left += weight;
                else right += weight;
            }
        }

        /// <summary>Mirrors a point across the army's centre line (its lateral offset from the origin flips sign).</summary>
        public static void MirrorAcrossAxis(in TacticalFrontAxis axis, float x, float z, out float mirroredX, out float mirroredZ)
        {
            mirroredX = x;
            mirroredZ = z;
            if (!axis.IsValid) return;
            Lay(in axis, Forward(in axis, x, z), -axis.LateralOf(x, z), out mirroredX, out mirroredZ);
        }

        /// <summary>
        /// The perceived enemies on the inward leg's corridor (leg length plus CorridorExtraMeters, CorridorWidthMeters
        /// wide; Visible, or Remembered at RememberedConfidence and over): how many, and the highest terrain height
        /// among them (heightAt is the runtime's BattlefieldSetup.GetTerrainHeight; terrain is public, the enemy position
        /// is perceived). maxHeight is float.MinValue with none.
        /// </summary>
        public static int CorridorEnemies(BattleSnapshot s, float anchorX, float anchorZ, float endX, float endZ,
            Func<float, float, float> heightAt, out float maxHeight)
        {
            maxHeight = float.MinValue;
            if (s == null || heightAt == null) return 0;
            float dx = endX - anchorX, dz = endZ - anchorZ;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 1f) return 0;
            float ux = dx / length, uz = dz / length;
            float reach = length + CorridorExtraMeters;
            float half = CorridorWidthMeters * 0.5f;
            int count = 0;
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].IsReinforcement) continue;
                if (obs[k].State != ObservationState.Visible && obs[k].Confidence01 < RememberedConfidence) continue;
                float px = obs[k].X - anchorX, pz = obs[k].Z - anchorZ;
                float along = px * ux + pz * uz;
                if (along < 0f || along > reach) continue;
                float across = px * -uz + pz * ux;
                if (Math.Abs(across) > half) continue;
                count++;
                float height = heightAt(obs[k].X, obs[k].Z);
                if (height > maxHeight) maxHeight = height;
            }
            return count;
        }

        // ---- lanes and nodes ------------------------------------------------------------------

        /// <summary>The lane offset of node <paramref name="index"/> of <paramref name="count"/> along the rect's lateral axis, centred on the anchor.</summary>
        public static float LaneOffset(int index, int count)
        {
            if (count <= 1 || index < 0) return 0f;
            return (index - (count - 1) * 0.5f) * LaneMeters;
        }

        /// <summary>"child-&lt;id&gt;" (DirectChildDiscovery) to the command-tree node id "node-&lt;id&gt;" (CommandTreeIntentAllocator).</summary>
        public static string NodeIdOfChild(string childId)
        {
            const string childPrefix = "child-";
            if (string.IsNullOrEmpty(childId) || !childId.StartsWith(childPrefix, StringComparison.Ordinal)) return string.Empty;
            return "node-" + childId.Substring(childPrefix.Length);
        }

        /// <summary>
        /// Expands the children's maneuver orders to every node of each child's subtree (a node whose
        /// CommandNodeIntent.SourceNodeId is the child's node): task, lane target on the rect's lateral axis, heading.
        /// A Hold carries no target (the child stands where it is). Phases that do not override add nothing.
        /// </summary>
        internal static ManeuverNodeTable ExpandToNodes(System.Collections.Generic.IReadOnlyList<ChildManeuver> maneuvers,
            System.Collections.Generic.IReadOnlyList<CommandNodeIntent> nodeIntents,
            System.Collections.Generic.IReadOnlyList<DirectChildIntent> directChildIntents = null)
        {
            var table = new ManeuverNodeTable();
            if (nodeIntents == null) return table;
            AddGuards(table, directChildIntents, nodeIntents);
            if (maneuvers == null) return table;
            for (int m = 0; m < maneuvers.Count; m++)
            {
                ChildManeuver maneuver = maneuvers[m];
                if (!maneuver.Order.Overrides) continue;
                string source = NodeIdOfChild(maneuver.ChildId);
                if (source.Length == 0) continue;
                // The child node itself is the unit that is moved (direct children come from unitsused): it takes the anchor. Its
                // direct sub-nodes take the 180 m lanes along the lateral axis, centred on the anchor; deeper nodes take the anchor
                // (a regiment-level group follows its parent's formation).
                int childDepth = int.MaxValue;
                for (int i = 0; i < nodeIntents.Count; i++)
                {
                    if (!string.Equals(nodeIntents[i].SourceNodeId, source, StringComparison.Ordinal)) continue;
                    if (string.Equals(nodeIntents[i].NodeId, source, StringComparison.Ordinal)) { childDepth = nodeIntents[i].Depth; break; }
                    if (nodeIntents[i].Depth < childDepth) childDepth = nodeIntents[i].Depth;
                }
                int lanes = 0;
                for (int i = 0; i < nodeIntents.Count; i++)
                    if (string.Equals(nodeIntents[i].SourceNodeId, source, StringComparison.Ordinal) && nodeIntents[i].Depth == childDepth + 1) lanes++;
                int lane = 0;
                for (int i = 0; i < nodeIntents.Count; i++)
                {
                    if (!string.Equals(nodeIntents[i].SourceNodeId, source, StringComparison.Ordinal)) continue;
                    bool hold = maneuver.Order.Phase == ManeuverPhase.Hold;
                    float offset = !hold && nodeIntents[i].Depth == childDepth + 1 ? LaneOffset(lane++, lanes) : 0f;
                    table.Add(new ManeuverNodeEntry(
                        nodeIntents[i].NodeId, maneuver.Order.Phase, maneuver.Order.Task, !hold,
                        maneuver.Order.TargetX + maneuver.LateralX * offset, maneuver.Order.TargetZ + maneuver.LateralZ * offset,
                        maneuver.Order.Heading, maneuver.Order.Reason,
                        maneuver.Order.HasVia, maneuver.Order.ViaX, maneuver.Order.ViaZ));
                }
            }
            return table;
        }

        // BC14 requirement-defender-crossing-release: a reserve released to a crossing stands at its guard point facing the crossing. Every node of
        // the child takes the guard point as its primary target (a guard has no lanes: the groups below follow their parent's formation) and the
        // guard heading, through the same maneuver override the doctrine build already applies; the child's role gives the GuardFlank task.
        private static void AddGuards(ManeuverNodeTable table, System.Collections.Generic.IReadOnlyList<DirectChildIntent> children,
            System.Collections.Generic.IReadOnlyList<CommandNodeIntent> nodeIntents)
        {
            if (children == null) return;
            for (int c = 0; c < children.Count; c++)
            {
                if (!children[c].HasGuardPoint) continue;
                string source = NodeIdOfChild(children[c].ChildId);
                if (source.Length == 0) continue;
                for (int i = 0; i < nodeIntents.Count; i++)
                {
                    if (!string.Equals(nodeIntents[i].SourceNodeId, source, StringComparison.Ordinal)) continue;
                    table.Add(new ManeuverNodeEntry(nodeIntents[i].NodeId, ManeuverPhase.March, CommandTaskType.GuardFlank, true,
                        children[c].Guard.X, children[c].Guard.Z, children[c].Guard.Heading, "guard-crossing", isGuard: true));
                }
            }
        }

        // A hash of what the doctrine build reads from the maneuvers, so a phase change triggers a ledger tick.
        internal static int Hash(System.Collections.Generic.IReadOnlyList<ChildManeuver> maneuvers)
        {
            if (maneuvers == null) return 0;
            unchecked
            {
                int h = 17;
                for (int m = 0; m < maneuvers.Count; m++)
                {
                    ChildManeuver c = maneuvers[m];
                    h = h * 31 + (c.ChildId?.GetHashCode() ?? 0);
                    h = h * 31 + (int)c.Order.Phase;
                    h = h * 31 + (int)c.Order.Task;
                    // A hold's target is the child's own (drifting) position and is never read: it stays out of the hash.
                    if (c.Order.Phase != ManeuverPhase.Hold)
                    {
                        h = h * 31 + (int)(c.Order.TargetX / 50f);
                        h = h * 31 + (int)(c.Order.TargetZ / 50f);
                    }
                    h = h * 31 + (int)(c.Order.Heading / 5f);
                    // BC14: a via set, moved or cleared (it is dropped once the child has crossed) re-stores the orders.
                    h = h * 31 + (c.Order.HasVia ? 1 : 0);
                    if (c.Order.HasVia)
                    {
                        h = h * 31 + (int)(c.Order.ViaX / 50f);
                        h = h * 31 + (int)(c.Order.ViaZ / 50f);
                    }
                }
                return h;
            }
        }

        private static float Forward(in TacticalFrontAxis axis, float x, float z)
        {
            return (x - axis.OriginX) * axis.ForwardX + (z - axis.OriginZ) * axis.ForwardZ;
        }

        private static void Lay(in TacticalFrontAxis axis, float depth, float lateral, out float x, out float z)
        {
            x = axis.OriginX + axis.ForwardX * depth + axis.RightX * lateral;
            z = axis.OriginZ + axis.ForwardZ * depth + axis.RightZ * lateral;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
