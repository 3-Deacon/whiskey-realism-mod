namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// Tactical zone kind for a play-template rect. Maps to a per-brigade role
    /// in the allocator and to a vanilla ai_stance via the cascade.
    ///
    /// Mirrors the Scourge of War "eSpot*" slot taxonomy (offai.cpp:1030,
    /// PSTATLValid / PSTATRValid) at the rect level — each rect declares a
    /// tactical purpose, and the corps assigned to that rect deploys with
    /// roles scoped to that purpose.
    /// </summary>
    public enum PlayRectKind
    {
        Unknown = 0,
        /// <summary>Primary offensive wing — anchor for the Main role.</summary>
        LeftWing,
        /// <summary>Primary offensive wing — anchor for the Main role.</summary>
        RightWing,
        /// <summary>Pinning / fixing rect — brigades become Fix role.</summary>
        Center,
        /// <summary>Defensive line rect — brigades become Defender → HoldObjective.</summary>
        CenterHold,
        /// <summary>Defensive flank — brigades become Defender (RefuseLeft).</summary>
        LeftFlank,
        /// <summary>Defensive flank — brigades become Defender (RefuseRight).</summary>
        RightFlank,
        /// <summary>Forward screening rect — brigades become Screen.</summary>
        ForwardScreen,
        /// <summary>Reserve assembly area — brigades become Reserve.</summary>
        Reserve,
        /// <summary>Rear defensive line — brigades become Reserve at line position.</summary>
        ReserveLine,
        /// <summary>Cavalry deep-raid rect — anchor far behind enemy lines.</summary>
        DeepRaid,
        /// <summary>Mobile flank-march rect — Wide-arc envelopment column.</summary>
        FlankMarch,
        /// <summary>Demonstration / feint rect — brigades become Screen with light pressure.</summary>
        Demonstration,
    }

    /// <summary>
    /// A single zone in a play template. Coordinates are army-relative — at
    /// projection time, RelativeAnchor is interpreted in a frame anchored at
    /// the army centroid with forwardAxis pointing toward the enemy centroid
    /// and rightAxis perpendicular.
    ///
    /// Coordinate convention:
    ///   - RelativeAnchor.x : lateral offset, -1.0 = far-left wing, +1.0 = far-right wing
    ///   - RelativeAnchor.y : forward offset, -1.0 = behind own line, +1.0 = forward toward enemy
    ///   - RelativeSize    : proportional to army frontage / depth (1.0 = "as wide as the army")
    /// </summary>
    public readonly struct PlayRect
    {
        public PlayRect(
            PlayRectKind kind,
            float relativeAnchorX,
            float relativeAnchorY,
            float relativeSizeX,
            float relativeSizeY,
            float startTimeOffsetSeconds,
            int priority)
        {
            Kind = kind;
            RelativeAnchorX = SanitizeRange(relativeAnchorX, -2f, 2f);
            RelativeAnchorY = SanitizeRange(relativeAnchorY, -2f, 2f);
            RelativeSizeX = SanitizeNonNeg(relativeSizeX);
            RelativeSizeY = SanitizeNonNeg(relativeSizeY);
            StartTimeOffsetSeconds = SanitizeNonNeg(startTimeOffsetSeconds);
            Priority = priority;
        }

        public PlayRectKind Kind { get; }
        public float RelativeAnchorX { get; }
        public float RelativeAnchorY { get; }
        public float RelativeSizeX { get; }
        public float RelativeSizeY { get; }
        public float StartTimeOffsetSeconds { get; }
        public int Priority { get; }

        private static float SanitizeRange(float v, float min, float max)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static float SanitizeNonNeg(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return v < 0f ? 0f : v;
        }
    }
}
