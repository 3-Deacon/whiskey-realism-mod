using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Construction
{
    public sealed class ConstructionTelemetry
    {
        private readonly List<ConstructionStartEvent> _recentStarts = new List<ConstructionStartEvent>();
        private readonly int[] _pendingBuildingStarts = new int[2];
        private readonly int[] _pendingRailStarts = new int[2];
        private readonly string[] _pendingLast = new string[2] { "<none>", "<none>" };

        public void Record(ConstructionStartEvent start)
        {
            _recentStarts.Add(start);
            while (_recentStarts.Count > 64)
                _recentStarts.RemoveAt(0);

            if (start.AllianceId < 0 || start.AllianceId >= 2) return;

            if (start.Kind == ConstructionCandidateKind.Railroad)
                _pendingRailStarts[start.AllianceId]++;
            else
                _pendingBuildingStarts[start.AllianceId]++;

            _pendingLast[start.AllianceId] = LastLabel(start);
        }

        // Monthly telemetry calls Summary once per alliance. Return starts since
        // the previous call for that alliance, then drain that scoped window.
        public string Summary(int alliance)
        {
            if (alliance < 0 || alliance >= 2)
                return FormatSummary(0, 0, "<none>");

            int buildings = _pendingBuildingStarts[alliance];
            int rail = _pendingRailStarts[alliance];
            string last = _pendingLast[alliance] ?? "<none>";

            _pendingBuildingStarts[alliance] = 0;
            _pendingRailStarts[alliance] = 0;
            _pendingLast[alliance] = "<none>";

            return FormatSummary(buildings, rail, last);
        }

        public string RecentSummary(int alliance)
        {
            int buildings = 0;
            int rail = 0;
            string last = "<none>";

            for (int i = 0; i < _recentStarts.Count; i++)
            {
                if (_recentStarts[i].AllianceId != alliance) continue;

                if (_recentStarts[i].Kind == ConstructionCandidateKind.Railroad)
                    rail++;
                else
                    buildings++;

                last = _recentStarts[i].Kind + ":" + (_recentStarts[i].Name ?? "<unnamed>");
            }

            return FormatSummary(buildings, rail, last);
        }

        private static string LastLabel(ConstructionStartEvent start)
        {
            return start.Kind + ":" + (start.Name ?? "<unnamed>");
        }

        private static string FormatSummary(int buildings, int rail, string last)
        {
            return "starts_building=" + buildings + " starts_rail=" + rail + " last=" + (last ?? "<none>");
        }
    }
}
