using System;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle.Producers
{
    // Infantry charge producer, runtime half (BC6b, spec §4b, plan D10). Decisions are in
    // ChargeIntentProducer.Plan.cs (pure, reads the BattleSnapshot only) and
    // ChargeDoctrine.cs; this half owns the per-side memory, reads vanilla's charge
    // constants once per battle and supplies the config.
    //
    // Vanilla (Assembly-CSharp build 14703111): AI infantry never charges on its own
    // (MicroAICheckForCharges 4905 is dead behind the 4910 GameObject/Regiment compare;
    // micro cycle 6, 5617-5638, writes behavior 1/2 only). The doctrine writes behavior 3
    // per regiment into Regiment.UpdateChargeTarget's arming gate (combatbehavior == 3 &&
    // movementmode == 0 && regimentpaths > 0, 122214) and leaves vanilla's odds check
    // (microaitriggerforcharge, 122259) the final word. Constants, read once per battle:
    //   GamePrefs.maxchargeradius (53232; unitprefs "Maximum search radius for charge
    //     targets (calculated from next group commander)", shipped 50 m);
    //   GamePrefs.timetorenewaichargecheck (54820; battleprefs "Time (game hrs) to renew
    //     check of charge ... a renewed charge order is not given if it was broken up
    //     before due to bad conditions", shipped 0.5 h). Vanilla's own use (4917) is dead
    //     code, and compares lastaichargetime < now + renew, which is always true; the
    //     doctrine waits max(RearmAfterRefusalBattleSeconds, renew) after a refusal;
    //   GamePrefs.nomicroaichargeifunitisoutflanked (54784; battleprefs "No micro ai
    //     charge if unit is outflanked or receiving flanking fire", shipped true, never
    //     read by vanilla): honoured, an outflanked regiment is not armed.
    //   BC8: microaitriggerforcharge and weightingfactorsformicroaicharge (54776-54781) through
    //     TroopQualityRuntime (per battle; Enable Experience And Drill AI Weighting): a
    //     regiment is armed only when vanilla's own odds would pass.
    //
    // Built by BattleCoreTick's producer registry; runs last in the arbiter tick
    // (BattleCoreArbiterPatch, AdjustGroupFormations 5870 Postfix).
    // Rollback: Enable Infantry Charge Doctrine = false (releases what the doctrine armed,
    // then idles) or Tactical Commander Mode MonitorOnly/Off (the executor writes nothing).
    internal sealed partial class ChargeIntentProducer : IBattleIntentProducer
    {
        private static readonly ChargeMemory[] _memory = { new ChargeMemory(), new ChargeMemory() };

        private static bool _prefsRead;
        private static float _maxChargeRadius;
        private static float _renewHours;
        private static bool _noChargeIfOutflanked;

        static ChargeIntentProducer()
        {
            BattleScope.Register("charge-producer", Reset);
        }

        public string Name => "Charge";

        public void Produce(BattleSnapshot snapshot, IIntentSink sink)
        {
            using (TelemetryPerf.Scope("tactical.producer.charge", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (snapshot == null || sink == null) return;
                int alliance = snapshot.Alliance;
                if (alliance < 0 || alliance >= _memory.Length || !PluginEnabled()) return;
                ReadPrefsOnce();

                var settings = new Settings(ConfigOn(), _maxChargeRadius, _renewHours, _noChargeIfOutflanked,
                    TroopQualityRuntime.Prefs.ChargeOddsRulesIfEnabled);
                int confirmed = Plan(snapshot, settings, _memory[alliance], OrderExecutor.ForAlliance(alliance), sink);
                if (confirmed > 0)
                    OnceLog.Info("tactical-battle-core:charge-first-fire",
                        "[ChargeDoctrine] first infantry charge armed: combat behavior 3 issued to a leading regiment of an assault group");
            }
        }

        private static void ReadPrefsOnce()
        {
            if (_prefsRead) return;
            _prefsRead = true;
            try
            {
                _maxChargeRadius = GamePrefs.maxchargeradius;
                _renewHours = GamePrefs.timetorenewaichargecheck;
                _noChargeIfOutflanked = GamePrefs.nomicroaichargeifunitisoutflanked;
            }
            catch (Exception ex)
            {
                // Radius 0 never charges (strict <); the 120 s floor still holds.
                _maxChargeRadius = 0f;
                _renewHours = 0f;
                _noChargeIfOutflanked = true;
                OnceLog.Warning("tactical-battle-core:charge-prefs",
                    "[ChargeDoctrine] GamePrefs charge constants unreadable; the doctrine arms nothing: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static bool PluginEnabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled != null && Plugin.Instance.Enabled.Value;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:charge-enabled",
                    "[ChargeDoctrine] enable check failed; producer idle: " + ex.Message);
                return false;
            }
        }

        // Off still runs the plan, so a regiment the doctrine armed is handed back.
        private static bool ConfigOn()
        {
            try
            {
                return Plugin.EnableInfantryChargeDoctrine != null && Plugin.EnableInfantryChargeDoctrine.Value;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:charge-config",
                    "[ChargeDoctrine] config read failed; treated as off: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static void Reset()
        {
            _prefsRead = false;
            for (int a = 0; a < _memory.Length; a++) _memory[a].Clear();
        }
    }
}
