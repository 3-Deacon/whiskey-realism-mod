using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Strategic.Projects;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AICampaign.UpdateProjects picks/funds the next AI research project
    // per subsidy lane. This Prefix seeds/replaces nextprojecttoresearch[subsidy]
    // before vanilla spending so the original method still funds/appoints. Doctrine
    // signals come from the daily review's need vector (CampaignNeedBuilder.ToProjectSignals),
    // shared with the need-driven policy selection.
    [HarmonyPatch(typeof(AICampaign), "UpdateProjects")]
    internal static class ProjectSelectionPatch
    {
        private static readonly ProjectDoctrineLogGate SelectionLogGate = new ProjectDoctrineLogGate();
        private static readonly ProjectDoctrineLogGate StarvedLaneLogGate = new ProjectDoctrineLogGate();
        private static bool _starvedLaneObserverLogged;
        private static bool _selectionObserverLogged;
        private static bool _projectSelectionWiredLogged;
        private static bool _catalogLogged;

        [HarmonyPrefix]
        internal static void Prefix(int alliance)
        {
            using (TelemetryPerf.Scope("campaign.patch.project-selection", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.project-selection");
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (StrategicCoordinator.Instance == null) return;
                if (alliance < 0 || alliance >= 2) return;
                if (GameVars.frame <= 50) return;
                if (GameVars.alliance == null || alliance >= GameVars.alliance.Length) return;

                int playerAlliance = GameVars.playeralliance;
                if (alliance == playerAlliance && !GameVars.automanageprojects && !GameVars.ai_vs_ai) return;

                var allianceState = GameVars.alliance[alliance];
                if (allianceState == null) return;

                var aiPersonality = allianceState.GetAIPersonality(alliance);
                if (aiPersonality == null) return;

                if (StrategicCoordinator.IsPlayerCICOf(alliance, playerAlliance)) return;

                int[] nextProjects = allianceState.nextprojecttoresearch;
                if (nextProjects == null)
                {
                    OnceLog.Warning("project-selection:nextprojecttoresearch", "[Patch:ProjectSelection] nextprojecttoresearch was null");
                    return;
                }

                if (!_projectSelectionWiredLogged)
                {
                    _projectSelectionWiredLogged = true;
                    EmitProjectDoctrineObserver("project-selection", alliance);
                }

                int lanes = nextProjects.Length;
                if (allianceState.subsidyfunding != null)
                    lanes = Math.Min(lanes, allianceState.subsidyfunding.Length);

                var era = StrategicCoordinator.Instance.Eras[alliance]?.Stage ?? EraStage.Amateur1861;
                int campaignYear = ReadCampaignYear();
                var profile = GrandStrategyRegistry.Resolve(alliance, era);
                var fiscalIntent = StrategicCoordinator.Instance.FiscalIntents != null && alliance < StrategicCoordinator.Instance.FiscalIntents.Length
                    ? StrategicCoordinator.Instance.FiscalIntents[alliance]
                    : null;

                if (!_catalogLogged)
                {
                    _catalogLogged = true;
                    EmitProjectDoctrineCatalog(ProjectDoctrineCatalog.AllActive.Count);
                }

                // Doctrine signals from the daily review's need vector (stage E); neutral before the first review.
                var signals = CampaignNeedBuilder.ToProjectSignals(
                    StrategicCoordinator.Instance.NeedSnapshots[alliance],
                    StrategicCoordinator.Instance.Needs[alliance]);

                for (int subsidyType = 0; subsidyType < lanes; subsidyType++)
                {
                    if (subsidyType == 6 || subsidyType == 7) continue;

                    bool constructionCurrentlyWins = UseSubsidyForPurpose(alliance, subsidyType) != 0;
                    if (constructionCurrentlyWins && nextProjects[subsidyType] < 0) continue;

                    int vanillaProjectId = nextProjects[subsidyType];
                    float vanillaWeight = ReadVanillaProjectWeight(aiPersonality, vanillaProjectId);
                    var candidates = BuildCandidates(alliance, subsidyType, aiPersonality);
                    float fundingAvailable = allianceState.subsidyfunding != null && subsidyType < allianceState.subsidyfunding.Length
                        ? allianceState.subsidyfunding[subsidyType]
                        : 0f;
                    var decision = ProjectDoctrineScorer.Select(
                        profile,
                        signals,
                        subsidyType,
                        vanillaProjectId,
                        vanillaWeight,
                        candidates,
                        projectId => FiscalPolicyScorer.ProjectWeight(fiscalIntent, alliance, projectId, subsidyType),
                        projectId => BuildRuntimeFacts(projectId, subsidyType, alliance, campaignYear),
                        fundingAvailable,
                        netFundingPerDay: 0f,
                        constructionCurrentlyWins: constructionCurrentlyWins);

                    if (decision.LaneIntent != null && decision.LaneIntent.CriticalDoctrineProject && decision.LaneIntent.ConstructionCurrentlyWins)
                    {
                        string starvedSignature = StableStarvedLaneSignature(decision.LaneIntent);
                        if (StarvedLaneLogGate.ShouldLog(starvedSignature))
                        {
                            if (!_starvedLaneObserverLogged)
                            {
                                _starvedLaneObserverLogged = true;
                                EmitProjectDoctrineObserver("starved-lane", alliance);
                            }
                            EmitProjectDoctrineStarvedLane(decision.LaneIntent, starvedSignature);
                        }
                    }

                    if (constructionCurrentlyWins) continue;
                    if (!decision.ShouldReplace) continue;
                    if (decision.ProjectId < 0 || decision.ProjectId == vanillaProjectId) continue;

                    nextProjects[subsidyType] = decision.ProjectId;
                    string signature = ProjectDoctrineLogGate.SelectionSignature(alliance, subsidyType, vanillaProjectId, decision.ProjectId, decision.Reason);
                    if (SelectionLogGate.ShouldLog(signature))
                    {
                        if (!_selectionObserverLogged)
                        {
                            _selectionObserverLogged = true;
                            EmitProjectDoctrineObserver("selection", alliance);
                        }
                        TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "ProjectDoctrine", TelemetrySeverity.Info, ev => ev
                            .WithAlliance(alliance)
                            .WithDecision("select", decision.Reason, signature)
                            .WithField("lane", subsidyType)
                            .WithField("oldProject", vanillaProjectId)
                            .WithField("newProject", decision.ProjectId)
                            .WithField("era", era.ToString())
                            .WithField("funding", fundingAvailable)
                            .WithField("vanillaScore", decision.VanillaScore)
                            .WithField("bestScore", decision.BestScore));
                    }
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-selection:prefix", "[Patch:ProjectSelection] prefix failed: " + ex.Message);
            }
            } // end using TelemetryPerf.Scope
        }

        private static List<ProjectCandidateInput> BuildCandidates(
            int alliance,
            int subsidyType,
            GameVars.Alliance.AIPersonality aiPersonality)
        {
            var candidates = new List<ProjectCandidateInput>();
            var personalityProjects = aiPersonality?.projects;
            var personalityProjectProb = aiPersonality?.projectsprob;
            if (personalityProjects == null || personalityProjectProb == null)
            {
                OnceLog.Warning("project-selection:personality-projects", "[Patch:ProjectSelection] AI personality projects/projectsprob was null");
                return candidates;
            }

            int count = Math.Min(personalityProjects.Count, personalityProjectProb.Count);
            for (int i = 0; i < count; i++)
            {
                int projectId = personalityProjects[i];
                int vanillaProb = personalityProjectProb[i];
                if (projectId < 0 || vanillaProb <= 0) continue;

                var project = ResolveProject(projectId);
                if (project == null) continue;
                if (project.subsidytype != subsidyType) continue;
                if (!ProjectApplies(project, alliance)) continue;
                if (!ProjectIsAppointable(project, alliance)) continue;

                candidates.Add(new ProjectCandidateInput
                {
                    ProjectId = project.projectid,
                    SubsidyType = project.subsidytype,
                    VanillaWeight = ReadVanillaProjectWeight(aiPersonality, project.projectid)
                });
            }

            return candidates;
        }

        private static string StableStarvedLaneSignature(ProjectLaneIntent intent)
        {
            if (intent == null)
                return "missing";

            return intent.Alliance + "|"
                + intent.SubsidyLane + "|"
                + intent.QueuedProjectId + "|"
                + intent.ConstructionCurrentlyWins + "|"
                + intent.CriticalDoctrineProject;
        }

        private static void EmitProjectDoctrineObserver(string observer, int alliance)
        {
            string signature = "observer=" + observer + "|alliance=" + alliance;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.State, "ProjectDoctrine", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithDecision("observer", observer, signature)
                .WithField("observer", observer));
        }

        private static void EmitProjectDoctrineCatalog(int catalogEntries)
        {
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.State, "ProjectDoctrine", TelemetrySeverity.Info, ev => ev
                .WithDecision("catalog", "active-entries", "catalogEntries=" + catalogEntries)
                .WithField("catalogEntries", catalogEntries));
        }

        private static void EmitProjectDoctrineStarvedLane(ProjectLaneIntent intent, string signature)
        {
            if (intent == null) return;
            string inputSignature = string.IsNullOrWhiteSpace(signature)
                ? ProjectDoctrineLogGate.StarvedLaneSignature(intent)
                : signature;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Gate, "ProjectDoctrine", TelemetrySeverity.Info, ev => ev
                .WithAlliance(intent.Alliance)
                .WithDecision("starved-lane", "starved-critical-project", inputSignature)
                .WithField("lane", intent.SubsidyLane)
                .WithField("queued", intent.QueuedProjectId)
                .WithField("fundingAvailable", intent.FundingAvailable)
                .WithField("fundingNeeded", intent.FundingNeeded)
                .WithField("netFundingPerDay", intent.NetFundingPerDay)
                .WithField("timeToFundDays", intent.TimeToFundEstimateDays)
                .WithField("constructionWins", intent.ConstructionCurrentlyWins)
                .WithField("criticalDoctrineProject", intent.CriticalDoctrineProject));
        }

        // The campaign date's year (0 when vanilla cannot say), read once per selection.
        private static int ReadCampaignYear()
        {
            try { return GameVars.GetCampaignFloatingDateSep().year; }
            catch (Exception ex)
            {
                OnceLog.Warning("project-selection:campaign-year", "[Patch:ProjectSelection] campaign year read failed: " + ex.Message);
                return 0;
            }
        }

        private static ProjectRuntimeFacts BuildRuntimeFacts(int projectId, int subsidyType, int alliance, int campaignYear)
        {
            var project = ResolveProject(projectId);
            if (project == null)
                return new ProjectRuntimeFacts { ProjectId = projectId, SubsidyLane = subsidyType };

            var facts = new ProjectRuntimeFacts
            {
                ProjectId = projectId,
                SubsidyLane = project.subsidytype,
                CampaignYear = campaignYear
            };

            try
            {
                int level = 1;
                if (GameVars.alliance != null && alliance >= 0 && alliance < GameVars.alliance.Length && GameVars.alliance[alliance] != null)
                    level = Math.Max(1, (int)GameVars.alliance[alliance].GetProjectLevel(projectId) + 1);
                facts.Cost = project.GetSubsidyCost(level);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-selection:cost", "[Patch:ProjectSelection] project cost read failed: " + ex.Message);
            }

            try
            {
                if (project.datefrom != null && alliance >= 0)
                {
                    if (alliance < project.datefrom.Length && project.datefrom[alliance] != null)
                    {
                        facts.DateFromKnown = true;
                        facts.DateFromYear = project.datefrom[alliance].year;
                        facts.DateFromMonth = project.datefrom[alliance].month;
                        facts.DateFromDay = project.datefrom[alliance].day;
                    }
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-selection:datefrom", "[Patch:ProjectSelection] project date read failed: " + ex.Message);
            }

            return facts;
        }

        private static Projects.LoadedProjects ResolveProject(int projectId)
        {
            try
            {
                return Projects.LoadedProjects.GetLoadedProjectFromID(projectId);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-selection:resolve-project", "[Patch:ProjectSelection] GetLoadedProjectFromID failed: " + ex.Message);
                return null;
            }
        }

        private static bool ProjectApplies(Projects.LoadedProjects project, int alliance)
        {
            try
            {
                if (project.alliances == null || !project.alliances.Contains(alliance)) return false;
                if (project.applicablescenarios == null || !project.applicablescenarios.Contains(GamePrefs.leveltoload)) return false;
                if (!project.FulfillsDLC()) return false;
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-selection:applies", "[Patch:ProjectSelection] project applicability check failed: " + ex.Message);
                return false;
            }
        }

        private static bool ProjectIsAppointable(Projects.LoadedProjects project, int alliance)
        {
            try
            {
                return Projects.IsAppointable(project, alliance, useprestige: false, usesubsidies: false);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-selection:isappointable", "[Patch:ProjectSelection] Projects.IsAppointable failed for a candidate: " + ex.Message);
                return false;
            }
        }

        private static int UseSubsidyForPurpose(int alliance, int subsidyType)
        {
            try
            {
                return AICampaign.UseSubsidyForPurpose(alliance, subsidyType);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-selection:subsidy-purpose", "[Patch:ProjectSelection] AICampaign.UseSubsidyForPurpose failed: " + ex.Message);
                return 1;
            }
        }

        private static float ReadVanillaProjectWeight(GameVars.Alliance.AIPersonality aiPersonality, int projectId)
        {
            if (aiPersonality == null || projectId < 0) return 0f;
            var projects = aiPersonality.projects;
            var projectProb = aiPersonality.projectsprob;
            if (projects == null || projectProb == null) return 0f;

            float max = 0f;
            for (int i = 0; i < projectProb.Count; i++)
                if (projectProb[i] > max)
                    max = projectProb[i];

            if (max <= 0f) return 0f;

            int count = Math.Min(projects.Count, projectProb.Count);
            for (int i = 0; i < count; i++)
                if (projects[i] == projectId && projectProb[i] > 0)
                    return projectProb[i] / max;

            return 0f;
        }
    }
}
