using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// Static catalog mapping each <see cref="BattlePlanId"/> playbook archetype
    /// to its geometric <see cref="PlayTemplate"/>. Templates are defined in
    /// army-relative coordinates (see <see cref="PlayRect"/>) and projected to
    /// world rects at runtime by <see cref="PlayProjection"/>.
    ///
    /// Returns null for unknown / unsupported playbooks — caller falls back to
    /// the existing evidence-based role allocator path.
    /// </summary>
    public static class PlayTemplateCatalog
    {
        public static PlayTemplate Lookup(BattlePlanId id)
        {
            switch (id)
            {
                // ---- Offensive: envelopment archetypes ----
                case BattlePlanId.DoubleEnvelopment:    return DoubleEnvelopment();
                case BattlePlanId.MobileFlankMarch:     return MobileFlankMarch();
                case BattlePlanId.WideFlankApproach:    return WideFlankApproach();
                case BattlePlanId.ManeuverAndFix:       return ManeuverAndFix();

                // ---- Offensive: frontal / attritional ----
                case BattlePlanId.FrontalShockAssault:  return FrontalShockAssault();
                case BattlePlanId.ForcedFrontalAttack:  return ForcedFrontalAttack();
                case BattlePlanId.ContinuousAttrition:  return ContinuousAttrition();
                case BattlePlanId.HesitatedCommit:      return HesitatedCommit();

                // ---- Defensive ----
                case BattlePlanId.PreparedDefense:      return PreparedDefense();
                case BattlePlanId.ReverseSlopeDefense:  return ReverseSlopeDefense();
                case BattlePlanId.DelayingDefense:      return DelayingDefense();

                // ---- Cavalry-specific ----
                case BattlePlanId.CavalryRaid:          return CavalryRaid();
                case BattlePlanId.CavalryScreeningDelay:return CavalryScreeningDelay();

                // ---- Meeting / generic ----
                case BattlePlanId.MeetingEngagement:    return MeetingEngagement();
                case BattlePlanId.GenericAggressive:    return GenericAggressive();
                case BattlePlanId.GenericCautious:      return GenericCautious();
                case BattlePlanId.GenericMethodical:    return GenericMethodical();
                case BattlePlanId.GenericDesperate:     return GenericDesperate();

                default: return null;
            }
        }

        // -------- Template definitions --------
        //
        // Coordinate convention recap:
        //   x = lateral (-1 = far-left wing, +1 = far-right wing)
        //   y = forward (0 = on own line, +1 = forward toward enemy)
        //   StartTimeOffsetSeconds: 0 = immediate; >0 = corps holds until offset elapses

        private static PlayTemplate DoubleEnvelopment()
        {
            // Two wings + center pin + reserve. Both wings dispatch simultaneously
            // (StartTimeOffset=0); each wing's child marches to its rect, faces the enemy line
            // end on its side and attacks it (EnvelopmentManeuver). A wing that fails the
            // FlankMove checks (river or anchored high ground on the inward leg) is abandoned.
            // Reserve sits in rear-center.
            return new PlayTemplate("DoubleEnvelopment", BattlePlanId.DoubleEnvelopment, new[]
            {
                new PlayRect(PlayRectKind.LeftWing,  -0.9f, +0.4f, 0.5f, 0.5f, 0f, priority: 10, envelops: true),
                new PlayRect(PlayRectKind.RightWing, +0.9f, +0.4f, 0.5f, 0.5f, 0f, priority: 10, envelops: true),
                new PlayRect(PlayRectKind.Center,     0.0f, +0.3f, 0.4f, 0.4f, 0f, priority: 8),
                new PlayRect(PlayRectKind.Reserve,    0.0f, -0.4f, 0.4f, 0.4f, 0f, priority: 5),
            });
        }

        private static PlayTemplate MobileFlankMarch()
        {
            // Fast operational flanking: the FlankMarch rect's child holds until 900 battle-seconds
            // after the Center child's contact, marches to the rect, faces the enemy line end
            // and attacks it (EnvelopmentManeuver). Reverse-engineerable from Jackson
            // Chancellorsville flank-march pattern.
            return new PlayTemplate("MobileFlankMarch", BattlePlanId.MobileFlankMarch, new[]
            {
                new PlayRect(PlayRectKind.Center,    0.0f, +0.3f, 0.5f, 0.4f, 0f,    priority: 10),
                new PlayRect(PlayRectKind.FlankMarch,-1.1f, +0.6f, 0.5f, 0.6f, 900f, priority: 9, envelops: true),
                new PlayRect(PlayRectKind.Reserve,    0.0f, -0.4f, 0.4f, 0.4f, 0f,   priority: 5),
            });
        }

        private static PlayTemplate WideFlankApproach()
        {
            // Single wide-arc flank with a strong center fix. The FlankMarch rect's child holds until
            // 600 battle-seconds after the Center child's contact, then marches, faces the enemy
            // line end and attacks it (EnvelopmentManeuver); the mirrored side is tried when the
            // right is anchored or cut by a river. Right-wing chosen (mirror-arbitrary; corps
            // assignment resolves it geographically).
            return new PlayTemplate("WideFlankApproach", BattlePlanId.WideFlankApproach, new[]
            {
                new PlayRect(PlayRectKind.Center,     0.0f, +0.4f, 0.5f, 0.4f, 0f,   priority: 10),
                new PlayRect(PlayRectKind.FlankMarch,+1.0f, +0.7f, 0.5f, 0.6f, 600f, priority: 9, envelops: true),
                new PlayRect(PlayRectKind.Reserve,    0.0f, -0.4f, 0.4f, 0.4f, 0f,   priority: 5),
            });
        }

        private static PlayTemplate ManeuverAndFix()
        {
            // Fix center, maneuver one wing wide. The RIGHT wing is the maneuvering wing: the centre fixes, the
            // right wing goes round 300 battle-seconds after the Center child's contact (EnvelopmentManeuver).
            // Sherman Atlanta pattern.
            return new PlayTemplate("ManeuverAndFix", BattlePlanId.ManeuverAndFix, new[]
            {
                new PlayRect(PlayRectKind.Center,     0.0f, +0.3f, 0.5f, 0.4f, 0f,   priority: 10),
                new PlayRect(PlayRectKind.RightWing, +0.85f,+0.5f, 0.5f, 0.5f, 300f, priority: 9, envelops: true),
                new PlayRect(PlayRectKind.Demonstration,-0.7f,+0.3f,0.4f,0.4f, 0f,   priority: 6),
                new PlayRect(PlayRectKind.Reserve,    0.0f, -0.4f, 0.4f, 0.4f, 0f,   priority: 5),
            });
        }

        private static PlayTemplate FrontalShockAssault()
        {
            // Aggressive frontal commit — all force forward into center +
            // immediate flanks. Hood's pattern.
            return new PlayTemplate("FrontalShockAssault", BattlePlanId.FrontalShockAssault, new[]
            {
                new PlayRect(PlayRectKind.Center,    0.0f, +0.5f, 0.6f, 0.5f, 0f, priority: 10),
                new PlayRect(PlayRectKind.LeftWing, -0.6f, +0.4f, 0.4f, 0.5f, 0f, priority: 8),
                new PlayRect(PlayRectKind.RightWing,+0.6f, +0.4f, 0.4f, 0.5f, 0f, priority: 8),
                new PlayRect(PlayRectKind.Reserve,   0.0f, -0.3f, 0.3f, 0.3f, 0f, priority: 4),
            });
        }

        private static PlayTemplate ForcedFrontalAttack()
        {
            // Frontal under disadvantage — narrower, denser commit, no flanks.
            return new PlayTemplate("ForcedFrontalAttack", BattlePlanId.ForcedFrontalAttack, new[]
            {
                new PlayRect(PlayRectKind.Center,    0.0f, +0.5f, 0.5f, 0.5f, 0f, priority: 10),
                new PlayRect(PlayRectKind.LeftFlank, -0.7f, +0.0f, 0.3f, 0.4f, 0f, priority: 6),
                new PlayRect(PlayRectKind.RightFlank,+0.7f, +0.0f, 0.3f, 0.4f, 0f, priority: 6),
                new PlayRect(PlayRectKind.Reserve,    0.0f, -0.4f, 0.3f, 0.3f, 0f, priority: 4),
            });
        }

        private static PlayTemplate ContinuousAttrition()
        {
            // Sustained attritional pressure — broad front, deep reserves to
            // keep rotating fresh formations forward.
            return new PlayTemplate("ContinuousAttrition", BattlePlanId.ContinuousAttrition, new[]
            {
                new PlayRect(PlayRectKind.LeftWing,  -0.7f, +0.4f, 0.4f, 0.5f, 0f, priority: 9),
                new PlayRect(PlayRectKind.Center,     0.0f, +0.4f, 0.4f, 0.5f, 0f, priority: 10),
                new PlayRect(PlayRectKind.RightWing, +0.7f, +0.4f, 0.4f, 0.5f, 0f, priority: 9),
                new PlayRect(PlayRectKind.Reserve,    0.0f, -0.4f, 0.6f, 0.4f, 0f, priority: 7),
            });
        }

        private static PlayTemplate HesitatedCommit()
        {
            // Methodical-aggressive with deferred commit — center moves first, the wings hold for
            // their 600 battle-second offset (a start delay: the child holds, then takes its
            // allocator role; no maneuver) and then advance.
            return new PlayTemplate("HesitatedCommit", BattlePlanId.HesitatedCommit, new[]
            {
                new PlayRect(PlayRectKind.Center,    0.0f, +0.4f, 0.5f, 0.4f, 0f,   priority: 10),
                new PlayRect(PlayRectKind.LeftWing, -0.6f, +0.3f, 0.4f, 0.4f, 600f, priority: 8),
                new PlayRect(PlayRectKind.RightWing,+0.6f, +0.3f, 0.4f, 0.4f, 600f, priority: 8),
                new PlayRect(PlayRectKind.Reserve,   0.0f, -0.4f, 0.4f, 0.4f, 0f,   priority: 5),
            });
        }

        private static PlayTemplate PreparedDefense()
        {
            // Methodical defensive setup — center holds, flanks anchored,
            // reserve line behind. Forward screen forward of main line.
            return new PlayTemplate("PreparedDefense", BattlePlanId.PreparedDefense, new[]
            {
                new PlayRect(PlayRectKind.ForwardScreen, 0.0f, +0.5f, 0.6f, 0.2f, 0f, priority: 7),
                new PlayRect(PlayRectKind.LeftFlank,    -0.8f, 0.0f, 0.4f, 0.4f, 0f, priority: 9),
                new PlayRect(PlayRectKind.CenterHold,    0.0f, 0.0f, 0.6f, 0.4f, 0f, priority: 10),
                new PlayRect(PlayRectKind.RightFlank,   +0.8f, 0.0f, 0.4f, 0.4f, 0f, priority: 9),
                new PlayRect(PlayRectKind.ReserveLine,   0.0f, -0.5f, 0.6f, 0.4f, 0f, priority: 6),
            });
        }

        private static PlayTemplate ReverseSlopeDefense()
        {
            // Defensive reverse-slope — main line BEHIND the crest, screen
            // forward to lure enemy onto the crest exposed to our fire.
            return new PlayTemplate("ReverseSlopeDefense", BattlePlanId.ReverseSlopeDefense, new[]
            {
                new PlayRect(PlayRectKind.ForwardScreen, 0.0f, +0.5f, 0.6f, 0.2f, 0f, priority: 8),
                new PlayRect(PlayRectKind.CenterHold,    0.0f, -0.1f, 0.7f, 0.4f, 0f, priority: 10),
                new PlayRect(PlayRectKind.LeftFlank,    -0.7f, -0.1f, 0.3f, 0.4f, 0f, priority: 9),
                new PlayRect(PlayRectKind.RightFlank,   +0.7f, -0.1f, 0.3f, 0.4f, 0f, priority: 9),
                new PlayRect(PlayRectKind.ReserveLine,   0.0f, -0.6f, 0.5f, 0.3f, 0f, priority: 5),
            });
        }

        private static PlayTemplate DelayingDefense()
        {
            // Disciplined fight-yield-yield — forward line holds briefly,
            // falls back to mid line, then rear line. Sequential timing.
            return new PlayTemplate("DelayingDefense", BattlePlanId.DelayingDefense, new[]
            {
                new PlayRect(PlayRectKind.ForwardScreen, 0.0f, +0.5f, 0.7f, 0.2f, 0f, priority: 10),
                new PlayRect(PlayRectKind.CenterHold,    0.0f, +0.1f, 0.6f, 0.3f, 0f, priority: 9),
                new PlayRect(PlayRectKind.ReserveLine,   0.0f, -0.4f, 0.6f, 0.3f, 0f, priority: 7),
                new PlayRect(PlayRectKind.LeftFlank,    -0.6f, +0.1f, 0.3f, 0.3f, 0f, priority: 6),
                new PlayRect(PlayRectKind.RightFlank,   +0.6f, +0.1f, 0.3f, 0.3f, 0f, priority: 6),
            });
        }

        private static PlayTemplate CavalryRaid()
        {
            // Cavalry deep raid — single rect anchored far behind enemy; the child marches first to an
            // outflank point beyond the less-pressured end of the perceived enemy line, then to the rect
            // (EnvelopmentManeuver). Other forces (if any) hold a screening rect.
            return new PlayTemplate("CavalryRaid", BattlePlanId.CavalryRaid, new[]
            {
                new PlayRect(PlayRectKind.DeepRaid,     0.0f, +1.5f, 0.4f, 0.4f, 0f, priority: 10, envelops: true),
                new PlayRect(PlayRectKind.ForwardScreen,0.0f, +0.3f, 0.5f, 0.3f, 0f, priority: 5),
            });
        }

        private static PlayTemplate CavalryScreeningDelay()
        {
            // Cavalry screen + delay — wide forward screen, light delay line behind.
            return new PlayTemplate("CavalryScreeningDelay", BattlePlanId.CavalryScreeningDelay, new[]
            {
                new PlayRect(PlayRectKind.ForwardScreen,0.0f, +0.6f, 0.9f, 0.2f, 0f, priority: 10),
                new PlayRect(PlayRectKind.ReserveLine,  0.0f, -0.2f, 0.5f, 0.3f, 0f, priority: 6),
            });
        }

        private static PlayTemplate MeetingEngagement()
        {
            // Both sides arrive piecemeal — center contact + flanks deploy
            // as they arrive. No fixed timing (corps assigned by proximity).
            return new PlayTemplate("MeetingEngagement", BattlePlanId.MeetingEngagement, new[]
            {
                new PlayRect(PlayRectKind.Center,    0.0f, +0.5f, 0.5f, 0.4f, 0f, priority: 10),
                new PlayRect(PlayRectKind.LeftWing, -0.7f, +0.3f, 0.4f, 0.4f, 0f, priority: 8),
                new PlayRect(PlayRectKind.RightWing,+0.7f, +0.3f, 0.4f, 0.4f, 0f, priority: 8),
                new PlayRect(PlayRectKind.Reserve,   0.0f, -0.3f, 0.3f, 0.3f, 0f, priority: 5),
            });
        }

        // ---- Generic templates ----
        // Generic playbooks use simplified 3-4 rect templates suitable for
        // the personality archetype regardless of historical inspiration.

        private static PlayTemplate GenericAggressive()
        {
            return new PlayTemplate("GenericAggressive", BattlePlanId.GenericAggressive, new[]
            {
                new PlayRect(PlayRectKind.Center,    0.0f, +0.4f, 0.5f, 0.4f, 0f, priority: 10),
                new PlayRect(PlayRectKind.LeftWing, -0.7f, +0.4f, 0.4f, 0.4f, 0f, priority: 9),
                new PlayRect(PlayRectKind.RightWing,+0.7f, +0.4f, 0.4f, 0.4f, 0f, priority: 9),
                new PlayRect(PlayRectKind.Reserve,   0.0f, -0.4f, 0.3f, 0.3f, 0f, priority: 5),
            });
        }

        private static PlayTemplate GenericCautious()
        {
            return new PlayTemplate("GenericCautious", BattlePlanId.GenericCautious, new[]
            {
                new PlayRect(PlayRectKind.ForwardScreen, 0.0f,+0.5f, 0.6f, 0.2f, 0f, priority: 7),
                new PlayRect(PlayRectKind.CenterHold,    0.0f, 0.0f, 0.6f, 0.3f, 0f, priority: 10),
                new PlayRect(PlayRectKind.LeftFlank,    -0.7f, 0.0f, 0.3f, 0.4f, 0f, priority: 8),
                new PlayRect(PlayRectKind.RightFlank,   +0.7f, 0.0f, 0.3f, 0.4f, 0f, priority: 8),
                new PlayRect(PlayRectKind.ReserveLine,   0.0f,-0.5f, 0.5f, 0.3f, 0f, priority: 5),
            });
        }

        private static PlayTemplate GenericMethodical()
        {
            // The wings' 300 battle-second offset is a start delay (hold, then the allocator's role; no maneuver).
            return new PlayTemplate("GenericMethodical", BattlePlanId.GenericMethodical, new[]
            {
                new PlayRect(PlayRectKind.Center,     0.0f, +0.3f, 0.5f, 0.4f, 0f,   priority: 10),
                new PlayRect(PlayRectKind.LeftWing,  -0.6f, +0.3f, 0.4f, 0.4f, 300f, priority: 8),
                new PlayRect(PlayRectKind.RightWing, +0.6f, +0.3f, 0.4f, 0.4f, 300f, priority: 8),
                new PlayRect(PlayRectKind.Reserve,    0.0f, -0.4f, 0.4f, 0.4f, 0f,   priority: 6),
            });
        }

        private static PlayTemplate GenericDesperate()
        {
            // All-in, no reserve.
            return new PlayTemplate("GenericDesperate", BattlePlanId.GenericDesperate, new[]
            {
                new PlayRect(PlayRectKind.Center,    0.0f, +0.5f, 0.6f, 0.5f, 0f, priority: 10),
                new PlayRect(PlayRectKind.LeftWing, -0.6f, +0.5f, 0.4f, 0.5f, 0f, priority: 9),
                new PlayRect(PlayRectKind.RightWing,+0.6f, +0.5f, 0.4f, 0.5f, 0f, priority: 9),
            });
        }
    }
}
