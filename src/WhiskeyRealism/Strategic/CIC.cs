using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    // Config values the CIC reads. StrategicCoordinator refreshes these from Plugin config
    // before every review so CIC.cs stays free of BepInEx types and compiles in the harness.
    internal static class CicSettings
    {
        internal static bool OverrideVanillaSettings = true;  // Plugin default
        internal static int LockedDifficulty = DifficultyPersonalityModifier.HistoricalHardDifficultyIndex;
        internal static bool HistoricalOperationDoctrine = true;
    }

    public class CIC
    {
        public int AllianceId;
        public string OfficerName;
        public PersonalityVector OfficerPersonality;
        public OperationalPlan ActivePlan;
        private static readonly HashSet<string> ObjectivesDiagnosticLogged = new HashSet<string>();

        // Seams for the vanilla reads Replan needs. Defaults are the reflection paths;
        // the harness swaps them for fixtures.
        // Candidates for scoring/historical matching: mintownobjectives = 0, because the Union
        // operations target abstract objective 3 (Richmond, no towns in campaignobjectives.dat)
        // through its catalog position.
        internal static Func<int, IList> AvailableObjectivesProvider = alliance => GetAvailableObjectivesViaReflection(alliance, 0);
        // Exactly vanilla's own PickCampaignObjective call (decompile 17782): default
        // mintownobjectives = 1, i.e. at least one enemy-held town/IIP (178827).
        internal static Func<int, IList> VanillaPickableObjectivesProvider = alliance => GetAvailableObjectivesViaReflection(alliance, 1);
        internal static Func<object, int> ObjectiveIdReader = ReadObjectiveIdViaReflection;
        internal static Func<int, bool> TargetPositionResolves = id => ObjectiveAdapter.ResolveObjectivePosition(id).HasValue;
        internal static Func<object, bool> ObjectiveHasGround = ReadObjectiveHasGroundViaReflection;

        public PersonalityVector Effective(EraStageManager era)
        {
            var composed = PersonalityVector.Compose(
                OfficerPersonality,
                era.StageVector,
                FactionProfiles.For(AllianceId));

            return PersonalityVector.Add(
                composed,
                DifficultyPersonalityModifier.ForLockedHistoricalDifficulty(
                    CicSettings.OverrideVanillaSettings,
                    CicSettings.LockedDifficulty));
        }

        // Copy for StrategicLedgerSet.PublishFrom: the review mutates the active plan in place.
        public CIC CloneForPublish()
        {
            return new CIC
            {
                AllianceId = AllianceId,
                OfficerName = OfficerName,
                OfficerPersonality = OfficerPersonality,
                ActivePlan = ActivePlan?.Clone()
            };
        }

        // Applies a PhaseTruthOutput to the active plan. The routing itself lives in
        // CicReviewRouter (single implementation, harness-covered); this wrapper only drops
        // an aborted plan and logs phase advances.
        public bool ReviewPlanWithTruth(int day, int currentMonth, int currentYear, PhaseTruthOutput truth)
        {
            if (ActivePlan == null) return false;
            int daySerial = currentYear * 372 + currentMonth * 31 + day;
            int phaseBefore = ActivePlan.CurrentPhaseIndex;
            var outcome = CicReviewRouter.Route(ActivePlan, truth, daySerial, currentMonth, currentYear);
            if (ActivePlan.CurrentPhaseIndex != phaseBefore)
                EmitCampaignInfo($"[Plan:{OfficerName}] action=phase-advanced phase={ActivePlan.CurrentPhaseIndex}");

            if (outcome == CicRouteOutcome.AbandonPlan)
            {
                ActivePlan = null;
                return false;
            }
            return outcome == CicRouteOutcome.KeepPlan;
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
            var availableObjectives = AvailableObjectivesProvider(AllianceId);
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
            var availableIds = new HashSet<int>();
            for (int i = 0; i < scored.Count; i++)
                availableIds.Add(GetObjectiveId(scored[i].obj));

            if (CicSettings.HistoricalOperationDoctrine)
            {
                string requestedOperationId = ActivePlan?.PendingRetarget == true
                    ? ActivePlan.PendingRetargetReason
                    : null;
                if (!string.IsNullOrEmpty(requestedOperationId))
                {
                    var requested = SelectRequestedHistoricalPlan(
                        requestedOperationId,
                        scored,
                        availableIds,
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

                var historical = SelectHistoricalPlan(
                    scored,
                    availableIds,
                    p,
                    strategy,
                    era.Stage,
                    currentMonth,
                    currentYear,
                    daySerial,
                    vanillaChapter,
                    posture,
                    context);
                if (historical != null)
                {
                    ActivePlan = historical;
                    return;
                }
                // R5(d): no historical profile fits — fall back to the generic plan below
                // instead of leaving the faction with no plan (which used to block vanilla's
                // PickCampaignObjective and freeze followedcampaignobjective).
            }

            // Generic fallback only over objectives vanilla itself would pick and can act on:
            //  - in vanilla's own candidate set, GetAvailableObjectives(alliance) with the default
            //    mintownobjectives = 1 (PickCampaignObjective 17782 / 178827), and
            //  - owning at least one town/IIP: followedcampaignobjective only adds importance
            //    through the objective's towns/IIPs (UpdateImportanceValues 14922/14961). This is
            //    checked separately because vanilla's IIP counter (num2) is not reset per
            //    objective, so an abstract objective can slip through mintownobjectives = 1.
            // Abstract objectives 0-28 (e.g. 4 "Capture Washington") never qualify; with no such
            // candidate there is no plan and vanilla's PickCampaignObjective runs.
            var vanillaPickable = new HashSet<int>();
            var pickableList = VanillaPickableObjectivesProvider(AllianceId);
            if (pickableList != null)
                foreach (var obj in pickableList) vanillaPickable.Add(GetObjectiveId(obj));
            var reachable = new List<(object obj, float score, ObjectiveMetadata meta)>();
            for (int i = 0; i < scored.Count; i++)
            {
                int id = GetObjectiveId(scored[i].obj);
                if (vanillaPickable.Contains(id) && ObjectiveHasGround(scored[i].obj) && TargetPositionResolves(id))
                    reachable.Add(scored[i]);
            }
            if (reachable.Count == 0)
            {
                EmitPlanDecision("clear", "no-objective-with-ground", "alliance=" + AllianceId + "|decision=clear|objectiveCount=" + scored.Count, ev => ev.WithField("objectiveCount", scored.Count));
                ActivePlan = null;
                return;
            }

            var top3 = reachable.GetRange(0, Math.Min(3, reachable.Count));
            var picked = WeightedPick(top3);

            ActivePlan = BuildLegacyGenericPlan(picked.obj, picked.meta, p, strategy, currentMonth, currentYear);

            if (ActivePlan != null && Planner.PlannerSettings.PlanTrace)
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
            HashSet<int> availableIds,
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
            if (!HistoricalOperationCatalog.PhaseTargetsAvailable(profile, availableIds))
            {
                EmitHistoricalOperation("no-profile", operationId, profile.PrimaryObjectiveId, null, "alternate-phase-target-unavailable", null);
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
            HashSet<int> availableIds,
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
                    context,
                    availableIds);
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
                EmitHistoricalOperation(
                    "no-profile",
                    profile?.OperationId,
                    GetObjectiveId(pickedObjective),
                    null,
                    reason,
                    null,
                    TelemetryCategory.Failure,
                    TelemetrySeverity.Warning);
                OnceLog.Warning(
                    "historical-operation:no-profile:" + AllianceId + ":" + (profile?.OperationId ?? "<null>"),
                    $"CIC historical operation profile invalid alliance={AllianceId} operation={profile?.OperationId ?? "<null>"} reason={reason}");
                return null;
            }

            for (int i = 0; i < profile.Phases.Length; i++)
            {
                if (!TargetPositionResolves(profile.Phases[i].TargetObjectiveId))
                {
                    EmitHistoricalOperation("no-profile", profile.OperationId, GetObjectiveId(pickedObjective), profile.Phases[i].PhaseId, "phase-target-position-unresolved", null);
                    return null;
                }
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

            int planStartSerial = currentYear * 372 + currentMonth * 31 + 1;
            plan.Phases.Add(new Phase
            {
                PhaseStartedDaySerial  = planStartSerial,
                TargetAreaId           = -1,
                TargetObjectiveId      = objId,
                ForceFractionRequired  = forceFraction,
                Transition             = PhaseTransition.TargetTaken,
                DeadlineMonth          = deadline.month,
                DeadlineYear           = deadline.year,
                OperationPosture        = OperationPosture.ProbeAndDevelop,
                Fallback               = null
            });
            ApplyGenericFlags(plan.Phases[plan.Phases.Count - 1]);

            if (phaseCount >= 3)
            {
                plan.Phases.Insert(0, new Phase
                {
                    PhaseStartedDaySerial = planStartSerial,
                    TargetAreaId          = -1,
                    TargetObjectiveId     = objId,
                    ForceFractionRequired = Math.Max(0.2f, forceFraction - 0.2f),
                    Transition            = PhaseTransition.TargetEngaged,
                    DeadlineMonth         = AddMonths(currentMonth, currentYear, monthsAhead / 3).month,
                    DeadlineYear          = AddMonths(currentMonth, currentYear, monthsAhead / 3).year,
                    OperationPosture       = OperationPosture.ProbeAndDevelop,
                    Fallback              = null
                });
                ApplyGenericFlags(plan.Phases[0]);
                plan.CurrentPhaseIndex = 0;
            }

            return plan;
        }

        private static void ApplyGenericFlags(Phase phase)
        {
            CicReviewRouter.GenericPhaseFlags(phase.Transition, out bool attack, out bool reinforce, out bool probeOnly);
            phase.AllowCoordinatedAttack = attack;
            phase.AllowReinforcementPackage = reinforce;
            phase.AllowProbeOnly = probeOnly;
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
            return ObjectiveIdReader(campaignObjective);
        }

        // CampaignObjective.objectives: the Town/IIP list loaded from campaignobjectives.dat
        // (InitializeObjectives 178635).
        private static bool ReadObjectiveHasGroundViaReflection(object campaignObjective)
        {
            if (campaignObjective == null) return false;
            var list = AccessTools.Field(campaignObjective.GetType(), "objectives")?.GetValue(campaignObjective) as IList;
            return list != null && list.Count > 0;
        }

        private static int ReadObjectiveIdViaReflection(object campaignObjective)
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
            float? score,
            TelemetryCategory category = TelemetryCategory.Decision,
            TelemetrySeverity severity = TelemetrySeverity.Info)
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

            TelemetryRouter.Emit(TelemetryLayer.Campaign, category, "HistoricalOperation", severity, ev =>
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

        // Sidecar-routed rows ([Plan:*]) go to the sidecar only; a row whose tag
        // has no sidecar route ([CIC:diag]) is written to the main log, which
        // LegacyInfo alone never does.
        private static void EmitCampaignInfo(string line)
        {
            TelemetryRouter.LegacyInfoToMainLogIfAllowed(line, TelemetryLayer.Campaign);
        }

        private static IList GetAvailableObjectivesViaReflection(int allianceId, int minTownObjectives)
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
                return m.Invoke(null, new object[] { allianceId, false, minTownObjectives }) as IList;
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
