namespace WhiskeyRealism.Tactical.PlayerOrders
{
    internal enum PlayerOrderScope
    {
        Tactical,
        Campaign,
    }

    internal enum PlayerOrderProvenance
    {
        Unknown,
        Vanilla,
        WhiskeyTactical,
        WhiskeyCampaign,
    }

    internal enum PlayerOrderIntent
    {
        None,
        BuildSupplyDepot,
        BuildFort,
        DefendCapital,
        AdvanceToAssemblyArea,
        ProbeObjective,
        AttackObjective,
        SupportMainEffort,
        HoldObjective,
        FallBackToLine,
        RetreatToExit,
        RecoverFromCombat,
        ClearHoldTransition,
    }

    internal enum PlayerOrderDedupeDecisionKind
    {
        Issue,
        SuppressSignature,
        SuppressThrottle,
        YieldVanillaTransition,
        BlockedByVanillaDedupe,
        BlockedByScopePriority,
        BlockedByUnknownActiveOrder,
        BlockedByDisabledWrites,
        NoCandidate,
    }

    internal readonly struct PlayerOrderPoint
    {
        public PlayerOrderPoint(float x, float z, bool validExitPoint = false)
        {
            X = x;
            Z = z;
            ValidExitPoint = validExitPoint;
        }

        public float X { get; }
        public float Z { get; }
        public bool ValidExitPoint { get; }
    }

    internal readonly struct PlayerOrderVanillaMapping
    {
        public PlayerOrderVanillaMapping(int type, PlayerOrderIntent intent)
        {
            Type = type;
            Intent = intent;
        }

        public int Type { get; }
        public PlayerOrderIntent Intent { get; }
    }

    internal readonly struct PlayerOrderCandidate
    {
        public PlayerOrderCandidate(
            PlayerOrderScope scope,
            PlayerOrderIntent intent,
            int vanillaType,
            int priority,
            string unitKey,
            string battleIdentity,
            int givenOrderSession,
            PlayerOrderPoint targetPoint,
            float rotation,
            string objectiveKey,
            string reason,
            bool activeCampaignActionable,
            bool campaignGroupFlag,
            bool validExitPoint = false)
        {
            Scope = scope;
            Intent = intent;
            VanillaType = vanillaType;
            Priority = priority;
            UnitKey = unitKey ?? string.Empty;
            BattleIdentity = battleIdentity ?? string.Empty;
            GivenOrderSession = givenOrderSession;
            TargetPoint = targetPoint;
            Rotation = rotation;
            ObjectiveKey = objectiveKey ?? string.Empty;
            Reason = reason ?? string.Empty;
            ActiveCampaignActionable = activeCampaignActionable;
            CampaignGroupFlag = campaignGroupFlag;
            ValidExitPoint = validExitPoint || targetPoint.ValidExitPoint;
        }

        public PlayerOrderScope Scope { get; }
        public PlayerOrderIntent Intent { get; }
        public int VanillaType { get; }
        public int Priority { get; }
        public string UnitKey { get; }
        public string BattleIdentity { get; }
        public int GivenOrderSession { get; }
        public PlayerOrderPoint TargetPoint { get; }
        public float Rotation { get; }
        public string ObjectiveKey { get; }
        public string Reason { get; }
        public bool ActiveCampaignActionable { get; }
        public bool CampaignGroupFlag { get; }
        public bool ValidExitPoint { get; }

        public bool HasCandidate => Intent != PlayerOrderIntent.None && VanillaType >= 0 && !string.IsNullOrEmpty(UnitKey);
    }

    internal readonly struct PlayerOrderActiveSnapshot
    {
        public PlayerOrderActiveSnapshot(
            PlayerOrderScope scope,
            PlayerOrderIntent intent,
            int vanillaType,
            int priority,
            string unitKey,
            string battleIdentity,
            int givenOrderSession,
            PlayerOrderPoint targetPoint,
            float rotation,
            string objectiveKey,
            string reason,
            bool activeCampaignActionable,
            bool campaignGroupFlag,
            PlayerOrderProvenance provenance,
            bool battleEnded = false,
            bool stale = false)
        {
            Scope = scope;
            Intent = intent;
            VanillaType = vanillaType;
            Priority = priority;
            UnitKey = unitKey ?? string.Empty;
            BattleIdentity = battleIdentity ?? string.Empty;
            GivenOrderSession = givenOrderSession;
            TargetPoint = targetPoint;
            Rotation = rotation;
            ObjectiveKey = objectiveKey ?? string.Empty;
            Reason = reason ?? string.Empty;
            ActiveCampaignActionable = activeCampaignActionable;
            CampaignGroupFlag = campaignGroupFlag;
            Provenance = provenance;
            BattleEnded = battleEnded;
            Stale = stale;
        }

        public PlayerOrderScope Scope { get; }
        public PlayerOrderIntent Intent { get; }
        public int VanillaType { get; }
        public int Priority { get; }
        public string UnitKey { get; }
        public string BattleIdentity { get; }
        public int GivenOrderSession { get; }
        public PlayerOrderPoint TargetPoint { get; }
        public float Rotation { get; }
        public string ObjectiveKey { get; }
        public string Reason { get; }
        public bool ActiveCampaignActionable { get; }
        public bool CampaignGroupFlag { get; }
        public PlayerOrderProvenance Provenance { get; }
        public bool BattleEnded { get; }
        public bool Stale { get; }
        public bool HasActiveOrder => VanillaType >= 0;
    }

    internal readonly struct PlayerOrderSignature
    {
        public PlayerOrderSignature(
            PlayerOrderScope scope,
            PlayerOrderIntent intent,
            int vanillaType,
            string unitKey,
            string battleIdentity,
            int givenOrderSession,
            PlayerOrderPoint targetPoint,
            float rotation,
            string objectiveKey)
        {
            Scope = scope;
            Intent = intent;
            VanillaType = vanillaType;
            UnitKey = unitKey ?? string.Empty;
            BattleIdentity = battleIdentity ?? string.Empty;
            GivenOrderSession = givenOrderSession;
            TargetPoint = targetPoint;
            RotationBucket = Bucket(rotation);
            XBucket = Bucket(targetPoint.X);
            ZBucket = Bucket(targetPoint.Z);
            ObjectiveKey = objectiveKey ?? string.Empty;
        }

        public PlayerOrderScope Scope { get; }
        public PlayerOrderIntent Intent { get; }
        public int VanillaType { get; }
        public string UnitKey { get; }
        public string BattleIdentity { get; }
        public int GivenOrderSession { get; }
        public PlayerOrderPoint TargetPoint { get; }
        public int RotationBucket { get; }
        public int XBucket { get; }
        public int ZBucket { get; }
        public string ObjectiveKey { get; }

        public static PlayerOrderSignature FromCandidate(PlayerOrderCandidate candidate)
        {
            return new PlayerOrderSignature(
                candidate.Scope,
                candidate.Intent,
                candidate.VanillaType,
                candidate.UnitKey,
                candidate.BattleIdentity,
                candidate.GivenOrderSession,
                candidate.TargetPoint,
                candidate.Rotation,
                candidate.ObjectiveKey);
        }

        public bool MaterialEquals(PlayerOrderSignature other)
        {
            return Scope == other.Scope &&
                Intent == other.Intent &&
                VanillaType == other.VanillaType &&
                UnitKey == other.UnitKey &&
                BattleIdentity == other.BattleIdentity &&
                GivenOrderSession == other.GivenOrderSession &&
                XBucket == other.XBucket &&
                ZBucket == other.ZBucket &&
                RotationBucket == other.RotationBucket &&
                ObjectiveKey == other.ObjectiveKey;
        }

        public bool MatchesActiveOrder(PlayerOrderActiveSnapshot active)
        {
            return Scope == active.Scope &&
                (active.Intent == PlayerOrderIntent.None || Intent == active.Intent) &&
                VanillaType == active.VanillaType &&
                UnitKey == active.UnitKey &&
                BattleIdentity == active.BattleIdentity &&
                GivenOrderSession == active.GivenOrderSession &&
                XBucket == Bucket(active.TargetPoint.X) &&
                ZBucket == Bucket(active.TargetPoint.Z) &&
                RotationBucket == Bucket(active.Rotation) &&
                ObjectiveKey == active.ObjectiveKey;
        }

        private static int Bucket(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0;
            }

            return (int)System.Math.Round(value / 10f);
        }
    }

    internal readonly struct PlayerOrderShadow
    {
        public PlayerOrderShadow(PlayerOrderSignature signature, long tick, string battleIdentity)
            : this(signature, signature, tick, battleIdentity)
        {
        }

        public PlayerOrderShadow(
            PlayerOrderSignature requestSignature,
            PlayerOrderSignature activeSignature,
            long tick,
            string battleIdentity)
        {
            RequestSignature = requestSignature;
            ActiveSignature = activeSignature;
            Tick = tick;
            BattleIdentity = battleIdentity ?? string.Empty;
        }

        public PlayerOrderSignature Signature => RequestSignature;
        public PlayerOrderSignature RequestSignature { get; }
        public PlayerOrderSignature ActiveSignature { get; }
        public long Tick { get; }
        public string BattleIdentity { get; }
    }

    internal readonly struct PlayerOrderDedupeOptions
    {
        public PlayerOrderDedupeOptions(bool writesEnabled, long throttleTicks)
        {
            WritesEnabled = writesEnabled;
            ThrottleTicks = throttleTicks < 0 ? 0 : throttleTicks;
        }

        public bool WritesEnabled { get; }
        public long ThrottleTicks { get; }

        public static PlayerOrderDedupeOptions Default => new PlayerOrderDedupeOptions(true, 120);
    }

    internal readonly struct PlayerOrderDedupeDecision
    {
        public PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind kind, string reason)
        {
            Kind = kind;
            Reason = reason ?? string.Empty;
        }

        public PlayerOrderDedupeDecisionKind Kind { get; }
        public string Reason { get; }
        public bool ShouldIssue => Kind == PlayerOrderDedupeDecisionKind.Issue;
    }
}
