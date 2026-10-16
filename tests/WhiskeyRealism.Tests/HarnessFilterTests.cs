using System;
using static TestHarness;

/// <summary>The <c>--only</c> filter: one class or one test, and no effect when unset.</summary>
internal static class HarnessFilterTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("harness filter name prefix ends at a space", NamePrefixEndsAtSpace),
            ("harness filter unset accepts everything", UnsetAcceptsEverything),
            ("harness filter class accepts only the named class", ClassAcceptsOnlyNamed),
        });
    }

    static void NamePrefixEndsAtSpace()
    {
        string savedClass = TestFilter.Class, savedName = TestFilter.Name;
        try
        {
            TestFilter.Class = null;
            TestFilter.Name = "bc14 FEAS-SCN-1";
            AssertTrue(TestFilter.RunsTest("bc14 FEAS-SCN-1 a superior force converges"), "the prefix selects its own test");
            AssertTrue(TestFilter.RunsTest("bc14 FEAS-SCN-1"), "the exact name selects itself");
            AssertFalse(TestFilter.RunsTest("bc14 FEAS-SCN-10 behind on points"), "FEAS-SCN-10 is another test");
            AssertFalse(TestFilter.RunsTest("bc14 FEAS-SCN-2 piecemeal"), "FEAS-SCN-2 is another test");
        }
        finally { TestFilter.Class = savedClass; TestFilter.Name = savedName; }
    }

    static void UnsetAcceptsEverything()
    {
        string savedClass = TestFilter.Class, savedName = TestFilter.Name;
        try
        {
            TestFilter.Class = null;
            TestFilter.Name = null;
            AssertTrue(TestFilter.RunsTest("anything at all"), "no name filter runs every test");
            AssertTrue(TestFilter.RunsClass("AnyTests"), "no class filter runs every class");
        }
        finally { TestFilter.Class = savedClass; TestFilter.Name = savedName; }
    }

    static void ClassAcceptsOnlyNamed()
    {
        string savedClass = TestFilter.Class, savedName = TestFilter.Name;
        try
        {
            TestFilter.Class = "ManeuverFeasibilityTests";
            TestFilter.Name = null;
            AssertTrue(TestFilter.RunsClass("ManeuverFeasibilityTests"), "the named class runs");
            AssertFalse(TestFilter.RunsClass("ManeuverFeasibilityTestsOther"), "another class does not");
            AssertFalse(TestFilter.RunsClass("HarnessFilterTests"), "another class does not");
        }
        finally { TestFilter.Class = savedClass; TestFilter.Name = savedName; }
    }
}
