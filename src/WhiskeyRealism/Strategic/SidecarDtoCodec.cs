using System;
using System.Collections.Generic;
using System.IO;
using WhiskeyRealism.Strategic.Fiscal;

namespace WhiskeyRealism.Strategic
{
    // Plain container for everything the sidecar persists. Load decodes into a fresh
    // snapshot and the coordinator swaps it in only when decoding fully succeeded
    // (transactional apply — PER-version).
    internal sealed class SidecarSnapshot
    {
        public CIC[] Cics = new CIC[2];
        public EraStage?[] Eras = new EraStage?[2];
        public DirectorMemory[] DirectorMemories = { new DirectorMemory(), new DirectorMemory() };
        public int[][] RecentReplans = new int[2][];
        public OperationalProbeState[] ProbeStates = new OperationalProbeState[2];
        public FiscalStateMemory[] FiscalMemories = { new FiscalStateMemory(), new FiscalStateMemory() };
        public Dictionary<int, PersonalityVector> MinorOfficers = new Dictionary<int, PersonalityVector>();
        public HashSet<int> FiredEvents = new HashSet<int>();
        public HashSet<int> AppliedEvents = new HashSet<int>();
        // Ids marked fired by the ForceAllSuccessionEvents test flag; never persisted (PER-force).
        public HashSet<int> ForcedEvents = new HashSet<int>();
        public List<BattleHistoryRecord> Battles = new List<BattleHistoryRecord>();
        public int LastSeenMonth = -1;
        public int LastSeenYear = -1;
        // followedcampaignobjective value PickCampaignObjectivePatch last wrote, per alliance.
        public int[] ModWrittenObjective = { -1, -1 };

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
            dto.MinorOfficerProfiles = dto.MinorOfficerProfiles ?? new List<MinorOfficerDto>();
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
                LastSeenMonth = snap.LastSeenMonth,
                LastSeenYear = snap.LastSeenYear
            };
            for (int alliance = 0; alliance < 2; alliance++)
            {
                var cic = snap.Cics[alliance];
                var faction = new FactionDto
                {
                    FactionId = alliance,
                    FactionName = alliance == 1 ? "CSA" : "Union",
                    CurrentEra = (snap.Eras[alliance] ?? EraStage.Amateur1861).ToString(),
                    Cic = cic == null ? null : new CICDto
                    {
                        OfficerName = cic.OfficerName,
                        Personality = PersonalityDto.FromVector(cic.OfficerPersonality),
                        ActivePlan = cic.ActivePlan != null ? PlanToDto(cic.ActivePlan) : null
                    },
                    DirectorMemory = StrategicResilienceDirector.MemoryToDto(snap.DirectorMemories[alliance]),
                    ProbeState = ProbeToDto(snap.ProbeStates[alliance]),
                    ModWrittenObjective = snap.ModWrittenObjective[alliance],
                    FiscalMemory = FiscalToDto(snap.FiscalMemories[alliance])
                };
                if (faction.DirectorMemory != null)
                    faction.DirectorMemory.RecentOperationReplanDaySerials = snap.RecentReplans[alliance];
                dto.Factions.Add(faction);
            }
            foreach (var kv in snap.MinorOfficers)
                dto.MinorOfficerProfiles.Add(new MinorOfficerDto { CommanderId = kv.Key, Personality = PersonalityDto.FromVector(kv.Value) });
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
                    if (f.Cic != null)
                    {
                        result.Cics[a] = new CIC
                        {
                            AllianceId = a,
                            OfficerName = f.Cic.OfficerName,
                            OfficerPersonality = f.Cic.Personality?.ToVector() ?? default(PersonalityVector),
                            ActivePlan = f.Cic.ActivePlan != null ? PlanFromDto(f.Cic.ActivePlan, a) : null
                        };
                    }
                    result.DirectorMemories[a] = StrategicResilienceDirector.MemoryFromDto(f.DirectorMemory);
                    result.RecentReplans[a] = f.DirectorMemory?.RecentOperationReplanDaySerials;
                    result.ProbeStates[a] = ProbeFromDto(f.ProbeState);
                    result.FiscalMemories[a] = FiscalFromDto(f.FiscalMemory);
                    result.ModWrittenObjective[a] = f.ModWrittenObjective ?? -1;
                }
                foreach (var m in dto.MinorOfficerProfiles)
                {
                    if (m?.Personality == null) continue;
                    result.MinorOfficers[m.CommanderId] = m.Personality.ToVector();
                }
                result.FiredEvents.UnionWith(dto.Succession.FiredEvents);
                result.AppliedEvents.UnionWith(dto.Succession.AppliedEvents);
                foreach (var b in dto.BattleHistory)
                {
                    var record = BattleFromDto(b);
                    if (record != null) result.Battles.Add(record);
                }
                while (result.Battles.Count > MaxBattleHistory) result.Battles.RemoveAt(0);
                result.LastSeenMonth = dto.LastSeenMonth ?? -1;
                result.LastSeenYear = dto.LastSeenYear ?? -1;
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

        // Loads the first usable sidecar among <path>, <path>.bak, <path>.tmp. Returns false
        // with reason "no-sidecar" when none exists, otherwise the last rejection reason.
        internal static bool TryLoadWithFallback(string path, out SidecarSnapshot snapshot, out string source, out string reason)
        {
            snapshot = null;
            source = null;
            reason = "no-sidecar";
            foreach (var candidate in new[] { path, path + ".bak", path + ".tmp" })
            {
                if (!File.Exists(candidate)) continue;
                string json;
                try
                {
                    json = File.ReadAllText(candidate);
                }
                catch (Exception ex)
                {
                    reason = "read-failed " + candidate + ": " + ex.Message;
                    continue;
                }
                if (TryDeserialize(json, out var dto, out var why) && TryDecode(dto, out var snap, out why))
                {
                    snapshot = snap;
                    source = candidate;
                    reason = "ok";
                    return true;
                }
                reason = candidate + ": " + why;
            }
            return false;
        }

        internal static OperationalPlanDto PlanToDto(OperationalPlan p)
        {
            var dto = new OperationalPlanDto
            {
                OperationId = p.OperationId,
                OperationName = p.OperationName,
                OperationTempo = p.OperationTempo.ToString(),
                OperationPosture = p.OperationPosture.ToString(),
                OperationStartedDaySerial = p.OperationStartedDaySerial,
                OperationLastDecisionDaySerial = p.OperationLastDecisionDaySerial,
                PendingRetarget = p.PendingRetarget,
                PendingRetargetReason = p.PendingRetargetReason,
                AssignedTheaterId = p.AssignedTheaterId,
                CurrentPhaseIndex = p.CurrentPhaseIndex,
                PlanDeadlineMonth = p.PlanDeadlineMonth,
                PlanDeadlineYear = p.PlanDeadlineYear,
                Rationale = p.Rationale,
                IsDirty = p.IsDirty
            };
            foreach (var ph in p.Phases)
            {
                if (ph == null) continue;
                dto.Phases.Add(new PhaseDto
                {
                    PhaseId = ph.PhaseId,
                    PhaseName = ph.PhaseName,
                    TargetAreaId = ph.TargetAreaId,
                    TargetObjectiveId = ph.TargetObjectiveId,
                    TargetAreaKey = ph.TargetAreaKey,
                    TargetSectorKey = ph.TargetSectorKey,
                    ForceFractionRequired = ph.ForceFractionRequired,
                    Transition = ph.Transition.ToString(),
                    DeadlineMonth = ph.DeadlineMonth,
                    DeadlineYear = ph.DeadlineYear,
                    OperationPosture = ph.OperationPosture.ToString(),
                    AllowCoordinatedAttack = ph.AllowCoordinatedAttack,
                    AllowReinforcementPackage = ph.AllowReinforcementPackage,
                    AllowProbeOnly = ph.AllowProbeOnly,
                    PhaseStartedDaySerial = ph.PhaseStartedDaySerial
                });
            }
            return dto;
        }

        internal static OperationalPlan PlanFromDto(OperationalPlanDto dto, int allianceId)
        {
            var p = new OperationalPlan
            {
                CICFactionAllianceId = allianceId,
                OperationId = dto.OperationId,
                OperationName = dto.OperationName,
                OperationStartedDaySerial = dto.OperationStartedDaySerial,
                OperationLastDecisionDaySerial = dto.OperationLastDecisionDaySerial,
                PendingRetarget = dto.PendingRetarget,
                PendingRetargetReason = dto.PendingRetargetReason,
                AssignedTheaterId = dto.AssignedTheaterId,
                CurrentPhaseIndex = dto.CurrentPhaseIndex,
                PlanDeadlineMonth = dto.PlanDeadlineMonth,
                PlanDeadlineYear = dto.PlanDeadlineYear,
                Rationale = dto.Rationale,
                IsDirty = dto.IsDirty
            };
            Enum.TryParse<OperationTempoPreset>(dto.OperationTempo, out p.OperationTempo);
            Enum.TryParse<OperationPosture>(dto.OperationPosture, out p.OperationPosture);
            if (dto.Phases != null)
            {
                foreach (var ph in dto.Phases)
                {
                    if (ph == null) continue;
                    Enum.TryParse<PhaseTransition>(ph.Transition, out var trans);
                    Enum.TryParse<OperationPosture>(ph.OperationPosture, out var posture);
                    p.Phases.Add(new Phase
                    {
                        PhaseId = ph.PhaseId,
                        PhaseName = ph.PhaseName,
                        TargetAreaId = ph.TargetAreaId,
                        TargetObjectiveId = ph.TargetObjectiveId,
                        TargetAreaKey = ph.TargetAreaKey,
                        TargetSectorKey = ph.TargetSectorKey,
                        ForceFractionRequired = ph.ForceFractionRequired,
                        Transition = trans,
                        DeadlineMonth = ph.DeadlineMonth,
                        DeadlineYear = ph.DeadlineYear,
                        OperationPosture = posture,
                        AllowCoordinatedAttack = ph.AllowCoordinatedAttack,
                        AllowReinforcementPackage = ph.AllowReinforcementPackage,
                        AllowProbeOnly = ph.AllowProbeOnly,
                        PhaseStartedDaySerial = ph.PhaseStartedDaySerial,
                        Fallback = null
                    });
                }
            }
            // A plan whose phase index no longer fits its phases is unusable: replan.
            if (p.CurrentPhase == null) p.IsDirty = true;
            // A historical plan saved under an older catalog (e.g. CSA defense ops aimed at
            // objective 4 before R5) is replanned instead of steering an outdated target.
            if (!string.IsNullOrEmpty(p.OperationId) && !MatchesCatalog(p)) p.IsDirty = true;
            return p;
        }

        private static bool MatchesCatalog(OperationalPlan plan)
        {
            if (!HistoricalOperationCatalog.TryGetById(plan.OperationId, out var profile) || profile.Phases == null)
                return false;
            foreach (var phase in plan.Phases)
            {
                bool found = false;
                foreach (var template in profile.Phases)
                {
                    if (template == null || !string.Equals(template.PhaseId, phase.PhaseId, StringComparison.Ordinal)) continue;
                    if (template.TargetObjectiveId != phase.TargetObjectiveId) return false;
                    found = true;
                    break;
                }
                if (!found) return false;
            }
            return true;
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

        private static OperationalProbeStateDto ProbeToDto(OperationalProbeState state)
        {
            if (state == null) return null;
            return new OperationalProbeStateDto
            {
                ProbeId = state.ProbeId,
                UnitKey = state.UnitKey,
                TargetAreaKey = state.TargetAreaKey,
                SourceSectorKey = state.SourceSectorKey,
                StartedDaySerial = state.StartedDaySerial,
                LastObservedEnemyStrength = state.LastObservedEnemyStrength,
                LastObservedFriendlyStrength = state.LastObservedFriendlyStrength
            };
        }

        private static OperationalProbeState ProbeFromDto(OperationalProbeStateDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.UnitKey)) return null;
            return new OperationalProbeState
            {
                ProbeId = dto.ProbeId,
                UnitKey = dto.UnitKey,
                TargetAreaKey = dto.TargetAreaKey,
                SourceSectorKey = dto.SourceSectorKey,
                StartedDaySerial = dto.StartedDaySerial,
                LastObservedEnemyStrength = dto.LastObservedEnemyStrength,
                LastObservedFriendlyStrength = dto.LastObservedFriendlyStrength
            };
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

        private static void CopyList<T>(List<T> source, T[] target)
        {
            if (source == null || target == null) return;
            for (int i = 0; i < source.Count && i < target.Length; i++)
                target[i] = source[i];
        }
    }
}
