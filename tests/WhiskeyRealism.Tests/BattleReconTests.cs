using System;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// BC9: reconnaissance and probing (plan § "BC9 — reconnaissance and probing").
// The pure recon planner over a BattleSnapshot fixture, the contact ledger's
// where-to-look reads, the skirmisher screen push and the doctrine fixes the slice
// folds in (director no-contact scouting, GT recon, cavalry follow, facing Entry).
internal static class BattleReconTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("recon scout is a standing AI cavalry regiment", ScoutIsStandingAiCavalryRegiment),
            ("recon scout never a company, player-owned, feuding or shaken regiment", ScoutFilterRefusals),
            ("recon scout's brigade must be settled and not moved by posture this tick", ScoutParentMustBeSettled),
            ("recon stale entry point beats stale band and forward", StaleEntryPointPickedFirst),
            ("recon stale band when every entry point is watched", StaleBandPickedNext),
            ("recon forward only before any contact", ForwardOnlyBeforeContact),
            ("recon no mission above the gate without stale ground", NoMissionAboveGateWithoutStaleGround),
            ("recon stand point refused near a visible superior enemy", StandPointRefusedNearSuperiorEnemy),
            ("recon stand point refused when the path passes a superior enemy", StandPointRefusedOnCorridor),
            ("recon weaker visible enemy does not block the stand point", WeakerEnemyDoesNotBlock),
            ("recon mission starts bounded per doctrine hour", MissionStartsBoundedPerHour),
            ("recon repeat target suppressed within the hour", RepeatTargetSuppressed),
            ("recon mission ends when the executor did not issue the move", MissionEndsWithoutExecutorIssue),
            ("recon mission continues on an executor issued move", MissionContinuesOnIssuedMove),
            ("recon outbound stops and observes on first sight", OutboundStopsOnFirstSight),
            ("recon superior contact sends the scout back", SuperiorContactReturns),
            ("recon observe dwells then returns to the brigade", ObserveDwellsThenReturns),
            ("recon recalled when another source moves the scout", RecalledByAnotherSource),
            ("recon mission times out", MissionTimesOut),
            ("probe nominates one group per empty band in the probe phase", ProbeNominatesPerEmptyBand),
            ("probe never for a defender or outside the probe phase", ProbeNeverForDefenderOrOtherPhase),
            ("probe skips linked groups and player subtrees", ProbeSkipsLinkedAndPlayerSubtree),
            ("probe one start per band per doctrine hour", ProbeBoundedPerBandPerHour),
            ("probe ends on visible contact with one stop", ProbeEndsOnContactWithStop),
            ("look point is one infantry sight depth along the axis", LookPointIsOneSightDepth),
            ("look point refused toward a visible superior enemy", LookPointRefusedTowardSuperior),
            ("look point toward the objective, capped at it, before the axis has a target", LookPointTowardObjectiveBeforeContact),
            ("recon planner allocates nothing after warm-up", PlanAllocatesNothingAfterWarmup),
            ("ledger entry point stamps keep the last watched hour", LedgerEntryStampsKeepLastWatched),
            ("ledger stalest band is the lowest recency band", LedgerStalestBand),
            ("skirmisher push in screen stance out of contact", SkirmisherPushInScreen),
            ("skirmisher push off in contact, defend, player-owned or for a non-company", SkirmisherPushRefusals),
            ("skirmisher push while an attack-stance group moves", SkirmisherPushWhileAttackMoves),
            ("director: no enemy contact selects scouting, never a commit", DirectorNoContactScouts),
            ("doctrine: scouting without contact gives probe and screen tasks", ScoutingWithoutContactTasks),
            ("doctrine: a defensive network is not sent probing", DefensiveNetworkNotProbing),
            ("gt recon: cavalry in the subtree no longer turns a group into a scout", GtReconNoCavalryScoutBranch),
            ("cavalry follow: a target in square or a command group is no raid target", CavalryFollowSquareAndOfficer),
            ("facing: an unwatched entry point faces a screen, never urgently", FacingEntryIsNotUrgent),
            ("facing: the stalest unwatched entry point is the screen's bearing", EntryBearingPicksStalest),
            ("look points replace only the forward targets of a scout or probe", LookPointOnlyForForwardTargets),
            ("probe stop is not swallowed while the group's regiments still walk", ProbeStopReachesWalkingRegiments),
            ("recon looks at the nearest remembered enemy when confidence fell after contact", RememberedEnemyTargetAfterContact),
            ("recon own rotated retry is not taken for a recall", RetryIsNotARecall),
            ("director: a defender without contact screens, never probes", DirectorDefenderScreens),
            ("director: meeting and attacker stances still scout by probes", DirectorMeetingAndAttackerProbe),
            ("gt recon: a defender's unseen front is screened, not probed", GtReconDefenderScreens),
            ("recon: a defender still scouts with its cavalry", DefenderStillScouts),
            ("probe hold: weaker contact holds the ground gained", ProbeHoldsAfterWeakerContact),
            ("probe hold: superior contact falls back to the ledger task", ProbeSuperiorContactNoHold),
            ("probe hold: arrival holds the probe point", ProbeHoldsOnArrival),
            ("probe hold: released when the ledger reassigns the group", ProbeHoldReleasedOnReassign),
            ("probe hold: blocks only moves back over the ground gained", ProbeHoldBlocksOnlyBackward),
            ("probe hold: a new nomination replaces the hold", ProbeHoldReplacedByNomination),
            ("defender: the stance guard turns probe and attack tasks into screen and hold", StanceGuardTable),
            ("defender: never committed to an offensive shape", DefenderNeverCommitsOffensively),
            ("defender: a commit that is downgraded leaves no probe or attack task", DefenderDowngradeNoProbe),
            ("defender: an offensive operation held from before is neutralised", DefenderLegacyOffensiveOperation),
            ("defender: doctrine assignment guards every shape", DefenderGuardIndependentOfShape),
            ("gt recon: a defender with a visible enemy gets no probe or attack task", GtReconDefenderVisibleEnemy),
            ("defender: the army's stance reaches the ledger without the opportunity doctrine", DefenderStanceReachesLedger),
            ("defender: the stance is read before the opportunity doctrine flag", StanceReadBeforeOpportunityFlag),
            ("facing: the entry source turns only screens, probes and flank guards", EntryFacingOnlyWatchingTasks),
            ("facing: a regiment follows its screen's entry bearing out of range", RegimentFollowsEntryBearing),
            ("facing: the brigade pulse submits a screen's entry turn, never a hold's", BrigadePulseEntrySubmits),
            ("recon never rides to a visible enemy as a remembered one", RememberedTargetIsNeverVisible),
            ("probe a band holding only remembered enemies", ProbeBandWithOnlyRememberedEnemy),
            ("probe hold: a new hold never overwrites another group's", ProbeHoldNeverOverwrites),
            ("defender: ledger node tasks are guarded where they are planned", DefenderNodeTasksGuarded),
            ("defender: #45 ledger stance never attack, a weak point included", DefenderLedgerStanceCapped),
            ("defender: the posture stance guard rewrites state and order", PostureStanceGuardRewrites),
            ("defender: the posture pipeline and the leaf cascade call the stance guard", PostureGuardCallSitesPinned),
            ("defender: one stance reader for the division play", DivisionPlayReadsArmyStance),
            ("defender: #45's default sector scorer never returns attack stance", DefaultScorerDefenderCapped),
        });
    }

    // ------------------------------------------------------------------ fixture

    private const int CavBrigade = 100, CavA = 101, CavB = 102, Company = 103;
    private const int InfBrigade = 200, InfA = 201, InfDivision = 300;
    private const int EnemyId = 900;
    private static long _tick;

    private static ReconSettings Settings(bool enabled = true) =>
        new ReconSettings(enabled, cavalrySight: 250f, infantrySight: 200f, standOff: 150f, interruptMeters: 100f, minMorale: 0.5f);

    // Own line on z = -1000 facing +z; cavalry brigade at x = 0 (regiments at -40 / +40),
    // infantry brigade at x = 400 under a division at x = 200. Front axis forward +z.
    private static BattleSnapshot Field(double hours, Action<BattleSnapshot> extra = null)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, ++_tick, hours);
        s.SetSliceCount(20);
        AddGroup(s, InfDivision, TacticalUnitType.BattleGroupDivision, 200f);
        AddGroup(s, CavBrigade, TacticalUnitType.BattleGroupBrigade, 0f);
        AddUnit(s, CavA, TacticalUnitType.Cavalry, -40f, UnitFlags.IsCavalry);
        AddUnit(s, CavB, TacticalUnitType.Cavalry, 40f, UnitFlags.IsCavalry);
        AddGroup(s, InfBrigade, TacticalUnitType.BattleGroupBrigade, 400f);
        AddUnit(s, InfA, TacticalUnitType.Infantry, 400f, UnitFlags.IsInfantry);
        extra?.Invoke(s);
        for (int i = 0; i < s.OwnCount; i++)
        {
            int id = s.Own[i].InstanceId;
            if (id == CavA || id == CavB || id == Company) s.Own[i].ParentIndex = s.IndexOfOwn(CavBrigade);
            else if (id == InfA) s.Own[i].ParentIndex = s.IndexOfOwn(InfBrigade);
            else if (id == InfBrigade) s.Own[i].ParentIndex = s.IndexOfOwn(InfDivision);
        }
        s.IndexCommandTree(null);
        s.SetFrontAxis(TacticalFrontAxis.FromCentroids(150f, -1000f, 150f, 0f).WithOwnExtent(-200f, 300f));
        return s;
    }

    private static void AddGroup(BattleSnapshot s, int id, int typ, float x)
    {
        s.AddOwn(new UnitRecord
        {
            InstanceId = id, UnitTyp = typ, X = x, Z = -1000f, AiStance = 1, AiStanceOrdered = 1,
            Strength = 1200, GroupStrengthAiGroup = 1200, Morale = 0.9f,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup,
        });
    }

    private static void AddUnit(BattleSnapshot s, int id, int typ, float x, UnitFlags kind)
    {
        s.AddOwn(new UnitRecord
        {
            InstanceId = id, UnitTyp = typ, X = x, Z = -1000f, Strength = 400, Morale = 0.9f, RotationY = 0f,
            Flags = UnitFlags.Alive | UnitFlags.Active | kind,
        });
    }

    private static void SeeEnemy(BattleSnapshot s, float x, float z, float strength, double hours)
    {
        s.AddObservation(new EnemyObservation
        {
            InstanceId = EnemyId + s.ObservationCount, UnitTyp = TacticalUnitType.Infantry, X = x, Z = z, Strength = strength,
            State = ObservationState.Visible, Confidence01 = 1f, LastSeenHours = hours,
        });
    }

    private static ref UnitRecord Own(BattleSnapshot s, int id) => ref s.Own[s.IndexOfOwn(id)];

    private static ReconPicture Picture(BattleSnapshot s, float confidence = 0.1f, bool hadContact = true,
        BattlePhase phase = BattlePhase.MainEffort, bool defending = false, CommandTaskType postureTask = CommandTaskType.FormUp)
    {
        var p = new ReconPicture();
        p.BeginTick(s.OwnCount);
        p.IntelConfidence = confidence;
        p.HadContact = hadContact;
        p.Phase = phase;
        p.SideDefending = defending;
        for (int i = 0; i < s.OwnCount; i++)
        {
            if ((s.Own[i].Flags & UnitFlags.IsGroup) == 0 || s.Own[i].InstanceId == InfDivision) continue;
            p.AiPool[i] = true;
            p.PostureTask[i] = postureTask;
        }
        return p;
    }

    private sealed class Sink : IIntentSink
    {
        public readonly BattleOrderIntent[] Items = new BattleOrderIntent[32];
        public int Count;
        public void Submit(in BattleOrderIntent intent) { if (Count < Items.Length) Items[Count++] = intent; }
        public void Clear() { Count = 0; }

        public bool TryFind(int id, OrderChannel channel, out BattleOrderIntent intent)
        {
            for (int i = 0; i < Count; i++)
                if (Items[i].GroupId == id && Items[i].Channel == channel) { intent = Items[i]; return true; }
            intent = default(BattleOrderIntent);
            return false;
        }

        public int CountOf(OrderChannel channel)
        {
            int n = 0;
            for (int i = 0; i < Count; i++) if (Items[i].Channel == channel) n++;
            return n;
        }
    }

    private sealed class Rig
    {
        public readonly ReconMemory Memory = new ReconMemory();
        public readonly OrderExecutor Executor = new OrderExecutor();
        public readonly Sink Sink = new Sink();
        public ReconTickResult Last;

        public ReconTickResult Plan(BattleSnapshot s, ReconPicture p, ReconSettings? settings = null)
        {
            Sink.Clear();
            Last = BattleRecon.Plan(s, settings ?? Settings(), p, Memory, Executor, Sink);
            return Last;
        }

        // What the executor records when it issues an intent (RecordIssued: LastOutcome Issued).
        public void Issue(in BattleOrderIntent intent, double hours)
        {
            Executor.RecordIssued(intent, hours, Array.Empty<IssuedTarget>(), 0, Array.Empty<UnitWaypoint>(), 0, 0, 1);
        }

        public void IssueAll(double hours)
        {
            for (int i = 0; i < Sink.Count; i++) Issue(Sink.Items[i], hours);
        }
    }

    private static ReconPicture WithEntry(ReconPicture p, float x, float z, double watchedHours)
    {
        p.AddEntry(x, z, watchedHours);
        return p;
    }

    // ------------------------------------------------------------------ scout

    private static void ScoutIsStandingAiCavalryRegiment()
    {
        var s = Field(1.0);
        var p = Picture(s);
        AssertTrue(BattleRecon.IsScoutCandidate(s, s.IndexOfOwn(CavA), Settings(), p), "standing cavalry regiment");
        AssertFalse(BattleRecon.IsScoutCandidate(s, s.IndexOfOwn(InfA), Settings(), p), "infantry is no scout");
        AssertFalse(BattleRecon.IsScoutCandidate(s, s.IndexOfOwn(CavBrigade), Settings(), p), "a group is no scout");
        Own(s, CavA).RegimentPaths = 1;
        AssertFalse(BattleRecon.IsScoutCandidate(s, s.IndexOfOwn(CavA), Settings(), p), "a moving regiment is busy");
        Own(s, CavA).RegimentPaths = 0;
        Own(s, CavA).QueuedOrders = 1;
        AssertFalse(BattleRecon.IsScoutCandidate(s, s.IndexOfOwn(CavA), Settings(), p), "an order in flight");
    }

    private static void ScoutFilterRefusals()
    {
        var s = Field(1.0, f => AddUnit(f, Company, TacticalUnitType.Cavalry, 0f, UnitFlags.IsCavalry | UnitFlags.DetachedCompany));
        var p = Picture(s);
        AssertFalse(BattleRecon.IsScoutCandidate(s, s.IndexOfOwn(Company), Settings(), p), "a detached company is vanilla's");
        int a = s.IndexOfOwn(CavA);
        UnitFlags[] refuse = { UnitFlags.PlayerOwned, UnitFlags.Feud, UnitFlags.Routed, UnitFlags.MarkedForRout, UnitFlags.OnRetreat,
            UnitFlags.InMelee, UnitFlags.Garrisoned, UnitFlags.PermanentlyDetached };
        foreach (UnitFlags flag in refuse)
        {
            s.Own[a].Flags |= flag;
            AssertFalse(BattleRecon.IsScoutCandidate(s, a, Settings(), p), "refused: " + flag);
            s.Own[a].Flags &= ~flag;
        }
        s.Own[a].Morale = 0.4f;
        AssertFalse(BattleRecon.IsScoutCandidate(s, a, Settings(), p), "below vanilla's morale bar to send men out");
        s.Own[a].Morale = 0.9f;
        SeeEnemy(s, -40f, -880f, 100f, 1.0);
        AssertFalse(BattleRecon.IsScoutCandidate(s, a, Settings(), p), "a visible enemy within the stand-off");
    }

    private static void ScoutParentMustBeSettled()
    {
        var s = Field(1.0);
        var p = Picture(s);
        int a = s.IndexOfOwn(CavA);
        int brigade = s.IndexOfOwn(CavBrigade);
        p.PostureMoved[brigade] = true;
        AssertFalse(BattleRecon.IsScoutCandidate(s, a, Settings(), p), "brigade got a posture move this tick");
        p.PostureMoved[brigade] = false;
        s.Own[brigade].RegimentPaths = 1;
        AssertFalse(BattleRecon.IsScoutCandidate(s, a, Settings(), p), "brigade moving");
        s.Own[brigade].RegimentPaths = 0;
        s.Own[brigade].QueuedOrders = 1;
        AssertFalse(BattleRecon.IsScoutCandidate(s, a, Settings(), p), "brigade has an order in flight");
        s.Own[brigade].QueuedOrders = 0;
        s.Own[brigade].AiStanceOrdered = 4;
        AssertFalse(BattleRecon.IsScoutCandidate(s, a, Settings(), p), "brigade ordered to assault");
        s.Own[brigade].AiStanceOrdered = 1;
        s.Own[brigade].Flags |= UnitFlags.PlayerOwned;
        AssertFalse(BattleRecon.IsScoutCandidate(s, a, Settings(), p), "brigade is the player's");
    }

    // ------------------------------------------------------------------ targets

    private static void StaleEntryPointPickedFirst()
    {
        var s = Field(3.0);
        var p = WithEntry(Picture(s), 0f, -200f, -1.0);
        p.HasStaleBand = true; p.StaleBandLateral = -150f; p.StaleBandRecency = 0.1f; p.FrameHeading = 0f;
        var rig = new Rig();
        rig.Plan(s, p);
        AssertEqual(ReconTargetKind.EntryPoint, rig.Memory.Kind, "entry point first");
        AssertTrue(rig.Sink.TryFind(CavA, OrderChannel.Move, out BattleOrderIntent move) || rig.Sink.TryFind(CavB, OrderChannel.Move, out move), "a scout moves");
        AssertNear(0f, move.TargetX, 60f, "toward the entry point");
        AssertNear(-350f, move.TargetZ, 1f, "stand-off 150 m short of it on the line from the scout");
        AssertEqual(IntentPriority.Support, move.Priority, "support priority");
        AssertEqual(IntentSource.Recon, move.Source, "recon source");
        AssertEqual(CommandTaskType.Scout, move.Task, "scout task");
        AssertTrue((move.Flags & IntentFlags.UseOrderDelay) != 0, "by courier like vanilla AI moves");
        AssertTrue((rig.Last.Events & ReconEvents.MissionStarted) != 0, "mission started event");
    }

    private static void StaleBandPickedNext()
    {
        var s = Field(3.0);
        var p = WithEntry(Picture(s), 0f, -200f, 2.9); // watched 0.1 h ago
        p.HasStaleBand = true; p.StaleBandLateral = -150f; p.StaleBandRecency = 0.1f; p.FrameHeading = 0f;
        var rig = new Rig();
        rig.Plan(s, p);
        AssertEqual(ReconTargetKind.StaleBand, rig.Memory.Kind, "band next");
        // Heading 0: right = (1, 0), so lateral -150 is x = -150 at the own line's depth.
        AssertNear(-150f, rig.Memory.TargetX, 0.5f, "band lateral centre");
        AssertNear(-1000f, rig.Memory.TargetZ, 0.5f, "own line depth");
        AssertTrue(rig.Sink.TryFind(CavA, OrderChannel.Move, out _), "the scout nearest the band rides");
    }

    private static void ForwardOnlyBeforeContact()
    {
        var s = Field(3.0);
        var rig = new Rig();
        rig.Plan(s, Picture(s, confidence: 0f, hadContact: false));
        AssertEqual(ReconTargetKind.Forward, rig.Memory.Kind, "forward without contact");
        AssertNear(-500f, rig.Memory.TargetZ, 0.5f, "own line depth plus two cavalry sight depths");
        var rig2 = new Rig();
        rig2.Plan(s, Picture(s, confidence: 0.1f, hadContact: true));
        AssertEqual(ReconLeg.None, rig2.Memory.Leg, "after contact, low confidence alone sends nobody forward blind");
    }

    private static void NoMissionAboveGateWithoutStaleGround()
    {
        var s = Field(3.0);
        var p = WithEntry(Picture(s, confidence: 0.6f), 0f, -200f, 2.9);
        var rig = new Rig();
        rig.Plan(s, p);
        AssertEqual(ReconLeg.None, rig.Memory.Leg, "confident and nothing stale");
        AssertEqual(0, rig.Sink.Count, "no intent");
        p.HasStaleBand = true; p.StaleBandLateral = -150f; p.StaleBandRecency = 0.2f;
        rig.Plan(s, p);
        AssertEqual(ReconTargetKind.StaleBand, rig.Memory.Kind, "stale ground is looked at even above the gate");
    }

    private static void StandPointRefusedNearSuperiorEnemy()
    {
        var s = Field(3.0, f => { });
        SeeEnemy(s, 0f, -260f, 2000f, 3.0);
        var rig = new Rig();
        rig.Plan(s, WithEntry(Picture(s), 0f, -200f, -1.0));
        AssertEqual(ReconLeg.None, rig.Memory.Leg, "superior enemy at the stand point");
        AssertFalse(BattleRecon.StandPointSafe(s, 0f, -1000f, 0f, -350f, 400f, Settings()), "unsafe stand point");
    }

    private static void StandPointRefusedOnCorridor()
    {
        var s = Field(3.0);
        SeeEnemy(s, 60f, -700f, 2000f, 3.0); // 60 m off the straight path, far from the stand point
        AssertFalse(BattleRecon.StandPointSafe(s, 0f, -1000f, 0f, -350f, 400f, Settings()), "superior enemy within 100 m of the path");
    }

    private static void WeakerEnemyDoesNotBlock()
    {
        var s = Field(3.0);
        SeeEnemy(s, 60f, -700f, 100f, 3.0);
        AssertTrue(BattleRecon.StandPointSafe(s, 0f, -1000f, 0f, -350f, 400f, Settings()), "a weaker picket does not block");
    }

    // ------------------------------------------------------------------ missions

    // Ends the running mission the way the runtime does when the executor issued
    // nothing for it (fail closed): a tick with nothing new to look at.
    private static void EndMission(Rig rig, double hours)
    {
        var s = Field(hours);
        rig.Plan(s, Picture(s, confidence: 0.6f));
        AssertEqual(ReconLeg.None, rig.Memory.Leg, "mission ended");
    }

    private static void MissionStartsBoundedPerHour()
    {
        var rig = new Rig();
        double h = 3.0;
        for (int k = 0; k < ReconSettings.MaxMissionStartsPerHour; k++)
        {
            var s = Field(h);
            rig.Plan(s, WithEntry(Picture(s), -400f + 200f * k, -200f, -1.0));
            AssertTrue(rig.Memory.Leg == ReconLeg.Outbound, "start " + k);
            EndMission(rig, h + 0.01);
            h += 0.05;
        }
        var t = Field(h);
        rig.Plan(t, WithEntry(Picture(t), 600f, -200f, -1.0));
        AssertEqual(ReconLeg.None, rig.Memory.Leg, "fifth start inside the hour refused");
        var u = Field(4.01);
        rig.Plan(u, WithEntry(Picture(u), 600f, -200f, -1.0));
        AssertEqual(ReconLeg.Outbound, rig.Memory.Leg, "allowed again an hour after the first start");
    }

    private static void RepeatTargetSuppressed()
    {
        var rig = new Rig();
        var s = Field(3.0);
        rig.Plan(s, WithEntry(Picture(s), 0f, -200f, -1.0));
        EndMission(rig, 3.01);
        var t = Field(3.2);
        rig.Plan(t, WithEntry(Picture(t), 30f, -200f, -1.0));
        AssertEqual(ReconLeg.None, rig.Memory.Leg, "same entry point inside the hour");
        var u = Field(4.1);
        rig.Plan(u, WithEntry(Picture(u), 30f, -200f, -1.0));
        AssertEqual(ReconLeg.Outbound, rig.Memory.Leg, "after the hour");
    }

    private static Rig Outbound(out int scout)
    {
        var rig = new Rig();
        var s = Field(3.0);
        rig.Plan(s, WithEntry(Picture(s), 0f, -200f, -1.0));
        scout = rig.Memory.ScoutId;
        return rig;
    }

    private static void MissionEndsWithoutExecutorIssue()
    {
        var rig = Outbound(out int scout);
        var s = Field(3.01);
        rig.Plan(s, WithEntry(Picture(s), 0f, -200f, -1.0));
        AssertEqual(ReconLeg.None, rig.Memory.Leg, "fail closed: nothing issued");
        AssertEqual("not-issued", rig.Last.EndReason, "reason");
        AssertTrue((rig.Last.Events & ReconEvents.MissionEnded) != 0, "ended event");
    }

    private static void MissionContinuesOnIssuedMove()
    {
        var rig = Outbound(out int scout);
        rig.IssueAll(3.0);
        var s = Field(3.01, f => { });
        Own(s, scout).RegimentPaths = 1;
        rig.Plan(s, WithEntry(Picture(s), 0f, -200f, -1.0));
        AssertEqual(ReconLeg.Outbound, rig.Memory.Leg, "still outbound");
        AssertTrue(rig.Memory.Confirmed, "confirmed by the executor's Issued outcome");
        AssertTrue(rig.Sink.TryFind(scout, OrderChannel.Move, out _), "move re-submitted to hold the claim");
    }

    private static void OutboundStopsOnFirstSight()
    {
        var rig = Outbound(out int scout);
        rig.IssueAll(3.0);
        var s = Field(3.02);
        Own(s, scout).Z = -600f;
        Own(s, scout).RegimentPaths = 1;
        SeeEnemy(s, Own(s, scout).X, -480f, 100f, 3.02); // 120 m ahead, weaker
        rig.Plan(s, WithEntry(Picture(s), 0f, -200f, -1.0));
        AssertEqual(ReconLeg.Observe, rig.Memory.Leg, "observe on first sight");
        AssertTrue(rig.Sink.TryFind(scout, OrderChannel.Stop, out BattleOrderIntent stop), "a stop: a screen does not stop itself");
        AssertEqual(IntentSource.Recon, stop.Source, "recon stop");
    }

    private static void SuperiorContactReturns()
    {
        var rig = Outbound(out int scout);
        rig.IssueAll(3.0);
        var s = Field(3.02);
        Own(s, scout).Z = -600f;
        Own(s, scout).RegimentPaths = 1;
        SeeEnemy(s, Own(s, scout).X, -520f, 3000f, 3.02); // 80 m, superior
        rig.Plan(s, WithEntry(Picture(s), 0f, -200f, -1.0));
        AssertEqual(ReconLeg.Return, rig.Memory.Leg, "back on superior contact");
        AssertTrue(rig.Sink.TryFind(scout, OrderChannel.Move, out BattleOrderIntent back), "return move");
        AssertTrue(back.TargetZ < -600f, "back toward the own line");
    }

    private static void ObserveDwellsThenReturns()
    {
        var rig = Outbound(out int scout);
        rig.IssueAll(3.0);
        var s = Field(3.05);
        Own(s, scout).Z = -350f; // arrived
        rig.Plan(s, WithEntry(Picture(s), 0f, -200f, -1.0));
        AssertEqual(ReconLeg.Observe, rig.Memory.Leg, "arrived: observe");
        AssertEqual(0, rig.Sink.CountOf(OrderChannel.Move), "no move while observing");
        var t = Field(3.05 + ReconSettings.ObserveDwellHours + 0.001);
        Own(t, scout).Z = -350f;
        rig.Plan(t, WithEntry(Picture(t), 0f, -200f, -1.0));
        AssertEqual(ReconLeg.Return, rig.Memory.Leg, "dwell over: return");
        AssertTrue(rig.Sink.TryFind(scout, OrderChannel.Move, out BattleOrderIntent back), "return move");
        AssertNear(0f, back.TargetX, 0.5f, "to its brigade");
        AssertNear(-1000f, back.TargetZ, 0.5f, "to its brigade");
        rig.IssueAll(t.DoctrineHours);
        var u = Field(3.2);
        Own(u, scout).Z = -980f;
        rig.Plan(u, WithEntry(Picture(u), 0f, -200f, -1.0));
        AssertEqual(ReconLeg.None, rig.Memory.Leg, "home: mission over");
        AssertEqual("returned", rig.Last.EndReason, "reason");
    }

    private static void RecalledByAnotherSource()
    {
        var rig = Outbound(out int scout);
        rig.IssueAll(3.0);
        rig.Issue(new BattleOrderIntent { GroupId = scout, Channel = OrderChannel.Move, Source = IntentSource.Withdrawal, Priority = IntentPriority.Safety }, 3.01);
        var s = Field(3.02);
        rig.Plan(s, WithEntry(Picture(s), 0f, -200f, -1.0));
        AssertEqual(ReconLeg.None, rig.Memory.Leg, "withdrawal took the scout");
        AssertEqual("recalled", rig.Last.EndReason, "reason");
    }

    private static void MissionTimesOut()
    {
        var rig = Outbound(out int scout);
        rig.IssueAll(3.0);
        var s = Field(3.0 + ReconSettings.MaxMissionHours + 0.01);
        Own(s, scout).RegimentPaths = 1;
        rig.Plan(s, WithEntry(Picture(s), 0f, -200f, -1.0));
        AssertEqual(ReconLeg.None, rig.Memory.Leg, "timed out");
        AssertEqual("timeout", rig.Last.EndReason, "reason");
    }

    // ------------------------------------------------------------------ probe

    private static void ProbeNominatesPerEmptyBand()
    {
        var s = Field(3.0);
        var rig = new Rig();
        rig.Plan(s, Picture(s, confidence: 0.6f, phase: BattlePhase.Probe));
        AssertTrue(rig.Memory.TryGetProbe(CavBrigade, out float x, out float z), "cavalry brigade's band probes");
        AssertTrue(rig.Memory.TryGetProbe(InfBrigade, out float ix, out float iz), "infantry brigade's band probes");
        AssertNear(-800f, iz, 0.5f, "one infantry sight depth (200 m) forward");
        AssertNear(400f, ix, 0.5f, "straight ahead");
        AssertFalse(rig.Memory.TryGetProbe(InfDivision, out _, out _), "the division is not in the AI pool");
        AssertTrue((rig.Last.Events & ReconEvents.ProbeNominated) != 0, "nomination event");
        AssertEqual(0, rig.Sink.CountOf(OrderChannel.Move), "the probe move is the posture producer's");
    }

    private static void ProbeNeverForDefenderOrOtherPhase()
    {
        var s = Field(3.0);
        var rig = new Rig();
        rig.Plan(s, Picture(s, confidence: 0.6f, phase: BattlePhase.Probe, defending: true));
        AssertFalse(rig.Memory.TryGetProbe(InfBrigade, out _, out _), "a defender never probes");
        rig.Plan(s, Picture(s, confidence: 0.6f, phase: BattlePhase.MainEffort));
        AssertFalse(rig.Memory.TryGetProbe(InfBrigade, out _, out _), "only in the probe phase");
        rig.Plan(s, Picture(s, confidence: 0.6f, phase: BattlePhase.Probe));
        AssertTrue(rig.Memory.TryGetProbe(InfBrigade, out _, out _), "probe phase");
        rig.Plan(s, Picture(s, confidence: 0.6f, phase: BattlePhase.MainEffort));
        AssertFalse(rig.Memory.TryGetProbe(InfBrigade, out _, out _), "nomination ends with the probe phase");
    }

    private static void ProbeSkipsLinkedAndPlayerSubtree()
    {
        var s = Field(3.0);
        Own(s, InfBrigade).Flags |= UnitFlags.Linked;
        Own(s, CavB).Flags |= UnitFlags.PlayerOwned;
        var rig = new Rig();
        rig.Plan(s, Picture(s, confidence: 0.6f, phase: BattlePhase.Probe));
        AssertFalse(rig.Memory.TryGetProbe(InfBrigade, out _, out _), "vanilla's link would re-lay a linked group");
        AssertFalse(rig.Memory.TryGetProbe(CavBrigade, out _, out _), "a player-owned unit in the subtree");
    }

    private static void ProbeBoundedPerBandPerHour()
    {
        var s = Field(3.0);
        var rig = new Rig();
        rig.Plan(s, Picture(s, confidence: 0.6f, phase: BattlePhase.Probe));
        var t = Field(3.1);
        Own(t, InfBrigade).Z = -800f; // arrived
        rig.Plan(t, Picture(t, confidence: 0.6f, phase: BattlePhase.Probe));
        AssertFalse(rig.Memory.TryGetProbe(InfBrigade, out _, out _), "arrived: nomination ends");
        var u = Field(3.5);
        rig.Plan(u, Picture(u, confidence: 0.6f, phase: BattlePhase.Probe));
        AssertFalse(rig.Memory.TryGetProbe(InfBrigade, out _, out _), "band probed inside the hour");
        var v = Field(4.05);
        rig.Plan(v, Picture(v, confidence: 0.6f, phase: BattlePhase.Probe));
        AssertTrue(rig.Memory.TryGetProbe(InfBrigade, out _, out _), "band probed again after the hour");
    }

    private static void ProbeEndsOnContactWithStop()
    {
        var s = Field(3.0);
        var rig = new Rig();
        rig.Plan(s, Picture(s, confidence: 0.6f, phase: BattlePhase.Probe));
        var t = Field(3.05);
        Own(t, InfBrigade).Z = -900f;
        Own(t, InfBrigade).RegimentPaths = 1;
        SeeEnemy(t, 400f, -720f, 500f, 3.05); // 180 m: inside infantry sight
        rig.Plan(t, Picture(t, confidence: 0.6f, phase: BattlePhase.Probe));
        AssertFalse(rig.Memory.TryGetProbe(InfBrigade, out _, out _), "contact made: nomination ends");
        AssertTrue(rig.Sink.TryFind(InfBrigade, OrderChannel.Stop, out BattleOrderIntent stop), "one stop");
        AssertEqual(IntentPriority.Support, stop.Priority, "support stop");
        AssertTrue((rig.Last.Events & ReconEvents.ProbeEnded) != 0, "probe ended event");
        rig.Plan(t, Picture(t, confidence: 0.6f, phase: BattlePhase.Probe));
        AssertEqual(0, rig.Sink.CountOf(OrderChannel.Stop), "the stop is sent once");
    }

    private static void LookPointIsOneSightDepth()
    {
        var s = Field(3.0);
        AssertTrue(BattleRecon.LookPoint(s, s.IndexOfOwn(InfBrigade), Settings(), Picture(s), out float x, out float z), "look point");
        AssertNear(400f, x, 0.5f, "x");
        AssertNear(-800f, z, 0.5f, "z");
    }

    private static void LookPointRefusedTowardSuperior()
    {
        var s = Field(3.0);
        SeeEnemy(s, 400f, -760f, 5000f, 3.0);
        AssertFalse(BattleRecon.LookPoint(s, s.IndexOfOwn(InfBrigade), Settings(), Picture(s), out _, out _), "never into a visible superior enemy");
    }

    private static void LookPointTowardObjectiveBeforeContact()
    {
        var s = Field(3.0);
        s.SetFrontAxis(TacticalFrontAxis.FromCentroids(150f, -1000f, 150f, -1000f).WithOwnExtent(-200f, 300f)); // no target: +z by default
        var p = Picture(s);
        AssertFalse(BattleRecon.LookPoint(s, s.IndexOfOwn(InfBrigade), Settings(), p, out _, out _), "no axis target and no objective: nowhere to look");
        p.HasObjective = true; p.ObjectiveX = 400f + 120f; p.ObjectiveZ = -1000f; // 120 m east
        AssertTrue(BattleRecon.LookPoint(s, s.IndexOfOwn(InfBrigade), Settings(), p, out float x, out float z), "toward the objective");
        AssertNear(520f, x, 0.5f, "capped at the objective (120 m < 200 m)");
        AssertNear(-1000f, z, 0.5f, "east, not the default +z");
    }

    private static void PlanAllocatesNothingAfterWarmup()
    {
        var rig = Outbound(out int scout);
        rig.IssueAll(3.0);
        var s = Field(3.01);
        Own(s, scout).RegimentPaths = 1;
        var p = WithEntry(Picture(s, phase: BattlePhase.Probe), 0f, -200f, -1.0);
        var settings = Settings();
        rig.Sink.Clear();
        BattleRecon.Plan(s, settings, p, rig.Memory, rig.Executor, rig.Sink);
        rig.Sink.Clear();
        BattleRecon.Plan(s, settings, p, rig.Memory, rig.Executor, rig.Sink);
        long before = GC.GetAllocatedBytesForCurrentThread();
        rig.Sink.Clear();
        BattleRecon.Plan(s, settings, p, rig.Memory, rig.Executor, rig.Sink);
        long after = GC.GetAllocatedBytesForCurrentThread();
        AssertEqual(0L, after - before, "zero allocation after warm-up");
    }

    // ------------------------------------------------------------------ ledger

    private static void LedgerEntryStampsKeepLastWatched()
    {
        var ledger = new TacticalEnemyContactLedger();
        int[] ids = { 7, 8 };
        float[] xs = { 10f, 20f };
        float[] zs = { -10f, -20f };
        bool[] watched = { true, false };
        ledger.ObserveEntryPoints(1f, ids, xs, zs, watched, 2);
        AssertEqual(2, ledger.EntryPointCount, "two enemy entry points");
        AssertTrue(ledger.TryGetEntryPoint(0, out float x0, out float z0, out float w0), "first");
        AssertEqual(10f, x0, "x");
        AssertEqual(1f, w0, "watched now");
        AssertTrue(ledger.TryGetEntryPoint(1, out _, out _, out float w1), "second");
        AssertEqual(-1f, w1, "never watched");
        watched[0] = false;
        ledger.ObserveEntryPoints(2f, ids, xs, zs, watched, 2);
        ledger.TryGetEntryPoint(0, out _, out _, out w0);
        AssertEqual(1f, w0, "keeps the last watched hour");
        ledger.Reset();
        AssertEqual(0, ledger.EntryPointCount, "reset");
    }

    private static void LedgerStalestBand()
    {
        var ledger = new TacticalEnemyContactLedger();
        float[] min = { -100f, 0f };
        float[] max = { 0f, 100f };
        ledger.ObserveCoverage(1f, min, max, new[] { true, true }, 2, 0, 0, 0f);
        ledger.ObserveCoverage(2f, min, max, new[] { false, true }, 2, 0, 0, 0f);
        AssertTrue(ledger.TryStalestBand(2f, out float lateral, out float recency), "a band");
        AssertNear(-50f, lateral, 0.01f, "the left band was seen an hour earlier");
        AssertNear(0.5f, recency, 0.01f, "recency 1 - 1 h / 2 h");
        AssertNear(0f, ledger.FrameHeading, 0.01f, "frame heading");
    }

    // ------------------------------------------------------------------ skirmisher push

    private static BattleSnapshot Skirmish(double hours, int groupStance, bool groupMoving = false)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, ++_tick, hours);
        s.SetSliceCount(20);
        AddGroup(s, InfBrigade, TacticalUnitType.BattleGroupBrigade, 0f);
        s.Own[0].AiStance = groupStance;
        s.Own[0].RegimentPaths = groupMoving ? 1 : 0;
        AddUnit(s, InfA, TacticalUnitType.Infantry, 0f, UnitFlags.IsInfantry);
        AddUnit(s, Company, TacticalUnitType.Skirmisher, 0f, UnitFlags.DetachedCompany);
        s.Own[2].ParentRegimentId = InfA;
        s.Own[1].ParentIndex = 0;
        s.Own[2].ParentIndex = 0;
        s.IndexCommandTree(null);
        return s;
    }

    private static void SkirmisherPushInScreen()
    {
        var s = Skirmish(1.0, groupStance: 1);
        float extra = BattleRecon.SkirmisherPushMeters(s, s.IndexOfOwn(Company), vanillaLinkMeters: 30f, pushMeters: 100f, cancelMeters: 150f);
        AssertNear(70f, extra, 0.01f, "from vanilla's 30 m to the 100 m interruption distance");
    }

    private static void SkirmisherPushRefusals()
    {
        var s = Skirmish(1.0, groupStance: 1);
        int c = s.IndexOfOwn(Company);
        Own(s, Company).FirstEnemyInRangeId = 55;
        AssertEqual(0f, BattleRecon.SkirmisherPushMeters(s, c, 30f, 100f, 150f), "own range list not empty: vanilla's 30 m");
        Own(s, Company).FirstEnemyInRangeId = 0;
        SeeEnemy(s, 0f, -800f, 50f, 1.0); // 200 m < 150 + 100
        AssertEqual(0f, BattleRecon.SkirmisherPushMeters(s, c, 30f, 100f, 150f), "visible enemy too close to push");
        var d = Skirmish(1.0, groupStance: 2);
        AssertEqual(0f, BattleRecon.SkirmisherPushMeters(d, d.IndexOfOwn(Company), 30f, 100f, 150f), "defend stance keeps vanilla's");
        var p = Skirmish(1.0, groupStance: 1);
        Own(p, Company).Flags |= UnitFlags.PlayerOwned;
        AssertEqual(0f, BattleRecon.SkirmisherPushMeters(p, p.IndexOfOwn(Company), 30f, 100f, 150f), "W&L player's company");
        var r = Skirmish(1.0, groupStance: 1);
        AssertEqual(0f, BattleRecon.SkirmisherPushMeters(r, r.IndexOfOwn(InfA), 30f, 100f, 150f), "not a company");
        Own(r, Company).Flags |= UnitFlags.InMelee;
        AssertEqual(0f, BattleRecon.SkirmisherPushMeters(r, r.IndexOfOwn(Company), 30f, 100f, 150f), "in melee");
    }

    private static void SkirmisherPushWhileAttackMoves()
    {
        var still = Skirmish(1.0, groupStance: 3);
        AssertEqual(0f, BattleRecon.SkirmisherPushMeters(still, still.IndexOfOwn(Company), 30f, 100f, 150f), "attack stance standing");
        var moving = Skirmish(1.0, groupStance: 3, groupMoving: true);
        AssertNear(70f, BattleRecon.SkirmisherPushMeters(moving, moving.IndexOfOwn(Company), 30f, 100f, 150f), 0.01f, "attack stance approaching");
    }

    // ------------------------------------------------------------------ doctrine fixes

    private static BattlefieldObjectiveEstimate NoEnemyObjective(float confidence) =>
        new BattlefieldObjectiveEstimate("obj-1", TacticalObjectiveType.VictoryPoint, 0f, confidence, false, 0.8f, 0f, 0f, 0f, 0f);

    private static void DirectorNoContactScouts()
    {
        var input = TacticalOperationDirectorInput.ForTest(OperationRecord.Noop, 100f, 20000f, 0.3f, 0.5f, 0.5f,
            new[] { NoEnemyObjective(0.4f) });
        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);
        AssertEqual(TacticalOperationPhase.Scouting, decision.Operation.Phase, "no contact: scout, not commit on own / 0");
        AssertEqual("no-contact-scout", decision.Reason, "reason");
    }

    private static CommandDoctrineOrder[] Orders(TacticalOperationShape shape, params CommandNodeRole[] roles) =>
        Orders(new OperationRecord(shape, TacticalOperationPhase.Scouting, "obj-1", 500f), NoEnemyObjective(0.4f), roles);

    private static CommandDoctrineOrder[] Orders(OperationRecord operation, BattlefieldObjectiveEstimate objective, params CommandNodeRole[] roles) =>
        Orders(operation, objective, roles, BattleStance.Meeting);

    private static CommandDoctrineOrder[] Orders(OperationRecord operation, BattlefieldObjectiveEstimate objective, CommandNodeRole[] roles, BattleStance stance)
    {
        var nodes = new CommandNodeOperationalState[roles.Length];
        for (int i = 0; i < roles.Length; i++)
            nodes[i] = new CommandNodeOperationalState("node-" + (i + 1), CommandEchelonKind.DivisionLike, roles[i], CommandTaskType.FormUp, CommandTaskState.Planning);
        var picture = new BattlefieldPictureSnapshot(new[] { objective });
        return CommandDoctrineAssignment.Build(nodes, operation, picture, 20000f, 100f, 0.3f, stance);
    }

    private static void ScoutingWithoutContactTasks()
    {
        CommandDoctrineOrder[] orders = Orders(TacticalOperationShape.SingleMainEffort, CommandNodeRole.MainEffort, CommandNodeRole.ScreeningForce);
        AssertEqual(CommandTaskType.Probe, orders[0].Task, "main effort probes");
        AssertEqual(CommandTaskType.Screen, orders[1].Task, "screen screens");
    }

    private static void DefensiveNetworkNotProbing()
    {
        CommandDoctrineOrder[] orders = Orders(TacticalOperationShape.DefensiveNetwork, CommandNodeRole.MainEffort);
        AssertTrue(orders[0].Task != CommandTaskType.Probe, "a defender is not sent forward");
    }

    private static void GtReconNoCavalryScoutBranch()
    {
        TacticalGrandTacticianReconDecision d = TacticalGrandTacticianReconDoctrine.Decide(
            new TacticalGrandTacticianReconInput(CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, hasFowVisibleEnemy: false, underRecentFire: false));
        AssertEqual(CommandTaskType.Probe, d.Task, "no visible enemy: probe by bounds, cavalry or not");
        TacticalGrandTacticianReconDecision screen = TacticalGrandTacticianReconDoctrine.Decide(
            new TacticalGrandTacticianReconInput(CommandNodeRole.ScreeningForce, CommandTaskType.Screen, hasFowVisibleEnemy: false, underRecentFire: false));
        AssertEqual(CommandTaskType.Screen, screen.Task, "screens stay screens");
    }

    private static TacticalCavalryFollowInput Raid(bool inSquare, bool isOfficer) =>
        new TacticalCavalryFollowInput(TacticalCavalryFollowMode.Raid, hasFollowTarget: true, targetIsOfficer: isOfficer,
            targetInFort: false, targetInSquare: inSquare, targetHasInfantryOrArtillerySupport: false, canChargeTarget: true,
            fearAdvantage01: 0.9f, enemyClose: true, enemyDistance: 80f, longRange: 300f);

    private static void CavalryFollowSquareAndOfficer()
    {
        AssertEqual(TacticalCavalryFollowAction.ChargeRaidTarget, TacticalCavalryFollowDoctrine.Decide(Raid(false, false)).Action, "open target");
        AssertEqual(TacticalCavalryFollowAction.ClearFollowTarget, TacticalCavalryFollowDoctrine.Decide(Raid(true, false)).Action, "square");
        AssertEqual(TacticalCavalryFollowAction.ClearFollowTarget, TacticalCavalryFollowDoctrine.Decide(Raid(false, true)).Action, "officer");
        AssertTrue(BattleRecon.IsSquare(TacticalUnitType.Infantry, 4), "regiment formation 4 is the square");
        AssertFalse(BattleRecon.IsSquare(TacticalUnitType.BattleGroupBrigade, 4), "a group's formation 4 is march column");
        AssertTrue(BattleRecon.IsOfficerTarget(TacticalUnitType.BattleGroupBrigade), "a command group");
        AssertTrue(BattleRecon.IsOfficerTarget(TacticalUnitType.Courier), "a courier");
        AssertFalse(BattleRecon.IsOfficerTarget(TacticalUnitType.Scout), "a scout company is no officer");
    }

    private static void FacingEntryIsNotUrgent()
    {
        TacticalFacingPulseDecision d = TacticalFacingPulseDoctrine.Decide(new TacticalFacingPulseInput(
            TacticalFacingPulseScope.Brigade, CommandTaskType.Screen, 0, 0, 0f, 90f, true, TacticalFacingThreatSource.Entry,
            false, false, false, false, false, false, false, 15f));
        AssertEqual(TacticalFacingPulseAction.CorrectFacing, d.Action, "faces the unwatched entry point");
        AssertFalse(d.Urgent, "never urgent");
    }

    private static void EntryBearingPicksStalest()
    {
        var s = Field(3.0);
        var p = Picture(s);
        p.AddEntry(400f, -1000f + 500f, 2.5);  // north of the infantry brigade, watched half an hour ago
        p.AddEntry(900f, -1000f, -1.0);        // east, never watched
        AssertTrue(BattleRecon.TryEntryBearing(s, s.IndexOfOwn(InfBrigade), p, out float heading), "a bearing");
        AssertNear(90f, heading, 0.5f, "the never-watched entry point to the east");
        var q = Picture(s);
        q.AddEntry(900f, -1000f, 2.99);
        AssertFalse(BattleRecon.TryEntryBearing(s, s.IndexOfOwn(InfBrigade), q, out _), "a watched entry point is no threat bearing");
    }

    // ------------------------------------------------------------------ review fixes

    private static void LookPointOnlyForForwardTargets()
    {
        AssertTrue(BattleRecon.UsesLookPoint(CommandTaskType.Probe, PostureExecutionTarget.DoctrinePrimaryTarget), "probe forward");
        AssertTrue(BattleRecon.UsesLookPoint(CommandTaskType.Scout, PostureExecutionTarget.ObjectiveApproach), "scout forward");
        AssertFalse(BattleRecon.UsesLookPoint(CommandTaskType.Probe, PostureExecutionTarget.DoctrineFallbackTarget), "a break-off stays a break-off");
        AssertFalse(BattleRecon.UsesLookPoint(CommandTaskType.Scout, PostureExecutionTarget.RecoveryPath), "a recovery stays a recovery");
        AssertFalse(BattleRecon.UsesLookPoint(CommandTaskType.AttackObjective, PostureExecutionTarget.DoctrinePrimaryTarget), "not a recon task");
    }

    private sealed class AllowGuard : IOrderGuard
    {
        public bool CanWrite(in UnitRecord unit, out SkipReason reason) { reason = SkipReason.None; return true; }
    }

    private static void ProbeStopReachesWalkingRegiments()
    {
        var s = Field(3.0);
        Own(s, InfA).RegimentPaths = 1; // the brigade HQ stands, its regiment walks on
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        var stop = new BattleOrderIntent
        {
            GroupId = InfBrigade, Channel = OrderChannel.Stop, Heading = -1f,
            Priority = IntentPriority.Support, Source = IntentSource.Recon, Reason = BattleRecon.ProbeStopReason,
        };
        ExecutionPlan plan = executor.Plan(s, stop, 3.0);
        AssertTrue(plan.Execute, "a stop is no duplicate while a descendant still moves: " + plan.Skip);
        Own(s, InfA).RegimentPaths = 0;
        AssertEqual(SkipReason.Duplicate, executor.Plan(s, stop, 3.0).Skip, "nothing moves: duplicate");
    }

    private static void SeeRemembered(BattleSnapshot s, float x, float z, float strength)
    {
        s.AddObservation(new EnemyObservation
        {
            InstanceId = 800 + s.ObservationCount, UnitTyp = TacticalUnitType.Infantry, X = x, Z = z, Strength = strength,
            State = ObservationState.Remembered, Confidence01 = 0.4f, LastSeenHours = 2.0,
        });
    }

    private static void RememberedEnemyTargetAfterContact()
    {
        var s = Field(3.0);
        SeeRemembered(s, 0f, -300f, 3000f);
        SeeRemembered(s, 900f, -300f, 3000f);
        var rig = new Rig();
        rig.Plan(s, Picture(s, confidence: 0.1f, hadContact: true));
        AssertEqual(ReconTargetKind.RememberedEnemy, rig.Memory.Kind, "the last known enemy is looked at again");
        AssertNear(-450f, rig.Memory.TargetZ, 2f, "stand-off 150 m short of it");
        AssertTrue(Math.Abs(rig.Memory.TargetX) < 20f, "the nearer remembered enemy");
        var confident = new Rig();
        confident.Plan(s, Picture(s, confidence: 0.6f, hadContact: true));
        AssertEqual(ReconLeg.None, confident.Memory.Leg, "above the gate nobody rides to a remembered enemy");
    }

    private static void RetryIsNotARecall()
    {
        var rig = Outbound(out int scout);
        var move = rig.Sink.Items[0];
        // The executor issued the move, nothing moved: Failed, then a rotated retry ticket.
        rig.Executor.RecordIssued(move, 3.0, Array.Empty<IssuedTarget>(), 0, Array.Empty<UnitWaypoint>(), 0, 0, 0);
        var s = Field(3.01);
        var outcomes = new OrderOutcome[8];
        rig.Executor.ObserveOutcomes(s, 3.01, outcomes, out int count);
        AssertTrue(rig.Executor.TryGetLastOutcome(scout, OrderChannel.Move, out OrderOutcome last) && last.Kind == OrderOutcomeKind.Retried, "retried");
        var t = Field(3.02);
        Own(t, scout).RegimentPaths = 1;
        Own(t, scout).LastWaypointX = 400f; // the rotated target, far off ours
        Own(t, scout).LastWaypointZ = -500f;
        rig.Plan(t, WithEntry(Picture(t), 0f, -200f, -1.0));
        AssertEqual(ReconLeg.Outbound, rig.Memory.Leg, "our own retry is not a recall");
    }

    // ------------------------------------------------------------------ defender stance

    // A screen, not a formed line: enemy present, confidence above the recon floor, main line not exposed.
    private static BattlefieldObjectiveEstimate ScreenContactObjective() =>
        new BattlefieldObjectiveEstimate("obj-1", TacticalObjectiveType.VictoryPoint, 300f, 0.4f, false, 0.8f, 0f, 0f, 0f, 0f);

    private static TacticalOperationDirectorDecision DecideFor(BattleStance stance, BattlefieldObjectiveEstimate objective) =>
        TacticalOperationDirector.Decide(TacticalOperationDirectorInput.ForTest(OperationRecord.Noop, 100f, 20000f, 0.3f, 0.5f, 0.5f,
            new[] { objective }).WithStance(stance));

    private static void AssertNoProbe(CommandDoctrineOrder[] orders, string label)
    {
        for (int i = 0; i < orders.Length; i++)
            AssertTrue(orders[i].Task != CommandTaskType.Probe, label + ": node " + (i + 1) + " is " + orders[i].Task);
    }

    private static void DirectorDefenderScreens()
    {
        CommandNodeRole[] roles = { CommandNodeRole.MainEffort, CommandNodeRole.ScreeningForce, CommandNodeRole.SupportingAttack, CommandNodeRole.FixingForce };
        TacticalOperationDirectorDecision none = DecideFor(BattleStance.Defender, NoEnemyObjective(0.4f));
        AssertEqual(TacticalOperationPhase.Scouting, none.Operation.Phase, "no contact: scouting, not a commit");
        AssertEqual(TacticalOperationShape.DefensiveNetwork, none.Operation.Shape, "a defender's scouting is not sent forward");
        AssertEqual("defender-screen", none.Reason, "reason");
        CommandDoctrineOrder[] orders = Orders(none.Operation, NoEnemyObjective(0.4f), roles);
        AssertNoProbe(orders, "defender, no contact");
        AssertEqual(CommandTaskType.Screen, orders[1].Task, "the screen screens");

        TacticalOperationDirectorDecision screen = DecideFor(BattleStance.Defender, ScreenContactObjective());
        AssertEqual(TacticalOperationShape.DefensiveNetwork, screen.Operation.Shape, "reconnaissance contact: still no forward shape");
        AssertNoProbe(Orders(screen.Operation, ScreenContactObjective(), roles), "defender, reconnaissance contact");
    }

    private static void DirectorMeetingAndAttackerProbe()
    {
        foreach (BattleStance stance in new[] { BattleStance.Meeting, BattleStance.Attacker })
        {
            TacticalOperationDirectorDecision none = DecideFor(stance, NoEnemyObjective(0.4f));
            AssertEqual(TacticalOperationShape.FixAndFlank, none.Operation.Shape, stance + ": unchanged shape");
            AssertEqual("no-contact-scout", none.Reason, stance + ": unchanged reason");
            AssertEqual(CommandTaskType.Probe, Orders(none.Operation, NoEnemyObjective(0.4f), CommandNodeRole.MainEffort)[0].Task, stance + ": main effort probes");
            AssertEqual("recon-contact", DecideFor(stance, ScreenContactObjective()).Reason, stance + ": reconnaissance contact unchanged");
        }
    }

    private static void GtReconDefenderScreens()
    {
        TacticalGrandTacticianReconDecision d = TacticalGrandTacticianReconDoctrine.Decide(
            new TacticalGrandTacticianReconInput(CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, hasFowVisibleEnemy: false,
                underRecentFire: false, sideDefending: true));
        AssertEqual(CommandTaskType.Screen, d.Task, "a defender screens the unseen front instead of probing it");
    }

    private static void DefenderStillScouts()
    {
        var s = Field(3.0);
        var rig = new Rig();
        rig.Plan(s, WithEntry(Picture(s, defending: true), 0f, -200f, -1.0));
        AssertEqual(ReconLeg.Outbound, rig.Memory.Leg, "a defender's cavalry still rides to look");
    }

    // ------------------------------------------------------------------ probe hold

    private static Rig Probing()
    {
        var s = Field(3.0);
        var rig = new Rig();
        rig.Plan(s, Picture(s, confidence: 0.6f, phase: BattlePhase.Probe));
        AssertTrue(rig.Memory.TryGetProbe(InfBrigade, out _, out _), "nominated");
        return rig;
    }

    private static void ProbeHoldsAfterWeakerContact()
    {
        var rig = Probing();
        var t = Field(3.05);
        Own(t, InfBrigade).Z = -900f;
        Own(t, InfBrigade).RegimentPaths = 1;
        SeeEnemy(t, 400f, -720f, 500f, 3.05); // 180 m, weaker than the brigade's 1200
        rig.Plan(t, Picture(t, confidence: 0.6f, phase: BattlePhase.Probe, postureTask: CommandTaskType.Probe));
        AssertFalse(rig.Memory.TryGetProbe(InfBrigade, out _, out _), "the probe is over");
        AssertTrue(rig.Sink.TryFind(InfBrigade, OrderChannel.Stop, out _), "stopped on contact");
        AssertTrue(rig.Memory.TryGetHold(InfBrigade, CommandTaskType.FormUp, out float x, out float z, out float dx, out float dz), "holds");
        AssertNear(400f, x, 0.5f, "where the contact stopped it");
        AssertNear(-900f, z, 0.5f, "100 m gained");
        AssertNear(1f, dz, 0.01f, "the probe's advance direction");
        AssertFalse(rig.Memory.TryGetHold(InfBrigade, CommandTaskType.Screen, out _, out _, out _, out _), "only for the ledger task it was nominated under");
        AssertTrue((rig.Last.Events & ReconEvents.ProbeHeld) != 0, "hold event");
    }

    private static void ProbeSuperiorContactNoHold()
    {
        var rig = Probing();
        var t = Field(3.05);
        Own(t, InfBrigade).Z = -900f;
        Own(t, InfBrigade).RegimentPaths = 1;
        SeeEnemy(t, 400f, -720f, 1200f, 3.05); // at least as strong
        rig.Plan(t, Picture(t, confidence: 0.6f, phase: BattlePhase.Probe, postureTask: CommandTaskType.Probe));
        AssertTrue(rig.Sink.TryFind(InfBrigade, OrderChannel.Stop, out _), "stopped on contact");
        AssertFalse(rig.Memory.TryGetHold(InfBrigade, CommandTaskType.FormUp, out _, out _, out _, out _), "falls back to its ledger task");
    }

    private static void ProbeHoldsOnArrival()
    {
        var rig = Probing();
        var t = Field(3.1);
        Own(t, InfBrigade).Z = -800f;
        rig.Plan(t, Picture(t, confidence: 0.6f, phase: BattlePhase.Probe, postureTask: CommandTaskType.Probe));
        AssertEqual(0, rig.Sink.CountOf(OrderChannel.Stop), "standing: no stop");
        AssertTrue(rig.Memory.TryGetHold(InfBrigade, CommandTaskType.FormUp, out _, out float z, out _, out _), "holds the probe point");
        AssertNear(-800f, z, 0.5f, "the probe point");
    }

    private static void ProbeHoldReleasedOnReassign()
    {
        var rig = Probing();
        var t = Field(3.1);
        Own(t, InfBrigade).Z = -800f;
        rig.Plan(t, Picture(t, confidence: 0.6f, phase: BattlePhase.Probe, postureTask: CommandTaskType.Probe));
        var u = Field(3.15);
        Own(u, InfBrigade).Z = -800f;
        rig.Plan(u, Picture(u, confidence: 0.6f, phase: BattlePhase.MainEffort, postureTask: CommandTaskType.FormUp));
        AssertTrue(rig.Memory.TryGetHold(InfBrigade, CommandTaskType.FormUp, out _, out _, out _, out _), "same ledger task, other phase: still held");
        rig.Plan(u, Picture(u, confidence: 0.6f, phase: BattlePhase.MainEffort, postureTask: CommandTaskType.AttackObjective));
        AssertFalse(rig.Memory.TryGetHold(InfBrigade, CommandTaskType.FormUp, out _, out _, out _, out _), "the ledger reassigned it: released");
        AssertFalse(rig.Memory.TryGetHold(InfBrigade, CommandTaskType.AttackObjective, out _, out _, out _, out _), "for good");
    }

    private static void ProbeHoldBlocksOnlyBackward()
    {
        AssertTrue(BattleRecon.HoldBlocks(PostureExecutionTarget.AssemblyArea, 400f, -800f, 0f, 1f, 400f, -1000f), "back to form-up: held");
        AssertTrue(BattleRecon.HoldBlocks(PostureExecutionTarget.DoctrinePrimaryTarget, 400f, -800f, 0f, 1f, 300f, -900f), "back and aside: held");
        AssertFalse(BattleRecon.HoldBlocks(PostureExecutionTarget.DoctrinePrimaryTarget, 400f, -800f, 0f, 1f, 400f, -600f), "forward: goes");
        AssertFalse(BattleRecon.HoldBlocks(PostureExecutionTarget.AssemblyArea, 400f, -800f, 0f, 1f, 500f, -830f), "a shift along the line within the dedupe radius: goes");
        AssertFalse(BattleRecon.HoldBlocks(PostureExecutionTarget.DoctrineFallbackTarget, 400f, -800f, 0f, 1f, 400f, -1000f), "a break-off stays a break-off");
        AssertFalse(BattleRecon.HoldBlocks(PostureExecutionTarget.FallbackLine, 400f, -800f, 0f, 1f, 400f, -1000f), "a fallback stays a fallback");
        AssertFalse(BattleRecon.HoldBlocks(PostureExecutionTarget.RecoveryPath, 400f, -800f, 0f, 1f, 400f, -1000f), "a recovery stays a recovery");
    }

    private static void ProbeHoldReplacedByNomination()
    {
        var rig = Probing();
        var t = Field(3.1);
        Own(t, InfBrigade).Z = -800f;
        rig.Plan(t, Picture(t, confidence: 0.6f, phase: BattlePhase.Probe, postureTask: CommandTaskType.Probe));
        AssertTrue(rig.Memory.TryGetHold(InfBrigade, CommandTaskType.FormUp, out _, out _, out _, out _), "held");
        var v = Field(4.15);
        Own(v, InfBrigade).Z = -800f;
        rig.Plan(v, Picture(v, confidence: 0.6f, phase: BattlePhase.Probe));
        AssertTrue(rig.Memory.TryGetProbe(InfBrigade, out _, out float z), "probes the next bound after the hour");
        AssertNear(-600f, z, 0.5f, "from the ground gained");
        AssertFalse(rig.Memory.TryGetHold(InfBrigade, CommandTaskType.FormUp, out _, out _, out _, out _), "the new probe replaces the hold");
    }

    // ------------------------------------------------------------------ verify round 1

    private static readonly CommandNodeRole[] AllRoles =
    {
        CommandNodeRole.MainEffort, CommandNodeRole.SupportingAttack, CommandNodeRole.FixingForce,
        CommandNodeRole.ScreeningForce, CommandNodeRole.Probe, CommandNodeRole.FlankMarch, CommandNodeRole.Defender,
    };

    private static void AssertDefensive(CommandDoctrineOrder[] orders, string label)
    {
        for (int i = 0; i < orders.Length; i++)
        {
            CommandTaskType t = orders[i].Task;
            AssertTrue(t != CommandTaskType.Probe && t != CommandTaskType.AttackObjective &&
                t != CommandTaskType.SupportAttack && t != CommandTaskType.FixEnemy, label + ": node " + (i + 1) + " is " + t);
        }
    }

    private static void AssertNotOffensive(OperationRecord op, string label)
    {
        AssertTrue(op.Shape == TacticalOperationShape.DefensiveNetwork || op.Shape == TacticalOperationShape.DelayAndFallback,
            label + ": shape " + op.Shape);
        AssertTrue(op.Phase != TacticalOperationPhase.Committed && op.Phase != TacticalOperationPhase.Exploiting,
            label + ": phase " + op.Phase);
    }

    private static BattlefieldObjectiveEstimate ExposedLine() =>
        new BattlefieldObjectiveEstimate("obj-1", TacticalObjectiveType.VictoryPoint, 2000f, 0.9f, true, 0.8f, 0f, 0f, 0f, 0f);

    private static void StanceGuardTable()
    {
        AssertEqual(CommandTaskType.Screen, TacticalDecisionDoctrine.TaskForStance(CommandTaskType.Probe, BattleStance.Defender), "probe");
        AssertEqual(CommandTaskType.HoldObjective, TacticalDecisionDoctrine.TaskForStance(CommandTaskType.AttackObjective, BattleStance.Defender), "attack");
        AssertEqual(CommandTaskType.HoldObjective, TacticalDecisionDoctrine.TaskForStance(CommandTaskType.SupportAttack, BattleStance.Defender), "support");
        AssertEqual(CommandTaskType.HoldObjective, TacticalDecisionDoctrine.TaskForStance(CommandTaskType.FixEnemy, BattleStance.Defender), "fix");
        AssertEqual(CommandTaskType.Scout, TacticalDecisionDoctrine.TaskForStance(CommandTaskType.Scout, BattleStance.Defender), "scouting is information-gathering");
        AssertEqual(CommandTaskType.Screen, TacticalDecisionDoctrine.TaskForStance(CommandTaskType.Screen, BattleStance.Defender), "screen");
        foreach (BattleStance stance in new[] { BattleStance.Meeting, BattleStance.Attacker })
            foreach (CommandTaskType t in (CommandTaskType[])Enum.GetValues(typeof(CommandTaskType)))
                AssertEqual(t, TacticalDecisionDoctrine.TaskForStance(t, stance), stance + " keeps " + t);
    }

    private static void DefenderNeverCommitsOffensively()
    {
        TacticalOperationDirectorDecision meeting = DecideFor(BattleStance.Meeting, ExposedLine());
        AssertEqual(TacticalOperationPhase.Committed, meeting.Operation.Phase, "control: a meeting side commits on 10:1");
        TacticalOperationDirectorDecision defender = DecideFor(BattleStance.Defender, ExposedLine());
        AssertNotOffensive(defender.Operation, "defender on 10:1");
        AssertDefensive(Orders(defender.Operation, ExposedLine(), AllRoles, BattleStance.Defender), "defender orders");
    }

    private static void DefenderDowngradeNoProbe()
    {
        // Committed before (stance not yet known), then the contact turns out a screen.
        var committed = new OperationRecord(TacticalOperationShape.FixAndFlank, TacticalOperationPhase.Committed, "obj-1", 900f);
        TacticalOperationDirectorDecision d = TacticalOperationDirector.Decide(TacticalOperationDirectorInput.ForTest(committed, 5000f,
            20000f, 0.3f, 0.5f, 0.5f, new[] { ScreenContactObjective() }).WithStance(BattleStance.Defender));
        AssertNotOffensive(d.Operation, "after the downgrade");
        AssertDefensive(Orders(d.Operation, ScreenContactObjective(), AllRoles, BattleStance.Defender), "downgraded orders");
    }

    private static void DefenderLegacyOffensiveOperation()
    {
        var committed = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "obj-1", 900f);
        TacticalOperationDirectorDecision d = TacticalOperationDirector.Decide(TacticalOperationDirectorInput.ForTest(committed, 100f,
            20000f, 0.3f, 0.5f, 0.5f, new[] { ExposedLine() }).WithStance(BattleStance.Defender));
        AssertNotOffensive(d.Operation, "inside the commit window");
    }

    private static void DefenderGuardIndependentOfShape()
    {
        foreach (TacticalOperationShape shape in (TacticalOperationShape[])Enum.GetValues(typeof(TacticalOperationShape)))
            foreach (TacticalOperationPhase phase in (TacticalOperationPhase[])Enum.GetValues(typeof(TacticalOperationPhase)))
            {
                var op = new OperationRecord(shape, phase, "obj-1", 900f);
                AssertDefensive(Orders(op, ScreenContactObjective(), AllRoles, BattleStance.Defender), shape + "/" + phase + " screen contact");
                AssertDefensive(Orders(op, ExposedLine(), AllRoles, BattleStance.Defender), shape + "/" + phase + " exposed line");
                AssertDefensive(Orders(op, NoEnemyObjective(0.4f), AllRoles, BattleStance.Defender), shape + "/" + phase + " no contact");
            }
    }

    private static void GtReconDefenderVisibleEnemy()
    {
        foreach (CommandTaskType task in new[] { CommandTaskType.Probe, CommandTaskType.AttackObjective, CommandTaskType.SupportAttack, CommandTaskType.FixEnemy })
        {
            TacticalGrandTacticianReconDecision d = TacticalGrandTacticianReconDoctrine.Decide(new TacticalGrandTacticianReconInput(
                CommandNodeRole.MainEffort, task, hasFowVisibleEnemy: true, underRecentFire: false, sideDefending: true));
            AssertTrue(d.Task != CommandTaskType.Probe && d.Task != CommandTaskType.AttackObjective &&
                d.Task != CommandTaskType.SupportAttack && d.Task != CommandTaskType.FixEnemy, task + " with a visible enemy became " + d.Task);
        }
        TacticalGrandTacticianReconDecision attacker = TacticalGrandTacticianReconDoctrine.Decide(new TacticalGrandTacticianReconInput(
            CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, hasFowVisibleEnemy: true, underRecentFire: false));
        AssertEqual(CommandTaskType.AttackObjective, attacker.Task, "control: contact confirmed keeps an attacker's attack");
    }

    private static void DefenderStanceReachesLedger()
    {
        var side = new TacticalBattleOrchestrator(1, TacticalCommanderRoster.BuildFromSynthetic(Array.Empty<SyntheticCommanderInput>()));
        var army = TestFixtures.NewArmyOrchestratorWithPlan();
        side.AttachArmy(army);
        // No reinforcement-opportunity update: only the stance refresh.
        army.UpdateBattleStance(BattleStance.Defender);
        AssertEqual(BattleStance.Defender, army.CurrentBattleStance, "stance");
        side.TickOperationsLedger(
            TacticalCommanderMode.Active,
            new[] { TestFixtures.ObjectiveRecordFor("ridge-a", enemyStrength: 70f, friendlyAssignedStrength: 100f, status: TacticalObjectiveStatus.WeaklyHeld) },
            new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign"),
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
        AssertNotOffensive(army.CurrentOperation, "the ledger saw the defender stance");
    }

    private static void StanceReadBeforeOpportunityFlag()
    {
        string path = System.IO.Path.Combine(TestFixtures.FindRepoPath("src/WhiskeyRealism"), "Tactical", "Orchestrator", "ArmyPostureInputsRuntime.cs");
        string text = System.IO.File.ReadAllText(path);
        int stance = text.IndexOf("army.UpdateBattleStance(ArmyEvidenceBuilder.ReadBattleStance(", StringComparison.Ordinal);
        int flag = text.IndexOf("Plugin.EnableTacticalReinforcementOpportunityDoctrine == null", StringComparison.Ordinal);
        AssertTrue(stance >= 0, "Refresh reads the stance");
        AssertTrue(flag >= 0, "the opportunity flag gate is still there");
        AssertTrue(stance < flag, "the stance is read before the opportunity flag can return");
    }

    private static TacticalFacingPulseDecision EntryPulse(TacticalFacingPulseScope scope, CommandTaskType task) =>
        TacticalFacingPulseDoctrine.Decide(EntryInput(scope, task));

    private static TacticalFacingPulseInput EntryInput(TacticalFacingPulseScope scope, CommandTaskType task) =>
        new TacticalFacingPulseInput(scope, task, 0, 0, 0f, 90f, true, TacticalFacingThreatSource.Entry,
            false, false, false, false, false, false, false, 15f, isArtillery: false, threatWithinFireRange: false);

    private static readonly CommandTaskType[] FrontAxisTasks =
    {
        CommandTaskType.HoldObjective, CommandTaskType.ReserveWait, CommandTaskType.Consolidate, CommandTaskType.FixEnemy,
        CommandTaskType.Delay, CommandTaskType.HoldChoke, CommandTaskType.FallBackToLine,
    };

    private static void EntryFacingOnlyWatchingTasks()
    {
        foreach (CommandTaskType task in new[] { CommandTaskType.Screen, CommandTaskType.Probe, CommandTaskType.GuardFlank })
            AssertEqual(TacticalFacingPulseAction.CorrectFacing, EntryPulse(TacticalFacingPulseScope.Brigade, task).Action, task + " faces the entry point");
        foreach (CommandTaskType task in FrontAxisTasks)
            AssertFalse(EntryPulse(TacticalFacingPulseScope.Brigade, task).ShouldWrite, task + " keeps its front-axis facing");
    }

    private static void RegimentFollowsEntryBearing()
    {
        TacticalFacingPulseDecision d = EntryPulse(TacticalFacingPulseScope.Regiment, CommandTaskType.Screen);
        AssertEqual(TacticalFacingPulseAction.CorrectFacing, d.Action, "an entry point is not a threat out of range: " + d.Reason);
        AssertNear(90f, d.TargetFacingDegrees, 0.01f, "the bearing handed down");
        AssertFalse(d.Urgent, "never urgent");
        AssertFalse(EntryPulse(TacticalFacingPulseScope.Regiment, CommandTaskType.HoldObjective).ShouldWrite, "a hold's regiments do not turn");
    }

    private static int BrigadePulse(CommandTaskType task)
    {
        var sink = new Sink();
        TacticalFacingPulseInput input = EntryInput(TacticalFacingPulseScope.Brigade, task);
        TacticalFacingPulseDecision d = TacticalFacingPulseDoctrine.Decide(input);
        PostureIntentPlan.SubmitBrigadePulse(sink, InfBrigade, input, d, subtreeAcceptsGroupFacing: true);
        return sink.CountOf(OrderChannel.Facing);
    }

    private static void BrigadePulseEntrySubmits()
    {
        AssertEqual(1, BrigadePulse(CommandTaskType.Screen), "a screen turns to the entry point");
        AssertEqual(1, BrigadePulse(CommandTaskType.Probe), "a probe turns to the entry point");
        AssertEqual(1, BrigadePulse(CommandTaskType.GuardFlank), "a flank guard keeps its turn");
        foreach (CommandTaskType task in FrontAxisTasks)
            AssertEqual(0, BrigadePulse(task), task + " submits nothing from the entry source");
    }

    private static void RememberedTargetIsNeverVisible()
    {
        var s = Field(3.0);
        SeeEnemy(s, 0f, -300f, 50f, 3.0);          // visible, weak, nearer
        SeeRemembered(s, 900f, -300f, 3000f);      // last seen here
        var rig = new Rig();
        rig.Plan(s, Picture(s, confidence: 0.1f, hadContact: true));
        AssertEqual(ReconTargetKind.RememberedEnemy, rig.Memory.Kind, "a remembered enemy is looked at again");
        AssertTrue(rig.Memory.TargetX > 500f, "not the visible one: x " + rig.Memory.TargetX);
    }

    private static void ProbeBandWithOnlyRememberedEnemy()
    {
        var s = Field(3.0);
        SeeRemembered(s, 400f, -500f, 300f); // in the infantry brigade's band, no longer seen
        var rig = new Rig();
        rig.Plan(s, Picture(s, confidence: 0.6f, phase: BattlePhase.Probe));
        AssertTrue(rig.Memory.TryGetProbe(InfBrigade, out _, out _), "only a visible enemy makes a band known");
    }

    private static void ProbeHoldNeverOverwrites()
    {
        var m = new ReconMemory();
        const int groups = 3 * ReconMemory.MaxBands;
        for (int g = 1; g <= groups; g++)
        {
            int band = g % ReconMemory.MaxBands;
            m.Nominate(band, 1000 + g, 0f, 0f, 0f, 200f, CommandTaskType.FormUp, 0.0);
            AssertTrue(m.Hold(band, g, 200f), "hold " + g);
        }
        for (int g = 1; g <= groups; g++)
        {
            AssertTrue(m.TryGetHold(1000 + g, CommandTaskType.FormUp, out float x, out _, out _, out _), "hold " + g + " kept");
            AssertNear(g, x, 0.01f, "its own point");
        }
    }

    // ------------------------------------------------------------------ verify round 2

    private static readonly CommandNodeIntent[] DefenderIntents =
    {
        new CommandNodeIntent("main", "army", DirectChildRole.Main, DirectChildAxis.SectorAxis, 2, 100, 0.5f, 3),
        new CommandNodeIntent("support", "army", DirectChildRole.SupportMain, DirectChildAxis.SectorAxis, 2, 80, 0.4f, 3),
        new CommandNodeIntent("fix", "army", DirectChildRole.Fix, DirectChildAxis.Hold, 3, 60, 0.3f, 3),
        new CommandNodeIntent("screen", "army", DirectChildRole.Screen, DirectChildAxis.Hold, 4, 40, 0.2f, 3),
    };

    private static System.Collections.Generic.IReadOnlyList<CommandNodeOperationalState> NodeStates(TacticalOperationShape shape, BattleStance stance) =>
        CommandNodeOperationsRuntime.Build(DefenderIntents, new OperationRecord(shape, TacticalOperationPhase.Forming, "ridge-a", 0f),
            new[] { TestFixtures.ObjectiveRecordFor("ridge-a", enemyStrength: 900f, friendlyAssignedStrength: 100f, status: TacticalObjectiveStatus.WeaklyHeld) },
            stance);

    private static void DefenderNodeTasksGuarded()
    {
        var meeting = NodeStates(TacticalOperationShape.DefensiveNetwork, BattleStance.Meeting);
        AssertEqual(CommandTaskType.SupportAttack, meeting[1].Task, "control: the planner gives support an attack task");
        foreach (TacticalOperationShape shape in (TacticalOperationShape[])Enum.GetValues(typeof(TacticalOperationShape)))
        {
            var states = NodeStates(shape, BattleStance.Defender);
            for (int i = 0; i < states.Count; i++)
            {
                CommandTaskType t = states[i].Task;
                AssertTrue(t != CommandTaskType.Probe && t != CommandTaskType.AttackObjective && t != CommandTaskType.SupportAttack &&
                    t != CommandTaskType.FixEnemy, shape + ": " + states[i].NodeId + " is " + t);
            }
        }
        var defender = NodeStates(TacticalOperationShape.DefensiveNetwork, BattleStance.Defender);
        AssertEqual(CommandTaskType.HoldObjective, defender[1].Task, "support holds");
        AssertTrue(CommandNodeOperationsRuntime.TryBuildSingle(DefenderIntents[1], new OperationRecord(TacticalOperationShape.DefensiveNetwork,
            TacticalOperationPhase.Forming, "ridge-a", 0f),
            new[] { TestFixtures.ObjectiveRecordFor("ridge-a", 900f, 100f, TacticalObjectiveStatus.WeaklyHeld) },
            BattleStance.Defender, out CommandNodeOperationalState single), "single");
        AssertEqual(CommandTaskType.HoldObjective, single.Task, "the posture fallback path holds too");
    }

    private static void DefenderLedgerStanceCapped()
    {
        var states = NodeStates(TacticalOperationShape.DefensiveNetwork, BattleStance.Defender);
        for (int i = 0; i < states.Count; i++)
        {
            foreach (bool weakPoint in new[] { false, true })
            {
                int stance = TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(states[i].Task, weakPoint, BattleStance.Defender);
                AssertTrue(stance <= TacticalCommandTaskToStanceMapper.StanceDefend, states[i].NodeId + " weakPoint=" + weakPoint + " stance " + stance);
            }
        }
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceDefend,
            TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(CommandTaskType.HoldObjective, true, BattleStance.Defender), "a defender's hold is capped at defend");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceAttack,
            TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(CommandTaskType.HoldObjective, true, BattleStance.Meeting), "control: a meeting side's hold commits on a weak point");
    }

    private static void PostureStanceGuardRewrites()
    {
        var op = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "obj-1", 900f);
        CommandDoctrineOrder order = Orders(op, ExposedLine(), CommandNodeRole.MainEffort)[0];
        AssertEqual(CommandTaskType.AttackObjective, order.Task, "control order");
        var state = new CommandNodeOperationalState("node-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.Probe, CommandTaskState.Planning);
        AssertTrue(PostureStanceGuard.Apply(BattleStance.Defender, ref state, hasOrder: true, ref order), "rewritten");
        AssertEqual(CommandTaskType.Screen, state.Task, "state");
        AssertEqual(CommandTaskType.Screen, order.Task, "order follows the state");
        var keep = new CommandNodeOperationalState("node-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, CommandTaskState.Planning);
        CommandDoctrineOrder keepOrder = Orders(op, ExposedLine(), CommandNodeRole.MainEffort)[0];
        AssertFalse(PostureStanceGuard.Apply(BattleStance.Attacker, ref keep, hasOrder: true, ref keepOrder), "an attacker keeps its attack");
        AssertEqual(CommandTaskType.AttackObjective, keep.Task, "unchanged");
        AssertEqual(CommandTaskType.HoldObjective, PostureStanceGuard.Task(CommandTaskType.SupportAttack, BattleStance.Defender), "leaf task");
    }

    private static string MethodBody(string text, string start, string end)
    {
        int a = text.IndexOf(start, StringComparison.Ordinal);
        int b = a < 0 ? -1 : text.IndexOf(end, a, StringComparison.Ordinal);
        AssertTrue(a >= 0 && b > a, "found " + start);
        return text.Substring(a, b - a);
    }

    private static string ProducerSource(string file) =>
        System.IO.File.ReadAllText(System.IO.Path.Combine(TestFixtures.FindRepoPath("src/WhiskeyRealism"), "Tactical", "Battle", "Producers", file));

    private static void AssertInOrder(string body, string label, params string[] marks)
    {
        int at = -1;
        foreach (string mark in marks)
        {
            int next = body.IndexOf(mark, at + 1, StringComparison.Ordinal);
            AssertTrue(next > at, label + ": " + mark + " in order");
            at = next;
        }
    }

    private static void PostureGuardCallSitesPinned()
    {
        string text = ProducerSource("PostureIntentProducer.cs");
        string run = MethodBody(text, "internal static bool Run(", "private static bool CanBypassStaleQueuedOrder(");
        AssertInOrder(run, "posture pipeline", "TacticalGrandTacticianReconDoctrine.Decide(", "TryCavalryFollow(",
            "TaskForLocalFlankEmergency(", "TacticalOpportunityTaskOverride.Override(", "PostureStanceGuard.Apply(", "CommandPostureExecutor.Decide(");
        string leaf = MethodBody(text, "private static void ProduceLeaf(", "private static CommandTaskType ResolveMainArrivalHold(");
        AssertInOrder(leaf, "leaf cascade", "TacticalOpportunityTaskOverride.Override(", "ResolveMainArrivalHold(", "PostureStanceGuard.Task(",
            "LeafBrigadeMovePolicy.Decide(");
    }

    private static void DivisionPlayReadsArmyStance()
    {
        string text = ProducerSource("PlayIntentProducer.cs");
        AssertTrue(text.Contains("army.CurrentBattleStance == BattleStance.Defender"), "the division play reads the army's stance");
        AssertFalse(text.Contains("ArmyPostureInputsRuntime.SideDefending"), "no second stance reader");
    }

    // ------------------------------------------------------------------ integrator round 4

    private static TacticalGroupStanceDecision SectorStance(int macroAi, TacticalSectorAssessment sector, bool defending) =>
        TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
            vanillaStance: 2, macroAi: macroAi, sector: sector, orderFrictionAllowsChange: true, wlAllowsControl: true,
            sideDefending: defending));

    private static void DefaultScorerDefenderCapped()
    {
        var weakPoint = new TacticalSectorAssessment(0, TacticalSectorSource.VisibleLineContact, 1000f, 400f, 0.85f,
            strongPoint: false, flankRisk: false, mission: TacticalSectorMission.AttackWeakPoint);
        var counterstroke = new TacticalSectorAssessment(4, TacticalSectorSource.VisibleLineContact, 17651f, 914f, 0.85f,
            strongPoint: false, flankRisk: false, mission: TacticalSectorMission.AttackWeakPoint);

        TacticalGroupStanceDecision attack = SectorStance(1, weakPoint, defending: false);
        AssertEqual(3, attack.GroupStance, "control: a meeting side attacks the weak point");
        AssertEqual("attack-weak-point", attack.Reason, "control reason");
        TacticalGroupStanceDecision counter = SectorStance(2, counterstroke, defending: false);
        AssertEqual(3, counter.GroupStance, "control: a meeting side's counterstroke");
        AssertEqual("defensive-counterstroke", counter.Reason, "control reason");

        TacticalGroupStanceDecision dAttack = SectorStance(1, weakPoint, defending: true);
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceDefend, dAttack.GroupStance, "a defender's weak point: defend, not attack");
        AssertEqual(TacticalDoctrineDecisionKind.Apply, dAttack.Kind, "still Whiskey's decision");
        TacticalGroupStanceDecision dCounter = SectorStance(2, counterstroke, defending: true);
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceDefend, dCounter.GroupStance, "a defender's counterstroke: defend, not attack");
    }
}
