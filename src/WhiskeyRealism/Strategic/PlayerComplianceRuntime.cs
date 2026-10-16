using System;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    // Feeds PlayerComplianceLedger from vanilla's given order (chain-of-command spec
    // §E, stage F plan F4) and publishes Reliability: the battle evidence
    // (ArmyEvidenceBuilder) weighs the player's regiments by it, and the campaign
    // planner (stage G) reads it.
    //
    // Vanilla anchors (decompile, build 14703111): DLC_WL.givenorder (one slot) and
    // GivenOrders.givenorderssession (bumped per new order, 8651); GivenOrders.showzone
    // (no zone, no compliance: AvatarButtonPanel.Update deletes the mark, 160798-160807);
    // GivenOrders.firstunitreached set by Regiment.CheckDLCWLTopics (123517) before it
    // clears campaign types 5/6/7/16 (123533); GivenOrders.IsBattleOrder (41772).
    //
    // Writers: the #69 UpdateUnitAI Postfix on the campaign map (ObserveCampaign) and
    // the battle coordinator tick (ObserveBattle). The ledger is keyed by career
    // (ComplianceCareerKey: commander id + SceneManagement.currentcampaignscenario,
    // which a campaign battle leaves alone) and deliberately not CampaignScope-reset:
    // a return from battle reloads the campaign, and the ledger measures the human
    // player, not the save. Battle end drops the pending battle order (BattleScope).
    // Vanilla's re-issue of the same order is one sample (ComplianceOrderStream).
    internal static class PlayerComplianceRuntime
    {
        private const double HoursPerYear = WlCampaignDispatchPolicy.HoursPerYear;

        private static readonly PlayerComplianceCareers _careers = new PlayerComplianceCareers();
        private static readonly ComplianceOrderStream _orders = new ComplianceOrderStream();
        // The order object each scope's pending ledger entry stands for.
        private static readonly DLC_WL.GivenOrders[] _tracked = new DLC_WL.GivenOrders[2];

        static PlayerComplianceRuntime()
        {
            BattleScope.Register("player-compliance-battle", DropBattle);
        }

        internal static bool Enabled()
        {
            return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                && Plugin.EnablePlayerComplianceLedger != null && Plugin.EnablePlayerComplianceLedger.Value;
        }

        // 1 (count the player fully) unless a W&L career with the ledger on.
        internal static float Reliability
        {
            get
            {
                try
                {
                    if (!Enabled() || !DLC_WL.dlc_scenarioactive) return 1f;
                    return _careers.Ledger.Reliability;
                }
                catch { return 1f; }
            }
        }

        internal static void ObserveCampaign()
        {
            if (!DLC_WL.dlc_scenarioactive || !Enabled()) return;
            try
            {
                var command = CurrentCommand();
                if ((UnityEngine.Object)command == null) return;
                bool ownMapGroup = (UnityEngine.Object)BattleUnits.GetCampaignGroup(command) == (UnityEngine.Object)command;
                Observe(ComplianceScope.Campaign, GameVars.GetCampaignFloatingDate() * HoursPerYear, playerDecides: ownMapGroup);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("player-compliance:campaign", "[PlayerCompliance] campaign observation failed: " + ex.Message);
            }
        }

        internal static void ObserveBattle(double nowBattleHours)
        {
            if (!DLC_WL.dlc_scenarioactive || !Enabled()) return;
            try
            {
                if ((UnityEngine.Object)CurrentCommand() == null) return;
                Observe(ComplianceScope.Battle, nowBattleHours, playerDecides: true);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("player-compliance:battle", "[PlayerCompliance] battle observation failed: " + ex.Message);
            }
        }

        private static void Observe(ComplianceScope scope, double nowHours, bool playerDecides)
        {
            EnsureCareer();
            var ledger = _careers.Ledger;
            var order = DLC_WL.givenorder;
            int session = DLC_WL.GivenOrders.givenorderssession;
            ComplianceOrderKey orderKey = KeyOf(order);
            ComplianceOrderEvent seen = _orders.See(order != null, session, in orderKey);

            int s = (int)scope;
            var tracked = _tracked[s];
            if (tracked != null)
            {
                if (tracked.firstunitreached)
                    Report(scope, ledger.OnReached(scope, ledger.PendingSession(scope)));
                else if (!ReferenceEquals(order, tracked))
                {
                    // Vanilla's re-issue of the pending order: follow the new object
                    // and keep the issue time; anything else ends the order unreached.
                    ComplianceOrderKey trackedKey = KeyOf(tracked);
                    if (order != null && ComplianceOrderKey.Same(in orderKey, in trackedKey))
                        _tracked[s] = order;
                    else
                        ledger.DropPending(scope);
                }
            }
            else
            {
                // Pending without a watched order (scene change): no fair verdict.
                ledger.DropPending(scope);
            }
            if (!ledger.HasPending(scope)) _tracked[s] = null;

            if (seen == ComplianceOrderEvent.New)
            {
                bool battleOrder = order.IsBattleOrder();
                if (playerDecides && order.showzone && battleOrder == (scope == ComplianceScope.Battle))
                {
                    // D17 (a): an order that reaches the player already inside its zone is no compliance.
                    if (scope == ComplianceScope.Battle && !StartsBattleSample(order))
                        SkipInsideAtIssue(order, session);
                    else
                    {
                        ledger.OnDispatch(scope, session, nowHours);
                        _tracked[s] = order;
                    }
                }
            }

            Report(scope, ledger.Tick(scope, nowHours));
            if (!ledger.HasPending(scope)) _tracked[s] = null;
        }

        // The player's command against the order's zone when the order reaches him.
        private static bool StartsBattleSample(DLC_WL.GivenOrders order)
        {
            var command = CurrentCommand();
            if ((UnityEngine.Object)command == null) return true;
            UnityEngine.Vector3 at = command.GetPosition();
            return ComplianceSampleGate.StartsSample(at.x, at.z, order.position.x, order.position.z, order.arearotation, order.areawidth, order.areadepth);
        }

        private static void SkipInsideAtIssue(DLC_WL.GivenOrders order, int session)
        {
            TelemetryRouter.LegacyInfoToMainLogIfAllowed(
                "[PlayerCompliance] scope=battle skip reason=inside-at-issue type=" + order.type + " session=" + session,
                TelemetryLayer.Tactical);
        }

        private static ComplianceOrderKey KeyOf(DLC_WL.GivenOrders order)
        {
            if (order == null) return default(ComplianceOrderKey);
            int unit = (UnityEngine.Object)order.groupunit != null ? order.groupunit.GetInstanceID() : 0;
            return new ComplianceOrderKey(unit, order.type, order.position.x, order.position.z);
        }

        private static void EnsureCareer()
        {
            if (!_careers.Ensure(DLC_WL.dlc_chosencommander, SceneManagement.currentcampaignscenario)) return;
            _orders.Reset();
            _tracked[0] = null;
            _tracked[1] = null;
        }

        private static void DropBattle()
        {
            _careers.Ledger.DropPending(ComplianceScope.Battle);
            _tracked[(int)ComplianceScope.Battle] = null;
        }

        private static Regiment CurrentCommand()
        {
            int chosen = DLC_WL.dlc_chosencommander;
            if (chosen < 0 || GameVars.commander == null || chosen >= GameVars.commander.Count) return null;
            return GameVars.commander[chosen].currentcommand;
        }

        private static void Report(ComplianceScope scope, ComplianceOutcome outcome)
        {
            if (outcome != ComplianceOutcome.Complied && outcome != ComplianceOutcome.Missed) return;
            TelemetryRouter.LegacyInfoToMainLogIfAllowed(
                "[PlayerCompliance] scope=" + (scope == ComplianceScope.Battle ? "battle" : "campaign") +
                " outcome=" + (outcome == ComplianceOutcome.Complied ? "complied" : "missed") +
                " reliability=" + _careers.Ledger.Reliability.ToString("0.00") + " samples=" + _careers.Ledger.Samples,
                scope == ComplianceScope.Battle ? TelemetryLayer.Tactical : TelemetryLayer.Campaign);
        }

        // Sidecar: the current career's estimate only (a saved estimate for it was
        // adopted by EnsureCareer); null outside a W&L career or without samples.
        internal static PlayerComplianceDto Export()
        {
            try
            {
                if (!DLC_WL.dlc_scenarioactive) return null;
                EnsureCareer();
                if (!_careers.TryExport(out string key, out float reliability, out int samples)) return null;
                return new PlayerComplianceDto { CareerKey = key, Reliability = reliability, Samples = samples };
            }
            catch (Exception ex)
            {
                OnceLog.Warning("player-compliance:export", "[PlayerCompliance] export failed: " + ex.Message);
                return null;
            }
        }

        // Sidecar load: adopted when its career is observed and this session has no
        // samples for it (PlayerComplianceLedger.Restore).
        internal static void Restore(PlayerComplianceDto saved)
        {
            if (saved == null) return;
            _careers.Restore(saved.CareerKey, saved.Reliability, saved.Samples);
        }
    }
}
