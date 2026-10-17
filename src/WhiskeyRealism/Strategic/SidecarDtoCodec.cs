using System;
using System.Collections.Generic;
using System.IO;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Strategic.Planner;

namespace WhiskeyRealism.Strategic
{
    // Plain container for everything the sidecar persists. Load decodes into a fresh
    // snapshot and the coordinator swaps it in only when decoding fully succeeded
    // (transactional apply — PER-version).
    internal sealed class SidecarSnapshot
    {
        public EraStage?[] Eras = new EraStage?[2];
        public DirectorMemory[] DirectorMemories = { new DirectorMemory(), new DirectorMemory() };
        public FiscalStateMemory[] FiscalMemories = { new FiscalStateMemory(), new FiscalStateMemory() };
        public HashSet<int> FiredEvents = new HashSet<int>();
        public HashSet<int> AppliedEvents = new HashSet<int>();
        // Ids marked fired by the ForceAllSuccessionEvents test flag; never persisted (PER-force).
        public HashSet<int> ForcedEvents = new HashSet<int>();
        public List<BattleHistoryRecord> Battles = new List<BattleHistoryRecord>();
        public int LastSeenMonth = -1;
        public int LastSeenYear = -1;
        public long SaveSequence;
        // followedcampaignobjective value the objective executor last wrote (ModWrittenObjective), per alliance;
        // null = unknown (sidecar written before the field existed).
        public int?[] ModWrittenObjective = { -1, -1 };
        // Runtime counters that would otherwise restart on every battle round-trip.
        public int[] FrontChangedDay = { -1, -1 };
        public int[] CapitalStreakDay = { -1, -1 };
        // The campaign planner's state per alliance (sidecar v3); null = a file without it (v1/v2): the planner starts fresh.
        public PlannerSnapshot[] Planner = new PlannerSnapshot[2];
        // The version of the file this snapshot was decoded from, and how many saved operational plans (v1/v2 CIC plans) it
        // dropped; the coordinator logs the migration once.
        public int SourceVersion = SidecarDto.CurrentVersion;
        public int PlansDropped;
        // W&L player compliance (PlayerComplianceRuntime); null when none.
        public PlayerComplianceDto PlayerCompliance;
        // W&L campaign courier queue (order flow); null or empty when none.
        public List<CampaignCourierDto> CampaignCouriers;
        // Stage E: smoothed policy needs (null = none yet) and the policy ledger pick (-1 = none).
        public float[][] PolicyNeeds = new float[2][];
        public int[] PolicyPicks = { -1, -1 };

        public static SidecarSnapshot Empty() => new SidecarSnapshot();
    }

    // R15/H3: decides when strategic state must be reset for a new campaign. Vanilla only
    // calls AICampaign.Load when SceneManagement.currentoperation is 5 (return from battle)
    // or 6 (load save) (CampaignController 30003-30023), so a brand-new campaign never hits
    // the load hook. Each campaign scene has its own AICampaign MonoBehaviour, so a new
    // instance seen in AICampaign.Update without a load for it means a new campaign.
    // The load path resets + applies the sidecar itself and marks its scene here, whichever
    // of Load / first Update happens first.
    internal sealed class CampaignEpochTracker
    {
        private object _lastInstance;
        private bool _loadPendingUnknownInstance;

        internal void NoteLoad(object aiCampaignInstance)
        {
            if (aiCampaignInstance != null)
            {
                _lastInstance = aiCampaignInstance;
                _loadPendingUnknownInstance = false;
            }
            else
            {
                _loadPendingUnknownInstance = true;
            }
        }

        // Log reason for a scene-change reset, from SceneManagement.currentoperation:
        // 3/8 = campaign battle (re)load, 5 = return from battle, 6 = load save
        // (SceneManagement 36640-36700, 36806; CampaignController 30003).
        internal static string ResetReason(int currentOperation)
        {
            switch (currentOperation)
            {
                case 3:
                case 8: return "battle-scene";
                case 5: return "battle-return-scene";
                case 6: return "save-load-scene";
                default: return "new-campaign-scene";
            }
        }

        internal bool ShouldResetOnUpdate(object aiCampaignInstance)
        {
            if (aiCampaignInstance == null || ReferenceEquals(aiCampaignInstance, _lastInstance)) return false;
            _lastInstance = aiCampaignInstance;
            if (_loadPendingUnknownInstance)
            {
                _loadPendingUnknownInstance = false;
                return false;
            }
            return true;
        }
    }

    // Pure sidecar codec (no Unity / BepInEx): JSON <-> SidecarDto <-> SidecarSnapshot, plus
    // the atomic file write. Harness-covered.
    internal static class SidecarDtoCodec
    {
        internal const int MaxBattleHistory = 64;

        internal static string Serialize(SidecarDto dto)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(dto, Newtonsoft.Json.Formatting.Indented);
        }

        // Rejects empty/corrupt JSON and sidecars written by a newer mod version; normalizes
        // missing collections so later decoding never dereferences null.
        internal static bool TryDeserialize(string json, out SidecarDto dto, out string reason)
        {
            dto = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                reason = "empty-file";
                return false;
            }
            try
            {
                dto = Newtonsoft.Json.JsonConvert.DeserializeObject<SidecarDto>(json);
            }
            catch (Exception ex)
            {
                reason = "corrupt-json: " + ex.Message;
                dto = null;
                return false;
            }
            if (dto == null)
            {
                reason = "null-document";
                return false;
            }
            if (dto.Version < 1 || dto.Version > SidecarDto.CurrentVersion)
            {
                reason = "unsupported-version " + dto.Version + " (supported 1.." + SidecarDto.CurrentVersion + ")";
                dto = null;
                return false;
            }
            dto.Factions = dto.Factions ?? new List<FactionDto>();
            if (dto.Version < SidecarDto.CurrentVersion) dto.PlansDropped = CountSavedPlans(json);
            dto.Succession = dto.Succession ?? new SuccessionDto();
            dto.Succession.FiredEvents = dto.Succession.FiredEvents ?? new List<int>();
            dto.Succession.AppliedEvents = dto.Succession.AppliedEvents ?? new List<int>();
            dto.BattleHistory = dto.BattleHistory ?? new List<BattleHistoryDto>();
            reason = "ok";
            return true;
        }

        internal static SidecarDto Encode(SidecarSnapshot snap)
        {
            var dto = new SidecarDto
            {
                SaveSequence = snap.SaveSequence,
                LastSeenMonth = snap.LastSeenMonth,
                LastSeenYear = snap.LastSeenYear,
                PlayerCompliance = snap.PlayerCompliance,
                CampaignCouriers = snap.CampaignCouriers != null && snap.CampaignCouriers.Count > 0 ? snap.CampaignCouriers : null
            };
            for (int alliance = 0; alliance < 2; alliance++)
            {
                dto.Factions.Add(new FactionDto
                {
                    FactionId = alliance,
                    FactionName = alliance == 1 ? "CSA" : "Union",
                    CurrentEra = (snap.Eras[alliance] ?? EraStage.Amateur1861).ToString(),
                    DirectorMemory = StrategicResilienceDirector.MemoryToDto(snap.DirectorMemories[alliance]),
                    ModWrittenObjective = snap.ModWrittenObjective[alliance],
                    FrontChangedDay = snap.FrontChangedDay[alliance],
                    CapitalStreakDay = snap.CapitalStreakDay[alliance],
                    FiscalMemory = FiscalToDto(snap.FiscalMemories[alliance]),
                    PolicyNeeds = snap.PolicyNeeds[alliance],
                    PolicyPick = snap.PolicyPicks[alliance] >= 0 ? snap.PolicyPicks[alliance] : (int?)null,
                    Planner = PlannerToDto(snap.Planner[alliance])
                });
            }
            foreach (int id in snap.FiredEvents)
                if (!snap.ForcedEvents.Contains(id)) dto.Succession.FiredEvents.Add(id);
            dto.Succession.FiredEvents.Sort();
            dto.Succession.AppliedEvents.AddRange(snap.AppliedEvents);
            dto.Succession.AppliedEvents.Sort();
            foreach (var battle in snap.Battles)
            {
                var battleDto = BattleToDto(battle);
                if (battleDto != null) dto.BattleHistory.Add(battleDto);
            }
            return dto;
        }

        // Builds a complete snapshot from a validated DTO. Returns false (and a reason) on any
        // failure; the caller then keeps its fresh state instead of a half-applied one.
        internal static bool TryDecode(SidecarDto dto, out SidecarSnapshot snap, out string reason)
        {
            snap = null;
            if (dto == null)
            {
                reason = "null-document";
                return false;
            }
            try
            {
                var result = SidecarSnapshot.Empty();
                foreach (var f in dto.Factions)
                {
                    if (f == null || f.FactionId < 0 || f.FactionId >= 2) continue;
                    int a = f.FactionId;
                    if (Enum.TryParse<EraStage>(f.CurrentEra, out var era)) result.Eras[a] = era;
                    result.DirectorMemories[a] = StrategicResilienceDirector.MemoryFromDto(f.DirectorMemory);
                    result.FiscalMemories[a] = FiscalFromDto(f.FiscalMemory);
                    result.ModWrittenObjective[a] = f.ModWrittenObjective;
                    result.FrontChangedDay[a] = f.FrontChangedDay ?? -1;
                    result.CapitalStreakDay[a] = f.CapitalStreakDay ?? -1;
                    result.PolicyNeeds[a] = f.PolicyNeeds;
                    result.PolicyPicks[a] = f.PolicyPick ?? -1;
                    result.Planner[a] = PlannerFromDto(f.Planner);
                }
                result.FiredEvents.UnionWith(dto.Succession.FiredEvents);
                result.AppliedEvents.UnionWith(dto.Succession.AppliedEvents);
                foreach (var b in dto.BattleHistory)
                {
                    var record = BattleFromDto(b);
                    if (record != null) result.Battles.Add(record);
                }
                while (result.Battles.Count > MaxBattleHistory) result.Battles.RemoveAt(0);
                result.SaveSequence = dto.SaveSequence;
                result.LastSeenMonth = dto.LastSeenMonth ?? -1;
                result.LastSeenYear = dto.LastSeenYear ?? -1;
                result.PlayerCompliance = dto.PlayerCompliance;
                result.CampaignCouriers = dto.CampaignCouriers;
                result.SourceVersion = dto.Version;
                result.PlansDropped = dto.PlansDropped;
                snap = result;
                reason = "ok";
                return true;
            }
            catch (Exception ex)
            {
                reason = "decode-failed: " + ex.Message;
                return false;
            }
        }

        // PER-atomic: write to <path>.tmp, then swap it in. File.Replace keeps the previous
        // sidecar as <path>.bak; on first write fall back to Move, and when Replace is not
        // supported or fails, to a copy that never leaves <path> missing. A crash mid-write
        // leaves the old sidecar (or its .bak / the .tmp) for TryLoadWithFallback.
        internal static void WriteAtomic(string path, string contents)
        {
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            File.WriteAllText(tmp, contents);
            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                return;
            }
            try
            {
                File.Replace(tmp, path, bak);
            }
            catch (PlatformNotSupportedException)
            {
                ReplaceByCopy(tmp, path, bak);
            }
            catch (IOException)
            {
                ReplaceByCopy(tmp, path, bak);
            }
        }

        // Win32 ReplaceFile can fail after it has already renamed the original to .bak, so the
        // original is only backed up when it still exists; the new content is copied over the
        // target (no delete-then-move window) before the temp file is removed.
        internal static void ReplaceByCopy(string tmp, string path, string bak)
        {
            if (File.Exists(path))
                File.Copy(path, bak, overwrite: true);
            File.Copy(tmp, path, overwrite: true);
            File.Delete(tmp);
        }

        // Loads <path> when it parses. Otherwise (missing, truncated by a crash mid-copy, or
        // rejected) picks the NEWEST of <path>.bak / <path>.tmp that parses, by the
        // SaveSequence stored in the document (a stale .tmp from an older crash loses to a
        // newer .bak; a complete newer .tmp wins over the previous save in .bak). Returns false
        // with reason "no-sidecar" when no file exists, otherwise the last rejection reason.
        internal static bool TryLoadWithFallback(string path, out SidecarSnapshot snapshot, out string source, out string reason)
        {
            snapshot = null;
            source = null;
            reason = "no-sidecar";
            if (TryLoadFile(path, ref reason, out var primary))
            {
                snapshot = primary;
                source = path;
                return true;
            }

            foreach (var candidate in new[] { path + ".bak", path + ".tmp" })
            {
                if (!TryLoadFile(candidate, ref reason, out var snap)) continue;
                if (snapshot == null || snap.SaveSequence > snapshot.SaveSequence)
                {
                    snapshot = snap;
                    source = candidate;
                }
            }
            if (snapshot == null) return false;
            reason = "ok";
            return true;
        }

        private static bool TryLoadFile(string candidate, ref string reason, out SidecarSnapshot snapshot)
        {
            snapshot = null;
            if (!File.Exists(candidate)) return false;
            string json;
            try
            {
                json = File.ReadAllText(candidate);
            }
            catch (Exception ex)
            {
                reason = "read-failed " + candidate + ": " + ex.Message;
                return false;
            }
            if (TryDeserialize(json, out var dto, out var why) && TryDecode(dto, out snapshot, out why))
                return true;
            reason = candidate + ": " + why;
            snapshot = null;
            return false;
        }

        internal static BattleHistoryDto BattleToDto(BattleHistoryRecord record)
        {
            if (record == null) return null;
            return new BattleHistoryDto
            {
                BattleName = record.BattleName,
                Day = record.Day,
                Month = record.Month,
                Year = record.Year,
                LandOrSea = record.LandOrSea,
                AllianceWon = record.AllianceWon,
                BattleResultType = record.BattleResultType,
                BattleEndType = record.BattleEndType,
                Theater = record.Theater.ToString(),
                PositionX = record.PositionX,
                PositionZ = record.PositionZ,
                Alliance = new List<int>(record.Alliance),
                Commander = new List<int>(record.Commander),
                CommanderName = new List<string>(record.CommanderName),
                Casualties = new List<int>(record.Casualties),
                CommanderKia = new List<int>(record.CommanderKia)
            };
        }

        internal static BattleHistoryRecord BattleFromDto(BattleHistoryDto dto)
        {
            if (dto == null) return null;
            var record = new BattleHistoryRecord
            {
                BattleName = dto.BattleName,
                Day = dto.Day,
                Month = dto.Month,
                Year = dto.Year,
                LandOrSea = dto.LandOrSea,
                AllianceWon = dto.AllianceWon,
                BattleResultType = dto.BattleResultType,
                BattleEndType = dto.BattleEndType,
                PositionX = dto.PositionX,
                PositionZ = dto.PositionZ
            };
            if (!Enum.TryParse<Theater>(dto.Theater, out record.Theater))
                record.Theater = Theater.Unknown;
            CopyList(dto.Alliance, record.Alliance);
            CopyList(dto.Commander, record.Commander);
            CopyList(dto.CommanderName, record.CommanderName);
            CopyList(dto.Casualties, record.Casualties);
            if (dto.CommanderKia != null) record.CommanderKia.AddRange(dto.CommanderKia);
            return record;
        }

        private static FiscalMemoryDto FiscalToDto(FiscalStateMemory memory)
        {
            if (memory == null) return null;
            return new FiscalMemoryDto
            {
                PreviousPosture = memory.PreviousPosture.ToString(),
                StableTicksAboveEmergency = memory.StableTicksAboveEmergency,
                EmergencyResidue = memory.EmergencyResidue
            };
        }

        private static FiscalStateMemory FiscalFromDto(FiscalMemoryDto dto)
        {
            var memory = new FiscalStateMemory();
            if (dto == null) return memory;
            if (Enum.TryParse<FiscalPosture>(dto.PreviousPosture, out var posture)) memory.PreviousPosture = posture;
            memory.StableTicksAboveEmergency = dto.StableTicksAboveEmergency;
            memory.EmergencyResidue = dto.EmergencyResidue;
            return memory;
        }

        // The v1/v2 files' saved operational plans (factions[].cic.activePlan), for the migration line. Never throws.
        private static int CountSavedPlans(string json)
        {
            try
            {
                int plans = 0;
                var factions = Newtonsoft.Json.Linq.JObject.Parse(json)["factions"] as Newtonsoft.Json.Linq.JArray;
                if (factions == null) return 0;
                foreach (var faction in factions)
                {
                    // A faction whose "cic" is null (or not an object) holds no plan; it must not end the count.
                    var cic = (faction as Newtonsoft.Json.Linq.JObject)?["cic"] as Newtonsoft.Json.Linq.JObject;
                    var plan = cic?["activePlan"];
                    if (plan != null && plan.Type != Newtonsoft.Json.Linq.JTokenType.Null) plans++;
                }
                return plans;
            }
            catch { return 0; }
        }

        // ---- the campaign planner's state (sidecar v3) ----

        internal static PlannerStateDto PlannerToDto(PlannerSnapshot p)
        {
            if (p == null) return null;
            var s = p.State ?? new PlannerState();
            var dto = new PlannerStateDto
            {
                CurrentMainObjective = s.CurrentMainObjective,
                ChallengerObjective = s.ChallengerObjective,
                ChallengerDays = s.ChallengerDays,
                DecisionSerial = s.DecisionSerial,
                CampaignSeed = s.CampaignSeed,
                BelowOddsDays = s.BelowOddsDays,
                CapitalThreatLatched = s.CapitalThreatLatched,
                TempoReleasedDecision = s.TempoReleasedDecision,
                LastFullReviewDay = s.LastFullReviewDay,
                ReinforcementKey = s.ReinforcementKey,
                ReinforcementDecision = s.ReinforcementDecision,
                ScoutedDecision = s.ScoutedDecision,
                UnreliableDays = p.UnreliableDays,
                UnreliableLastDay = p.UnreliableLastDay
            };
            foreach (var pair in s.Departments)
                dto.Departments.Add(new DepartmentDto { UnitKey = pair.Key, Department = pair.Value.ToString() });
            dto.Departments.Sort((a, b) => string.CompareOrdinal(a.UnitKey, b.UnitKey));   // a stable file for the same state
            foreach (var b in p.Beliefs)
            {
                if (string.IsNullOrEmpty(b.UnitKey)) continue;
                dto.Beliefs.Add(new EnemyBeliefDto
                {
                    UnitKey = b.UnitKey, UnitTyp = b.UnitTyp, X = b.X, Z = b.Z, Strength = b.Strength,
                    LastSeenDay = b.LastSeenDay, Confidence = b.Confidence01, Radius = b.UncertaintyRadius,
                    Source = (int)b.LastSource, Traits = (int)b.Traits, LastEmbarkedDay = b.LastEmbarkedDay
                });
            }
            foreach (var a in p.Appointments)
                dto.Appointments.Add(new AppointmentDto { CommanderId = a.Key, ProtectedUntilDay = a.Value });
            foreach (var d in p.DepotTasks)
                dto.DepotTasks.Add(new DepotTaskDto
                {
                    X = d.X, Z = d.Z, SectorId = d.SectorId, BuilderUnitKey = d.BuilderUnitKey, State = d.State.ToString(),
                    StartedDay = d.StartedDay, EndedDay = d.EndedDay, WishX = d.WishX, WishZ = d.WishZ
                });
            foreach (var m in p.Moves)
            {
                if (string.IsNullOrEmpty(m.UnitKey)) continue;
                dto.Moves.Add(new MoveDto { UnitKey = m.UnitKey, TargetX = m.TargetX, TargetZ = m.TargetZ, IssuedDay = m.IssuedDay, Retries = m.Retries, ObjectiveId = m.ObjectiveId });
            }
            foreach (var m in p.ReconMissions)
            {
                if (string.IsNullOrEmpty(m.UnitKey)) continue;
                dto.ReconMissions.Add(new ReconMissionDto
                {
                    MissionId = m.MissionId, Kind = m.Kind.ToString(), UnitKey = m.UnitKey, HoldX = m.HoldX, HoldZ = m.HoldZ, SectorId = m.SectorId,
                    State = m.State.ToString(), IssuedDay = m.IssuedDay, Reason = m.Reason,
                    ObservedEnemyStrength = m.ObservedEnemyStrength, ObservedObjectiveId = m.ObservedObjectiveId,
                    StandDownDue = m.StandDownDue, ClearedDay = m.ClearedDay
                });
            }
            foreach (var g in p.GarrisonPosts)
                dto.GarrisonPosts.Add(new GarrisonPostDto { Kind = g.Kind.ToString(), X = g.X, Z = g.Z, UnitKey = g.AssignedUnitKey, QuietSinceDay = g.QuietSinceDay });
            return dto;
        }

        // A record without the identity it needs (a belief or a move with no unit key) is skipped; an unknown enum name decodes to
        // the enum's default.
        internal static PlannerSnapshot PlannerFromDto(PlannerStateDto dto)
        {
            if (dto == null) return null;
            var p = new PlannerSnapshot { UnreliableDays = dto.UnreliableDays, UnreliableLastDay = dto.UnreliableLastDay };
            p.State.Restore(dto.CurrentMainObjective, dto.ChallengerObjective, dto.ChallengerDays, dto.DecisionSerial, dto.CampaignSeed,
                dto.BelowOddsDays, dto.CapitalThreatLatched, dto.TempoReleasedDecision, dto.LastFullReviewDay, dto.ReinforcementKey, dto.ReinforcementDecision, dto.ScoutedDecision);
            if (dto.Departments != null)
            {
                var departments = new List<KeyValuePair<string, Theater>>();
                foreach (var d in dto.Departments)
                {
                    if (d == null || string.IsNullOrEmpty(d.UnitKey) || !Enum.TryParse<Theater>(d.Department, out var theater)) continue;
                    departments.Add(new KeyValuePair<string, Theater>(d.UnitKey, theater));
                }
                p.State.RestoreDepartments(departments);
            }
            if (dto.Beliefs != null)
                foreach (var b in dto.Beliefs)
                {
                    if (b == null || string.IsNullOrEmpty(b.UnitKey)) continue;
                    p.Beliefs.Add(new EnemyBelief
                    {
                        InstanceId = -1, UnitKey = b.UnitKey, UnitTyp = b.UnitTyp, X = b.X, Z = b.Z, Strength = b.Strength,
                        LastSeenDay = b.LastSeenDay, Confidence01 = b.Confidence, UncertaintyRadius = b.Radius,
                        LastSource = DetectionSources.FromSaved(b.Source), Traits = (EnemyTraits)b.Traits, LastEmbarkedDay = b.LastEmbarkedDay
                    });
                }
            if (dto.Appointments != null)
                foreach (var a in dto.Appointments)
                    if (a != null && a.CommanderId >= 0) p.Appointments.Add(new KeyValuePair<int, int>(a.CommanderId, a.ProtectedUntilDay));
            if (dto.DepotTasks != null)
                foreach (var d in dto.DepotTasks)
                {
                    if (d == null) continue;
                    Enum.TryParse<DepotTaskState>(d.State, out var state);
                    p.DepotTasks.Add(new DepotTaskRecord
                    {
                        X = d.X, Z = d.Z, SectorId = d.SectorId, BuilderInstanceId = -1, BuilderUnitKey = d.BuilderUnitKey, State = state,
                        StartedDay = d.StartedDay, EndedDay = d.EndedDay, WishX = d.WishX, WishZ = d.WishZ
                    });
                }
            if (dto.Moves != null)
                foreach (var m in dto.Moves)
                {
                    if (m == null || string.IsNullOrEmpty(m.UnitKey)) continue;
                    p.Moves.Add(new MoveRecord { InstanceId = -1, UnitKey = m.UnitKey, TargetX = m.TargetX, TargetZ = m.TargetZ, IssuedDay = m.IssuedDay, Retries = m.Retries, ObjectiveId = m.ObjectiveId });
                }
            if (dto.ReconMissions != null)
                foreach (var m in dto.ReconMissions)
                {
                    if (m == null || string.IsNullOrEmpty(m.UnitKey)) continue;
                    Enum.TryParse<ReconKind>(m.Kind, out var kind);
                    Enum.TryParse<ReconState>(m.State, out var state);
                    p.ReconMissions.Add(new ReconMission
                    {
                        MissionId = m.MissionId, Kind = kind, InstanceId = -1, UnitKey = m.UnitKey, HoldX = m.HoldX, HoldZ = m.HoldZ, SectorId = m.SectorId,
                        State = state, IssuedDay = m.IssuedDay, Reason = m.Reason,
                        ObservedEnemyStrength = m.ObservedEnemyStrength, ObservedObjectiveId = m.ObservedObjectiveId,
                        StandDownDue = m.StandDownDue, ClearedDay = m.ClearedDay
                    });
                }
            if (dto.GarrisonPosts != null)
                foreach (var g in dto.GarrisonPosts)
                {
                    if (g == null) continue;
                    Enum.TryParse<GarrisonPostKind>(g.Kind, out var kind);
                    p.GarrisonPosts.Add(new GarrisonPostRecord { Kind = kind, X = g.X, Z = g.Z, AssignedUnitKey = g.UnitKey, QuietSinceDay = g.QuietSinceDay });
                }
            return p;
        }

        private static void CopyList<T>(List<T> source, T[] target)
        {
            if (source == null || target == null) return;
            for (int i = 0; i < source.Count && i < target.Length; i++)
                target[i] = source[i];
        }
    }
}
