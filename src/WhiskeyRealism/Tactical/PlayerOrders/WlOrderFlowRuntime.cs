using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
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
        internal static bool Delivering { get; private set; }
        // Set by the capture Prefix when it takes or drops a call; read by the observer Postfix so its
        // telemetry does not read a captured call as a vanilla drop; cleared by the capture Finalizer.
        internal static bool CapturedThisCall { get; private set; }

        private static readonly BattleHost Host = new BattleHost();
        internal static readonly WlOrderFlow Flow = new WlOrderFlow(Host);
        private static readonly OrderAgeTracker Age = new OrderAgeTracker();
        private static readonly CourierLeg[] Legs = new CourierLeg[MaxChainLegs];
        private static readonly int[] Chain = new int[MaxChainLegs];
        private static readonly PlayerOrderDiagnostics DropLog = new PlayerOrderDiagnostics(128);

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

        // ----------------------------------------------------------------- capture

        // The capture Prefix: false means vanilla must not run this call (it was queued or dropped).
        // Returns true (vanilla runs) on any exception.
        internal static bool Capture(Regiment unit, int type, Vector3 position, string destinationName,
            float rotation, float width, float depth, bool calledFromCampaign)
        {
            try
            {
                if (Delivering) return true;
                CaptureVerdict verdict = Route(unit, type, position, destinationName, rotation, width, depth,
                    OrderOrigin.Vanilla, calledFromCampaign, out _);
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

        // Whiskey's own emitters (#62 and the executor's ancestor dispatch) enqueue directly. True when
        // the order went on a courier. With both order-flow switches off the call goes to vanilla as it
        // always did, and the result is whether vanilla showed a new order.
        internal static bool EnqueueWhiskey(Regiment unit, int type, float x, float z, float rotation, string destinationName, OrderOrigin origin)
        {
            if (!TacticalWriteGuard.ModeAllowsWrites()) return false;
            if (!MasterEnabled()) return false;
            if (!ReadmitOn() && !CouriersOn()) return CallVanillaDirect(unit, type, x, z, rotation, destinationName);
            CaptureVerdict verdict = Route(unit, type, new Vector3(x, 0f, z), destinationName, rotation, ZoneWidth, ZoneDepth,
                origin, calledFromCampaign: false, out bool queued);
            return verdict == CaptureVerdict.Queue && queued;
        }

        private static bool CallVanillaDirect(Regiment unit, int type, float x, float z, float rotation, string destinationName)
        {
            int before = DLC_WL.GivenOrders.givenorderssession;
            AIBattle.CheckCurrentOrderUpdate(unit, type, new Vector3(x, 0f, z), destinationName ?? string.Empty, rotation, ZoneWidth, ZoneDepth);
            return DLC_WL.GivenOrders.givenorderssession != before;
        }

        // The capture decision for one call, shared by the Prefix and the Whiskey emitters: gather the
        // facts cheapest first, ask WlOrderFlow, and queue the order when it says so.
        private static CaptureVerdict Route(Regiment unit, int type, Vector3 position, string destinationName,
            float rotation, float width, float depth, OrderOrigin origin, bool calledFromCampaign, out bool queued)
        {
            queued = false;
            var input = default(CaptureInput);
            input.Enabled = MasterEnabled();
            input.ReadmitOn = ReadmitOn();
            input.CouriersOn = CouriersOn();
            input.Delivering = Delivering;
            input.Facts.CampaignCall = calledFromCampaign;
            if (!input.Enabled || (!input.ReadmitOn && !input.CouriersOn) || input.Delivering || calledFromCampaign)
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
            ActiveOrderView active = Host.ReadActive();
            float y = VanillaTerrain.SampleHeight(position, position.y);
            input.Candidate = new OrderCandidateView(type, position.x, y, position.z, rotation, origin, false);

            CaptureVerdict verdict = WlOrderFlow.DecideCapture(in input, in active, Flow.Queue.Count > 0, out double deferUntil, out string reason);
            if (verdict == CaptureVerdict.Pass) return verdict;

            if (!BattleCore.TryGetSnapshot(GameVars.playeralliance, out BattleSnapshot snapshot) || snapshot == null)
            {
                // No battle snapshot: no courier path to measure. Vanilla handles the call as it always did.
                OnceLog.Warning("wl-order:no-snapshot", "[W&LOrder] no battle snapshot for the player's side; orders reach the panel unqueued");
                return CaptureVerdict.Pass;
            }
            if (!TryMakeEntry(snapshot, unit, type, new Vector3(position.x, y, position.z), destinationName, rotation, width, depth,
                    origin, now, deferUntil, out CourierEntry entry))
                return CaptureVerdict.Pass;

            if (verdict == CaptureVerdict.Drop)
            {
                Host.Row(in entry, "drop", reason);
                return verdict;
            }

            queued = Flow.Enqueue(in entry, PlayerOrderReadmitPolicy.MinHoldBattleHours);
            if (queued) OnceLog.Info("wl-order:first-capture", "[W&LOrder] first courier queued type=" + type + " origin=" + origin);
            return verdict;
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
            return true;
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
            Flow.RunPass();
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

        // Is the objective nearest the point (within ObjectiveMeters) held by the player's alliance?
        private static bool ObjectiveHeld(Vector3 point)
        {
            Objectives[] all = BattleUnits.objectives;
            if (all == null) return false;
            Objectives nearest = null;
            float best = ObjectiveMeters * ObjectiveMeters;
            for (int i = 0; i < all.Length; i++)
            {
                if ((UnityEngine.Object)all[i] == null) continue;
                Vector3 p = all[i].transform.position;
                float dx = p.x - point.x;
                float dz = p.z - point.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > best) continue;
                best = d2;
                nearest = all[i];
            }
            return nearest != null && nearest.owner == GameVars.playeralliance;
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

            public bool ReleaseStillDue(in CourierEntry e) => false; // no release entries exist yet

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
                Age.Accepted(Session, NowHours);
                OnceLog.Info("wl-order:first-delivery", "[W&LOrder] first order delivered type=" + e.Type + " origin=" + e.Origin);
            }

            public void Row(in CourierEntry e, string action, string reason) => EmitRow(in e, action, reason);

            public void NotifyP62(in CourierEntry e, P62Outcome outcome)
            {
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
