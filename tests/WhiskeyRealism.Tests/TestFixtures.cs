using System.IO;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;

// Test-only builders and oracles for production seams (kept out of src so the
// production assembly carries no test-only members).
internal static class TestFixtures
{
    // Deployment terrain rules the discipline tests are written against.
    internal static TacticalTerrainRules DeploymentTerrainRules =>
        new TacticalTerrainRules(60f, 90f, requireDeploymentZone: true, requireVisibleEnemyForFacing: false);

    // Oracle: vanilla AIBattle.AdjustGroupAIStance re-evaluates a group's stance
    // only once lastaistancechangetime < now + updatetimeforresettingofaistance
    // (decompile 4264).
    internal static bool VanillaWouldReevaluateStance(float lastStanceChangeHours, float nowHours, float vanillaResetHours)
        => lastStanceChangeHours < nowHours + vanillaResetHours;

    internal static TacticalDirectChildGate.Input WithIntendedTargetSector(this TacticalDirectChildGate.Input input, int sector)
        => new TacticalDirectChildGate.Input(
            input.GateEnabled, input.SideIsAi, input.Role, input.AxisSector, input.PrimarySector,
            input.IntendedTargetBearingFromGroupRadians, input.IntendedTargetDistanceFromGroup,
            input.NearestEnemyBearingFromGroupRadians, input.FeudMaxDistance, sector);

    internal static bool TryEnqueue(this TelemetryQueue queue, TelemetryEvent ev)
        => queue.Enqueue(ev) == TelemetryQueueResult.Enqueued;

    internal static string CreateSessionDirectory(string gameRoot, string sessionId)
    {
        string directory = TelemetrySession.SessionDirectory(gameRoot, sessionId);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
