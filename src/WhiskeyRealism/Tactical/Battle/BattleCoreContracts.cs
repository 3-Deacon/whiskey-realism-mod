// Battle-core contracts (BC0, docs/superpowers/plans/2026-10-15-battle-core.md).
//
// Pure: no Unity or vanilla types. Every battle-core workstream (snapshot, perception,
// ownership, arbiter/executor, charge doctrine) is written against these identifiers and
// enum integer values, so do not renumber them. BattleSnapshot is declared in
// BattleSnapshot.cs (BC1).

namespace WhiskeyRealism.Tactical.Battle
{
    [System.Flags]
    public enum UnitFlags : uint
    {
        None = 0,
        Alive = 1,
        Active = 2,
        Routed = 4,
        OnRetreat = 8,
        InMelee = 16,
        PlayerOwned = 32,
        AncestorOfPlayerCommand = 64,
        PlayerCommandDetached = 128,
        Feud = 256,
        PermanentlyDetached = 512,
        Garrisoned = 1024,
        IsGroup = 2048,
        IsArtillery = 4096,
        IsCavalry = 8192,
        IsInfantry = 16384,
        PlayerCommand = 32768,
        LiedDown = 65536,
        MarkedForRout = 131072, // vanilla markedforrout: a cavalry leaf mounting up to rout (Rout 123555-123559)
        PathInterrupted = 262144, // vanilla pathinterrupted (111012): contact stopped the move (117330), restored at 117195
        // BC5a, additive: SlotOffsetX/Z hold this unit's entry in its command parent's
        // layout (groupposition.unitoffsetposition, 78378; the layout group is the
        // plan-D5 parent).
        HasLayoutSlot = 524288,
    }

    public struct UnitRecord
    {
        public int InstanceId;
        public int UnitTyp;
        public int Alliance;
        public int Commander;
        public float X, Y, Z, RotationY;
        public int Strength;
        public float GroupStrengthDirect;
        public int GroupOwnInRange, GroupStrengthAiGroup; // vanilla groupowninrange / groupstrengthaigroup
        public float Morale, BattleStartMorale, Ammo01, Fatigue, Experience;
        public int RegimentPaths, QueuedOrders, MovementMode, AiStance, AiStanceOrdered, Formation, CombatBehaviorOrdered;
        public float LastWaypointX, LastWaypointZ;
        // BC5a, additive (the leaf slot, shipped #61 TryFormationSlot): a group's
        // lastsetwaypointrotation (SetGroupFormation stores it with the waypoint,
        // 92067-92068), its own groupposition.groupwidth / groupdepth (78380-78382),
        // and, with UnitFlags.HasLayoutSlot, its entry in its parent's layout.
        public float LastWaypointRotation;
        public float GroupWidth, GroupDepth;
        public float SlotOffsetX, SlotOffsetZ;
        // Own-side state vanilla's fallback rules read (CheckLineFallbacks 5167):
        // covervalue (111404) and coverobject (111408; "no cover" = covervalue <= 0 or
        // coverobject 3), friendlyroutednear (111158; <= 0, clamped to [-1, 0] at 128253).
        public float CoverValue;
        public int CoverObject;
        public float FriendlyRoutedNear;
        public int ParentIndex;         // -1 = root
        public int FirstEnemyInRangeId; // enemyinrangereg[0], or 0
        public UnitFlags Flags;
    }

    public enum PerceptionMode
    {
        FogGrid = 0,
        RangeOnly = 1,
        OwnSideWithinFog = 2,
    }

    public enum PerceptionFallbackReason
    {
        None,
        FogDisabled,
        HqInTheSaddle,
        GridNotPainted,
        ConfigOff,
    }

    public enum ObservationState : byte
    {
        Visible = 1,
        Remembered = 2,
    }

    public struct EnemyObservation
    {
        public int InstanceId;
        public int UnitTyp;
        public float X, Z;
        public float Strength;
        public ObservationState State;
        public double LastSeenHours;
        public float Confidence01;
        public int HiddenTicks;
        public bool IsReinforcement;
    }

    public enum OrderChannel
    {
        Move = 0,
        Stance = 1,
        Formation = 2,
        Facing = 3,
        CombatBehavior = 4,
        Stop = 5,
    }

    public static class OrderChannels
    {
        public const int Count = 6;
    }

    // Higher value wins a claim on a (group, channel).
    public enum IntentPriority
    {
        Scorer = 1,
        Support = 2,
        Play = 3,
        Executor = 4,
        Safety = 5,
    }

    public enum IntentSource
    {
        PostureExecutor,
        FacingPulse,
        FireControl,
        Play,
        DivisionPlay,
        Withdrawal,
        ArtilleryDoctrine,
        ArtilleryPointBlank,
        ReserveRelease,
        ReserveCourierParity,
        GroupStanceScorer,
        ChargeDemotion,
        ChargeDoctrine,
        FeudReplay,
    }

    [System.Flags]
    public enum IntentFlags
    {
        None = 0,
        UseOrderDelay = 1,
        ReliefOrSupport = 2,
        ArtilleryFirePosition = 4,
        InContactWithdrawal = 8,
        DoubleQuick = 16,
        Limber = 32,
        Unlimber = 64,
        PerRegiment = 128,
        // BC5a, additive: route the move through vanilla AIBattle.BlockedMovingOrder
        // (3054-3150: NavMeshObstacles over the enemy-covered crossings of the
        // side's blockedcrossings, then SetWaypoint two frames later, UpdateOrders
        // 3382), as vanilla's own line-group moves over 150 m do (6953-6956).
        AvoidCoveredCrossings = 256,
    }

    // Refused flank of a group's formation or facing order (BC5a, additive).
    // None is the default so an intent built without it refuses nothing;
    // OrderExecutor.VanillaRefuseFlank maps it to SetGroupFormation's refuseflank
    // (-1 none, 0 left, 1 right; SkewedPosition 92095-92097).
    public enum RefuseFlank : byte
    {
        None = 0,
        Left = 1,
        Right = 2,
    }

    public struct BattleOrderIntent
    {
        public int GroupId;
        public OrderChannel Channel;
        public float TargetX, TargetZ;
        public float Heading;     // -1 = none
        public int Value;         // stance | formation | combat behavior
        public int FireTargetId;
        public IntentPriority Priority;
        public IntentSource Source;
        public IntentFlags Flags;
        public WhiskeyRealism.Tactical.Operations.CommandTaskType Task;
        public string Reason;     // static literal only
        public RefuseFlank Refuse; // Formation / Facing on a group (BC5a)
    }

    public interface IIntentSink
    {
        void Submit(in BattleOrderIntent intent);
    }

    public interface IBattleIntentProducer
    {
        string Name { get; }
        void Produce(BattleSnapshot snapshot, IIntentSink sink);
    }

    // Not persisted. 2 was Pending (no courier wait to observe, plan D6); the other
    // values keep their numbers.
    public enum OrderOutcomeKind
    {
        None = 0,
        Issued = 1,
        Moved = 3,
        Failed = 4,
        Retried = 5,
        GaveUp = 6,
        Skipped = 7,
        Deduped = 8,
        Superseded = 9,
    }

    public enum SkipReason
    {
        None,
        ModeNotActive,
        PlayerOwned,
        Feud,
        NotAiControlled,
        Routed,
        OnRetreat,
        Garrison,
        Inactive,
        ChainNoTargets,
        OffMap,
        Water,
        FriendlyFootprint,
        FriendlyFireLine,
        PointBlank,
        Cooldown,
        Duplicate,
        LowerPriorityThanClaim,
    }

    public struct OrderOutcome
    {
        public int GroupId;
        public OrderChannel Channel;
        public OrderOutcomeKind Kind;
        public SkipReason Reason;
        public IntentSource Source;
        public double AtHours;
        public int Retries;
    }
}
