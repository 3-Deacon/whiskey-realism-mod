using System;
using System.Collections.Generic;
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
    //     macro triggers are divided by 1 + initiative × this (6396-6430);
    //   unbearable-losses rule (GetUnbearableLossesTrigger 113363-113400): unitprefs
    //     triggers / influences / skirmisher / per-day factors (53594-53604, 53927-53929),
    //     campaignprefs aibonus_restoringmorale (56359), GameVars ai_vs_ai,
    //     aicheat_shiftedbonusmoralelosstrigger, usedcampaignbonusrunning;
    //   micro-charge odds: microaitriggerforcharge and weightingfactorsformicroaicharge[0..3]
    //     (54776-54781).
    // Commander leadership for the break rule (GetCommanderLeadership 61134: parent commander
    // influence walks the command transform) is cached per commander for
    // LeadershipRefreshHours of doctrine time.
    internal static class TroopQualityRuntime
    {
        internal const double LeadershipRefreshHours = 0.25;

        private static bool _read;
        private static TroopQualityPrefs _prefs;
        private static readonly Dictionary<int, float> _leadership = new Dictionary<int, float>(256);
        private static readonly Dictionary<int, double> _leadershipAt = new Dictionary<int, double>(256);

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
                prefs.Break = new BreakToleranceRules
                {
                    Trigger0 = GamePrefs.unbearablelossesmoraletrigger0,
                    Trigger100 = GamePrefs.unbearablelossesmoraletrigger100,
                    InfluenceExperience = GamePrefs.influenceexpunbearablelossestrigger,
                    InfluenceTraining = GamePrefs.influencetrainingunbearablelossestrigger,
                    InfluenceLeadership = GamePrefs.influenceleadershipunbearablelossestrigger,
                    SkirmisherFactor = GamePrefs.unbearablelossestriggerskirmishers,
                    AiBonusRestoringMorale = GamePrefs.aibonus_restoringmorale,
                    AiShiftedBonus = GameVars.aicheat_shiftedbonusmoralelosstrigger,
                    AiVsAi = GameVars.ai_vs_ai,
                    UsedCampaignBonusRunning = GameVars.usedcampaignbonusrunning,
                    PerDayExperience0 = GamePrefs.unbearablelosstriggerincreaseperdayfactorexp0,
                    PerDayExperience100 = GamePrefs.unbearablelosstriggerincreaseperdayfactorexp100,
                };
                float[] weights = GamePrefs.weightingfactorsformicroaicharge;
                if (weights != null && weights.Length >= 4)
                {
                    prefs.ChargeOdds = new ChargeOddsRules
                    {
                        Trigger = GamePrefs.microaitriggerforcharge,
                        WeightStrength = weights[0],
                        WeightExperience = weights[1],
                        WeightMorale = weights[2],
                        WeightFatigue = weights[3],
                    };
                }
            }
            catch (Exception ex)
            {
                prefs.Enabled = false;
                OnceLog.Warning("tactical-battle-core:troop-quality-prefs",
                    "[TroopQuality] GamePrefs unreadable; AI decisions use headcount only: " + ex.GetType().Name + " " + ex.Message);
            }
            _prefs = prefs;
        }

        /// <summary>
        /// The commander's vanilla leadership (GetCommanderLeadership 61134, with its parent
        /// commander influence), cached per commander for LeadershipRefreshHours of doctrine
        /// time. 0 for an unknown commander. Never throws.
        /// </summary>
        internal static float Leadership(int commander, double nowHours)
        {
            if (_leadershipAt.TryGetValue(commander, out double at) && nowHours - at < LeadershipRefreshHours && nowHours >= at)
                return _leadership[commander];
            float value = 0f;
            try
            {
                var commanders = GameVars.commander;
                if (commanders != null && commander >= 0 && commander < commanders.Count && commanders[commander] != null)
                    value = commanders[commander].GetCommanderLeadership();
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:troop-quality-leadership",
                    "[TroopQuality] commander leadership unreadable; read as 0: " + ex.GetType().Name + " " + ex.Message);
            }
            if (float.IsNaN(value) || float.IsInfinity(value)) value = 0f;
            _leadership[commander] = value;
            _leadershipAt[commander] = nowHours;
            return value;
        }

        /// <summary>
        /// Campaign (BC8, read at call time, not battle-scoped):
        /// GamePrefs.commanderinfluenceinitiativeforoperations (campaignprefs 0.25), the factor
        /// CheckOffensiveMovements scales its dominance requirement by (14250 / 14281); 0 with
        /// the weighting off or the pref unreadable.
        /// </summary>
        internal static float CampaignOffensiveInitiativeInfluence()
        {
            if (!FlagOn()) return 0f;
            try
            {
                return GamePrefs.commanderinfluenceinitiativeforoperations;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-troop-quality:initiative-pref",
                    "[TroopQuality] commanderinfluenceinitiativeforoperations unreadable; offensive ratios unscaled: " + ex.GetType().Name + " " + ex.Message);
                return 0f;
            }
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
            _leadership.Clear();
            _leadershipAt.Clear();
        }
    }
}
