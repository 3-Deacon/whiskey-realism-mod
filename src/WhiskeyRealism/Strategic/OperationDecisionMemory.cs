using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class OperationDecisionMemory
    {
        private readonly int[] _recentReplanDaySerials = new int[16];
        private int _count;

        public void RecordReplan(int daySerial)
        {
            if (Contains(daySerial)) return;

            if (_count < _recentReplanDaySerials.Length)
            {
                _recentReplanDaySerials[_count++] = daySerial;
                return;
            }

            int oldestIndex = 0;
            for (int i = 1; i < _recentReplanDaySerials.Length; i++)
            {
                if (_recentReplanDaySerials[i] < _recentReplanDaySerials[oldestIndex])
                    oldestIndex = i;
            }
            _recentReplanDaySerials[oldestIndex] = daySerial;
        }

        public int CountRecentReplans(int daySerial, int windowDays)
        {
            int window = Math.Max(0, windowDays);
            int write = 0;
            int count = 0;

            for (int i = 0; i < _count; i++)
            {
                int recordedDay = _recentReplanDaySerials[i];
                int age = daySerial - recordedDay;
                if (age > window)
                    continue;

                _recentReplanDaySerials[write++] = recordedDay;
                if (age >= 0)
                    count++;
            }

            for (int i = write; i < _count; i++)
                _recentReplanDaySerials[i] = 0;

            _count = write;
            return count;
        }

        public int[] SnapshotRecentReplans()
        {
            var snapshot = new int[_count];
            Array.Copy(_recentReplanDaySerials, snapshot, _count);
            Array.Sort(snapshot);
            return snapshot;
        }

        public void RestoreRecentReplans(int[] daySerials)
        {
            Array.Clear(_recentReplanDaySerials, 0, _recentReplanDaySerials.Length);
            _count = 0;
            if (daySerials == null || daySerials.Length == 0) return;

            var unique = new SortedSet<int>();
            for (int i = 0; i < daySerials.Length; i++)
                unique.Add(daySerials[i]);

            int skip = Math.Max(0, unique.Count - _recentReplanDaySerials.Length);
            int index = 0;
            foreach (int day in unique)
            {
                if (index++ < skip) continue;
                _recentReplanDaySerials[_count++] = day;
            }
        }

        private bool Contains(int daySerial)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_recentReplanDaySerials[i] == daySerial)
                    return true;
            }
            return false;
        }
    }
}
