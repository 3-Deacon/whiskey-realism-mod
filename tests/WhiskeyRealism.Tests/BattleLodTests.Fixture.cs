using WhiskeyRealism.Tactical;
using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;

// BC15's fixture battle: a mutable own roster, a mutable enemy memory (with the real latch and drop log), a scripted task lookup and a
// scripted friendly corridor. Tick() builds the snapshot the way the runtime does (units, command tree, observations, the real
// SlicePressureBuilder), runs BattleLod.Classify on it and then consumes the latches, exactly as BattleCoreTick does.
internal static partial class BattleLodTests
{
    internal const double Cycle = 0.015;

    internal sealed class LodUnit
    {
        public int Id, ParentId, UnitTyp;
        public float X, Z, Rotation;
        public UnitFlags Flags = UnitFlags.Alive | UnitFlags.Active;
        public int RegimentPaths, MovementMode, QueuedOrders, Formation, AiStanceOrdered;
        public float LastWaypointX, LastWaypointZ;
        public bool ReceivedFire;
        public bool IsGroup => (Flags & UnitFlags.IsGroup) != 0;
    }

    internal sealed class LodTasks : ITaskLookup
    {
        public readonly Dictionary<int, CommandTaskType> Task = new Dictionary<int, CommandTaskType>();
        public readonly Dictionary<int, (float x, float z, float heading, int formation)> Target = new Dictionary<int, (float, float, float, int)>();
        public readonly HashSet<int> Latched = new HashSet<int>();
        // The operation's primary objective: the target of a group whose order carries none (the pipeline's objective approach).
        public bool HasObjective;
        public int ObjectiveGroupId;   // the one group whose order carries no target of its own
        public float ObjectiveX, ObjectiveZ;
        public readonly Dictionary<int, (float x, float z, float heading)> Slot = new Dictionary<int, (float, float, float)>();
        public LodWorld World;

        public CommandTaskType TaskFor(int groupId) => Task.TryGetValue(groupId, out CommandTaskType t) ? t : CommandTaskType.HoldObjective;

        public bool TryTarget(int groupId, out float x, out float z, out float maneuverHeading, out int orderFormation)
        {
            if (Target.TryGetValue(groupId, out var v))
            {
                x = v.x; z = v.z; maneuverHeading = v.heading; orderFormation = v.formation;
                return true;
            }
            x = z = 0f; maneuverHeading = -1f; orderFormation = -1;
            if (!HasObjective || groupId != ObjectiveGroupId) return false;
            x = ObjectiveX; z = ObjectiveZ;
            return true;
        }

        public bool HasLatchOrManeuver(int groupId) => Latched.Contains(groupId);

        // The #45 cached decision per group id (-1: none).
        public readonly Dictionary<int, int> Stance = new Dictionary<int, int>();

        public int StanceDecision(int ownIndex)
        {
            int id = World != null && ownIndex >= 0 && ownIndex < World.Units.Count ? World.IdAtIndex(ownIndex) : 0;
            return Stance.TryGetValue(id, out int v) ? v : -1;
        }

        public bool TrySlot(int ownIndex, out float x, out float z, out float heading)
        {
            int id = World != null && ownIndex >= 0 && ownIndex < World.Units.Count ? World.IdAtIndex(ownIndex) : 0;
            if (Slot.TryGetValue(id, out var v))
            {
                x = v.x; z = v.z; heading = v.heading;
                return true;
            }
            x = z = 0f; heading = -1f;
            return false;
        }
    }

    // The friendly units of the fixture that stand inside a corridor, as the runtime's grid would report them.
    internal sealed class LodCorridor : IFriendlyCorridor
    {
        public readonly List<(int id, float x, float z)> Friendly = new List<(int, float, float)>();

        public int Signature(int selfId, float x0, float z0, float x1, float z1, float widthMeters, out int count)
        {
            count = 0;
            int hash = 17;
            float ax = x1 - x0, az = z1 - z0;
            float len = (float)Math.Sqrt(ax * ax + az * az);
            if (len < 1f) return 0;
            ax /= len; az /= len;
            foreach (var f in Friendly)
            {
                if (f.id == selfId) continue;
                float rx = f.x - x0, rz = f.z - z0;
                float forward = rx * ax + rz * az;
                // The runtime grid skips what stands within CorridorMinForward of the group, so its own regiments are not corridor traffic.
                if (forward < 30f || forward > len) continue;
                float lateral = Math.Abs(rx * az - rz * ax);
                if (lateral > widthMeters / 2f) continue;
                count++;
                hash = unchecked(hash * 31 + f.id);
            }
            return count == 0 ? 0 : hash;   // an empty corridor hashes to 0 whatever its length, as the runtime grid's does
        }
    }

    internal sealed class LodWorld
    {
        public readonly List<LodUnit> Units = new List<LodUnit>();
        public readonly EnemyObservation[] Memory = new EnemyObservation[256];
        public int MemoryCount;
        public readonly ObservationDropLog Drops = new ObservationDropLog();
        public readonly BattleLodState State = new BattleLodState();
        public readonly OrderExecutor Executor = new OrderExecutor();
        public readonly LodTasks Tasks = new LodTasks();
        public readonly LodCorridor Corridor = new LodCorridor();
        public BattleSnapshot S = new BattleSnapshot();
        public double Hours = 10.0;
        public double CycleHours = Cycle;
        public int PlanSignature = 1;
        public long TickId;
        public int[] Descendants;
        public int[] Moving;
        public UnitFlags[] DescendantFlags;
        public bool UseCorridor = true;
        public readonly List<UnitRecord> EnemyRecords = new List<UnitRecord>();

        public LodWorld()
        {
            Tasks.World = this;
        }

        public int IdAtIndex(int index) => S != null && index < S.OwnCount ? S.Own[index].InstanceId : 0;

        public LodUnit Brigade(int id, int parentId, float x, float z)
        {
            return Add(new LodUnit { Id = id, ParentId = parentId, UnitTyp = TacticalUnitType.BattleGroupBrigade, X = x, Z = z, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup });
        }

        public LodUnit Division(int id, int parentId, float x, float z)
        {
            return Add(new LodUnit { Id = id, ParentId = parentId, UnitTyp = TacticalUnitType.BattleGroupDivision, X = x, Z = z, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup });
        }

        public LodUnit Regiment(int id, int parentId, float x, float z)
        {
            return Add(new LodUnit { Id = id, ParentId = parentId, UnitTyp = TacticalUnitType.Infantry, X = x, Z = z, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry });
        }

        private LodUnit Add(LodUnit u)
        {
            Units.Add(u);
            return u;
        }

        public LodUnit Unit(int id)
        {
            foreach (LodUnit u in Units) if (u.Id == id) return u;
            throw new ArgumentException("no unit " + id);
        }

        // A standing quiet brigade with two regiments, the unit of most fixtures. Returns the brigade.
        public LodUnit QuietBrigade(int id, float x, float z, int parentId = 0)
        {
            LodUnit b = Brigade(id, parentId, x, z);
            Regiment(id * 10 + 1, id, x - 30f, z);
            Regiment(id * 10 + 2, id, x + 30f, z);
            Corridor.Friendly.Add((id * 10 + 1, x - 30f, z));
            Corridor.Friendly.Add((id * 10 + 2, x + 30f, z));
            return b;
        }

        // An enemy the side sees: enters memory through the real Update, so it carries the real latch.
        public void SeeEnemy(int id, float x, float z, bool reinforcement = false)
        {
            var probe = PerceptionProbeResult.Create(1);
            probe.Count = 1;
            probe.EnemyIds[0] = id;
            probe.Visible[0] = true;
            probe.X[0] = x;
            probe.Z[0] = z;
            probe.Strength[0] = 500f;
            probe.UnitTyp[0] = 0;
            probe.IsReinforcement[0] = reinforcement;
            BattlePerception.Update(Memory, ref MemoryCount, in probe, Hours, Drops);
        }

        // An enemy the side lost sight of long enough ago to be Remembered, at the given confidence.
        public void RememberEnemy(int id, float x, float z, float confidence)
        {
            SeeEnemy(id, x, z);
            int i = IndexOfEnemy(id);
            Memory[i].State = ObservationState.Remembered;
            Memory[i].Confidence01 = confidence;
            Memory[i].HiddenTicks = BattlePerception.HideConfirmTicks;
            Memory[i].ChangedSinceLastTier = false;
        }

        public int IndexOfEnemy(int id)
        {
            for (int i = 0; i < MemoryCount; i++) if (Memory[i].InstanceId == id) return i;
            return -1;
        }

        public void MoveEnemy(int id, float x, float z)
        {
            int i = IndexOfEnemy(id);
            Memory[i].X = x;
            Memory[i].Z = z;
        }

        public void ForgetEnemy(int id)
        {
            var probe = PerceptionProbeResult.Create(1);
            probe.Count = 1;
            probe.EnemyIds[0] = id;
            probe.Visible[0] = true;
            probe.X[0] = Memory[IndexOfEnemy(id)].X;
            probe.Z[0] = Memory[IndexOfEnemy(id)].Z;
            probe.Strength[0] = 500f;
            probe.Routed[0] = true;
            BattlePerception.Update(Memory, ref MemoryCount, in probe, Hours, Drops);
        }

        // One doctrine tick: advance the clock one cycle, build the snapshot, classify, consume the latches.
        public void Tick(double advance = -1.0)
        {
            Hours += advance >= 0.0 ? advance : CycleHours;
            Build();
            Classify();
        }

        public void Build()
        {
            TickId++;
            S.BeginBuild(1, 0, 0, TickId, Hours);
            foreach (LodUnit u in Units)
            {
                S.AddOwn(new UnitRecord
                {
                    InstanceId = u.Id, UnitTyp = u.UnitTyp, Alliance = 0, X = u.X, Z = u.Z, RotationY = u.Rotation, Flags = u.Flags,
                    RegimentPaths = u.RegimentPaths, MovementMode = u.MovementMode, QueuedOrders = u.QueuedOrders, ReceivedFire = u.ReceivedFire,
                    Strength = 500, Formation = u.Formation, AiStanceOrdered = u.AiStanceOrdered, LastWaypointX = u.LastWaypointX, LastWaypointZ = u.LastWaypointZ,
                });
            }
            var index = new Dictionary<int, int>();
            for (int i = 0; i < Units.Count; i++) index[Units[i].Id] = i;
            for (int i = 0; i < Units.Count; i++)
                S.Own[i].ParentIndex = Units[i].ParentId != 0 && index.TryGetValue(Units[i].ParentId, out int p) ? p : -1;
            S.SetSliceCount(20);
            S.IndexCommandTree(null);
            for (int i = 0; i < MemoryCount; i++) S.AddObservation(in Memory[i]);
            S.SetDroppedObservations(Drops);
            for (int e = 0; e < EnemyRecords.Count; e++) S.AddEnemy(EnemyRecords[e]);
            SlicePressureBuilder.Build(S, new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f });
            PostureSense.AggregateDescendants(S, ref DescendantFlags, ref Moving);
        }

        public LodSideInput Side()
        {
            return new LodSideInput
            {
                CycleHours = CycleHours, PlanSignature = PlanSignature, Executor = Executor, Tasks = Tasks,
                Friendly = UseCorridor ? Corridor : null, MovingDescendants = Moving,
            };
        }

        public void Classify()
        {
            BattleLod.Classify(S, State, Side(), Hours);
            BattlePerception.ClearChangeLatch(Memory, MemoryCount, Drops);
            Executor.ClearRefusals();
        }

        // Tier answers by unit id for the snapshot of the last tick.
        public bool Due(int id) => State.IsDue(S.IndexOfOwn(id));
        public LodTier Tier(int id) => State.TierOf(S.IndexOfOwn(id));
        public LodPromotion Why(int id) => State.LastPromotion(S.IndexOfOwn(id));

        // Ticks until the group is Distant (a quiet fixture enters it on the third tick); throws if it never does.
        public void TickUntilDistant(int id, int limit = 8)
        {
            for (int t = 0; t < limit; t++)
            {
                Tick();
                if (Tier(id) == LodTier.Distant) return;
            }
            throw new Exception("group " + id + " never became Distant in " + limit + " ticks");
        }
    }
}
