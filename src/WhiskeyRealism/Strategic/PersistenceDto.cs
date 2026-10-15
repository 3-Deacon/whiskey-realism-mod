using System.Collections.Generic;
using Newtonsoft.Json;

namespace WhiskeyRealism.Strategic
{
    // Version history: 1 = v0.2.x; 2 = adds lastSeenMonth/Year, per-faction probe state and
    // fiscal memory, nullable director posture. Readers accept 1..CurrentVersion.
    internal class SidecarDto
    {
        internal const int CurrentVersion = 2;
        [JsonProperty("version")] public int Version = CurrentVersion;
        // Monotonic per campaign line: each save writes (last loaded/saved sequence + 1). Used
        // to choose the newest of .bak/.tmp after a crash, never file mtimes.
        [JsonProperty("saveSequence")] public long SaveSequence;
        [JsonProperty("lastSeenMonth")] public int? LastSeenMonth;
        [JsonProperty("lastSeenYear")] public int? LastSeenYear;
        [JsonProperty("factions")] public List<FactionDto> Factions = new List<FactionDto>();
        [JsonProperty("minorOfficerProfiles")] public List<MinorOfficerDto> MinorOfficerProfiles = new List<MinorOfficerDto>();
        [JsonProperty("succession")] public SuccessionDto Succession = new SuccessionDto();
        [JsonProperty("battleHistory")] public List<BattleHistoryDto> BattleHistory = new List<BattleHistoryDto>();
    }

    internal class FactionDto
    {
        [JsonProperty("factionId")]   public int FactionId;
        [JsonProperty("factionName")] public string FactionName;
        [JsonProperty("currentEra")]  public string CurrentEra;
        [JsonProperty("cic")]         public CICDto Cic;
        [JsonProperty("directorMemory")] public DirectorMemoryDto DirectorMemory;
        [JsonProperty("probeState")] public OperationalProbeStateDto ProbeState;
        [JsonProperty("fiscalMemory")] public FiscalMemoryDto FiscalMemory;
        [JsonProperty("modWrittenObjective")] public int? ModWrittenObjective;
        [JsonProperty("frontChangedDay")] public int? FrontChangedDay;
        [JsonProperty("capitalStreakDay")] public int? CapitalStreakDay;
        [JsonProperty("probeHysteresis")] public ProbeHysteresisDto ProbeHysteresis;
        // Stage E need-driven policy: smoothed needs (NeedKind order) and the ledger's pick.
        [JsonProperty("policyNeeds")] public float[] PolicyNeeds;
        [JsonProperty("policyPick")] public int? PolicyPick;
    }

    internal class OperationalProbeStateDto
    {
        [JsonProperty("probeId")] public string ProbeId;
        [JsonProperty("unitKey")] public string UnitKey;
        [JsonProperty("targetAreaKey")] public string TargetAreaKey;
        [JsonProperty("sourceSectorKey")] public string SourceSectorKey;
        [JsonProperty("startedDaySerial")] public int StartedDaySerial;
        [JsonProperty("lastEnemy")] public float LastObservedEnemyStrength;
        [JsonProperty("lastFriendly")] public float LastObservedFriendlyStrength;
    }

    internal class ProbeHysteresisDto
    {
        [JsonProperty("withdrawStreak")] public int WithdrawStreak;
        [JsonProperty("cooldownUntilDay")] public int CooldownUntilDay = int.MinValue;
        [JsonProperty("cooldownTargetKey")] public string CooldownTargetKey;
    }

    internal class FiscalMemoryDto
    {
        [JsonProperty("previousPosture")] public string PreviousPosture;
        [JsonProperty("stableTicksAboveEmergency")] public int StableTicksAboveEmergency;
        [JsonProperty("emergencyResidue")] public bool EmergencyResidue;
    }

    internal class CICDto
    {
        [JsonProperty("officerName")] public string OfficerName;
        [JsonProperty("personality")] public PersonalityDto Personality;
        [JsonProperty("activePlan")]  public OperationalPlanDto ActivePlan;
    }

    internal class OperationalPlanDto
    {
        [JsonProperty("operationId")] public string OperationId;
        [JsonProperty("operationName")] public string OperationName;
        [JsonProperty("operationTempo")] public string OperationTempo;
        [JsonProperty("operationPosture")] public string OperationPosture;
        [JsonProperty("operationStartedDaySerial")] public int OperationStartedDaySerial;
        [JsonProperty("operationLastDecisionDaySerial")] public int OperationLastDecisionDaySerial;
        [JsonProperty("pendingRetarget")] public bool PendingRetarget;
        [JsonProperty("pendingRetargetReason")] public string PendingRetargetReason;
        [JsonProperty("assignedTheaterId")] public int AssignedTheaterId;
        [JsonProperty("phases")] public List<PhaseDto> Phases = new List<PhaseDto>();
        [JsonProperty("currentPhaseIndex")] public int CurrentPhaseIndex;
        [JsonProperty("planDeadlineMonth")] public int PlanDeadlineMonth;
        [JsonProperty("planDeadlineYear")]  public int PlanDeadlineYear;
        [JsonProperty("rationale")] public string Rationale;
        [JsonProperty("isDirty")]   public bool   IsDirty;
    }

    internal class PhaseDto
    {
        [JsonProperty("phaseId")]               public string PhaseId;
        [JsonProperty("phaseName")]             public string PhaseName;
        [JsonProperty("targetAreaId")]          public int    TargetAreaId;
        [JsonProperty("targetObjectiveId")]     public int    TargetObjectiveId;
        [JsonProperty("targetAreaKey")]         public string TargetAreaKey;
        [JsonProperty("targetSectorKey")]       public string TargetSectorKey;
        [JsonProperty("forceFractionRequired")] public float  ForceFractionRequired;
        [JsonProperty("transition")]            public string Transition;
        [JsonProperty("deadlineMonth")]         public int    DeadlineMonth;
        [JsonProperty("deadlineYear")]          public int    DeadlineYear;
        [JsonProperty("operationPosture")]      public string OperationPosture;
        [JsonProperty("allowCoordinatedAttack")] public bool  AllowCoordinatedAttack;
        [JsonProperty("allowReinforcementPackage")] public bool AllowReinforcementPackage;
        [JsonProperty("allowProbeOnly")]        public bool   AllowProbeOnly;
        [JsonProperty("phaseStartedDaySerial")] public int    PhaseStartedDaySerial;
    }

    internal class PersonalityDto
    {
        [JsonProperty("agg")]  public float Aggression;
        [JsonProperty("caut")] public float Caution;
        [JsonProperty("aud")]  public float Audacity;
        [JsonProperty("cas")]  public float CasualtyTolerance;
        [JsonProperty("pol")]  public float PoliticalResponsiveness;

        public PersonalityVector ToVector() => new PersonalityVector(Aggression, Caution, Audacity, CasualtyTolerance, PoliticalResponsiveness);

        public static PersonalityDto FromVector(PersonalityVector v) => new PersonalityDto
        {
            Aggression = v.Aggression, Caution = v.Caution, Audacity = v.Audacity,
            CasualtyTolerance = v.CasualtyTolerance, PoliticalResponsiveness = v.PoliticalResponsiveness
        };
    }

    internal class MinorOfficerDto
    {
        [JsonProperty("commanderId")] public int CommanderId;
        [JsonProperty("personality")] public PersonalityDto Personality;
    }

    internal class SuccessionDto
    {
        [JsonProperty("firedEvents")]   public List<int> FiredEvents   = new List<int>();
        [JsonProperty("appliedEvents")] public List<int> AppliedEvents = new List<int>();   // v0.2.1 — track concrete-swap applications
    }

    internal class BattleHistoryDto
    {
        [JsonProperty("battleName")] public string BattleName;
        [JsonProperty("day")] public int Day;
        [JsonProperty("month")] public int Month;
        [JsonProperty("year")] public int Year;
        [JsonProperty("landOrSea")] public int LandOrSea;
        [JsonProperty("allianceWon")] public int AllianceWon;
        [JsonProperty("battleResultType")] public int BattleResultType;
        [JsonProperty("battleEndType")] public int BattleEndType;
        [JsonProperty("theater")] public string Theater;
        [JsonProperty("positionX")] public float PositionX;
        [JsonProperty("positionZ")] public float PositionZ;
        [JsonProperty("alliance")] public List<int> Alliance = new List<int>();
        [JsonProperty("commander")] public List<int> Commander = new List<int>();
        [JsonProperty("commanderName")] public List<string> CommanderName = new List<string>();
        [JsonProperty("casualties")] public List<int> Casualties = new List<int>();
        [JsonProperty("commanderKia")] public List<int> CommanderKia = new List<int>();
    }

    internal class DirectorMemoryDto
    {
        // Nullable: a sidecar without a published posture must not decode to an all-zero one.
        [JsonProperty("pace")]            public int? Pace;
        [JsonProperty("intent")]          public int? Intent;
        [JsonProperty("risk")]            public int? Risk;
        [JsonProperty("theaterPriority")] public int? TheaterPriority;
        [JsonProperty("lastFullRefresh")] public int LastFullRefreshDay = -1;
        [JsonProperty("capitalStreak")]   public int CapitalDangerStreakDays;
        [JsonProperty("daysSinceBattle")] public int DaysSinceLastBattle;
        [JsonProperty("sourceSig")]       public string LastSourceSignature;
        [JsonProperty("recentEvents")]    public List<string> RecentEventSummaries = new List<string>();
        [JsonProperty("recentOperationReplanDaySerials")] public int[] RecentOperationReplanDaySerials;
    }
}
