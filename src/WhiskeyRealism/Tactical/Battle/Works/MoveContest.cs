using System;
using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>What the contest does with a held group this tick: nothing, stop it where it stands, or move it back to its hold point.</summary>
    public enum ContestAction
    {
        None,
        /// <summary>The group stands within <see cref="OrderArbiter.DedupeMeters"/> of its hold point: an executor Stop (the Move duplicate rule would drop a Move to the point it stands on).</summary>
        HoldStop,
        /// <summary>The group is farther from its hold point: a Move back to it.</summary>
        HoldMove,
    }

    /// <summary>One held group's tick as <see cref="MoveContest.Decide"/> reads it (spec § 7A requirement-works-hold, "One contest rule").</summary>
    public struct ContestInput
    {
        /// <summary>The works hold guards the group (<c>WorksHoldGuard.Holds</c>).</summary>
        public bool Held;
        /// <summary>A BC9 recon mission group: never contested.</summary>
        public bool Recon;
        /// <summary>The first tick the group is held and it already walks to a waypoint in the band: the march under way at the hold's onset.</summary>
        public bool OnsetMoving;
        public bool HasPath;
        /// <summary>The group's last waypoint is in the works band or on the works.</summary>
        public bool WaypointInBand;
        public bool LastSeenPath;
        /// <summary>Whiskey issued a Move to this group (or to a group its Move fanned out to) and it is recorded.</summary>
        public bool HasLastIssued;
        public float X, Z, WaypointX, WaypointZ, LastSeenX, LastSeenZ, LastIssuedX, LastIssuedZ, HoldX, HoldZ;
    }

    /// <summary>What the pipeline knows of a group this tick: the raw reads <see cref="MoveContestState.Evaluate"/> turns into a <see cref="ContestInput"/>.</summary>
    public struct ContestObservation
    {
        public bool Held, Recon, HasPath, HasLastIssued;
        public float X, Z, WaypointX, WaypointZ, LastIssuedX, LastIssuedZ;
        /// <summary>A former fix's lane (its fix target, outside the band): its hold point, instead of where it stands at the first hold.</summary>
        public bool HasLane;
        public float LaneX, LaneZ;
        /// <summary>Vanilla's per-group stance (<c>AiStance</c>); 4 is the assault.</summary>
        public int Stance;
        public IWorksQuery Works;
    }

    /// <summary>
    /// BC14W (spec § 7A requirement-works-hold, "One contest rule for moves made outside the stored orders"): a group the works hold guard holds is
    /// not left to walk into the works band by a move Whiskey did not issue (vanilla's chain move, its micro cycle, a courier order it queued before).
    /// Pure: <see cref="Decide"/> answers one group's tick, <see cref="MoveContestState"/> keeps the per-group memory and the counts, and the posture
    /// pipeline turns the answer into a Stop or a Move through the executor (the one writer). Not capped: a cap would release the group to vanilla's march.
    /// </summary>
    public static class MoveContest
    {
        /// <summary>A waypoint this far from the last seen one, or from the move Whiskey last issued, is another move.</summary>
        public const float ContestToleranceMeters = 50f;

        public static ContestAction Decide(in ContestInput c)
        {
            if (!c.Held || c.Recon || !c.HasPath || !c.WaypointInBand) return ContestAction.None;
            if (!c.OnsetMoving && !IsNewMove(in c)) return ContestAction.None;
            return Distance(c.X, c.Z, c.HoldX, c.HoldZ) <= OrderArbiter.DedupeMeters ? ContestAction.HoldStop : ContestAction.HoldMove;
        }

        // A snapshot waypoint more than the tolerance from the last seen one, or a path where there was none, that is not the move Whiskey last issued.
        private static bool IsNewMove(in ContestInput c)
        {
            bool moved = !c.LastSeenPath || Distance(c.WaypointX, c.WaypointZ, c.LastSeenX, c.LastSeenZ) > ContestToleranceMeters;
            if (!moved) return false;
            return !(c.HasLastIssued && Distance(c.WaypointX, c.WaypointZ, c.LastIssuedX, c.LastIssuedZ) <= ContestToleranceMeters);
        }

        /// <summary>The point is in the works band or on the works (the strip the hold keeps a held group out of).</summary>
        public static bool InBand(IWorksQuery works, float x, float z)
            => works != null && (works.InWorksBand(x, z) || works.OnHeldWorks(x, z));

        /// <summary>
        /// Whiskey's own interrupted-path recovery reads the same band: a held group's recovery target in it is refused (the plan becomes the hold).
        /// </summary>
        public static bool RecoveryAllowed(bool holds, float x, float z, IWorksQuery works)
            => !holds || !InBand(works, x, z);

        internal static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// The contest's per-group memory (the last seen waypoint and whether it had a path, the hold point latched at the first hold, the tick of the last
    /// contest) and the side's counts of what the hold held back, per alliance and per battle. The posture pipeline is its one writer.
    /// </summary>
    public sealed class MoveContestState
    {
        private struct Memory
        {
            public bool LastSeenPath;
            public float LastSeenX, LastSeenZ, HoldX, HoldZ;
            public long LastContestTick;
            public bool InBand, BandCounted, InAdvance;
        }

        private static readonly MoveContestState[] _byAlliance = { new MoveContestState(), new MoveContestState() };

        static MoveContestState()
        {
            BattleScope.Register("move-contest", ResetAll);
        }

        private readonly Dictionary<int, Memory> _groups = new Dictionary<int, Memory>(64);

        /// <summary>Contests of moves the executor did not issue (vanilla's chain move and the like), since the counts were last taken.</summary>
        public int VanillaChainContests;
        /// <summary>Contests of a march under way when the hold began.</summary>
        public int OnsetContests;
        /// <summary>Entries of a held group into the works band by an executor-issued move, and reads of vanilla stance 4, once per group per entry.</summary>
        public int AdvancesOnWorks;
        /// <summary>Held groups whose position entered the band, whatever moved them, and was still inside one tick later.</summary>
        public int BandEntries;

        /// <summary>Bound-checked: alliances 0 and 1 only; Europe (2) and anything else → null.</summary>
        public static MoveContestState ForAlliance(int alliance)
            => alliance >= 0 && alliance < _byAlliance.Length ? _byAlliance[alliance] : null;

        /// <summary>
        /// One group's tick. A group that is not held forgets its memory (the next hold is a new onset); a recon group is neither contested nor counted.
        /// At most one contest per group per <paramref name="tick"/>. <paramref name="holdX"/>/<paramref name="holdZ"/> is the hold point the answer
        /// returns the group to.
        /// </summary>
        public ContestAction Evaluate(int groupId, long tick, in ContestObservation o, out float holdX, out float holdZ)
        {
            holdX = o.X;
            holdZ = o.Z;
            if (!o.Held)
            {
                _groups.Remove(groupId);
                return ContestAction.None;
            }
            if (o.Recon) return ContestAction.None;

            bool onset = !_groups.TryGetValue(groupId, out Memory m);
            if (onset)
            {
                m = default(Memory);
                m.HoldX = o.HasLane ? o.LaneX : o.X;
                m.HoldZ = o.HasLane ? o.LaneZ : o.Z;
                m.LastContestTick = long.MinValue;
            }
            holdX = m.HoldX;
            holdZ = m.HoldZ;

            bool waypointInBand = o.HasPath && InBandOf(o.Works, o.WaypointX, o.WaypointZ);
            bool inBand = InBandOf(o.Works, o.X, o.Z);
            var input = new ContestInput
            {
                Held = true, Recon = false, OnsetMoving = onset && waypointInBand, HasPath = o.HasPath, WaypointInBand = waypointInBand,
                LastSeenPath = m.LastSeenPath, HasLastIssued = o.HasLastIssued,
                X = o.X, Z = o.Z, WaypointX = o.WaypointX, WaypointZ = o.WaypointZ, LastSeenX = m.LastSeenX, LastSeenZ = m.LastSeenZ,
                LastIssuedX = o.LastIssuedX, LastIssuedZ = o.LastIssuedZ, HoldX = m.HoldX, HoldZ = m.HoldZ,
            };
            ContestAction action = m.LastContestTick == tick ? ContestAction.None : MoveContest.Decide(in input);
            if (action != ContestAction.None)
            {
                m.LastContestTick = tick;
                if (input.OnsetMoving) OnsetContests++;
                else VanillaChainContests++;
            }

            // An advance: a waypoint in the band that is the move Whiskey last issued, or vanilla's assault stance. Counted once per entry; a group
            // already in either at the hold's onset counts only after it left and came back (an onset march is the contest's, not an advance).
            bool executorBand = waypointInBand && o.HasLastIssued &&
                MoveContest.Distance(o.WaypointX, o.WaypointZ, o.LastIssuedX, o.LastIssuedZ) <= MoveContest.ContestToleranceMeters;
            bool advancing = executorBand || o.Stance == AssaultStance;
            if (advancing && !m.InAdvance && !onset) AdvancesOnWorks++;
            m.InAdvance = advancing;

            // A band entry: the position is inside on two observations running (one arbiter tick after the contest that answered the move), counted once
            // per stay; a group already inside at the onset is counted only after it left and came back.
            if (inBand)
            {
                if (m.InBand && !m.BandCounted)
                {
                    BandEntries++;
                    m.BandCounted = true;
                }
                if (onset) m.BandCounted = true;
            }
            else
            {
                m.BandCounted = false;
            }
            m.InBand = inBand;

            m.LastSeenPath = o.HasPath;
            m.LastSeenX = o.WaypointX;
            m.LastSeenZ = o.WaypointZ;
            _groups[groupId] = m;
            return action;
        }

        // groupstancename (65061): 4 = assault.
        private const int AssaultStance = 4;

        private static bool InBandOf(IWorksQuery works, float x, float z) => MoveContest.InBand(works, x, z);

        /// <summary>Reads and clears the four counts (the telemetry row's "since the last row").</summary>
        public void TakeCounts(out int vanillaChainContests, out int onsetContests, out int advancesOnWorks, out int bandEntries)
        {
            vanillaChainContests = VanillaChainContests;
            onsetContests = OnsetContests;
            advancesOnWorks = AdvancesOnWorks;
            bandEntries = BandEntries;
            VanillaChainContests = OnsetContests = AdvancesOnWorks = BandEntries = 0;
        }

        private void Reset()
        {
            _groups.Clear();
            VanillaChainContests = OnsetContests = AdvancesOnWorks = BandEntries = 0;
        }

        private static void ResetAll()
        {
            for (int i = 0; i < _byAlliance.Length; i++) _byAlliance[i].Reset();
        }
    }
}
