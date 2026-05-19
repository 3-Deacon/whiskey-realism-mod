using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class HistoricalFigureRegistry
    {
        internal struct Entry
        {
            public string  AllianceTag;
            public string  CanonicalName;
            public PersonalityVector V;
        }

        private static readonly List<Entry> _entries = new List<Entry>
        {
            // CSA leadership (Davis administration + senior generals)
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
            // CSA additions (corps/division/cavalry commanders + secondary)
            new Entry { AllianceTag = "CSA",   CanonicalName = "polk",        V = new PersonalityVector(+0.3f, +0.2f, +0.0f, +0.2f, +0.3f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "ewell",       V = new PersonalityVector(+0.4f, +0.1f, +0.2f, +0.2f, +0.0f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "hill",        V = new PersonalityVector(+0.5f, -0.2f, +0.3f, +0.4f, -0.1f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "early",       V = new PersonalityVector(+0.5f, -0.1f, +0.4f, +0.3f, +0.0f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "hardee",      V = new PersonalityVector(+0.2f, +0.3f, +0.1f, +0.0f, +0.1f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "cleburne",    V = new PersonalityVector(+0.6f, -0.2f, +0.5f, +0.4f, -0.3f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "pickett",     V = new PersonalityVector(+0.5f, -0.2f, +0.4f, +0.5f, +0.0f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "wheeler",     V = new PersonalityVector(+0.5f, -0.3f, +0.6f, +0.3f, -0.2f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "morgan",      V = new PersonalityVector(+0.6f, -0.4f, +0.7f, +0.4f, -0.3f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "hampton",     V = new PersonalityVector(+0.4f, -0.1f, +0.4f, +0.2f, +0.0f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "anderson",    V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.1f, +0.1f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "mahone",      V = new PersonalityVector(+0.4f, +0.0f, +0.3f, +0.2f, +0.0f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "stephens",    V = new PersonalityVector(+0.0f, +0.4f, -0.2f, -0.2f, +0.6f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "smith",       V = new PersonalityVector(+0.1f, +0.4f, -0.1f, -0.3f, +0.4f) },  // Kirby Smith
            new Entry { AllianceTag = "CSA",   CanonicalName = "vandorn",     V = new PersonalityVector(+0.5f, -0.4f, +0.5f, +0.4f, +0.2f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "magruder",    V = new PersonalityVector(+0.2f, +0.4f, +0.0f, +0.1f, +0.5f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "huger",       V = new PersonalityVector(-0.1f, +0.5f, -0.2f, -0.3f, +0.3f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "pemberton",   V = new PersonalityVector(-0.1f, +0.6f, -0.3f, -0.4f, +0.2f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "loring",      V = new PersonalityVector(+0.1f, +0.3f, +0.0f, -0.2f, +0.4f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "price",       V = new PersonalityVector(+0.3f, +0.0f, +0.2f, +0.0f, +0.3f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "taylor",      V = new PersonalityVector(+0.4f, -0.1f, +0.3f, +0.2f, +0.1f) },  // Richard Taylor
            new Entry { AllianceTag = "CSA",   CanonicalName = "stewart",     V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.0f, +0.1f) },  // A.P. Stewart
            new Entry { AllianceTag = "CSA",   CanonicalName = "buckner",     V = new PersonalityVector(+0.2f, +0.3f, +0.0f, -0.1f, +0.2f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "wise",        V = new PersonalityVector(+0.1f, +0.3f, +0.0f, -0.1f, +0.6f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "floyd",       V = new PersonalityVector(-0.1f, +0.4f, -0.2f, -0.4f, +0.7f) },
            // Union leadership (Lincoln administration + general-in-chief tier)
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
            new Entry { AllianceTag = "Union", CanonicalName = "banks",       V = new PersonalityVector(-0.1f, +0.5f, -0.2f,  0f,   +0.7f) },
            // Union additions (corps/division/cavalry/secondary commanders)
            new Entry { AllianceTag = "Union", CanonicalName = "hunter",      V = new PersonalityVector(+0.3f, +0.0f, +0.1f, +0.4f, +0.4f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sigel",       V = new PersonalityVector(-0.2f, +0.3f, +0.0f, -0.3f, +0.5f) },
            new Entry { AllianceTag = "Union", CanonicalName = "schofield",   V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.1f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "mcpherson",   V = new PersonalityVector(+0.5f, +0.0f, +0.4f, +0.3f, +0.0f) },
            new Entry { AllianceTag = "Union", CanonicalName = "reynolds",    V = new PersonalityVector(+0.4f, +0.1f, +0.3f, +0.3f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "hancock",     V = new PersonalityVector(+0.5f, -0.1f, +0.3f, +0.4f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sedgwick",    V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.2f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "couch",       V = new PersonalityVector(+0.1f, +0.3f, +0.0f, +0.1f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "warren",      V = new PersonalityVector(+0.3f, +0.2f, +0.2f, +0.2f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "porter",      V = new PersonalityVector(+0.2f, +0.3f, +0.0f, +0.0f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "humphreys",   V = new PersonalityVector(+0.3f, +0.1f, +0.2f, +0.2f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "logan",       V = new PersonalityVector(+0.5f, -0.2f, +0.3f, +0.4f, +0.3f) },
            new Entry { AllianceTag = "Union", CanonicalName = "howard",      V = new PersonalityVector(+0.2f, +0.2f, +0.1f, +0.1f, +0.4f) },
            new Entry { AllianceTag = "Union", CanonicalName = "slocum",      V = new PersonalityVector(+0.2f, +0.3f, +0.0f, +0.0f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "buford",      V = new PersonalityVector(+0.5f, -0.2f, +0.5f, +0.3f, -0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "kilpatrick",  V = new PersonalityVector(+0.6f, -0.4f, +0.6f, +0.3f, +0.0f) },
            new Entry { AllianceTag = "Union", CanonicalName = "custer",      V = new PersonalityVector(+0.8f, -0.6f, +0.7f, +0.7f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "ord",         V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.1f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "wallace",     V = new PersonalityVector(+0.3f, +0.1f, +0.3f, +0.2f, +0.2f) },  // Lew Wallace
            new Entry { AllianceTag = "Union", CanonicalName = "fremont",     V = new PersonalityVector(+0.0f, +0.4f, +0.0f, -0.2f, +0.7f) },
            new Entry { AllianceTag = "Union", CanonicalName = "butler",      V = new PersonalityVector(-0.1f, +0.4f, -0.1f, -0.3f, +0.8f) },
            new Entry { AllianceTag = "Union", CanonicalName = "stoneman",    V = new PersonalityVector(+0.2f, +0.2f, +0.3f, +0.1f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "wilson",      V = new PersonalityVector(+0.5f, -0.2f, +0.5f, +0.3f, +0.0f) },  // James H. Wilson
            new Entry { AllianceTag = "Union", CanonicalName = "smith",       V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.1f, +0.1f) },  // A.J. Smith / W.F. Smith — generic Union "Smith"
            new Entry { AllianceTag = "Union", CanonicalName = "miles",       V = new PersonalityVector(+0.3f, +0.2f, +0.2f, +0.1f, +0.0f) },
            new Entry { AllianceTag = "Union", CanonicalName = "doubleday",   V = new PersonalityVector(+0.2f, +0.2f, +0.1f, +0.1f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "newton",      V = new PersonalityVector(+0.2f, +0.2f, +0.1f, +0.0f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sykes",       V = new PersonalityVector(+0.2f, +0.3f, +0.0f, +0.0f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "birney",      V = new PersonalityVector(+0.4f, +0.0f, +0.3f, +0.3f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "barlow",      V = new PersonalityVector(+0.6f, -0.2f, +0.4f, +0.4f, +0.0f) }
        };

        internal static IReadOnlyList<Entry> EntriesForTest => _entries;
        internal static string NormalizeLastNameForTest(string combinedName) => NormalizeLastName(combinedName);

        internal static (PersonalityVector vector, bool isHistorical) Resolve(object commanderObj, int allianceId, string nameHint = null)
        {
            try
            {
                var allianceTag = (allianceId == 1) ? "CSA" : "Union";

                // Primary: vanilla `combinedname` (typically full historical name with rank/title).
                var combinedName = Reflection.GetField<string>(commanderObj, "combinedname") ?? "";
                if (!string.IsNullOrWhiteSpace(combinedName))
                {
                    var key = NormalizeLastName(combinedName);
                    foreach (var e in _entries)
                    {
                        if (e.AllianceTag == allianceTag && e.CanonicalName == key)
                            return (e.V, true);
                    }
                }

                // Fallback: caller-supplied nameHint (typically GameVars.commander[id].name).
                // Vanilla doesn't always populate combinedname during early lifecycle, and the
                // shorter .name field is what the roster discovery already used for the display
                // name (e.g., "Hunter", "Beauregard"). Trying both means the registry matches
                // whichever vanilla field is populated.
                if (!string.IsNullOrWhiteSpace(nameHint))
                {
                    var hintKey = NormalizeLastName(nameHint);
                    if (!string.IsNullOrEmpty(hintKey))
                    {
                        foreach (var e in _entries)
                        {
                            if (e.AllianceTag == allianceTag && e.CanonicalName == hintKey)
                                return (e.V, true);
                        }
                    }
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
            // Strip periods (handles "P.G.T. Beauregard" -> "PGT Beauregard")
            // and commas, then walk tokens from the right looking for the first
            // token >= 3 chars. This skips initials ("P", "G", "T") while keeping
            // legitimate short surnames like "Lee" or "Ord" (length 3 — still kept).
            string compact = combinedName.Replace(".", "").Replace(",", "").Trim();
            string[] tokens = compact.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = tokens.Length - 1; i >= 0; i--)
            {
                if (tokens[i].Length >= 3) return tokens[i].ToLowerInvariant();
            }
            // Fallback: last non-empty token, even if short.
            for (int i = tokens.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(tokens[i])) return tokens[i].ToLowerInvariant();
            }
            return compact.ToLowerInvariant();
        }
    }
}
