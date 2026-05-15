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
    // before vanilla spending so the original method still funds/appoints.
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
                var profile = GrandStrategyRegistry.Resolve(alliance, era);
                var fiscalIntent = StrategicCoordinator.Instance.FiscalIntents != null && alliance < StrategicCoordinator.Instance.FiscalIntents.Length
                    ? StrategicCoordinator.Instance.FiscalIntents[alliance]
                    : null;

                if (!_catalogLogged)
                {
                    _catalogLogged = true;
                    EmitProjectDoctrineCatalog(ProjectDoctrineCatalog.AllActive.Count);
                }

                for (int subsidyType = 0; subsidyType < lanes; subsidyType++)
                {
                    if (subsidyType == 6 || subsidyType == 7) continue;

                    bool constructionCurrentlyWins = UseSubsidyForPurpose(alliance, subsidyType) != 0;
                    if (constructionCurrentlyWins && nextProjects[subsidyType] < 0) continue;

                    int vanillaProjectId = nextProjects[subsidyType];
                    float vanillaWeight = ReadVanillaProjectWeight(aiPersonality, vanillaProjectId);
                    var candidates = BuildCandidates(alliance, subsidyType, aiPersonality);
                    var signals = BuildSignals(alliance, era, fiscalIntent);
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
                        projectId => BuildRuntimeFacts(projectId, subsidyType, alliance),
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

        private static ProjectRuntimeFacts BuildRuntimeFacts(int projectId, int subsidyType, int alliance)
        {
            var project = ResolveProject(projectId);
            if (project == null)
                return new ProjectRuntimeFacts { ProjectId = projectId, SubsidyLane = subsidyType };

            var facts = new ProjectRuntimeFacts
            {
                ProjectId = projectId,
                SubsidyLane = project.subsidytype
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

        private static ProjectDoctrineSignals BuildSignals(int alliance, EraStage era, FiscalOutput fiscalIntent)
        {
            var input = new ProjectDoctrineSignalInput
            {
                Alliance = alliance,
                Era = era,
                FiscalPosture = fiscalIntent != null ? fiscalIntent.Posture : FiscalPosture.BalancedWar,
                PortViabilityInput = 0.5f,
                StrengthRatio = 1f
            };

            try
            {
                int enemy = alliance == 0 ? 1 : 0;
                if (GameVars.alliance != null && alliance >= 0 && enemy >= 0 && alliance < GameVars.alliance.Length && enemy < GameVars.alliance.Length)
                {
                    var ownState = GameVars.alliance[alliance];
                    var enemyState = GameVars.alliance[enemy];

                    input.BlockadeRatio = GameVars.alliance.Length > 1 && GameVars.alliance[1] != null
                        ? GameVars.alliance[1].averageblockaderatio
                        : 0f;
                    input.SupplyPressure = fiscalIntent != null && fiscalIntent.SupplyProtection ? 0.75f : 0f;
                    input.TransportPressure = fiscalIntent != null && fiscalIntent.LogisticsExpansion ? 0.75f : 0f;

                    input.RecognitionProbability = ReadRecognitionProbability(alliance);

                    if (ownState != null && enemyState != null)
                    {
                        float ownWeaponProduction = ownState.GetProjectLevel(102);
                        float enemyWeaponProduction = enemyState.GetProjectLevel(102);
                        float ownIndustry = ownState.GetProjectLevel(104);
                        float enemyIndustry = enemyState.GetProjectLevel(104);
                        input.IndustryGapInput = ProjectDoctrineSignalBuilder.Clamp01(
                            Math.Max(enemyWeaponProduction + enemyIndustry - ownWeaponProduction - ownIndustry, 0f) / 3f);
                    }
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-selection:signals", "[Patch:ProjectSelection] signal read failed: " + ex.Message);
            }

            return ProjectDoctrineSignalBuilder.Build(input);
        }

        private static float ReadRecognitionProbability(int alliance)
        {
            try
            {
                if (alliance < 0 || alliance >= 2) return 0f;
                if (GameVars.alliance == null || GameVars.alliance.Length <= 3) return 0f;
                if (GameVars.alliance[0] == null || GameVars.alliance[1] == null) return 0f;

                var unionIntervention = GameVars.alliance[0].interventionlevels;
                var confederateIntervention = GameVars.alliance[1].interventionlevels;
                if (unionIntervention == null || confederateIntervention == null) return 0f;
                if (unionIntervention.Length <= 3 || confederateIntervention.Length <= 3) return 0f;

                return Math.Max(
                    GameVars.Alliance.GetInterventionProbability(2),
                    GameVars.Alliance.GetInterventionProbability(3));
            }
            catch (Exception)
            {
                OnceLog.Warning("project-selection:recognition-signal", "[Patch:ProjectSelection] recognition signal unavailable; using neutral value");
                return 0f;
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
