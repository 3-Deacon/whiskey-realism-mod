using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class HistoricalFigureRegistry
    {
        private struct Entry
        {
            public string  AllianceTag;
            public string  CanonicalName;
            public PersonalityVector V;
        }

        private static readonly List<Entry> _entries = new List<Entry>
        {
            new Entry { AllianceTag = "CSA",   CanonicalName = "davis",       V = new PersonalityVector(-0.1f, +0.3f, -0.3f, -0.3f, +0.5f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "lee",         V = new PersonalityVector(+0.7f, -0.5f, +0.6f, +0.4f, -0.2f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "johnston",    V = new PersonalityVector(-0.2f, +0.5f, -0.2f, -0.6f, +0.1f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "bragg",       V = new PersonalityVector(+0.2f, +0.3f, -0.4f, -0.1f, +0.4f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "beauregard",  V = new PersonalityVector(+0.4f, -0.1f, +0.5f, +0.1f, -0.1f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "hood",        V = new PersonalityVector(+0.9f, -0.8f, +0.4f, +0.9f, +0.3f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "jackson",     V = new PersonalityVector(+0.8f, -0.5f, +0.8f, +0.4f, -0.5f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "longstreet",  V = new PersonalityVector(+0.4f, +0.1f, +0.3f, -0.2f, -0.1f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "stuart",      V = new PersonalityVector(+0.5f, -0.4f, +0.7f, -0.1f, -0.2f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "forrest",     V = new PersonalityVector(+0.7f, -0.3f, +0.8f, +0.3f, -0.6f) },
            new Entry { AllianceTag = "Union", CanonicalName = "lincoln",     V = new PersonalityVector(+0.3f, +0.1f, +0.1f, +0.4f, +0.7f) },
            new Entry { AllianceTag = "Union", CanonicalName = "scott",       V = new PersonalityVector(-0.1f, +0.4f, +0.2f, -0.4f, +0.3f) },
            new Entry { AllianceTag = "Union", CanonicalName = "mcclellan",   V = new PersonalityVector(-0.3f, +0.9f, -0.6f, -0.7f, +0.6f) },
            new Entry { AllianceTag = "Union", CanonicalName = "halleck",     V = new PersonalityVector( 0f,   +0.6f, -0.3f, -0.2f, +0.5f) },
            new Entry { AllianceTag = "Union", CanonicalName = "pope",        V = new PersonalityVector(+0.6f, -0.4f, +0.4f, +0.2f, +0.4f) },
            new Entry { AllianceTag = "Union", CanonicalName = "burnside",    V = new PersonalityVector(+0.3f, +0.2f, -0.1f, +0.5f, +0.5f) },
            new Entry { AllianceTag = "Union", CanonicalName = "hooker",      V = new PersonalityVector(+0.5f, -0.1f, +0.6f, +0.3f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "meade",       V = new PersonalityVector(+0.2f, +0.4f,  0f,   +0.1f, +0.3f) },
            new Entry { AllianceTag = "Union", CanonicalName = "grant",       V = new PersonalityVector(+0.8f, -0.6f, +0.5f, +0.7f, -0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sherman",     V = new PersonalityVector(+0.7f, -0.4f, +0.9f, +0.5f, -0.5f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sheridan",    V = new PersonalityVector(+0.8f, -0.3f, +0.7f, +0.4f, -0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "thomas",      V = new PersonalityVector(+0.3f, +0.4f, -0.1f, -0.1f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "buell",       V = new PersonalityVector(-0.1f, +0.6f, -0.3f, -0.5f, +0.4f) },
            new Entry { AllianceTag = "Union", CanonicalName = "rosecrans",   V = new PersonalityVector(+0.3f, +0.3f, +0.2f,  0f,   +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "banks",       V = new PersonalityVector(-0.1f, +0.5f, -0.2f,  0f,   +0.7f) }
        };

        internal static (PersonalityVector vector, bool isHistorical) Resolve(object commanderObj, int allianceId)
        {
            try
            {
                var combinedName = Reflection.GetField<string>(commanderObj, "combinedname") ?? "";
                var allianceTag = (allianceId == 1) ? "CSA" : "Union";

                var key = NormalizeLastName(combinedName);
                foreach (var e in _entries)
                {
                    if (e.AllianceTag == allianceTag && e.CanonicalName == key)
                        return (e.V, true);
                }

                return (Derive(commanderObj), false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HistoricalFigureRegistry] resolve failed: {ex.Message}");
                return (default(PersonalityVector), false);
            }
        }

        private static PersonalityVector Derive(object commanderObj)
        {
            bool westpoint = false;
            bool political = false;
            float fame     = 0f;
            float lastfame = 0f;
            try
            {
                var t = commanderObj.GetType();
                var fWP   = AccessTools.Field(t, "westpoint");
                var fPol  = AccessTools.Field(t, "political");
                var fFame = AccessTools.Field(t, "fame");
                var fLast = AccessTools.Field(t, "lastfame");
                if (fWP   != null) westpoint = (bool)fWP.GetValue(commanderObj);
                if (fPol  != null) political = (bool)fPol.GetValue(commanderObj);
                if (fFame != null) fame      = (float)fFame.GetValue(commanderObj);
                if (fLast != null) lastfame  = (float)fLast.GetValue(commanderObj);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HistoricalFigureRegistry] derive read failed: {ex.Message}");
            }

            float agg  = (westpoint ? 0.1f : 0f) + (!political ? 0.2f : 0f);
            float caut = political ? 0.3f : 0f;
            float aud  = westpoint ? 0.2f : 0f;
            float cas  = PersonalityVector.Clamp(0.1f * (fame - lastfame));
            float pol  = political ? 0.4f : 0f;
            return new PersonalityVector(agg, caut, aud, cas, pol);
        }

        private static string NormalizeLastName(string combinedName)
        {
            if (string.IsNullOrWhiteSpace(combinedName)) return "";
            var trimmed = combinedName.Trim();
            var space = trimmed.LastIndexOf(' ');
            var last = (space >= 0) ? trimmed.Substring(space + 1) : trimmed;
            return last.ToLowerInvariant();
        }
    }
}
