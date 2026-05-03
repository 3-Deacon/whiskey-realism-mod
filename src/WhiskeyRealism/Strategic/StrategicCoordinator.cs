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
                var isCIC = AccessTools.Method(dlcType, "IsCommanderInChief");
                if (isCIC == null) return false;
                return (bool)isCIC.Invoke(null, null);
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
    }
}
