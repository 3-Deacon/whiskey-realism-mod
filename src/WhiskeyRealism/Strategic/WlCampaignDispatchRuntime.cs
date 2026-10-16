using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    // The one W&L campaign dispatcher (chain-of-command spec §D, stage F plan F2/F3; amended 2026-10-16).
    //
    // It serves one caller: WlStrategicOrderBridge, for Whiskey's own strategic order to a W&L player
    // whose command is itself a campaign-map group (vanilla's gate, AIBattle.CheckCurrentOrderUpdate
    // 8366, admits exactly that player). A player below map-group level (a brigade or division carried
    // by an AI corps or army) is sent no campaign order by vanilla and none by Whiskey: he cannot move
    // himself, so the group that carries him marches without a word, as in the base game.
    //
    // Emission mirrors AIBattle.CheckCurrentOrderUpdate's tail (decompile 8398-8664, build 14703111):
    // group unit = the player's parent unit (8400, GetParentUnit 93883); zone = max(20, unit width,
    // group width) / depth, at least GamePrefs.dlcwl_minimumobjectivezone (8402-8428); headline/content =
    // the type's tooltip (5 1085, 6 1087, 7 1086, 8 1088, 9 1089, 10 1090, 16 1203; 8592-8636); terrain
    // height (8638); new GivenOrders(..., rotation -1 + 180, ..., resetfirstunitreached, showzone,
    // issuer) (8648); givenorderssession++ (8651); CareerInformationPanel.ShowNewOrder on the UI panel
    // (8653-8664). Vanilla's CheckDLCWLTopics (123468) then pays compliance unchanged.
    //
    // State (the dispatch history) is W&L dispatch bookkeeping, reset per campaign (CampaignScope).
    internal static class WlCampaignDispatchRuntime
    {
        private const int MaxLoggedSignatures = 64;
        private const string Trigger = "whiskey-order";

        private static readonly HashSet<string> _loggedSignatures = new HashSet<string>(StringComparer.Ordinal);
        private static readonly WlDispatchHistory _history = new WlDispatchHistory();
        private static CareerInformationPanel _panel;

        static WlCampaignDispatchRuntime()
        {
            CampaignScope.Register("wl-campaign-dispatch", Reset);
        }

        private static void Reset()
        {
            _loggedSignatures.Clear();
            _history.Clear();
            _panel = null;
        }

        internal static bool Enabled()
        {
            return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                && Plugin.EnableCampaignChainDispatches != null && Plugin.EnableCampaignChainDispatches.Value;
        }

        // Whiskey's own order (WlStrategicOrderBridge) to the group of a map-group player.
        // Returns WlCampaignDispatchPolicy.Issue, a skip reason, or "failed".
        internal static string EmitForPlayerCommand(Regiment unit, int type, Vector3 target, string destinationName, float width, float depth)
        {
            if (!Enabled()) return "dispatch-disabled";
            try
            {
                int issuer = SeniorCommanderId();
                Vector3 from = (UnityEngine.Object)unit != null ? ((Component)unit).transform.position : target;
                return Emit(unit, issuer, type, target, destinationName, XZDistance(from, target), width, depth);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-campaign-dispatch:bridge", "[W&LCampaignDispatch] Whiskey order emission failed: " + ex.Message);
                return "failed";
            }
        }

        private static string Emit(Regiment issuerUnit, int issuerCommanderId, int type, Vector3 target, string destinationName, float moveDistance, float width, float depth)
        {
            double nowDays = NowDays();
            string decision = WlCampaignDispatchPolicy.Decide(type, target.x, target.z, moveDistance, _history, CurrentGivenOrder(), nowDays);
            string signature = Trigger + ":" + type + ":" + Mathf.RoundToInt(target.x) + ":" + Mathf.RoundToInt(target.z);
            if (decision != WlCampaignDispatchPolicy.Issue)
            {
                LogBounded(signature + ":" + decision,
                    "[W&LCampaignDispatch] trigger=" + Trigger + " type=" + type + " unit=" + SafeName(issuerUnit) +
                    " target=" + Fmt(target) + " action=skip reason=" + decision);
                return decision;
            }

            // The order rides a courier (W&L Order Couriers): it is shown, and the dispatch history
            // written, when it arrives (ShowQueued). Without one it is shown at once.
            string queued = WlCampaignCourierRuntime.EnqueueWhiskey(issuerUnit, issuerCommanderId, type, target, destinationName, width, depth);
            if (queued == WlCampaignCourierRuntime.Queued)
            {
                LogBounded(signature + ":queued",
                    "[W&LCampaignDispatch] trigger=" + Trigger + " type=" + type + " unit=" + SafeName(issuerUnit) +
                    " target=" + Fmt(target) + " dest=" + destinationName + " action=queue");
                return decision;
            }
            if (queued != null)
            {
                LogBounded(signature + ":" + queued,
                    "[W&LCampaignDispatch] trigger=" + Trigger + " type=" + type + " unit=" + SafeName(issuerUnit) +
                    " target=" + Fmt(target) + " action=skip reason=" + queued);
                return queued;
            }

            if (!ShowOrder(issuerUnit, issuerCommanderId, type, target, destinationName, width, depth)) return "failed";
            _history.Record(target.x, target.z, nowDays);
            LogIssued(issuerUnit, issuerCommanderId, type, target, destinationName);
            return decision;
        }

        // A courier arrived (WlCampaignCourierRuntime): the same order ShowOrder has always built, now
        // that the delay has passed. The dispatch history is written here, so an entry is never
        // rate-limited against its own target.
        internal static bool ShowQueued(Regiment issuerUnit, in CourierEntry e)
        {
            var target = new Vector3(e.X, e.Y, e.Z);
            if (!ShowOrder(issuerUnit, e.IssuerCommanderId, e.Type, target, e.Name, e.Width, e.Depth)) return false;
            _history.Record(e.X, e.Z, NowDays());
            LogIssued(issuerUnit, e.IssuerCommanderId, e.Type, target, e.Name);
            return true;
        }

        private static void LogIssued(Regiment issuerUnit, int issuerCommanderId, int type, Vector3 target, string destinationName)
        {
            TelemetryRouter.LegacyInfoToMainLogIfAllowed(
                "[W&LCampaignDispatch] trigger=" + Trigger + " type=" + type + " unit=" + SafeName(issuerUnit) +
                " issuer=" + CommanderName(issuerCommanderId) + " target=" + Fmt(target) + " dest=" + destinationName + " action=issue",
                TelemetryLayer.Campaign);
        }

        private static bool ShowOrder(Regiment issuerUnit, int issuerCommanderId, int type, Vector3 position, string destinationName, float width, float depth)
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
                destinationName, resetfirstunitreached: true, _showzone: true, manualcommanderid: issuerCommanderId);
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
