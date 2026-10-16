namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// BC13 requirement-enemy-samples as one pure predicate (extracted for BC14): the enemy observations a decision may count.
    /// An observation is a sample when it is Visible, or Remembered at <see cref="RememberedMinConfidence"/> or more; is not a
    /// reinforcement column; and is infantry, cavalry or artillery (unit types 0 to 2). The detector, the opportunity tick, the
    /// cavalry strike patch and the feasibility evaluation all read it, so the fog rule has one definition.
    /// </summary>
    public static class EnemySamples
    {
        public const float RememberedMinConfidence = 0.5f;

        public static bool Accepts(in EnemyObservation o) =>
            !o.IsReinforcement && o.UnitTyp >= 0 && o.UnitTyp <= 2 &&
            (o.State == ObservationState.Visible || (o.State == ObservationState.Remembered && o.Confidence01 >= RememberedMinConfidence));
    }
}
