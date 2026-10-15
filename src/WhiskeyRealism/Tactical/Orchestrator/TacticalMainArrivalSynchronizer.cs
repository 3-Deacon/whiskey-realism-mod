using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure predicate for brigade-arrival synchronization on the Main effort.
    ///
    /// Problem: Main brigade gets <see cref="CommandTaskType.AttackObjective"/>
    /// the moment its waypoint writes; SupportMain brigades dispatch
    /// simultaneously but arrive later because they start farther from contact.
    /// Result: Main engages alone before supports arrive — the
    /// "3rd Brigade attacks superior force without support" smoke pattern
    /// observed on 2026-05-22.
    ///
    /// Real Civil War / Napoleonic doctrine staggered the attack so the
    /// supporting force arrived together with the main force (Jackson's
    /// Chancellorsville flank march moved hours before the attack, so the
    /// entire corps emerged on the Union flank as one coordinated wave).
    /// We approximate this with a synchronization hold:
    ///
    ///   - Main brigade with assigned SupportMain siblings checks: has at
    ///     least one support closed within the proximity threshold of
    ///     contact?
    ///   - If NO and we have time remaining: HOLD (executor writes
    ///     AttackReady posture without the advance waypoint).
    ///   - If YES or timeout: RELEASE (executor writes the normal
    ///     AttackObjective waypoint).
    ///
    /// Bypass conditions:
    ///   - Aggressive commanders (aggression ≥ 0.7) do not wait — Hood and
    ///     Jackson commit Main regardless of support arrival.
    ///   - No SupportMain siblings exist (single-axis or all-Reserve army)
    ///     — nothing to wait for, release immediately.
    ///   - Timeout fires (default 30s) — supports may be blocked or routed;
    ///     do not stall Main indefinitely.
    /// </summary>
    public static class TacticalMainArrivalSynchronizer
    {
        /// <summary>
        /// Default proximity threshold: a SupportMain within this distance of
        /// the Main brigade's contact point is "close enough" to coordinate.
        /// Calibrated to ~2 minutes of march time at standard pace.
        /// </summary>
        public const float DefaultSupportProximityThresholdMeters = 500f;

        /// <summary>
        /// Maximum time Main will hold for supports before releasing regardless.
        /// Prevents indefinite stall when supports are blocked, routed, or
        /// engaging an unrelated threat.
        /// </summary>
        public const float DefaultMaxHoldSeconds = 30f;

        /// <summary>
        /// Aggression threshold above which the sync gate is bypassed.
        /// Lee/Jackson/Hood (≥ 0.7) commit Main without waiting for supports.
        /// </summary>
        public const float SyncBypassAggressionThreshold = 0.7f;

        /// <summary>
        /// One SupportMain sibling's distance to the Main brigade's contact
        /// point (or any "coordination anchor" — the post-fix executor decides
        /// which world point to measure against). Lower = closer to providing
        /// effective support.
        /// </summary>
        public readonly struct SupportMainPosition
        {
            public SupportMainPosition(float distanceToContactMeters)
            {
                DistanceToContactMeters = distanceToContactMeters;
            }
            public float DistanceToContactMeters { get; }
        }

        /// <summary>
        /// True when Main should HOLD at its current position (executor writes
        /// AttackReady posture and skips the advance waypoint) until at least
        /// one SupportMain has closed within the proximity threshold OR the
        /// hold-time budget elapses.
        /// </summary>
        public static bool ShouldHoldMainForSupports(
            IReadOnlyList<SupportMainPosition> supportMains,
            float commanderAggression01,
            float holdSecondsElapsed,
            float supportProximityThresholdMeters,
            float maxHoldSeconds)
        {
            // Aggressive commanders never hold.
            if (commanderAggression01 >= SyncBypassAggressionThreshold) return false;

            // Sanitize: timeout elapsed → release.
            float safeMaxHold = maxHoldSeconds > 0f ? maxHoldSeconds : DefaultMaxHoldSeconds;
            float safeElapsed = holdSecondsElapsed < 0f ? 0f : holdSecondsElapsed;
            if (float.IsNaN(safeElapsed) || float.IsInfinity(safeElapsed)) return false;
            if (safeElapsed >= safeMaxHold) return false;

            // No supports → nothing to wait for.
            if (supportMains == null || supportMains.Count == 0) return false;

            // Any support within proximity threshold → release.
            float threshold = supportProximityThresholdMeters > 0f
                ? supportProximityThresholdMeters
                : DefaultSupportProximityThresholdMeters;
            if (float.IsNaN(threshold) || float.IsInfinity(threshold))
                threshold = DefaultSupportProximityThresholdMeters;

            for (int i = 0; i < supportMains.Count; i++)
            {
                float d = supportMains[i].DistanceToContactMeters;
                if (float.IsNaN(d) || float.IsInfinity(d)) continue;
                if (d <= threshold) return false;
            }

            // All supports still distant AND aggressive bypass didn't fire AND
            // timeout hasn't elapsed → HOLD.
            return true;
        }

        /// <summary>
        /// Resolves the task a Main-effort brigade should execute this tick,
        /// applying the arrival-sync hold. When the Main is committed to
        /// <see cref="CommandTaskType.AttackObjective"/> but
        /// <see cref="ShouldHoldMainForSupports"/> says to wait, the task is
        /// downgraded to <see cref="CommandTaskType.Screen"/> — the Main
        /// skirmishes forward (vanilla <c>ai_stance = 1</c>) without committing
        /// to the assault until the supporting brigades close the line. When
        /// supports are in proximity, the hold-time budget elapses, or the
        /// commander is aggressive (≥ 0.7), the original attack task passes
        /// through and the army advances as one coordinated wave.
        ///
        /// Only Main-role brigades on <see cref="CommandTaskType.AttackObjective"/>
        /// are subject to the hold; every other role/task returns unchanged so
        /// supports, screens, and reserves execute their own tasks normally.
        /// Pure — the runtime gathers <paramref name="supportMains"/> distances
        /// and tracks <paramref name="holdSecondsElapsed"/>.
        /// </summary>
        public static CommandTaskType ResolveMainAttackTask(
            DirectChildRole role,
            CommandTaskType attackTask,
            IReadOnlyList<SupportMainPosition> supportMains,
            float commanderAggression01,
            float holdSecondsElapsed,
            float supportProximityThresholdMeters,
            float maxHoldSeconds,
            out bool holding)
        {
            holding = false;
            if (role != DirectChildRole.Main) return attackTask;
            if (attackTask != CommandTaskType.AttackObjective) return attackTask;

            if (ShouldHoldMainForSupports(
                    supportMains, commanderAggression01, holdSecondsElapsed,
                    supportProximityThresholdMeters, maxHoldSeconds))
            {
                holding = true;
                return CommandTaskType.Screen;
            }
            return attackTask;
        }
    }
}
