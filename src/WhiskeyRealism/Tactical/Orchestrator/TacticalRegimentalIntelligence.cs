using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Regimental intelligence layer (2026-05-21). Ported from Scourge of War's
    // unitai.cpp StandTargetAmmoAI tactical decision tree (sdk/SowAiInf/unitai.cpp
    // lines ~1410-1640). At each cycle the worker computes a per-regiment threat
    // assessment + recommended action, main reads it and applies waypoint /
    // stance writes if any. Mirrors Scourge's 4-quadrant model:
    //
    //   Front       — direction toward the centroid of nearby enemies (±45°)
    //   Left flank  — 45°–135° counter-clockwise from front
    //   Rear        — beyond ±135° from front
    //   Right flank — 45°–135° clockwise from front
    //
    // "Pressed" counter increments per quadrant with an enemy inside wheel-
    // distance (default ~120m). pressed >= 2 → unit is boxed in, can't safely
    // wheel, must retreat (Scourge's bMustWheel + !bCanWheel pattern).
    //
    // Isolation detection is a GT-specific enhancement: Scourge doesn't track
    // brigade cohesion at the regimental level, but GT's wider scale (200+m
    // brigade frontages) makes "no friendly regiment within 400m" a useful
    // signal that vanilla's brigade-cohesion behavior can't recover from.

    public enum RegimentalQuadrant
    {
        Front = 0,
        LeftFlank = 1,
        Rear = 2,
        RightFlank = 3,
    }

    public enum RegimentalAction
    {
        // No action — worker recommends no override; brigade/orchestrator orders prevail.
        NoAction = 0,
        // Wheel to face flank — emit a stance/facing change. Used when flanked
        // AND not yet pressed enough to require retreat (Scourge's bMustWheel
        // without !bCanWheel).
        WheelToLeftFlank = 1,
        WheelToRightFlank = 2,
        WheelToRear = 3,
        // Fall back a short distance toward brigade — emit a waypoint behind
        // the regiment toward the brigade rally point. Maintains formation
        // integrity (Scourge's eComFallback). Triggered by flank threat or
        // isolation when retreat would be premature.
        FallBack = 4,
        // Full retreat to brigade rally point — emit a waypoint at the brigade
        // commander's position. Triggered when pressed >= 2 (boxed in) or
        // when high casualties + flanked + can't wheel (Scourge's eComRetreat).
        RetreatToBrigade = 5,
    }

    // Threat per quadrant + meta. Built on worker thread from pure observation
    // data. Immutable after construction. Reference-shared into the published
    // decision snapshot.
    public readonly struct TacticalRegimentalThreatAssessment
    {
        public TacticalRegimentalThreatAssessment(
            float frontThreat,
            float leftFlankThreat,
            float rearThreat,
            float rightFlankThreat,
            int pressedCount,
            bool isFlanked,
            bool isIsolated,
            int totalEnemiesInRange,
            float frontBearingDegrees)
        {
            FrontThreat = Sanitize(frontThreat);
            LeftFlankThreat = Sanitize(leftFlankThreat);
            RearThreat = Sanitize(rearThreat);
            RightFlankThreat = Sanitize(rightFlankThreat);
            PressedCount = pressedCount < 0 ? 0 : pressedCount;
            IsFlanked = isFlanked;
            IsIsolated = isIsolated;
            TotalEnemiesInRange = totalEnemiesInRange < 0 ? 0 : totalEnemiesInRange;
            FrontBearingDegrees = float.IsNaN(frontBearingDegrees) || float.IsInfinity(frontBearingDegrees)
                ? 0f
                : frontBearingDegrees;
        }

        public float FrontThreat { get; }
        public float LeftFlankThreat { get; }
        public float RearThreat { get; }
        public float RightFlankThreat { get; }
        public int PressedCount { get; }
        public bool IsFlanked { get; }
        public bool IsIsolated { get; }
        public int TotalEnemiesInRange { get; }
        // Bearing in degrees [0..360) toward the enemy centroid. Stored so the
        // apply path can construct fallback waypoints directly away from this
        // direction without re-walking the observation array.
        public float FrontBearingDegrees { get; }

        public static TacticalRegimentalThreatAssessment Empty => new TacticalRegimentalThreatAssessment(
            0f, 0f, 0f, 0f, 0, false, false, 0, 0f);

        public float MaxFlankThreat
        {
            get
            {
                float l = LeftFlankThreat;
                float r = RightFlankThreat;
                float rear = RearThreat;
                float m = l > r ? l : r;
                return m > rear ? m : rear;
            }
        }

        private static float Sanitize(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) return 0f;
            return v;
        }
    }

    // Final decision per regiment. Carries both the action and a telemetry
    // reason string so smoke can attribute the choice.
    public readonly struct TacticalRegimentalDecision
    {
        public TacticalRegimentalDecision(RegimentalAction action, string reason, TacticalRegimentalThreatAssessment threats)
        {
            Action = action;
            Reason = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
            Threats = threats;
        }

        public RegimentalAction Action { get; }
        public string Reason { get; }
        public TacticalRegimentalThreatAssessment Threats { get; }

        public static TacticalRegimentalDecision NoAction => new TacticalRegimentalDecision(
            RegimentalAction.NoAction,
            "default",
            TacticalRegimentalThreatAssessment.Empty);
    }
}
