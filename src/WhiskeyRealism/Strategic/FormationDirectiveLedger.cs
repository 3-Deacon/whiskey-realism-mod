using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class FormationDirectiveAssignment
    {
        public string UnitKey;
        public int AllianceId;
        public FormationLevel Level;
        public string AreaKey;
        public string SectorKey;
        public FormationDirective Directive;
        public string Reason;
        public float CombatAvailability;
        public float ExchangePressure;
        public float LocalFriendlySupport;
        public float LocalEnemyStrength;
        public float Readiness;
        public float Morale;
        public float Ammo;
        public float Supply;
        public float Fatigue;
        public float WeaponFirepower;
        public bool OffensiveAllowed;
        public bool DefensiveAllowed;
        public bool TransferDonorAllowed;
        public bool DirectMovementAllowed;
        public bool RaidAllowed;
        public bool InheritsFromParent;
        public string ParentUnitKey;
    }

    public sealed class FormationPressureSummary
    {
        public int LowSupplyCount;
        public int LowAmmoCount;
        public int RecoverCount;
        public int GuardCount;
        public int MassCount;
        public string TopSupplyAreaKey;
    }

    public sealed class FormationDirectiveLedger
    {
        private readonly Dictionary<string, FormationDirectiveAssignment> _assignments =
            new Dictionary<string, FormationDirectiveAssignment>();
        private readonly List<FormationDirectiveAssignment> _ordered =
            new List<FormationDirectiveAssignment>();

        public IReadOnlyList<FormationDirectiveAssignment> Assignments => _ordered;
        public FormationPressureSummary Pressure = new FormationPressureSummary();

        public static FormationDirectiveLedger Build(IEnumerable<FormationSnapshot> snapshots, EraStage era, string planTargetAreaKey)
        {
            var ledger = new FormationDirectiveLedger();
            if (snapshots == null) return ledger;

            var pendingAttached = new List<FormationSnapshot>();
            foreach (var snapshot in snapshots)
            {
                if (snapshot == null || string.IsNullOrEmpty(snapshot.UnitKey)) continue;
                if (snapshot.IsAttachedDivision)
                {
                    pendingAttached.Add(snapshot);
                    continue;
                }
                if (!snapshot.CanReceiveDirectDirective) continue;
                if (!snapshot.IsTopStrategicFormation) continue;

                var assignment = ResolveTopDirective(snapshot, era, planTargetAreaKey);
                ledger.Add(assignment);
            }

            for (int i = 0; i < pendingAttached.Count; i++)
            {
                var child = pendingAttached[i];
                FormationDirectiveAssignment parent = null;
                if (!string.IsNullOrEmpty(child.ParentUnitKey))
                    parent = ledger.GetAssignment(child.ParentUnitKey);

                var assignment = BaseAssignment(child);
                assignment.InheritsFromParent = true;
                assignment.ParentUnitKey = child.ParentUnitKey;
                assignment.DirectMovementAllowed = false;
                assignment.Directive = parent != null ? parent.Directive : FormationDirective.Hold;
                assignment.Reason = parent != null ? "attached-inherits-parent" : "attached-parent-missing";
                assignment.OffensiveAllowed = false;
                assignment.DefensiveAllowed = parent == null || parent.DefensiveAllowed;
                assignment.TransferDonorAllowed = false;
                ledger.Add(assignment);
            }

            ledger.RecomputePressure();
            return ledger;
        }

        public FormationDirectiveAssignment GetAssignment(string unitKey)
        {
            if (unitKey == null) return null;
            _assignments.TryGetValue(unitKey, out var assignment);
            return assignment;
        }

        public string Summary()
        {
            if (_ordered.Count == 0) return "<none>";
            var parts = new List<string>();
            foreach (var assignment in _ordered)
                parts.Add($"{assignment.UnitKey}:{assignment.Directive}:{assignment.Reason}");
            return string.Join(",", parts);
        }

        public bool ApplyOperationalProbe(OperationalProbeOutput probe)
        {
            if (probe == null || string.IsNullOrEmpty(probe.SelectedUnitKey)) return false;
            var assignment = GetAssignment(probe.SelectedUnitKey);
            if (assignment == null) return false;

            switch (probe.Decision)
            {
                case OperationalProbeDecision.Probe:
                    assignment.Directive = FormationDirective.Probe;
                    assignment.Reason = probe.Reason ?? "operational-probe";
                    assignment.OffensiveAllowed = false;
                    assignment.DefensiveAllowed = true;
                    assignment.TransferDonorAllowed = false;
                    break;

                case OperationalProbeDecision.Pause:
                    assignment.Directive = FormationDirective.Delay;
                    assignment.Reason = probe.Reason ?? "probe-pause";
                    assignment.OffensiveAllowed = false;
                    assignment.DefensiveAllowed = true;
                    assignment.TransferDonorAllowed = false;
                    break;

                case OperationalProbeDecision.Withdraw:
                    assignment.Directive = FormationDirective.Recover;
                    assignment.Reason = probe.Reason ?? "probe-withdraw";
                    assignment.OffensiveAllowed = false;
                    assignment.DefensiveAllowed = false;
                    assignment.TransferDonorAllowed = false;
                    break;

                case OperationalProbeDecision.Escalate:
                    assignment.Directive = assignment.Level == FormationLevel.Army
                        ? FormationDirective.Mass
                        : FormationDirective.Counterstroke;
                    assignment.Reason = probe.Reason ?? "probe-escalate";
                    assignment.OffensiveAllowed = true;
                    assignment.DefensiveAllowed = true;
                    assignment.TransferDonorAllowed = false;
                    break;

                default:
                    return false;
            }

            RecomputePressure();
            return true;
        }

        private void Add(FormationDirectiveAssignment assignment)
        {
            _assignments[assignment.UnitKey] = assignment;
            _ordered.Add(assignment);
        }

        private static FormationDirectiveAssignment ResolveTopDirective(FormationSnapshot snapshot, EraStage era, string planTargetAreaKey)
        {
            var assignment = BaseAssignment(snapshot);

            if (snapshot.Morale < 0.35f || snapshot.Readiness < 0.35f)
                return Set(assignment, FormationDirective.Recover, "low-morale-readiness", false, false, false);

            if (snapshot.MinimumAmmo < 0.25f || snapshot.Supply < 0.25f)
                return Set(assignment, FormationDirective.Recover, "low-ammo-supply", false, false, false);

            if (snapshot.OnRetreat && snapshot.HasActivePath)
                return Set(assignment, FormationDirective.Delay, "retreat-skirmish-risk", false, true, false);

            if (snapshot.IsCavalryCapable && snapshot.Readiness >= 0.65f && snapshot.Supply >= 0.65f &&
                snapshot.FrontPosture == FrontPosture.EconomyOfForce && snapshot.LocalEnemyStrength <= snapshot.GroupStrengthActive)
            {
                assignment.RaidAllowed = true;
                return Set(assignment, FormationDirective.RaidSupport, "cavalry-raid-support", false, false, true);
            }

            if (snapshot.Level == FormationLevel.Division &&
                snapshot.VisibleEnemyLevel == FormationLevel.Army &&
                !HasReachableSupport(snapshot))
                return Set(assignment, FormationDirective.Screen, "division-vs-army-no-support", false, true, false);

            if (snapshot.Level == FormationLevel.Division &&
                snapshot.VisibleEnemyLevel == FormationLevel.Corps &&
                !DivisionCanCounterstroke(snapshot))
                return Set(assignment, FormationDirective.Delay, "division-vs-corps-delay", false, true, false);

            if (snapshot.FrontPosture == FrontPosture.Concede)
                return Set(assignment, FormationDirective.Concede, "front-concede", false, false, true);

            if (snapshot.FrontPosture == FrontPosture.EconomyOfForce)
                return Set(assignment, FormationDirective.Reserve, "economy-of-force", false, true, true);

            if (snapshot.IsPlanTargetArea && snapshot.Level == FormationLevel.Army && snapshot.GrandArmyStructureAvailable)
                return Set(assignment, FormationDirective.Mass, "army-plan-target", true, true, false);

            if ((snapshot.FrontPosture == FrontPosture.Counterstroke || snapshot.FrontPosture == FrontPosture.Exploit) &&
                AttackRiskPasses(snapshot))
            {
                var directive = snapshot.FrontPosture == FrontPosture.Exploit ? FormationDirective.Mass : FormationDirective.Counterstroke;
                return Set(assignment, directive, "risk-gate-passed", true, true, false);
            }

            if (snapshot.IsCriticalSector)
                return Set(assignment, FormationDirective.Guard, "critical-sector", false, true, false);

            if (snapshot.Level == FormationLevel.Army)
                return Set(assignment, FormationDirective.Hold, "army-hold", false, true, false);

            return Set(assignment, FormationDirective.Screen, "default-screen", false, true, false);
        }

        private static FormationDirectiveAssignment BaseAssignment(FormationSnapshot snapshot)
        {
            float availability = CombatAvailability(snapshot);
            return new FormationDirectiveAssignment
            {
                UnitKey = snapshot.UnitKey,
                AllianceId = snapshot.AllianceId,
                Level = snapshot.Level,
                AreaKey = snapshot.AreaKey,
                SectorKey = snapshot.SectorKey,
                CombatAvailability = availability,
                ExchangePressure = ExchangePressure(snapshot),
                LocalFriendlySupport = snapshot.LocalFriendlySupportStrength,
                LocalEnemyStrength = snapshot.LocalEnemyStrength,
                Readiness = FormationSnapshot.Clamp01(snapshot.Readiness),
                Morale = FormationSnapshot.Clamp01(snapshot.Morale),
                Ammo = snapshot.MinimumAmmo,
                Supply = FormationSnapshot.Clamp01(snapshot.Supply),
                Fatigue = FormationSnapshot.Clamp01(snapshot.Fatigue),
                WeaponFirepower = Math.Max(0f, snapshot.WeaponFirepower),
                DirectMovementAllowed = snapshot.CanReceiveDirectMovement
            };
        }

        private static FormationDirectiveAssignment Set(FormationDirectiveAssignment assignment, FormationDirective directive, string reason, bool offensive, bool defensive, bool donor)
        {
            assignment.Directive = directive;
            assignment.Reason = reason;
            assignment.OffensiveAllowed = offensive;
            assignment.DefensiveAllowed = defensive;
            assignment.TransferDonorAllowed = donor;
            return assignment;
        }

        public static float CombatAvailability(FormationSnapshot snapshot)
        {
            return Math.Max(0f, snapshot.GroupStrengthActive) *
                   Gate(snapshot.Morale, 0.35f) *
                   Gate(snapshot.Readiness, 0.35f) *
                   Gate(snapshot.MinimumAmmo, 0.25f) *
                   Gate(snapshot.Supply, 0.25f) *
                   (1f - 0.5f * FormationSnapshot.Clamp01(snapshot.Fatigue));
        }

        public static float ExchangePressure(FormationSnapshot snapshot)
        {
            return Sqrt(Math.Max(1f, snapshot.GroupStrengthActive)) *
                   Sqrt(Math.Max(1f, snapshot.WeaponFirepower)) *
                   FormationSnapshot.Clamp01(snapshot.Morale) *
                   FormationSnapshot.Clamp01(snapshot.Readiness) *
                   FormationSnapshot.Clamp01(snapshot.MinimumAmmo) *
                   FormationSnapshot.Clamp01(snapshot.Supply) *
                   (1f - 0.5f * FormationSnapshot.Clamp01(snapshot.Fatigue));
        }

        private static bool HasReachableSupport(FormationSnapshot snapshot)
        {
            return snapshot.SupportCanReach && snapshot.LocalFriendlySupportStrength >= snapshot.GroupStrengthActive * 0.5f;
        }

        private static bool DivisionCanCounterstroke(FormationSnapshot snapshot)
        {
            if (!HasReachableSupport(snapshot)) return false;
            float own = snapshot.GroupStrengthActive + snapshot.LocalFriendlySupportStrength;
            return own >= snapshot.LocalEnemyStrength * 1.25f;
        }

        private static bool AttackRiskPasses(FormationSnapshot snapshot)
        {
            float own = snapshot.GroupStrengthActive + (snapshot.SupportCanReach ? snapshot.LocalFriendlySupportStrength : 0f);
            float ratio = own / Math.Max(1f, snapshot.LocalEnemyStrength);
            if (snapshot.Level == FormationLevel.Division) return ratio >= 1.5f;
            if (snapshot.Level == FormationLevel.Corps) return ratio >= 1.2f;
            return ratio >= 1.05f;
        }

        private void RecomputePressure()
        {
            Pressure = new FormationPressureSummary();
            var areaScores = new Dictionary<string, int>();
            foreach (var assignment in _ordered)
            {
                if (assignment.Supply < 0.35f)
                {
                    Pressure.LowSupplyCount++;
                    AddAreaPressure(areaScores, assignment.AreaKey);
                }
                if (assignment.Ammo < 0.35f)
                {
                    Pressure.LowAmmoCount++;
                    AddAreaPressure(areaScores, assignment.AreaKey);
                }
                if (assignment.Directive == FormationDirective.Recover) Pressure.RecoverCount++;
                if (assignment.Directive == FormationDirective.Guard) Pressure.GuardCount++;
                if (assignment.Directive == FormationDirective.Mass) Pressure.MassCount++;
            }

            int best = 0;
            foreach (var kv in areaScores)
            {
                if (kv.Value > best)
                {
                    best = kv.Value;
                    Pressure.TopSupplyAreaKey = kv.Key;
                }
            }
        }

        private static void AddAreaPressure(Dictionary<string, int> scores, string areaKey)
        {
            areaKey = string.IsNullOrEmpty(areaKey) ? "Unknown" : areaKey;
            scores.TryGetValue(areaKey, out var count);
            scores[areaKey] = count + 1;
        }

        private static float Gate(float value, float floor)
        {
            value = FormationSnapshot.Clamp01(value);
            if (value < floor) return 0f;
            return value;
        }

        private static float Sqrt(float value)
        {
            return (float)Math.Sqrt(value);
        }
    }
}
