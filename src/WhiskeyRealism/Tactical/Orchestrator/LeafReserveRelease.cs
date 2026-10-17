using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>One reserve a holding command has sent out: which, in whose command, as what, and the allocator's own reason (crisis, stagger or flank threat).</summary>
    public readonly struct LeafRelease
    {
        public LeafRelease(int parentInstanceId, int childInstanceId, DirectChildRole role, string reason, int held, int floor)
        {
            ParentInstanceId = parentInstanceId;
            ChildInstanceId = childInstanceId;
            Role = role;
            Reason = reason;
            Held = held;
            Floor = floor;
        }

        public int ParentInstanceId { get; }
        public int ChildInstanceId { get; }
        public DirectChildRole Role { get; }
        public string Reason { get; }
        public int Held { get; }
        public int Floor { get; }
    }

    /// <summary>
    /// The release of the Defender's reserves inside a holding command (verify F4: the cascade held a third of a division back and nothing
    /// ever let them go). It is the army-level release (<see cref="DirectChildAllocator.ReleaseReserves"/>, the same crisis, the same
    /// stagger by interval and floor, the same threatened-wing guard) run over the command's subordinates: their evidence is the
    /// perceived picture only (<see cref="EnemyVisibleState"/>: the sector the subordinate stands in, the strengths seen there, whether fire was
    /// seen, and the flank exposure the cascade read), so fog decides what is a threat here as it does for the army. Each command keeps its own
    /// <see cref="ReserveReleaseState"/> (its interval, its floor and the children it has released), so one command's release does not spend
    /// another's. The result is the set of reserves sent out and the role each takes; the next leaf map applies it (BuildMap's
    /// <c>released</c>). Crossing guards are not carried down to a subordinate command: a reserve division is released to a crossing by the army's
    /// allocation, which holds the guard point the play executor reads.
    /// </summary>
    public sealed class LeafReserveRelease
    {
        private readonly Dictionary<int, ReserveReleaseState> _states = new Dictionary<int, ReserveReleaseState>();
        private readonly Dictionary<int, DirectChildRole> _released = new Dictionary<int, DirectChildRole>();
        private readonly List<LeafRelease> _releases = new List<LeafRelease>();
        private readonly List<LeafRelease> _fresh = new List<LeafRelease>();

        /// <summary>The reserves sent out now: the subordinate command's instance id to the role it takes.</summary>
        public IReadOnlyDictionary<int, DirectChildRole> Released => _released;

        /// <summary>Every release now in force, with the reason and the held and floor counts it was made against.</summary>
        public IReadOnlyList<LeafRelease> Releases => _releases;

        /// <summary>The releases of the last <see cref="Compute"/> that were not in force before it (the telemetry rows).</summary>
        public IReadOnlyList<LeafRelease> NewReleases => _fresh;

        /// <summary>A stand-down, a new plan or a battle's end: nothing is held released.</summary>
        public void Clear()
        {
            _states.Clear();
            _released.Clear();
            _releases.Clear();
            _fresh.Clear();
        }

        /// <summary>
        /// Runs the release over every command in <paramref name="commands"/> that holds a reserve. Returns true when the set of released
        /// reserves or a role in it changed (the leaf map is then rebuilt).
        /// </summary>
        public bool Compute(IReadOnlyList<DefenderReserveRecord> commands, TacticalBattlePlan plan, TacticalFrontAxis axis, EnemyVisibleState enemy, float globalOdds,
            double nowHours, float agg01, float caution01, float casualtyTolerance01, bool flankRelease, bool staggered)
        {
            var before = new Dictionary<int, DirectChildRole>(_released);
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

                var snapshots = new DirectChildSnapshot[n];
                var sectors = new int[n];
                for (int i = 0; i < n; i++)
                {
                    snapshots[i] = new DirectChildSnapshot("child-" + command.ChildIds[i], string.Empty, 0, 0, string.Empty, true);
                    sectors[i] = axis.IsValid ? axis.SectorOf(command.WorldX[i], command.WorldZ[i]) : 0;
                }
                IReadOnlyList<DirectChildEvidence> evidence = DirectChildEvidenceBuilder.BuildAll(snapshots, sectors, command.FlankBuckets, enemy);
                if (evidence.Count != n) continue;

                if (!_states.TryGetValue(command.ParentInstanceId, out ReserveReleaseState state))
                {
                    state = new ReserveReleaseState();
                    _states[command.ParentInstanceId] = state;
                }
                var roles = (DirectChildRole[])command.BaseRoles.Clone();
                var context = new ReserveReleaseContext(globalOdds, flankRelease, state, null, nowHours, agg01, caution01, casualtyTolerance01, staggered);
                DirectChildAllocator.ReleaseReserves(plan, evidence, roles, context, snapshots, new GuardPoint[n]);

                for (int i = 0; i < n; i++)
                {
                    if (command.BaseRoles[i] != DirectChildRole.Reserve || roles[i] == DirectChildRole.Reserve) continue;
                    int id = command.ChildIds[i];
                    _released[id] = roles[i];
                    var release = new LeafRelease(command.ParentInstanceId, id, roles[i], ReasonOf(state, snapshots[i].ChildId, roles[i], out int held, out int floor), held, floor);
                    _releases.Add(release);
                    if (!before.TryGetValue(id, out DirectChildRole was) || was != roles[i]) _fresh.Add(release);
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
