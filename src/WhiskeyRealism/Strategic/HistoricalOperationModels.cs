namespace WhiskeyRealism.Strategic
{
    public enum OperationChapterPolicy
    {
        Exact,
        AllowDateDrift
    }

    public enum OperationTempoPreset
    {
        Deliberate,
        Standard,
        Press,
        Exploit,
        Recover
    }

    public enum OperationPosture
    {
        Inherit,
        ProbeAndDevelop,
        ConcentratedAttack,
        ReinforceAndHold,
        Counterstroke,
        ScreenAndDelay,
        ExploitBreakthrough,
        Recover
    }

    public enum HistoricalOperationMatchKind
    {
        NoProfile,
        Matched
    }

    public enum OperationDynamicTrigger
    {
        ObjectiveUnavailable,
        ObjectiveAccomplished,
        TargetEngaged,
        MajorFriendlyVictoryNearTarget,
        MajorFriendlyDefeatNearTarget,
        EnemyThreatensCapitalCorridor,
        EnemyConcentratesInTheater,
        EmptyTarget,
        ForceBelowThreshold,
        ReplanThrash
    }

    public enum OperationDynamicAction
    {
        Continue,
        AdvancePhase,
        CompleteOperation,
        Recover,
        Pause,
        PivotToAlternateOperation,
        AbortOperation,
        Exploit,
        Counterstroke,
        ScreenAndDelay
    }

    public sealed class HistoricalOperationCandidate
    {
        public int ObjectiveId;
        public ObjectiveMetadata Objective;
        public float ObjectiveScore;
    }

    public sealed class HistoricalOperationContext
    {
        public bool ObjectiveAvailable;
        public bool ObjectiveAccomplished;
        public bool TargetPositionResolves;
        public bool TargetEngagedRecently;
        public bool MajorFriendlyVictoryNearTarget;
        public bool MajorFriendlyDefeatNearTarget;
        public bool EnemyThreatensCapitalCorridor;
        public bool EnemyConcentratesInTheater;
        public float TargetSectorOwnStrength;
        public float TargetSectorEnemyStrength;
        public float TargetSectorRatio;
        public float TheaterOwnPressure;
        public float TheaterEnemyPressure;
        public CampaignPace Pace;
        public StrategicIntent DirectorIntent;
        public CollapseRisk CollapseRisk;
        public int RecentReplanCount;
    }

    public sealed class HistoricalOperationProfile
    {
        public string OperationId;
        public string OperationName;
        public int AllianceId;
        public Theater Theater;
        public EraStage Era;
        public int MinChapter;
        public int MaxChapter;
        public int StartMonth;
        public int StartYear;
        public int EndMonth;
        public int EndYear;
        public int PrimaryObjectiveId;
        public int[] ObjectiveAllowList;
        public OperationChapterPolicy ChapterPolicy;
        public int Priority;
        public StrategyTag[] RequiredTags;
        public StrategyTag[] PreferredTags;
        public OperationTempoPreset Tempo;
        public OperationPosture Posture;
        public OperationPhaseTemplate[] Phases;
        public OperationDynamicRule[] DynamicRules;
        public string[] AlternateOperationIds;
        public float NearTargetRadius;
    }

    public sealed class OperationPhaseTemplate
    {
        public string PhaseId;
        public string PhaseName;
        public int TargetObjectiveId;
        public int TargetAreaId;
        public string TargetAreaKey;
        public string TargetSectorKey;
        public PhaseTransition Transition;
        public float ForceFractionRequired;
        public OperationPosture Posture;
        public bool AllowCoordinatedAttack;
        public bool AllowReinforcementPackage;
        public bool AllowProbeOnly;
        public int DeadlineDays;
    }

    public sealed class OperationDynamicRule
    {
        public string RuleId;
        public OperationDynamicTrigger Trigger;
        public OperationDynamicAction Action;
        public int Priority;
        public float MinOwnEnemyRatio;
        public float MaxOwnEnemyRatio;
        public float MinReadiness;
        public int WindowDays;
        public string AlternateOperationId;
        public string Reason;
    }

    public sealed class HistoricalOperationMatch
    {
        public HistoricalOperationMatchKind Kind;
        public HistoricalOperationProfile Profile;
        public float Score;
        public string Reason;
    }
}
