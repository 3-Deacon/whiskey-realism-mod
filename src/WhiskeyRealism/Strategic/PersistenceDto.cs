using System.Collections.Generic;
using Newtonsoft.Json;

namespace WhiskeyRealism.Strategic
{
    internal class SidecarDto
    {
        [JsonProperty("version")] public int Version = 1;
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
    }

    internal class CICDto
    {
        [JsonProperty("officerName")] public string OfficerName;
        [JsonProperty("personality")] public PersonalityDto Personality;
        [JsonProperty("activePlan")]  public OperationalPlanDto ActivePlan;
    }

    internal class OperationalPlanDto
    {
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
        [JsonProperty("targetAreaId")]          public int    TargetAreaId;
        [JsonProperty("targetObjectiveId")]     public int    TargetObjectiveId;
        [JsonProperty("forceFractionRequired")] public float  ForceFractionRequired;
        [JsonProperty("transition")]            public string Transition;
        [JsonProperty("deadlineMonth")]         public int    DeadlineMonth;
        [JsonProperty("deadlineYear")]          public int    DeadlineYear;
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
        [JsonProperty("lastChecked")]   public string    LastChecked;
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
}
