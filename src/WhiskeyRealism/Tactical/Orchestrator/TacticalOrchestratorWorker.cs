using System;
using System.Threading;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Dedicated background <see cref="Thread"/> that runs tactical orchestrator
    /// analysis off the Unity main thread. Started in <c>OnBattleStart</c> (Task
    /// 12), joined in <c>OnBattleEnd</c>. Single-slot drop-old input enqueue
    /// (last-write-wins) via <see cref="Interlocked.Exchange{T}(ref T, T)"/>;
    /// atomic decision publish via <see cref="TacticalDecisionSnapshot.Publish"/>.
    ///
    /// Worker-thread contract (enforced by Task 7 audit + Task 8 input container):
    /// <list type="bullet">
    ///   <item>No reads of <c>Plugin.Instance.*</c>, <c>Time.*</c>, vanilla statics,
    ///         or any Unity API. All inputs flow through
    ///         <see cref="TacticalOrchestratorWorkerInput"/>.</item>
    ///   <item>Warnings use <c>Plugin.Log.LogWarning</c>, which is the BepInEx
    ///         <c>ManualLogSource</c> documented thread-safe for logging.</item>
    ///   <item><see cref="TelemetryRouter.Emit"/> is the only sanctioned worker
    ///         telemetry path (enqueues to a thread-safe queue; the writer thread
    ///         drains and serialises). <see cref="WhiskeyRealism.Util.OnceLog"/>
    ///         is NOT proven thread-safe and must not be called from here.</item>
    /// </list>
    /// </summary>
    internal sealed class TacticalOrchestratorWorker
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _inputAvailable = new ManualResetEventSlim(initialState: false);
        // Last-write-wins single-slot input. Reads/writes go through Interlocked.Exchange.
        private TacticalOrchestratorWorkerInput _pendingInput;
        private volatile bool _stopRequested;
        private long _cyclesProcessed;
        private int _faultEmitCount;
        private const int FaultEmitMaxPerSession = 64;

        public TacticalOrchestratorWorker()
        {
            _thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "WhiskeyRealism.OrchestratorWorker"
            };
        }

        public void Start()
        {
            if (_thread.IsAlive) return;
            _thread.Start();
        }

        public void RequestStop()
        {
            _stopRequested = true;
            _inputAvailable.Set();
        }

        public void Join(int timeoutMs)
        {
            try
            {
                if (_thread.IsAlive && !_thread.Join(timeoutMs))
                {
                    Plugin.Log.LogWarning("[TacticalOrchestratorWorker] join timed out at "
                        + timeoutMs + "ms; thread is background, will die on AppDomain unload");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestratorWorker] join failed: "
                    + e.GetType().Name + " " + e.Message);
            }
        }

        /// <summary>
        /// Main-thread side: enqueue the latest worker input. Drop-old semantics —
        /// if the worker hasn't picked up the prior input yet, the new one
        /// replaces it. Last-write-wins is intentional: stale orchestrator state
        /// is useless, so the freshest snapshot always supersedes a backlog.
        /// </summary>
        public void Enqueue(TacticalOrchestratorWorkerInput input)
        {
            if (input == null) return;
            Interlocked.Exchange(ref _pendingInput, input);
            _inputAvailable.Set();
        }

        public long CyclesProcessed
        {
            get { return Interlocked.Read(ref _cyclesProcessed); }
        }

        private void WorkerLoop()
        {
            while (!_stopRequested)
            {
                try
                {
                    _inputAvailable.Wait();
                    if (_stopRequested) break;
                    _inputAvailable.Reset();

                    var input = Interlocked.Exchange(ref _pendingInput, null);
                    if (input == null) continue;  // spurious wake, harmless

                    ProcessOne(input);
                }
                catch (ThreadInterruptedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    EmitFault(e);
                    // Continue loop; do not propagate.
                }
            }
        }

        private void ProcessOne(TacticalOrchestratorWorkerInput input)
        {
            long cycleId = Interlocked.Increment(ref _cyclesProcessed);

            TacticalDecisionSnapshot snapshot;
            using (TelemetryPerf.Scope("tactical.worker.cycle", TelemetryLayer.Tactical, TelemetryCategory.Performance, 50.0))
            {
                snapshot = TacticalOrchestratorWorkerAnalysis.Analyze(input, cycleId);
            }

            // Stopped workers (teardown or a stalled Join) and snapshots for a battle
            // that is no longer live are never published (BR-M2).
            if (_stopRequested) return;
            using (TelemetryPerf.Scope("tactical.worker.cycle.publish", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1.0))
            {
                TacticalDecisionSnapshot.TryPublish(snapshot);
            }
        }

        private void EmitFault(Exception e)
        {
            try
            {
                if (_faultEmitCount >= FaultEmitMaxPerSession) return;
                _faultEmitCount++;
                long cycles = Interlocked.Read(ref _cyclesProcessed);
                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Health,
                    "TacticalOrchestratorWorkerFault",
                    TelemetrySeverity.Warning,
                    ev => ev
                        .WithDecision("worker-fault", e.GetType().Name, "cycle=" + cycles)
                        .WithField("exceptionType", e.GetType().Name)
                        .WithField("message", e.Message ?? string.Empty)
                        .WithField("cyclesProcessed", cycles));
            }
            catch
            {
                // Swallow — never propagate from EmitFault.
            }
        }
    }
}
