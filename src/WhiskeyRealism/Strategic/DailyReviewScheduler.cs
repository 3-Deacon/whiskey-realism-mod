using System;
using System.Runtime.ExceptionServices;

namespace WhiskeyRealism.Strategic
{
    internal enum DailyReviewJobKind
    {
        Map,
        Head,
        Fiscal,
        Policy,
        Plan,
        Front,
        Defense,
        Execute,
        Formation,
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
    // alliance 0 and then alliance 1 the head (who runs today, era, succession, capital threat,
    // director posture), fiscal, policy, the campaign planner's review (Plan: snapshot,
    // beliefs, plan), front, defense, the planner's executors (Execute), formation
    // directives, construction and the report lines.
    // This is the order StrategicCoordinator ran them in one frame before the review was
    // time-sliced. Each job decides inside itself whether it has work today (the cadence and
    // source-signature gates read signatures that earlier jobs of the same day write).
    internal static class DailyReviewJobList
    {
        private static readonly DailyReviewJobKind[] PerAlliance =
        {
            DailyReviewJobKind.Head,
            DailyReviewJobKind.Fiscal,
            DailyReviewJobKind.Policy,
            DailyReviewJobKind.Plan,
            DailyReviewJobKind.Front,
            DailyReviewJobKind.Defense,
            DailyReviewJobKind.Execute,
            DailyReviewJobKind.Formation,
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

    // Which copy a reader of the campaign planner's stores (EnemyBeliefStore, CampaignPlanStore)
    // gets: inside a review job the working copy, outside it the copy published at the last
    // commit, the rule StrategicCoordinator.View applies to StrategicLedgerSet. The coordinator
    // attaches its scheduler's RunningJob at bootstrap; with nothing attached (the harness, menus
    // before the coordinator exists) every read is a working read, as before the review was sliced.
    internal static class DailyReviewView
    {
        private static Func<bool> _inJob;

        internal static void Attach(Func<bool> inJob)
        {
            _inJob = inJob;
        }

        internal static bool ReadsWorking => _inJob == null || _inJob();
    }

    // Review work and the campaign planner's terrain memo drain never share a frame: any slice of the
    // review (a pump, a drain before a new day or before a save) marks the frame, and the drain runs
    // only on a frame without review work. Vanilla's SaveAll (#111 DailyReviewSaveDrainPatch) can finish
    // a review earlier in the same frame than the AICampaign.Update Postfix that pumps, which would
    // otherwise put the drain's 0.9 ms after the save drain's work.
    internal sealed class ReviewFrameGate
    {
        private int _workFrame = -1;

        internal void MarkWork(int frame)
        {
            _workFrame = frame;
        }

        internal bool MayDrain(int frame)
        {
            return frame != _workFrame;
        }
    }

    // One reader-facing copy of review state outside StrategicLedgerSet, published with it: the
    // day's commit fills the spare copy from the working state (Prepare) and, once every copy of
    // the day is complete, swaps it in (Swap). A fill that throws leaves the previous day
    // published and nothing is swapped. The two copies alternate, so after warm-up a commit
    // allocates nothing; the review runs on the main thread, so no reader holds a copy across
    // the commit that refills it.
    internal sealed class PublishedDay<T> where T : class
    {
        private T _published;
        private T _spare;
        private bool _prepared;

        internal PublishedDay(T first, T second)
        {
            _published = first;
            _spare = second;
        }

        internal T Published => _published;

        internal void Prepare(Action<T> fill)
        {
            _prepared = false;
            fill(_spare);
            _prepared = true;
        }

        internal void Swap()
        {
            if (!_prepared) return;
            T previous = _published;
            _published = _spare;
            _spare = previous;
            _prepared = false;
        }

        // Clears both copies (a campaign reset).
        internal void Reset(Action<T> clear)
        {
            clear(_published);
            clear(_spare);
            _prepared = false;
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
        // A failure is rethrown afterwards; when both steps fail, both travel in one
        // AggregateException whose message names each, so the second never hides the first.
        internal static void Run(Action publishAndReport, Action applyDeferred)
        {
            Exception publishFailure = null;
            try
            {
                publishAndReport();
            }
            catch (Exception ex)
            {
                publishFailure = ex;
            }

            try
            {
                applyDeferred();
            }
            catch (Exception deferredFailure) when (publishFailure != null)
            {
                throw new AggregateException(
                    "publish failed: " + publishFailure.Message + "; deferred battle results failed: " + deferredFailure.Message,
                    publishFailure,
                    deferredFailure);
            }

            if (publishFailure != null)
                ExceptionDispatchInfo.Capture(publishFailure).Throw();
        }
    }
}
