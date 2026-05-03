using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class ArmyAreaLedger
    {
        private readonly Dictionary<string, ArmyAreaAssignment> _assignments =
            new Dictionary<string, ArmyAreaAssignment>();
        private readonly List<ArmyAreaAssignment> _ordered = new List<ArmyAreaAssignment>();

        public IReadOnlyList<ArmyAreaAssignment> Assignments => _ordered;

        public static ArmyAreaLedger Build(IEnumerable<ArmyAreaInput> inputs, string planTargetAreaKey = null)
        {
            var ledger = new ArmyAreaLedger();
            if (inputs == null) return ledger;

            foreach (var input in inputs)
            {
                if (input == null || string.IsNullOrEmpty(input.UnitKey)) continue;

                var doctrine = HistoricalArmyAreaRegistry.Resolve(input.AllianceId, input.UnitName, input.CommanderName);
                bool inPreferredArea = Contains(doctrine.PreferredAreaKeys, input.CurrentAreaKey);
                bool planTargetsThisArea = !string.IsNullOrEmpty(planTargetAreaKey) &&
                                           Contains(doctrine.PreferredAreaKeys, planTargetAreaKey);

                var assignment = new ArmyAreaAssignment
                {
                    UnitKey = input.UnitKey,
                    UnitName = input.UnitName,
                    CommanderName = input.CommanderName,
                    Doctrine = doctrine,
                    CurrentAreaKey = input.CurrentAreaKey,
                    AssignedAreaKey = inPreferredArea || doctrine.PrimaryAreaKey == "Unassigned"
                        ? input.CurrentAreaKey
                        : doctrine.PrimaryAreaKey,
                    OutOfArea = !inPreferredArea && doctrine.PrimaryAreaKey != "Unassigned"
                };

                if (assignment.OutOfArea)
                {
                    assignment.Behavior = ArmyAreaBehavior.Recover;
                    assignment.Reason = "outside-historical-area";
                }
                else if (planTargetsThisArea && input.Readiness >= 0.65f && input.Strength >= 15000f && doctrine.OffensiveBias > doctrine.DefensiveBias)
                {
                    assignment.Behavior = ArmyAreaBehavior.Exploit;
                    assignment.Reason = "plan-target-historical-area";
                }
                else if (input.Readiness < 0.45f)
                {
                    assignment.Behavior = ArmyAreaBehavior.Recover;
                    assignment.Reason = "low-readiness";
                }
                else
                {
                    assignment.Behavior = ArmyAreaBehavior.Hold;
                    assignment.Reason = "historical-area";
                }

                ledger._assignments[assignment.UnitKey] = assignment;
                ledger._ordered.Add(assignment);
            }

            return ledger;
        }

        public ArmyAreaAssignment GetAssignment(string unitKey)
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
            {
                parts.Add($"{assignment.UnitName}:{assignment.AssignedAreaKey}:{assignment.Behavior}");
            }
            return string.Join(",", parts);
        }

        private static bool Contains(List<string> values, string value)
        {
            if (values == null || value == null) return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
