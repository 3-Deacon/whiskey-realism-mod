using System;

namespace WhiskeyRealism.Strategic
{
    public enum OperationalProbeDecision
    {
        None,
        Probe,
        Pause,
        Withdraw,
        Escalate
    }

    public sealed class OperationalProbeOptions
    {
        public int MinimumProbeDays = 3;
        public float MinimumProbeReadiness = 0.55f;
        public float MinimumProbeSupply = 0.45f;
        public float MaximumProbeStrengthFraction = 0.35f;
        public float EnemyReactionMultiplier = 1.45f;
        public float EscalateFriendlyRatio = 1.8f;
        public float WithdrawFriendlyRatio = 0.55f;
    }

    public sealed class OperationalProbeState
    {
        public string ProbeId;
        public string UnitKey;
        public string TargetAreaKey;
        public string SourceSectorKey;
        public int StartedDaySerial;
        public float LastObservedEnemyStrength;
        public float LastObservedFriendlyStrength;
    }

    public sealed class OperationalProbeInput
    {
        public int AllianceId;
        public int DaySerial;
        public string PlanTargetAreaKey;
        public FrontSectorLedger Fronts;
        public FormationDirectiveLedger FormationDirectives;
        public OperationalProbeState Previous;
        public float CurrentEnemyStrength = -1f;
        public float CurrentFriendlyStrength = -1f;
        public OperationalProbeOptions Options = new OperationalProbeOptions();
        public CoordinatedOperationOptions PackageOptions;
        public ContactEvidence ContactEvidence = ContactEvidence.EnemyPresent;
    }

    public sealed class OperationalProbeOutput
    {
        public int AllianceId;
        public OperationalProbeDecision Decision;
        public string Reason;
        public string ProbeId;
        public string SelectedUnitKey;
        public string TargetAreaKey;
        public string SourceSectorKey;
        public bool RequiresMassCommitment;
        public CoordinatedOperationOutput Package;
        public OperationalProbeState State;

        public string Signature()
        {
            return ((int)Decision).ToString() +
                "|" + (ProbeId ?? "-") +
                "|" + (SelectedUnitKey ?? "-") +
                "|" + (TargetAreaKey ?? "-") +
                "|" + (SourceSectorKey ?? "-") +
                "|" + (Reason ?? "-") +
                "|" + (RequiresMassCommitment ? "mass" : "limited") +
                "|" + (Package?.Signature() ?? "-");
        }
    }

    public static class OperationalProbeLedger
    {
        public static OperationalProbeOutput Build(OperationalProbeInput input)
        {
            var output = new OperationalProbeOutput();
            if (input == null) return NoOp(output, "missing-input");

            var options = input.Options ?? new OperationalProbeOptions();
            output.AllianceId = input.AllianceId;
            output.TargetAreaKey = input.PlanTargetAreaKey;

            if (string.IsNullOrEmpty(input.PlanTargetAreaKey))
                return NoOp(output, "missing-plan-target");

            if (input.Previous != null)
                return FinishWithPackage(input, EvaluateExistingProbe(input, options, output));

            if (input.FormationDirectives == null || input.FormationDirectives.Assignments == null)
                return NoOp(output, "missing-formation-ledger");

            FormationDirectiveAssignment selected = null;
            foreach (var assignment in input.FormationDirectives.Assignments)
            {
                if (!EligibleProbeFormation(assignment, input, options)) continue;
                if (selected == null || assignment.CombatAvailability < selected.CombatAvailability)
                    selected = assignment;
            }

            if (selected == null)
                return NoOp(output, "no-eligible-probe-formation");

            output.Decision = OperationalProbeDecision.Probe;
            output.Reason = "limited-contact-probe";
            output.SelectedUnitKey = selected.UnitKey;
            output.SourceSectorKey = selected.SectorKey;
            output.ProbeId = BuildProbeId(input.AllianceId, input.PlanTargetAreaKey, selected.UnitKey);
            output.RequiresMassCommitment = false;
            output.State = new OperationalProbeState
            {
                ProbeId = output.ProbeId,
                UnitKey = selected.UnitKey,
                TargetAreaKey = input.PlanTargetAreaKey,
                SourceSectorKey = selected.SectorKey,
                StartedDaySerial = input.DaySerial,
                LastObservedEnemyStrength = Math.Max(0f, CurrentEnemy(input, selected)),
                LastObservedFriendlyStrength = Math.Max(0f, CurrentFriendly(input, selected))
            };
            return FinishWithPackage(input, output);
        }

        private static OperationalProbeOutput FinishWithPackage(OperationalProbeInput input, OperationalProbeOutput output)
        {
            if (output == null) return null;
            if (output.Decision == OperationalProbeDecision.Probe ||
                output.Decision == OperationalProbeDecision.Escalate)
            {
                output.Package = output.Package ?? BuildPackage(
                    input,
                    output,
                    output.Decision == OperationalProbeDecision.Escalate
                        ? CoordinatedOperationIntent.Attack
                        : CoordinatedOperationIntent.Probe);
            }
            return output;
        }

        private static CoordinatedOperationOutput BuildPackage(
            OperationalProbeInput input,
            OperationalProbeOutput probe,
            CoordinatedOperationIntent intent)
        {
            var lead = input.FormationDirectives?.GetAssignment(probe.SelectedUnitKey);
            float enemy = Math.Max(0f, CurrentEnemy(input, lead));
            var packageInput = new CoordinatedOperationInput
            {
                AllianceId = input.AllianceId,
                IsPlayerCic = false,
                Intent = intent,
                TargetName = input.PlanTargetAreaKey,
                TargetAreaKey = input.PlanTargetAreaKey,
                TargetSectorKey = probe.SourceSectorKey,
                TargetX = lead?.X ?? 0f,
                TargetZ = lead?.Z ?? 0f,
                TargetEnemyStrength = enemy,
                PreferredLeadStableUnitId = StableIdFor(input.FormationDirectives, probe.SelectedUnitKey),
                Options = input.PackageOptions ?? CoordinatedOperationOptions.StableDefaults(enemy)
            };

            if (input.FormationDirectives?.Assignments != null)
            {
                foreach (var assignment in input.FormationDirectives.Assignments)
                    packageInput.Candidates.Add(CandidateFromAssignment(assignment));
            }

            return CoordinatedOperationPackageLedger.Build(packageInput);
        }

        private static int StableIdFor(FormationDirectiveLedger ledger, string unitKey)
        {
            return ledger?.GetAssignment(unitKey)?.StableUnitId ?? 0;
        }

        private static CoordinatedOperationCandidate CandidateFromAssignment(FormationDirectiveAssignment assignment)
        {
            return new CoordinatedOperationCandidate
            {
                StableUnitId = assignment.StableUnitId,
                DisplayUnitKey = assignment.UnitKey,
                AllianceId = assignment.AllianceId,
                Level = assignment.Level,
                Directive = assignment.Directive,
                AreaKey = assignment.AreaKey,
                SectorKey = assignment.SectorKey,
                X = assignment.X,
                Z = assignment.Z,
                CombatAvailability = assignment.CombatAvailability,
                ExchangePressure = assignment.ExchangePressure,
                LocalFriendlySupport = assignment.LocalFriendlySupport,
                LocalEnemyStrength = assignment.LocalEnemyStrength,
                Readiness = assignment.Readiness,
                Morale = assignment.Morale,
                Ammo = assignment.Ammo,
                Supply = assignment.Supply,
                Fatigue = assignment.Fatigue,
                OffensiveAllowed = assignment.OffensiveAllowed ||
                    assignment.Directive == FormationDirective.Probe ||
                    assignment.Directive == FormationDirective.Counterstroke,
                DefensiveAllowed = assignment.DefensiveAllowed,
                TransferDonorAllowed = assignment.TransferDonorAllowed,
                DirectMovementAllowed = assignment.DirectMovementAllowed,
                InheritsFromParent = assignment.InheritsFromParent,
                CriticalSector = false,
                FrontPosture = FrontPosture.Counterstroke,
                CommitMode = CoordinatedCommitMode.DirectMovement
            };
        }

        private static OperationalProbeOutput EvaluateExistingProbe(
            OperationalProbeInput input,
            OperationalProbeOptions options,
            OperationalProbeOutput output)
        {
            var prior = input.Previous;
            float enemy = Math.Max(0f, input.CurrentEnemyStrength);
            float friendly = Math.Max(0f, input.CurrentFriendlyStrength);
            float previousEnemy = Math.Max(1f, prior.LastObservedEnemyStrength);
            float ratio = friendly / Math.Max(1f, enemy);
            int age = Math.Max(0, input.DaySerial - prior.StartedDaySerial);

            output.ProbeId = prior.ProbeId;
            output.SelectedUnitKey = prior.UnitKey;
            output.TargetAreaKey = prior.TargetAreaKey;
            output.SourceSectorKey = prior.SourceSectorKey;
            output.State = new OperationalProbeState
            {
                ProbeId = prior.ProbeId,
                UnitKey = prior.UnitKey,
                TargetAreaKey = prior.TargetAreaKey,
                SourceSectorKey = prior.SourceSectorKey,
                StartedDaySerial = prior.StartedDaySerial,
                LastObservedEnemyStrength = enemy,
                LastObservedFriendlyStrength = friendly
            };

            if (ratio <= options.WithdrawFriendlyRatio)
            {
                output.Decision = OperationalProbeDecision.Withdraw;
                output.Reason = "probe-overmatched";
                return output;
            }

            if (enemy >= previousEnemy * options.EnemyReactionMultiplier && ratio < options.EscalateFriendlyRatio)
            {
                output.Decision = OperationalProbeDecision.Pause;
                output.Reason = "enemy-reaction";
                return output;
            }

            if (age >= options.MinimumProbeDays && ratio >= options.EscalateFriendlyRatio &&
                input.ContactEvidence != ContactEvidence.NoContact &&
                input.ContactEvidence != ContactEvidence.OvermatchedContact)
            {
                output.Decision = OperationalProbeDecision.Escalate;
                output.Reason = "favorable-contact";
                output.RequiresMassCommitment = true;
                return output;
            }

            if (age >= options.MinimumProbeDays && IsFavorablePackageContact(input.ContactEvidence))
            {
                var package = BuildPackage(input, output, CoordinatedOperationIntent.Attack);
                if (package.Decision == CoordinatedOperationDecision.CoordinateAttack)
                {
                    output.Decision = OperationalProbeDecision.Escalate;
                    output.Reason = "favorable-package-contact";
                    output.RequiresMassCommitment = true;
                    output.Package = package;
                    return output;
                }
            }

            output.Decision = OperationalProbeDecision.Probe;
            output.Reason = "continue-probe";
            return output;
        }

        internal static float ResolvePackageDesiredStrength(OperationalProbeInput input)
        {
            if (input == null) return 1f;
            if (input.CurrentEnemyStrength >= 0f) return input.CurrentEnemyStrength;

            FormationDirectiveAssignment assignment = null;
            if (input.FormationDirectives != null)
            {
                if (input.Previous != null && !string.IsNullOrEmpty(input.Previous.UnitKey))
                    assignment = input.FormationDirectives.GetAssignment(input.Previous.UnitKey);
                if (assignment == null && input.Previous != null && !string.IsNullOrEmpty(input.Previous.SourceSectorKey))
                {
                    foreach (var candidate in input.FormationDirectives.Assignments)
                    {
                        if (candidate == null) continue;
                        if (string.Equals(candidate.SectorKey, input.Previous.SourceSectorKey, StringComparison.OrdinalIgnoreCase))
                        {
                            assignment = candidate;
                            break;
                        }
                    }
                }
            }

            return assignment != null && assignment.LocalEnemyStrength >= 0f
                ? assignment.LocalEnemyStrength
                : 1f;
        }

        private static bool IsFavorablePackageContact(ContactEvidence evidence)
        {
            return evidence == ContactEvidence.FavorableContact;
        }

        private static bool EligibleProbeFormation(
            FormationDirectiveAssignment assignment,
            OperationalProbeInput input,
            OperationalProbeOptions options)
        {
            if (assignment == null) return false;
            if (assignment.InheritsFromParent) return false;
            if (!assignment.DirectMovementAllowed) return false;
            if (assignment.Directive == FormationDirective.Guard ||
                assignment.Directive == FormationDirective.Hold ||
                assignment.Directive == FormationDirective.Recover ||
                assignment.Directive == FormationDirective.Concede)
                return false;
            if (assignment.Readiness < options.MinimumProbeReadiness) return false;
            if (assignment.Supply < options.MinimumProbeSupply) return false;
            if (!string.Equals(assignment.AreaKey, input.PlanTargetAreaKey, StringComparison.OrdinalIgnoreCase))
                return false;

            var source = input.Fronts?.GetSector(assignment.SectorKey);
            if (source != null)
            {
                if (source.Posture == FrontPosture.Hold) return false;
                if (source.IsCritical && source.StrengthRatio < source.MinimumHoldRatio) return false;
                float maxProbeStrength = Math.Max(1000f, source.OwnStrength * options.MaximumProbeStrengthFraction);
                if (assignment.CombatAvailability > maxProbeStrength) return false;
            }

            return true;
        }

        private static float CurrentEnemy(OperationalProbeInput input, FormationDirectiveAssignment selected)
        {
            if (input.CurrentEnemyStrength >= 0f) return input.CurrentEnemyStrength;
            return selected?.LocalEnemyStrength ?? 0f;
        }

        private static float CurrentFriendly(OperationalProbeInput input, FormationDirectiveAssignment selected)
        {
            if (input.CurrentFriendlyStrength >= 0f) return input.CurrentFriendlyStrength;
            return selected?.CombatAvailability ?? 0f;
        }

        private static string BuildProbeId(int allianceId, string targetAreaKey, string unitKey)
        {
            return allianceId.ToString() + ":" + (targetAreaKey ?? "-") + ":" + (unitKey ?? "-");
        }

        private static OperationalProbeOutput NoOp(OperationalProbeOutput output, string reason)
        {
            output.Decision = OperationalProbeDecision.None;
            output.Reason = reason;
            return output;
        }
    }
}
