using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The mover's vanilla side (plan G5a): the daily review's movement step for one alliance. The decisions are
    // CampaignMover's (pure, tested); this class reads the public and own-side inputs of the chapter tempo roll, implements
    // the pass's host over vanilla, and logs. Runs only inside ReviewExecute, after the pre-war gate (executors write vanilla
    // state only from the review).
    //
    // Vanilla it rests on (decompile build 14703111): AICampaign.MoveUnitTo 14479-14540 (the move is a queued
    // DefensiveMovingOrder when movearoundenemyunits is set, one order served at a time, 9933-10052; it refuses a target in a
    // state that is neither own, allied nor at war, 14486); BattlefieldSetup.IsTerrainPositionRestricted 25755 (the final
    // waypoint test SetWaypoint applies through CheckIfFinalWaypointIsPassable, 130768); AIFaction.unitsinoffensiveoperations
    // (the list vanilla's job 6 keeps retargeting, 14003-14061); the tempo roll 14316-14350; the W&L order path 14381-14456 and
    // stage F's WlStrategicOrderBridge.TryIssue / dispatcher for the map-group player.
    //
    // A group that carries a W&L player below map level (CarryingGroup) is moved here like any AI formation, with no order to the
    // player it carries (vanilla sends him none, 8366; Whiskey stopped dispatching its marches to him on 2026-10-16). The defensive
    // operation list is vanilla's alone (13782-13848): nothing here names it.
    internal static class CampaignMoverRuntime
    {
        private static readonly VanillaMoverHost Host = new VanillaMoverHost();
        private static readonly MoverInputs Inputs = new MoverInputs();

        internal static void Execute(int alliance, CampaignSnapshot snapshot, PlannerState state)
        {
            using (TelemetryPerf.Scope("campaign.planner.mover", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                if (snapshot == null || state == null) return;
                if (!CampaignPlanStore.TryGetPlan(alliance, out var plan) || !CampaignPlanStore.TryGetAllocation(alliance, out var allocation)) return;
                object faction = AICampaignReflect.GetFaction(snapshot.FactionIndex);
                if (faction == null) return;

                bool wlActive = DLC_WL.dlc_scenarioactive;
                Inputs.Snapshot = snapshot;
                Inputs.Plan = plan;
                Inputs.Allocation = allocation;
                Inputs.WlPlayerAlliance = wlActive && GameVars.playeralliance == alliance;
                Inputs.Chapter = Policy.CurrentChapter;
                Inputs.Tempo = ReadTempoWorld(alliance, wlActive);
                Host.Bind(alliance, faction);

                var tally = CampaignMover.Review(Inputs, Host, MoveTrackers.For(alliance), state);

                Inputs.Snapshot = null;
                Inputs.Plan = null;
                Inputs.Allocation = null;
                Host.Unbind();
                if (tally.TempoFirstRow || tally.TempoReleaseRow) TelemetryRouter.LegacyInfo(tally.TempoRow(alliance), TelemetryLayer.Campaign);
                if (tally.MainUndispatchableRow) TelemetryRouter.LegacyInfo(tally.UndispatchableRow(alliance), TelemetryLayer.Campaign);
                if (tally.Any) TelemetryRouter.LegacyInfo(tally.Row(alliance), TelemetryLayer.Campaign);
            }
        }

        // The tempo roll's inputs, as vanilla reads them at 14303-14347 (and #38's mirror at the same lines): the chapter's
        // aggression, the faction's (alliances 0 and 1 only), the campaign's, the winter factor, the W&L multiplier and the
        // scripted push or the debug flag that releases every roll.
        private static TempoWorld ReadTempoWorld(int alliance, bool wlActive)
        {
            float date = GameVars.GetCampaignFloatingDate();
            var factionPrefs = GamePrefs.aiagressivenessoffensiveoperationfaction;
            return new TempoWorld
            {
                ChapterAggression = CampaignTempoGate.ChapterAggression(GamePrefs.aiagressiveness, Policy.CurrentChapter),
                FactionAggression = factionPrefs != null && alliance >= 0 && alliance < factionPrefs.Length ? factionPrefs[alliance] : 1f,
                CampaignAggression = GameVars.usedcampaignagressiveness,
                WinterModifier = WinterOffensiveModifierPatch.CurrentRollModifier(),
                WlActive = wlActive,
                WlAggressiveness = GamePrefs.dlcwl_careerincreasedaggressiveness,
                Forced = GameVars.debug_permanentaimovement || CampaignTempoGate.ForcedInvasion(GamePrefs.leveltoload, CampaignObjective.GetObjectiveAccomplishment(6), alliance, date),
                CampaignDate = date
            };
        }

        private sealed class VanillaMoverHost : VanillaMoveHost, IMoverHost
        {
            private object _factionObject;
            private IList _offensive;

            internal void Bind(int alliance, object factionObject)
            {
                Alliance = alliance;
                _factionObject = factionObject;
                _offensive = null;
            }

            internal void Unbind()
            {
                _factionObject = null;
                _offensive = null;
            }

            public int StateAllianceAt(float x, float z)
            {
                int state = BattlefieldSetup.GetStateOfField(new Vector3(x, 0f, z), usecloseststate: true);
                if (state < 0 || GameVars.nation == null || state >= GameVars.nation.Length) return -1;
                return GameVars.nation[state].alliance;
            }

            public bool AtWar(int otherAlliance)
            {
                if (GameVars.alliance == null || Alliance < 0 || Alliance >= GameVars.alliance.Length) return false;
                return GameVars.alliance[Alliance].IsAtWarWith(otherAlliance);
            }

            // The map-group player: his Support order is a W&L Redeploy dispatch (order type 5) through the campaign
            // dispatcher with stage F's dedupe, never a move.
            public bool IssuePlayerOrder(int instanceId, float x, float z)
            {
                var unit = UnitOf(instanceId);
                if (unit == null) return false;
                var decision = WlStrategicOrderBridge.TryIssue(new WlStrategicOrderRequest
                {
                    AllianceId = Alliance,
                    Unit = unit,
                    TargetPosition = new Vector3(x, unit.GetPosition().y, z),
                    Intent = WlStrategicIntent.Redeploy
                });
                return decision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder;
            }

            public void ListOffensive(int instanceId, bool listed)
            {
                var unit = UnitOf(instanceId);
                if (unit == null) return;
                if (_offensive == null) _offensive = VanillaFieldCache.List(_factionObject, "unitsinoffensiveoperations");
                if (_offensive == null) return;
                bool present = _offensive.Contains(unit);
                if (listed && !present) _offensive.Add(unit);
                else if (!listed && present) _offensive.Remove(unit);
            }
        }
    }
}
