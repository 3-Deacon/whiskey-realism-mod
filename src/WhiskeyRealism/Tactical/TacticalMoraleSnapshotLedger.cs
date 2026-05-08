using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    public sealed class TacticalMoraleSnapshotLedger
    {
        public readonly struct Key
        {
            public readonly int InstanceId;
            public readonly string UnitName;
            public Key(int unitInstanceId, string unitName) { InstanceId = unitInstanceId; UnitName = unitName; }
        }

        private struct Sample { public float Morale; public float TimeFromStart; public float VanillaLastUpdate; }

        private readonly int capacity;
        private readonly Dictionary<int, List<Sample>> byInstanceId = new Dictionary<int, List<Sample>>();
        private readonly Dictionary<string, List<Sample>> byName = new Dictionary<string, List<Sample>>();

        public TacticalMoraleSnapshotLedger(int capacity) { this.capacity = capacity; }

        public void RecordSample(Key key, float morale, float timeFromStart)
        {
            RecordInternal(key, morale, timeFromStart, vanillaLastUpdate: timeFromStart);
        }

        public bool RecordSampleIfNew(Key key, float morale, float timeFromStart, float vanillaLastMoraleUpdate)
        {
            if (byInstanceId.TryGetValue(key.InstanceId, out var existing) &&
                existing.Count > 0 &&
                existing[existing.Count - 1].VanillaLastUpdate == vanillaLastMoraleUpdate)
            {
                return false;
            }
            RecordInternal(key, morale, timeFromStart, vanillaLastMoraleUpdate);
            return true;
        }

        private void RecordInternal(Key key, float morale, float timeFromStart, float vanillaLastUpdate)
        {
            if (!byInstanceId.TryGetValue(key.InstanceId, out var listById))
            {
                listById = new List<Sample>(capacity);
                byInstanceId[key.InstanceId] = listById;
            }
            listById.Add(new Sample { Morale = morale, TimeFromStart = timeFromStart, VanillaLastUpdate = vanillaLastUpdate });
            if (listById.Count > capacity) listById.RemoveAt(0);

            byName[key.UnitName] = listById;
        }

        public bool TryGetLatest(Key key, out float morale, out float timeFromStart)
        {
            if (byInstanceId.TryGetValue(key.InstanceId, out var listById) && listById.Count > 0)
            {
                var s = listById[listById.Count - 1];
                morale = s.Morale; timeFromStart = s.TimeFromStart; return true;
            }
            if (key.UnitName != null && byName.TryGetValue(key.UnitName, out var listByName) && listByName.Count > 0)
            {
                var s = listByName[listByName.Count - 1];
                morale = s.Morale; timeFromStart = s.TimeFromStart; return true;
            }
            morale = 0f; timeFromStart = 0f; return false;
        }

        public bool TryGetOldestRetained(Key key, out float morale, out float timeFromStart)
        {
            if (byInstanceId.TryGetValue(key.InstanceId, out var listById) && listById.Count > 0)
            {
                var s = listById[0];
                morale = s.Morale; timeFromStart = s.TimeFromStart; return true;
            }
            morale = 0f; timeFromStart = 0f; return false;
        }

        public int SampleCount(Key key)
            => byInstanceId.TryGetValue(key.InstanceId, out var listById) ? listById.Count : 0;

        public void PruneRouted(Key key)
        {
            byInstanceId.Remove(key.InstanceId);
            if (key.UnitName != null) byName.Remove(key.UnitName);
        }
    }
}
