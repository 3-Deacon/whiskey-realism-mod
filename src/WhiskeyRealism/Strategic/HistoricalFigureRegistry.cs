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
            /// <summary>Surname, for diagnostics only — never matched alone.</summary>
            public string  CanonicalName;
            /// <summary>Full names as vanilla spells them (Config/commanders.txt combined name).</summary>
            public string[] FullNames;
            public PersonalityVector V;
        }

        private static readonly List<Entry> _entries = new List<Entry>
        {
            // CSA leadership (Davis administration + senior generals)
            new Entry { AllianceTag = "CSA", CanonicalName = "davis", FullNames = new[] { "Jefferson Davis" }, V = new PersonalityVector(-0.1f, +0.3f, -0.3f, -0.3f, +0.5f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "lee", FullNames = new[] { "Robert E. Lee" }, V = new PersonalityVector(+0.7f, -0.5f, +0.6f, +0.4f, -0.2f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "johnston", FullNames = new[] { "Joseph E. Johnston" }, V = new PersonalityVector(-0.2f, +0.5f, -0.2f, -0.6f, +0.1f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "bragg", FullNames = new[] { "Braxton Bragg" }, V = new PersonalityVector(+0.2f, +0.3f, -0.4f, -0.1f, +0.4f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "beauregard", FullNames = new[] { "P.G.T. Beauregard" }, V = new PersonalityVector(+0.4f, -0.1f, +0.5f, +0.1f, -0.1f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "hood", FullNames = new[] { "John B. Hood" }, V = new PersonalityVector(+0.9f, -0.8f, +0.4f, +0.9f, +0.3f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "jackson", FullNames = new[] { "Thomas J. Jackson" }, V = new PersonalityVector(+0.8f, -0.5f, +0.8f, +0.4f, -0.5f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "longstreet", FullNames = new[] { "James Longstreet" }, V = new PersonalityVector(+0.4f, +0.1f, +0.3f, -0.2f, -0.1f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "stuart", FullNames = new[] { "J.E.B. Stuart" }, V = new PersonalityVector(+0.5f, -0.4f, +0.7f, -0.1f, -0.2f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "forrest", FullNames = new[] { "Nathan B. Forrest" }, V = new PersonalityVector(+0.7f, -0.3f, +0.8f, +0.3f, -0.6f) },
            // CSA additions (corps/division/cavalry commanders + secondary)
            new Entry { AllianceTag = "CSA", CanonicalName = "polk", FullNames = new[] { "Leonidas Polk" }, V = new PersonalityVector(+0.3f, +0.2f, +0.0f, +0.2f, +0.3f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "ewell", FullNames = new[] { "Richard S. Ewell" }, V = new PersonalityVector(+0.4f, +0.1f, +0.2f, +0.2f, +0.0f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "hill", FullNames = new[] { "Ambrose P. Hill" }, V = new PersonalityVector(+0.5f, -0.2f, +0.3f, +0.4f, -0.1f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "early", FullNames = new[] { "Jubal A. Early" }, V = new PersonalityVector(+0.5f, -0.1f, +0.4f, +0.3f, +0.0f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "hardee", FullNames = new[] { "William J. Hardee" }, V = new PersonalityVector(+0.2f, +0.3f, +0.1f, +0.0f, +0.1f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "cleburne", FullNames = new[] { "Patrick R. Cleburne" }, V = new PersonalityVector(+0.6f, -0.2f, +0.5f, +0.4f, -0.3f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "pickett", FullNames = new[] { "George E. Pickett" }, V = new PersonalityVector(+0.5f, -0.2f, +0.4f, +0.5f, +0.0f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "wheeler", FullNames = new[] { "Joseph Wheeler" }, V = new PersonalityVector(+0.5f, -0.3f, +0.6f, +0.3f, -0.2f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "morgan", FullNames = new[] { "John H. Morgan" }, V = new PersonalityVector(+0.6f, -0.4f, +0.7f, +0.4f, -0.3f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "hampton", FullNames = new[] { "Wade Hampton" }, V = new PersonalityVector(+0.4f, -0.1f, +0.4f, +0.2f, +0.0f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "anderson", FullNames = new[] { "Richard H. Anderson" }, V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.1f, +0.1f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "mahone", FullNames = new[] { "William Mahone" }, V = new PersonalityVector(+0.4f, +0.0f, +0.3f, +0.2f, +0.0f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "stephens", FullNames = new[] { "Alexander H. Stephens" }, V = new PersonalityVector(+0.0f, +0.4f, -0.2f, -0.2f, +0.6f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "smith", FullNames = new[] { "Edmund Kirby Smith" }, V = new PersonalityVector(+0.1f, +0.4f, -0.1f, -0.3f, +0.4f) },  // Kirby Smith
            new Entry { AllianceTag = "CSA", CanonicalName = "vandorn", FullNames = new[] { "Earl Van Dorn" }, V = new PersonalityVector(+0.5f, -0.4f, +0.5f, +0.4f, +0.2f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "magruder", FullNames = new[] { "John B. Magruder" }, V = new PersonalityVector(+0.2f, +0.4f, +0.0f, +0.1f, +0.5f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "huger", FullNames = new[] { "Benjamin Huger" }, V = new PersonalityVector(-0.1f, +0.5f, -0.2f, -0.3f, +0.3f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "pemberton", FullNames = new[] { "John C. Pemberton" }, V = new PersonalityVector(-0.1f, +0.6f, -0.3f, -0.4f, +0.2f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "loring", FullNames = new[] { "William W. Loring" }, V = new PersonalityVector(+0.1f, +0.3f, +0.0f, -0.2f, +0.4f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "price", FullNames = new[] { "Sterling Price" }, V = new PersonalityVector(+0.3f, +0.0f, +0.2f, +0.0f, +0.3f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "taylor", FullNames = new[] { "Richard Taylor" }, V = new PersonalityVector(+0.4f, -0.1f, +0.3f, +0.2f, +0.1f) },  // Richard Taylor
            new Entry { AllianceTag = "CSA", CanonicalName = "stewart", FullNames = new[] { "Alexander P. Stewart" }, V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.0f, +0.1f) },  // A.P. Stewart
            new Entry { AllianceTag = "CSA", CanonicalName = "buckner", FullNames = new[] { "Simon B. Buckner" }, V = new PersonalityVector(+0.2f, +0.3f, +0.0f, -0.1f, +0.2f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "wise", FullNames = new[] { "Henry A. Wise" }, V = new PersonalityVector(+0.1f, +0.3f, +0.0f, -0.1f, +0.6f) },
            new Entry { AllianceTag = "CSA", CanonicalName = "floyd", FullNames = new[] { "John B. Floyd" }, V = new PersonalityVector(-0.1f, +0.4f, -0.2f, -0.4f, +0.7f) },
            // Union leadership (Lincoln administration + general-in-chief tier)
            new Entry { AllianceTag = "Union", CanonicalName = "lincoln", FullNames = new[] { "Abraham Lincoln" }, V = new PersonalityVector(+0.3f, +0.1f, +0.1f, +0.4f, +0.7f) },
            new Entry { AllianceTag = "Union", CanonicalName = "scott", FullNames = new[] { "Winfield Scott" }, V = new PersonalityVector(-0.1f, +0.4f, +0.2f, -0.4f, +0.3f) },
            new Entry { AllianceTag = "Union", CanonicalName = "mcclellan", FullNames = new[] { "George B. McClellan" }, V = new PersonalityVector(-0.3f, +0.9f, -0.6f, -0.7f, +0.6f) },
            new Entry { AllianceTag = "Union", CanonicalName = "halleck", FullNames = new[] { "Henry W. Halleck" }, V = new PersonalityVector( 0f,   +0.6f, -0.3f, -0.2f, +0.5f) },
            new Entry { AllianceTag = "Union", CanonicalName = "pope", FullNames = new[] { "John Pope" }, V = new PersonalityVector(+0.6f, -0.4f, +0.4f, +0.2f, +0.4f) },
            new Entry { AllianceTag = "Union", CanonicalName = "burnside", FullNames = new[] { "Ambrose E. Burnside" }, V = new PersonalityVector(+0.3f, +0.2f, -0.1f, +0.5f, +0.5f) },
            new Entry { AllianceTag = "Union", CanonicalName = "hooker", FullNames = new[] { "Joseph Hooker" }, V = new PersonalityVector(+0.5f, -0.1f, +0.6f, +0.3f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "meade", FullNames = new[] { "George G. Meade" }, V = new PersonalityVector(+0.2f, +0.4f,  0f,   +0.1f, +0.3f) },
            new Entry { AllianceTag = "Union", CanonicalName = "grant", FullNames = new[] { "Ulysses S. Grant" }, V = new PersonalityVector(+0.8f, -0.6f, +0.5f, +0.7f, -0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sherman", FullNames = new[] { "William T. Sherman" }, V = new PersonalityVector(+0.7f, -0.4f, +0.9f, +0.5f, -0.5f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sheridan", FullNames = new[] { "Philip H. Sheridan" }, V = new PersonalityVector(+0.8f, -0.3f, +0.7f, +0.4f, -0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "thomas", FullNames = new[] { "George H. Thomas" }, V = new PersonalityVector(+0.3f, +0.4f, -0.1f, -0.1f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "buell", FullNames = new[] { "Don Carlos Buell" }, V = new PersonalityVector(-0.1f, +0.6f, -0.3f, -0.5f, +0.4f) },
            new Entry { AllianceTag = "Union", CanonicalName = "rosecrans", FullNames = new[] { "William Rosecrans" }, V = new PersonalityVector(+0.3f, +0.3f, +0.2f,  0f,   +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "banks", FullNames = new[] { "Nathaniel P. Banks" }, V = new PersonalityVector(-0.1f, +0.5f, -0.2f,  0f,   +0.7f) },
            // Union additions (corps/division/cavalry/secondary commanders)
            new Entry { AllianceTag = "Union", CanonicalName = "hunter", FullNames = new[] { "David Hunter" }, V = new PersonalityVector(+0.3f, +0.0f, +0.1f, +0.4f, +0.4f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sigel", FullNames = new[] { "Franz Sigel" }, V = new PersonalityVector(-0.2f, +0.3f, +0.0f, -0.3f, +0.5f) },
            new Entry { AllianceTag = "Union", CanonicalName = "schofield", FullNames = new[] { "John M. Schofield" }, V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.1f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "mcpherson", FullNames = new[] { "James B. McPherson" }, V = new PersonalityVector(+0.5f, +0.0f, +0.4f, +0.3f, +0.0f) },
            new Entry { AllianceTag = "Union", CanonicalName = "reynolds", FullNames = new[] { "John F. Reynolds" }, V = new PersonalityVector(+0.4f, +0.1f, +0.3f, +0.3f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "hancock", FullNames = new[] { "Winfield S. Hancock" }, V = new PersonalityVector(+0.5f, -0.1f, +0.3f, +0.4f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sedgwick", FullNames = new[] { "John Sedgwick" }, V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.2f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "couch", FullNames = new[] { "Darius N. Couch" }, V = new PersonalityVector(+0.1f, +0.3f, +0.0f, +0.1f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "warren", FullNames = new[] { "Gouverneur K. Warren" }, V = new PersonalityVector(+0.3f, +0.2f, +0.2f, +0.2f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "porter", FullNames = new[] { "Fitz J. Porter" }, V = new PersonalityVector(+0.2f, +0.3f, +0.0f, +0.0f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "humphreys", FullNames = new[] { "Andrew A. Humphreys" }, V = new PersonalityVector(+0.3f, +0.1f, +0.2f, +0.2f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "logan", FullNames = new[] { "John A. Logan" }, V = new PersonalityVector(+0.5f, -0.2f, +0.3f, +0.4f, +0.3f) },
            new Entry { AllianceTag = "Union", CanonicalName = "howard", FullNames = new[] { "Oliver O. Howard" }, V = new PersonalityVector(+0.2f, +0.2f, +0.1f, +0.1f, +0.4f) },
            new Entry { AllianceTag = "Union", CanonicalName = "slocum", FullNames = new[] { "Henry W. Slocum" }, V = new PersonalityVector(+0.2f, +0.3f, +0.0f, +0.0f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "buford", FullNames = new[] { "John Buford" }, V = new PersonalityVector(+0.5f, -0.2f, +0.5f, +0.3f, -0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "kilpatrick", FullNames = new[] { "Judson Kilpatrick" }, V = new PersonalityVector(+0.6f, -0.4f, +0.6f, +0.3f, +0.0f) },
            new Entry { AllianceTag = "Union", CanonicalName = "custer", FullNames = new[] { "George A. Custer" }, V = new PersonalityVector(+0.8f, -0.6f, +0.7f, +0.7f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "ord", FullNames = new[] { "Edward O.C. Ord" }, V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.1f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "wallace", FullNames = new[] { "Lew Wallace" }, V = new PersonalityVector(+0.3f, +0.1f, +0.3f, +0.2f, +0.2f) },  // Lew Wallace
            new Entry { AllianceTag = "Union", CanonicalName = "fremont", FullNames = new[] { "John C. Fremont" }, V = new PersonalityVector(+0.0f, +0.4f, +0.0f, -0.2f, +0.7f) },
            new Entry { AllianceTag = "Union", CanonicalName = "butler", FullNames = new[] { "Benjamin F. Butler" }, V = new PersonalityVector(-0.1f, +0.4f, -0.1f, -0.3f, +0.8f) },
            new Entry { AllianceTag = "Union", CanonicalName = "stoneman", FullNames = new[] { "George Stoneman" }, V = new PersonalityVector(+0.2f, +0.2f, +0.3f, +0.1f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "wilson", FullNames = new[] { "James H. Wilson" }, V = new PersonalityVector(+0.5f, -0.2f, +0.5f, +0.3f, +0.0f) },  // James H. Wilson
            new Entry { AllianceTag = "Union", CanonicalName = "smith", FullNames = new[] { "Andrew J. Smith", "William F. Smith" }, V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.1f, +0.1f) },  // A.J. Smith / W.F. Smith — generic Union "Smith"
            new Entry { AllianceTag = "Union", CanonicalName = "miles", FullNames = new[] { "Nelson A. Miles" }, V = new PersonalityVector(+0.3f, +0.2f, +0.2f, +0.1f, +0.0f) },
            new Entry { AllianceTag = "Union", CanonicalName = "doubleday", FullNames = new[] { "Abner Doubleday" }, V = new PersonalityVector(+0.2f, +0.2f, +0.1f, +0.1f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "newton", FullNames = new[] { "John Newton" }, V = new PersonalityVector(+0.2f, +0.2f, +0.1f, +0.0f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sykes", FullNames = new[] { "George Sykes" }, V = new PersonalityVector(+0.2f, +0.3f, +0.0f, +0.0f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "birney", FullNames = new[] { "David B. Birney" }, V = new PersonalityVector(+0.4f, +0.0f, +0.3f, +0.3f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "barlow", FullNames = new[] { "Francis C. Barlow" }, V = new PersonalityVector(+0.6f, -0.2f, +0.4f, +0.4f, +0.0f) }
        };

        internal static IReadOnlyList<Entry> EntriesForTest => _entries;
        internal static string NormalizeFullNameForTest(string fullName) => NormalizeFullName(fullName);

        /// <summary>
        /// Resolves a commander to a historical personality by FULL name.
        /// Surnames collide heavily in GTCW's roster (Config/commanders.txt:
        /// CSA has 7 Lees, 4 Johnstons, 12 Jacksons, 14 Smiths, 11 Andersons),
        /// so a bare surname never matches. Candidates, in order: vanilla
        /// <c>combinedname</c> (= firstname + " " + name, decompile 59081),
        /// <c>firstname + " " + name</c>, then <paramref name="nameHint"/>.
        /// </summary>
        internal static (PersonalityVector vector, bool isHistorical) Resolve(object commanderObj, int allianceId, string nameHint = null)
        {
            try
            {
                var allianceTag = (allianceId == 1) ? "CSA" : "Union";
                string combined = commanderObj == null ? null : Reflection.GetField<string>(commanderObj, "combinedname");
                string first = commanderObj == null ? null : Reflection.GetField<string>(commanderObj, "firstname");
                string last = commanderObj == null ? null : Reflection.GetField<string>(commanderObj, "name");
                string joined = string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last) ? null : first + " " + last;

                if (TryMatch(allianceTag, combined, out var v1)) return (v1, true);
                if (TryMatch(allianceTag, joined, out var v2)) return (v2, true);
                if (TryMatch(allianceTag, nameHint, out var v3)) return (v3, true);

                return (commanderObj == null ? default(PersonalityVector) : Derive(commanderObj), false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HistoricalFigureRegistry] resolve failed: {ex.Message}");
                return (default(PersonalityVector), false);
            }
        }

        private static bool TryMatch(string allianceTag, string fullName, out PersonalityVector vector)
        {
            vector = default(PersonalityVector);
            string key = NormalizeFullName(fullName);
            if (key.IndexOf(' ') < 0) return false;
            foreach (var e in _entries)
            {
                if (e.AllianceTag != allianceTag || e.FullNames == null) continue;
                for (int i = 0; i < e.FullNames.Length; i++)
                {
                    if (NormalizeFullName(e.FullNames[i]) == key)
                    {
                        vector = e.V;
                        return true;
                    }
                }
            }
            return false;
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

        /// <summary>
        /// Lower-case, periods and commas removed, whitespace collapsed:
        /// "P.G.T. Beauregard" -> "pgt beauregard", "Edward O.C. Ord" -> "edward oc ord".
        /// </summary>
        private static string NormalizeFullName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return "";
            string compact = fullName.Replace(".", "").Replace(",", "").Trim().ToLowerInvariant();
            string[] tokens = compact.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", tokens);
        }
    }
}
