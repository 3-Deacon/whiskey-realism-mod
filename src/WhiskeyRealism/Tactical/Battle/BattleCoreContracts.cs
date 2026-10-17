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
        // BC9, additive. A skirmisher (3) or scout (4) company DetachCompany split off a
        // regiment (parentregiment set, 90430-90515; UnitRecord.ParentRegimentId).
        DetachedCompany = 1048576,
        // Vanilla mounted >= 1 (111066): a mounted cavalry regiment.
        Mounted = 2097152,
        // Vanilla unitlinkedto != null (111136): UpdateUnitLink re-lays the unit to its
        // link slot (124396-124500), so a move of it lasts only until the next link cycle.
        Linked = 4194304,
        // BC14, additive. Vanilla ishorseartillery on an artillery regiment (GetSlowestSpeedOfGroup 91717-91720
        // scales its march speed by GamePrefs.horseartilleryspeedfactor).
        HorseArtillery = 8388608,
    }

    public struct UnitRecord
    {
        public int InstanceId;
        public int UnitTyp;
        public int Alliance;
        public int Commander;
        public float X, Y, Z, RotationY;
        public int Strength;
        public int GroupOwnInRange, GroupStrengthAiGroup; // vanilla groupowninrange / groupstrengthaigroup
        public float Morale, BattleStartMorale, Ammo01, Fatigue;
        // BC8 troop quality: a regiment's Experience() (140114, W&L player-unit effects
        // applied) and raw training (never GetTraining(), which writes training under W&L,
        // 140537-140549); a group's groupexperience / grouptraining (UpdateGroupStrength
        // 120473 / 120508, divided 120671 / 120674).
        public float Experience, Training;
        // BC8, groups only (0 for a regiment): groupmorale, the mean morale of the group's
        // unrouted units (UpdateGroupStrength 120158 / 120670), GetGroupStrength's own morale
        // term (6054). A group record's Morale stays its own morale field.
        public float GroupMorale;
        // BC8 break tolerance, own infantry/cavalry regiments only (0 otherwise or with the
        // weighting off): the loss fraction vanilla's rout test reads (LossesAreUnbearable
        // 139829-139843, the detached regiment's strength included) and the fraction at which it routs (GetUnbearableLossesTrigger 113363-113400,
        // TroopQuality.BreakLossFraction).
        public float LossFraction, BreakLossFraction;
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
        // BC10, additive: vanilla firerange (the unit's effective fire distance), captured for
        // own and enemy rows. CoveredCrossings reads an enemy's through its observation.
        public float FireRange;
        // BC11, additive. ReceivedFire: vanilla receivedfire.Count > 0 (111858): the
        // regiment took fire recently, which widens its micro turn reach from distancetoturntowardsenemy to its fire range (StopMicroUnitsCloseToEnemyAndRotateTowards
        // 4560-4565). LastAiOrder: a group's lastaiorder (110990; 4 + direction after a vanilla
        // FlankMove, 117107). UnitWidth: a regiment's GetWidth() (139382), the chord of
        // SkewedPosition's rotation (78131-78167); a group carries GroupWidth instead.
        public bool ReceivedFire;
        public int LastAiOrder;
        public float UnitWidth;
        // Charge doctrine inputs (BC6b): lastaichargetime (110798), the battle hour of
        // vanilla's last odds refusal (UpdateChargeTarget 122290-122292; 0 = never), and
        // the flank-sector code UpdateFlanking computes (122959-123175; 0 = not outflanked).
        // BC10: the builder no longer copies vanilla's outflanked (111488), which reads the
        // unfogged enemyinrangereg; PerceivedFlanking.Apply fills it from the units the side
        // sees (own rows and Visible enemy rows; every other row reads 0).
        public float LastAiChargeTime;
        public int Outflanked;
        // firefromflank (111492): flank code of fire this unit received recently
        // (receivedfire within timetolastfireforflank, UpdateFlanking 123208-123264).
        public int FireFromFlank;
        // groupaiobject (110820) instance id, 0 = none: the unit's vanilla AI group, the
        // group AddUnitToPool(overwriteaigroups) assigns to every descendant (7637-7648).
        public int AiGroupId;
        // BC9: a detached company's source regiment (parentregiment 111132, instance id);
        // 0 for every other unit.
        public int ParentRegimentId;
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
        // BC8, last seen while Visible (a Remembered entry keeps them): the star experience
        // the player sees (TroopQuality.PerceivedExperience, UpdateUnitIcon 121832-121852)
        // and the regiment's morale (BC6b precedent: a visible target's morale is read).
        // Reinforcement columns carry neither.
        public float Experience;
        public float Morale;
        // BC10, additive, last seen while Visible (a Remembered entry keeps them; BC8
        // precedent): the facing (eulerAngles.y), fire range, cover and whether the
        // regiment stood still (no paths). CoveredCrossings reads them, vanilla's
        // UpdateBlockedCrossings (7917-7973) inputs without its omniscient unit scan.
        public float Heading;
        public float FireRange;
        public float CoverValue;
        public int CoverObject;
        public bool Standing;
        // BC15 decision 2(a): the observation appeared, turned Visible or turned Remembered since the battle-AI tier pass last read it.
        // BattlePerception.Update sets it on every build (including deployment, battle-start and gated-off builds) and only
        // BattlePerception.ClearChangeLatch clears it, so a change seen between two arbiter ticks is not lost.
        public bool ChangedSinceLastTier;
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
        Recon, // BC9: the recon producer's scout moves and stops
        Deployment,        // BC10: #60's fog-honest deployment placement (an immediate-placement Move)
        DeploymentReserve, // BC10: #60's deployment reserve echelon placement; the allocator reads its outcome
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
        // side's perceived covered points (CoveredCrossings), then SetWaypoint two frames later, UpdateOrders
        // 3382), as vanilla's own line-group moves over 150 m do (6953-6956).
        AvoidCoveredCrossings = 256,
        // CombatBehavior, charge doctrine Disarm (BC6b): after the behavior write the
        // executor also leaves charge movement mode 3 with SetMovementMode(0), as
        // vanilla's own refusal does (122290); behavior alone never ends mode 3.
        LeaveChargeMode = 512,
        // BC10, Move only: the executor issues SetGroupFormation(immediateplacement: true,
        // newpath: true, ignoredeplyomentzone: false) and clears the placed tree's
        // lastsetwaypointposition and immediateunitplacement (85873-85883), #60's call.
        ImmediatePlacement = 1024,
        // BC10, Move only: the intent's two vias are a road route (RoadRoutePlanner: the first road entry and the last road exit). The leg between
        // them follows the road, which takes its own crossing: the crossing gate inserts no via there and only checks the crossing the leg passes.
        RoadRoute = 2048,
        // BC14W, CombatBehavior only (behavior 9): the artillery doctrine starts a bombardment of the focal assault's focal point as vanilla's
        // CheckAIBombardment does (3897-3900): the behavior, then the battery's bombardposition (the intent's target) and bombardrange. The executor's
        // OrderExecutorRuntime.StartBombard is its one writer.
        BombardStart = 4096,
        // BC14W, Stance only: the stance is the works hold's (HoldObjective, 2) for a group the guard holds in front of enemy works. The stance
        // ownership applies it after the army retreat and the charge demotion, yields to vanilla's weak-group screen, and against vanilla's own
        // attack stances (3, 4) takes neither the yield step nor the vanilla-assault refusal. (2048 is RoadRoute; the BC14W bombard start, 4096, is task 8's.)
        WorksHold = 8192,
        // BC14W, Move and Stop: the works hold's contest (MoveContest) re-issuing the hold of a group a move outside the stored orders is walking into
        // the works band. Exempt from the executor's recent-order window and courier cadence (InCooldown), so it lands at once; a failed-write backoff
        // still applies. (4096 is the BC14W bombard start's.)
        Contest = 16384,
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
        // BC10 road march, Move only (additive): up to two via points the executor sends the
        // group through before the target (RoadRoutePlanner). ViaCount 0 = a plain move.
        public int ViaCount;
        public float ViaX0, ViaZ0, ViaX1, ViaZ1;
        // With IntentFlags.RoadRoute: the BC14 crossing links the road takes between its two vias (RoadRoute.LinkCount / Link0 / Link1); 0 = unknown.
        public int RoadLinkCount, RoadLink0, RoadLink1;
        // BC13: the live exploit (ExploitEntry.Id, 1 and up) an intent carries out; 0 = none. The arbiter tick counts the
        // PlayerOwned skips and the player-child fan-out exclusions of such an intent against that exploit (playerSkips).
        public int ExploitId;
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
        /// <summary>A move across a river with no crossing the side knows (RiverCrossingRule).</summary>
        NoKnownCrossing,
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
