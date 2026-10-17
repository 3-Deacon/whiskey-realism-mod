using System;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// A gated move does not pass unchecked while the river map is still being built (pure). The map follows the terrain picture within a fraction of a
    /// second of the battle's start (RiverRegionsRuntime), but until then the crossing gate has no picture and used to let every leg through as one
    /// bank. A side whose terrain has a river (and whose map is neither off nor failed) waits for it: the move is refused as pending and its producer
    /// asks again next tick. The wait is bounded by <see cref="CapHours"/> of doctrine time from the first pending move: a map that does not arrive
    /// (the build stalled) opens the gate, as with no river map at all, so no move is held for good.
    /// </summary>
    public sealed class CrossingPictureWait
    {
        /// <summary>The longest a move waits for the river map: 72 battle seconds of doctrine time.</summary>
        public const double CapHours = 0.02;

        private double _pendingSince = double.NaN;

        public void Clear() => _pendingSince = double.NaN;

        /// <summary>The first doctrine hour a move waited for the map; NaN when none waits.</summary>
        public double PendingSinceHours => _pendingSince;

        /// <summary>
        /// Whether a move waits: the map is <paramref name="pending"/> and the cap, counted from the first pending move, has not run out. A map that is
        /// no longer pending (built, off, failed or the terrain has no river) clears the wait.
        /// </summary>
        public bool Waits(bool pending, double nowHours)
        {
            if (!pending)
            {
                _pendingSince = double.NaN;
                return false;
            }
            if (double.IsNaN(_pendingSince)) _pendingSince = nowHours;
            return nowHours - _pendingSince < CapHours;
        }
    }
}
