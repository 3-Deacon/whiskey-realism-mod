using System.Collections.Generic;
using Newtonsoft.Json;

namespace WhiskeyRealism.Strategic
{
    // Version history: 1 = v0.2.x; 2 = adds lastSeenMonth/Year, per-faction probe state and
    // fiscal memory, nullable director posture; 3 = campaign planner: plan state, beliefs, task ledgers;
    // CIC plans dropped. Readers accept 1..CurrentVersion; a v1/v2 file decodes with no planner state
    // (the removed members are ignored) and its saved plans are counted in PlansDropped.
    // Optional fields added without a version bump (older readers ignore them, a
    // missing field decodes to null): playerCompliance (stage F, 2026-10-15; W&L order
    // reliability {careerKey, reliability, samples}, PlayerComplianceRuntime).
    internal class SidecarDto
    {
        internal const int CurrentVersion = 3;
        [JsonProperty("version")] public int Version = CurrentVersion;
        // Monotonic per campaign line: each save writes (last loaded/saved sequence + 1). Used
        // to choose the newest of .bak/.tmp after a crash, never file mtimes.
        [JsonProperty("saveSequence")] public long SaveSequence;
        [JsonProperty("lastSeenMonth")] public int? LastSeenMonth;
        [JsonProperty("lastSeenYear")] public int? LastSeenYear;
        [JsonProperty("factions")] public List<FactionDto> Factions = new List<FactionDto>();
        [JsonProperty("succession")] public SuccessionDto Succession = new SuccessionDto();
        [JsonProperty("battleHistory")] public List<BattleHistoryDto> BattleHistory = new List<BattleHistoryDto>();
        // W&L player-compliance estimate (stage F); null when none. Older readers ignore it.
        [JsonProperty("playerCompliance")] public PlayerComplianceDto PlayerCompliance;
        // W&L campaign courier queue (order flow, 2026-10-15): campaign orders still on their way to the
        // player's panel, so a save inside the delay loses none; null when none. Older readers ignore it.
        [JsonProperty("campaignCouriers")] public List<CampaignCourierDto> CampaignCouriers;
        // A v1/v2 file's saved operational plans (CIC activePlan), counted at read: the planner starts fresh. Not serialized.
        [JsonIgnore] public int PlansDropped;
    }

    // One pending campaign order. The unit is the GameObject name plus its commander id (instance ids do
    // not survive a save); times are campaign years since 1860 (GameVars.currenttimefromstart).
    internal class CampaignCourierDto
    {
        [JsonProperty("unit")] public string Unit;
        [JsonProperty("unitCommander")] public int UnitCommander;
        [JsonProperty("type")] public int Type;
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
        [JsonProperty("z")] public float Z;
        [JsonProperty("name")] public string Name;
        [JsonProperty("rotation")] public float Rotation;
        [JsonProperty("width")] public float Width;
        [JsonProperty("depth")] public float Depth;
        [JsonProperty("origin")] public string Origin;
        [JsonProperty("issuer")] public int Issuer;
        // The player's command the order was queued for (unit GameObject name + "#" + chosen commander id);
        // null in a sidecar written before the identity existed.
        [JsonProperty("playerCommand")] public string PlayerCommand;
        [JsonProperty("issuedAt")] public double IssuedAt;
        [JsonProperty("dueAt")] public double DueAt;
    }

    internal class PlayerComplianceDto
    {
        [JsonProperty("careerKey")] public string CareerKey;
        [JsonProperty("reliability")] public float Reliability;
        [JsonProperty("samples")] public int Samples;
    }

    internal class FactionDto
    {
        [JsonProperty("factionId")]   public int FactionId;
        [JsonProperty("factionName")] public string FactionName;
        [JsonProperty("currentEra")]  public string CurrentEra;
        [JsonProperty("directorMemory")] public DirectorMemoryDto DirectorMemory;
        [JsonProperty("fiscalMemory")] public FiscalMemoryDto FiscalMemory;
        [JsonProperty("modWrittenObjective")] public int? ModWrittenObjective;
        [JsonProperty("frontChangedDay")] public int? FrontChangedDay;
        [JsonProperty("capitalStreakDay")] public int? CapitalStreakDay;
        // Stage E need-driven policy: smoothed needs (NeedKind order) and the ledger's pick.
        [JsonProperty("policyNeeds")] public float[] PolicyNeeds;
        [JsonProperty("policyPick")] public int? PolicyPick;
        // v3: the campaign planner's state; null in a v1/v2 file.
        [JsonProperty("planner")] public PlannerStateDto Planner;
    }

    // One alliance's planner state (PlannerSnapshot). No field is a Unity instance id: a formation is its unitKey
    // (name:commander) and is rebound to this session's formation on the first review after a load (PlannerRebind).
    internal class PlannerStateDto
    {
        [JsonProperty("currentMainObjective")] public int CurrentMainObjective = -1;
        [JsonProperty("challengerObjective")] public int ChallengerObjective = -1;
        [JsonProperty("challengerDays")] public int ChallengerDays;
        [JsonProperty("decisionSerial")] public int DecisionSerial;
        [JsonProperty("campaignSeed")] public long CampaignSeed;
        [JsonProperty("belowOddsDays")] public int BelowOddsDays;
        [JsonProperty("capitalThreatLatched")] public bool CapitalThreatLatched;
        // The decision whose main-effort launch the chapter tempo gate released, and the day serial of the previous
        // executors-allowed Full review (the tempo clock). Absent in a file written before the field: -1.
        [JsonProperty("tempoReleasedDecision")] public int TempoReleasedDecision = -1;
        [JsonProperty("lastFullReviewDay")] public int LastFullReviewDay = -1;
        // D6 (smoke amendments 2026-10-16): the one reinforcement of the current decision and the home department of every top formation
        // by unit key (a Theater name). Absent in a file written before the fields: no reinforcement, no departments (re-derived).
        [JsonProperty("reinforcementKey")] public string ReinforcementKey;
        [JsonProperty("reinforcementDecision")] public int ReinforcementDecision = -1;
        // The decision whose main objective was seen scouted (a march released on a fresh picture is not re-staged); absent: -1.
        [JsonProperty("scoutedDecision")] public int ScoutedDecision = -1;
        [JsonProperty("departments")] public List<DepartmentDto> Departments = new List<DepartmentDto>();
        [JsonProperty("unreliableDays")] public int UnreliableDays;
        [JsonProperty("unreliableLastDay")] public int UnreliableLastDay = int.MinValue;
        [JsonProperty("beliefs")] public List<EnemyBeliefDto> Beliefs = new List<EnemyBeliefDto>();
        [JsonProperty("appointments")] public List<AppointmentDto> Appointments = new List<AppointmentDto>();
        [JsonProperty("depotTasks")] public List<DepotTaskDto> DepotTasks = new List<DepotTaskDto>();
        [JsonProperty("moves")] public List<MoveDto> Moves = new List<MoveDto>();
        [JsonProperty("reconMissions")] public List<ReconMissionDto> ReconMissions = new List<ReconMissionDto>();
        // G8: the garrison ledger, D8's secession posts and the ordinary ones. Additive in v3: an older file has none.
        [JsonProperty("garrisonPosts")] public List<GarrisonPostDto> GarrisonPosts = new List<GarrisonPostDto>();
    }

    internal class GarrisonPostDto
    {
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("x")] public float X;
        [JsonProperty("z")] public float Z;
        // Every member's unit key (name:commander) in seat order; empty for a post no formation held. "unitKey" is the first member's: what an
        // older file carries (it loads as one member) and what an older reader still finds.
        [JsonProperty("unitKey")] public string UnitKey;
        [JsonProperty("unitKeys")] public List<string> UnitKeys = new List<string>();
        [JsonProperty("quietSinceDay")] public int QuietSinceDay = -1;
    }

    internal class DepartmentDto
    {
        [JsonProperty("unitKey")] public string UnitKey;
        [JsonProperty("department")] public string Department;
    }

    internal class EnemyBeliefDto
    {
        [JsonProperty("unitKey")] public string UnitKey;
        [JsonProperty("unitTyp")] public int UnitTyp;
        [JsonProperty("x")] public float X;
        [JsonProperty("z")] public float Z;
        [JsonProperty("strength")] public float Strength;
        [JsonProperty("lastSeenDay")] public int LastSeenDay;
        [JsonProperty("confidence")] public float Confidence;
        [JsonProperty("radius")] public float Radius;
        [JsonProperty("source")] public int Source;
        [JsonProperty("traits")] public int Traits;
        [JsonProperty("lastEmbarkedDay")] public int LastEmbarkedDay = -1;
    }

    internal class AppointmentDto
    {
        [JsonProperty("commanderId")] public int CommanderId;
        [JsonProperty("protectedUntilDay")] public int ProtectedUntilDay;
    }

    internal class DepotTaskDto
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("z")] public float Z;
        [JsonProperty("sectorId")] public int SectorId;
        [JsonProperty("builderUnitKey")] public string BuilderUnitKey;
        [JsonProperty("state")] public string State;
        [JsonProperty("startedDay")] public int StartedDay;
        [JsonProperty("endedDay")] public int EndedDay = -1;
        [JsonProperty("wishX")] public float WishX;
        [JsonProperty("wishZ")] public float WishZ;
    }

    internal class MoveDto
    {
        [JsonProperty("unitKey")] public string UnitKey;
        [JsonProperty("tx")] public float TargetX;
        [JsonProperty("tz")] public float TargetZ;
        [JsonProperty("issuedDay")] public int IssuedDay;
        [JsonProperty("retries")] public int Retries;
        // The objective the move went toward, -1 for a move toward none.
        [JsonProperty("objectiveId")] public int ObjectiveId = -1;
    }

    internal class ReconMissionDto
    {
        [JsonProperty("missionId")] public int MissionId;
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("unitKey")] public string UnitKey;
        [JsonProperty("holdX")] public float HoldX;
        [JsonProperty("holdZ")] public float HoldZ;
        [JsonProperty("sectorId")] public int SectorId;
        [JsonProperty("state")] public string State;
        [JsonProperty("issuedDay")] public int IssuedDay;
        [JsonProperty("reason")] public string Reason;
        // A probe's remembered enemy reaction (-1 before its first look) and the objective it was seen at.
        [JsonProperty("observedEnemyStrength")] public float ObservedEnemyStrength = -1f;
        [JsonProperty("observedObjectiveId")] public int ObservedObjectiveId = -1;
        // The executor's stand-down debt (Scouting taken off the screen) and the day the mission was seen Cleared.
        [JsonProperty("standDownDue")] public bool StandDownDue;
        [JsonProperty("clearedDay")] public int ClearedDay = -1;
    }

    internal class FiscalMemoryDto
    {
        [JsonProperty("previousPosture")] public string PreviousPosture;
        [JsonProperty("stableTicksAboveEmergency")] public int StableTicksAboveEmergency;
        [JsonProperty("emergencyResidue")] public bool EmergencyResidue;
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
    }
}
