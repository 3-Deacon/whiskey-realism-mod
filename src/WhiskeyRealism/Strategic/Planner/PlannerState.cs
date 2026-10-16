using System;
using System.Collections.Generic;
using System.Text;

namespace WhiskeyRealism.Strategic.Planner
{
    // One alliance's main-effort memory across reviews (plan P4/P5): the current main effort, the
    // challenger the hysteresis is counting, the reviews the current main effort has spent just
    // below its required odds (the odds deadband), the capital-threat latch (the trigger fires on
    // onset only), the decision serial and the campaign seed. Written
    // only by TheaterPlanner.Plan inside the daily review; persisted by the sidecar (G6, through
    // Restore). CampaignSeed 0 means "not computed yet": the first review computes it.
    public sealed class PlannerState
    {
        public int CurrentMainSector = -1;
        public int CurrentMainObjective = -1;
        public int ChallengerObjective = -1;
        public int ChallengerDays;
        public int DecisionSerial;
        public long CampaignSeed;
        public int BelowOddsDays;
        public bool CapitalThreatLatched;
        // G5a (chapters decision 5): the DecisionSerial whose main-effort launch the chapter tempo roll
        // released, -1 for none. `TempoReleasedDecision == DecisionSerial` is the one release predicate
        // (the mover sets it on a passed, forced or debug roll and before the first MainEffort dispatch
        // of a decision that needs no roll); DecisionSerial starts at 0 and is 1 at the first decision, so
        // -1 never reads as released. Persisted (PlannerStateDto.tempoReleasedDecision, G6).
        public int TempoReleasedDecision = -1;
        // The day serial of the previous review that ran the executors (PlannerReviewMode.Full with
        // PlannerChapterRules.ExecutorsAllowed): the tempo clock's reference (CampaignTempoGate.TimeDiffYears).
        // -1 none. Persisted (PlannerStateDto.lastFullReviewDay, G6).
        public int LastFullReviewDay = -1;
        // The decision whose first [CampaignTempo] row was emitted (bounds the rows to two per decision).
        // Telemetry only, not persisted: after a reload a decision may log its first row once more.
        public int TempoRowDecision = -1;

        public void Restore(int mainSector, int mainObjective, int challengerObjective, int challengerDays, int decisionSerial, long campaignSeed,
                            int belowOddsDays = 0, bool capitalThreatLatched = false, int tempoReleasedDecision = -1, int lastFullReviewDay = -1)
        {
            BelowOddsDays = belowOddsDays;
            CapitalThreatLatched = capitalThreatLatched;
            TempoReleasedDecision = tempoReleasedDecision;
            LastFullReviewDay = lastFullReviewDay;
            TempoRowDecision = -1;
            CurrentMainSector = mainSector;
            CurrentMainObjective = mainObjective;
            ChallengerObjective = challengerObjective;
            ChallengerDays = challengerDays;
            DecisionSerial = decisionSerial;
            CampaignSeed = campaignSeed;
        }

        // P5: 64-bit FNV-1a over the UTF-8 bytes of "<scenario>|yyyy-mm-dd" (the scenario is
        // SceneManagement.currentcampaignscenario, 36382/36576; the date is the first review's).
        // Never string.GetHashCode, which differs between runtimes.
        public static long ComputeCampaignSeed(string scenario, int year, int month, int day)
        {
            string key = (scenario ?? "") + "|" + year.ToString("D4") + "-" + month.ToString("D2") + "-" + day.ToString("D2");
            return unchecked((long)Fnv1a64(key));
        }

        internal static ulong Fnv1a64(string text)
        {
            ulong hash = 14695981039346656037UL;
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }

    // Finished battles waiting for each alliance's next plan (P4's decisive-battle trigger).
    // StrategicCoordinator.RecordBattleOutcome forwards every new record (it already drops one with
    // no winner) through CampaignPlannerRuntime.OnBattleRecorded. As the R4 rule it replaces
    // (DirtyPlanIfBattleInvalidates for AllianceWon and LosingAlliance), a battle concerns the
    // alliances that fought it: it is queued for each participant of 0 and 1 (Alliance[], the
    // winner and the loser) and each review takes only its own queue. A queue is not drained while
    // the planner pauses (W&L hierarchy -1) or stands down, so Take drops every battle older than
    // MaxBattleAgeDays instead of judging it against today's main effort.
    public sealed class BattleInbox
    {
        public const int MaxPending = 16;
        public const int MaxBattleAgeDays = 7;
        private readonly List<BattleHistoryRecord>[] _pending = { new List<BattleHistoryRecord>(), new List<BattleHistoryRecord>() };

        internal void Add(BattleHistoryRecord record)
        {
            if (record == null) return;
            for (int a = 0; a < _pending.Length; a++)
            {
                if (!Fought(record, a)) continue;
                var list = _pending[a];
                list.Add(record);
                if (list.Count > MaxPending) list.RemoveAt(0);
            }
        }

        private static bool Fought(BattleHistoryRecord record, int alliance)
        {
            if (record.AllianceWon == alliance || record.LosingAlliance == alliance) return true;
            var sides = record.Alliance;
            return sides != null && Array.IndexOf(sides, alliance) >= 0;
        }

        // The alliance's pending battles no older than MaxBattleAgeDays at daySerial
        // (ReviewCadenceCatchUp.DaySerial), moved into `into` (cleared first); the queue empties.
        internal void Take(int alliance, int daySerial, List<BattleHistoryRecord> into)
        {
            into.Clear();
            if (alliance < 0 || alliance >= _pending.Length) return;
            var list = _pending[alliance];
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (daySerial - ReviewCadenceCatchUp.DaySerial(r.Day, r.Month, r.Year) <= MaxBattleAgeDays) into.Add(r);
            }
            list.Clear();
        }

        internal void Clear()
        {
            for (int a = 0; a < _pending.Length; a++) _pending[a].Clear();
        }
    }
}
