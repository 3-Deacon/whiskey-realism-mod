using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
    // Reads vanilla game state to populate the war-state booleans that gate
    // SuccessionScheduler events. All reads are reflective + try/catch so game
    // version drift degrades to "gate false" instead of crashing monthly ticks.
    internal static class WarStateObserver
    {
        // Alliance IDs per GTCW convention: 0 = Union, 1 = CSA.
        private const int Union = 0;
        private const int CSA   = 1;

        internal struct Snapshot
        {
            public bool VicksburgFallen;     // Vicksburg owner is no longer CSA
            public bool ChattanoogaFallen;   // Chattanooga owner is no longer CSA
            public bool AtlantaThreatened;   // Atlanta owner is no longer CSA (simplified proxy)
            public bool ANVHasLostMajorBattle;
            public bool JohnstonWoundedOrDisabled;
            public bool BragsCommandRatingLow;
            public bool AoPHasFailedNOffensives;
            public bool BurnsidesFirstDefeatPassed;
            public bool LeeInvadingPennsylvania;
            public bool WesternMajorDefeatPassed;
            public bool DavisPatienceExhausted;
            public bool ValleyOpsNeeded;
            public bool WarClearlyLost;
        }

        internal static Snapshot Observe()
        {
            var history = StrategicCoordinator.Instance?.BattleHistory;
            bool westernMajorDefeat = HasDefeat(history, CSA, majorOnly: true, Theater.West, Theater.River, Theater.TransMiss);
            bool atlantaThreatened = TownOwnershipChanged("Atlanta", originalOwner: CSA);

            return new Snapshot
            {
                VicksburgFallen   = TownOwnershipChanged("Vicksburg",   originalOwner: CSA),
                ChattanoogaFallen = TownOwnershipChanged("Chattanooga", originalOwner: CSA),
                AtlantaThreatened = atlantaThreatened,
                ANVHasLostMajorBattle      = HasDefeat(history, CSA, majorOnly: true, Theater.East),
                JohnstonWoundedOrDisabled  = CommanderHasUnavailableStatus("johnston", CSA),
                BragsCommandRatingLow      = HasCommanderDefeat(history, CSA, "beauregard", majorOnly: false),
                AoPHasFailedNOffensives    = CountDefeats(history, Union, majorOnly: false, Theater.East) >= 2,
                BurnsidesFirstDefeatPassed = HasCommanderDefeat(history, Union, "burnside", majorOnly: false),
                LeeInvadingPennsylvania    = CommanderInState("lee", CSA, "pennsylvania"),
                WesternMajorDefeatPassed   = westernMajorDefeat,
                DavisPatienceExhausted     = atlantaThreatened || westernMajorDefeat,
                ValleyOpsNeeded            = CountDefeats(history, Union, majorOnly: false, Theater.East) > 0,
                WarClearlyLost             = AllianceMoraleBelow(CSA, 0.30f) || (atlantaThreatened && westernMajorDefeat)
            };
        }

        private static bool HasDefeat(List<BattleHistoryRecord> history, int alliance, bool majorOnly, params Theater[] theaters)
            => CountDefeats(history, alliance, majorOnly, theaters) > 0;

        private static int CountDefeats(List<BattleHistoryRecord> history, int alliance, bool majorOnly, params Theater[] theaters)
        {
            if (history == null) return 0;
            int count = 0;
            for (int i = 0; i < history.Count; i++)
            {
                var battle = history[i];
                if (battle == null || !battle.IsLandBattle) continue;
                if (battle.LosingAlliance != alliance) continue;
                if (majorOnly && !battle.IsMajorResult) continue;
                if (theaters != null && theaters.Length > 0 && Array.IndexOf(theaters, battle.Theater) < 0) continue;
                count++;
            }
            return count;
        }

        private static bool HasCommanderDefeat(List<BattleHistoryRecord> history, int alliance, string lastName, bool majorOnly)
        {
            if (history == null) return false;
            for (int i = 0; i < history.Count; i++)
            {
                var battle = history[i];
                if (battle == null || !battle.IsLandBattle) continue;
                if (battle.LosingAlliance != alliance) continue;
                if (majorOnly && !battle.IsMajorResult) continue;
                for (int j = 0; j < battle.CommanderName.Length; j++)
                    if (NameContains(battle.CommanderName[j], lastName)) return true;
            }
            return false;
        }

        private static bool CommanderHasUnavailableStatus(string lastName, int alliance)
        {
            try
            {
                var commanders = AccessTools.Field(AccessTools.TypeByName("GameVars"), "commander")?.GetValue(null) as IList;
                if (commanders == null) return false;
                for (int i = 0; i < commanders.Count; i++)
                {
                    var commander = commanders[i];
                    if (commander == null) continue;
                    var t = commander.GetType();
                    int commanderAlliance = (int)(AccessTools.Field(t, "alliance")?.GetValue(commander) ?? -1);
                    if (commanderAlliance != alliance) continue;
                    string name = AccessTools.Field(t, "combinedname")?.GetValue(commander) as string
                        ?? AccessTools.Field(t, "name")?.GetValue(commander) as string
                        ?? "";
                    if (!NameContains(name, lastName)) continue;
                    int status = (int)(AccessTools.Field(t, "status")?.GetValue(commander) ?? 0);
                    if (status > 0) return true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[WarStateObserver] commander status {lastName}: {ex.Message}");
            }
            return false;
        }

        private static bool CommanderInState(string lastName, int alliance, string stateName)
        {
            try
            {
                var commanders = AccessTools.Field(AccessTools.TypeByName("GameVars"), "commander")?.GetValue(null) as IList;
                if (commanders == null) return false;
                for (int i = 0; i < commanders.Count; i++)
                {
                    var commander = commanders[i];
                    if (commander == null) continue;
                    var t = commander.GetType();
                    int commanderAlliance = (int)(AccessTools.Field(t, "alliance")?.GetValue(commander) ?? -1);
                    if (commanderAlliance != alliance) continue;
                    string name = AccessTools.Field(t, "combinedname")?.GetValue(commander) as string
                        ?? AccessTools.Field(t, "name")?.GetValue(commander) as string
                        ?? "";
                    if (!NameContains(name, lastName)) continue;

                    var command = AccessTools.Field(t, "currentcommand")?.GetValue(commander) as Component;
                    if (command == null) continue;
                    int state = StateAt(command.transform.position);
                    if (StateNameContains(state, stateName)) return true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[WarStateObserver] commander position {lastName}: {ex.Message}");
            }
            return false;
        }

        private static int StateAt(Vector3 position)
        {
            var battlefieldSetup = AccessTools.TypeByName("BattlefieldSetup");
            var getState = AccessTools.Method(battlefieldSetup, "GetStateOfField", new[] { typeof(Vector3), typeof(bool) });
            if (getState == null) return -1;
            return (int)getState.Invoke(null, new object[] { position, true });
        }

        private static bool StateNameContains(int state, string needle)
        {
            var nations = AccessTools.Field(AccessTools.TypeByName("GameVars"), "nation")?.GetValue(null) as IList;
            if (nations == null || state < 0 || state >= nations.Count) return false;
            var nation = nations[state];
            string name = AccessTools.Field(nation.GetType(), "name")?.GetValue(nation) as string ?? "";
            return NameContains(name, needle);
        }

        private static bool AllianceMoraleBelow(int alliance, float threshold)
        {
            try
            {
                var alliances = AccessTools.Field(AccessTools.TypeByName("GameVars"), "alliance")?.GetValue(null) as IList;
                if (alliances == null || alliance < 0 || alliance >= alliances.Count) return false;
                object allianceObj = alliances[alliance];
                float morale = Convert.ToSingle(AccessTools.Field(allianceObj.GetType(), "nationalmorale")?.GetValue(allianceObj) ?? 1f);
                return morale > 0f && morale < threshold;
            }
            catch { return false; }
        }

        private static bool NameContains(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Returns true when the named town's Owner is no longer the original
        // owner — i.e., the town has been captured. False if town can't be
        // resolved (degrades safely; succession event simply doesn't fire).
        private static bool TownOwnershipChanged(string cityName, int originalOwner)
        {
            try
            {
                var townType = AccessTools.TypeByName("Town");
                if (townType == null) return false;
                // Vanilla signature: public static Town GetTownFromName(string name, string statename = "")
                // (decompile line 144004). Takes TWO args; pass empty string for statename.
                var getTownFromName = AccessTools.Method(townType, "GetTownFromName", new[] { typeof(string), typeof(string) });
                if (getTownFromName == null) return false;

                var town = getTownFromName.Invoke(null, new object[] { cityName, "" });
                if (town == null) return false;

                var ownerField = AccessTools.Field(townType, "Owner");
                if (ownerField == null) return false;
                int currentOwner = (int)ownerField.GetValue(town);

                // currentOwner == -1 means uninitialized / abandoned; not "fallen".
                if (currentOwner < 0) return false;
                return currentOwner != originalOwner;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[WarStateObserver] {cityName}: {ex.Message}");
                return false;
            }
        }
    }
}
