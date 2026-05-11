using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure deterministic role allocator. Given the army CO's plan, commander
    /// personality, and a parallel evidence list (one entry per registered child
    /// in registration order), produces a parallel list of DirectChildIntent
    /// with role assignments per spec rules. No Unity types.
    /// </summary>
    public static class DirectChildAllocator
    {
        private const int FlankExposureRefuseThreshold = 2;

        public static IReadOnlyList<DirectChildIntent> Allocate(
            TacticalBattlePlan plan,
            PersonalityVector personality,
            IReadOnlyList<DirectChildSnapshot> snapshots,
            IReadOnlyList<DirectChildEvidence> evidence)
        {
            var enemyIntents = new TacticalIntentModel[snapshots.Count];
            for (int i = 0; i < enemyIntents.Length; i++)
                enemyIntents[i] = new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>());
            return AllocateWithChildIntent(plan, personality, snapshots, evidence, enemyIntents);
        }

        public static IReadOnlyList<DirectChildIntent> AllocateWithChildIntent(
            TacticalBattlePlan plan,
            PersonalityVector personality,
            IReadOnlyList<DirectChildSnapshot> snapshots,
            IReadOnlyList<DirectChildEvidence> evidence,
            IReadOnlyList<TacticalIntentModel> perChildEnemyIntent)
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

            int mainIdx = PickMainEffort(plan.MainEffortSector, snapshots, evidence);
            if (mainIdx >= 0 && !ShouldFallbackUnderSevereOvermatch(evidence[mainIdx], perChildEnemyIntent[mainIdx]))
                roles[mainIdx] = DirectChildRole.Main;

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

                if (mainIdx >= 0 && IsAdjacentSector(ev.PrimarySector, evidence[mainIdx].PrimarySector) && ev.OwnStrengthBucket >= 1 && ev.FlankExposureBucket < FlankExposureRefuseThreshold)
                {
                    roles[i] = DirectChildRole.SupportMain;
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
                    roles[i] = ev.PrimarySector < mainSector
                        ? DirectChildRole.RefuseLeft
                        : DirectChildRole.RefuseRight;
                    continue;
                }

                if (ev.OwnStrengthBucket >= 2 && !ev.ContactFlag)
                {
                    roles[i] = DirectChildRole.Reserve;
                    continue;
                }

                if (ev.EnemyStrengthBucket > ev.OwnStrengthBucket + 1
                    && perChildEnemyIntent[i].PrimaryIntent == InferredIntent.Attack)
                {
                    roles[i] = DirectChildRole.Fallback;
                    continue;
                }
            }

            var intents = new DirectChildIntent[snapshots.Count];
            float aggressionBias01 = (personality.Aggression + 1f) * 0.5f;
            for (int i = 0; i < snapshots.Count; i++)
            {
                var snap = snapshots[i];
                var ev = evidence[i];
                var role = roles[i];
                int axisSector = role == DirectChildRole.Main || role == DirectChildRole.SupportMain
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

        private static int PickMainEffort(int mainSector, IReadOnlyList<DirectChildSnapshot> snaps, IReadOnlyList<DirectChildEvidence> ev)
        {
            int best = -1;
            int bestScore = -1;
            for (int i = 0; i < snaps.Count; i++)
            {
                if (ev[i].PrimarySector != mainSector) continue;
                int score = ev[i].OwnStrengthBucket * Math.Max(1, 4 - ev[i].FlankExposureBucket);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        private static bool Contains(int[] arr, int val)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == val) return true;
            return false;
        }

        private static bool IsAdjacentSector(int s, int main) => Math.Abs(s - main) == 1;

        private static bool ShouldFallbackUnderSevereOvermatch(
            DirectChildEvidence evidence,
            TacticalIntentModel enemyIntent)
        {
            bool severeBucketMismatch = evidence.EnemyStrengthBucket > evidence.OwnStrengthBucket + 1;
            bool largeEnemyOvermatch = evidence.EnemyStrengthBucket >= 4 &&
                evidence.OwnStrengthBucket <= 3 &&
                evidence.Confidence01 >= 0.9f;
            bool activePressure = enemyIntent.PrimaryIntent == InferredIntent.Attack ||
                evidence.Confidence01 >= 0.9f;

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
