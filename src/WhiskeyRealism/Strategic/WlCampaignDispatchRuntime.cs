using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    // The one W&L campaign dispatcher (chain-of-command spec §D, stage F plan F2/F3).
    //
    // Trigger 1: vanilla moves the campaign-map group that carries a subordinate player
    // (#69 MoveUnitTo Postfix records it, #69 UpdateUnitAI Postfix classifies and emits
    // once the vanilla caller has listed the unit). Trigger 2: WlStrategicOrderBridge,
    // for Whiskey's own strategic order to a top-level player's group.
    //
    // Emission mirrors AIBattle.CheckCurrentOrderUpdate's tail (decompile 8398-8664,
    // build 14703111): group unit = the player's parent unit (8400, GetParentUnit
    // 93883); zone = max(20, unit width, group width) / depth, at least
    // GamePrefs.dlcwl_minimumobjectivezone (8402-8428); headline/content = the type's
    // tooltip (5 1085, 6 1087, 7 1086, 8 1088, 9 1089, 10 1090, 16 1203; 8592-8636);
    // terrain height (8638); new GivenOrders(..., rotation -1 + 180, ...,
    // resetfirstunitreached, showzone, issuer) (8648); givenorderssession++ (8651);
    // CareerInformationPanel.ShowNewOrder on the UI panel (8653-8664). Vanilla's
    // CheckDLCWLTopics (123468) then pays compliance unchanged.
    //
    // State (pending moves, last emission) is W&L dispatch bookkeeping written by the
    // #69 event handlers, reset per campaign (CampaignScope).
    internal static class WlCampaignDispatchRuntime
    {
        private const int MaxPending = 16;
        private const int AifactionScanLimit = 8;
        private const int MaxLoggedSignatures = 64;
        private const float VanillaZone = 20f;
        private const string VanillaMoveTrigger = "vanilla-move";

        private struct PendingMove
        {
            public Regiment Unit;
            public Vector3 Destination;
            public float Distance;
            public bool WasInOffensive;
            public bool Queued;
            public WlMoveSource Source;
        }

        private static readonly List<PendingMove> _pending = new List<PendingMove>(MaxPending);
        private static readonly int[] _pendingUnitIds = new int[MaxPending];
        private static WlMoveSource _moveSource;
        private static readonly HashSet<string> _loggedSignatures = new HashSet<string>(StringComparer.Ordinal);
        private static readonly WlDispatchHistory _history = new WlDispatchHistory();
        private static readonly WlCorrectionMemory<Regiment> _corrections = new WlCorrectionMemory<Regiment>();
        private static readonly Func<Regiment, bool> QueuedOrderRunning = QueuedOrderRunningOf;
        private static CareerInformationPanel _panel;

        static WlCampaignDispatchRuntime()
        {
            CampaignScope.Register("wl-campaign-dispatch", Reset);
        }

        private static void Reset()
        {
            _pending.Clear();
            _moveSource = WlMoveSource.Other;
            _loggedSignatures.Clear();
            _history.Clear();
            _corrections.Clear();
            _panel = null;
        }

        internal static bool Enabled()
        {
            return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                && Plugin.EnableCampaignChainDispatches != null && Plugin.EnableCampaignChainDispatches.Value;
        }

        // #69 context patches: the vanilla caller (RollUpEnemyObjectivesInZone,
        // CheckFinalPositions) whose moves list membership cannot classify.
        internal static void EnterMoveSource(WlMoveSource source)
        {
            _moveSource = source;
        }

        internal static void ExitMoveSource()
        {
            _moveSource = WlMoveSource.Other;
        }

        // #69 MoveUnitTo Postfix. Cheap exits first: it runs for every AI move.
        // destination is the caller's target as the Prefix read it (vanilla reassigns
        // the argument to the stack position, 14498); queued is movearoundenemyunits,
        // MoveUnitTo's DefensiveMovingOrder path (14517-14523).
        internal static void OnMoveUnitTo(Regiment unit, Vector3 destination, bool queued, bool moved)
        {
            if (!moved || (UnityEngine.Object)unit == null || !DLC_WL.dlc_scenarioactive || !Enabled()) return;
            try
            {
                Regiment command = PlayerCommand();
                if ((UnityEngine.Object)command == null) return;
                Regiment campaignGroup = BattleUnits.GetCampaignGroup(command);
                if ((UnityEngine.Object)campaignGroup != (UnityEngine.Object)unit) return;

                var facts = new WlMoveFacts
                {
                    MoveSucceeded = true,
                    WlActive = true,
                    HasCommand = true,
                    PlayerIsCic = DLC_WL.IsCommanderInChief(),
                    PlayerIsOwnCampaignGroup = (UnityEngine.Object)campaignGroup == (UnityEngine.Object)command,
                    MovedIsPlayerCampaignGroup = true,
                    MovedIsPlayerOwned = unit.dlcw_isundercommander,
                    MovedByPlayer = DLC_WL.IsMovedByPlayer(unit),
                    MovedIsPlayerAlliance = unit.alliance == GameVars.playeralliance,
                };
                if (!WlCampaignDispatchPolicy.IsTrigger(in facts, out string reason))
                {
                    LogBounded("skip:" + reason, "[W&LCampaignDispatch] trigger=vanilla-move action=skip reason=" + reason + " unit=" + SafeName(unit));
                    return;
                }
                if (_pending.Count >= MaxPending)
                {
                    OnceLog.Warning("wl-campaign-dispatch:pending-full", "[W&LCampaignDispatch] pending moves full; dropping a move");
                    return;
                }

                object faction = FactionOf(unit.alliance, out _);
                Vector3 from = ((Component)unit).transform.position;
                _pending.Add(new PendingMove
                {
                    Unit = unit,
                    Destination = destination,
                    Distance = XZDistance(from, destination),
                    WasInOffensive = ListContains(faction, "unitsinoffensiveoperations", unit),
                    Queued = queued,
                    Source = _moveSource,
                });
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-campaign-dispatch:record", "[W&LCampaignDispatch] recording a move failed: " + ex.Message);
            }
        }

        // #69 UpdateUnitAI Postfix: the vanilla caller has listed the unit by now.
        internal static void Flush()
        {
            if (_pending.Count == 0)
            {
                ExpireCorrections();
                return;
            }
            try
            {
                int count = _pending.Count;
                for (int i = 0; i < count; i++)
                    _pendingUnitIds[i] = (UnityEngine.Object)_pending[i].Unit != null ? _pending[i].Unit.GetInstanceID() : 0;
                for (int i = 0; i < count; i++)
                {
                    if (!WlCampaignDispatchPolicy.IsLastMoveOfUnit(_pendingUnitIds, count, i)) continue;
                    EmitPending(_pending[i]);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-campaign-dispatch:flush", "[W&LCampaignDispatch] flush failed: " + ex.Message);
            }
            finally
            {
                _pending.Clear();
                ExpireCorrections();
            }
        }

        // After this pass's executions were matched: drop remembered corrections
        // whose queued order is gone or that aged out (WlCorrectionMemory).
        private static void ExpireCorrections()
        {
            if (_corrections.Count == 0) return;
            try
            {
                _corrections.Expire(NowDays(), QueuedOrderRunning);
            }
            catch (Exception ex)
            {
                _corrections.Clear();
                OnceLog.Warning("wl-campaign-dispatch:expire", "[W&LCampaignDispatch] correction expiry failed: " + ex.Message);
            }
        }

        private static bool QueuedOrderRunningOf(Regiment unit)
        {
            return (UnityEngine.Object)unit != null && AICampaign.DefensiveMovingOrder.OrderRunning(unit);
        }

        // Trigger 2 (WlStrategicOrderBridge): Whiskey's own order to the player's group.
        // Returns WlCampaignDispatchPolicy.Issue, a skip reason, or "failed".
        internal static string EmitForPlayerCommand(Regiment unit, int type, Vector3 target, string destinationName, float width, float depth)
        {
            if (!Enabled()) return "dispatch-disabled";
            try
            {
                int issuer = SeniorCommanderId();
                Vector3 from = (UnityEngine.Object)unit != null ? ((Component)unit).transform.position : target;
                return Emit("whiskey-order", unit, issuer, type, target, destinationName, XZDistance(from, target), width, depth);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-campaign-dispatch:bridge", "[W&LCampaignDispatch] Whiskey order emission failed: " + ex.Message);
                return "failed";
            }
        }

        private static void EmitPending(PendingMove move)
        {
            if ((UnityEngine.Object)move.Unit == null) return;
            object faction = FactionOf(move.Unit.alliance, out int factionIndex);
            var context = new WlMoveContext
            {
                Source = move.Source,
                Queued = move.Queued,
                QueuedStillRunning = move.Queued && AICampaign.DefensiveMovingOrder.OrderRunning(move.Unit),
                ExecutesSkippedCorrection = !move.Queued && move.Source == WlMoveSource.Other
                    && _corrections.ConsumeExecution(move.Unit, move.Destination.x, move.Destination.z, NowDays()),
                WasInOffensive = move.WasInOffensive,
                InOffensive = ListContains(faction, "unitsinoffensiveoperations", move.Unit),
                InDefensive = ListContains(faction, "unitsindefensiveoperations", move.Unit),
                InCapitalDefense = ListContains(faction, "groupstodefendcapital", move.Unit),
                EnemyAtTarget = EnemyAtTarget(move.Destination, factionIndex),
            };
            string skip = WlCampaignDispatchPolicy.PendingSkipReason(in context);
            if (skip != null)
            {
                _corrections.NoteSkip(move.Unit, move.Destination.x, move.Destination.z, NowDays(), in context, skip);
                LogBounded(VanillaMoveTrigger + ":" + skip,
                    "[W&LCampaignDispatch] trigger=" + VanillaMoveTrigger + " unit=" + SafeName(move.Unit) +
                    " target=" + Fmt(move.Destination) + " action=skip reason=" + skip);
                return;
            }
            int type = WlCampaignDispatchPolicy.ClassifyType(in context);

            Vector3 target = move.Destination;
            string name = "Objective";
            if (type == WlCampaignDispatchPolicy.DefendCapital)
                CapitalTarget(move.Unit.alliance, ref target, ref name);
            else if (type == WlCampaignDispatchPolicy.Engage)
                name = NearestEnemyName(faction, move.Destination) ?? name;

            Emit(VanillaMoveTrigger, move.Unit, move.Unit.commander, type, target, name, move.Distance, VanillaZone, VanillaZone);
        }

        private static string Emit(string trigger, Regiment issuerUnit, int issuerCommanderId, int type, Vector3 target, string destinationName, float moveDistance, float width, float depth)
        {
            double nowDays = NowDays();
            string decision = WlCampaignDispatchPolicy.Decide(type, target.x, target.z, moveDistance, _history, CurrentGivenOrder(), nowDays);
            string signature = trigger + ":" + type + ":" + Mathf.RoundToInt(target.x) + ":" + Mathf.RoundToInt(target.z);
            if (decision != WlCampaignDispatchPolicy.Issue)
            {
                LogBounded(signature + ":" + decision,
                    "[W&LCampaignDispatch] trigger=" + trigger + " type=" + type + " unit=" + SafeName(issuerUnit) +
                    " target=" + Fmt(target) + " action=skip reason=" + decision);
                return decision;
            }

            bool showZone = WlCampaignDispatchPolicy.ShowsZone(subordinateDispatch: trigger == VanillaMoveTrigger);
            // The order rides a courier (W&L Order Couriers): it is shown, and the dispatch history
            // written, when it arrives (ShowQueued). Without one it is shown at once.
            OrderOrigin origin = trigger == VanillaMoveTrigger ? OrderOrigin.C69 : OrderOrigin.CampaignBridge;
            string queued = WlCampaignCourierRuntime.EnqueueWhiskey(origin, issuerUnit, issuerCommanderId, type, target, destinationName, width, depth, showZone);
            if (queued == WlCampaignCourierRuntime.Queued)
            {
                LogBounded(signature + ":queued",
                    "[W&LCampaignDispatch] trigger=" + trigger + " type=" + type + " unit=" + SafeName(issuerUnit) +
                    " target=" + Fmt(target) + " dest=" + destinationName + " action=queue");
                return decision;
            }
            if (queued != null)
            {
                LogBounded(signature + ":" + queued,
                    "[W&LCampaignDispatch] trigger=" + trigger + " type=" + type + " unit=" + SafeName(issuerUnit) +
                    " target=" + Fmt(target) + " action=skip reason=" + queued);
                return queued;
            }

            if (!ShowOrder(issuerUnit, issuerCommanderId, type, target, destinationName, width, depth, showZone)) return "failed";
            _history.Record(target.x, target.z, nowDays);
            LogIssued(trigger, issuerUnit, issuerCommanderId, type, target, destinationName, showZone);
            return decision;
        }

        // A courier arrived (WlCampaignCourierRuntime): the same order ShowOrder has always built, now
        // that the delay has passed. The dispatch history is written here, so an entry is never
        // rate-limited against its own target.
        internal static bool ShowQueued(Regiment issuerUnit, in CourierEntry e)
        {
            var target = new Vector3(e.X, e.Y, e.Z);
            if (!ShowOrder(issuerUnit, e.IssuerCommanderId, e.Type, target, e.Name, e.Width, e.Depth, e.ShowZone)) return false;
            _history.Record(e.X, e.Z, NowDays());
            LogIssued(e.Origin == OrderOrigin.C69 ? VanillaMoveTrigger : "whiskey-order", issuerUnit, e.IssuerCommanderId, e.Type, target, e.Name, e.ShowZone);
            return true;
        }

        private static void LogIssued(string trigger, Regiment issuerUnit, int issuerCommanderId, int type, Vector3 target, string destinationName, bool showZone)
        {
            TelemetryRouter.LegacyInfoToMainLogIfAllowed(
                "[W&LCampaignDispatch] trigger=" + trigger + " type=" + type + " unit=" + SafeName(issuerUnit) +
                " issuer=" + CommanderName(issuerCommanderId) + " target=" + Fmt(target) + " dest=" + destinationName + " zone=" + (showZone ? "shown" : "none") + " action=issue",
                TelemetryLayer.Campaign);
        }

        private static bool ShowOrder(Regiment issuerUnit, int issuerCommanderId, int type, Vector3 position, string destinationName, float width, float depth, bool showZone)
        {
            int tooltip = TooltipFor(type);
            if (tooltip < 0) return false;
            Regiment command = PlayerCommand();
            if ((UnityEngine.Object)command == null) return false;
            Regiment group = BattleUnits.GetParentUnit(command);
            if ((UnityEngine.Object)group == null) return false;

            if (group.groupposition != null)
            {
                width = Mathf.Max(width, group.width, group.groupposition.groupwidth);
                depth = Mathf.Max(depth, group.depth, group.groupposition.groupdepth);
            }
            else
            {
                width = Mathf.Max(width, group.width);
                depth = Mathf.Max(depth, group.depth);
            }
            width = Mathf.Max(width, GamePrefs.dlcwl_minimumobjectivezone);
            depth = Mathf.Max(depth, GamePrefs.dlcwl_minimumobjectivezone);

            Regiment textUnit = (UnityEngine.Object)issuerUnit != null ? issuerUnit : group;
            string headline = Tooltip.GetTooltipText(tooltip, 0);
            string content = Tooltip.GetTooltipText(tooltip, 1, new Tooltip.VariableContainer(0f, destinationName, textUnit, 0));
            position = new Vector3(position.x, VanillaTerrain.SampleHeight(position, position.y), position.z);

            var panel = Panel();
            if ((UnityEngine.Object)panel == null)
            {
                OnceLog.Warning("wl-campaign-dispatch:panel", "[W&LCampaignDispatch] CareerInformationPanel not found; order not shown");
                return false;
            }

            DLC_WL.givenorder = new DLC_WL.GivenOrders(group, type, headline, content, position, -1f + 180f, width, depth,
                destinationName, resetfirstunitreached: true, _showzone: showZone, manualcommanderid: issuerCommanderId);
            DLC_WL.GivenOrders.givenorderssession++;
            panel.ShowNewOrder(DLC_WL.givenorder);
            return true;
        }

        private static int TooltipFor(int type)
        {
            switch (type)
            {
                case 5: return 1085;
                case 6: return 1087;
                case 7: return 1086;
                case 8: return 1088;
                case 9: return 1089;
                case 10: return 1090;
                case 16: return 1203;
                default: return -1;
            }
        }

        private static CareerInformationPanel Panel()
        {
            if ((UnityEngine.Object)_panel != null) return _panel;
            var ui = GameObject.Find("UI");
            if ((UnityEngine.Object)ui == null) return null;
            var panels = ui.GetComponentsInChildren<CareerInformationPanel>(true);
            _panel = panels != null && panels.Length > 0 ? panels[0] : null;
            return _panel;
        }

        internal static WlDispatchHistory History => _history;

        internal static GivenOrderView CurrentGivenOrder()
        {
            var order = DLC_WL.givenorder;
            int session = DLC_WL.GivenOrders.givenorderssession;
            if (order == null) return new GivenOrderView(false, -1, 0f, 0f, session);
            return new GivenOrderView(true, order.type, order.position.x, order.position.z, session);
        }

        private static Regiment PlayerCommand()
        {
            int chosen = DLC_WL.dlc_chosencommander;
            if (chosen < 0 || GameVars.commander == null || chosen >= GameVars.commander.Count) return null;
            return GameVars.commander[chosen].currentcommand;
        }

        // DLC_WL.GetSeniorCommander (47718) returns the Commander; GivenOrders wants its index.
        private static int SeniorCommanderId()
        {
            var senior = DLC_WL.GetSeniorCommander();
            if (senior == null || GameVars.commander == null) return -1;
            return GameVars.commander.IndexOf(senior);
        }

        private static object FactionOf(int allianceId, out int index)
        {
            for (int i = 0; i < AifactionScanLimit; i++)
            {
                object faction = AICampaignReflect.GetFaction(i);
                if (faction == null) break;
                if (AICampaignReflect.GetAllianceId(i) == allianceId)
                {
                    index = i;
                    return faction;
                }
            }
            index = -1;
            return null;
        }

        private static bool ListContains(object faction, string field, Regiment unit)
        {
            IList list = faction != null ? VanillaFieldCache.List(faction, field) : null;
            return list != null && list.Contains(unit);
        }

        // Vanilla's offensive enemy figure num2 for the target area (14228-14229).
        private static bool EnemyAtTarget(Vector3 destination, int factionIndex)
        {
            if (factionIndex < 0 || AICampaign.aiareas == null) return false;
            var area = AICampaign.AIArea.GetAIArea(AICampaign.aiareas.GetColorOnPos(destination));
            if (area == null) return false;
            float[] strength = area.enemycampaignunitsstrengthclose;
            float[] morale = area.avgtroopmoraleinzone;
            float[] weapon = area.enemycampaignunitsstrengthcloseweapon;
            if (strength == null || morale == null || weapon == null
                || factionIndex >= strength.Length || factionIndex >= morale.Length || factionIndex >= weapon.Length) return false;
            return WlCampaignDispatchPolicy.VanillaEnemyAtTarget(strength[factionIndex], morale[factionIndex],
                GamePrefs.aiimportanceenemyunits, weapon[factionIndex], AICampaign.GetNationalMoraleFactor(factionIndex));
        }

        // Vanilla's capital-defense order target and name (11710-11711).
        private static void CapitalTarget(int allianceId, ref Vector3 target, ref string name)
        {
            if (GameVars.alliance == null || allianceId < 0 || allianceId >= GameVars.alliance.Length) return;
            var capital = GameVars.alliance[allianceId].capital;
            if ((UnityEngine.Object)capital == null) return;
            target = ((Component)capital).transform.position;
            if (!string.IsNullOrEmpty(capital.CityName)) name = capital.CityName;
        }

        // Vanilla names the engaged enemy unit (13715, 13775).
        private static string NearestEnemyName(object faction, Vector3 destination)
        {
            IList enemies = faction != null ? VanillaFieldCache.List(faction, "enemyunits") : null;
            if (enemies == null) return null;
            string best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i] as Regiment;
                if ((UnityEngine.Object)enemy == null) continue;
                float d = XZDistance(((Component)enemy).transform.position, destination);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = enemy.name;
                }
            }
            return best;
        }

        internal static double NowDays()
        {
            return GameVars.GetCampaignFloatingDate() * WlCampaignDispatchPolicy.HoursPerYear / 24.0;
        }

        private static float XZDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static void LogBounded(string signature, string line)
        {
            if (_loggedSignatures.Count >= MaxLoggedSignatures) _loggedSignatures.Clear();
            if (!_loggedSignatures.Add(signature)) return;
            TelemetryRouter.LegacyInfo(line, TelemetryLayer.Campaign);
        }

        private static string CommanderName(int id)
        {
            try
            {
                if (GameVars.commander == null || id < 0 || id >= GameVars.commander.Count) return "-";
                return GameVars.commander[id]?.combinedname ?? "-";
            }
            catch { return "-"; }
        }

        private static string SafeName(Regiment unit)
        {
            try { return (UnityEngine.Object)unit != null ? unit.name : "-"; }
            catch { return "-"; }
        }

        private static string Fmt(Vector3 v) => Mathf.RoundToInt(v.x) + "," + Mathf.RoundToInt(v.z);
    }
}
