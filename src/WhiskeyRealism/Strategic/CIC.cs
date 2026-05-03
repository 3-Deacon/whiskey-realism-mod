using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    public class CIC
    {
        public int AllianceId;
        public int OfficerCommanderId;
        public string OfficerName;
        public PersonalityVector OfficerPersonality;
        public List<TheaterCommander> Theaters = new List<TheaterCommander>();
        public OperationalPlan ActivePlan;

        public PersonalityVector Effective(EraStageManager era)
            => PersonalityVector.Compose(
                   OfficerPersonality,
                   era.StageVector,
                   FactionProfiles.For(AllianceId));

        public bool ReviewPlan(int currentMonth, int currentYear)
        {
            if (ActivePlan == null) return false;
            if (ActivePlan.IsDirty) return false;

            if (currentYear > ActivePlan.PlanDeadlineYear ||
               (currentYear == ActivePlan.PlanDeadlineYear && currentMonth > ActivePlan.PlanDeadlineMonth))
                return false;

            var p = ActivePlan.CurrentPhase;
            if (p != null && (currentYear > p.DeadlineYear ||
                             (currentYear == p.DeadlineYear && currentMonth > p.DeadlineMonth)))
            {
                return AdvancePhase();
            }

            return true;
        }

        public bool AdvancePhase()
        {
            if (ActivePlan == null) return false;
            if (ActivePlan.CurrentPhaseIndex + 1 < ActivePlan.Phases.Count)
            {
                ActivePlan.CurrentPhaseIndex++;
                Plugin.Log.LogInfo($"[CIC:{OfficerName}] phase advanced to {ActivePlan.CurrentPhaseIndex}");
                return true;
            }
            ActivePlan.IsDirty = true;
            return false;
        }

        public void Replan(EraStageManager era, int currentMonth, int currentYear)
        {
            var availableObjectives = GetAvailableObjectivesViaReflection(AllianceId);
            int objCount = availableObjectives?.Count ?? -1;
            if (availableObjectives == null || availableObjectives.Count == 0)
            {
                // -1 = reflection failure (warning was already logged); 0 = vanilla
                // genuinely returned empty (likely pre-war chapter, no objectives published yet).
                Plugin.Log.LogInfo($"[CIC:{OfficerName}] Replan — no available objectives (count={objCount}), plan cleared.");
                ActivePlan = null;
                return;
            }

            var p = Effective(era);
            var scored = new List<(object obj, float score, ObjectiveMetadata meta)>();

            foreach (var obj in availableObjectives)
            {
                var meta = ObjectiveAdapter.Resolve(obj);
                float score = ScoreObjective(p, meta);
                scored.Add((obj, score, meta));
            }

            scored.Sort((a, b) => b.score.CompareTo(a.score));
            var top3 = scored.GetRange(0, Math.Min(3, scored.Count));

            var picked = WeightedPick(top3);

            ActivePlan = BuildPlan(picked.obj, picked.meta, p, currentMonth, currentYear);

            if (ActivePlan != null && Plugin.Instance.PlanTrace.Value)
            {
                Plugin.Log.LogInfo($"[Plan:{OfficerName}] {ActivePlan.Rationale} — phases={ActivePlan.Phases.Count} deadline={ActivePlan.PlanDeadlineYear}-{ActivePlan.PlanDeadlineMonth:D2}");
                for (int i = 0; i < scored.Count && i < 5; i++)
                    Plugin.Log.LogInfo($"  [Plan:scores] obj_id={GetObjectiveId(scored[i].obj)} score={scored[i].score:F2} theater={scored[i].meta.Theater} category={scored[i].meta.Category}");
            }
        }

        private float ScoreObjective(PersonalityVector p, ObjectiveMetadata meta)
        {
            float theaterPref = FactionProfiles.TheaterPreferenceFor(AllianceId, meta.Theater);
            float foreignWeight = FactionProfiles.ForeignRecognitionWeightFor(AllianceId);
            float forceRatioTerm = 0.5f;
            float distanceTerm   = 0f;

            return theaterPref
                 + meta.SupplyReachWeight        * 1.0f
                 + meta.ForeignRecognitionWeight * foreignWeight
                 + meta.AttritionWeight          * p.CasualtyTolerance
                 + forceRatioTerm                * (1f - p.Caution)
                 - distanceTerm                  * (1f - p.Audacity);
        }

        private (object obj, float score, ObjectiveMetadata meta) WeightedPick(
            List<(object obj, float score, ObjectiveMetadata meta)> top)
        {
            if (top.Count == 0) return default;
            if (top.Count == 1) return top[0];

            float total = 0f;
            foreach (var t in top) total += Math.Max(0f, t.score);
            if (total <= 0f) return top[0];

            float roll = (float)(new System.Random().NextDouble()) * total;
            float acc = 0f;
            foreach (var t in top)
            {
                acc += Math.Max(0f, t.score);
                if (roll <= acc) return t;
            }
            return top[0];
        }

        private OperationalPlan BuildPlan(object pickedObjective, ObjectiveMetadata meta, PersonalityVector p,
                                           int currentMonth, int currentYear)
        {
            int objId = GetObjectiveId(pickedObjective);
            int phaseCount = 2;
            if (p.Audacity < 0.0f) phaseCount = 3;
            if (p.Audacity < -0.3f && p.Caution > 0.3f) phaseCount = 4;

            float forceFraction = PersonalityVector.Clamp(0.4f + 0.4f * p.Caution + 0.3f * (1f - p.Audacity));
            forceFraction = Math.Max(0.3f, Math.Min(0.95f, forceFraction));

            int monthsAhead = (int)(6f * (1f + 0.5f * p.Caution));
            var deadline = AddMonths(currentMonth, currentYear, monthsAhead);

            var plan = new OperationalPlan
            {
                CICFactionAllianceId = AllianceId,
                AssignedTheaterId    = (Theaters.Count > 0 ? Theaters[0].TheaterId : 0),
                CurrentPhaseIndex    = 0,
                PlanDeadlineMonth    = deadline.month,
                PlanDeadlineYear     = deadline.year,
                Rationale            = $"theater={meta.Theater} category={meta.Category} forceFrac={forceFraction:F2}",
                IsDirty              = false
            };

            plan.Phases.Add(new Phase
            {
                TargetAreaId           = -1,
                TargetObjectiveId      = objId,
                ForceFractionRequired  = forceFraction,
                Transition             = PhaseTransition.TargetTaken,
                DeadlineMonth          = deadline.month,
                DeadlineYear           = deadline.year,
                Fallback               = null
            });

            if (phaseCount >= 3)
            {
                plan.Phases.Insert(0, new Phase
                {
                    TargetAreaId          = -1,
                    TargetObjectiveId     = objId,
                    ForceFractionRequired = Math.Max(0.2f, forceFraction - 0.2f),
                    Transition            = PhaseTransition.TargetEngaged,
                    DeadlineMonth         = AddMonths(currentMonth, currentYear, monthsAhead / 3).month,
                    DeadlineYear          = AddMonths(currentMonth, currentYear, monthsAhead / 3).year,
                    Fallback              = null
                });
                plan.CurrentPhaseIndex = 0;
            }

            return plan;
        }

        private static (int month, int year) AddMonths(int month, int year, int delta)
        {
            int total = month + delta;
            int dy = (total - 1) / 12;
            int dm = ((total - 1) % 12) + 1;
            return (dm, year + dy);
        }

        private static int GetObjectiveId(object campaignObjective)
        {
            if (campaignObjective == null) return -1;
            var f = AccessTools.Field(campaignObjective.GetType(), "UniqueObjectiveID");
            return f != null ? (int)f.GetValue(campaignObjective) : -1;
        }

        private static IList GetAvailableObjectivesViaReflection(int allianceId)
        {
            try
            {
                var t = AccessTools.TypeByName("CampaignObjective");
                if (t == null) { Plugin.Log.LogWarning("[CIC] CampaignObjective type not found"); return null; }
                // Vanilla signature: public static List<CampaignObjective> GetAvailableObjectives(
                //     int allianceid, bool includeaccomplished = false, int mintownobjectives = 1)
                // (decompile line 178825). Three args, two with defaults.
                //
                // We pass mintownobjectives = 0 (vanilla default is 1) so abstract
                // win-condition objectives — "Break Confederate Morale", etc. — pass
                // through. Vanilla's mintownobjectives=1 filter excludes objectives
                // without town/IIP targets. In W&L scenario "002" this prunes most
                // of the qualifying objectives (verified via Config/campaignobjectives.dat
                // inspection 2026-05-03). Our own ObjectiveAdapter scoring layer
                // gives abstract objectives lower priority than concrete ones.
                var m = AccessTools.Method(t, "GetAvailableObjectives", new[] { typeof(int), typeof(bool), typeof(int) });
                if (m == null) { Plugin.Log.LogWarning("[CIC] CampaignObjective.GetAvailableObjectives(int,bool,int) not found"); return null; }
                return m.Invoke(null, new object[] { allianceId, false, 0 }) as IList;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[CIC] GetAvailableObjectives reflection failed: " + ex.Message);
                return null;
            }
        }
    }
}
