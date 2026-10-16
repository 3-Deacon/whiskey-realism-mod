using System;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

// One text builder (plan D10): every place name Whiskey passes to vanilla's order panel and every
// content line it adds. Vanilla fills "[VAR47] [VAR48] [VAR49]" from the echelon type names and the raw
// destination name (decompile 223167-223188); a Whiskey key such as "withdrawal-fallback" reads as a
// place there, which is the defect this fixes.
internal static class WlOrderTextTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("wl order text internal key rejects kebab and colon", IsInternalKeyRejectsKebabAndColon),
            ("wl order text internal key rejects single lower word", IsInternalKeyRejectsSingleLowerWord),
            ("wl order text objective name within 150 m", ObjectiveNameWithin150m),
            ("wl order text relative phrase eight sectors yards rounded", RelativePhraseEightSectorsYardsRounded),
            ("wl order text sector boundary goes clockwise", SectorBoundaryGoesClockwise),
            ("wl order text present position under 150 yards", PresentPositionUnder150Yards),
            ("wl order text NaN gives stated position", NaNGivesStatedPosition),
            ("wl order text slot clause appended after place", SlotClauseAppendedAfterPlace),
            ("wl order text slot clause used when given", SlotClauseUsedWhenGiven),
            ("wl order text overlong name falls back to phrase", OverlongNameFallsBackToPhrase),
            ("wl order text line breaks become spaces", LineBreaksBecomeSpaces),
            ("wl order text no line break max 80", NoLineBreakMax80),
            ("wl order text formation name digit gets the", FormationNameDigitGetsThe),
            ("wl order text replace my echelon exact phrase only", ReplaceMyEchelonExactPhraseOnly),
            ("wl order text content templates per task", ContentTemplatesPerTask),
            ("wl order text delay line replaces hold line", DelayLineReplacesHoldLine),
            ("wl order text dawn line only before dawn for attack", DawnLineOnlyBeforeDawnForAttack),
            ("wl order text neighbours omit a missing side", NeighboursOmitAMissingSide),
        });
    }

    // The player's command at the origin facing +z (yaw 0); the target is placed by bearing and metres.
    private static DestinationInput At(float bearingDegrees, float meters, float facingDegrees = 0f, string objective = null, float objectiveMeters = float.NaN, string clause = null)
    {
        double r = bearingDegrees * Math.PI / 180.0;
        return new DestinationInput(objective, objectiveMeters, 0f, 0f, facingDegrees,
            (float)(meters * Math.Sin(r)), (float)(meters * Math.Cos(r)), clause);
    }

    private static void IsInternalKeyRejectsKebabAndColon()
    {
        foreach (string key in new[] { "fallback-line", "withdrawal-fallback", "hold-position", "visible-enemy", "node:5", "a_b", "ridge-a", "objective-3" })
            AssertTrue(WlOrderText.IsInternalKey(key), key + " is an internal key");
        foreach (string name in new[] { "Peach Orchard", "Hood's Division", "Seminary Ridge", "Cemetery Hill", "Round Top" })
            AssertFalse(WlOrderText.IsInternalKey(name), name + " is a real name");
        AssertFalse(WlOrderText.IsInternalKey(null), "null");
        AssertFalse(WlOrderText.IsInternalKey(""), "empty");
    }

    private static void IsInternalKeyRejectsSingleLowerWord()
    {
        // Whiskey's single-word keys ("exit", "objective") and any all-lower-case token.
        AssertTrue(WlOrderText.IsInternalKey("exit"), "exit");
        AssertTrue(WlOrderText.IsInternalKey("objective"), "objective");
        AssertTrue(WlOrderText.IsInternalKey("ridge"), "a lower-case word is no vanilla display name");
        AssertFalse(WlOrderText.IsInternalKey("Exit"), "capitalised is a name");
        AssertFalse(WlOrderText.IsInternalKey("Peach orchard"), "mixed case is a name");
    }

    private static void ObjectiveNameWithin150m()
    {
        AssertEqual("Peach Orchard", WlOrderText.Destination(At(0f, 2000f, objective: "Peach Orchard", objectiveMeters: 100f)), "100 m");
        AssertEqual("Peach Orchard", WlOrderText.Destination(At(0f, 2000f, objective: "Peach Orchard", objectiveMeters: 150f)), "exactly 150 m is inclusive");
        AssertTrue(WlOrderText.Destination(At(0f, 2000f, objective: "Peach Orchard", objectiveMeters: 150.5f)).StartsWith("about "), "past 150 m the phrase");
        AssertTrue(WlOrderText.Destination(At(0f, 2000f, objective: "Peach Orchard", objectiveMeters: float.NaN)).StartsWith("about "), "no objective distance");
        AssertTrue(WlOrderText.Destination(At(0f, 2000f, objective: "ridge", objectiveMeters: 10f)).StartsWith("about "), "a key-like name is rejected");
        AssertTrue(WlOrderText.Destination(At(0f, 2000f, objective: null, objectiveMeters: 10f)).StartsWith("about "), "no name");
        AssertEqual(150f, WlOrderText.ObjectiveNameMeters, "constant");
    }

    private static void RelativePhraseEightSectorsYardsRounded()
    {
        string[] sectors = { "ahead", "ahead and to the right", "to the right", "behind and to the right", "behind", "behind and to the left", "to the left", "ahead and to the left" };
        for (int i = 0; i < 8; i++)
            AssertEqual("about 2,200 yards " + sectors[i] + " of your position",
                WlOrderText.Destination(At(i * 45f, 2000f)),
                "sector " + i + " at bearing " + i * 45);
        // The player's facing rotates the sectors: facing east, a target to the east is ahead.
        AssertEqual("about 2,200 yards ahead of your position", WlOrderText.Destination(At(90f, 2000f, facingDegrees: 90f)), "facing east");
        AssertEqual("about 2,200 yards to the right of your position", WlOrderText.Destination(At(180f, 2000f, facingDegrees: 90f)), "south of an eastward facing is to the right");
        // Yards: metres x 1.0936 to the nearest hundred, half away from zero; a thousands comma.
        AssertEqual("about 1,000 yards ahead of your position", WlOrderText.Destination(At(0f, 914.4f)), "1,000 yd");
        AssertEqual("about 5,500 yards ahead of your position", WlOrderText.Destination(At(0f, 5000f)), "5,468 yd rounds to 5,500");
        AssertEqual("about 200 yards ahead of your position", WlOrderText.Destination(At(0f, 150f)), "164 yd rounds to 200");
        AssertEqual("about 1,300 yards ahead of your position", WlOrderText.Destination(At(0f, 1143.1f)), "1,250.1 yd rounds up");
        AssertEqual("about 1,200 yards ahead of your position", WlOrderText.Destination(At(0f, 1142.9f)), "1,249.9 yd rounds down");
    }

    private static void SectorBoundaryGoesClockwise()
    {
        AssertTrue(WlOrderText.Destination(At(22.4f, 2000f)).Contains(" ahead of "), "22.4 degrees is ahead");
        AssertTrue(WlOrderText.Destination(At(22.5f, 2000f)).Contains("ahead and to the right"), "22.5 degrees belongs to the clockwise sector");
        AssertTrue(WlOrderText.Destination(At(337.4f, 2000f)).Contains("ahead and to the left"), "337.4 degrees");
        AssertTrue(WlOrderText.Destination(At(337.5f, 2000f)).Contains(" ahead of "), "337.5 degrees belongs to ahead (clockwise)");
        AssertTrue(WlOrderText.Destination(At(67.5f, 2000f)).Contains("to the right of"), "67.5 degrees is the right sector");
        AssertTrue(WlOrderText.Destination(At(67.4f, 2000f)).Contains("ahead and to the right"), "67.4 degrees");
    }

    private static void PresentPositionUnder150Yards()
    {
        AssertEqual("your present position", WlOrderText.Destination(At(0f, 0f)), "on the spot");
        AssertEqual("your present position", WlOrderText.Destination(At(90f, 100f)), "109 yd");
        AssertEqual("your present position", WlOrderText.Destination(At(90f, 136.9f)), "149.7 yd is under 150");
        AssertTrue(WlOrderText.Destination(At(90f, 137.2f)).StartsWith("about "), "150.05 yd is not");
    }

    private static void NaNGivesStatedPosition()
    {
        AssertEqual("the stated position", WlOrderText.Destination(new DestinationInput(null, float.NaN, 0f, 0f, 0f, float.NaN, 5f, null)), "NaN x");
        AssertEqual("the stated position", WlOrderText.Destination(new DestinationInput(null, float.NaN, 0f, 0f, 0f, 5f, float.PositiveInfinity, null)), "infinite z");
        AssertEqual("the stated position", WlOrderText.Destination(new DestinationInput(null, float.NaN, float.NaN, 0f, 0f, 5f, 5f, null)), "NaN player");
        AssertEqual("the stated position", WlOrderText.Destination(new DestinationInput(null, float.NaN, 0f, 0f, float.NaN, 500f, 500f, null)), "NaN facing");
    }

    private static void SlotClauseAppendedAfterPlace()
    {
        string clause = "first line, left of Barksdale's Brigade, double line";
        AssertEqual("Peach Orchard — " + clause, WlOrderText.Destination(At(0f, 2000f, objective: "Peach Orchard", objectiveMeters: 10f, clause: clause)), "place, dash, clause");
        string phrase = WlOrderText.Destination(At(0f, 2000f, clause: "first line"));
        AssertEqual("about 2,200 yards ahead of your position — first line", phrase, "after the relative phrase too");
        // A clause too long for the remaining room is cut at the last word boundary that fits.
        string longClause = "first line, left of Barksdale's Brigade, right of Wofford's Brigade and Kershaw's Brigade, double line";
        string cut = WlOrderText.Destination(At(0f, 2000f, objective: "Peach Orchard", objectiveMeters: 10f, clause: longClause));
        AssertTrue(cut.Length <= WlOrderText.MaxDestinationChars, "within the save line: " + cut.Length);
        AssertTrue(cut.StartsWith("Peach Orchard — first line, left of Barksdale's Brigade"), "keeps what fits: " + cut);
        AssertFalse(cut.EndsWith(" ") || cut.EndsWith(","), "cut on a word boundary");
        AssertTrue(longClause.StartsWith(cut.Substring("Peach Orchard — ".Length)), "a prefix of the clause");
        // No room for a clause at all: the place stands alone.
        string bigName = new string('A', 40) + " " + new string('B', 38);
        AssertEqual(bigName, WlOrderText.Destination(At(0f, 2000f, objective: bigName, objectiveMeters: 10f, clause: "first line")), "79 characters leave no room");
    }

    private static void SlotClauseUsedWhenGiven()
    {
        AssertEqual("Peach Orchard", WlOrderText.Destination(At(0f, 2000f, objective: "Peach Orchard", objectiveMeters: 10f, clause: null)), "no clause: place only");
        AssertEqual("Peach Orchard", WlOrderText.Destination(At(0f, 2000f, objective: "Peach Orchard", objectiveMeters: 10f, clause: "  ")), "blank clause: place only");
        AssertTrue(WlOrderText.Destination(At(0f, 2000f, objective: "Peach Orchard", objectiveMeters: 10f, clause: "double line")).EndsWith("double line"), "given clause is used");
    }

    private static void OverlongNameFallsBackToPhrase()
    {
        string name = "The Very Long Name Of A Battlefield Feature That Cannot Possibly Fit On One Saved Line Of Text";
        AssertTrue(name.Length > WlOrderText.MaxDestinationChars, "fixture is over-long");
        string d = WlOrderText.Destination(At(0f, 2000f, objective: name, objectiveMeters: 10f));
        AssertTrue(d.StartsWith("about 2,200 yards"), "falls back to the phrase: " + d);
        string exact = new string('A', 40) + " " + new string('B', 39);
        AssertEqual(80, exact.Length, "fixture is exactly the cap");
        AssertEqual(exact, WlOrderText.Destination(At(0f, 2000f, objective: exact, objectiveMeters: 10f)), "80 characters fit");
    }

    private static void LineBreaksBecomeSpaces()
    {
        AssertEqual("Peach Orchard Gate", WlOrderText.Destination(At(0f, 2000f, objective: "Peach\nOrchard\r\nGate", objectiveMeters: 10f)), "breaks in a name");
        string d = WlOrderText.Destination(At(0f, 2000f, objective: "Peach Orchard", objectiveMeters: 10f, clause: "first line,\nleft of Barksdale"));
        AssertFalse(d.Contains("\n") || d.Contains("\r"), "breaks in a clause");
        AssertEqual("Peach Orchard — first line, left of Barksdale", d, "a break reads as a space");
    }

    private static void NoLineBreakMax80()
    {
        var rng = new Random(7);
        for (int i = 0; i < 300; i++)
        {
            string name = RandomText(rng, rng.Next(0, 120));
            string clause = RandomText(rng, rng.Next(0, 160));
            string d = WlOrderText.Destination(At(rng.Next(0, 360), rng.Next(0, 6000), objective: name, objectiveMeters: rng.Next(0, 200), clause: clause));
            AssertTrue(d.Length <= WlOrderText.MaxDestinationChars, "length " + d.Length + " for '" + name + "'/'" + clause + "'");
            AssertFalse(d.Contains("\n") || d.Contains("\r"), "line break survived");
            AssertTrue(d.Length > 0, "never empty");
        }
    }

    private static string RandomText(Random rng, int length)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJ     \n,-'";
        var chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = alphabet[rng.Next(alphabet.Length)];
        return new string(chars);
    }

    private static void FormationNameDigitGetsThe()
    {
        AssertEqual("Hood's Division", WlOrderText.FormationName("Hood's Division"), "a named formation");
        AssertEqual("The 1st Division", WlOrderText.FormationName("1st Division"), "a numbered one gets 'The'");
        AssertEqual("The 2nd Corps", WlOrderText.FormationName("2nd Corps"), "another");
        AssertEqual("", WlOrderText.FormationName(null), "null");
        AssertEqual("", WlOrderText.FormationName("   "), "blank means no replacement");
        AssertEqual("Hood's Division", WlOrderText.FormationName("  Hood's Division  "), "trimmed");
    }

    private static void ReplaceMyEchelonExactPhraseOnly()
    {
        AssertEqual("Hood's Division is ordered to attack.", WlOrderText.ReplaceMyEchelon("My Division is ordered to attack.", "Division", "Hood's Division"), "the phrase");
        AssertEqual("Tell Hood's Division and then Hood's Division again", WlOrderText.ReplaceMyEchelon("Tell My Division and then My Division again", "Division", "Hood's Division"), "every occurrence");
        AssertEqual("my Division stays", WlOrderText.ReplaceMyEchelon("my Division stays", "Division", "Hood's Division"), "lower-case is left alone (tooltip 465)");
        AssertEqual("with the Division", WlOrderText.ReplaceMyEchelon("with the Division", "Division", "Hood's Division"), "the [VAR47] is left alone (tooltip 585)");
        AssertEqual("My Brigade stays", WlOrderText.ReplaceMyEchelon("My Brigade stays", "Division", "Hood's Division"), "another echelon");
        AssertEqual("ArmyMy Division stays", WlOrderText.ReplaceMyEchelon("ArmyMy Division stays", "Division", "Hood's Division"), "only at a word boundary");
        AssertEqual("My Division stays", WlOrderText.ReplaceMyEchelon("My Division stays", "Division", ""), "no formation name: unchanged");
        AssertEqual("My Division stays", WlOrderText.ReplaceMyEchelon("My Division stays", "", "Hood's Division"), "no echelon name: unchanged");
        AssertEqual(null, WlOrderText.ReplaceMyEchelon(null, "Division", "Hood's Division"), "null in, null out");
        AssertEqual("The 1st Division is ordered", WlOrderText.ReplaceMyEchelon("My Division is ordered", "Division", WlOrderText.FormationName("1st Division")), "numbered");
    }

    private static ContentInput Content(OrderIntentKind kind, string mainEffort = null, bool beforeFirstLight = false, string left = null, string right = null)
        => new ContentInput(kind, mainEffort, beforeFirstLight, left, right);

    private static void ContentTemplatesPerTask()
    {
        AssertEqual("Carry the position and hold it.", WlOrderText.ContentLines(Content(OrderIntentKind.Attack)), "attack");
        AssertEqual("Support Hood's Division at the stated position.", WlOrderText.ContentLines(Content(OrderIntentKind.Support, "Hood's Division")), "support names the main effort");
        AssertEqual("Support the main effort at the stated position.", WlOrderText.ContentLines(Content(OrderIntentKind.Support)), "support without a name");
        AssertEqual("Hold until relieved or ordered back.", WlOrderText.ContentLines(Content(OrderIntentKind.Hold)), "hold");
        AssertEqual("Feel the enemy's line; do not bring on a general engagement.", WlOrderText.ContentLines(Content(OrderIntentKind.Deploy)), "deploy");
        AssertEqual("Contact has ended; hold your present position.", WlOrderText.ContentLines(Content(OrderIntentKind.Release)), "release");
        AssertEqual("Hold the position you have reached and await orders.", WlOrderText.ContentLines(Content(OrderIntentKind.FollowUp)), "follow-up");
        AssertEqual("", WlOrderText.ContentLines(Content(OrderIntentKind.None)), "no template, no lines");
    }

    private static void DelayLineReplacesHoldLine()
    {
        string delay = WlOrderText.ContentLines(Content(OrderIntentKind.Delay));
        AssertEqual("Fall back if pressed; keep contact.", delay, "delay");
        AssertFalse(delay.Contains("Hold until"), "the hold line is replaced, not added to");
    }

    private static void DawnLineOnlyBeforeDawnForAttack()
    {
        AssertEqual("Carry the position and hold it.\nMove at first light.", WlOrderText.ContentLines(Content(OrderIntentKind.Attack, beforeFirstLight: true)), "attack before first light");
        AssertEqual("Carry the position and hold it.", WlOrderText.ContentLines(Content(OrderIntentKind.Attack, beforeFirstLight: false)), "attack by day");
        AssertEqual("Hold until relieved or ordered back.", WlOrderText.ContentLines(Content(OrderIntentKind.Hold, beforeFirstLight: true)), "only an attack waits for light");
        AssertEqual("Contact has ended; hold your present position.", WlOrderText.ContentLines(Content(OrderIntentKind.Release, beforeFirstLight: true)), "nor a release");
    }

    private static void NeighboursOmitAMissingSide()
    {
        AssertEqual("Hold until relieved or ordered back.\nKershaw's Brigade is on your left; Wofford's Brigade is on your right.",
            WlOrderText.ContentLines(Content(OrderIntentKind.Hold, left: "Kershaw's Brigade", right: "Wofford's Brigade")), "both");
        AssertEqual("Hold until relieved or ordered back.\nKershaw's Brigade is on your left.",
            WlOrderText.ContentLines(Content(OrderIntentKind.Hold, left: "Kershaw's Brigade")), "left only");
        AssertEqual("Hold until relieved or ordered back.\nWofford's Brigade is on your right.",
            WlOrderText.ContentLines(Content(OrderIntentKind.Hold, right: "Wofford's Brigade")), "right only");
        AssertEqual("Kershaw's Brigade is on your left.", WlOrderText.ContentLines(Content(OrderIntentKind.None, left: "Kershaw's Brigade")), "neighbours alone");
        string cut = WlOrderText.ContentLines(Content(OrderIntentKind.Hold, left: new string('A', 30) + " " + new string('B', 200)));
        AssertTrue(cut.Split('\n')[1].Length <= 100, "an over-long neighbour name is cut: " + cut.Split('\n')[1].Length);
    }
}
