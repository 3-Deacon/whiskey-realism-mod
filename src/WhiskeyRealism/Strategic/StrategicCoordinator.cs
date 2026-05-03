using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    public class StrategicCoordinator : MonoBehaviour
    {
        public static StrategicCoordinator Instance { get; private set; }

        public CIC[] CICs = new CIC[2];
        public EraStageManager[] Eras = new EraStageManager[2];
        internal SuccessionScheduler Succession = new SuccessionScheduler();
        public Dictionary<int, PersonalityVector> MinorOfficerProfiles = new Dictionary<int, PersonalityVector>();

        public int LastSeenMonth = -1;
        public int LastSeenYear  = -1;

        public bool Initialized;

        public static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("WhiskeyRealismStrategicCoordinator");
            Instance = go.AddComponent<StrategicCoordinator>();
            DontDestroyOnLoad(go);
            Plugin.Log.LogInfo("[Coordinator] bootstrapped");
        }

        public void InitializeFromGameState()
        {
            try
            {
                Eras[0] = Eras[0] ?? new EraStageManager { Stage = EraStage.Amateur1861 };
                Eras[1] = Eras[1] ?? new EraStageManager { Stage = EraStage.Amateur1861 };

                int playerAlliance = ResolvePlayerAlliance();
                for (int alliance = 0; alliance < 2; alliance++)
                {
                    if (IsPlayerCICOf(alliance, playerAlliance))
                    {
                        CICs[alliance] = null;
                        continue;
                    }
                    if (CICs[alliance] == null)
                        CICs[alliance] = BuildCICForAlliance(alliance);
                }
                Initialized = true;
                Plugin.Log.LogInfo($"[Coordinator] initialized (playerAlliance={playerAlliance})");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Coordinator] init failed: " + ex);
            }
        }

        private CIC BuildCICForAlliance(int allianceId)
        {
            var cic = new CIC { AllianceId = allianceId };
            if (allianceId == 1)
            {
                cic.OfficerName = "Davis";
                cic.OfficerPersonality = new PersonalityVector(-0.1f, +0.3f, -0.3f, -0.3f, +0.5f);
            }
            else
            {
                cic.OfficerName = "Lincoln";
                cic.OfficerPersonality = new PersonalityVector(+0.3f, +0.1f, +0.1f, +0.4f, +0.7f);
            }
            return cic;
        }

        public void NotifyDateAdvanced(int gameMonth, int gameYear)
        {
            if (!Initialized) InitializeFromGameState();

            if (LastSeenMonth < 0)
            {
                LastSeenMonth = gameMonth;
                LastSeenYear  = gameYear;
                return;
            }

            bool rollover = (gameMonth != LastSeenMonth) || (gameYear != LastSeenYear);
            if (!rollover) return;

            LastSeenMonth = gameMonth;
            LastSeenYear  = gameYear;
            OnMonthlyTick(gameMonth, gameYear);
        }

        public void OnMonthlyTick(int month, int year)
        {
            try
            {
                int playerAlliance = ResolvePlayerAlliance();
                for (int alliance = 0; alliance < 2; alliance++)
                {
                    if (IsPlayerCICOf(alliance, playerAlliance))
                    {
                        OnceLog.Info("playerciconly:" + alliance,
                            $"player is CIC of alliance {alliance} ({(alliance == 1 ? "CSA" : "Union")}) — mod stands down for that faction");
                        CICs[alliance] = null;
                        continue;
                    }
                    if (CICs[alliance] == null) CICs[alliance] = BuildCICForAlliance(alliance);

                    var era = Eras[alliance];
                    var ws = ObserveWarState(month, year, alliance);
                    era.CheckTransition(month, year, ws.VicksburgFallen, ws.AtlantaThreatened);

                    var fired = Succession.CheckEvents(BuildSchedulerView(month, year, alliance, ws));
                    foreach (var e in fired)
                    {
                        if (e.AllianceId != alliance) continue;
                        SwapOfficer(alliance, e);
                        if (CICs[alliance].ActivePlan != null)
                            CICs[alliance].ActivePlan.IsDirty = true;
                    }

                    var cic = CICs[alliance];
                    if (cic.ReviewPlan(month, year))
                    {
                        // plan still valid
                    }
                    else
                    {
                        cic.Replan(era, month, year);
                    }

                    Plugin.Log.LogInfo(
                        $"[Heartbeat] {year}-{month:D2} alliance={alliance} " +
                        $"era={era.Stage} cic={cic.OfficerName ?? "<none>"} " +
                        $"plan={(cic.ActivePlan == null ? "<none>" : $"phase{cic.ActivePlan.CurrentPhaseIndex + 1}/{cic.ActivePlan.Phases.Count} obj={cic.ActivePlan.CurrentPhase?.TargetObjectiveId}")} " +
                        $"succession_fired={Succession.FiredEventIds.Count}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Coordinator] tick failed: " + ex);
            }
        }

        public void OnEventTrigger(int allianceId, string eventType)
        {
            if (allianceId < 0 || allianceId >= CICs.Length) return;
            var cic = CICs[allianceId];
            if (cic?.ActivePlan == null) return;

            cic.ActivePlan.IsDirty = true;
            Plugin.Log.LogInfo($"[Coordinator] event '{eventType}' for alliance {allianceId} — plan marked dirty");
        }

        public static int ResolvePlayerAlliance()
        {
            try
            {
                var t = AccessTools.TypeByName("GameVars");
                var f = AccessTools.Field(t, "playeralliance");
                return f != null ? (int)f.GetValue(null) : -1;
            }
            catch { return -1; }
        }

        public static bool IsPlayerCICOf(int allianceId, int playerAlliance)
        {
            if (allianceId != playerAlliance) return false;
            try
            {
                var dlcType = AccessTools.TypeByName("DLC_WL");
                if (dlcType == null) return false;
                var scenarioActive = (bool)AccessTools.Field(dlcType, "dlc_scenarioactive").GetValue(null);
                if (!scenarioActive) return false;
                // Vanilla signature: public static bool IsCommanderInChief(int manualcommander = -1)
                // (decompile line 47475). Pass -1 to use the default behavior (check dlc_chosencommander).
                var isCIC = AccessTools.Method(dlcType, "IsCommanderInChief", new[] { typeof(int) });
                if (isCIC == null) return false;
                return (bool)isCIC.Invoke(null, new object[] { -1 });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Coordinator] IsPlayerCICOf failed: " + ex.Message);
                return false;
            }
        }

        private struct WarSnapshot
        {
            public bool VicksburgFallen;
            public bool ChattanoogaFallen;
            public bool AtlantaThreatened;
            public bool ANVHasLostMajorBattle;
        }

        private WarSnapshot ObserveWarState(int month, int year, int alliance)
        {
            return new WarSnapshot();
        }

        private SuccessionScheduler.WarStateView BuildSchedulerView(int month, int year, int alliance, WarSnapshot snap)
        {
            return new SuccessionScheduler.WarStateView
            {
                CurrentMonth = month,
                CurrentYear  = year,
                VicksburgFallen     = snap.VicksburgFallen,
                ChattanoogaFallen   = snap.ChattanoogaFallen,
                AtlantaThreatened   = snap.AtlantaThreatened,
                ANVHasLostMajorBattle = snap.ANVHasLostMajorBattle,
                JohnstonWoundedOrDisabled = false,
                BragsCommandRatingLow     = false,
                AoPHasFailedNOffensives   = false,
                BurnsidesFirstDefeatPassed = false,
                LeeInvadingPennsylvania   = false,
                WesternMajorDefeatPassed  = false,
                DavisPatienceExhausted    = false,
                ValleyOpsNeeded           = false,
                WarClearlyLost            = false
            };
        }

        private void SwapOfficer(int alliance, SuccessionScheduler.Event e)
        {
            Plugin.Log.LogInfo($"[Succession:{e.Id}] FIRED — {e.Name}, replacing role={e.ReplacedRole} with={e.ReplacementName}");
        }

        public void SaveSidecar(string fullPath)
        {
            try
            {
                var dto = BuildDto();
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(dto, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(fullPath, json);
                Plugin.Log.LogInfo("[Coordinator] sidecar written: " + fullPath);
            }
            catch (Exception ex) { Plugin.Log.LogError("[Coordinator] sidecar save failed: " + ex); }
        }

        public void LoadSidecar(string fullPath)
        {
            try
            {
                var json = System.IO.File.ReadAllText(fullPath);
                var dto = Newtonsoft.Json.JsonConvert.DeserializeObject<SidecarDto>(json);
                if (dto == null) { InitializeFromGameState(); return; }
                ApplyDto(dto);
                Initialized = true;
                Plugin.Log.LogInfo("[Coordinator] sidecar loaded: " + fullPath);
                OnceLog.Reset();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Coordinator] sidecar load failed (falling back to fresh init): " + ex.Message);
                InitializeFromGameState();
            }
        }

        private SidecarDto BuildDto()
        {
            var dto = new SidecarDto();
            for (int alliance = 0; alliance < 2; alliance++)
            {
                if (CICs[alliance] == null) continue;
                var f = new FactionDto
                {
                    FactionId   = alliance,
                    FactionName = (alliance == 1) ? "CSA" : "Union",
                    CurrentEra  = Eras[alliance].Stage.ToString(),
                    Cic = new CICDto
                    {
                        OfficerName = CICs[alliance].OfficerName,
                        Personality = PersonalityDto.FromVector(CICs[alliance].OfficerPersonality),
                        ActivePlan  = (CICs[alliance].ActivePlan != null) ? PlanToDto(CICs[alliance].ActivePlan) : null
                    }
                };
                foreach (var tc in CICs[alliance].Theaters)
                {
                    f.TheaterCommanders.Add(new TheaterCommanderDto
                    {
                        TheaterId   = tc.TheaterId,
                        OfficerName = tc.OfficerName,
                        Personality = PersonalityDto.FromVector(tc.Personality)
                    });
                }
                dto.Factions.Add(f);
            }
            foreach (var kv in MinorOfficerProfiles)
            {
                dto.MinorOfficerProfiles.Add(new MinorOfficerDto
                {
                    CommanderId = kv.Key,
                    Personality = PersonalityDto.FromVector(kv.Value)
                });
            }
            dto.Succession.FiredEvents = new List<int>(Succession.FiredEventIds);
            dto.Succession.LastChecked = LastSeenYear + "-" + LastSeenMonth.ToString("D2") + "-01";
            return dto;
        }

        private void ApplyDto(SidecarDto dto)
        {
            for (int alliance = 0; alliance < 2; alliance++)
            {
                Eras[alliance] = Eras[alliance] ?? new EraStageManager();
            }
            foreach (var f in dto.Factions)
            {
                if (f.FactionId < 0 || f.FactionId >= 2) continue;
                if (Enum.TryParse<EraStage>(f.CurrentEra, out var era)) Eras[f.FactionId].Stage = era;
                var cic = new CIC
                {
                    AllianceId         = f.FactionId,
                    OfficerName        = f.Cic?.OfficerName,
                    OfficerPersonality = f.Cic?.Personality?.ToVector() ?? default(PersonalityVector),
                    ActivePlan         = (f.Cic?.ActivePlan != null) ? PlanFromDto(f.Cic.ActivePlan, f.FactionId) : null
                };
                foreach (var tc in f.TheaterCommanders)
                {
                    cic.Theaters.Add(new TheaterCommander
                    {
                        TheaterId   = tc.TheaterId,
                        OfficerName = tc.OfficerName,
                        Personality = tc.Personality?.ToVector() ?? default(PersonalityVector)
                    });
                }
                CICs[f.FactionId] = cic;
            }
            MinorOfficerProfiles.Clear();
            foreach (var m in dto.MinorOfficerProfiles)
                MinorOfficerProfiles[m.CommanderId] = m.Personality.ToVector();
            Succession.FiredEventIds = new HashSet<int>(dto.Succession.FiredEvents);
        }

        private OperationalPlanDto PlanToDto(OperationalPlan p)
        {
            var dto = new OperationalPlanDto
            {
                AssignedTheaterId = p.AssignedTheaterId,
                CurrentPhaseIndex = p.CurrentPhaseIndex,
                PlanDeadlineMonth = p.PlanDeadlineMonth,
                PlanDeadlineYear  = p.PlanDeadlineYear,
                Rationale         = p.Rationale,
                IsDirty           = p.IsDirty
            };
            foreach (var ph in p.Phases)
                dto.Phases.Add(new PhaseDto
                {
                    TargetAreaId          = ph.TargetAreaId,
                    TargetObjectiveId     = ph.TargetObjectiveId,
                    ForceFractionRequired = ph.ForceFractionRequired,
                    Transition            = ph.Transition.ToString(),
                    DeadlineMonth         = ph.DeadlineMonth,
                    DeadlineYear          = ph.DeadlineYear
                });
            return dto;
        }

        private OperationalPlan PlanFromDto(OperationalPlanDto dto, int allianceId)
        {
            var p = new OperationalPlan
            {
                CICFactionAllianceId = allianceId,
                AssignedTheaterId    = dto.AssignedTheaterId,
                CurrentPhaseIndex    = dto.CurrentPhaseIndex,
                PlanDeadlineMonth    = dto.PlanDeadlineMonth,
                PlanDeadlineYear     = dto.PlanDeadlineYear,
                Rationale            = dto.Rationale,
                IsDirty              = dto.IsDirty
            };
            foreach (var ph in dto.Phases)
            {
                Enum.TryParse<PhaseTransition>(ph.Transition, out var trans);
                p.Phases.Add(new Phase
                {
                    TargetAreaId          = ph.TargetAreaId,
                    TargetObjectiveId     = ph.TargetObjectiveId,
                    ForceFractionRequired = ph.ForceFractionRequired,
                    Transition            = trans,
                    DeadlineMonth         = ph.DeadlineMonth,
                    DeadlineYear          = ph.DeadlineYear,
                    Fallback              = null
                });
            }
            return p;
        }
    }
}
