using System;
using System.Collections.Generic;

/// <summary>
/// Optional run filter set by <c>--only &lt;class&gt; [&lt;test prefix&gt;]</c>, so a red-first run executes one class or one test.
/// Both fields are null in the default run, which then runs everything.
/// </summary>
internal static class TestFilter
{
    internal static string Class;   // null: every class (the default run)
    internal static string Name;    // null: every test

    internal static bool RunsClass(string className) => Class == null || className == Class;

    // A name filter matches the whole test name or a prefix that ends at a space,
    // so "bc14 FEAS-SCN-1" selects "bc14 FEAS-SCN-1 ..." and not "bc14 FEAS-SCN-10 ...".
    internal static bool RunsTest(string testName) =>
        Name == null || testName == Name || testName.StartsWith(Name + " ", StringComparison.Ordinal);
}

/// <summary>
/// Shared assertions and the per-class test runner. Test classes are discovered
/// by Program.Main; see AGENTS.md in this directory.
/// </summary>
internal static class TestHarness
{
    // Runs each test in order and prints "PASS <name>". A failing test throws out
    // of the harness, which ends the run with a non-zero exit code.
    internal static void RunAll((string name, Action run)[] tests)
    {
        foreach (var test in tests)
        {
            if (!TestFilter.RunsTest(test.name)) continue;
            test.run();
            Console.WriteLine("PASS " + test.name);
        }
    }

    internal static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception("expected " + expected + " but got " + actual);
    }

    internal static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(label + ": expected " + expected + " got " + actual);
    }

    internal static void AssertContains(string value, string expected, string label)
    {
        if (value == null || !value.Contains(expected))
            throw new Exception(label + ": expected '" + value + "' to contain '" + expected + "'");
    }

    internal static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    internal static void AssertFalse(bool condition, string message)
    {
        if (condition) throw new Exception(message);
    }

    internal static void AssertFinite(float value, string label)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new Exception(label + ": expected finite value but got " + value);
    }

    internal static void AssertNear(float expected, float actual, float tolerance, string label)
    {
        if (float.IsNaN(expected) || float.IsInfinity(expected) || float.IsNaN(actual) || float.IsInfinity(actual))
            throw new Exception(label + ": expected finite values but got expected " + expected + " actual " + actual);
        if (Math.Abs(expected - actual) > tolerance)
            throw new Exception(label + ": expected " + expected + " got " + actual);
    }

    internal static void AssertThrows(Action action, string label)
    {
        try
        {
            action();
        }
        catch
        {
            return;
        }

        throw new Exception(label + ": expected exception");
    }
}
