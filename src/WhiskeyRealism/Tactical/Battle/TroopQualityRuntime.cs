using System;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Runtime half of the BC8 troop-quality rules (pure half TroopQuality.cs; plan
    // docs/superpowers/plans/2026-10-15-battle-core.md § "BC8 — experience and drill").
    // Reads vanilla's prefs once per battle (BattleScope) and the rollback flag
    // Enable Experience And Drill AI Weighting; off gives the neutral prefs.
    //
    // Vanilla prefs (battleprefs, parsed by GamePrefs, build 14703111):
    //   expinfluenceonaidecisions (54714, shipped 0.25): the experience blend of
    //     GetGroupStrength (6057), 1 - ei + groupexperience × ei;
    //   influenceofcommanderinitiativeonattackdefendtrigger (54680, shipped 0.7): the
    //     macro triggers are divided by 1 + initiative × this (6396-6430).
    internal static class TroopQualityRuntime
    {
        private static bool _read;
        private static TroopQualityPrefs _prefs;

        static TroopQualityRuntime()
        {
            BattleScope.Register("troop-quality-prefs", Reset);
        }

        /// <summary>This battle's prefs, read on first use. Never throws.</summary>
        internal static TroopQualityPrefs Prefs
        {
            get
            {
                if (!_read) Read();
                return _prefs;
            }
        }

        private static void Read()
        {
            _read = true;
            var prefs = new TroopQualityPrefs { Enabled = FlagOn() };
            try
            {
                prefs.ExpInfluenceOnAiDecisions = GamePrefs.expinfluenceonaidecisions;
                prefs.InitiativeTriggerInfluence = GamePrefs.influenceofcommanderinitiativeonattackdefendtrigger;
            }
            catch (Exception ex)
            {
                prefs.Enabled = false;
                OnceLog.Warning("tactical-battle-core:troop-quality-prefs",
                    "[TroopQuality] GamePrefs unreadable; AI decisions use headcount only: " + ex.GetType().Name + " " + ex.Message);
            }
            _prefs = prefs;
        }

        private static bool FlagOn()
        {
            try
            {
                return Plugin.EnableExperienceAndDrillAiWeighting == null || Plugin.EnableExperienceAndDrillAiWeighting.Value;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:troop-quality-flag",
                    "[TroopQuality] flag read failed; treated as off: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static void Reset()
        {
            _read = false;
            _prefs = default;
        }
    }
}
