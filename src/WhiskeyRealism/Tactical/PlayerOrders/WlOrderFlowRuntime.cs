using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    // The battle half of the W&L order flow (plan 2026-10-15-wl-order-flow.md): the game-facing side
    // of WlOrderFlow. Every order that reaches the W&L player's panel goes through
    // AIBattle.CheckCurrentOrderUpdate (decompile 8233-8667, build 14703111); the capture Prefix
    // (WlOrderCapturePatch) takes each new order bound for the player into a courier queue and the
    // delivery Postfix (WlOrderDeliveryPatch, after AIBattle.UpdateDLCPlayerOrders 6747) calls vanilla
    // for it once the courier has arrived, so vanilla's own gates and texts still run.
    //
    // Vanilla anchors:
    //   8251-8369  the gates before the dedupe (WlOrderCaptureGate mirrors them)
    //   8640-8646  the dedupe (PlayerOrderReadmitPolicy.PredictVanilla)
    //   8648-8664  GivenOrders construction, session bump, panel
    //   6777-6811  CheckRemovalOfOrders: 15 (macroai 3, non-null slot), 13/14 on contact
    //   125009-125171  AddOrderCourierline: the delay model (WlCourierDelay)
    //
    // State: the courier queue, the order-age tracker and the per-frame cache of the player's
    // command, all battle-scoped (BattleScope). Patches only drive this runtime; they write nothing
    // themselves. The slot (DLC_WL.givenorder) is written inside the delivery transaction only
    // (WlOrderFlow.RunPass): emptied for a re-admitted order, restored when vanilla refuses it.
    // No movement, stance, formation, facing or combat-behaviour writer is ever called on a
    // player-owned unit here.
    internal static class WlOrderFlowRuntime
    {
        private const float ZoneWidth = 100f;      // the zone sizes #62 and the executor always passed
        private const float ZoneDepth = 50f;
        private const float RetreatZone = 50f;     // vanilla's own retreat order, 6798
        private const float ObjectiveMeters = 150f;
        private const int MaxChainLegs = 16;

        // Set by the delivery transaction around its call into vanilla; the capture Prefix lets a call
        // through while it is set (so a delivered order, and the type-11 relay vanilla makes while
        // delivering it, are not captured twice).
        internal static bool Delivering { get => WlOrderBypass.Active; private set => WlOrderBypass.Active = value; }
        // Set by the capture Prefix when it takes or drops a call; read by the observer Postfix so its
        // telemetry does not read a captured call as a vanilla drop; cleared by the capture Finalizer.
        internal static bool CapturedThisCall { get; private set; }

        private static readonly BattleHost Host = new BattleHost();
        internal static readonly WlOrderFlow Flow = new WlOrderFlow(Host);
        private static readonly OrderAgeTracker Age = new OrderAgeTracker();
        private static readonly CourierLeg[] Legs = new CourierLeg[MaxChainLegs];
        private static readonly int[] Chain = new int[MaxChainLegs];
        private static readonly PlayerOrderDiagnostics DropLog = new PlayerOrderDiagnostics(128);
        private static readonly ReleaseLatch Latch = new ReleaseLatch();
        // Scratch for the routed test and the fire-range scan (grown on demand, never per call).
        private static float[] _strength = new float[64];
        private static bool[] _routedFlags = new bool[64];
        // D21: the order session whose first-entry prestige is suppressed (an automatic hold).
        private static int _nonPayingSession = int.MinValue;
        // criterion-no-automatic-prestige: what each order session paid, sampled per delivery pass. The
        // origin of the session comes from the delivery that started it (anything else is vanilla's own).
        private static int _prestigeSession = int.MinValue;
        private static OrderOrigin _prestigeOrigin = OrderOrigin.Vanilla;
        private static float _prestigeLast = float.NaN;
        private static float _prestigeSessionDelta;
        private static int _originSession = int.MinValue;
        private static OrderOrigin _originOfSession = OrderOrigin.Vanilla;
        // D17 (b): the order session in which a 1-4 order was first seen reached, when (doctrine hours), and
        // the session a follow-up was already built for.
        private static int _reachedSession = int.MinValue;
        private static double _reachedAt;
        private static int _followUpSession = int.MinValue;

        // The player's command, parent unit and campaign group, read once per frame (vanilla
        // recomputes them per call, 8263-8265, and GetParentUnit allocates, 93883).
        private static int _frame = -1;
        private static bool _hasCommand;
        private static bool _isCic;
        private static Regiment _command;
        private static Regiment _parent;
        private static Regiment _campaignGroup;
        private static BattleUnits _bunits;
        private static FieldInfo _bunitsField;
        private static bool _bunitsFieldMissing;
        // The player-side AIBattle (isplayeraiorfeud == 1): where macroai and newmacroaitoset live.
        private static AIBattle _playerBattle;
        private static double _lastRetreatBuildHours = double.NegativeInfinity;
        private static int _boundaryBattleId;
        private static int _boundaryCommandId;
        private static string _boundaryBattleKey = string.Empty;
        private static string _boundaryCommandKey = string.Empty;

        static WlOrderFlowRuntime()
        {
            BattleScope.Register("wl-order-flow", ResetBattle);
        }

        private static void ResetBattle()
        {
            try { Flow.Queue.Clear("battle-end"); }
            catch { }
            Age.Reset();
            Flow.ResetHold();
            Delivering = false;
            CapturedThisCall = false;
            _frame = -1;
            _hasCommand = false;
            _command = null;
            _parent = null;
            _campaignGroup = null;
            _bunits = null;
            _playerBattle = null;
            _lastRetreatBuildHours = double.NegativeInfinity;
            Latch.Reset();
            _nonPayingSession = int.MinValue;
            _reachedSession = int.MinValue;
            _reachedAt = 0.0;
            _followUpSession = int.MinValue;
            EnemyRetreatGuard.Clear();
            _prestigeSession = int.MinValue;
            _prestigeOrigin = OrderOrigin.Vanilla;
            _prestigeLast = float.NaN;
            _prestigeSessionDelta = 0f;
            _originSession = int.MinValue;
            _originOfSession = OrderOrigin.Vanilla;
            _boundaryBattleId = 0;
            _boundaryCommandId = 0;
            _boundaryBattleKey = string.Empty;
            _boundaryCommandKey = string.Empty;
        }

        // ---------------------------------------------------------------- switches

        private static bool MasterEnabled()
        {
            return Plugin.Instance != null && Plugin.Instance.Enabled != null && Plugin.Instance.Enabled.Value;
        }

        private static bool ReadmitOn()
        {
            return Plugin.EnableWlOrderReadmit != null && Plugin.EnableWlOrderReadmit.Value;
        }

        private static bool CouriersOn()
        {
            return Plugin.EnableWlOrderCouriers != null && Plugin.EnableWlOrderCouriers.Value;
        }

        private static bool EnemyRetreatGuardOn()
        {
            return Plugin.EnableWlEnemyRetreatGuard != null && Plugin.EnableWlEnemyRetreatGuard.Value;
        }

        private static bool TextOn()
        {
            return Plugin.EnableWlOrderText != null && Plugin.EnableWlOrderText.Value;
        }

        private static bool FollowUpOn()
        {
            return Plugin.EnableWlOrderFollowUp != null && Plugin.EnableWlOrderFollowUp.Value;
        }

        // ----------------------------------------------------------------- capture

        // One CheckCurrentOrderUpdate call, or one order a Whiskey emitter wants delivered.
        private struct OrderCall
        {
            public Regiment Unit;
            public int Type;
            public Vector3 Position;
            public string Name;
            public float Rotation, Width, Depth;
            public OrderOrigin Origin;
            public bool Campaign;
            public PlayerOrderCandidate P62;
            public OrderIntentKind Kind;
        }

        // The capture Prefix: false means vanilla must not run this call (it was queued or dropped).
        // Returns true (vanilla runs) on any exception.
        internal static bool Capture(Regiment unit, int type, Vector3 position, string destinationName,
            float rotation, float width, float depth, bool calledFromCampaign)
        {
            try
            {
                if (Delivering) return true;
                var call = new OrderCall
                {
                    Unit = unit, Type = type, Position = position, Name = destinationName,
                    Rotation = rotation, Width = width, Depth = depth, Origin = OrderOrigin.Vanilla, Campaign = calledFromCampaign,
                };
                CaptureVerdict verdict = Route(in call, out _);
                if (verdict == CaptureVerdict.Pass) return true;
                CapturedThisCall = true;
                return false;
            }
            catch (Exception ex)
            {
                CapturedThisCall = false;
                OnceLog.Warning("wl-order:capture-failed", "[W&LOrder] capture failed; the call goes to vanilla: " + ex.GetType().Name + " " + ex.Message);
                return true;
            }
        }

        internal static void ClearCapturedFlag()
        {
            CapturedThisCall = false;
        }

        // The unit the order flow gives #62's orders to: the player's parent unit (the unit vanilla's own
        // gate computes at 8264 and 6463 passes), never his own command, which vanilla's 8337 refuses for
        // a subordinate. Cached once per frame.
        internal static Regiment PlayerParentUnit()
        {
            RefreshFrame();
            if (!_hasCommand) return null;
            return (UnityEngine.Object)_parent != null ? _parent : _command;
        }

        // Doctrine hours since the order on the panel was accepted (+infinity when Whiskey never saw it issued).
        internal static double ActiveOrderAgeHours()
        {
            return Age.AgeHours(TacticalDoctrineTime.NowBattleHours);
        }

        // Whiskey's own emitters enqueue directly: the executor's ancestor dispatch (origin Executor).
        // True when the order went on a courier. With both order-flow switches off the call goes to vanilla
        // as it always did, and the result is whether vanilla showed a new order.
        internal static bool EnqueueWhiskey(Regiment unit, int type, float x, float z, float rotation, string destinationName, OrderOrigin origin, OrderIntentKind kind)
        {
            var call = new OrderCall
            {
                Unit = unit, Type = type, Position = new Vector3(x, 0f, z), Name = destinationName,
                Rotation = rotation, Width = ZoneWidth, Depth = ZoneDepth, Origin = origin, Kind = kind,
            };
            return EnqueueCall(in call);
        }

        // #62's order, with the candidate its bookkeeping follows through the delivery callback.
        internal static bool EnqueueP62(Regiment unit, in PlayerOrderCandidate candidate, string destinationName, OrderIntentKind kind)
        {
            var call = new OrderCall
            {
                Unit = unit, Type = candidate.VanillaType, Position = new Vector3(candidate.TargetPoint.X, 0f, candidate.TargetPoint.Z),
                Name = destinationName, Rotation = candidate.Rotation, Width = ZoneWidth, Depth = ZoneDepth,
                Origin = OrderOrigin.P62, P62 = candidate, Kind = kind,
            };
            return EnqueueCall(in call);
        }

        private static bool EnqueueCall(in OrderCall call)
        {
            if (!TacticalWriteGuard.ModeAllowsWrites()) return false;
            if (!MasterEnabled()) return false;
            if (!ReadmitOn() && !CouriersOn()) return CallVanillaDirect(in call);
            CaptureVerdict verdict = Route(in call, out bool queued);
            // A reserve order onto an active reserve order is vanilla's in-place refresh of the reserve zone
            // (flag4, 8549-8552): the capture passes it, so the emitter's call goes to vanilla as it did before.
            if (verdict == CaptureVerdict.Pass && call.Type == 11 && DLC_WL.givenorder != null && DLC_WL.givenorder.type == 11)
                CallVanillaDirect(in call);
            return verdict == CaptureVerdict.Queue && queued;
        }

        private static bool CallVanillaDirect(in OrderCall call)
        {
            int before = DLC_WL.GivenOrders.givenorderssession;
            AIBattle.CheckCurrentOrderUpdate(call.Unit, call.Type, call.Position, call.Name ?? string.Empty, call.Rotation, call.Width, call.Depth);
            return DLC_WL.GivenOrders.givenorderssession != before;
        }

        // The capture decision for one call, shared by the Prefix and the Whiskey emitters: gather the
        // facts cheapest first, ask WlOrderFlow, and queue the order when it says so.
        private static CaptureVerdict Route(in OrderCall call, out bool queued)
        {
            queued = false;
            if (call.Campaign) return RouteCampaign(in call);
            if (TryDropEnemySideRetreat(in call)) return CaptureVerdict.Drop;
            Regiment unit = call.Unit;
            int type = call.Type;
            var input = default(CaptureInput);
            input.Enabled = MasterEnabled();
            input.ReadmitOn = ReadmitOn();
            input.CouriersOn = CouriersOn();
            input.Delivering = Delivering;
            input.Facts.CampaignCall = call.Campaign;
            if (!input.Enabled || (!input.ReadmitOn && !input.CouriersOn) || input.Delivering)
                return CaptureVerdict.Pass;

            RefreshFrame();
            input.Facts.WlActive = DLC_WL.dlc_scenarioactive;
            input.Facts.HasCurrentCommand = _hasCommand;
            input.Facts.PlayerIsCic = _isCic;
            if (!input.Facts.WlActive || !_hasCommand || _isCic) return CaptureVerdict.Pass;
            input.ModeAllowsWrites = TacticalWriteGuard.ModeAllowsWrites();
            if (WlOrderFlow.PassesUntouched(in input, out _)) return CaptureVerdict.Pass;

            // Cheap gates first (8259): the enemy side's calls end here without a chain test.
            BattleUnits bunits = Bunits();
            input.Facts.Type = type;
            input.Facts.EodCycle = bunits != null && bunits.eodcycle > 0;
            input.Facts.UnitMissing = (UnityEngine.Object)unit == null || (UnityEngine.Object)unit.transform.parent == null;
            input.Facts.UnitIsPlayerAlliance = !input.Facts.UnitMissing && unit.alliance == GameVars.playeralliance;
            if (WlOrderCaptureGate.CheapReject(in input.Facts)) return CaptureVerdict.Pass;

            FillChainFacts(ref input.Facts, unit, type, bunits);
            if (!WlOrderCaptureGate.ReachesDedupe(in input.Facts)) return CaptureVerdict.Pass;

            double now = TacticalDoctrineTime.NowBattleHours;
            Age.Observe(DLC_WL.GivenOrders.givenorderssession, now);
            bool havePlayerSnapshot = BattleCore.TryGetSnapshot(GameVars.playeralliance, out BattleSnapshot snapshot) && snapshot != null;
            // N1: while the player's command is routed vanilla's 13 and 14 are dropped (D6).
            if ((type == 13 || type == 14) && input.ReadmitOn && havePlayerSnapshot)
                input.PlayerRouted = IsPlayerRouted(snapshot);

            ActiveOrderView active = Host.ReadActive();
            Vector3 position = call.Position;
            float y = VanillaTerrain.SampleHeight(position, position.y);
            input.Hold = Flow.Hold();
            input.Candidate = new OrderCandidateView(type, position.x, y, position.z, call.Rotation, call.Origin, false);

            CaptureVerdict verdict = WlOrderFlow.DecideCapture(in input, in active, out double deferUntil, out string reason);
            if (verdict == CaptureVerdict.Pass) return verdict;

            if (!havePlayerSnapshot)
            {
                // No battle snapshot: no courier path to measure. Vanilla handles the call as it always did.
                OnceLog.Warning("wl-order:no-snapshot", "[W&LOrder] no battle snapshot for the player's side; orders reach the panel unqueued");
                return CaptureVerdict.Pass;
            }
            if (!TryMakeEntry(snapshot, unit, type, new Vector3(position.x, y, position.z), call.Name, call.Rotation, call.Width, call.Depth,
                    call.Origin, now, deferUntil, out CourierEntry entry))
                return CaptureVerdict.Pass;
            entry.P62Candidate = call.P62;
            entry.Kind = call.Kind;

            if (verdict == CaptureVerdict.Drop)
            {
                Host.Row(in entry, "drop", reason);
                return verdict;
            }

            queued = Flow.Enqueue(in entry, PlayerOrderReadmitPolicy.MinHoldBattleHours);
            if (queued) OnceLog.Info("wl-order:first-capture", "[W&LOrder] first courier queued type=" + type + " origin=" + call.Origin);
            return verdict;
        }

        // A vanilla campaign call (calledfromcampaign: true): vanilla's gate (8366) first, with the
        // facts it reads; the campaign runtime then drops a re-pop of the order on the panel or queues
        // the order on a campaign courier. The battle re-admit policy has no say: vanilla does not
        // dedupe campaign calls (8640).
        private static CaptureVerdict RouteCampaign(in OrderCall call)
        {
            if (Delivering || !WlCampaignCourierRuntime.CaptureWanted()) return CaptureVerdict.Pass;
            if (!ReachesPlayerPanel(call.Unit, call.Type, campaignCall: true)) return CaptureVerdict.Pass;
            return WlCampaignCourierRuntime.Capture(call.Unit, call.Type, call.Position, call.Name, call.Rotation, call.Width, call.Depth);
        }

        // Would vanilla's gates (8251-8369) let this call reach the player's panel? The player's command,
        // parent unit and campaign group come from the per-frame cache; the cheap facts are read first.
        private static bool ReachesPlayerPanel(Regiment unit, int type, bool campaignCall)
        {
            RefreshFrame();
            var facts = default(CaptureFacts);
            facts.CampaignCall = campaignCall;
            facts.Type = type;
            facts.WlActive = DLC_WL.dlc_scenarioactive;
            facts.HasCurrentCommand = _hasCommand;
            facts.PlayerIsCic = _isCic;
            if (!facts.WlActive || !_hasCommand || _isCic) return false;
            BattleUnits bunits = Bunits();
            facts.EodCycle = bunits != null && bunits.eodcycle > 0;
            facts.UnitMissing = (UnityEngine.Object)unit == null || (UnityEngine.Object)unit.transform.parent == null;
            facts.UnitIsPlayerAlliance = !facts.UnitMissing && unit.alliance == GameVars.playeralliance;
            if (WlOrderCaptureGate.CheapReject(in facts)) return false;
            FillChainFacts(ref facts, unit, type, bunits);
            return WlOrderCaptureGate.ReachesDedupe(in facts);
        }

        // BUG-WL-019 (D13, N2): a type-15 call made while the enemy's or the feud's AIBattle is inside
        // CheckRemovalOfOrders is the enemy's retreat sent to the player by mistake. A bug fix, so it does
        // not depend on the order flow's switches or on Commander Mode.
        private static bool TryDropEnemySideRetreat(in OrderCall call)
        {
            if (!EnemyRetreatGuard.IsEnemySideRetreat(call.Type, EnemyRetreatGuard.Stamp)) return false;
            if (!MasterEnabled() || !EnemyRetreatGuardOn()) return false;
            if (!ReachesPlayerPanel(call.Unit, call.Type, call.Campaign)) return false;
            var entry = new CourierEntry
            {
                UnitId = call.Unit.gameObject.GetInstanceID(),
                Type = call.Type,
                Origin = call.Origin,
                IssuerKey = EnemyRetreatGuard.Stamp,
                IssuedAt = TacticalDoctrineTime.NowBattleHours,
            };
            Host.Row(in entry, "drop", "enemy-side-retreat");
            OnceLog.Info("wl-order:first-enemy-retreat-dropped", "[W&LOrder] the enemy side's retreat order was not sent to the player");
            return true;
        }

        private static void FillChainFacts(ref CaptureFacts f, Regiment unit, int type, BattleUnits bunits)
        {
            f.PlayerIsBattleCo = bunits != null && DLC_WL.IsCommandingOfficerBattle(bunits);
            f.CampaignGroupIsOwnCommand = (UnityEngine.Object)_campaignGroup == (UnityEngine.Object)_command;
            // Vanilla's `flag`: true unless the player's parent unit is not under the call's unit (8272).
            f.ParentIsUnderUnit = (UnityEngine.Object)_parent == null || _parent.transform.IsChildOf(unit.transform);
            f.SameGroupAiObject = (UnityEngine.Object)_parent != null && (UnityEngine.Object)unit.groupaiobject == (UnityEngine.Object)_parent.groupaiobject;
            f.UnitIsUnderCommander = unit.dlcw_isundercommander;
            f.UnitIsPlayerCommand = (UnityEngine.Object)unit == (UnityEngine.Object)_command;
            f.UnitInPlayerSubtree = type == 2 && (f.UnitIsPlayerCommand || unit.transform.IsChildOf(_command.transform));
        }

        // ------------------------------------------------------------------ entries

        // Builds the courier entry for an order: its unit, the issuing headquarters (the call's unit when
        // it is an AI ancestor of the player's command, else the player's parent unit, 8341-8347), the
        // courier delay down the snapshot's command chain, and the due time.
        private static bool TryMakeEntry(BattleSnapshot snapshot, Regiment unit, int type, Vector3 position, string destinationName,
            float rotation, float width, float depth, OrderOrigin origin, double now, double deferUntil, out CourierEntry entry)
        {
            entry = default(CourierEntry);
            int unitId = unit.gameObject.GetInstanceID();
            int unitIndex = snapshot.IndexOfOwn(unitId);
            if (unitIndex < 0) return false;

            int playerIndex = snapshot.PlayerCommandIndex;
            int issuerIndex = unitIndex;
            if (playerIndex >= 0 && playerIndex < snapshot.OwnCount && (unitIndex == playerIndex || !snapshot.IsAncestorOf(unitIndex, playerIndex)))
                issuerIndex = snapshot.Own[playerIndex].ParentIndex;
            if (issuerIndex < 0) issuerIndex = playerIndex >= 0 ? playerIndex : unitIndex;

            int legs = BuildLegs(snapshot, issuerIndex, playerIndex, out float meters);
            double delay = WlCourierDelay.BattleHours(Legs, legs, BattlePrefs());

            entry = new CourierEntry
            {
                UnitId = unitId,
                Type = type,
                X = position.x,
                Y = position.y,
                Z = position.z,
                Name = destinationName ?? string.Empty,
                Rotation = rotation,
                Width = width,
                Depth = depth,
                CampaignCall = false,
                Origin = origin,
                IssuerKey = snapshot.Own[issuerIndex].InstanceId,
                IssuedAt = now,
                Legs = legs,
                Meters = meters,
            };
            entry.DueAt = WlOrderFlow.DueAt(now, delay, CouriersOn(), deferUntil);
            // The unit's own move is still riding to it (the executor queues it before it dispatches, and
            // vanilla calls SetWaypoint before CheckCurrentOrderUpdate): the player's order waits for it.
            if (CouriersOn() && WlOrderFlow.ArmsMoveGate(origin, type) && MoveStateOf(unit) != MoveState.Executed)
            {
                entry.AwaitsMove = true;
                entry.LegHours = delay;
            }
            return true;
        }

        // Where the unit's own move stands in its superior's order queue: not queued (carried out), queued,
        // or queued with its courier gone (lost). SetWaypoint queues it on the unit's scene parent, or on the
        // unit itself under the "Units" root (91461-91504), so the scene parent is the question here and
        // not the command tree: this reads the queue vanilla wrote. Reads only; allocation free.
        private static MoveState MoveStateOf(Regiment unit)
        {
            Transform parent = unit.transform.parent;
            Regiment owner = parent != null ? parent.GetComponent<Regiment>() : null;
            if ((UnityEngine.Object)owner == null) owner = unit;
            var queue = owner.orderqueue;
            if (queue == null) return MoveState.Executed;
            GameObject moved = unit.gameObject;
            bool queued = false;
            bool lost = false;
            for (int i = 0; i < queue.Count; i++)
            {
                Regiment.OrderQueue entry = queue[i];
                if (entry == null || entry.ordertype > 1 || (UnityEngine.Object)entry.advisedunit != (UnityEngine.Object)moved) continue;
                queued = true;
                lost |= CourierLost(entry, moved);
            }
            return WlMoveState.Classify(queued, lost);
        }

        // The entry's own line to the moved unit is a mounted courier line (type 1) whose courier is gone:
        // vanilla's lost-order test (125281-125286). Secondary lines to the unit's regiments are not asked.
        private static bool CourierLost(Regiment.OrderQueue entry, GameObject moved)
        {
            var lines = entry.courierline;
            if (lines == null) return false;
            for (int k = 0; k < lines.Count; k++)
            {
                Regiment.OrderQueue.CourierLine line = lines[k];
                if (line != null && line.type == 1 && (UnityEngine.Object)line.targetunit == (UnityEngine.Object)moved
                    && (UnityEngine.Object)line.courierref == null) return true;
            }
            return false;
        }

        // Fills Legs with the path from the issuing headquarters down to the player's command along the
        // snapshot's parent map (never transform.parent); at least one leg, so the floor delay applies.
        private static int BuildLegs(BattleSnapshot snapshot, int issuerIndex, int playerIndex, out float totalMeters)
        {
            totalMeters = 0f;
            float standardBugle = GamePrefs.standardbuglerange;
            int n = 0;
            if (playerIndex >= 0 && playerIndex != issuerIndex)
            {
                int cur = playerIndex;
                while (cur >= 0 && cur != issuerIndex && n < MaxChainLegs)
                {
                    Chain[n++] = cur;
                    cur = snapshot.Own[cur].ParentIndex;
                }
                if (cur != issuerIndex) n = 0; // the issuer is not above the player's command: no chain to ride
            }
            if (n == 0)
            {
                Legs[0] = new CourierLeg(0f, standardBugle);
                return 1;
            }
            for (int j = 0; j < n; j++)
            {
                int receiver = Chain[n - 1 - j];
                int sender = j == 0 ? issuerIndex : Chain[n - j];
                float dx = snapshot.Own[receiver].X - snapshot.Own[sender].X;
                float dz = snapshot.Own[receiver].Z - snapshot.Own[sender].Z;
                float meters = (float)Math.Sqrt(dx * dx + dz * dz);
                Regiment senderUnit = BattleSnapshotBuilder.UnitAt(snapshot, sender, enemy: false);
                float bugle = (UnityEngine.Object)senderUnit != null ? senderUnit.buglerange : standardBugle;
                Legs[j] = new CourierLeg(meters, bugle);
                totalMeters += meters;
            }
            return n;
        }

        private static BattleCourierPrefs BattlePrefs()
        {
            // The courier unit (type 5, mounted) at running pace: unitprefs.txt 231 x 243 x 854.
            float speed = GamePrefs.unitbasicspeed[5, 1] * GamePrefs.runningspeedfact[5] * GamePrefs.basicunitspeed;
            return new BattleCourierPrefs(GamePrefs.processingtimegroupstandard, GamePrefs.processingtimegrouproute,
                GamePrefs.buglestandardrecognitiontime, speed, GameVars.useorderdelays);
        }

        // ----------------------------------------------------------------- delivery

        // The delivery Postfix body: runs on the player-side AIBattle only, once per order pass.
        internal static void OnDeliveryPass(AIBattle battle)
        {
            if (battle == null || battle.isplayeraiorfeud != 1) return;
            if (!MasterEnabled() || (!ReadmitOn() && !CouriersOn())) return;
            if (!DLC_WL.dlc_scenarioactive) return;
            _playerBattle = battle;
            RefreshFrame();
            if (!_hasCommand) return;
            NoteBoundary();
            if (!TacticalWriteGuard.ModeAllowsWrites())
            {
                if (Flow.Queue.Count > 0) Flow.Queue.Clear("mode");
                return;
            }
            if (_isCic) return;
            Age.Observe(DLC_WL.GivenOrders.givenorderssession, TacticalDoctrineTime.NowBattleHours);
            DLC_WL.GivenOrders slot = DLC_WL.givenorder;
            if (slot == null || slot.type != 13) Latch.Reset();
            Flow.RunPass();
            SamplePrestige();
            CorrectStaleIssuer();
        }

        // BUG-WL-022 (D18, N3): the order on the panel names the commander of the unit that issued it; when
        // that commander has since been killed, relieved or swapped, the portrait is the order group's
        // current commander. Writes the portrait id only.
        private static void CorrectStaleIssuer()
        {
            try
            {
                if (_isCic || !TextOn()) return;
                DLC_WL.GivenOrders order = DLC_WL.givenorder;
                if (order == null || (UnityEngine.Object)order.groupunit == null || GameVars.commander == null) return;
                int id = order.commanderid;
                bool exists = id >= 0 && id < GameVars.commander.Count && GameVars.commander[id] != null;
                Regiment command = exists ? GameVars.commander[id].currentcommand : null;
                int owner = (UnityEngine.Object)command != null ? command.commander : -1;
                if (!StaleIssuer.TryReplacement(id, exists, owner, order.groupunit.commander, DLC_WL.dlc_chosencommander, out int replacement)) return;
                order.commanderid = replacement;
                OnceLog.Info("wl-order:first-stale-issuer", "[W&LOrder] the issuer shown on the panel was replaced by the order group's commander");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-order:stale-issuer", "[W&LOrder] correcting the issuer failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // What the order session that just ended paid, as one row (a non-paying session always gets one: it
        // is the control). Reads DLC_WL.prestige and writes nothing.
        private static void SamplePrestige()
        {
            try
            {
                int session = DLC_WL.GivenOrders.givenorderssession;
                float prestige = DLC_WL.prestige;
                if (!float.IsNaN(_prestigeLast)) _prestigeSessionDelta += prestige - _prestigeLast;
                _prestigeLast = prestige;
                if (session == _prestigeSession) return;
                if (_prestigeSession != int.MinValue && (_prestigeSessionDelta != 0f || WlOrderFlow.IsNonPaying(_prestigeOrigin)))
                    EmitPrestigeRow(_prestigeSession, _prestigeOrigin, _prestigeSessionDelta);
                _prestigeSession = session;
                _prestigeSessionDelta = 0f;
                _prestigeOrigin = _originSession == session ? _originOfSession : OrderOrigin.Vanilla;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-order:prestige-sample", "[W&LOrder] sampling prestige failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void EmitPrestigeRow(int session, OrderOrigin origin, float delta)
        {
            string originText = origin.ToString();
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalPlayerOrderFlow", TelemetrySeverity.Info,
                ev => ev
                    .WithDecision("prestige-delta", originText, session.ToString())
                    .WithField("scope", "tactical")
                    .WithField("action", "prestige-delta")
                    .WithField("origin", originText)
                    .WithField("session", session)
                    .WithField("delta", (double)delta));
        }

        // The battle, the player's command or his CIC status changed: queued orders belong to the old one.
        private static void NoteBoundary()
        {
            int battleId = (UnityEngine.Object)_bunits != null ? _bunits.GetInstanceID() : 0;
            int commandId = _command.GetInstanceID();
            if (battleId != _boundaryBattleId)
            {
                _boundaryBattleId = battleId;
                _boundaryBattleKey = "battle-" + battleId;
            }
            if (commandId != _boundaryCommandId)
            {
                _boundaryCommandId = commandId;
                _boundaryCommandKey = "unit-" + commandId;
            }
            Flow.NoteBoundary(_boundaryBattleKey, _boundaryCommandKey, _isCic);
        }

        // ------------------------------------------------------------------- helpers

        private static void RefreshFrame()
        {
            int frame = Time.frameCount;
            if (frame == _frame) return;
            _frame = frame;
            _hasCommand = false;
            _isCic = false;
            _command = null;
            _parent = null;
            _campaignGroup = null;
            if (!DLC_WL.dlc_scenarioactive) return;
            int chosen = DLC_WL.dlc_chosencommander;
            if (chosen < 0 || GameVars.commander == null || chosen >= GameVars.commander.Count) return;
            Regiment command = GameVars.commander[chosen].currentcommand;
            if ((UnityEngine.Object)command == null) return;
            _command = command;
            _parent = BattleUnits.GetParentUnit(command);
            _campaignGroup = BattleUnits.GetCampaignGroup(command);
            _isCic = DLC_WL.IsCommanderInChief();
            _hasCommand = true;
        }

        // AIBattle keeps the scene's BattleUnits in a private static (3306).
        private static BattleUnits Bunits()
        {
            if ((UnityEngine.Object)_bunits != null) return _bunits;
            if (_bunitsFieldMissing) return null;
            try
            {
                if (_bunitsField == null)
                {
                    _bunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
                    if (_bunitsField == null)
                    {
                        _bunitsFieldMissing = true;
                        OnceLog.Warning("wl-order:bunits-field", "[W&LOrder] AIBattle.bunits not found; end-of-day gate unavailable");
                        return null;
                    }
                }
                _bunits = _bunitsField.GetValue(null) as BattleUnits;
            }
            catch (Exception ex)
            {
                _bunitsFieldMissing = true;
                OnceLog.Warning("wl-order:bunits-field", "[W&LOrder] AIBattle.bunits unreadable: " + ex.GetType().Name + " " + ex.Message);
            }
            return _bunits;
        }

        private static Regiment ResolveUnit(int instanceId)
        {
            if (!BattleCore.TryGetSnapshot(GameVars.playeralliance, out BattleSnapshot snapshot) || snapshot == null) return null;
            int index = snapshot.IndexOfOwn(instanceId);
            if (index < 0) return null;
            Regiment unit = BattleSnapshotBuilder.UnitAt(snapshot, index, enemy: false);
            return (UnityEngine.Object)unit != null ? unit : null;
        }

        // D6: at least RoutedShare of the strength of the player's command's regiments (its leaf units) is
        // routed or retreating on the side's snapshot.
        private static bool IsPlayerRouted(BattleSnapshot snapshot)
        {
            int n = 0;
            for (int i = 0; i < snapshot.OwnCount; i++)
            {
                UnitFlags flags = snapshot.Own[i].Flags;
                if ((flags & UnitFlags.PlayerOwned) == 0 || (flags & UnitFlags.IsGroup) != 0 || (flags & UnitFlags.Alive) == 0) continue;
                if (n == _strength.Length)
                {
                    Array.Resize(ref _strength, n * 2);
                    Array.Resize(ref _routedFlags, n * 2);
                }
                _strength[n] = snapshot.Own[i].Strength;
                _routedFlags[n] = (flags & (UnitFlags.Routed | UnitFlags.OnRetreat)) != 0;
                n++;
            }
            return PlayerOrderReadmitPolicy.Routed(_strength, _routedFlags, n);
        }

        // D5: does any regiment of the player's command see a Visible enemy inside its own fire range?
        // Fog-honest: the side's observations only.
        private static bool VisibleEnemyInFireRange(BattleSnapshot snapshot)
        {
            for (int i = 0; i < snapshot.OwnCount; i++)
            {
                UnitFlags flags = snapshot.Own[i].Flags;
                if ((flags & UnitFlags.PlayerOwned) == 0 || (flags & UnitFlags.IsGroup) != 0 || (flags & UnitFlags.Alive) == 0) continue;
                if (snapshot.ClosestVisibleEnemy(i, out float distance) >= 0 && distance <= snapshot.Own[i].FireRange) return true;
            }
            return false;
        }

        // Vanilla's own test for issuing engage-at-will (6800): an HQ command is in contact when a
        // subordinate is engaged, a regiment command when vanilla's unfiltered search finds an enemy in
        // fire range. Read only to predict vanilla's own panel write; it never feeds an AI decision.
        private static bool VanillaContactTrigger()
        {
            Regiment command = _command;
            if ((UnityEngine.Object)command == null) return false;
            if (command.unittyp >= 14) return command.combatstatussubordinates > 0f;
            return (UnityEngine.Object)command.GetClosestEnemyUnitReg(command.GetFireRange()) != null;
        }

        // D21: vanilla's first-entry prestige (CheckDLCWLTopics 123484-123492) fires when a player-owned top
        // unit's lastcombatzonecount is below the order session. For an automatic hold that session's
        // count is set up front on every such unit (own units from the snapshot), so the branch never
        // fires. Order bookkeeping that vanilla itself saves (71810), not a movement or combat field.
        private static void StampTopUnitsAt(int session)
        {
            if (!BattleCore.TryGetSnapshot(GameVars.playeralliance, out BattleSnapshot snapshot) || snapshot == null) return;
            for (int i = 0; i < snapshot.OwnCount; i++)
            {
                UnitRecord record = snapshot.Own[i];
                if ((record.Flags & UnitFlags.PlayerOwned) == 0 || record.ParentRegimentId != 0) continue;
                Regiment unit = BattleSnapshotBuilder.UnitAt(snapshot, i, enemy: false);
                if ((UnityEngine.Object)unit != null) unit.lastcombatzonecount = session;
            }
        }

        // Is the objective nearest the point (within ObjectiveMeters) held by the player's alliance?
        private static bool ObjectiveHeld(Vector3 point)
        {
            Objectives nearest = WlOrderTextRuntime.NearestObjective(point, out _, out float meters);
            return nearest != null && meters <= ObjectiveMeters && nearest.owner == GameVars.playeralliance;
        }

        // One flow row (TacticalPlayerOrderFlow). Capture-time drops come from continuous vanilla callers
        // and are bounded by a ring of recent signatures; every other row is one event.
        private static void EmitRow(in CourierEntry e, string action, string reason)
        {
            try
            {
                double deliveredH = TacticalDoctrineTime.NowBattleHours;
                if (action == "drop")
                {
                    string signature = reason + "|" + e.Type + "|" + e.IssuerKey;
                    if (!DropLog.ShouldLog("drop", signature)) return;
                }
                CourierEntry row = e;
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalPlayerOrderFlow", TelemetrySeverity.Info,
                    ev => ev
                        .WithDecision(action, reason, row.IssuerKey + "|" + row.Type)
                        .WithField("scope", "tactical")
                        .WithField("action", action)
                        .WithField("reason", reason)
                        .WithField("type", row.Type)
                        .WithField("origin", row.Origin.ToString())
                        .WithField("issuer", row.IssuerKey)
                        .WithField("issuedH", row.IssuedAt)
                        .WithField("dueH", row.DueAt)
                        .WithField("deliveredH", deliveredH)
                        .WithField("legs", row.Legs)
                        .WithField("meters", (double)row.Meters)
                        .WithField("dest", row.Name ?? string.Empty)
                        .WithField("session", DLC_WL.GivenOrders.givenorderssession));
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------- host

        // WlOrderFlow's view of the game: the order slot, the clock, vanilla's call.
        private sealed class BattleHost : IWlOrderHost
        {
            public double NowHours => TacticalDoctrineTime.NowBattleHours;

            public bool EodCycle
            {
                get
                {
                    BattleUnits bunits = Bunits();
                    return bunits != null && bunits.eodcycle > 0;
                }
            }

            public bool ReadmitOn => WlOrderFlowRuntime.ReadmitOn();

            public object SlotObject => DLC_WL.givenorder;

            public int SlotType
            {
                get
                {
                    DLC_WL.GivenOrders order = DLC_WL.givenorder;
                    return order == null ? -1 : order.type;
                }
            }

            public int Session => DLC_WL.GivenOrders.givenorderssession;

            public ActiveOrderView ReadActive()
            {
                DLC_WL.GivenOrders order = DLC_WL.givenorder;
                if (order == null) return default(ActiveOrderView);
                double now = NowHours;
                bool defensive = _playerBattle != null && (_playerBattle.newmacroaitoset == 2 || _playerBattle.macroai == 2);
                return new ActiveOrderView(true, order.type, order.position.x, order.position.y, order.position.z, order.arearotation,
                    Age.AgeHours(now), now, ObjectiveHeld(order.position), defensive,
                    (UnityEngine.Object)_campaignGroup == (UnityEngine.Object)_command);
            }

            public bool UnitGone(in CourierEntry e) => ResolveUnit(e.UnitId) == null;

            public MoveState MoveOf(in CourierEntry e)
            {
                Regiment unit = ResolveUnit(e.UnitId);
                // A unit that is gone has no move to wait for; Process has already discarded it.
                return unit == null ? MoveState.Executed : MoveStateOf(unit);
            }

            // The release is stale when contact has resumed (or the hysteresis no longer holds) while its
            // courier rode: the same test that derived it, on fresh facts.
            public bool ReleaseStillDue(in CourierEntry e)
            {
                if (_isCic || !ReadmitOn()) return false;
                if (!BattleCore.TryGetSnapshot(GameVars.playeralliance, out BattleSnapshot snapshot) || snapshot == null) return false;
                return PlayerOrderReadmitPolicy.ReleaseDue(VisibleEnemyInFireRange(snapshot), VanillaContactTrigger(),
                    Latch.QuietSince, NowHours, IsPlayerRouted(snapshot));
            }

            public void SetDelivering(bool on) => Delivering = on;

            public void ClearSlot() => DLC_WL.givenorder = null;

            public void RestoreSlot(object saved) => DLC_WL.givenorder = (DLC_WL.GivenOrders)saved;

            public void CallVanilla(in CourierEntry e)
            {
                Regiment unit = ResolveUnit(e.UnitId);
                if (unit == null) return;
                if (!TacticalWriteGuard.ModeAllowsWrites()) return;
                AIBattle.CheckCurrentOrderUpdate(unit, e.Type, new Vector3(e.X, e.Y, e.Z), e.Name ?? string.Empty, e.Rotation, e.Width, e.Depth);
            }

            public void VanillaThrew(in CourierEntry e, Exception ex)
            {
                OnceLog.Warning("wl-order:vanilla-threw", "[W&LOrder] vanilla CheckCurrentOrderUpdate threw for a delivered order (type " + e.Type + "): " + ex.GetType().Name + " " + ex.Message);
            }

            public void Accepted(in CourierEntry e)
            {
                _originSession = Session;
                _originOfSession = e.Origin;
                Age.Accepted(Session, NowHours);
                WlOrderTextRuntime.OnAccepted(in e);
                OnceLog.Info("wl-order:first-delivery", "[W&LOrder] first order delivered type=" + e.Type + " origin=" + e.Origin);
            }

            public void Row(in CourierEntry e, string action, string reason) => EmitRow(in e, action, reason);

            public void NotifyP62(in CourierEntry e, P62Outcome outcome)
            {
                Tactical.Battle.TacticalPlayerDispatchRuntime.OnP62Outcome(_playerBattle, e.P62Candidate, outcome, NowHours);
            }

            public void MarkNonPayingSession(int session)
            {
                _nonPayingSession = session;
                StampTopUnitsAt(session);
            }

            public void RestampNonPayingSession()
            {
                if (_nonPayingSession == int.MinValue) return;
                if (_nonPayingSession != Session) { _nonPayingSession = int.MinValue; return; }
                StampTopUnitsAt(_nonPayingSession);
            }

            // D5/D6: the hold at the player's position that releases engage-at-will.
            public bool TryBuildRelease(out CourierEntry entry)
            {
                entry = default(CourierEntry);
                if (_isCic || (UnityEngine.Object)_command == null || !ReadmitOn()) return false;
                if (!BattleCore.TryGetSnapshot(GameVars.playeralliance, out BattleSnapshot snapshot) || snapshot == null) return false;
                double now = NowHours;
                bool routed = IsPlayerRouted(snapshot);
                bool visible = VisibleEnemyInFireRange(snapshot);
                bool trigger = VanillaContactTrigger();
                double quiet = Latch.Update(visible || trigger, now);
                if (!PlayerOrderReadmitPolicy.ReleaseDue(visible, trigger, quiet, now, routed)) return false;

                Regiment unit = (UnityEngine.Object)_parent != null ? _parent : _command;
                Vector3 here = _command.GetPosition();
                if (!TryMakeEntry(snapshot, unit, 12, new Vector3(here.x, VanillaTerrain.SampleHeight(here, here.y), here.z),
                        WlOrderTextRuntime.DestinationFor(here.x, here.z, null),
                        0f, ZoneWidth, ZoneDepth, OrderOrigin.Release, now, double.NaN, out entry))
                    return false;
                entry.Kind = OrderIntentKind.Release;
                OnceLog.Info("wl-order:first-release", "[W&LOrder] engage-at-will released reason=" + (routed ? "routed" : "no-contact"));
                return true;
            }

            // D17 (b): vanilla sets firstunitreached on a 1-4 order when a player-owned top unit enters its
            // zone (123517). Once that has held for the hold window and nothing newer waits, the superior
            // tells the player to hold the position: a type 12 at the zone, a non-paying automatic hold.
            public bool TryBuildFollowUp(out CourierEntry entry)
            {
                entry = default(CourierEntry);
                if (_isCic || !FollowUpOn() || !ReadmitOn() || (UnityEngine.Object)_command == null) return false;
                DLC_WL.GivenOrders order = DLC_WL.givenorder;
                if (order == null || order.type < 1 || order.type > 4) return false;
                int session = Session;
                if (!order.firstunitreached)
                {
                    _reachedSession = int.MinValue;
                    return false;
                }
                double now = NowHours;
                if (_reachedSession != session)
                {
                    _reachedSession = session;
                    _reachedAt = now;
                    return false;
                }
                if (_followUpSession == session) return false;
                if (now - _reachedAt < PlayerOrderReadmitPolicy.MinHoldBattleHours - 1e-9) return false;
                if (!BattleCore.TryGetSnapshot(GameVars.playeralliance, out BattleSnapshot snapshot) || snapshot == null) return false;

                Regiment unit = (UnityEngine.Object)_parent != null ? _parent : _command;
                Vector3 at = order.position;
                if (!TryMakeEntry(snapshot, unit, 12, new Vector3(at.x, VanillaTerrain.SampleHeight(at, at.y), at.z),
                        WlOrderTextRuntime.DestinationFor(at.x, at.z, null),
                        order.arearotation - 180f, ZoneWidth, ZoneDepth, OrderOrigin.FollowUp, now, double.NaN, out entry))
                    return false;
                entry.Kind = OrderIntentKind.FollowUp;
                _followUpSession = session;
                OnceLog.Info("wl-order:first-follow-up", "[W&LOrder] reached order followed up type=" + order.type);
                return true;
            }

            // Still the reached order the follow-up was built for.
            public bool FollowUpStillDue(in CourierEntry e)
            {
                DLC_WL.GivenOrders order = DLC_WL.givenorder;
                return !_isCic && FollowUpOn() && ReadmitOn() && order != null && order.type >= 1 && order.type <= 4
                    && order.firstunitreached && Session == _followUpSession;
            }

            // D8a step 0: the army is retreating (the player-side AIBattle's macroai is 3) and the player
            // is not CIC; vanilla would send a 15 only into a non-null slot (6788), so after a load or at
            // battle start it never comes. The entry is vanilla's own call (6794-6798) on a courier.
            public bool TryBuildRetreat(out CourierEntry entry)
            {
                entry = default(CourierEntry);
                if (_isCic || _playerBattle == null || _playerBattle.macroai != 3) return false;
                double now = NowHours;
                if (now - _lastRetreatBuildHours < PlayerOrderReadmitPolicy.RetryThrottleBattleHours) return false;
                _lastRetreatBuildHours = now;

                Regiment unit = (UnityEngine.Object)_parent != null ? _parent : _command;
                var facts = default(CaptureFacts);
                facts.WlActive = true;
                facts.HasCurrentCommand = true;
                facts.Type = 15;
                BattleUnits bunits = Bunits();
                facts.EodCycle = bunits != null && bunits.eodcycle > 0;
                facts.UnitMissing = (UnityEngine.Object)unit.transform.parent == null;
                facts.UnitIsPlayerAlliance = unit.alliance == GameVars.playeralliance;
                if (WlOrderCaptureGate.CheapReject(in facts)) return false;
                FillChainFacts(ref facts, unit, 15, bunits);
                if (!WlOrderCaptureGate.ReachesDedupe(in facts)) return false;
                if (bunits == null || _command.unitrange == null) return false;

                // 6791-6797: the closest entry point behind the player's command, or the origin.
                Vector3 start = _command.unitrange.flankposition[2];
                EntryPoint exit = bunits.SearchForClosestEntryPoint(start, _command.alliance, false, _command.unitrange.retreatangle, 30f);
                Vector3 position = (UnityEngine.Object)exit != null ? exit.transform.position : default(Vector3);
                if (!BattleCore.TryGetSnapshot(GameVars.playeralliance, out BattleSnapshot snapshot) || snapshot == null) return false;
                return TryMakeEntry(snapshot, unit, 15, position, string.Empty, -1f, RetreatZone, RetreatZone,
                    OrderOrigin.Retreat, now, double.NaN, out entry);
            }
        }
    }
}
