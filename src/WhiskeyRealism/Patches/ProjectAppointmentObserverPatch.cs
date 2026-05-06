using System;
using HarmonyLib;
using WhiskeyRealism.Strategic.Projects;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Projects.AppointProject spends subsidy/prestige, records the project,
    // and applies effects. This Postfix observes appointments only.
    [HarmonyPatch(typeof(Projects), "AppointProject")]
    internal static class ProjectAppointmentObserverPatch
    {
        internal struct AppointmentSnapshot
        {
            public bool Valid;
            public float OldLevel;
            public float SubsidyCost;
            public bool SubsidyCostKnown;
            public bool ManualAppointment;
            public bool PrestigePayment;
        }

        [HarmonyPrefix]
        internal static void Prefix(
            Projects.LoadedProjects project,
            int alliance,
            bool manualappointment,
            ref AppointmentSnapshot __state)
        {
            try
            {
                __state = default(AppointmentSnapshot);
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (project == null) return;
                if (alliance < 0 || alliance >= 2) return;

                if (!TryReadProjectLevel(project.projectid, alliance, out float oldLevel)) return;
                bool prestigePayment = manualappointment && IsWlScenarioActive();
                var snapshot = new AppointmentSnapshot
                {
                    Valid = true,
                    OldLevel = oldLevel,
                    ManualAppointment = manualappointment,
                    PrestigePayment = prestigePayment
                };

                try
                {
                    snapshot.SubsidyCost = project.GetSubsidyCost(NextProjectLevel(oldLevel));
                    snapshot.SubsidyCostKnown = true;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("project-appoint-observer:cost", "[ProjectAppointed] subsidy cost read failed: " + ex.Message);
                }

                __state = snapshot;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-appoint-observer:prefix", "[ProjectAppointed] observer prefix failed: " + ex.Message);
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(
            Projects.LoadedProjects project,
            int alliance,
            bool manualappointment,
            AppointmentSnapshot __state)
        {
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (project == null) return;
                if (alliance < 0 || alliance >= 2) return;
                if (!__state.Valid) return;

                OnceLog.Info("project-appoint-observer", "Project appointment observer wired");

                var entry = ProjectDoctrineCatalog.Get(project.projectid);
                string bucket = entry != null ? entry.Bucket.ToString() : "Unknown";
                string side = entry != null ? entry.UiSide.ToString() : "Unknown";
                int lane = entry != null ? entry.SubsidyLane : project.subsidytype;
                if (!TryReadProjectLevel(project.projectid, alliance, out float newLevel)) return;
                if (newLevel <= __state.OldLevel) return;

                Plugin.Log.LogInfo(
                    $"[ProjectAppointed] alliance={alliance} project={project.projectid} name=\"{project.projectname}\" " +
                    $"lane={lane} side={side} bucket={bucket} level={__state.OldLevel:F0}->{newLevel:F0} " +
                    $"payment={PaymentLabel(manualappointment, __state)} {CostLabel(__state)}");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-appoint-observer:postfix", "[ProjectAppointed] observer failed: " + ex.Message);
            }
        }

        private static bool TryReadProjectLevel(int projectId, int alliance, out float level)
        {
            level = 0f;
            try
            {
                if (GameVars.alliance == null || alliance >= GameVars.alliance.Length || GameVars.alliance[alliance] == null)
                {
                    OnceLog.Warning("project-appoint-observer:level", "[ProjectAppointed] level read failed: alliance state unavailable");
                    return false;
                }

                level = GameVars.alliance[alliance].GetProjectLevel(projectId);
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-appoint-observer:level", "[ProjectAppointed] level read failed: " + ex.Message);
                return false;
            }
        }

        private static bool IsWlScenarioActive()
        {
            try
            {
                return DLC_WL.dlc_scenarioactive;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-appoint-observer:dlc", "[ProjectAppointed] DLC scenario state read failed: " + ex.Message);
                return false;
            }
        }

        private static string PaymentLabel(bool manualappointment, AppointmentSnapshot snapshot)
        {
            if (snapshot.PrestigePayment) return "prestige/manual";
            return manualappointment ? "subsidy/manual" : "subsidy";
        }

        private static string CostLabel(AppointmentSnapshot snapshot)
        {
            if (snapshot.PrestigePayment)
            {
                return snapshot.SubsidyCostKnown
                    ? $"cost=unknown subsidyCostWouldBe={snapshot.SubsidyCost:F0}"
                    : "cost=unknown subsidyCostWouldBe=unknown";
            }

            return snapshot.SubsidyCostKnown ? $"cost={snapshot.SubsidyCost:F0}" : "cost=unknown";
        }

        private static int NextProjectLevel(float oldLevel)
        {
            return Math.Max(0, (int)Math.Floor(oldLevel)) + 1;
        }
    }
}
