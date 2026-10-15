namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Pure scorer for the regimental intelligence layer (2026-05-21). Mirrors
    // Scourge of War's StandTargetAmmoAI decision tree (unitai.cpp lines
    // 1410-1640). Given a regiment's threat assessment + a few simple state
    // fields, returns the recommended action.
    //
    // Priority order (matches Scourge):
    //   1. Routed/broken — NoAction (vanilla morale system handles rout)
    //   2. Pressed >= 2 — RetreatToBrigade (boxed in; can't safely wheel)
    //   3. Flanked + low morale OR high casualties — FallBack
    //   4. Isolated from brigade — FallBack (GT enhancement, not in Scourge)
    //   5. Flanked + can wheel — WheelToFlank (face the threat)
    //   6. Otherwise — NoAction (let brigade-level orchestrator orders prevail)
    //
    // Input fields are deliberately minimal so the doctrine is harness-testable
    // with hand-crafted threat assessments. No Unity API touches.
    public static class TacticalRegimentalIntelligenceDoctrine
    {
        // Morale threshold below which fallback is preferred over wheel.
        // Scourge uses MORAL_BROKEN tier; we use 0.45 (above default-rout
        // threshold but below "fresh").
        public const float MoraleBrokenThreshold = 0.45f;
        // Casualty fraction at which an already-flanked unit prefers fallback
        // over wheel. Calibrated so a regiment that's taken substantial
        // losses doesn't try to face a new threat with depleted ranks.
        public const float CasualtyHighThreshold = 0.40f;
        // Minimum total nearby enemies before regimental intel engages at all.
        // Below this we trust the brigade-level plan. Prevents "1 enemy on
        // flank" from triggering a fallback when there's no real threat.
        public const int MinEngagementEnemyCount = 2;

        public static TacticalRegimentalDecision Decide(
            TacticalRegimentalThreatAssessment threats,
            float ownMorale01,
            float ownCasualtyFraction01,
            bool isRouted,
            bool stanceAllowsWheel)
        {
            // Routed or broken — vanilla AI's rout state machine handles
            // movement. We must not write conflicting orders.
            if (isRouted)
            {
                return new TacticalRegimentalDecision(
                    RegimentalAction.NoAction,
                    "routed-vanilla-handles",
                    threats);
            }

            // No real engagement and not isolated → no override.
            if (threats.TotalEnemiesInRange < MinEngagementEnemyCount && !threats.IsIsolated)
            {
                return new TacticalRegimentalDecision(
                    RegimentalAction.NoAction,
                    "no-meaningful-engagement",
                    threats);
            }

            // Priority 2: pressed >= 2 means enemies are inside wheel-distance
            // on multiple sides. Wheeling would expose another flank. Must
            // retreat. Scourge's bMustWheel + !bCanWheel + pressed>=2 path.
            if (threats.PressedCount >= 2)
            {
                return new TacticalRegimentalDecision(
                    RegimentalAction.RetreatToBrigade,
                    "pressed-on-multiple-sides",
                    threats);
            }

            // Priority 3: flanked + (low morale OR high casualties) → fallback.
            // Wheel would work but the unit is too depleted to absorb a
            // fresh fight on a new facing.
            if (threats.IsFlanked
                && (ownMorale01 < MoraleBrokenThreshold || ownCasualtyFraction01 > CasualtyHighThreshold))
            {
                return new TacticalRegimentalDecision(
                    RegimentalAction.FallBack,
                    ownMorale01 < MoraleBrokenThreshold ? "flanked-low-morale" : "flanked-high-casualties",
                    threats);
            }

            // Priority 4: isolated from brigade-mates (GT enhancement). Fall
            // back toward the brigade rally point before the situation gets
            // worse. Triggered even if threats are modest.
            if (threats.IsIsolated && threats.TotalEnemiesInRange >= 1)
            {
                return new TacticalRegimentalDecision(
                    RegimentalAction.FallBack,
                    "isolated-from-brigade",
                    threats);
            }

            // Priority 5: flanked + can wheel + healthy → wheel to face.
            // Choose which side based on which flank carries the most threat.
            if (threats.IsFlanked && stanceAllowsWheel)
            {
                float l = threats.LeftFlankThreat;
                float r = threats.RightFlankThreat;
                float rear = threats.RearThreat;
                RegimentalAction wheelAction;
                string reason;
                if (rear >= l && rear >= r)
                {
                    wheelAction = RegimentalAction.WheelToRear;
                    reason = "wheel-to-rear-threat";
                }
                else if (l >= r)
                {
                    wheelAction = RegimentalAction.WheelToLeftFlank;
                    reason = "wheel-to-left-threat";
                }
                else
                {
                    wheelAction = RegimentalAction.WheelToRightFlank;
                    reason = "wheel-to-right-threat";
                }
                return new TacticalRegimentalDecision(wheelAction, reason, threats);
            }

            // Priority 6: nothing actionable at the regimental level.
            return new TacticalRegimentalDecision(
                RegimentalAction.NoAction,
                "front-engagement-no-override",
                threats);
        }
    }
}
