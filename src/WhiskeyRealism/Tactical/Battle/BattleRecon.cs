using System;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC9 reconnaissance and probing (plan § "BC9 — reconnaissance and probing").
    // Pure: reads the BattleSnapshot, the side's ledger picture (ReconPicture) and the
    // order executor's last outcomes; submits intents; writes nothing else. The
    // runtime half is Producers/ReconIntentProducer.cs.
    //
    // Why: vanilla's battle AI never scouts because it is omniscient (forcebalance
    // 84641-84651 / 84862, GetClosestEnemyUnit 123760, CheckEnemyContact 117155-117383,
    // no fows[1] reader). Whiskey's AI is fog-honest (BattlePerception), so something
    // has to go and look.
    //
    // Vanilla anchors the rules follow:
    //   - The scout is a whole cavalry regiment: scout companies (unittyp 4) are re-pathed
    //     to their parent's target or stopped every Regiment update
    //     (CheckScoutMovingTargets 124616-124660), and a linked group is re-laid by
    //     UpdateUnitLink (124396-124500; screening groups are all linked,
    //     LinkReservesToLineGroup 6729-6742, AssignReserves 7102-7119). A regiment
    //     move lasts until its group is re-laid (SetGroupFormation 92076-92108), so the
    //     scout's brigade must be settled and not ordered this tick.
    //   - Stand-off = distancetoenemytocancelinterruption (150 m, unitprefs 991):
    //     vanilla's own "out of contact" distance, inside cavalry sight (250 m,
    //     unitprefs 73). Corridor = distancetoenemyforinterruption (100 m, unitprefs
    //     989): where vanilla stops a moving line on contact.
    //   - A screen-stance unit is not stopped by contact (CheckEnemyContact gate
    //     ai_stance != 1, 117253), so the planner sends the Stop itself.
    //   - Morale bar = aiminmoraletodeployskirmishers, vanilla's own bar for sending men
    //     out (CheckSkirmishing 5381 / 5400).
    //   - Probe advance = maximumlineofsight[infantry] (200 m, unitprefs 29): one sight
    //     depth past where the group stands.
    //   - Skirmisher link: GetUnitLinkData (124208-124390) puts a front-linked company
    //     unitlinkdistance (10, unitprefs 1069) + skirmisherdistance (20, unitprefs 864)
    //     ahead of its parent regiment; SkirmisherScreenDistancePatch moves that target to the interruption
    //     distance while the line screens out of contact (SkirmisherPushMeters).
    //   - Regiment formation 4 is the square (firearctype 2 = four all-round arcs,
    //     118174 / 118262-118270); a group's formation 4 is march column (5883).
    public enum ReconLeg : byte
    {
        None = 0,
        Outbound = 1,
        Observe = 2,
        Return = 3,
    }

    public enum ReconTargetKind : byte
    {
        None = 0,
        EntryPoint = 1,
        StaleBand = 2,
        Forward = 3,
        RememberedEnemy = 4,
        /// <summary>BC14: a link crossing the plan depends on that the side does not know (the feasibility ledger's look request).</summary>
        Crossing = 5,
    }

    [Flags]
    public enum ReconEvents
    {
        None = 0,
        MissionStarted = 1,
        ProbeNominated = 2,
        MissionEnded = 4,
        ProbeEnded = 8,
        ProbeHeld = 16,
    }

    public struct ReconTickResult
    {
        public ReconEvents Events;
        public ReconTargetKind StartedKind;
        public string EndReason; // static literal
        public int Intents;
    }

    /// <summary>Vanilla distances and the rollback flag, read by the runtime from GamePrefs.</summary>
    public readonly struct ReconSettings
    {
        public const int MaxMissionStartsPerHour = 4;
        // Recency 0.5 on the contact ledger's 2 h scale (ContactMemoryHours).
        public const float StaleHours = 1f;
        public const float StaleBandRecency = 0.5f;
        public const float TargetRepeatMeters = 100f;
        public const double TargetRepeatHours = 1.0;
        public const double ObserveDwellHours = 0.05;
        public const double MaxMissionHours = 0.5;
        public const double MaxProbeHours = 0.25;
        public const double ProbeRepeatHours = 1.0;
        // Perf slice B1: targets of one list (stale entry points, remembered enemies) tried
        // per tick; a failed window moves on to the next ranks next tick.
        public const int MaxTargetsPerTick = 8;

        public ReconSettings(bool enabled, float cavalrySight, float infantrySight, float standOff, float interruptMeters, float minMorale)
        {
            Enabled = enabled;
            CavalrySight = Positive(cavalrySight, 250f);
            InfantrySight = Positive(infantrySight, 200f);
            ObservationStandOff = Positive(standOff, 150f);
            CorridorMeters = Positive(interruptMeters, 100f);
            MinMorale = float.IsNaN(minMorale) || float.IsInfinity(minMorale) ? 0f : minMorale;
        }

        public bool Enabled { get; }
        public float CavalrySight { get; }
        public float InfantrySight { get; }
        public float ObservationStandOff { get; }
        public float CorridorMeters { get; }
        public float MinMorale { get; }
        public float ForwardReconMeters => 2f * CavalrySight;
        public float ProbeAdvanceMeters => InfantrySight;

        private static float Positive(float v, float fallback)
        {
            return float.IsNaN(v) || float.IsInfinity(v) || v <= 0f ? fallback : v;
        }
    }

    /// <summary>
    /// What the side knows this tick beyond the snapshot: the contact ledger's
    /// confidence, stalest band and entry-point watch stamps, the army's phase, the
    /// battle stance, and per own unit whether it is in the AI pool and whether the
    /// posture producer moved it this tick. Pooled; the runtime refills it per tick.
    /// </summary>
    public sealed class ReconPicture
    {
        public float IntelConfidence;
        public bool HadContact;
        public bool HasStaleBand;
        public float StaleBandLateral;
        public float StaleBandRecency = 1f;
        public float FrameHeading;
        // The army plan's phase; null before the army has a plan (no probing).
        public BattlePhase? Phase;
        public bool SideDefending;
        // The operation's primary objective (posture's TryPrimaryObjectivePoint): the
        // direction to look in while the snapshot front axis has no target yet.
        public bool HasObjective;
        public float ObjectiveX, ObjectiveZ;
        /// <summary>
        /// BC14W: the side's held enemy works (the feasibility ledger's picture), null with the works doctrine off or no works seen. A look point
        /// on them or in their band is refused (<see cref="BattleRecon.LookPoint"/>), so no probe is nominated and no Scout looks into the works.
        /// </summary>
        public IWorksQuery Works;
        public bool[] AiPool = new bool[BattleSnapshot.InitialCapacity];
        public bool[] PostureMoved = new bool[BattleSnapshot.InitialCapacity];
        // The posture pipeline's task for the group this tick (PostureTick GroupPosture
        // State.Task; None when it has none). The ledger task a probe was nominated
        // under, and the test for "the ledger reassigned it" that ends a hold.
        public CommandTaskType[] PostureTask = new CommandTaskType[BattleSnapshot.InitialCapacity];

        private float[] _lookX = new float[2];
        private float[] _lookZ = new float[2];
        private float[] _entryX = new float[8];
        private float[] _entryZ = new float[8];
        private double[] _entryWatched = new double[8];

        public int EntryCount { get; private set; }

        /// <summary>BC14: the crossings the plan wants looked at (the ledger's look requests), most pressing first.</summary>
        public int LookCount { get; private set; }

        public void BeginTick(int ownCount)
        {
            if (ownCount < 0) ownCount = 0;
            if (AiPool.Length < ownCount)
            {
                AiPool = new bool[Math.Max(ownCount, AiPool.Length * 2)];
                PostureMoved = new bool[AiPool.Length];
                PostureTask = new CommandTaskType[AiPool.Length];
            }
            Array.Clear(AiPool, 0, ownCount);
            Array.Clear(PostureMoved, 0, ownCount);
            Array.Clear(PostureTask, 0, ownCount);
            EntryCount = 0;
            LookCount = 0;
            Works = null;
            HasStaleBand = false;
            StaleBandRecency = 1f;
        }

        /// <summary>An enemy-side entry point and the doctrine hour it was last watched (&lt; 0 never).</summary>
        public void AddEntry(float x, float z, double lastWatchedHours)
        {
            if (EntryCount == _entryX.Length)
            {
                Array.Resize(ref _entryX, _entryX.Length * 2);
                Array.Resize(ref _entryZ, _entryX.Length);
                Array.Resize(ref _entryWatched, _entryX.Length);
            }
            _entryX[EntryCount] = x;
            _entryZ[EntryCount] = z;
            _entryWatched[EntryCount] = lastWatchedHours;
            EntryCount++;
        }

        /// <summary>A crossing look point (world x and z).</summary>
        public void AddLook(float x, float z)
        {
            if (LookCount == _lookX.Length)
            {
                Array.Resize(ref _lookX, _lookX.Length * 2);
                Array.Resize(ref _lookZ, _lookX.Length);
            }
            _lookX[LookCount] = x;
            _lookZ[LookCount] = z;
            LookCount++;
        }

        public float LookX(int k) => _lookX[k];
        public float LookZ(int k) => _lookZ[k];

        public float EntryX(int k) => _entryX[k];
        public float EntryZ(int k) => _entryZ[k];
        public double EntryWatchedHours(int k) => _entryWatched[k];

        /// <summary>Never watched, or not for <see cref="ReconSettings.StaleHours"/>.</summary>
        public bool EntryStale(int k, double nowHours)
        {
            double w = _entryWatched[k];
            return w < 0.0 || nowHours - w > ReconSettings.StaleHours;
        }
    }

    /// <summary>Per side, per battle (the runtime registers its reset with BattleScope).</summary>
    public sealed class ReconMemory
    {
        public const int MaxBands = 8;
        private const int RecentTargetSlots = 8;

        // The one running mission.
        public ReconLeg Leg { get; private set; }
        public int ScoutId { get; private set; }
        public ReconTargetKind Kind { get; private set; }
        public float TargetX { get; private set; }
        public float TargetZ { get; private set; }
        public bool Confirmed { get; private set; }
        // The executor retried this leg's move at a rotated bearing (Retried outcome):
        // the scout's waypoint is then off our target by our own doing.
        internal bool Retried { get; private set; }
        private double _startedHours, _legHours, _submittedHours;

        private readonly double[] _starts = new double[ReconSettings.MaxMissionStartsPerHour];
        private int _startNext;
        private readonly float[] _recentX = new float[RecentTargetSlots];
        private readonly float[] _recentZ = new float[RecentTargetSlots];
        private readonly double[] _recentAt = new double[RecentTargetSlots];
        private int _recentNext;

        private readonly int[] _probeGroup = new int[MaxBands];
        private readonly float[] _probeX = new float[MaxBands];
        private readonly float[] _probeZ = new float[MaxBands];
        private readonly double[] _probeStarted = new double[MaxBands];
        private readonly double[] _probeLastStart = new double[MaxBands];
        private readonly CommandTaskType[] _probeTask = new CommandTaskType[MaxBands];
        private readonly float[] _probeDirX = new float[MaxBands];
        private readonly float[] _probeDirZ = new float[MaxBands];

        // Ground a probe gained without meeting a superior enemy: the group holds it
        // (the posture pipeline does not walk it back) until the ledger reassigns it.
        // One slot per holding group, grow-only: a new hold never takes another
        // group's slot (at most one hold per own group, so growth stops at the roster).
        private int[] _holdGroup = new int[MaxBands];
        private float[] _holdX = new float[MaxBands];
        private float[] _holdZ = new float[MaxBands];
        private float[] _holdDirX = new float[MaxBands];
        private float[] _holdDirZ = new float[MaxBands];
        private CommandTaskType[] _holdTask = new CommandTaskType[MaxBands];

        // Scratch (grow-only).
        internal UnitFlags[] Descendants = new UnitFlags[BattleSnapshot.InitialCapacity];
        internal int[] Moving = new int[BattleSnapshot.InitialCapacity];
        // Mission search scratch (perf slice B1): this tick's scout candidates and visible
        // (non-reinforcement) observations, both ascending; one candidate-target list.
        internal int[] Scouts = new int[64];
        internal int ScoutCount;
        internal int[] Visible = new int[BattleSnapshot.InitialCapacity];
        internal int VisibleCount;
        internal int[] TargetOrder = new int[BattleSnapshot.InitialCapacity];
        internal float[] TargetDistance = new float[BattleSnapshot.InitialCapacity];
        // Rank window of the entry-point and remembered-enemy lists still to try after a
        // capped search failed (ReconSettings.MaxTargetsPerTick); 0 after a start.
        internal int EntryCursor;
        internal int RememberedCursor;

        public ReconMemory()
        {
            Clear();
        }

        public void Clear()
        {
            Leg = ReconLeg.None;
            ScoutId = 0;
            Kind = ReconTargetKind.None;
            Confirmed = false;
            for (int k = 0; k < _starts.Length; k++) _starts[k] = double.NegativeInfinity;
            for (int k = 0; k < RecentTargetSlots; k++) _recentAt[k] = double.NegativeInfinity;
            _startNext = 0;
            _recentNext = 0;
            for (int b = 0; b < MaxBands; b++)
            {
                _probeGroup[b] = 0;
                _probeLastStart[b] = double.NegativeInfinity;
            }
            Array.Clear(_holdGroup, 0, _holdGroup.Length);
            EntryCursor = 0;
            RememberedCursor = 0;
        }

        /// <summary>The probe nomination of <paramref name="groupId"/>: its look point.</summary>
        /// <summary>BC13: the running mission's scout group, 0 with no mission (an exploit never picks it, an ancestor or a descendant).</summary>
        public int MissionScoutId => Leg != ReconLeg.None ? ScoutId : 0;

        /// <summary>BC13: the group is a probe's nominee or holds the ground its probe gained (an exploit never picks it).</summary>
        public bool HasProbeOrHold(int groupId)
        {
            return groupId != 0 && (TryGetProbe(groupId, out _, out _) || HoldSlotOf(groupId) >= 0);
        }

        public bool TryGetProbe(int groupId, out float x, out float z)
        {
            for (int b = 0; b < MaxBands; b++)
            {
                if (groupId == 0 || _probeGroup[b] != groupId) continue;
                x = _probeX[b];
                z = _probeZ[b];
                return true;
            }
            x = 0f;
            z = 0f;
            return false;
        }

        /// <summary>
        /// The ground <paramref name="groupId"/> holds after its probe, while its posture
        /// task is still the ledger task it was nominated under; the probe's advance
        /// direction (unit vector).
        /// </summary>
        public bool TryGetHold(int groupId, CommandTaskType postureTask, out float x, out float z, out float dirX, out float dirZ)
        {
            int k = groupId != 0 ? HoldSlotOf(groupId) : -1;
            if (k >= 0 && _holdTask[k] == postureTask)
            {
                x = _holdX[k];
                z = _holdZ[k];
                dirX = _holdDirX[k];
                dirZ = _holdDirZ[k];
                return true;
            }
            x = z = dirX = dirZ = 0f;
            return false;
        }

        internal bool StartBudgetLeft(double now)
        {
            int recent = 0;
            for (int k = 0; k < _starts.Length; k++)
                if (now - _starts[k] < 1.0) recent++;
            return recent < ReconSettings.MaxMissionStartsPerHour;
        }

        internal bool RecentTarget(float x, float z, double now)
        {
            float r2 = ReconSettings.TargetRepeatMeters * ReconSettings.TargetRepeatMeters;
            for (int k = 0; k < RecentTargetSlots; k++)
            {
                if (now - _recentAt[k] >= ReconSettings.TargetRepeatHours) continue;
                float dx = _recentX[k] - x, dz = _recentZ[k] - z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        internal void Start(int scoutId, ReconTargetKind kind, float lookX, float lookZ, float standX, float standZ, double now)
        {
            Leg = ReconLeg.Outbound;
            ScoutId = scoutId;
            Kind = kind;
            TargetX = standX;
            TargetZ = standZ;
            Confirmed = false;
            Retried = false;
            _startedHours = _legHours = _submittedHours = now;
            _starts[_startNext] = now;
            _startNext = (_startNext + 1) % _starts.Length;
            _recentX[_recentNext] = lookX;
            _recentZ[_recentNext] = lookZ;
            _recentAt[_recentNext] = now;
            _recentNext = (_recentNext + 1) % RecentTargetSlots;
        }

        internal void EnterLeg(ReconLeg leg, float x, float z, double now)
        {
            Leg = leg;
            TargetX = x;
            TargetZ = z;
            _legHours = now;
            if (leg != ReconLeg.Observe)
            {
                _submittedHours = now;
                Confirmed = false;
                Retried = false;
            }
        }

        internal void Confirm() { Confirmed = true; }

        internal void NoteRetry() { Retried = true; }

        internal void End()
        {
            Leg = ReconLeg.None;
            ScoutId = 0;
            Kind = ReconTargetKind.None;
            Confirmed = false;
        }

        internal double StartedHours => _startedHours;
        internal double LegHours => _legHours;
        internal double SubmittedHours => _submittedHours;

        internal int ProbeGroup(int band) => _probeGroup[band];
        internal float ProbeX(int band) => _probeX[band];
        internal float ProbeZ(int band) => _probeZ[band];
        internal double ProbeStarted(int band) => _probeStarted[band];
        internal double ProbeLastStart(int band) => _probeLastStart[band];

        internal void Nominate(int band, int groupId, float fromX, float fromZ, float x, float z, CommandTaskType ledgerTask, double now)
        {
            _probeGroup[band] = groupId;
            _probeX[band] = x;
            _probeZ[band] = z;
            _probeStarted[band] = now;
            _probeLastStart[band] = now;
            _probeTask[band] = ledgerTask;
            float dx = x - fromX, dz = z - fromZ;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            _probeDirX[band] = len > 0f ? dx / len : 0f;
            _probeDirZ[band] = len > 0f ? dz / len : 0f;
            ReleaseHold(groupId);
        }

        internal void ClearProbe(int band) { _probeGroup[band] = 0; }

        // The probe of band b ended short of a superior enemy: its group holds (x, z).
        // No hold without a known ledger task (the posture pipeline had no state).
        internal bool Hold(int band, float x, float z)
        {
            int groupId = _probeGroup[band];
            if (groupId == 0 || _probeTask[band] == CommandTaskType.None) return false;
            int k = HoldSlotOf(groupId);
            if (k < 0) k = HoldSlotOf(0); // a free slot
            if (k < 0) k = GrowHolds();
            _holdGroup[k] = groupId;
            _holdX[k] = x;
            _holdZ[k] = z;
            _holdDirX[k] = _probeDirX[band];
            _holdDirZ[k] = _probeDirZ[band];
            _holdTask[k] = _probeTask[band];
            return true;
        }

        internal int HoldSlots => _holdGroup.Length;
        internal int HoldGroup(int slot) => _holdGroup[slot];
        internal CommandTaskType HoldTask(int slot) => _holdTask[slot];
        internal void ClearHold(int slot) { _holdGroup[slot] = 0; }

        private void ReleaseHold(int groupId)
        {
            int k = groupId != 0 ? HoldSlotOf(groupId) : -1;
            if (k >= 0) _holdGroup[k] = 0;
        }

        // Doubles the store; returns the first new (free) slot.
        private int GrowHolds()
        {
            int old = _holdGroup.Length;
            int size = old * 2;
            Array.Resize(ref _holdGroup, size);
            Array.Resize(ref _holdX, size);
            Array.Resize(ref _holdZ, size);
            Array.Resize(ref _holdDirX, size);
            Array.Resize(ref _holdDirZ, size);
            Array.Resize(ref _holdTask, size);
            return old;
        }

        private int HoldSlotOf(int groupId)
        {
            for (int k = 0; k < _holdGroup.Length; k++)
                if (_holdGroup[k] == groupId) return k;
            return -1;
        }

        internal bool IsNominated(int groupId)
        {
            for (int b = 0; b < MaxBands; b++)
                if (groupId != 0 && _probeGroup[b] == groupId) return true;
            return false;
        }
    }

    public static class BattleRecon
    {
        public const string OutboundReason = "recon-outbound";
        public const string ReturnReason = "recon-return";
        public const string StopReason = "recon-stop";
        public const string ProbeStopReason = "probe-contact-stop";
        public const string HoldReason = "probe-hold";

        /// <summary>
        /// Whether the telemetry row of this event names a map point: only a started mission has one (its scout's
        /// target). A probe nomination, a probe hold and a mission end carry none, and a row that printed
        /// targetX=0, targetZ=0 for them read as a scout sent to the map origin (smoke 2026-10-17).
        /// </summary>
        public static bool EventNamesTarget(ReconEvents single) => single == ReconEvents.MissionStarted;

        // Vanilla regiment formation index of the square (118174).
        public const int SquareFormation = 4;

        private const UnitFlags ScoutRefusals = UnitFlags.PlayerOwned | UnitFlags.Feud | UnitFlags.Routed |
            UnitFlags.MarkedForRout | UnitFlags.OnRetreat | UnitFlags.InMelee | UnitFlags.Garrisoned |
            UnitFlags.PermanentlyDetached | UnitFlags.DetachedCompany | UnitFlags.IsGroup;

        /// <summary>
        /// One arbiter tick of the side's recon: maintain or start the cavalry mission,
        /// maintain or nominate the probe groups. Allocation-free once the pooled
        /// arrays fit.
        /// </summary>
        public static ReconTickResult Plan(BattleSnapshot s, in ReconSettings settings, ReconPicture picture, ReconMemory memory,
            OrderExecutor executor, IIntentSink sink)
        {
            var result = new ReconTickResult();
            if (s == null || picture == null || memory == null || sink == null) return result;
            double now = s.DoctrineHours;
            if (!settings.Enabled)
            {
                // Rollback: hand everything back to the posture executor and vanilla.
                if (memory.Leg != ReconLeg.None) End(memory, ref result, "disabled");
                for (int b = 0; b < ReconMemory.MaxBands; b++) memory.ClearProbe(b);
                for (int k = 0; k < memory.HoldSlots; k++) memory.ClearHold(k);
                return result;
            }

            PostureSense.AggregateDescendants(s, ref memory.Descendants, ref memory.Moving);
            ProbeTick(s, settings, picture, memory, sink, now, ref result);

            if (memory.Leg != ReconLeg.None) MissionTick(s, settings, memory, executor, sink, now, ref result);
            else TryStartMission(s, settings, picture, memory, sink, now, ref result);
            return result;
        }

        // ---------------------------------------------------------------- mission

        private static void MissionTick(BattleSnapshot s, in ReconSettings st, ReconMemory m, OrderExecutor executor,
            IIntentSink sink, double now, ref ReconTickResult r)
        {
            int idx = s.IndexOfOwn(m.ScoutId);
            if (idx < 0) { End(m, ref r, "scout-lost"); return; }
            ref UnitRecord rec = ref s.Own[idx];
            if (now - m.StartedHours > ReconSettings.MaxMissionHours) { End(m, ref r, "timeout"); return; }
            if ((rec.Flags & (UnitFlags.Alive | UnitFlags.Active)) != (UnitFlags.Alive | UnitFlags.Active) ||
                (rec.Flags & (ScoutRefusals & ~UnitFlags.IsGroup)) != 0)
            {
                End(m, ref r, "scout-lost");
                return;
            }

            // Executor evidence (BC6b pattern): a move by anyone else after ours takes the
            // scout; ours counts only once the executor issued it.
            if (m.Leg != ReconLeg.Observe || m.Confirmed)
            {
                if (executor != null && executor.TryGetLastOutcome(m.ScoutId, OrderChannel.Move, out OrderOutcome outcome) &&
                    outcome.AtHours >= m.SubmittedHours)
                {
                    if (outcome.Source != IntentSource.Recon) { End(m, ref r, "recalled"); return; }
                    if (outcome.Kind == OrderOutcomeKind.GaveUp || outcome.Kind == OrderOutcomeKind.Superseded)
                    {
                        End(m, ref r, "gave-up");
                        return;
                    }
                    if (outcome.Kind == OrderOutcomeKind.Retried) m.NoteRetry();
                    m.Confirm();
                }
                else if (m.Leg == ReconLeg.Outbound && !m.Confirmed && now > m.SubmittedHours)
                {
                    End(m, ref r, "not-issued"); // fail closed: guard, claim, cooldown or validator refused it
                    return;
                }
            }

            float strength = Math.Max(1f, rec.Strength);
            bool hasEnemy = NearestVisible(s, rec.X, rec.Z, out float enemyDistance, out float enemyStrength);
            bool superiorClose = hasEnemy && enemyStrength >= strength && enemyDistance <= st.CorridorMeters;

            switch (m.Leg)
            {
                case ReconLeg.Outbound:
                    if (superiorClose) { StartReturn(s, idx, m, sink, now, ref r); return; }
                    if (hasEnemy && enemyDistance <= st.ObservationStandOff)
                    {
                        sink.Submit(Stop(m.ScoutId, StopReason));
                        r.Intents++;
                        m.EnterLeg(ReconLeg.Observe, m.TargetX, m.TargetZ, now);
                        return;
                    }
                    if (Distance(rec.X, rec.Z, m.TargetX, m.TargetZ) <= OrderArbiter.DedupeMeters ||
                        (m.Confirmed && rec.RegimentPaths <= 0 && now > m.SubmittedHours))
                    {
                        m.EnterLeg(ReconLeg.Observe, m.TargetX, m.TargetZ, now);
                        return;
                    }
                    if (m.Confirmed && !m.Retried && rec.RegimentPaths > 0 && PostureSense.HasPoint(rec.LastWaypointX, rec.LastWaypointZ) &&
                        Distance(rec.LastWaypointX, rec.LastWaypointZ, m.TargetX, m.TargetZ) > OrderArbiter.DedupeMeters)
                    {
                        End(m, ref r, "recalled"); // vanilla re-laid the scout with its brigade
                        return;
                    }
                    sink.Submit(Move(m.ScoutId, m.TargetX, m.TargetZ, OutboundReason));
                    r.Intents++;
                    return;
                case ReconLeg.Observe:
                    if (superiorClose || now - m.LegHours >= ReconSettings.ObserveDwellHours)
                        StartReturn(s, idx, m, sink, now, ref r);
                    return;
                case ReconLeg.Return:
                    if (Distance(rec.X, rec.Z, m.TargetX, m.TargetZ) <= OrderArbiter.DedupeMeters) { End(m, ref r, "returned"); return; }
                    sink.Submit(Move(m.ScoutId, m.TargetX, m.TargetZ, ReturnReason));
                    r.Intents++;
                    return;
            }
        }

        // Back to the scout's slot in its brigade's layout (the shipped #61 leaf slot
        // rule), else to the brigade's position.
        private static void StartReturn(BattleSnapshot s, int idx, ReconMemory m, IIntentSink sink, double now, ref ReconTickResult r)
        {
            ref UnitRecord rec = ref s.Own[idx];
            float x, z;
            if (!LeafSlotTarget.TryResolve(s, idx, 0f, 0f, RefuseFlank.None, out int parent, out x, out z, out _))
            {
                if (parent < 0 || parent >= s.OwnCount) { End(m, ref r, "no-way-back"); return; }
                x = s.Own[parent].X;
                z = s.Own[parent].Z;
            }
            if (Distance(rec.X, rec.Z, x, z) <= OrderArbiter.DedupeMeters) { End(m, ref r, "returned"); return; }
            m.EnterLeg(ReconLeg.Return, x, z, now);
            sink.Submit(Move(m.ScoutId, x, z, ReturnReason));
            r.Intents++;
        }

        // Perf slice B1. The search is the shipped one with its loop-invariant work hoisted:
        // the scout candidates (IsScoutCandidate does not depend on the target) and the
        // visible observations are collected once per tick in ascending order, so
        // NearestScout and StandPointSafe see the same units in the same order; with no
        // candidate no target can start a mission, so the tick returns at once. The entry
        // points and the remembered enemies are tried nearest first, at most
        // ReconSettings.MaxTargetsPerTick per list per tick: within that cap the pick is
        // the shipped one (the nearest feasible target, the lowest index on a tie); a list
        // longer than the cap whose window all failed resumes at the next ranks next tick.
        private static void TryStartMission(BattleSnapshot s, in ReconSettings st, ReconPicture p, ReconMemory m, IIntentSink sink,
            double now, ref ReconTickResult r)
        {
            if ((r.Events & ReconEvents.MissionEnded) != 0 || !m.StartBudgetLeft(now)) return;
            if (!CollectScouts(s, st, p, m)) return;
            if (!OwnLine(s, p.FrameHeading, out float centroidX, out float centroidZ, out float frameDepth, out float axisDepth)) return;
            CollectVisible(s, m);

            int scout = -1;
            float lookX = 0f, lookZ = 0f, standX = 0f, standZ = 0f;
            ReconTargetKind kind = ReconTargetKind.None;

            // 1. The nearest stale enemy-side entry point.
            int n = 0;
            for (int k = 0; k < p.EntryCount; k++)
            {
                if (!p.EntryStale(k, now)) continue;
                float ex = p.EntryX(k), ez = p.EntryZ(k);
                if (m.RecentTarget(ex, ez, now)) continue;
                AddTarget(m, ref n, k, Distance(centroidX, centroidZ, ex, ez));
            }
            int best = BestStandOffTarget(s, st, m, n, ref m.EntryCursor, ReconTargetKind.EntryPoint, p, out int c, out float sx, out float sz);
            if (best >= 0)
            {
                scout = c; kind = ReconTargetKind.EntryPoint;
                lookX = p.EntryX(best); lookZ = p.EntryZ(best); standX = sx; standZ = sz;
            }

            // 2. The stalest coverage band, at the own line's depth.
            if (scout < 0 && p.HasStaleBand && p.StaleBandRecency < ReconSettings.StaleBandRecency && !float.IsNaN(p.FrameHeading))
            {
                BandWorldPoint(p.StaleBandLateral, p.FrameHeading, frameDepth, out float bx, out float bz);
                if (!m.RecentTarget(bx, bz, now)) TryTarget(s, st, m, bx, bz, ReconTargetKind.StaleBand, ref scout, ref kind, ref lookX, ref lookZ, ref standX, ref standZ);
            }

            // 3. BC14: an unknown link crossing the plan depends on, from the stand-off on the own side (the scout's side of it).
            if (scout < 0 && p.LookCount > 0)
            {
                n = 0;
                for (int k = 0; k < p.LookCount; k++)
                {
                    if (m.RecentTarget(p.LookX(k), p.LookZ(k), now)) continue;
                    AddTarget(m, ref n, k, Distance(centroidX, centroidZ, p.LookX(k), p.LookZ(k)));
                }
                int lookCursor = 0;
                best = BestStandOffTarget(s, st, m, n, ref lookCursor, ReconTargetKind.Crossing, p, out c, out sx, out sz);
                if (best >= 0)
                {
                    scout = c; kind = ReconTargetKind.Crossing;
                    lookX = p.LookX(best); lookZ = p.LookZ(best); standX = sx; standZ = sz;
                }
            }

            // 4. Before any contact, below the action gate: straight ahead (along the
            // front axis, or toward the objective while the axis has no target).
            if (scout < 0 && !p.HadContact && p.IntelConfidence < TacticalReinforcementOpportunityDoctrine.MinIntelConfidenceForAction)
            {
                TacticalFrontAxis axis = s.FrontAxis;
                float fx, fz;
                if (axis.IsValid && axis.TargetDistance > 0f)
                {
                    fx = axis.OriginX + axis.ForwardX * (axisDepth + st.ForwardReconMeters);
                    fz = axis.OriginZ + axis.ForwardZ * (axisDepth + st.ForwardReconMeters);
                }
                else if (AdvanceDirection(s, p, centroidX, centroidZ, out float dx, out float dz, out float reach))
                {
                    float advance = Math.Min(st.ForwardReconMeters, reach);
                    fx = centroidX + dx * advance;
                    fz = centroidZ + dz * advance;
                }
                else return;
                if (!m.RecentTarget(fx, fz, now)) TryTarget(s, st, m, fx, fz, ReconTargetKind.Forward, ref scout, ref kind, ref lookX, ref lookZ, ref standX, ref standZ);
            }

            // 5. After contact, below the action gate: look again at the nearest enemy
            // last seen (a Remembered observation), from the stand-off.
            if (scout < 0 && p.HadContact && p.IntelConfidence < TacticalReinforcementOpportunityDoctrine.MinIntelConfidenceForAction)
            {
                EnemyObservation[] obs = s.Observations;
                n = 0;
                for (int k = 0; k < s.ObservationCount; k++)
                {
                    if (obs[k].State != ObservationState.Remembered || obs[k].IsReinforcement) continue;
                    if (m.RecentTarget(obs[k].X, obs[k].Z, now)) continue;
                    AddTarget(m, ref n, k, Distance(centroidX, centroidZ, obs[k].X, obs[k].Z));
                }
                best = BestStandOffTarget(s, st, m, n, ref m.RememberedCursor, ReconTargetKind.RememberedEnemy, p, out c, out sx, out sz);
                if (best >= 0)
                {
                    scout = c; kind = ReconTargetKind.RememberedEnemy;
                    lookX = obs[best].X; lookZ = obs[best].Z; standX = sx; standZ = sz;
                }
            }

            if (scout < 0) return;
            m.Start(s.Own[scout].InstanceId, kind, lookX, lookZ, standX, standZ, now);
            sink.Submit(Move(m.ScoutId, standX, standZ, OutboundReason));
            r.Intents++;
            r.Events |= ReconEvents.MissionStarted;
            r.StartedKind = kind;
        }

        // This tick's scout candidates, ascending; false when there is none.
        private static bool CollectScouts(BattleSnapshot s, in ReconSettings st, ReconPicture p, ReconMemory m)
        {
            m.ScoutCount = 0;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if (!IsScoutCandidate(s, i, st, p)) continue;
                if (m.ScoutCount == m.Scouts.Length) Array.Resize(ref m.Scouts, m.Scouts.Length * 2);
                m.Scouts[m.ScoutCount++] = i;
            }
            return m.ScoutCount > 0;
        }

        // This tick's visible, non-reinforcement observations, ascending (StandPointSafe's set).
        private static void CollectVisible(BattleSnapshot s, ReconMemory m)
        {
            EnemyObservation[] obs = s.Observations;
            if (m.Visible.Length < s.ObservationCount) m.Visible = new int[Math.Max(s.ObservationCount, m.Visible.Length * 2)];
            m.VisibleCount = 0;
            for (int k = 0; k < s.ObservationCount; k++)
                if (obs[k].State == ObservationState.Visible && !obs[k].IsReinforcement) m.Visible[m.VisibleCount++] = k;
        }

        private static void AddTarget(ReconMemory m, ref int n, int source, float distance)
        {
            if (n == m.TargetOrder.Length)
            {
                Array.Resize(ref m.TargetOrder, n * 2);
                Array.Resize(ref m.TargetDistance, n * 2);
            }
            m.TargetOrder[n] = source;
            m.TargetDistance[n] = distance;
            n++;
        }

        // The nearest target of the list (entry points, crossing looks or remembered observations,
        // by source) that has a scout and a safe stand-off point; -1 when none of this
        // tick's window does. Ranks are by (distance, source index), the shipped loops' order.
        private static int BestStandOffTarget(BattleSnapshot s, in ReconSettings st, ReconMemory m, int n, ref int cursor, ReconTargetKind source,
            ReconPicture picture, out int scout, out float standX, out float standZ)
        {
            scout = -1;
            standX = standZ = 0f;
            if (n == 0) { cursor = 0; return -1; }
            SortTargets(m.TargetDistance, m.TargetOrder, n);
            int start = n <= ReconSettings.MaxTargetsPerTick ? 0 : cursor % n;
            int tries = Math.Min(n, ReconSettings.MaxTargetsPerTick);
            int best = -1;
            float bestDistance = float.MaxValue;
            EnemyObservation[] obs = s.Observations;
            for (int t = 0; t < tries; t++)
            {
                int j = (start + t) % n;
                int k = m.TargetOrder[j];
                float d = m.TargetDistance[j];
                if (best >= 0 && (d > bestDistance || (d == bestDistance && k > best))) continue;
                float tx = source == ReconTargetKind.EntryPoint ? picture.EntryX(k) : source == ReconTargetKind.Crossing ? picture.LookX(k) : obs[k].X;
                float tz = source == ReconTargetKind.EntryPoint ? picture.EntryZ(k) : source == ReconTargetKind.Crossing ? picture.LookZ(k) : obs[k].Z;
                int c = NearestScout(s, m, tx, tz);
                if (c < 0 || !TryStandOff(s, st, m, c, tx, tz, out float sx, out float sz)) continue;
                best = k;
                bestDistance = d;
                scout = c;
                standX = sx;
                standZ = sz;
                if (start == 0) break; // an ascending window: the first feasible is the nearest
            }
            cursor = best >= 0 ? 0 : (start + tries) % n;
            return best;
        }

        // Ascending by distance, then by source index (allocation-free).
        private static void SortTargets(float[] distance, int[] order, int n)
        {
            if (n <= 1) return;
            Array.Sort(distance, order, 0, n);
            for (int i = 1; i < n; i++)
            {
                float d = distance[i];
                int k = order[i];
                int j = i - 1;
                while (j >= 0 && distance[j] == d && order[j] > k)
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = k;
            }
        }

        // The point ObservationStandOff short of a target on the line from the scout,
        // when the scout is farther than that (plus the 60 m move floor) and the point
        // is safe.
        private static bool TryStandOff(BattleSnapshot s, in ReconSettings st, ReconMemory m, int scout, float tx, float tz, out float sx, out float sz)
        {
            ref UnitRecord sr = ref s.Own[scout];
            sx = 0f;
            sz = 0f;
            float span = Distance(sr.X, sr.Z, tx, tz);
            if (span <= st.ObservationStandOff + OrderArbiter.DedupeMeters) return false;
            sx = tx + (sr.X - tx) / span * st.ObservationStandOff;
            sz = tz + (sr.Z - tz) / span * st.ObservationStandOff;
            return StandPointSafe(s, m, sr.X, sr.Z, sx, sz, Math.Max(1f, sr.Strength), st);
        }

        private static void TryTarget(BattleSnapshot s, in ReconSettings st, ReconMemory m, float x, float z, ReconTargetKind kind,
            ref int scout, ref ReconTargetKind chosen, ref float lookX, ref float lookZ, ref float standX, ref float standZ)
        {
            int c = NearestScout(s, m, x, z);
            if (c < 0) return;
            ref UnitRecord sr = ref s.Own[c];
            if (!PostureSense.IsMoveAway(sr, x, z)) return;
            if (!StandPointSafe(s, m, sr.X, sr.Z, x, z, Math.Max(1f, sr.Strength), st)) return;
            scout = c; chosen = kind;
            lookX = standX = x;
            lookZ = standZ = z;
        }

        // Over this tick's scout candidates (CollectScouts), ascending as the shipped scan.
        private static int NearestScout(BattleSnapshot s, ReconMemory m, float x, float z)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int n = 0; n < m.ScoutCount; n++)
            {
                int i = m.Scouts[n];
                float d = Distance(s.Own[i].X, s.Own[i].Z, x, z);
                if (d >= bestD) continue;
                bestD = d;
                best = i;
            }
            return best;
        }

        /// <summary>
        /// A recon scout: an own cavalry regiment (not a company) that stands, is fit
        /// and AI-controlled, with no visible enemy inside the stand-off, under a
        /// settled AI brigade the posture producer did not order this tick.
        /// </summary>
        public static bool IsScoutCandidate(BattleSnapshot s, int i, in ReconSettings st, ReconPicture p)
        {
            if (s == null || p == null || i < 0 || i >= s.OwnCount) return false;
            ref UnitRecord rec = ref s.Own[i];
            if (rec.UnitTyp != TacticalUnitType.Cavalry) return false;
            if ((rec.Flags & (UnitFlags.Alive | UnitFlags.Active)) != (UnitFlags.Alive | UnitFlags.Active)) return false;
            if ((rec.Flags & ScoutRefusals) != 0) return false;
            if (rec.RegimentPaths > 0 || rec.QueuedOrders > 0 || rec.MovementMode != 0) return false;
            if (rec.Morale < st.MinMorale) return false;

            // The cheap parent checks run before the enemy distance (all side-effect free).
            int parent = rec.ParentIndex;
            if (parent < 0 || parent >= s.OwnCount) return false;
            ref UnitRecord g = ref s.Own[parent];
            if ((g.Flags & UnitFlags.IsGroup) == 0 || (g.Flags & UnitFlags.Active) == 0) return false;
            if ((g.Flags & (UnitFlags.PlayerOwned | UnitFlags.Feud | UnitFlags.Routed)) != 0) return false;
            if (g.RegimentPaths > 0 || g.QueuedOrders > 0 || g.AiStanceOrdered == ChargeDoctrine.AssaultStance) return false;
            if (parent < p.PostureMoved.Length && p.PostureMoved[parent]) return false;
            return !(NearestVisibleDistance(s, i, out float d) && d <= st.ObservationStandOff);
        }

        /// <summary>
        /// Never toward a visible superior enemy: refused when a visible enemy at least
        /// as strong lies within the stand-off of the point or within the corridor of
        /// the straight path to it.
        /// </summary>
        public static bool StandPointSafe(BattleSnapshot s, float fromX, float fromZ, float x, float z, float ownStrength, in ReconSettings st)
        {
            if (s == null) return false;
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible || obs[k].IsReinforcement) continue;
                if (obs[k].Strength < ownStrength) continue;
                if (Distance(obs[k].X, obs[k].Z, x, z) <= st.ObservationStandOff) return false;
                if (DistanceToSegment(obs[k].X, obs[k].Z, fromX, fromZ, x, z) <= st.CorridorMeters) return false;
            }
            return true;
        }

        // StandPointSafe over this tick's visible observations (CollectVisible): the same
        // observations in the same order, so the same answer.
        private static bool StandPointSafe(BattleSnapshot s, ReconMemory m, float fromX, float fromZ, float x, float z, float ownStrength, in ReconSettings st)
        {
            EnemyObservation[] obs = s.Observations;
            for (int n = 0; n < m.VisibleCount; n++)
            {
                int k = m.Visible[n];
                if (obs[k].Strength < ownStrength) continue;
                if (Distance(obs[k].X, obs[k].Z, x, z) <= st.ObservationStandOff) return false;
                if (DistanceToSegment(obs[k].X, obs[k].Z, fromX, fromZ, x, z) <= st.CorridorMeters) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- probe

        private static void ProbeTick(BattleSnapshot s, in ReconSettings st, ReconPicture p, ReconMemory m, IIntentSink sink,
            double now, ref ReconTickResult r)
        {
            TacticalFrontAxis axis = s.FrontAxis;
            bool active = p.Phase == BattlePhase.Probe && !p.SideDefending && axis.IsValid && axis.BandCount > 0;
            int bands = active ? Math.Min(axis.BandCount, ReconMemory.MaxBands) : 0;

            // Holds first (a hold made below is checked from the next tick, when the
            // posture task is no longer the probe rewrite): released when the ledger
            // reassigned the group, the group is gone, or a visible enemy at least as
            // strong is within infantry sight.
            for (int k = 0; k < m.HoldSlots; k++)
            {
                int id = m.HoldGroup(k);
                if (id == 0) continue;
                int idx = s.IndexOfOwn(id);
                if (idx < 0 || idx >= p.PostureTask.Length || p.PostureTask[idx] != m.HoldTask(k) ||
                    SuperiorWithin(s, s.Own[idx], st.InfantrySight))
                    m.ClearHold(k);
            }

            for (int b = 0; b < ReconMemory.MaxBands; b++)
            {
                int id = m.ProbeGroup(b);
                if (id == 0) continue;
                int idx = s.IndexOfOwn(id);
                if (!active || b >= bands || idx < 0)
                {
                    m.ClearProbe(b);
                    r.Events |= ReconEvents.ProbeEnded;
                    continue;
                }
                ref UnitRecord rec = ref s.Own[idx];
                bool contact = NearestVisible(s, rec.X, rec.Z, out float d, out _) && d <= st.InfantrySight;
                bool arrived = Distance(rec.X, rec.Z, m.ProbeX(b), m.ProbeZ(b)) <= OrderArbiter.DedupeMeters;
                bool timedOut = now - m.ProbeStarted(b) > ReconSettings.MaxProbeHours;
                if (!contact && !arrived && !timedOut) continue;

                // The probe is over. Contact: stop (a screen-stance group walks on into
                // contact unless told, 117253). A visible enemy at least as strong sends
                // the group back to its ledger task; otherwise it holds the ground gained.
                if (contact && (rec.RegimentPaths > 0 || (idx < m.Moving.Length && m.Moving[idx] > 0)))
                {
                    sink.Submit(Stop(id, ProbeStopReason));
                    r.Intents++;
                }
                if (!SuperiorWithin(s, rec, st.InfantrySight) && m.Hold(b, rec.X, rec.Z))
                    r.Events |= ReconEvents.ProbeHeld;
                m.ClearProbe(b);
                r.Events |= ReconEvents.ProbeEnded;
            }

            for (int b = 0; b < bands; b++)
            {
                if (m.ProbeGroup(b) != 0 || now - m.ProbeLastStart(b) < ReconSettings.ProbeRepeatHours) continue;
                if (BandHasVisibleEnemy(s, axis, b)) continue;
                int best = -1;
                float bestForward = float.MinValue;
                float bx = 0f, bz = 0f;
                for (int i = 0; i < s.OwnCount; i++)
                {
                    if (!IsProbeCandidate(s, i, p, m)) continue;
                    ref UnitRecord rec = ref s.Own[i];
                    if (axis.SectorOf(rec.X, rec.Z) != b) continue;
                    float forward = (rec.X - axis.OriginX) * axis.ForwardX + (rec.Z - axis.OriginZ) * axis.ForwardZ;
                    bool better = best < 0 || rec.UnitTyp < s.Own[best].UnitTyp ||
                        (rec.UnitTyp == s.Own[best].UnitTyp && forward > bestForward);
                    if (!better) continue;
                    if (!LookPoint(s, i, st, p, out float x, out float z)) continue;
                    best = i;
                    bestForward = forward;
                    bx = x;
                    bz = z;
                }
                if (best < 0) continue;
                ref UnitRecord pick = ref s.Own[best];
                CommandTaskType ledgerTask = best < p.PostureTask.Length ? p.PostureTask[best] : CommandTaskType.None;
                m.Nominate(b, pick.InstanceId, pick.X, pick.Z, bx, bz, ledgerTask, now);
                r.Events |= ReconEvents.ProbeNominated;
            }
        }

        // A visible enemy at least as strong as the group within the given distance.
        private static bool SuperiorWithin(BattleSnapshot s, in UnitRecord rec, float meters)
        {
            float strength = Math.Max(1f, Math.Max(rec.Strength, rec.GroupStrengthAiGroup));
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible || obs[k].IsReinforcement) continue;
                if (obs[k].Strength < strength) continue;
                if (Distance(obs[k].X, obs[k].Z, rec.X, rec.Z) <= meters) return true;
            }
            return false;
        }

        /// <summary>
        /// A held group's posture move is dropped when it would walk the ground the
        /// probe gained back: a target more than the arbiter's dedupe radius behind the
        /// held point along the probe's advance. Forward and along-the-line targets
        /// go; a break-off, a fallback or a recovery keeps its own target.
        /// </summary>
        public static bool HoldBlocks(PostureExecutionTarget kind, float holdX, float holdZ, float dirX, float dirZ, float targetX, float targetZ)
        {
            if (kind == PostureExecutionTarget.DoctrineFallbackTarget || kind == PostureExecutionTarget.FallbackLine ||
                kind == PostureExecutionTarget.RecoveryPath || kind == PostureExecutionTarget.None)
                return false;
            float along = (targetX - holdX) * dirX + (targetZ - holdZ) * dirZ;
            return along < -OrderArbiter.DedupeMeters;
        }

        private static bool IsProbeCandidate(BattleSnapshot s, int i, ReconPicture p, ReconMemory m)
        {
            ref UnitRecord rec = ref s.Own[i];
            UnitFlags f = rec.Flags;
            if ((f & UnitFlags.IsGroup) == 0 || (f & UnitFlags.Active) == 0) return false;
            if (i >= p.AiPool.Length || !p.AiPool[i]) return false;
            if ((f & (UnitFlags.Linked | UnitFlags.Routed | UnitFlags.MarkedForRout | UnitFlags.PlayerOwned | UnitFlags.Feud)) != 0) return false;
            UnitFlags below = i < m.Descendants.Length ? m.Descendants[i] : UnitFlags.None;
            if ((below & UnitFlags.PlayerOwned) != 0) return false;
            if (((f | below) & (UnitFlags.IsInfantry | UnitFlags.IsCavalry)) == 0) return false;
            if (rec.RegimentPaths > 0 || rec.QueuedOrders > 0 || rec.AiStanceOrdered == ChargeDoctrine.AssaultStance) return false;
            return !m.IsNominated(rec.InstanceId);
        }

        private static bool BandHasVisibleEnemy(BattleSnapshot s, in TacticalFrontAxis axis, int band)
        {
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible || obs[k].IsReinforcement) continue;
                if (axis.SectorOf(obs[k].X, obs[k].Z) == band) return true;
            }
            return false;
        }

        /// <summary>
        /// Where a Scout or Probe task looks: one infantry sight depth past the group,
        /// along the side's front axis (toward the objective, and no farther than it,
        /// while the axis has no target), never toward a visible superior enemy.
        /// </summary>
        public static bool LookPoint(BattleSnapshot s, int i, in ReconSettings st, ReconPicture p, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            if (s == null || i < 0 || i >= s.OwnCount) return false;
            ref UnitRecord rec = ref s.Own[i];
            if (!AdvanceDirection(s, p, rec.X, rec.Z, out float dx, out float dz, out float reach)) return false;
            float advance = Math.Min(st.ProbeAdvanceMeters, reach);
            x = rec.X + dx * advance;
            z = rec.Z + dz * advance;
            if (!PostureSense.IsMoveAway(rec, x, z)) return false;
            // BC14W: a look never points into held works or the band in front of them (a probe nominated there would walk the group in).
            if (p.Works != null && (p.Works.OnHeldWorks(x, z) || p.Works.InWorksBand(x, z))) return false;
            float strength = Math.Max(1f, Math.Max(rec.Strength, rec.GroupStrengthAiGroup));
            return StandPointSafe(s, rec.X, rec.Z, x, z, strength, st);
        }

        /// <summary>
        /// A Scout or Probe task's forward targets (the doctrine primary target, the
        /// objective approach) are the look point; a break-off (DoctrineFallbackTarget,
        /// ShouldBreakOffRecon) or a recovery path keeps the posture decision's own target.
        /// </summary>
        public static bool UsesLookPoint(CommandTaskType task, PostureExecutionTarget kind)
        {
            if (task != CommandTaskType.Scout && task != CommandTaskType.Probe) return false;
            return kind == PostureExecutionTarget.DoctrinePrimaryTarget || kind == PostureExecutionTarget.ObjectiveApproach;
        }

        /// <summary>
        /// The direction the side looks in from a point: the snapshot front axis once it
        /// has a target (a seen enemy), else toward the operation's objective
        /// (<paramref name="reach"/> = the distance to it). False with neither.
        /// </summary>
        public static bool AdvanceDirection(BattleSnapshot s, ReconPicture p, float fromX, float fromZ, out float dx, out float dz, out float reach)
        {
            dx = 0f;
            dz = 0f;
            reach = float.MaxValue;
            TacticalFrontAxis axis = s.FrontAxis;
            if (axis.IsValid && axis.TargetDistance > 0f)
            {
                dx = axis.ForwardX;
                dz = axis.ForwardZ;
                return true;
            }
            if (p == null || !p.HasObjective) return false;
            float d = Distance(fromX, fromZ, p.ObjectiveX, p.ObjectiveZ);
            if (d < 1f) return false;
            dx = (p.ObjectiveX - fromX) / d;
            dz = (p.ObjectiveZ - fromZ) / d;
            reach = d;
            return true;
        }

        // ---------------------------------------------------------------- facing, follow, skirmishers

        /// <summary>
        /// The bearing from own unit <paramref name="i"/> to the stalest stale enemy-side
        /// entry point (never watched first, nearest among those; else the one watched
        /// longest ago). False when every entry point was watched within the hour.
        /// </summary>
        public static bool TryEntryBearing(BattleSnapshot s, int i, ReconPicture p, out float heading)
        {
            heading = -1f;
            if (s == null || p == null || i < 0 || i >= s.OwnCount) return false;
            double now = s.DoctrineHours;
            ref UnitRecord rec = ref s.Own[i];
            int best = -1;
            double bestWatched = double.MaxValue;
            float bestDistance = float.MaxValue;
            for (int k = 0; k < p.EntryCount; k++)
            {
                if (!p.EntryStale(k, now)) continue;
                double w = p.EntryWatchedHours(k) < 0.0 ? double.NegativeInfinity : p.EntryWatchedHours(k);
                float d = Distance(rec.X, rec.Z, p.EntryX(k), p.EntryZ(k));
                if (w > bestWatched || (w == bestWatched && d >= bestDistance)) continue;
                best = k;
                bestWatched = w;
                bestDistance = d;
            }
            if (best < 0) return false;
            heading = PostureSense.HeadingTo(rec.X, rec.Z, p.EntryX(best), p.EntryZ(best));
            return heading >= 0f;
        }

        /// <summary>A regiment in square (formation 4, 118174); a group's 4 is march column.</summary>
        public static bool IsSquare(int unitTyp, int formation)
        {
            return unitTyp <= TacticalUnitType.MaxCombat && formation == SquareFormation;
        }

        /// <summary>An officer for the cavalry raid filter: a command group (HQ) or a courier.</summary>
        public static bool IsOfficerTarget(int unitTyp)
        {
            return unitTyp > TacticalUnitType.MaxCombat || unitTyp == TacticalUnitType.Courier;
        }

        /// <summary>
        /// Extra metres SkirmisherScreenDistancePatch moves an AI skirmisher company's front link target forward,
        /// or 0 for vanilla's own (unitlinkdistance + skirmisherdistance). Only for a
        /// detached skirmisher company whose command group is in screen stance, or in
        /// attack stance while it moves, with nothing in its own range list
        /// (enemyinrangereg empty, so vanilla's re-link is running, 124467), not in melee,
        /// and no visible enemy within the cancel distance plus the push.
        /// </summary>
        public static float SkirmisherPushMeters(BattleSnapshot s, int i, float vanillaLinkMeters, float pushMeters, float cancelMeters)
        {
            if (s == null || i < 0 || i >= s.OwnCount) return 0f;
            ref UnitRecord rec = ref s.Own[i];
            if (rec.UnitTyp != TacticalUnitType.Skirmisher || (rec.Flags & UnitFlags.DetachedCompany) == 0) return 0f;
            if ((rec.Flags & UnitFlags.Active) == 0) return 0f;
            if ((rec.Flags & (UnitFlags.PlayerOwned | UnitFlags.Feud | UnitFlags.InMelee | UnitFlags.Routed | UnitFlags.MarkedForRout)) != 0) return 0f;
            if (rec.FirstEnemyInRangeId != 0) return 0f;
            int parent = rec.ParentIndex;
            if (parent < 0 || parent >= s.OwnCount) return 0f;
            ref UnitRecord g = ref s.Own[parent];
            bool screening = g.AiStance == TacticalCommandTaskToStanceMapper.StanceScreen;
            bool approaching = g.AiStance == TacticalCommandTaskToStanceMapper.StanceAttack && g.RegimentPaths > 0;
            if (!screening && !approaching) return 0f;
            if (NearestVisibleDistance(s, i, out float d) && d <= cancelMeters + pushMeters) return 0f;
            float extra = pushMeters - vanillaLinkMeters;
            return extra > 0f ? extra : 0f;
        }

        // ---------------------------------------------------------------- geometry

        /// <summary>World point of a ledger lateral (x·right + z·right of the frame heading) at a forward depth.</summary>
        public static void BandWorldPoint(float lateral, float headingDegrees, float forwardDepth, out float x, out float z)
        {
            double h = headingDegrees * Math.PI / 180.0;
            float fx = (float)Math.Sin(h), fz = (float)Math.Cos(h);
            // TacticalFrontAxis: right = (forwardZ, -forwardX).
            float rx = fz, rz = -fx;
            x = rx * lateral + fx * forwardDepth;
            z = rz * lateral + fz * forwardDepth;
        }

        // The own combat regiments' centroid, their mean forward depth in the ledger
        // frame (world-anchored) and along the snapshot axis (from its origin).
        private static bool OwnLine(BattleSnapshot s, float frameHeading, out float cx, out float cz, out float frameDepth, out float axisDepth)
        {
            cx = cz = frameDepth = axisDepth = 0f;
            double h = (float.IsNaN(frameHeading) ? 0f : frameHeading) * Math.PI / 180.0;
            float fx = (float)Math.Sin(h), fz = (float)Math.Cos(h);
            TacticalFrontAxis axis = s.FrontAxis;
            int n = 0;
            for (int i = 0; i < s.OwnCount; i++)
            {
                ref UnitRecord u = ref s.Own[i];
                if (u.UnitTyp > TacticalUnitType.MaxCombat || u.UnitTyp == TacticalUnitType.Courier) continue;
                if ((u.Flags & (UnitFlags.Alive | UnitFlags.Active)) != (UnitFlags.Alive | UnitFlags.Active)) continue;
                if ((u.Flags & (UnitFlags.Routed | UnitFlags.DetachedCompany)) != 0) continue;
                cx += u.X;
                cz += u.Z;
                frameDepth += u.X * fx + u.Z * fz;
                if (axis.IsValid) axisDepth += (u.X - axis.OriginX) * axis.ForwardX + (u.Z - axis.OriginZ) * axis.ForwardZ;
                n++;
            }
            if (n == 0) return false;
            cx /= n;
            cz /= n;
            frameDepth /= n;
            axisDepth /= n;
            return true;
        }

        // Distance from own unit i to the nearest visible enemy: the snapshot's precomputed
        // closest-visible row (SlicePressureBuilder: the same minimum over the same
        // observations) while it is current, else the scan.
        private static bool NearestVisibleDistance(BattleSnapshot s, int i, out float distance)
        {
            if (s.PressureCurrent) return s.ClosestVisibleEnemy(i, out distance) >= 0;
            return NearestVisible(s, s.Own[i].X, s.Own[i].Z, out distance, out _);
        }

        private static bool NearestVisible(BattleSnapshot s, float x, float z, out float distance, out float strength)
        {
            distance = float.MaxValue;
            strength = 0f;
            bool any = false;
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible || obs[k].IsReinforcement) continue;
                float d = Distance(obs[k].X, obs[k].Z, x, z);
                if (d >= distance) continue;
                distance = d;
                strength = obs[k].Strength;
                any = true;
            }
            return any;
        }

        private static void End(ReconMemory m, ref ReconTickResult r, string reason)
        {
            m.End();
            r.Events |= ReconEvents.MissionEnded;
            r.EndReason = reason;
        }

        private static BattleOrderIntent Move(int id, float x, float z, string reason)
        {
            return new BattleOrderIntent
            {
                GroupId = id, Channel = OrderChannel.Move, TargetX = x, TargetZ = z, Heading = -1f,
                Priority = IntentPriority.Support, Source = IntentSource.Recon, Flags = IntentFlags.UseOrderDelay,
                Task = CommandTaskType.Scout, Reason = reason,
            };
        }

        private static BattleOrderIntent Stop(int id, string reason)
        {
            return new BattleOrderIntent
            {
                GroupId = id, Channel = OrderChannel.Stop, Heading = -1f,
                Priority = IntentPriority.Support, Source = IntentSource.Recon, Task = CommandTaskType.Scout, Reason = reason,
            };
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static float DistanceToSegment(float px, float pz, float ax, float az, float bx, float bz)
        {
            float vx = bx - ax, vz = bz - az;
            float len2 = vx * vx + vz * vz;
            if (len2 < 0.001f) return Distance(px, pz, ax, az);
            float u = ((px - ax) * vx + (pz - az) * vz) / len2;
            if (u < 0f) u = 0f;
            if (u > 1f) u = 1f;
            return Distance(px, pz, ax + u * vx, az + u * vz);
        }
    }
}
