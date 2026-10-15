using System;

namespace WhiskeyRealism.Strategic
{
    internal enum DailyReviewJobKind
    {
        Map,
        Cic,
        Fiscal,
        Policy,
        Front,
        Defense,
        Formation,
        Probe,
        ArmyArea,
        Construction,
        Report
    }

    internal readonly struct DailyReviewJob
    {
        internal readonly DailyReviewJobKind Kind;
        internal readonly int Alliance;

        internal DailyReviewJob(DailyReviewJobKind kind, int alliance)
        {
            Kind = kind;
            Alliance = alliance;
        }
    }

    // The daily strategic review as an explicit ordered job list: the campaign map, then for
    // alliance 0 and then alliance 1 the CIC review, fiscal, policy, front, defense,
    // formation directives, operational probe, army areas, construction and the report lines.
    // This is the order StrategicCoordinator ran them in one frame before the review was
    // time-sliced. Each job decides inside itself whether it has work today (the cadence and
    // source-signature gates read signatures that earlier jobs of the same day write).
    internal static class DailyReviewJobList
    {
        private static readonly DailyReviewJobKind[] PerAlliance =
        {
            DailyReviewJobKind.Cic,
            DailyReviewJobKind.Fiscal,
            DailyReviewJobKind.Policy,
            DailyReviewJobKind.Front,
            DailyReviewJobKind.Defense,
            DailyReviewJobKind.Formation,
            DailyReviewJobKind.Probe,
            DailyReviewJobKind.ArmyArea,
            DailyReviewJobKind.Construction,
            DailyReviewJobKind.Report
        };

        internal static readonly DailyReviewJob[] Ordered = Build();

        private static DailyReviewJob[] Build()
        {
            var jobs = new DailyReviewJob[1 + 2 * PerAlliance.Length];
            int n = 0;
            jobs[n++] = new DailyReviewJob(DailyReviewJobKind.Map, -1);
            for (int alliance = 0; alliance < 2; alliance++)
                for (int i = 0; i < PerAlliance.Length; i++)
                    jobs[n++] = new DailyReviewJob(PerAlliance[i], alliance);
            return jobs;
        }
    }

    // Runs one day's review jobs in order across frames. Each Pump runs at least one job (so
    // the day always finishes) and keeps going while the slice has used less than its budget,
    // so a frame costs at most the budget plus one job. Drain runs everything left in one
    // call. A job that throws ends the day: the remaining jobs are skipped, as the single-frame
    // review stopped at an exception, and the day still completes so the caller publishes.
    // Pump returns true on the call that completed the day. The caller owns days: it must
    // Drain (or Clear) an in-flight day before Begin starts the next one.
    internal sealed class DailyReviewScheduler
    {
        private int _jobCount;
        private int _next;
        private int _generation;

        internal bool InFlight { get; private set; }

        // True only while a job runs; the coordinator serves the working ledgers to readers
        // inside a job (the review itself, and anything vanilla runs from a job's calls).
        internal bool RunningJob { get; private set; }

        internal bool Faulted { get; private set; }

        internal int Slices { get; private set; }

        internal long MaxSliceTicks { get; private set; }

        internal void Begin(int jobCount)
        {
            if (InFlight)
                throw new InvalidOperationException("daily review still in flight; drain it before starting the next day");
            _generation++;
            _jobCount = Math.Max(0, jobCount);
            _next = 0;
            Faulted = false;
            Slices = 0;
            MaxSliceTicks = 0;
            InFlight = true;
        }

        internal bool Pump(long budgetTicks, Func<long> clock, Action<int> runJob, Action<int, Exception> onFault)
        {
            if (!InFlight || RunningJob || clock == null || runJob == null) return false;
            int generation = _generation;
            long start = clock();
            Slices++;
            while (_next < _jobCount)
            {
                int job = _next++;
                RunningJob = true;
                try
                {
                    runJob(job);
                }
                catch (Exception ex)
                {
                    RunningJob = false;
                    if (generation == _generation)
                    {
                        Faulted = true;
                        _next = _jobCount;
                    }
                    onFault?.Invoke(job, ex);
                }
                finally
                {
                    RunningJob = false;
                }

                // Cleared (campaign reset) from inside a job: this day is gone.
                if (generation != _generation) return false;
                if (clock() - start >= budgetTicks) break;
            }

            long slice = clock() - start;
            if (slice > MaxSliceTicks) MaxSliceTicks = slice;
            if (_next < _jobCount) return false;
            InFlight = false;
            return true;
        }

        internal bool Drain(Func<long> clock, Action<int> runJob, Action<int, Exception> onFault)
        {
            return Pump(long.MaxValue, clock, runJob, onFault);
        }

        // Abandons the in-flight day without completing it (campaign reset, load).
        internal void Clear()
        {
            _generation++;
            _jobCount = 0;
            _next = 0;
            InFlight = false;
            Faulted = false;
        }
    }

    // Commit of a completed review day (StrategicCoordinator.CommitReviewDay).
    internal static class ReviewDayCommit
    {
        // The published set is replaced by reference only once its copy is complete, so a
        // copy that throws leaves the previous day's set in place, whole. A replaced set is
        // never written again: a reader still holding it keeps seeing one whole day.
        internal static void Publish<T>(ref T published, T working, Func<T, T> copy) where T : class
        {
            T next = copy(working);
            published = next;
        }

        // Publishes and reports the day, then applies the battle results deferred while it
        // ran even when the publish threw, so they are not held back to the next day's commit.
        internal static void Run(Action publishAndReport, Action applyDeferred)
        {
            try
            {
                publishAndReport();
            }
            finally
            {
                applyDeferred();
            }
        }
    }
}
