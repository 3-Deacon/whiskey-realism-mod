using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Orchestrator.Plays;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure deterministic role allocator. Given the army CO's plan, commander
    /// personality, and a parallel evidence list (one entry per registered child
    /// in registration order), produces a parallel list of DirectChildIntent
    /// with role assignments per spec rules. No Unity types.
    /// </summary>
    /// <summary>
    /// Army-level inputs for releasing Reserve-role children (T15): the army's
    /// current global odds, compared with the plan's ReserveCommitTriggerOdds.
    /// </summary>
    public readonly struct ReserveReleaseContext
    {
        public ReserveReleaseContext(float globalOdds)
        {
            GlobalOdds = float.IsNaN(globalOdds) || float.IsInfinity(globalOdds) || globalOdds < 0f ? 0f : globalOdds;
        }

        public float GlobalOdds { get; }
    }

    /// <summary>Main-role hysteresis memory kept by the orchestrator (T16).</summary>
    public sealed class MainRoleHysteresisState
    {
        public string IncumbentMainId { get; internal set; } = string.Empty;
        public string ChallengerId { get; internal set; } = string.Empty;
        public int ChallengerStreak { get; internal set; }
    }

    public static class DirectChildAllocator
    {
        /// <summary>
        /// Consecutive allocation cycles a different child must win Main
        /// before the incumbent hands it over (T16). Main used to hop between
        /// corps every tick on contact-flag / strength-bucket noise, rebuilding
        /// the leaf cascade and re-issuing #61 orders.
        /// </summary>
        public const int MainChallengeCyclesRequired = 3;

        // Role budgets + thresholds (2026-05-22 tuning, was producing
        // "main alone + 4×RefuseRight" allocations in smoke):
        //
        // - FlankExposureRefuseThreshold raised 2 → 3. Bucket 2 = moderate
        //   exposure; locking moderate-exposure brigades into static flank
        //   duty was over-allocating Refuse roles. Only HIGH (bucket 3+)
        //   flank exposure now locks a brigade into RefuseLeft/Right.
        //
        // - SupportMainAdjacencyRange is 1 (integration, 2026-10-15). It was
        //   widened to 2 on 2026-05-22 when sectors were per-group indices;
        //   sectors are now the TacticalFrontAxis lateral bands (5), where 2
        //   from the center band is every band, so "adjacent" meant nothing.
        //   The fall-through tier still fills the SupportMain budget.
        //
        // - MaxRefuseLeftCount / MaxRefuseRightCount cap 1 each. Beyond
        //   the cap, the brigade re-checks higher-priority roles
        //   (SupportMain / Reserve) instead of stacking on flank-guard.
        //
        // - MaxMainCount = 1, MaxSupportMainCount = 2. Prevents the
        //   smoke-observed "multiple Main roles assigned" symptom by
        //   enforcing role budgets after primary allocation.
        private const int FlankExposureRefuseThreshold = 3;
        internal const int SupportMainAdjacencyRange = 1;
        private const int MaxRefuseLeftCount = 1;
        private const int MaxRefuseRightCount = 1;
        private const int MaxSupportMainCount = 2;

        /// <summary>
        /// Zone-aware allocation overload (2026-05-23 / Stage 2 G). When
        /// <paramref name="zoneAssignments"/> is non-empty, each child whose
        /// <see cref="DirectChildSnapshot.ChildId"/> matches a row in the list
        /// gets the role implied by the rect kind (Main for LeftWing/RightWing,
        /// Fix for Center, Defender via Refuse for CenterHold/LeftFlank/etc.).
        /// Children without a zone assignment fall through to the existing
        /// evidence-based path.
        ///
        /// This is the integration point for <see cref="PlayCorpsAssignment"/>:
        /// the runtime computes corps-to-rect assignments from a play template,
        /// then passes them here so role allocation reflects the play geometry
        /// rather than per-child evidence alone. The result is that vacant /
        /// distant brigades that previously fell through to Reserve now get
        /// coherent zone-scoped roles when a play is active.
        /// </summary>
        public static IReadOnlyList<DirectChildIntent> AllocateWithZones(
            TacticalBattlePlan plan,
            PersonalityVector personality,
            IReadOnlyList<DirectChildSnapshot> snapshots,
            IReadOnlyList<DirectChildEvidence> evidence,
            IReadOnlyList<TacticalIntentModel> perChildEnemyIntent,
            IReadOnlyList<CorpsAssignment> zoneAssignments,
            ReserveReleaseContext release = default(ReserveReleaseContext))
        {
            if (snapshots == null || evidence == null || snapshots.Count != evidence.Count)
                return Array.Empty<DirectChildIntent>();
            if (perChildEnemyIntent == null || perChildEnemyIntent.Count != snapshots.Count)
            {
                var empty = new TacticalIntentModel[snapshots.Count];
                for (int i = 0; i < empty.Length; i++)
                    empty[i] = new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>());
                perChildEnemyIntent = empty;
            }

            var roles = new DirectChildRole[snapshots.Count];
            for (int i = 0; i < roles.Length; i++) roles[i] = DirectChildRole.Unknown;

            // Zone-based assignment FIRST — geometric play overrides evidence.
            // Build childId → assignment lookup.
            bool hasZones = zoneAssignments != null && zoneAssignments.Count > 0;
            Dictionary<string, CorpsAssignment> zoneByChildId = null;
            if (hasZones)
            {
                zoneByChildId = new Dictionary<string, CorpsAssignment>(zoneAssignments.Count);
                for (int i = 0; i < zoneAssignments.Count; i++)
                {
                    var za = zoneAssignments[i];
                    if (string.IsNullOrEmpty(za.ChildId)) continue;
                    if (!zoneByChildId.ContainsKey(za.ChildId))
                        zoneByChildId[za.ChildId] = za;
                }
                for (int i = 0; i < snapshots.Count; i++)
                {
                    if (snapshots[i].ChildId == null) continue;
                    if (zoneByChildId.TryGetValue(snapshots[i].ChildId, out var za))
                    {
                        roles[i] = PlayCorpsAssignment.RoleForRectKind(za.RectKind);
                    }
                }
            }

            float allocAggression01 = (personality.Aggression + 1f) * 0.5f;
            if (allocAggression01 < 0f) allocAggression01 = 0f;
            if (allocAggression01 > 1f) allocAggression01 = 1f;

            // Only pick a Main from evidence if zone assignments didn't already
            // place one (zone-based Main takes precedence — that's the entire
            // point of the geometric layer).
            bool zoneAssignedMain = false;
            if (hasZones)
            {
                for (int i = 0; i < roles.Length; i++)
                    if (roles[i] == DirectChildRole.Main) { zoneAssignedMain = true; break; }
            }

            int mainIdx = -1;
            if (!zoneAssignedMain)
            {
                mainIdx = PickMainEffort(plan, snapshots, evidence, allocAggression01);
                if (mainIdx >= 0 && !ShouldFallbackUnderSevereOvermatch(evidence[mainIdx], perChildEnemyIntent[mainIdx]))
                    roles[mainIdx] = DirectChildRole.Main;
            }

            // Role budget counters (2026-05-22). Enforce maximums during
            // allocation so we never over-assign SupportMain/Refuse roles.
            // Brigades that hit a budgeted cap fall through to the next
            // candidate role.
            int supportMainCount = 0;
            int refuseLeftCount = 0;
            int refuseRightCount = 0;

            for (int i = 0; i < snapshots.Count; i++)
            {
                if (roles[i] != DirectChildRole.Unknown) continue;
                var ev = evidence[i];

                if (ShouldFallbackUnderSevereOvermatch(ev, perChildEnemyIntent[i]))
                {
                    roles[i] = DirectChildRole.Fallback;
                    continue;
                }

                if (Contains(plan.FixingSectors, ev.PrimarySector) && ev.ContactFlag)
                {
                    roles[i] = DirectChildRole.Fix;
                    continue;
                }

                if (mainIdx >= 0
                    && IsAdjacentSector(ev.PrimarySector, evidence[mainIdx].PrimarySector)
                    && ev.OwnStrengthBucket >= 1
                    && ev.FlankExposureBucket < FlankExposureRefuseThreshold
                    && supportMainCount < MaxSupportMainCount)
                {
                    roles[i] = DirectChildRole.SupportMain;
                    supportMainCount++;
                    continue;
                }

                if (Contains(plan.ScreeningSectors, ev.PrimarySector) && ev.OwnStrengthBucket <= 1 && ev.EnemyStrengthBucket <= 1)
                {
                    roles[i] = DirectChildRole.Screen;
                    continue;
                }

                if (ev.FlankExposureBucket >= FlankExposureRefuseThreshold)
                {
                    int mainSector = mainIdx >= 0 ? evidence[mainIdx].PrimarySector : plan.MainEffortSector;
                    bool wantLeft = ev.PrimarySector < mainSector;
                    if (wantLeft && refuseLeftCount < MaxRefuseLeftCount)
                    {
                        roles[i] = DirectChildRole.RefuseLeft;
                        refuseLeftCount++;
                        continue;
                    }
                    if (!wantLeft && refuseRightCount < MaxRefuseRightCount)
                    {
                        roles[i] = DirectChildRole.RefuseRight;
                        refuseRightCount++;
                        continue;
                    }
                    // Refuse budget exhausted — fall through to lower-priority
                    // candidates (Reserve / Screen / Fallback) so we don't
                    // stack the flank-guard role on every brigade with moderate
                    // flank exposure.
                }

                if (ev.OwnStrengthBucket >= 2 && !ev.ContactFlag)
                {
                    roles[i] = DirectChildRole.Reserve;
                    continue;
                }

                if (ev.ContactFlag
                    && ev.EnemyStrengthBucket > ev.OwnStrengthBucket + 1
                    && perChildEnemyIntent[i].PrimaryIntent == InferredIntent.Attack)
                {
                    // Brigade is engaged AND severely outnumbered AND enemy
                    // is actively attacking → orderly fallback.
                    // Without ContactFlag, brigade stays in line (falls
                    // through to Reserve / SupportMain default).
                    roles[i] = DirectChildRole.Fallback;
                    continue;
                }

                // Fall-through default — TIERED (2026-05-23 G3 tightening):
                //   1) If still room in SupportMain budget AND brigade has
                //      meaningful strength → SupportMain (joins maneuver).
                //   2) Otherwise → Reserve. Even weak / far brigades get a
                //      coherent role at the cascade level. Reserve maps to
                //      CommandNodeRole.Reserve → CommandTaskType.ReserveWait;
                //      vanilla's macroai-driven AdjustGroupAIStance (per F
                //      sync) then writes the appropriate ai_stance (defend /
                //      screen / etc.) to form a line of battle if defense is
                //      the doctrine output.
                //
                // The prior fall-through left under-strength brigades as
                // Unknown — which mapped to CommandTaskType.FormUp and
                // produced incoherent behavior in smoke (3/5 CSA brigades at
                // sectors >2 from main effort logged as Unknown:7/8/9 in the
                // 2026-05-23 battle telemetry).
                if (mainIdx >= 0 && supportMainCount < MaxSupportMainCount && ev.OwnStrengthBucket >= 1)
                {
                    roles[i] = DirectChildRole.SupportMain;
                    supportMainCount++;
                    continue;
                }
                roles[i] = DirectChildRole.Reserve;
            }

            var axisOverride = ReleaseReserves(plan, evidence, roles, release);

            var intents = new DirectChildIntent[snapshots.Count];
            float aggressionBias01 = (personality.Aggression + 1f) * 0.5f;
            for (int i = 0; i < snapshots.Count; i++)
            {
                var snap = snapshots[i];
                var ev = evidence[i];
                var role = roles[i];
                int axisSector = axisOverride[i] >= 0
                    ? axisOverride[i]
                    : role == DirectChildRole.Main || role == DirectChildRole.SupportMain
                        ? (mainIdx >= 0 ? evidence[mainIdx].PrimarySector : plan.MainEffortSector)
                        : ev.PrimarySector;
                var axis = AxisFor(role);
                float supportPriority = SupportPriorityFor(role, ev);
                intents[i] = new DirectChildIntent(
                    snap.ChildId,
                    snap.RawUnitTyp,
                    snap.EffectiveCommandLevel,
                    snap.DisplayName,
                    ev.PrimarySector,
                    role,
                    axis,
                    axisSector,
                    supportPriority,
                    aggressionBias01,
                    perChildEnemyIntent[i]);
            }
            return intents;
        }

        /// <summary>
        /// Reserve release as a ROLE change (T15). The reserve gate
        /// (TacticalReserveCommitGate) denies vanilla CheckUseOfReserves for
        /// Reserve-role children, and nothing ever changed that role. A Reserve
        /// child is released when:
        ///   1. a sector within support range is in crisis (own line engaged
        ///      and outmatched by two buckets) -> Fix on the crisis band;
        ///   2. the plan is in Exploit -> SupportMain behind the main effort;
        ///   3. global odds reach the plan's ReserveCommitTriggerOdds ->
        ///      SupportMain.
        /// Returns per-child axis-sector overrides (-1 = none).
        /// </summary>
        private static int[] ReleaseReserves(
            TacticalBattlePlan plan,
            IReadOnlyList<DirectChildEvidence> evidence,
            DirectChildRole[] roles,
            ReserveReleaseContext release)
        {
            var axisOverride = new int[roles.Length];
            for (int i = 0; i < axisOverride.Length; i++) axisOverride[i] = -1;

            bool exploit = plan.Phase == BattlePhase.Exploit;
            bool oddsReached = plan.ReserveCommitTriggerOdds > 0f && release.GlobalOdds >= plan.ReserveCommitTriggerOdds;

            for (int i = 0; i < roles.Length; i++)
            {
                if (roles[i] != DirectChildRole.Reserve) continue;

                int crisisSector = -1;
                for (int j = 0; j < roles.Length; j++)
                {
                    if (j == i) continue;
                    var other = evidence[j];
                    if (!other.ContactFlag || other.EnemyStrengthBucket <= other.OwnStrengthBucket + 1) continue;
                    if (!IsAdjacentSector(other.PrimarySector, evidence[i].PrimarySector)) continue;
                    crisisSector = other.PrimarySector;
                    break;
                }

                if (crisisSector >= 0)
                {
                    roles[i] = DirectChildRole.Fix;
                    axisOverride[i] = crisisSector;
                }
                else if (exploit || oddsReached)
                {
                    roles[i] = DirectChildRole.SupportMain;
                }
            }
            return axisOverride;
        }

        /// <summary>
        /// Main-role hysteresis (T16): keep the incumbent Main unless the same
        /// challenger has won Main for <see cref="MainChallengeCyclesRequired"/>
        /// consecutive allocations, or the incumbent is gone / falling back.
        /// While held, the incumbent and challenger swap roles; SupportMain
        /// children keep supporting the incumbent's sector.
        /// </summary>
        public static IReadOnlyList<DirectChildIntent> StabilizeMain(
            IReadOnlyList<DirectChildIntent> intents,
            MainRoleHysteresisState state)
        {
            if (intents == null || state == null) return intents;
            int newMain = -1, incumbent = -1;
            for (int i = 0; i < intents.Count; i++)
            {
                if (newMain < 0 && intents[i].Role == DirectChildRole.Main) newMain = i;
                if (intents[i].ChildId == state.IncumbentMainId) incumbent = i;
            }
            if (newMain < 0) return intents;

            string newId = intents[newMain].ChildId;
            bool incumbentUsable = incumbent >= 0 && intents[incumbent].Role != DirectChildRole.Fallback;
            if (newId == state.IncumbentMainId || !incumbentUsable)
            {
                Accept(state, newId);
                return intents;
            }

            if (state.ChallengerId == newId) state.ChallengerStreak++;
            else { state.ChallengerId = newId; state.ChallengerStreak = 1; }
            if (state.ChallengerStreak >= MainChallengeCyclesRequired)
            {
                Accept(state, newId);
                return intents;
            }

            var held = new DirectChildIntent[intents.Count];
            int keepSector = intents[incumbent].PrimarySector;
            var challenger = intents[newMain];
            var inc = intents[incumbent];
            for (int i = 0; i < intents.Count; i++)
            {
                var x = intents[i];
                if (i == incumbent)
                    held[i] = With(x, DirectChildRole.Main, challenger.Axis, keepSector, challenger.SupportPriority01);
                else if (i == newMain)
                    held[i] = With(x, inc.Role, inc.Axis, inc.Role == DirectChildRole.SupportMain ? keepSector : inc.AxisSector, inc.SupportPriority01);
                else if (x.Role == DirectChildRole.SupportMain)
                    held[i] = With(x, x.Role, x.Axis, keepSector, x.SupportPriority01);
                else
                    held[i] = x;
            }
            return held;
        }

        private static void Accept(MainRoleHysteresisState state, string mainId)
        {
            state.IncumbentMainId = mainId ?? string.Empty;
            state.ChallengerId = string.Empty;
            state.ChallengerStreak = 0;
        }

        private static DirectChildIntent With(DirectChildIntent x, DirectChildRole role, DirectChildAxis axis, int axisSector, float support)
            => new DirectChildIntent(x.ChildId, x.RawUnitTyp, x.EffectiveCommandLevel, x.DisplayName, x.PrimarySector,
                role, axis, axisSector, support, x.AggressionBias01, x.EnemyIntent);

        /// <summary>
        /// Aggression threshold above which the local-odds gate is bypassed.
        /// Lee/Jackson/Hood (aggression ≥ 0.7) commit Main against locally
        /// unfavorable odds — the historical "audacity" pattern that produced
        /// Chancellorsville-style envelopments. Cautious/mid-band commanders
        /// require local own ≥ enemy before assigning Main effort, so we don't
        /// dispatch a single brigade alone at a clearly superior local force.
        /// </summary>
        public const float LocalOddsBypassAggressionThreshold = 0.7f;

        private static int PickMainEffort(
            TacticalBattlePlan plan,
            IReadOnlyList<DirectChildSnapshot> snaps,
            IReadOnlyList<DirectChildEvidence> ev,
            float commanderAggression01)
        {
            bool aggressiveOverride = commanderAggression01 >= LocalOddsBypassAggressionThreshold;

            int best = -1;
            int bestScore = -1;
            for (int i = 0; i < snaps.Count; i++)
            {
                if (ev[i].PrimarySector != plan.MainEffortSector) continue;
                // Local-odds gate (2026-05-22): cautious/mid-band commanders
                // refuse Main assignment to a brigade that is locally
                // outnumbered. Without this gate, the planned MainEffortSector
                // brigade gets Main regardless of local force ratio — observed
                // smoke pattern was "3rd Brigade attacks superior force alone".
                // Aggressive commanders (≥ 0.7) bypass: Hood's frontal-assault
                // pattern remains intact.
                if (!aggressiveOverride && ev[i].EnemyStrengthBucket > ev[i].OwnStrengthBucket) continue;

                int score = ev[i].OwnStrengthBucket * Math.Max(1, 4 - ev[i].FlankExposureBucket);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            if (best >= 0) return best;

            best = PickFallbackMainEffort(plan, snaps, ev);
            if (best >= 0) return best;

            return PickAggressiveProvisionalMainEffort(plan, snaps, ev, commanderAggression01);
        }

        private static int PickFallbackMainEffort(
            TacticalBattlePlan plan,
            IReadOnlyList<DirectChildSnapshot> snaps,
            IReadOnlyList<DirectChildEvidence> ev)
        {
            int best = -1;
            int bestScore = -1;
            for (int i = 0; i < snaps.Count; i++)
            {
                if (!snaps[i].Active) continue;
                if (Contains(plan.FixingSectors, ev[i].PrimarySector)) continue;
                if (Contains(plan.ScreeningSectors, ev[i].PrimarySector)) continue;
                if (!ev[i].ContactFlag && ev[i].Confidence01 < 0.6f) continue;
                if (ev[i].EnemyStrengthBucket > ev[i].OwnStrengthBucket) continue;

                int contactScore = ev[i].ContactFlag ? 10 : 0;
                int confidenceScore = (int)Math.Round(ev[i].Confidence01 * 4f);
                int score = ev[i].OwnStrengthBucket * 8
                    - ev[i].EnemyStrengthBucket * 2
                    - ev[i].FlankExposureBucket * 2
                    + contactScore
                    + confidenceScore;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        private static int PickAggressiveProvisionalMainEffort(
            TacticalBattlePlan plan,
            IReadOnlyList<DirectChildSnapshot> snaps,
            IReadOnlyList<DirectChildEvidence> ev,
            float commanderAggression01)
        {
            if (!AllowsProvisionalAttackAxis(plan)) return -1;
            bool aggressiveOverride = commanderAggression01 >= LocalOddsBypassAggressionThreshold;

            int best = -1;
            int bestScore = -1;
            for (int i = 0; i < snaps.Count; i++)
            {
                if (!snaps[i].Active) continue;
                if (Contains(plan.FixingSectors, ev[i].PrimarySector)) continue;
                if (Contains(plan.ScreeningSectors, ev[i].PrimarySector)) continue;
                // Provisional attack axis allows EnemyBucket = OwnBucket+1 only
                // for aggressive commanders. Cautious/mid-band commanders gate
                // at the same parity rule used in the preferred path —
                // otherwise mid-band commanders pile into 1.5×-disadvantage
                // sectors via the provisional fallback.
                int maxAcceptableEnemyOver = aggressiveOverride ? 1 : 0;
                if (ev[i].EnemyStrengthBucket > ev[i].OwnStrengthBucket + maxAcceptableEnemyOver) continue;

                int distancePenalty = Math.Abs(ev[i].PrimarySector - plan.MainEffortSector);
                int contactScore = ev[i].ContactFlag ? 4 : 0;
                int confidenceScore = (int)Math.Round(ev[i].Confidence01 * 4f);
                int score = ev[i].OwnStrengthBucket * 8
                    - ev[i].EnemyStrengthBucket * 2
                    - ev[i].FlankExposureBucket
                    - distancePenalty
                    + contactScore
                    + confidenceScore;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        private static bool AllowsProvisionalAttackAxis(TacticalBattlePlan plan)
        {
            if (plan.Phase == BattlePhase.Withdraw || plan.Phase == BattlePhase.Consolidate) return false;
            switch (plan.PlanId)
            {
                case BattlePlanId.FrontalShockAssault:
                case BattlePlanId.ForcedFrontalAttack:
                case BattlePlanId.GenericAggressive:
                case BattlePlanId.GenericDesperate:
                case BattlePlanId.CavalryRaid:
                    return true;
                default:
                    return false;
            }
        }

        private static bool Contains(int[] arr, int val)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == val) return true;
            return false;
        }

        // Sector adjacency (front-axis bands) for SupportMain eligibility and
        // reserve crisis relief: within SupportMainAdjacencyRange of the band.
        private static bool IsAdjacentSector(int s, int main) => Math.Abs(s - main) <= SupportMainAdjacencyRange;

        /// <summary>
        /// True when a brigade is severely outnumbered AND actively engaged,
        /// such that retreating (Fallback) is preferable to holding the line.
        /// Outnumbered-but-not-in-contact brigades stay in formation as
        /// Reserve / Defender — the Union must form a line of battle, not
        /// scatter as soon as they see a massed enemy.
        ///
        /// Tightened 2026-05-22 (user observation: Union side wasn't forming
        /// a line at all because every brigade was logging FallBackToLine /
        /// FallbackGuard). Previous broken behavior:
        ///   - Confidence01 ≥ 0.9 alone triggered activePressure — visibility
        ///     of a static enemy was being treated as an attack.
        ///   - largeEnemyOvermatch fired at EnemyBucket=4 vs OwnBucket≤3
        ///     (only 1 bucket diff) — that's parity, not severe overmatch.
        /// Corrected behavior:
        ///   - activePressure requires enemy intent = Attack (visibility no
        ///     longer counts).
        ///   - largeEnemyOvermatch requires actual bucket diff ≥ 2.
        ///   - ContactFlag required — outnumbered but distant enemies are
        ///     defended against in line, not retreated from preemptively.
        /// </summary>
        private static bool ShouldFallbackUnderSevereOvermatch(
            DirectChildEvidence evidence,
            TacticalIntentModel enemyIntent)
        {
            // Outnumbered + not yet in contact = form line and defend.
            // Without contact, the orchestrator cannot know whether the enemy
            // is closing on this brigade or another sector. Falling back
            // preemptively scatters the army.
            if (!evidence.ContactFlag) return false;

            bool severeBucketMismatch = evidence.EnemyStrengthBucket > evidence.OwnStrengthBucket + 1;
            bool largeEnemyOvermatch = evidence.EnemyStrengthBucket >= 4 &&
                evidence.OwnStrengthBucket + 2 <= evidence.EnemyStrengthBucket;
            bool activePressure = enemyIntent.PrimaryIntent == InferredIntent.Attack;

            return activePressure && (severeBucketMismatch || largeEnemyOvermatch);
        }

        private static DirectChildAxis AxisFor(DirectChildRole role)
        {
            switch (role)
            {
                case DirectChildRole.Main:
                case DirectChildRole.SupportMain:
                case DirectChildRole.Fix:
                    return DirectChildAxis.SectorAxis;
                case DirectChildRole.Fallback:
                    return DirectChildAxis.Withdraw;
                case DirectChildRole.Screen:
                case DirectChildRole.RefuseLeft:
                case DirectChildRole.RefuseRight:
                case DirectChildRole.Reserve:
                    return DirectChildAxis.Hold;
                default:
                    return DirectChildAxis.None;
            }
        }

        private static float SupportPriorityFor(DirectChildRole role, DirectChildEvidence ev)
        {
            switch (role)
            {
                case DirectChildRole.Main: return 1f;
                case DirectChildRole.SupportMain: return 0.7f;
                case DirectChildRole.Fix: return 0.5f;
                case DirectChildRole.Reserve: return 0.4f;
                case DirectChildRole.Screen: return 0.3f;
                case DirectChildRole.RefuseLeft:
                case DirectChildRole.RefuseRight: return 0.3f;
                case DirectChildRole.Fallback: return 0.2f;
                default: return 0f;
            }
        }
    }
}
