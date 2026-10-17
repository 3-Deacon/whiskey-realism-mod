using System;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14O: the objective table and the pressure of the victory clock, published whole with the candidates (double-buffered like them).
    public sealed partial class FeasibilityLedger
    {
        private ObjectiveEstimate[] _objectivesFront = new ObjectiveEstimate[ManeuverFeasibility.MaxObjectives];
        private ObjectiveEstimate[] _objectivesBack = new ObjectiveEstimate[ManeuverFeasibility.MaxObjectives];
        private int _objectivesFrontCount, _objectivesBackCount;
        private ObjectivePressure _pressureFront, _pressureBack;

        /// <summary>The pressure of the victory clock at the published evaluation (all zero with the flag off or no clock).</summary>
        public ObjectivePressure Pressure => _pressureFront;

        /// <summary>Rows in the published table: each live objective the side does not hold, and each own-held one under threat.</summary>
        public int ObjectiveCount => _objectivesFrontCount;

        public bool TryObjective(int index, out ObjectiveEstimate estimate)
        {
            if (index < 0 || index >= _objectivesFrontCount)
            {
                estimate = default(ObjectiveEstimate);
                return false;
            }
            estimate = _objectivesFront[index];
            return true;
        }

        internal void SetObjectivesBack(ObjectiveEstimate[] table, int count, in ObjectivePressure pressure)
        {
            _objectivesBackCount = Math.Min(count, _objectivesBack.Length);
            Array.Copy(table, _objectivesBack, _objectivesBackCount);
            _pressureBack = pressure;
        }

        internal void ClearObjectivesBack()
        {
            _objectivesBackCount = 0;
            _pressureBack = default(ObjectivePressure);
        }

        private void PublishObjectives()
        {
            ObjectiveEstimate[] swap = _objectivesFront;
            _objectivesFront = _objectivesBack;
            _objectivesBack = swap;
            _objectivesFrontCount = _objectivesBackCount;
            _pressureFront = _pressureBack;
        }

        /// <summary>The child is a reserve-rect member of the selected plan's candidate in the evaluation under way (the objective table's main body leaves it out).</summary>
        internal bool BackIsReserveRect(string childId)
        {
            if (!HasSelection) return false;
            for (int r = 0; r < _back.ResultCount; r++)
            {
                if (_back.Results[r].Plan != SelectedPlan) continue;
                CandidateResult result = _back.Results[r];
                for (int m = 0; m < result.MemberCount; m++)
                {
                    MemberEstimate member = _back.Members[result.MemberStart + m];
                    if (member.Reserve && string.Equals(member.ChildId, childId, StringComparison.Ordinal)) return true;
                }
                return false;
            }
            return false;
        }
    }
}
