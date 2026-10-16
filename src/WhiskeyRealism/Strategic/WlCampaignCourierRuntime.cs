using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    // The campaign half of the W&L order flow (plan 2026-10-15-wl-order-flow.md D9, D11): the courier
    // queue for orders bound for a top-level W&L player's panel on the campaign map.
    //
    // Two kinds ride it. A vanilla campaign call (AIBattle.CheckCurrentOrderUpdate with
    // calledfromcampaign: true; the capital defense, offensive and engage callers) is taken by the
    // capture Prefix (WlOrderCapturePatch -> WlOrderFlowRuntime.Capture) after vanilla's own gate
    // (8366) says it would reach the panel; it is delivered by calling vanilla again with the bypass
    // (WlOrderBypass). A Whiskey order (the strategic order bridge, #69) is enqueued by
    // WlCampaignDispatchRuntime.Emit and shown by WlCampaignDispatchRuntime.ShowQueued once it
    // arrives, which re-decides it and then records the dispatch history.
    //
    // The delay is vanilla's campaign order delay, WlCourierDelay.CampaignYears (decompile 125033-125128),
    // from the senior commander's command to the player's group, with the issuing headquarters' own
    // factors. Time is campaign years since 1860 (GameVars.currenttimefromstart; the campaign clock
    // stops while the game is paused). Delivery runs in the UpdateUnitAI Postfix right after
    // WlCampaignDispatchRuntime.Flush, only on the campaign map. Vanilla's campaign calls never dedupe
    // (8640), so the battle re-admit policy is never consulted here.
    //
    // State: the queue, a last-drop signature and the command boundary, all campaign-scoped
    // (CampaignScope). The pending entries are saved in the Whiskey sidecar (Export/Restore), so a
    // save inside the delay loses no order. Nothing here moves, stops or reorders a player-owned unit.
    internal static class WlCampaignCourierRuntime
    {
        internal const string Queued = "queued";
        private const int MaxDeliveriesPerPass = WlCourierQueue.MaxPending;

        private static readonly DiscardSink Sink = new DiscardSink();
        private static readonly WlCourierQueue Queue = new WlCourierQueue(Sink);
        private static readonly CourierEntry[] Scratch = new CourierEntry[WlCourierQueue.MaxPending];
        private static readonly List<CourierEntry> Loaded = new List<CourierEntry>(WlCourierQueue.MaxPending);

        // The last drop row written: a continuous vanilla caller repeats the same drop every pass.
        private static string _lastDropReason;
        private static int _lastDropType, _lastDropX, _lastDropZ, _lastDropSession;

        // CurrentCommandKey's cache.
        private static int _keyInstanceId;
        private static int _keyChosen = -1;
        private static string _key;

        // D17 (b): the campaign order last seen on the panel, to notice vanilla clearing it on arrival.
        private static DLC_WL.GivenOrders _watched;

        static WlCampaignCourierRuntime()
        {
            CampaignScope.Register("wl-campaign-courier", ResetCampaign);
        }

        private static void ResetCampaign()
        {
            try { Queue.Clear("campaign-end"); }
            catch { }
            _lastDropReason = null;
            _lastDropType = _lastDropX = _lastDropZ = _lastDropSession = 0;
            _watched = null;
            _keyInstanceId = 0;
            _keyChosen = -1;
            _key = null;
            Loaded.Clear();
        }

        // ---------------------------------------------------------------- switches

        private static bool MasterEnabled()
        {
            return Plugin.Instance != null && Plugin.Instance.Enabled != null && Plugin.Instance.Enabled.Value;
        }

        private static bool CouriersOn()
        {
            return Plugin.EnableWlOrderCouriers != null && Plugin.EnableWlOrderCouriers.Value;
        }

        private static bool CapitalFixOn()
        {
            return Plugin.FixCapitalDefensePlayerOrder != null && Plugin.FixCapitalDefensePlayerOrder.Value;
        }

        private static bool FollowUpOn()
        {
            return MasterEnabled() && Plugin.EnableWlOrderFollowUp != null && Plugin.EnableWlOrderFollowUp.Value;
        }

        private static bool TextOn()
        {
            return Plugin.EnableWlOrderText != null && Plugin.EnableWlOrderText.Value;
        }

        // The capture Prefix has something to do for a campaign call only with one of these on.
        internal static bool CaptureWanted()
        {
            return MasterEnabled() && (CouriersOn() || CapitalFixOn());
        }

        // ----------------------------------------------------------------- capture

        // A vanilla campaign call that vanilla's gate would let reach the player's panel (the caller has
        // run WlOrderCaptureGate): drop it as a re-pop of the order on the panel, queue it on a courier,
        // or let vanilla run it (couriers off). Pass on any exception.
        internal static CaptureVerdict Capture(Regiment unit, int type, Vector3 position, string destinationName,
            float rotation, float width, float depth)
        {
            try
            {
                bool couriers = CouriersOn();
                bool fix = CapitalFixOn();
                GivenOrderView active = WlCampaignDispatchRuntime.CurrentGivenOrder();
                string drop = WlCampaignCourierPolicy.CaptureDropReason(type, position.x, position.z, in active, fix, couriers);
                if (drop != null)
                {
                    Row("drop", drop, type, position.x, position.z, unit, null, OrderOrigin.Vanilla, 0.0, 0.0, 0f);
                    return CaptureVerdict.Drop;
                }
                if (!couriers) return CaptureVerdict.Pass;

                double now = GameVars.currenttimefromstart;
                Regiment hq = SeniorHeadquarters();
                float meters = DistanceToPlayerGroup(hq);
                double delay = WlCourierDelay.CampaignYears(meters, CampaignPrefs(), FactorsOf(hq));
                var entry = new CourierEntry
                {
                    UnitId = unit.GetInstanceID(),
                    UnitName = unit.name,
                    UnitCommanderId = unit.commander,
                    Type = type,
                    X = position.x,
                    Y = position.y,
                    Z = position.z,
                    Name = destinationName ?? string.Empty,
                    Rotation = rotation,
                    Width = width,
                    Depth = depth,
                    CampaignCall = true,
                    Origin = OrderOrigin.Vanilla,
                    IssuerKey = (UnityEngine.Object)hq != null ? hq.GetInstanceID() : unit.GetInstanceID(),
                    IssuerCommanderId = -1,
                    ShowZone = true,
                    IssuedAt = now,
                    DueAt = WlOrderFlow.DueAt(now, delay, couriersOn: true, deferUntil: double.NaN),
                    Meters = meters,
                    Legs = 1,
                    PlayerCommandKey = CurrentCommandKey(),
                };
                if (!Queue.TryEnqueue(in entry, WlCampaignDispatchPolicy.RateLimitYears, out string reason))
                {
                    Row("drop", reason, type, position.x, position.z, unit, entry.Name, OrderOrigin.Vanilla, now, entry.DueAt, meters);
                    return CaptureVerdict.Drop;
                }
                Row("capture", Queued, in entry);
                OnceLog.Info("wl-campaign-courier:first-capture", "[W&LCourier] first campaign courier queued type=" + type + " origin=Vanilla");
                return CaptureVerdict.Queue;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-campaign-courier:capture-failed", "[W&LCourier] campaign capture failed; the call goes to vanilla: " + ex.GetType().Name + " " + ex.Message);
                return CaptureVerdict.Pass;
            }
        }

        // Whiskey's order (WlCampaignDispatchRuntime.Emit, after its own Decide said Issue). Returns
        // Queued, a drop reason (the order is not issued), or null when no courier is used (couriers
        // off, or no entry could be built): the caller shows the order at once, as it always did.
        internal static string EnqueueWhiskey(OrderOrigin origin, Regiment issuerUnit, int issuerCommanderId, int type,
            Vector3 target, string destinationName, float width, float depth, bool showZone)
        {
            if (!MasterEnabled() || !CouriersOn()) return null;
            try
            {
                double now = GameVars.currenttimefromstart;
                Regiment hq = origin == OrderOrigin.C69 && (UnityEngine.Object)issuerUnit != null ? issuerUnit : SeniorHeadquarters();
                float meters = DistanceToPlayerGroup(hq);
                double delay = WlCourierDelay.CampaignYears(meters, CampaignPrefs(), FactorsOf(hq));
                bool hasUnit = (UnityEngine.Object)issuerUnit != null;
                var entry = new CourierEntry
                {
                    UnitId = hasUnit ? issuerUnit.GetInstanceID() : 0,
                    UnitName = hasUnit ? issuerUnit.name : null,
                    UnitCommanderId = hasUnit ? issuerUnit.commander : -1,
                    Type = type,
                    X = target.x,
                    Y = target.y,
                    Z = target.z,
                    Name = destinationName ?? string.Empty,
                    Rotation = -1f,
                    Width = width,
                    Depth = depth,
                    CampaignCall = true,
                    Origin = origin,
                    IssuerKey = (UnityEngine.Object)hq != null ? hq.GetInstanceID() : (hasUnit ? issuerUnit.GetInstanceID() : 0),
                    IssuerCommanderId = issuerCommanderId,
                    ShowZone = showZone,
                    IssuedAt = now,
                    DueAt = WlOrderFlow.DueAt(now, delay, couriersOn: true, deferUntil: double.NaN),
                    Meters = meters,
                    Legs = 1,
                    PlayerCommandKey = CurrentCommandKey(),
                };
                if (!Queue.TryEnqueue(in entry, WlCampaignDispatchPolicy.RateLimitYears, out string reason))
                {
                    Row("drop", reason, type, target.x, target.z, issuerUnit, entry.Name, origin, now, entry.DueAt, meters);
                    return reason;
                }
                Row("capture", Queued, in entry);
                OnceLog.Info("wl-campaign-courier:first-capture", "[W&LCourier] first campaign courier queued type=" + type + " origin=" + origin);
                return Queued;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-campaign-courier:enqueue-failed", "[W&LCourier] queueing a Whiskey campaign order failed; it is shown at once: " + ex.GetType().Name + " " + ex.Message);
                return null;
            }
        }

        // ---------------------------------------------------------------- delivery

        // The UpdateUnitAI Postfix, after the dispatcher's Flush: boundary, then every due courier.
        internal static void DeliverDue()
        {
            if (Queue.Count == 0) return;
            try
            {
                if (!MasterEnabled() || !DLC_WL.dlc_scenarioactive) return;
                Regiment command = PlayerCommand();
                if ((UnityEngine.Object)command == null) return;
                if (DLC_WL.IsCommanderInChief())
                {
                    Queue.Clear("command-change");
                    return;
                }

                double now = GameVars.currenttimefromstart;
                string commandKey = CurrentCommandKey();
                Queue.StampPlayerCommand(commandKey);   // an entry from an old sidecar has no identity yet
                for (int pass = 0; pass < MaxDeliveriesPerPass; pass++)
                {
                    if (!Queue.TryTakeDue(now, out CourierEntry entry, out bool superseded)) break;
                    if (superseded) continue;
                    // An order queued for another command (a promotion since) is void, however empty the
                    // queue was in between.
                    if (!WlCampaignCourierPolicy.BelongsToCommand(entry.PlayerCommandKey, commandKey))
                    {
                        Queue.Discard(in entry, "command-change");
                        continue;
                    }
                    DeliverOne(in entry);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-campaign-courier:deliver-failed", "[W&LCourier] campaign delivery failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // D17 (b), information only: vanilla clears a reached campaign order of type 5, 6, 7 or 16 right
        // after setting firstunitreached on it (123517-123533). The pass keeps the order it last saw; when
        // the slot is empty and that order was reached, the player is told in a short message. Nothing is
        // written to the order slot, the group or any order.
        internal static void AcknowledgeReached()
        {
            try
            {
                if (!FollowUpOn() || !DLC_WL.dlc_scenarioactive) { _watched = null; return; }
                DLC_WL.GivenOrders current = DLC_WL.givenorder;
                if (_watched != null && !ReferenceEquals(current, _watched))
                {
                    DLC_WL.GivenOrders was = _watched;
                    _watched = null;
                    if (WlCampaignCourierPolicy.AcknowledgesClear(was.type, was.firstunitreached, current == null))
                        ShowAcknowledgement(was);
                }
                if (current != null && !current.IsBattleOrder()) _watched = current;
            }
            catch (Exception ex)
            {
                _watched = null;
                OnceLog.Warning("wl-campaign-courier:acknowledge", "[W&LCourier] acknowledging a reached campaign order failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void ShowAcknowledgement(DLC_WL.GivenOrders was)
        {
            CareerInformationPanel panel = DLC_WL.careerinformationpanelref;
            if ((UnityEngine.Object)panel == null) return;
            string place = string.IsNullOrEmpty(was.destinationname) ? "the position" : was.destinationname;
            panel.ShowNewInfo("Well done", place + " is reached. Hold it and await further orders.");
            OnceLog.Info("wl-campaign-courier:first-acknowledgement", "[W&LCourier] reached campaign order acknowledged type=" + was.type);
        }

        private static void DeliverOne(in CourierEntry e)
        {
            Regiment unit = ResolveUnit(e.UnitName, e.UnitCommanderId);
            GivenOrderView active = WlCampaignDispatchRuntime.CurrentGivenOrder();
            string drop = WlCampaignCourierPolicy.DeliveryDropReason(in e, in active, WlCampaignDispatchRuntime.History,
                WlCampaignDispatchRuntime.NowDays(), onCampaignMap: true, unitResolved: (UnityEngine.Object)unit != null, capitalFixOn: CapitalFixOn());
            if (drop != null)
            {
                Queue.Discard(in e, drop);
                return;
            }

            if (WlCampaignCourierPolicy.RecordsHistory(e.Origin))
            {
                if (WlCampaignDispatchRuntime.ShowQueued(unit, in e))
                {
                    Accepted(in e);
                    return;
                }
                Queue.Discard(in e, "failed");
                return;
            }

            if ((UnityEngine.Object)unit == null)
            {
                Queue.Discard(in e, WlCampaignCourierPolicy.UnitGone);
                return;
            }
            int before = DLC_WL.GivenOrders.givenorderssession;
            WlOrderBypass.Active = true;
            try
            {
                AIBattle.CheckCurrentOrderUpdate(unit, e.Type, new Vector3(e.X, e.Y, e.Z), e.Name ?? string.Empty, e.Rotation, e.Width, e.Depth, calledfromcampaign: true);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-campaign-courier:vanilla-threw", "[W&LCourier] vanilla CheckCurrentOrderUpdate threw for a delivered campaign order (type " + e.Type + "): " + ex.GetType().Name + " " + ex.Message);
                Queue.Discard(in e, "vanilla-threw");
                return;
            }
            finally
            {
                WlOrderBypass.Active = false;
            }
            if (DLC_WL.GivenOrders.givenorderssession == before)
            {
                Queue.Discard(in e, "vanilla-refused");
                return;
            }
            ApplyFormationName();
            Accepted(in e);
        }

        // N4/D19 for a vanilla campaign order: the formation's name in vanilla's own text.
        private static void ApplyFormationName()
        {
            try
            {
                if (!TextOn()) return;
                DLC_WL.GivenOrders order = DLC_WL.givenorder;
                if (order == null) return;
                string content = WlGivenOrderText.WithFormationName(order);
                if (string.Equals(content, order.content, StringComparison.Ordinal)) return;
                order.content = content;
                WlGivenOrderText.Show(order);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-campaign-courier:text", "[W&LCourier] composing the campaign order text failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void Accepted(in CourierEntry e)
        {
            Queue.MarkAccepted(e.IssuedAt);
            Row("deliver", "accepted", in e);
            OnceLog.Info("wl-campaign-courier:first-delivery", "[W&LCourier] first campaign order delivered type=" + e.Type + " origin=" + e.Origin);
        }

        // --------------------------------------------------------------- sidecar

        internal static List<CampaignCourierDto> Export()
        {
            try
            {
                int n = Queue.CopyPending(Scratch);
                if (n == 0) return null;
                var list = new List<CampaignCourierDto>(n);
                WlCampaignCourierSidecar.Select(Scratch, n, CurrentCommandKey(), list);
                return list.Count > 0 ? list : null;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-campaign-courier:export", "[W&LCourier] saving the pending campaign orders failed: " + ex.GetType().Name + " " + ex.Message);
                return null;
            }
        }

        internal static void Restore(List<CampaignCourierDto> saved)
        {
            try
            {
                Queue.Clear("restore");
                Loaded.Clear();
                WlCampaignCourierSidecar.Sanitize(saved, Loaded);
                for (int i = 0; i < Loaded.Count; i++)
                {
                    CourierEntry e = Loaded[i];
                    Queue.Restore(in e);
                }
                if (Loaded.Count > 0)
                    OnceLog.Info("wl-campaign-courier:restored", "[W&LCourier] restored " + Loaded.Count + " pending campaign orders");
                Loaded.Clear();
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-campaign-courier:restore", "[W&LCourier] restoring the pending campaign orders failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // BUG-WL-018: the capital defense speed fix wrote the player's own march mode back.
        internal static void NoteSpeedRestore(Regiment group)
        {
            OnceLog.Info("capital-defense-speed:restored", "[W&LCapitalDefense] restored running/doublequick on the player's group");
            Row("speed-restore", "restored", WlCampaignDispatchPolicy.DefendCapital, 0f, 0f, group, null, OrderOrigin.Vanilla, 0.0, 0.0, 0f);
        }

        // ---------------------------------------------------------------- helpers

        // The player's command as a key that survives a reload and a battle round trip (both rebuild the scene,
        // so instance ids change): the command unit's GameObject name and the chosen commander id. Null when
        // there is none. Cached per command object, the name getter allocates.
        private static string CurrentCommandKey()
        {
            Regiment command = PlayerCommand();
            if ((UnityEngine.Object)command == null) return null;
            int id = command.GetInstanceID();
            int chosen = DLC_WL.dlc_chosencommander;
            if (id != _keyInstanceId || chosen != _keyChosen)
            {
                _keyInstanceId = id;
                _keyChosen = chosen;
                _key = command.name + "#" + chosen;
            }
            return _key;
        }

        private static Regiment PlayerCommand()
        {
            int chosen = DLC_WL.dlc_chosencommander;
            if (chosen < 0 || GameVars.commander == null || chosen >= GameVars.commander.Count) return null;
            return GameVars.commander[chosen].currentcommand;
        }

        // The command of the senior commander above the player: the headquarters that "sends" a vanilla
        // campaign order or Whiskey's strategic order (D7). Null when there is none (the player is CIC,
        // or the senior commander commands nothing).
        private static Regiment SeniorHeadquarters()
        {
            GameVars.Commander senior = DLC_WL.GetSeniorCommander();
            return senior != null ? senior.currentcommand : null;
        }

        // Vanilla's Vector3.Distance between the sender and the receiving group (125014).
        private static float DistanceToPlayerGroup(Regiment hq)
        {
            Regiment command = PlayerCommand();
            if ((UnityEngine.Object)hq == null || (UnityEngine.Object)command == null) return 0f;
            Regiment group = BattleUnits.GetCampaignGroup(command);
            if ((UnityEngine.Object)group == null) group = command;
            return Vector3.Distance(group.transform.position, hq.transform.position);
        }

        private static CampaignCourierPrefs CampaignPrefs()
        {
            return new CampaignCourierPrefs(GamePrefs.processingtimegroupstandard, GamePrefs.buglestandardrecognitiontime, GameVars.useorderdelays);
        }

        // The issuing headquarters' multipliers on the recognition time, exactly as vanilla computes them
        // (125037-125110): telegraph, winter quarters, entrenchment, army group, readiness, perks. The
        // incident and effect factors on outgoing player orders are not applied (finding 8).
        private static CampaignIssuerFactors FactorsOf(Regiment h)
        {
            if ((UnityEngine.Object)h == null) return CampaignIssuerFactors.One;
            try
            {
                float telegraph = (UnityEngine.Object)h.hastelegraphconnection != null ? GamePrefs.orderprocessingfactorwithintelegraphrange : 1f;
                float winter = (UnityEngine.Object)h.campbuilding != null && h.campbuilding.BuildingType == CBuilding.id_winterquarters
                    ? GamePrefs.orderdelayfactorwinterquarters : 1f;
                float entrench = (UnityEngine.Object)h.entrenchment != null
                    ? 1f + (GamePrefs.orderdelayfactorentrenchment - 1f) * h.entrenchmentlevel : 1f;
                float armyGroup = h.GetArmyGroupBonus(0);
                float readiness = 1f + (GamePrefs.readinessorderdelay - 1f) * (1f - h.readiness);
                float perks = 1f;
                if (h.HasPerk(1)) perks *= GamePrefs.fieldtelegraphorderdelays[h.GetPerkLevel(1)];
                if (h.HasPerkParent(1)) perks *= GamePrefs.fieldtelegraphorderdelays[h.GetPerkLevelParent(1)];
                if (h.HasPerk(2)) perks *= GamePrefs.orderdelaysballooncorps[h.GetPerkLevel(2)];
                if (h.HasPerkParent(2)) perks *= GamePrefs.orderdelaysballooncorps[h.GetPerkLevelParent(2)];
                return new CampaignIssuerFactors(telegraph, winter, entrench, armyGroup, readiness, perks);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-campaign-courier:factors", "[W&LCourier] issuer delay factors unreadable; counted as 1: " + ex.GetType().Name + " " + ex.Message);
                return CampaignIssuerFactors.One;
            }
        }

        // A campaign group by GameObject name and commander id (instance ids do not survive a save).
        private static Regiment ResolveUnit(string name, int commanderId)
        {
            if (string.IsNullOrEmpty(name)) return null;
            GameObject go = GameObject.Find(name);
            if ((UnityEngine.Object)go == null) return null;
            Regiment unit = go.GetComponent<Regiment>();
            if ((UnityEngine.Object)unit == null) return null;
            return commanderId < 0 || unit.commander == commanderId ? unit : null;
        }

        // ------------------------------------------------------------------- rows

        private static void Row(string action, string reason, in CourierEntry e)
        {
            Emit(action, reason, e.Type, e.Origin, e.IssuerKey, e.IssuedAt, e.DueAt, e.Meters, e.UnitName, e.Name);
        }

        // A drop before an entry exists (a re-pop, a queue refusal): one row per change of signature,
        // because a continuous vanilla caller repeats the same drop on every pass.
        private static void Row(string action, string reason, int type, float x, float z, Regiment unit, string name,
            OrderOrigin origin, double issuedAt, double dueAt, float meters)
        {
            int session = DLC_WL.GivenOrders.givenorderssession;
            int rx = Mathf.RoundToInt(x);
            int rz = Mathf.RoundToInt(z);
            if (string.Equals(_lastDropReason, reason, StringComparison.Ordinal) && _lastDropType == type
                && _lastDropX == rx && _lastDropZ == rz && _lastDropSession == session) return;
            _lastDropReason = reason;
            _lastDropType = type;
            _lastDropX = rx;
            _lastDropZ = rz;
            _lastDropSession = session;
            string unitName = (UnityEngine.Object)unit != null ? unit.name : null;
            Emit(action, reason, type, origin, 0, issuedAt, dueAt, meters, unitName, name);
        }

        private static void Emit(string action, string reason, int type, OrderOrigin origin, int issuer, double issuedAt, double dueAt,
            float meters, string unitName, string dest)
        {
            try
            {
                double nowYears = GameVars.currenttimefromstart;
                int session = DLC_WL.GivenOrders.givenorderssession;
                string originText = origin.ToString();
                TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "CampaignPlayerOrderFlow", TelemetrySeverity.Info,
                    ev => ev
                        .WithDecision(action, reason, issuer + "|" + type)
                        .WithField("scope", "campaign")
                        .WithField("action", action)
                        .WithField("reason", reason)
                        .WithField("type", type)
                        .WithField("origin", originText)
                        .WithField("issuer", issuer)
                        .WithField("issuedY", issuedAt)
                        .WithField("dueY", dueAt)
                        .WithField("deliveredY", nowYears)
                        .WithField("meters", (double)meters)
                        .WithField("unit", unitName ?? string.Empty)
                        .WithField("dest", dest ?? string.Empty)
                        .WithField("session", session));
            }
            catch
            {
            }
        }

        // Entries the queue discards by itself (superseded, overflow, cleared) and the delivery pass's
        // own drops all become one discard row.
        private sealed class DiscardSink : ICourierDiscardSink
        {
            public void Discarded(in CourierEntry e, string reason)
            {
                Row("discard", reason, in e);
            }
        }
    }
}
