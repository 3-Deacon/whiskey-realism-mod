using System;
using System.Collections.Generic;

/// <summary>
/// Shared test harness helpers for assertions and test registration.
/// </summary>
internal static class TestHarness
{
    public static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(label + ": expected " + expected + " got " + actual);
    }

    public static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static void AssertFalse(bool condition, string message)
    {
        if (condition)
            throw new Exception(message);
    }
}
