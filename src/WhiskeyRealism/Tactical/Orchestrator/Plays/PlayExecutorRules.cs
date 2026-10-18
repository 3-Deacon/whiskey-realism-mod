namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// Pure rules the play executor applies to a template (BC14W, spec § 8 test seams). The runtime half (<see cref="PlayExecutorRuntime"/>) is
    /// excluded from the test project, so the rule lives here and the runtime reads it.
    /// </summary>
    public static class PlayExecutorRules
    {
        /// <summary>
        /// BC11's one mirrored try: a template with exactly one enveloping rect may flip it to the other side when its first side fails the
        /// FlankMove checks. A template with two enveloping rects (DoubleEnvelopment) has nothing to flip to. BC14W: an
        /// <see cref="PlayRectKind.AssaultColumn"/> is centred on the works' focal point and has no other side, so it is never flipped.
        /// </summary>
        public static bool CanMirror(PlayTemplate template)
        {
            if (template == null) return false;
            int enveloping = 0;
            for (int r = 0; r < template.Rects.Count; r++)
            {
                if (template.Rects[r].Kind == PlayRectKind.AssaultColumn) return false;
                if (template.Rects[r].Envelops) enveloping++;
            }
            return enveloping == 1;
        }
    }
}
