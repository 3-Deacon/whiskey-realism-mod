using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WhiskeyRealism;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Util;

using static TestFixtures;
using static TestHarness;

internal static class HarnessSelfTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("assert near rejects nonfinite values", AssertNearRejectsNonfiniteValues),
        });
    }

    private static void AssertNearRejectsNonfiniteValues()
    {
        AssertThrows(() => AssertNear(float.NaN, float.NaN, 0.0001f, "nan pair"), "nan pair");
        AssertThrows(() => AssertNear(float.PositiveInfinity, float.PositiveInfinity, 0.0001f, "infinity pair"), "infinity pair");
        AssertThrows(() => AssertNear(0f, float.NegativeInfinity, 0.0001f, "actual infinity"), "actual infinity");
    }
}
