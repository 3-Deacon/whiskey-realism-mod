using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    // What a Whiskey order is for, as far as the panel text goes (D10). Vanilla writes its own text for
    // every order; Whiskey's orders add a condition line under it.
    internal enum OrderIntentKind
    {
        None,
        Attack,
        Support,
        Hold,
        Delay,
        Deploy,
        Release,
        FollowUp,
    }

    // The place an order names: the nearest vanilla objective (name and its distance from the order's
    // point), else the point relative to the player's command.
    internal readonly struct DestinationInput
    {
        public DestinationInput(string objectiveName, float objectiveMeters, float playerX, float playerZ,
            float playerFacingDegrees, float targetX, float targetZ, string slotClause)
        {
            ObjectiveName = objectiveName;
            ObjectiveMeters = objectiveMeters;
            PlayerX = playerX;
            PlayerZ = playerZ;
            PlayerFacingDegrees = playerFacingDegrees;
            TargetX = targetX;
            TargetZ = targetZ;
            SlotClause = slotClause;
        }

        public string ObjectiveName { get; }
        // Distance from the order's point to that objective; NaN when there is none.
        public float ObjectiveMeters { get; }
        public float PlayerX { get; }
        public float PlayerZ { get; }
        // Unity yaw, degrees clockwise from +z: the player's command's facing.
        public float PlayerFacingDegrees { get; }
        public float TargetX { get; }
        public float TargetZ { get; }
        // BC14's slot clause ("first line, left of Barksdale's Brigade, double line"), when it gives one.
        public string SlotClause { get; }
    }

    internal readonly struct ContentInput
    {
        public ContentInput(OrderIntentKind intent, string mainEffortName, bool beforeFirstLight, string leftName, string rightName)
        {
            Intent = intent;
            MainEffortName = mainEffortName;
            BeforeFirstLight = beforeFirstLight;
            LeftName = leftName;
            RightName = rightName;
        }

        public OrderIntentKind Intent { get; }
        public string MainEffortName { get; }
        // The order is delivered before the battle day has begun (the hour is below the month's day start).
        public bool BeforeFirstLight { get; }
        public string LeftName { get; }
        public string RightName { get; }
    }

    // The one W&L order text builder (plan D10): every place name Whiskey passes to vanilla's order panel
    // and every content line it adds. Pure.
    //
    // Vanilla fills a subordinate order's text from the echelon type name of the issuing unit ([VAR47],
    // "My Division"), the player's ([VAR48]) and the raw destination name ([VAR49]), decompile
    // 223167-223188, so whatever Whiskey passes as a destination is printed as a place. Whiskey's own
    // keys ("withdrawal-fallback", "objective") used to be printed there; this builder passes real
    // places or plain words only.
    internal static class WlOrderText
    {
        public const float ObjectiveNameMeters = 150f;
        // One save line (the destination is saved raw on a single line, 45117).
        public const int MaxDestinationChars = 80;
        private const int MinClauseChars = 8;
        private const int MaxNeighbourNameChars = 60;
        private const float YardsPerMeter = 1.0936f;
        private const double BoundaryToleranceDegrees = 1e-3;
        private const string Dash = " — ";

        private static readonly Regex InternalKey = new Regex(@"^[a-z0-9]+([-_:][a-z0-9]+)*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] Sectors =
        {
            "ahead", "ahead and to the right", "to the right", "behind and to the right",
            "behind", "behind and to the left", "to the left", "ahead and to the left",
        };

        // Whiskey's keys are all-lower-case tokens joined by - _ or :; vanilla's display names are capitalised.
        public static bool IsInternalKey(string s)
        {
            return !string.IsNullOrEmpty(s) && InternalKey.IsMatch(s);
        }

        // place [+ " — " + slot clause]; at most MaxDestinationChars, never a line break.
        public static string Destination(in DestinationInput input)
        {
            string place = Place(in input);
            string clause = Flatten(input.SlotClause);
            if (clause.Length == 0) return place;
            int room = MaxDestinationChars - place.Length - Dash.Length;
            if (room < MinClauseChars) return place;
            return place + Dash + CutAtWord(clause, room);
        }

        private static string Place(in DestinationInput input)
        {
            string name = Flatten(input.ObjectiveName);
            if (name.Length > 0 && !IsInternalKey(name) && name.Length <= MaxDestinationChars
                && !float.IsNaN(input.ObjectiveMeters) && input.ObjectiveMeters <= ObjectiveNameMeters)
                return name;
            return RelativePhrase(in input);
        }

        private static string RelativePhrase(in DestinationInput input)
        {
            if (!IsFinite(input.PlayerX) || !IsFinite(input.PlayerZ) || !IsFinite(input.TargetX) || !IsFinite(input.TargetZ)
                || !IsFinite(input.PlayerFacingDegrees))
                return "the stated position";

            double dx = (double)input.TargetX - input.PlayerX;
            double dz = (double)input.TargetZ - input.PlayerZ;
            double yards = Math.Sqrt(dx * dx + dz * dz) * YardsPerMeter;
            if (yards < 150.0) return "your present position";

            // Bearing in Unity yaw (clockwise from +z), relative to where the player's command faces.
            double bearing = Math.Atan2(dx, dz) * 180.0 / Math.PI;
            double relative = Normalize360(bearing - input.PlayerFacingDegrees);
            // A boundary angle belongs to the clockwise sector; the tolerance covers float coordinates.
            int sector = (int)Math.Floor((relative + 22.5 + BoundaryToleranceDegrees) / 45.0) % 8;
            double hundreds = Math.Round(yards / 100.0, MidpointRounding.AwayFromZero) * 100.0;
            return "about " + hundreds.ToString("N0", CultureInfo.InvariantCulture) + " yards " + Sectors[sector] + " of your position";
        }

        // The issuing unit's GameObject name as a formation is called: "Hood's Division"; a numbered one
        // reads "The 1st Division". Blank means no replacement.
        public static string FormationName(string gameObjectName)
        {
            string name = Flatten(gameObjectName);
            if (name.Length == 0) return string.Empty;
            return char.IsDigit(name[0]) ? "The " + name : name;
        }

        // Vanilla writes the issuing unit as "My [VAR47]" ("My Division", tooltips_dlcwl 288, 294, ...);
        // replaces every case-sensitive "My " + echelon type name, at a word boundary, with the formation's
        // name. "my [VAR47]" (465) and "with the [VAR47]" (585) are left as vanilla wrote them.
        public static string ReplaceMyEchelon(string content, string echelonTypeName, string formationName)
        {
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(echelonTypeName) || string.IsNullOrEmpty(formationName)) return content;
            string phrase = "My " + echelonTypeName;
            var sb = new StringBuilder(content.Length + 16);
            int at = 0;
            while (at < content.Length)
            {
                int hit = content.IndexOf(phrase, at, StringComparison.Ordinal);
                if (hit < 0) break;
                bool boundaryBefore = hit == 0 || !char.IsLetterOrDigit(content[hit - 1]);
                int end = hit + phrase.Length;
                bool boundaryAfter = end >= content.Length || !char.IsLetter(content[end]);
                sb.Append(content, at, hit - at);
                sb.Append(boundaryBefore && boundaryAfter ? formationName : phrase);
                at = end;
            }
            if (at == 0) return content;
            sb.Append(content, at, content.Length - at);
            return sb.ToString();
        }

        // Whiskey's own lines under vanilla's text: intent, timing, neighbours. Lines joined with "\n";
        // empty when there are none.
        public static string ContentLines(in ContentInput input)
        {
            var sb = new StringBuilder();
            switch (input.Intent)
            {
                case OrderIntentKind.Attack:
                    Line(sb, "Carry the position and hold it.");
                    if (input.BeforeFirstLight) Line(sb, "Move at first light.");
                    break;
                case OrderIntentKind.Support:
                    string main = Flatten(input.MainEffortName);
                    Line(sb, "Support " + (main.Length > 0 ? main : "the main effort") + " at the stated position.");
                    break;
                case OrderIntentKind.Hold:
                    Line(sb, "Hold until relieved or ordered back.");
                    break;
                case OrderIntentKind.Delay:
                    Line(sb, "Fall back if pressed; keep contact.");
                    break;
                case OrderIntentKind.Deploy:
                    Line(sb, "Feel the enemy's line; do not bring on a general engagement.");
                    break;
                case OrderIntentKind.Release:
                    Line(sb, "Contact has ended; hold your present position.");
                    break;
                case OrderIntentKind.FollowUp:
                    Line(sb, "Hold the position you have reached and await orders.");
                    break;
            }

            string left = CutAtWord(Flatten(input.LeftName), MaxNeighbourNameChars);
            string right = CutAtWord(Flatten(input.RightName), MaxNeighbourNameChars);
            if (left.Length > 0 && right.Length > 0) Line(sb, left + " is on your left; " + right + " is on your right.");
            else if (left.Length > 0) Line(sb, left + " is on your left.");
            else if (right.Length > 0) Line(sb, right + " is on your right.");
            return sb.ToString();
        }

        private static void Line(StringBuilder sb, string line)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }

        // Line breaks become single spaces; null and blank become empty.
        private static string Flatten(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            bool any = false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] == '\n' || s[i] == '\r') { any = true; break; }
            if (!any) return s.Trim();
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r' || c == '\n')
                {
                    if (sb.Length == 0 || sb[sb.Length - 1] != ' ') sb.Append(' ');
                    while (i + 1 < s.Length && (s[i + 1] == '\r' || s[i + 1] == '\n')) i++;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Trim();
        }

        // At most `max` characters, cut at the last word boundary that fits, with no dangling separator.
        private static string CutAtWord(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || max <= 0) return string.Empty;
            if (s.Length <= max) return s;
            int cut = s.LastIndexOf(' ', max);
            if (cut <= 0) cut = max;
            return s.Substring(0, cut).TrimEnd(' ', ',', ';', ':', '-');
        }

        private static double Normalize360(double degrees)
        {
            double d = degrees % 360.0;
            return d < 0.0 ? d + 360.0 : d;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
