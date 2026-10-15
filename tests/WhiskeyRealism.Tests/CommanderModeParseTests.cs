using System;
using WhiskeyRealism.Tactical.Operations;

internal static class CommanderModeParseTests
{
    public static void Run()
    {
        RecognizesDocumentedValuesCaseInsensitive();
        FlagsTyposAsUnrecognized();
        BlankIsRecognizedAsDefault();
    }

    private static void RecognizesDocumentedValuesCaseInsensitive()
    {
        foreach (var raw in new[] { "Off", "off", " OFF ", "MonitorOnly", "monitor-only", "Monitor Only", "Active", "active" })
            TestHarness.AssertTrue(TacticalCommanderModePolicy.IsRecognized(raw), "recognized: " + raw);
        TestHarness.AssertEqual(TacticalCommanderMode.Off, TacticalCommanderModePolicy.Parse(" OFF ", TacticalCommanderMode.Active), "off parses");
        Console.WriteLine("PASS commander mode recognizes documented values case-insensitively");
    }

    private static void FlagsTyposAsUnrecognized()
    {
        foreach (var raw in new[] { "Disabled", "None", "false", "Monitor" })
            TestHarness.AssertFalse(TacticalCommanderModePolicy.IsRecognized(raw), "unrecognized: " + raw);
        Console.WriteLine("PASS commander mode flags typos as unrecognized");
    }

    private static void BlankIsRecognizedAsDefault()
    {
        TestHarness.AssertTrue(TacticalCommanderModePolicy.IsRecognized(""), "blank recognized");
        TestHarness.AssertTrue(TacticalCommanderModePolicy.IsRecognized(null), "null recognized");
        Console.WriteLine("PASS commander mode treats blank as default");
    }
}
