using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // One theaterposition write the steering executor should make.
    internal struct TheaterWrite
    {
        public int InstanceId;
        public float X, Z;
    }

    // Spec C8: at most twice per formation per week. The runtime records the writes it makes.
    internal sealed class TheaterWriteLimiter
    {
        public const int MaxPerWeek = 2;
        public const int WeekDays = 7;
        private readonly Dictionary<int, List<int>> _writes = new Dictionary<int, List<int>>();

        public bool MayWrite(int instanceId, int daySerial)
        {
            if (!_writes.TryGetValue(instanceId, out var days)) return true;
            int recent = 0;
            for (int i = 0; i < days.Count; i++)
                if (daySerial - days[i] < WeekDays) recent++;
            return recent < MaxPerWeek;
        }

        public void Record(int instanceId, int daySerial)
        {
            if (!_writes.TryGetValue(instanceId, out var days))
            {
                days = new List<int>();
                _writes[instanceId] = days;
            }
            days.RemoveAll(d => daySerial - d >= WeekDays);
            days.Add(daySerial);
        }

        public void Clear()
        {
            _writes.Clear();
        }
    }

    // The pure halves of the steering executors (plan G5a): the campaign-objective write, the theater box and the importance
    // pointer's sector test. The runtime (CampaignSteeringRuntime, ImportanceValuesPatch) applies what these decide.
    internal static class SteeringRules
    {
        // A theater centre stays this fraction of the box's half extent from the formation, so the formation is strictly inside
        // the box vanilla tests (IsWithinOperationsTheater 17105: |position - theaterposition| < theatersize on x and z).
        public const float BoxMargin = 0.9f;
        // An existing centre within this fraction of the box already serves the formation: no rewrite.
        public const float RewriteTolerance = 0.1f;

        // ObjectiveWrite's answer for "write nothing".
        public const int NoWrite = int.MinValue;

        // Spec C6: the value to write into AIFaction.followedcampaignobjective, NoWrite for none.
        // Follow the plan's main objective while vanilla offers it (written only when it differs from what is followed); with no
        // main effort, or one vanilla no longer offers, release a value this mod wrote (-1, so vanilla re-picks, 17780-17786)
        // and leave vanilla's own pick alone.
        public static int ObjectiveWrite(int planMain, bool planMainAvailable, int current, int lastWrittenByMod)
        {
            int write;
            if (planMain >= 0 && planMainAvailable) write = planMain;
            else write = lastWrittenByMod >= 0 && current == lastWrittenByMod ? -1 : NoWrite;
            return write == NoWrite || write == current ? NoWrite : write;
        }

        // Spec C8: the theater centre toward a target that keeps the formation inside the box, on both axes.
        public static void TheaterCenter(float unitX, float unitZ, float targetX, float targetZ, float halfX, float halfZ, out float centerX, out float centerZ)
        {
            centerX = Clamp(targetX, unitX - BoxMargin * halfX, unitX + BoxMargin * halfX);
            centerZ = Clamp(targetZ, unitZ - BoxMargin * halfZ, unitZ + BoxMargin * halfZ);
        }

        // The theaterposition writes of one review: a MainEffort or Support formation already inside its sector is boxed toward
        // its target, a Hold formation is centred on itself; nothing else, and nothing for a formation the planner may not move
        // (the player's command, the map-group player) or one that is busy. A centre that already serves the formation is left
        // alone, and the limiter bounds the rewrites. The box size is GamePrefs.theatersize (the snapshot's half extents).
        public static void PlanTheaterWrites(CampaignSnapshot s, Allocation allocation, SectorModel sectors, TheaterWriteLimiter limiter, List<TheaterWrite> into)
        {
            into.Clear();
            if (s == null || allocation == null || sectors == null || limiter == null) return;
            float halfX = s.TheaterHalfExtentX, halfZ = s.TheaterHalfExtentZ;
            if (!(halfX > 0f) || !(halfZ > 0f)) return;
            for (int k = 0; k < allocation.Assignments.Count; k++)
            {
                var a = allocation.Assignments[k];
                int i = s.IndexOfOwn(a.InstanceId);
                if (i < 0) continue;
                var f = s.Own[i];
                if (!WlChainRules.MayMove(f.Chain) || !TheaterPlanner.Movable(f)) continue;
                float cx, cz;
                if (a.Role == FormationRole.MainEffort || a.Role == FormationRole.Support)
                {
                    if (sectors.SectorAt(f.X, f.Z) != a.SectorId) continue;
                    TheaterCenter(f.X, f.Z, a.TargetX, a.TargetZ, halfX, halfZ, out cx, out cz);
                }
                else if (a.Role == FormationRole.Hold)
                {
                    cx = f.X;
                    cz = f.Z;
                }
                else continue;
                if (f.HasTheater && Math.Abs(f.TheaterX - cx) <= RewriteTolerance * halfX && Math.Abs(f.TheaterZ - cz) <= RewriteTolerance * halfZ) continue;
                if (!limiter.MayWrite(f.InstanceId, s.DaySerial)) continue;
                into.Add(new TheaterWrite { InstanceId = f.InstanceId, X = cx, Z = cz });
            }
        }

        // Spec C7, narrowed by the smoke amendments 2026-10-16 (verify round 1, F3): an AI area (vanilla's aiarea index) lying in the MAIN
        // sector is pointed at the area holding its target, and only once the main effort is not staged (TheaterPlan.MainStaged: the
        // objective is unscouted and the mass is gathering at its start line, so nothing may be pulled at it yet). A Support sector, with
        // its single reinforcement, no longer points vanilla's own offensive gather at the main target from the rest of the map; any
        // other area keeps vanilla's pick.
        public static bool ImportanceSteers(TheaterPlan plan, SectorModel sectors, int sourceAreaIndex, out float targetX, out float targetZ)
        {
            targetX = 0f;
            targetZ = 0f;
            if (plan == null || sectors == null || plan.MainStaged) return false;
            int sector = sectors.SectorOfArea(sourceAreaIndex);
            if (sector < 0) return false;
            for (int i = 0; i < plan.Sectors.Count; i++)
            {
                var sp = plan.Sectors[i];
                if (sp == null || sp.SectorId != sector) continue;
                if (sp.Posture != TheaterPosture.MainEffort || sp.TargetObjectiveId < 0) return false;
                targetX = sp.TargetX;
                targetZ = sp.TargetZ;
                return true;
            }
            return false;
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }
}
