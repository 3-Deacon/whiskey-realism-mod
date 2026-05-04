using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AICampaign.UpdateProjects picks/funds the next AI research project
    // per subsidy lane. This Prefix seeds/replaces nextprojecttoresearch[subsidy]
    // before vanilla spending so the original method still funds/appoints.
    [HarmonyPatch(typeof(AICampaign), "UpdateProjects")]
    internal static class ProjectSelectionPatch
    {
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

                OnceLog.Info("project-selection", "ProjectSelectionPatch wired (grand-strategy project steering)");

                int lanes = nextProjects.Length;
                if (allianceState.subsidyfunding != null)
                    lanes = Math.Min(lanes, allianceState.subsidyfunding.Length);

                var era = StrategicCoordinator.Instance.Eras[alliance]?.Stage ?? EraStage.Amateur1861;
                var profile = GrandStrategyRegistry.Resolve(alliance, era);
                var fiscalIntent = StrategicCoordinator.Instance.FiscalIntents != null && alliance < StrategicCoordinator.Instance.FiscalIntents.Length
                    ? StrategicCoordinator.Instance.FiscalIntents[alliance]
                    : null;

                for (int subsidyType = 0; subsidyType < lanes; subsidyType++)
                {
                    if (UseSubsidyForPurpose(alliance, subsidyType) != 0) continue;

                    int vanillaProjectId = nextProjects[subsidyType];
                    float vanillaWeight = ReadVanillaProjectWeight(aiPersonality, vanillaProjectId);
                    var candidates = BuildCandidates(alliance, subsidyType, aiPersonality);
                    var decision = ProjectSelectionScorer.Select(
                        profile,
                        subsidyType,
                        vanillaProjectId,
                        vanillaWeight,
                        candidates,
                        projectId => FiscalPolicyScorer.ProjectWeight(fiscalIntent, alliance, projectId, subsidyType));

                    if (!decision.ShouldReplace) continue;
                    if (decision.ProjectId < 0 || decision.ProjectId == vanillaProjectId) continue;

                    nextProjects[subsidyType] = decision.ProjectId;
                    Plugin.Log.LogInfo(
                        $"[Patch:ProjectSelection] alliance={alliance} subsidy={subsidyType} " +
                        $"old={vanillaProjectId} new={decision.ProjectId} profile=\"{profile.Name}\" " +
                        $"vanillaScore={decision.VanillaScore:F2} bestScore={decision.BestScore:F2} reason={decision.Reason}");
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
