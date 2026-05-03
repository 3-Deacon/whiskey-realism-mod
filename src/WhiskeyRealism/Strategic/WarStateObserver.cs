using System;
using HarmonyLib;

namespace WhiskeyRealism.Strategic
{
    // Reads vanilla game state to populate the war-state booleans that gate
    // SuccessionScheduler events. v0.2.1 ships town-ownership observers
    // (Vicksburg / Chattanooga / Atlanta) which unlock events #8, #9, and
    // partially #10. Battle-history observers (ANV defeats / AoP offensive
    // failures / Burnside's first defeat / etc.) are deferred to v0.2.2 —
    // they need a battle-result tracking layer that doesn't exist yet.
    //
    // All reads are reflective + try/catch — if a town name doesn't resolve
    // (game version drift, custom scenarios), we degrade to "not fallen"
    // rather than crash.
    internal static class WarStateObserver
    {
        // Alliance IDs per GTCW convention: 0 = Union, 1 = CSA.
        private const int Union = 0;
        private const int CSA   = 1;

        internal struct Snapshot
        {
            public bool VicksburgFallen;     // Vicksburg owner is no longer CSA
            public bool ChattanoogaFallen;   // Chattanooga owner is no longer CSA
            public bool AtlantaThreatened;   // Atlanta owner is no longer CSA (simplified — refine in v0.2.2 with proximity check)
            public bool ANVHasLostMajorBattle;       // v0.2.2 — needs battle-history layer
            public bool JohnstonWoundedOrDisabled;   // v0.2.2 — needs commander health/disable check
            public bool BragsCommandRatingLow;       // v0.2.2 — needs commander rating threshold
            public bool AoPHasFailedNOffensives;     // v0.2.2 — needs offensive-history layer
            public bool BurnsidesFirstDefeatPassed;  // v0.2.2
            public bool LeeInvadingPennsylvania;     // v0.2.2 — needs army-position lookup
            public bool WesternMajorDefeatPassed;    // v0.2.2
            public bool DavisPatienceExhausted;      // v0.2.2 — abstract; might never wire
            public bool ValleyOpsNeeded;             // v0.2.2
            public bool WarClearlyLost;              // v0.2.2 — total morale + economy below threshold
        }

        internal static Snapshot Observe()
        {
            return new Snapshot
            {
                VicksburgFallen   = TownOwnershipChanged("Vicksburg",   originalOwner: CSA),
                ChattanoogaFallen = TownOwnershipChanged("Chattanooga", originalOwner: CSA),
                AtlantaThreatened = TownOwnershipChanged("Atlanta",     originalOwner: CSA),
                // v0.2.2 fields stay default (false) — events that gate on these can't fire yet.
            };
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
