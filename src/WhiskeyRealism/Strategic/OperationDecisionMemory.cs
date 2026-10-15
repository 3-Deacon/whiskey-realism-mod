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

    // C-Probe hysteresis, kept by the coordinator per alliance (OperationalProbeLedger itself
    // is stateless). A probe only withdraws after WithdrawConfirmDays consecutive Withdraw
    // evaluations (the first is held as Pause), and the same target is not re-probed for
    // ReprobeCooldownDays after a withdrawal, so Probe <-> Withdraw cannot flap daily.
    public sealed class ProbeHysteresis
    {
        public const int WithdrawConfirmDays = 2;
        public const int ReprobeCooldownDays = 3;

        private int _withdrawStreak;
        private int _cooldownUntilDay = int.MinValue;
        private string _cooldownTargetKey;

        public OperationalProbeDecision Filter(
            OperationalProbeDecision proposed,
            bool continuingProbe,
            string targetAreaKey,
            int daySerial)
        {
            if (proposed == OperationalProbeDecision.Withdraw)
            {
                _withdrawStreak++;
                if (_withdrawStreak < WithdrawConfirmDays)
                    return OperationalProbeDecision.Pause;
                _withdrawStreak = 0;
                _cooldownUntilDay = daySerial + ReprobeCooldownDays;
                _cooldownTargetKey = targetAreaKey;
                return OperationalProbeDecision.Withdraw;
            }

            _withdrawStreak = 0;
            if (!continuingProbe &&
                proposed == OperationalProbeDecision.Probe &&
                daySerial < _cooldownUntilDay &&
                string.Equals(targetAreaKey, _cooldownTargetKey, StringComparison.Ordinal))
                return OperationalProbeDecision.None;
            return proposed;
        }

        // A probe state belongs to one target area; when the plan target moves the old probe
        // is not this operation's probe any more.
        internal ProbeHysteresisDto Snapshot()
        {
            return new ProbeHysteresisDto
            {
                WithdrawStreak = _withdrawStreak,
                CooldownUntilDay = _cooldownUntilDay,
                CooldownTargetKey = _cooldownTargetKey
            };
        }

        internal static ProbeHysteresis FromSnapshot(ProbeHysteresisDto dto)
        {
            var h = new ProbeHysteresis();
            if (dto == null) return h;
            h._withdrawStreak = Math.Max(0, dto.WithdrawStreak);
            h._cooldownUntilDay = dto.CooldownUntilDay;
            h._cooldownTargetKey = dto.CooldownTargetKey;
            return h;
        }

        public static bool KeepPreviousState(string previousTargetAreaKey, string currentTargetAreaKey)
        {
            return !string.IsNullOrEmpty(currentTargetAreaKey) &&
                string.Equals(previousTargetAreaKey, currentTargetAreaKey, StringComparison.Ordinal);
        }

        // Mirrors vanilla's "operation ended" stop (AICampaign defensive-operation upkeep,
        // decompile 13838-13843): StopRegiment only when the unit is not a W&L
        // under-commander unit in an active W&L scenario. Units in battle are never touched
        // (vanilla's offensive upkeep skips inbattle units, 14004).
        public static bool ShouldStopUnit(bool inOffensive, bool inBattle, bool wlUnderCommander, bool wlScenarioActive)
        {
            if (!inOffensive || inBattle) return false;
            return !wlUnderCommander || !wlScenarioActive;
        }
    }
}
