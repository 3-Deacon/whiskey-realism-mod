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
    // (commander id + campaign level) and deliberately not CampaignScope-reset: a
    // return from battle reloads the campaign, and the ledger measures the human
    // player, not the save. Battle end drops the pending battle order (BattleScope).
    internal static class PlayerComplianceRuntime
    {
        private const double HoursPerYear = 8760.0;

        private static PlayerComplianceLedger _ledger = new PlayerComplianceLedger();
        private static string _careerKey;
        private static int _lastSeenSession = int.MinValue;
        // The order object each scope's pending ledger entry stands for.
        private static readonly DLC_WL.GivenOrders[] _tracked = new DLC_WL.GivenOrders[2];

        private static string _savedKey;
        private static float _savedReliability;
        private static int _savedSamples;

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
                    return _ledger.Reliability;
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
            var order = DLC_WL.givenorder;
            int session = DLC_WL.GivenOrders.givenorderssession;

            int s = (int)scope;
            var tracked = _tracked[s];
            if (tracked != null)
            {
                if (tracked.firstunitreached)
                    Report(scope, _ledger.OnReached(scope, _ledger.PendingSession(scope)));
                else if (!ReferenceEquals(order, tracked))
                    _ledger.DropPending(scope);
            }
            else
            {
                // Pending without a watched order (scene change): no fair verdict.
                _ledger.DropPending(scope);
            }
            if (!_ledger.HasPending(scope)) _tracked[s] = null;

            if (_lastSeenSession == int.MinValue)
            {
                // First look at this career: an order already standing has an unknown age.
                _lastSeenSession = session;
            }
            else if (order != null && session != _lastSeenSession)
            {
                _lastSeenSession = session;
                bool battleOrder = order.IsBattleOrder();
                if (playerDecides && order.showzone && battleOrder == (scope == ComplianceScope.Battle))
                {
                    _ledger.OnDispatch(scope, session, nowHours);
                    _tracked[s] = order;
                }
            }

            Report(scope, _ledger.Tick(scope, nowHours));
            if (!_ledger.HasPending(scope)) _tracked[s] = null;
        }

        private static void EnsureCareer()
        {
            string key = CareerKey();
            if (string.Equals(key, _careerKey, StringComparison.Ordinal)) return;
            _careerKey = key;
            _ledger = new PlayerComplianceLedger();
            _lastSeenSession = int.MinValue;
            _tracked[0] = null;
            _tracked[1] = null;
            if (_savedKey != null && string.Equals(_savedKey, key, StringComparison.Ordinal))
                _ledger.Restore(_savedReliability, _savedSamples);
        }

        private static string CareerKey()
        {
            return DLC_WL.dlc_chosencommander + ":" + (GamePrefs.leveltoload ?? string.Empty) + "/" + (GamePrefs.subleveltoload ?? string.Empty);
        }

        private static void DropBattle()
        {
            _ledger.DropPending(ComplianceScope.Battle);
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
                " reliability=" + _ledger.Reliability.ToString("0.00") + " samples=" + _ledger.Samples,
                scope == ComplianceScope.Battle ? TelemetryLayer.Tactical : TelemetryLayer.Campaign);
        }

        // Sidecar: the current career's estimate, or what a load brought in and no
        // observation has replaced yet.
        internal static PlayerComplianceDto Export()
        {
            if (_careerKey != null && _ledger.Samples > 0)
                return new PlayerComplianceDto { CareerKey = _careerKey, Reliability = _ledger.Reliability, Samples = _ledger.Samples };
            if (_savedKey != null)
                return new PlayerComplianceDto { CareerKey = _savedKey, Reliability = _savedReliability, Samples = _savedSamples };
            return null;
        }

        // Sidecar load: adopted when its career is observed and this session has no
        // samples for it (PlayerComplianceLedger.Restore).
        internal static void Restore(PlayerComplianceDto saved)
        {
            if (saved == null || string.IsNullOrEmpty(saved.CareerKey)) return;
            _savedKey = saved.CareerKey;
            _savedReliability = saved.Reliability;
            _savedSamples = saved.Samples;
            if (string.Equals(_careerKey, saved.CareerKey, StringComparison.Ordinal))
                _ledger.Restore(saved.Reliability, saved.Samples);
        }
    }
}
