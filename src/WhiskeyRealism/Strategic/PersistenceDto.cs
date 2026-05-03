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
    }

    internal class FactionDto
    {
        [JsonProperty("factionId")]   public int FactionId;
        [JsonProperty("factionName")] public string FactionName;
        [JsonProperty("currentEra")]  public string CurrentEra;
        [JsonProperty("cic")]         public CICDto Cic;
        [JsonProperty("theaterCommanders")] public List<TheaterCommanderDto> TheaterCommanders = new List<TheaterCommanderDto>();
    }

    internal class CICDto
    {
        [JsonProperty("officerName")] public string OfficerName;
        [JsonProperty("personality")] public PersonalityDto Personality;
        [JsonProperty("activePlan")]  public OperationalPlanDto ActivePlan;
    }

    internal class TheaterCommanderDto
    {
        [JsonProperty("theaterId")]   public int TheaterId;
        [JsonProperty("officerName")] public string OfficerName;
        [JsonProperty("personality")] public PersonalityDto Personality;
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
        [JsonProperty("firedEvents")] public List<int> FiredEvents = new List<int>();
        [JsonProperty("lastChecked")] public string    LastChecked;
    }
}
