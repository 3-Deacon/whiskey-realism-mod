using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>One reserve a holding command has sent out: which, in whose command, as what, to which band, and the allocator's own reason (crisis, stagger or flank threat).</summary>
    public readonly struct LeafRelease
    {
        public LeafRelease(int parentInstanceId, int childInstanceId, DirectChildRole role, string reason, int held, int floor, int sector)
        {
            ParentInstanceId = parentInstanceId;
            ChildInstanceId = childInstanceId;
            Role = role;
            Reason = reason;
            Held = held;
            Floor = floor;
            Sector = sector;
        }

        public int ParentInstanceId { get; }
        public int ChildInstanceId { get; }
        public DirectChildRole Role { get; }
        public string Reason { get; }
        public int Held { get; }
        public int Floor { get; }

        /// <summary>The front-axis band the reserve is sent to: the crisis band, the exposed wing's band, or the main effort's band for the stagger.</summary>
        public int Sector { get; }
    }

    /// <summary>
    /// The release of the Defender's reserves inside a holding command (verify F4: the cascade held a third of a division back and nothing
    /// ever let them go). It is the army-level release (<see cref="DirectChildAllocator.ReleaseReserves"/>, the same crisis, the same
    /// stagger by interval and floor, the same threatened-wing guard) run over the command's subordinates: their evidence is the
    /// perceived picture only (<see cref="EnemyVisibleState"/>: the sector the subordinate stands in, the strengths seen there, whether fire was
    /// seen, and the flank exposure the cascade read), so fog decides what is a threat here as it does for the army. Each command keeps its own
    /// <see cref="ReserveReleaseState"/> (its interval, its floor and the children it has released), so one command's release does not spend
    /// another's. The release and the return are damped by one <see cref="ReleaseHysteresis"/> (verify r2: no flip every tick). The result is the
    /// set of reserves sent out, the role each takes and the band it goes to; the next leaf map applies the roles (BuildMap's <c>released</c>) and
    /// <see cref="Battle.LeafReservePlanner"/> moves them. Crossing guards are not carried down to a subordinate command: a reserve division is
    /// released to a crossing by the army's allocation, which holds the guard point the play executor reads. Allocation free once a command's
    /// size is known.
    /// </summary>
    public sealed class LeafReserveRelease
    {
        private sealed class Scratch
        {
            public int[] Ids;
            public DirectChildSnapshot[] Snapshots;
            public int[] Sectors;
            public DirectChildEvidence[] Evidence;
            public DirectChildRole[] Roles;
            public int[] Axis;
            public GuardPoint[] Guards;
            public readonly ReserveReleaseState State = new ReserveReleaseState();

            public Scratch(int[] ids)
            {
                int n = ids.Length;
                Ids = (int[])ids.Clone();
                Snapshots = new DirectChildSnapshot[n];
                for (int i = 0; i < n; i++) Snapshots[i] = new DirectChildSnapshot("child-" + ids[i], string.Empty, 0, 0, string.Empty, true);
                Sectors = new int[n];
                Evidence = new DirectChildEvidence[n];
                Roles = new DirectChildRole[n];
                Axis = new int[n];
                Guards = new GuardPoint[n];
            }

            public bool Matches(int[] ids)
            {
                if (Ids.Length != ids.Length) return false;
                for (int i = 0; i < ids.Length; i++) if (Ids[i] != ids[i]) return false;
                return true;
            }
        }

        private readonly Dictionary<int, Scratch> _scratch = new Dictionary<int, Scratch>();
        private readonly ReleaseHysteresis _hysteresis = new ReleaseHysteresis();
        private Dictionary<int, DirectChildRole> _released = new Dictionary<int, DirectChildRole>();
        private Dictionary<int, DirectChildRole> _previous = new Dictionary<int, DirectChildRole>();
        private readonly List<LeafRelease> _releases = new List<LeafRelease>();
        private readonly List<LeafRelease> _fresh = new List<LeafRelease>();

        /// <summary>The reserves sent out now: the subordinate command's instance id to the role it takes.</summary>
        public IReadOnlyDictionary<int, DirectChildRole> Released => _released;

        /// <summary>Every release now in force, with the reason, the band and the held and floor counts it was made against.</summary>
        public IReadOnlyList<LeafRelease> Releases => _releases;

        /// <summary>The releases of the last <see cref="Compute"/> that were not in force before it (the telemetry rows).</summary>
        public IReadOnlyList<LeafRelease> NewReleases => _fresh;

        /// <summary>A stand-down, a new plan or a battle's end: nothing is held released.</summary>
        public void Clear()
        {
            _scratch.Clear();
            _hysteresis.Clear();
            _released.Clear();
            _previous.Clear();
            _releases.Clear();
            _fresh.Clear();
        }

        /// <summary>
        /// Runs the release over every command in <paramref name="commands"/> that holds a reserve. <paramref name="live"/> (optional) gives the
        /// positions the subordinates stand at now; without it the positions the tree was built with are used. Returns true when the set of
        /// released reserves or a role in it changed (the leaf map is then rebuilt).
        /// </summary>
        public bool Compute(IReadOnlyList<DefenderReserveRecord> commands, TacticalBattlePlan plan, TacticalFrontAxis axis, EnemyVisibleState enemy, float globalOdds,
            double nowHours, float agg01, float caution01, float casualtyTolerance01, bool flankRelease, bool staggered, BattleSnapshot live = null)
        {
            Dictionary<int, DirectChildRole> before = _released;
            _released = _previous;
            _previous = before;
            _released.Clear();
            _releases.Clear();
            _fresh.Clear();

            for (int c = 0; commands != null && c < commands.Count; c++)
            {
                DefenderReserveRecord command = commands[c];
                if (!command.CanRelease) continue;
                int n = command.ChildIds.Length;
                bool holds = false;
                for (int i = 0; i < n; i++) if (command.BaseRoles[i] == DirectChildRole.Reserve) holds = true;
                if (!holds) continue;

                if (!_scratch.TryGetValue(command.ParentInstanceId, out Scratch scratch) || !scratch.Matches(command.ChildIds))
                {
                    scratch = new Scratch(command.ChildIds);
                    _scratch[command.ParentInstanceId] = scratch;
                }
                for (int i = 0; i < n; i++)
                {
                    float x = command.WorldX[i], z = command.WorldZ[i];
                    if (live != null)
                    {
                        int at = live.IndexOfOwn(command.ChildIds[i]);
                        if (at >= 0) { x = live.Own[at].X; z = live.Own[at].Z; }
                    }
                    scratch.Sectors[i] = axis.IsValid ? axis.SectorOf(x, z) : 0;
                    scratch.Roles[i] = command.BaseRoles[i];
                }
                if (!DirectChildEvidenceBuilder.BuildInto(scratch.Evidence, scratch.Snapshots, scratch.Sectors, command.FlankBuckets, enemy)) continue;
                Array.Clear(scratch.Guards, 0, n);

                var context = new ReserveReleaseContext(globalOdds, flankRelease, scratch.State, null, nowHours, agg01, caution01, casualtyTolerance01, staggered,
                    hysteresis: _hysteresis);
                DirectChildAllocator.ReleaseReservesInto(plan, scratch.Evidence, scratch.Roles, context, scratch.Snapshots, scratch.Guards, scratch.Axis);

                for (int i = 0; i < n; i++)
                {
                    if (command.BaseRoles[i] != DirectChildRole.Reserve || scratch.Roles[i] == DirectChildRole.Reserve) continue;
                    int id = command.ChildIds[i];
                    DirectChildRole role = scratch.Roles[i];
                    _released[id] = role;
                    int sector = scratch.Axis[i] >= 0 ? scratch.Axis[i] : plan.MainEffortSector;
                    var release = new LeafRelease(command.ParentInstanceId, id, role, ReasonOf(scratch.State, scratch.Snapshots[i].ChildId, role, out int held, out int floor),
                        held, floor, sector);
                    _releases.Add(release);
                    if (!before.TryGetValue(id, out DirectChildRole was) || was != role) _fresh.Add(release);
                }
            }

            if (before.Count != _released.Count) return true;
            foreach (var pair in _released)
                if (!before.TryGetValue(pair.Key, out DirectChildRole role) || role != pair.Value) return true;
            return false;
        }

        // The allocator's own reason for a release, from what it recorded: a crisis is Fix on the crisis band, a stagger or odds release is SupportMain,
        // a guard for a threatened wing is RefuseLeft / RefuseRight.
        private static string ReasonOf(ReserveReleaseState state, string childId, DirectChildRole role, out int held, out int floor)
        {
            held = 0;
            floor = 0;
            for (int i = 0; i < state.Outcomes.Count; i++)
            {
                if (!string.Equals(state.Outcomes[i].ChildId, childId, StringComparison.Ordinal)) continue;
                held = state.Outcomes[i].Held;
                floor = state.Outcomes[i].Floor;
                return state.Outcomes[i].Crisis ? "crisis" : "stagger";
            }
            if (role == DirectChildRole.RefuseLeft || role == DirectChildRole.RefuseRight) return "flank-threat";
            return role == DirectChildRole.Fix ? "crisis" : "stagger";
        }
    }
}
