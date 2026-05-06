using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic.Projects;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Projects.CheckProjectUnlocks seeds date/scenario project IDs
    // during campaign setup. This Prefix/Postfix observes newly added IDs only.
    [HarmonyPatch(typeof(Projects), "CheckProjectUnlocks")]
    internal static class ProjectUnlockObserverPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(int alliance, out HashSet<int> __state)
        {
            __state = Snapshot(alliance);
        }

        [HarmonyPostfix]
        internal static void Postfix(int alliance, HashSet<int> __state)
        {
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (alliance < 0 || alliance >= 2) return;

                OnceLog.Info("project-unlock-observer", "Project unlock observer wired");

                var after = Snapshot(alliance);
                foreach (int projectId in after)
                {
                    if (__state != null && __state.Contains(projectId)) continue;

                    var project = ResolveProject(projectId);
                    var entry = ResolveCatalogEntry(projectId);
                    int lane = entry != null ? entry.SubsidyLane : (project != null ? project.subsidytype : -1);
                    string bucket = entry != null ? entry.Bucket.ToString() : "Unknown";
                    string name = project != null ? project.projectname : "unknown";

                    Plugin.Log.LogInfo(
                        $"[ProjectUnlock] alliance={alliance} project={projectId} name=\"{name}\" lane={lane} bucket={bucket} source=CheckProjectUnlocks");
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-unlock-observer:postfix", "[ProjectUnlock] observer failed: " + ex.Message);
            }
        }

        private static HashSet<int> Snapshot(int alliance)
        {
            var ids = new HashSet<int>();
            try
            {
                if (GameVars.alliance == null) return ids;
                if (alliance < 0 || alliance >= GameVars.alliance.Length) return ids;

                var allianceState = GameVars.alliance[alliance];
                if (allianceState == null || allianceState.projects == null) return ids;

                foreach (int id in allianceState.projects)
                    ids.Add(id);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-unlock-observer:snapshot", "[ProjectUnlock] snapshot failed: " + ex.Message);
            }

            return ids;
        }

        private static Projects.LoadedProjects ResolveProject(int projectId)
        {
            try
            {
                return Projects.LoadedProjects.GetLoadedProjectFromID(projectId);
            }
            catch
            {
                return null;
            }
        }

        private static ProjectDoctrineEntry ResolveCatalogEntry(int projectId)
        {
            try
            {
                return ProjectDoctrineCatalog.Get(projectId);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("project-unlock-observer:catalog", "[ProjectUnlock] catalog lookup failed: " + ex.Message);
                return null;
            }
        }
    }
}
