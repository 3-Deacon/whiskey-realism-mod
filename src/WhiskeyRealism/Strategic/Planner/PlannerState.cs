using System.Collections.Generic;
using System.Text;

namespace WhiskeyRealism.Strategic.Planner
{
    // One alliance's main-effort memory across reviews (plan P4/P5): the current main effort, the
    // challenger the hysteresis is counting, the decision serial and the campaign seed. Written
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

        public void Restore(int mainSector, int mainObjective, int challengerObjective, int challengerDays, int decisionSerial, long campaignSeed)
        {
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
    // StrategicCoordinator.RecordBattleOutcome forwards every new record through
    // CampaignPlannerRuntime.OnBattleRecorded; a battle concerns both sides, so it is queued for
    // alliances 0 and 1 and each review takes only its own queue.
    public sealed class BattleInbox
    {
        public const int MaxPending = 16;
        private readonly List<BattleHistoryRecord>[] _pending = { new List<BattleHistoryRecord>(), new List<BattleHistoryRecord>() };

        internal void Add(BattleHistoryRecord record)
        {
            if (record == null) return;
            for (int a = 0; a < _pending.Length; a++)
            {
                var list = _pending[a];
                list.Add(record);
                if (list.Count > MaxPending) list.RemoveAt(0);
            }
        }

        // The alliance's pending battles, moved into `into` (cleared first); the queue empties.
        internal void Take(int alliance, List<BattleHistoryRecord> into)
        {
            into.Clear();
            if (alliance < 0 || alliance >= _pending.Length) return;
            into.AddRange(_pending[alliance]);
            _pending[alliance].Clear();
        }

        internal void Clear()
        {
            for (int a = 0; a < _pending.Length; a++) _pending[a].Clear();
        }
    }
}
