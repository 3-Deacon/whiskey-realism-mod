using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Construction
{
    public sealed class ConstructionTelemetry
    {
        private readonly List<ConstructionStartEvent> _recentStarts = new List<ConstructionStartEvent>();

        public void Record(ConstructionStartEvent start)
        {
            _recentStarts.Add(start);
            while (_recentStarts.Count > 64)
                _recentStarts.RemoveAt(0);
        }

        public string Summary(int alliance)
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

            return "starts_building=" + buildings + " starts_rail=" + rail + " last=" + last;
        }
    }
}
