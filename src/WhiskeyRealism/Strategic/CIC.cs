using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    public class CIC
    {
        public int AllianceId;
        public int OfficerCommanderId;
        public string OfficerName;
        public PersonalityVector OfficerPersonality;
        public OperationalPlan ActivePlan;
        private static readonly HashSet<string> ObjectivesDiagnosticLogged = new HashSet<string>();

        public PersonalityVector Effective(EraStageManager era)
        {
            var composed = PersonalityVector.Compose(
                OfficerPersonality,
                era.StageVector,
                FactionProfiles.For(AllianceId));

            bool overrideSettings = Plugin.Instance?.OverrideVanillaSettings?.Value ?? false;
            int lockedDifficulty = Plugin.Instance?.LockedDifficulty?.Value
                ?? DifficultyPersonalityModifier.HistoricalHardDifficultyIndex;

            return PersonalityVector.Add(
                composed,
                DifficultyPersonalityModifier.ForLockedHistoricalDifficulty(
                    overrideSettings,
                    lockedDifficulty));
        }

        // Consults PhaseTruthOutput to decide how to handle the current plan.
        // When truth is null, falls back to ReviewPlan (deadline-only logic).
        // The routing switch is pure logic delegated to CicReviewRouter so it can be tested
        // without BepInEx/HarmonyLib dependencies in the test harness.
        public bool ReviewPlanWithTruth(int currentMonth, int currentYear, PhaseTruthOutput truth)
        {
            if (ActivePlan == null) return false;
            if (truth == null) return ReviewPlan(currentMonth, currentYear);

            switch (truth.RecommendedAction)
            {
                case PhaseTruthAction.Advance:
                    return AdvancePhase();
                case PhaseTruthAction.Complete:
                    ActivePlan.IsDirty = true;
                    return false;
                case PhaseTruthAction.Pause:
                    MarkOperationDecision(truth, OperationPosture.ScreenAndDelay, allowAttack: false, allowReinforce: true, probeOnly: true, currentMonth: currentMonth, currentYear: currentYear);
                    return true;
                case PhaseTruthAction.Abort:
                    ActivePlan = null;
                    return false;
                case PhaseTruthAction.Exploit:
                    MarkOperationDecision(truth, OperationPosture.ExploitBreakthrough, allowAttack: true, allowReinforce: true, probeOnly: false, currentMonth: currentMonth, currentYear: currentYear);
                    return true;
                case PhaseTruthAction.Counterstroke:
                    MarkOperationDecision(truth, OperationPosture.Counterstroke, allowAttack: true, allowReinforce: true, probeOnly: false, currentMonth: currentMonth, currentYear: currentYear);
                    return true;
                case PhaseTruthAction.ScreenAndDelay:
                    MarkOperationDecision(truth, OperationPosture.ScreenAndDelay, allowAttack: false, allowReinforce: true, probeOnly: true, currentMonth: currentMonth, currentYear: currentYear);
                    return true;
                case PhaseTruthAction.Recover:
                    MarkOperationDecision(truth, OperationPosture.Recover, allowAttack: false, allowReinforce: true, probeOnly: true, currentMonth: currentMonth, currentYear: currentYear);
                    return true;
                case PhaseTruthAction.Pivot:
                    ActivePlan.PendingRetarget = true;
                    ActivePlan.PendingRetargetReason = truth.AlternateOperationId;
                    ActivePlan.IsDirty = true;
                    return false;
                case PhaseTruthAction.Replan:
                    ActivePlan.PendingRetarget = false;
                    ActivePlan.PendingRetargetReason = null;
                    ActivePlan.IsDirty = true;
                    return false;
                case PhaseTruthAction.Continue:
                default:
                    return ReviewPlan(currentMonth, currentYear);
            }
        }

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

        private void MarkOperationDecision(
            PhaseTruthOutput truth,
            OperationPosture posture,
            bool allowAttack,
            bool allowReinforce,
            bool probeOnly,
            int currentMonth,
            int currentYear)
        {
            if (ActivePlan == null) return;
            ActivePlan.OperationPosture = posture;
            ActivePlan.OperationLastDecisionDaySerial = currentYear * 372 + currentMonth * 31;
            ActivePlan.PendingRetarget = false;
            ActivePlan.PendingRetargetReason = truth?.Reason;

            var phase = ActivePlan.CurrentPhase;
            if (phase == null) return;
            phase.OperationPosture = posture;
            phase.AllowCoordinatedAttack = allowAttack;
            phase.AllowReinforcementPackage = allowReinforce;
            phase.AllowProbeOnly = probeOnly;
        }

        public bool AdvancePhase()
        {
            if (ActivePlan == null) return false;
            if (ActivePlan.CurrentPhaseIndex + 1 < ActivePlan.Phases.Count)
            {
                ActivePlan.CurrentPhaseIndex++;
                EmitCampaignInfo($"[Plan:{OfficerName}] action=phase-advanced phase={ActivePlan.CurrentPhaseIndex}");
                return true;
            }
            ActivePlan.IsDirty = true;
            return false;
        }

        public void Replan(EraStageManager era, int currentMonth, int currentYear)
        {
            int daySerial = currentYear * 372 + currentMonth * 31 + 1;
            Replan(
                era,
                currentMonth,
                currentYear,
                daySerial,
                ResolveCurrentChapter(),
                null,
                null);
        }

        public void Replan(
            EraStageManager era,
            int currentMonth,
            int currentYear,
            int daySerial,
            int vanillaChapter,
            DirectorPosture posture,
            HistoricalOperationContext context)
        {
            var availableObjectives = GetAvailableObjectivesViaReflection(AllianceId);
            int objCount = availableObjectives?.Count ?? -1;
            if (availableObjectives == null || availableObjectives.Count == 0)
            {
                // -1 = reflection failure (warning was already logged); 0 = vanilla
                // genuinely returned empty (likely pre-war chapter, no objectives published yet).
                EmitPlanDecision("clear", "no-available-objectives", "objectiveCount=" + objCount, ev => ev.WithField("objectiveCount", objCount));
                ActivePlan = null;
                return;
            }

            var p = Effective(era);
            var strategy = GrandStrategyRegistry.Resolve(AllianceId, era.Stage);
            var scored = new List<(object obj, float score, ObjectiveMetadata meta)>();

            foreach (var obj in availableObjectives)
            {
                var meta = ObjectiveAdapter.Resolve(obj);
                float score = ScoreObjective(p, strategy, meta);
                scored.Add((obj, score, meta));
            }

            scored.Sort((a, b) => b.score.CompareTo(a.score));
            bool doctrineEnabled = Plugin.Instance == null ||
                Plugin.Instance.EnableHistoricalOperationDoctrine == null ||
                Plugin.Instance.EnableHistoricalOperationDoctrine.Value;

            if (doctrineEnabled)
            {
                string requestedOperationId = ActivePlan?.PendingRetarget == true
                    ? ActivePlan.PendingRetargetReason
                    : null;
                if (!string.IsNullOrEmpty(requestedOperationId))
                {
                    var requested = SelectRequestedHistoricalPlan(
                        requestedOperationId,
                        scored,
                        p,
                        strategy,
                        currentMonth,
                        currentYear,
                        daySerial);
                    if (requested != null)
                    {
                        ActivePlan = requested;
                        return;
                    }
                }

                ActivePlan = SelectHistoricalPlan(
                    scored,
                    p,
                    strategy,
                    era.Stage,
                    currentMonth,
                    currentYear,
                    daySerial,
                    vanillaChapter,
                    posture,
                    context);
                return;
            }

            var top3 = scored.GetRange(0, Math.Min(3, scored.Count));
            var picked = WeightedPick(top3);

            ActivePlan = BuildLegacyGenericPlan(picked.obj, picked.meta, p, strategy, currentMonth, currentYear);

            if (ActivePlan != null && Plugin.Instance.PlanTrace.Value)
            {
                EmitPlanDecision("select", ActivePlan.Rationale, "alliance=" + AllianceId + "|officer=" + OfficerName + "|phases=" + ActivePlan.Phases.Count + "|deadline=" + ActivePlan.PlanDeadlineYear + "-" + ActivePlan.PlanDeadlineMonth.ToString("D2"), ev => ev
                    .WithField("phases", ActivePlan.Phases.Count)
                    .WithField("deadlineYear", ActivePlan.PlanDeadlineYear)
                    .WithField("deadlineMonth", ActivePlan.PlanDeadlineMonth));
                for (int i = 0; i < scored.Count && i < 5; i++)
                    EmitCampaignInfo($"[Plan:scores] alliance={AllianceId} obj_id={GetObjectiveId(scored[i].obj)} score={scored[i].score:F2} theater={scored[i].meta.Theater} category={scored[i].meta.Category}");
            }
        }

        private OperationalPlan SelectRequestedHistoricalPlan(
            string operationId,
            List<(object obj, float score, ObjectiveMetadata meta)> scored,
            PersonalityVector p,
            GrandStrategyProfile strategy,
            int currentMonth,
            int currentYear,
            int daySerial)
        {
            if (!HistoricalOperationCatalog.TryGetById(operationId, out var profile))
            {
                EmitHistoricalOperation("no-profile", operationId, -1, null, "alternate-not-found", null);
                return null;
            }

            for (int i = 0; i < scored.Count; i++)
            {
                int objectiveId = GetObjectiveId(scored[i].obj);
                if (objectiveId != profile.PrimaryObjectiveId)
                    continue;

                var match = new HistoricalOperationMatch
                {
                    Kind = HistoricalOperationMatchKind.Matched,
                    Profile = profile,
                    Score = scored[i].score,
                    Reason = "requested-pivot"
                };
                var plan = BuildHistoricalOperationPlan(
                    scored[i].obj,
                    match,
                    p,
                    strategy,
                    currentMonth,
                    currentYear,
                    daySerial);
                if (plan == null)
                {
                    EmitHistoricalOperation("no-profile", operationId, objectiveId, null, "requested-plan-invalid", null);
                    return null;
                }

                EmitHistoricalOperation("pivot", operationId, objectiveId, plan.CurrentPhase?.PhaseId, "requested-pivot", scored[i].score);
                return plan;
            }

            EmitHistoricalOperation("no-profile", operationId, -1, null, "alternate-objective-unavailable", null);
            return null;
        }

        private float ScoreObjective(PersonalityVector p, GrandStrategyProfile strategy, ObjectiveMetadata meta)
        {
            return ObjectiveScoring.Score(AllianceId, p, strategy, meta);
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

        private OperationalPlan SelectHistoricalPlan(
            List<(object obj, float score, ObjectiveMetadata meta)> scored,
            PersonalityVector p,
            GrandStrategyProfile strategy,
            EraStage era,
            int currentMonth,
            int currentYear,
            int daySerial,
            int vanillaChapter,
            DirectorPosture posture,
            HistoricalOperationContext context)
        {
            HistoricalOperationMatch bestMatch = null;
            (object obj, float score, ObjectiveMetadata meta) bestCandidate = default;
            int limit = Math.Min(5, scored.Count);

            for (int i = 0; i < limit; i++)
            {
                int objId = GetObjectiveId(scored[i].obj);
                var candidate = new HistoricalOperationCandidate
                {
                    ObjectiveId = objId,
                    Objective = scored[i].meta,
                    ObjectiveScore = scored[i].score
                };
                var match = HistoricalOperationCatalog.Resolve(
                    AllianceId,
                    era,
                    vanillaChapter,
                    currentMonth,
                    currentYear,
                    candidate,
                    strategy,
                    p,
                    posture,
                    context);
                if (match.Kind != HistoricalOperationMatchKind.Matched)
                {
                    EmitHistoricalOperation("no-profile-candidate", null, objId, null, match.Reason, null);
                    continue;
                }

                if (bestMatch == null ||
                    match.Profile.Priority < bestMatch.Profile.Priority ||
                    (match.Profile.Priority == bestMatch.Profile.Priority && match.Score > bestMatch.Score))
                {
                    bestMatch = match;
                    bestCandidate = scored[i];
                }
            }

            if (bestMatch == null)
            {
                int topObjective = scored.Count > 0 ? GetObjectiveId(scored[0].obj) : -1;
                EmitHistoricalOperation("no-profile", null, topObjective, null, "no-explicit-profile", null);
                return null;
            }

            var plan = BuildHistoricalOperationPlan(
                bestCandidate.obj,
                bestMatch,
                p,
                strategy,
                currentMonth,
                currentYear,
                daySerial);
            if (plan == null)
            {
                EmitHistoricalOperation("no-profile", bestMatch.Profile.OperationId, GetObjectiveId(bestCandidate.obj), null, "plan-invalid", null);
                return null;
            }
            EmitHistoricalOperation("select", bestMatch.Profile.OperationId, GetObjectiveId(bestCandidate.obj), plan.CurrentPhase?.PhaseId, bestMatch.Reason, bestMatch.Score);
            return plan;
        }

        private OperationalPlan BuildHistoricalOperationPlan(
            object pickedObjective,
            HistoricalOperationMatch match,
            PersonalityVector p,
            GrandStrategyProfile strategy,
            int currentMonth,
            int currentYear,
            int daySerial)
        {
            var profile = match.Profile;
            if (!HistoricalOperationCatalog.ValidateProfile(profile, out var reason))
            {
                EmitHistoricalOperation("no-profile", profile?.OperationId, GetObjectiveId(pickedObjective), null, reason, null);
                Plugin.Log.LogWarning(
                    $"[CIC] Historical operation profile invalid alliance={AllianceId} operation={profile?.OperationId ?? "<null>"} reason={reason}");
                return null;
            }

            int monthsAhead = 6;
            var deadline = AddMonths(currentMonth, currentYear, monthsAhead);
            var plan = new OperationalPlan
            {
                CICFactionAllianceId = AllianceId,
                AssignedTheaterId = 0,
                OperationId = profile.OperationId,
                OperationName = profile.OperationName,
                OperationTempo = profile.Tempo,
                OperationPosture = profile.Posture,
                OperationStartedDaySerial = daySerial,
                OperationLastDecisionDaySerial = daySerial,
                CurrentPhaseIndex = 0,
                PlanDeadlineMonth = deadline.month,
                PlanDeadlineYear = deadline.year,
                Rationale = $"operation={profile.OperationId} strategy={strategy.Name} posture={profile.Posture} tempo={profile.Tempo}",
                IsDirty = false
            };

            for (int i = 0; i < profile.Phases.Length; i++)
            {
                var template = profile.Phases[i];
                int phaseMonths = Math.Max(1, template.DeadlineDays / 30);
                var phaseDeadline = AddMonths(currentMonth, currentYear, phaseMonths);
                var posture = template.Posture == OperationPosture.Inherit ? profile.Posture : template.Posture;
                plan.Phases.Add(new Phase
                {
                    PhaseId = template.PhaseId,
                    PhaseName = template.PhaseName,
                    TargetAreaId = template.TargetAreaId,
                    TargetObjectiveId = template.TargetObjectiveId,
                    TargetAreaKey = template.TargetAreaKey,
                    TargetSectorKey = template.TargetSectorKey,
                    ForceFractionRequired = template.ForceFractionRequired,
                    Transition = template.Transition,
                    DeadlineMonth = phaseDeadline.month,
                    DeadlineYear = phaseDeadline.year,
                    OperationPosture = posture,
                    AllowCoordinatedAttack = template.AllowCoordinatedAttack,
                    AllowReinforcementPackage = template.AllowReinforcementPackage,
                    AllowProbeOnly = template.AllowProbeOnly,
                    PhaseStartedDaySerial = daySerial,
                    Fallback = null
                });
            }

            return plan;
        }

        private OperationalPlan BuildLegacyGenericPlan(object pickedObjective, ObjectiveMetadata meta, PersonalityVector p,
                                                       GrandStrategyProfile strategy,
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
                AssignedTheaterId    = 0,
                CurrentPhaseIndex    = 0,
                PlanDeadlineMonth    = deadline.month,
                PlanDeadlineYear     = deadline.year,
                OperationTempo       = OperationTempoPreset.Standard,
                OperationPosture     = OperationPosture.ProbeAndDevelop,
                Rationale            = $"strategy={strategy.Name} theater={meta.Theater} category={meta.Category} forceFrac={forceFraction:F2}",
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
                OperationPosture        = OperationPosture.ProbeAndDevelop,
                AllowCoordinatedAttack  = true,
                AllowReinforcementPackage = true,
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
                    OperationPosture       = OperationPosture.ProbeAndDevelop,
                    AllowCoordinatedAttack = false,
                    AllowReinforcementPackage = true,
                    AllowProbeOnly         = true,
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

        private void EmitHistoricalOperation(
            string action,
            string operationId,
            int objectiveId,
            string phaseId,
            string reason,
            float? score)
        {
            string safeOperation = string.IsNullOrWhiteSpace(operationId) ? "-" : operationId;
            string safePhase = string.IsNullOrWhiteSpace(phaseId) ? "-" : phaseId;
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "-" : reason;
            string signature = "alliance=" + AllianceId +
                "|action=" + action +
                "|operation=" + safeOperation +
                "|objective=" + objectiveId +
                "|phase=" + safePhase;
            if (score.HasValue)
                signature += "|score=" + (Math.Round(score.Value * 2.0f) / 2.0f).ToString("0.0");

            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "HistoricalOperation", TelemetrySeverity.Info, ev =>
            {
                ev.WithAlliance(AllianceId)
                    .WithPhase(safePhase)
                    .WithDecision(action, safeReason, signature)
                    .WithField("operation", safeOperation)
                    .WithField("objective", objectiveId);
                if (score.HasValue)
                    ev.WithField("score", score.Value);
            });
        }

        private void EmitPlanDecision(string decision, string reason, string signature, Action<TelemetryEvent> configure = null)
        {
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "Plan", TelemetrySeverity.Info, ev =>
            {
                ev.WithAlliance(AllianceId)
                    .WithUnit(OfficerName)
                    .WithDecision(decision, reason, string.IsNullOrWhiteSpace(signature) ? "alliance=" + AllianceId + "|decision=" + decision : signature)
                    .WithField("officer", OfficerName);
                if (ActivePlan != null)
                {
                    ev.WithField("operation", ActivePlan.OperationId ?? "-")
                        .WithField("currentPhase", ActivePlan.CurrentPhaseIndex);
                }
                configure?.Invoke(ev);
            });
        }

        private static void EmitCampaignInfo(string line)
        {
            TelemetryRouter.LegacyInfo(line, TelemetryLayer.Campaign);
        }

        private static int ResolveCurrentChapter()
        {
            try
            {
                var t = AccessTools.TypeByName("Policy");
                var f = t != null ? AccessTools.Field(t, "CurrentChapter") : null;
                if (f == null) return -1;
                return (int)f.GetValue(null);
            }
            catch
            {
                return -1;
            }
        }

        private static IList GetAvailableObjectivesViaReflection(int allianceId)
        {
            try
            {
                var t = AccessTools.TypeByName("CampaignObjective");
                if (t == null) { Plugin.Log.LogWarning("[CIC] CampaignObjective type not found"); return null; }

                // Diagnostic — inspect the global state to understand why filters return empty.
                // OnceLog'd so we don't spam.
                LogObjectivesDiagnosticOnce(t, allianceId);

                // Vanilla signature: public static List<CampaignObjective> GetAvailableObjectives(
                //     int allianceid, bool includeaccomplished = false, int mintownobjectives = 1)
                // mintownobjectives=0 lets abstract win-condition objectives pass.
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

        // Logs the global allcampaignobjectives count + leveltoload + per-objective gate
        // results to expose which filter is rejecting everything. OnceLog-keyed by alliance
        // so we get one snapshot per alliance per save-load cycle.
        private static void LogObjectivesDiagnosticOnce(Type coType, int allianceId)
        {
            string key = "objs-diag:" + allianceId;
            try
            {
                var allField = AccessTools.Field(coType, "allcampaignobjectives");
                var all = allField?.GetValue(null) as IList;
                var prefsType = AccessTools.TypeByName("GamePrefs");
                var leveltoload = AccessTools.Field(prefsType, "leveltoload")?.GetValue(null) as string ?? "<null>";

                int total = all?.Count ?? -1;
                int passAlliance = 0, passScenario = 0, passDeact = 0, passAccomp = 0;

                // Inspect the IsDeactivated sub-gates for the first matching alliance objective.
                int firstObjId = -1;
                int currentChapter = -999;
                bool nationNull = true;
                int[] firstObjChapters = null;

                if (all != null)
                {
                    foreach (var obj in all)
                    {
                        if (obj == null) continue;
                        int oa = (int)(AccessTools.Field(coType, "ObjectiveAlliance")?.GetValue(obj) ?? -1);
                        if (oa != allianceId) continue;
                        passAlliance++;

                        var scenList = AccessTools.Field(coType, "ObjectiveScenario")?.GetValue(obj) as IList<string>;
                        bool scenOk = scenList != null && scenList.Contains(leveltoload);
                        if (!scenOk) continue;
                        passScenario++;

                        var isDeactMethod = AccessTools.Method(coType, "IsDeactivated");
                        bool isDeact = isDeactMethod != null && (bool)isDeactMethod.Invoke(obj, null);

                        // Capture first objective's deeper state.
                        if (firstObjId < 0)
                        {
                            firstObjId = (int)(AccessTools.Field(coType, "UniqueObjectiveID")?.GetValue(obj) ?? -1);
                            firstObjChapters = AccessTools.Field(coType, "ObjectiveChapters")?.GetValue(obj) as int[];
                            var policyType = AccessTools.TypeByName("Policy");
                            currentChapter = (int)(AccessTools.Field(policyType, "CurrentChapter")?.GetValue(null) ?? -999);
                            var gvType = AccessTools.TypeByName("GameVars");
                            var nation = AccessTools.Field(gvType, "nation")?.GetValue(null);
                            nationNull = nation == null;
                        }

                        if (isDeact) continue;
                        passDeact++;

                        bool accomp = (bool)(AccessTools.Field(coType, "accomplished")?.GetValue(obj) ?? false);
                        if (accomp) continue;
                        passAccomp++;
                    }
                }

                string chapterStr = (firstObjChapters != null) ? string.Join(",", firstObjChapters) : "<null>";
                if (ObjectivesDiagnosticLogged.Add(key))
                {
                    EmitCampaignInfo(
                        $"[CIC:diag] alliance={allianceId} leveltoload='{leveltoload}' total={total} " +
                        $"alliance-pass={passAlliance} scenario-pass={passScenario} not-deact={passDeact} not-accomp={passAccomp} " +
                        $"| first-obj-id={firstObjId} firstObjChapters=[{chapterStr}] Policy.CurrentChapter={currentChapter} GameVars.nation==null:{nationNull}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[CIC:diag] " + ex.Message);
            }
        }
    }
}
