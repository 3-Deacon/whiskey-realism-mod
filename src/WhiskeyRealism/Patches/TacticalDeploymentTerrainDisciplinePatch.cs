using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #60 — Vanilla BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew
    // (decompile 85524) places AI groups immediately, then clamps deployment-zone
    // and terrain state (MoveAllUnitsIntoDeploymentZone 85862). Vanilla chooses each
    // group's objective and facing from the TRUE enemy: the candidate objectives exclude any
    // with an enemy unit within 50 m (a scan of the whole unit list, 85604-85618), day 1 takes the
    // objective nearest any enemy group (86260-86281), and the facing is the bearing to
    // unitrange.closestenemyunitfar (85809-85815).
    //
    // The Prefix (non-skipping) records the side's group positions and the W&L command's AI
    // group before vanilla runs. The Postfix (BC10 decisions 7-9):
    //   1. fog-honest re-placement (DeploymentPlanRuntime / DeploymentDoctrine): the groups vanilla
    //      moved in this call are re-planned from the assumed enemy point (what the side sees, else
    //      the nearest enemy-owned entry point, else the map centre) and a third of the eligible
    //      direct children go 425 m back as a reserve echelon; groups whose choice differs are
    //      placed again;
    //   2. terrain discipline (as before): a group left on water or outside its zone is moved to a
    //      terrain-safe candidate, facing the same assumed enemy point.
    // Every write is an immediate-placement Move through the battle core's order executor
    // (OrderExecutorRuntime.TryPlanInline / ExecutePlannedInline); this patch calls no vanilla
    // writer. Fog-honest re-placement and the reserve are gated by Enable Deployment And March
    // Doctrine; the terrain discipline by Enable Tactical Deployment Terrain Discipline.
    //
    // Vanilla rules (WS T3):
    //   - SetGroupFormation's targetpos is the LINE FRONT; vanilla puts the HQ at
    //     targetpos + (groupdepth + groupunitdistancetoback) (92044-92058). The
    //     corrected HQ point is converted to the front the same way, and the
    //     front is re-checked for water / deployment zone;
    //   - groups vanilla placed on fortification lines (num3 == 1, 85684-85751,
    //     ignoredeplyomentzone: true) are left alone, so a Defender whose groups all take
    //     fortifications gets no deployment reserve;
    //   - the W&L current command's AI group is never re-placed (the command and its parent
    //     stay together; vanilla re-attaches the command for this call, 85650-85674);
    //   - fleets and zones with fewer than 2 frontline vectors are skipped;
    //   - writes go through TacticalWriteGuard.CanWrite.
    [HarmonyPatch(typeof(BattleUnits), "DoPlacementAIUnitsWithinDeploymentzoneNew")]
    internal static class TacticalDeploymentTerrainDisciplinePatch
    {
        private const int DefaultMaxCandidates = 16;
        private const int MaxAdviceRowsPerBattle = 64;
        private const float DefaultMaxCorrectionMeters = 60f;
        private const float DefaultPreferredFacingDeltaDegrees = 90f;
        // A group counts as placed by vanilla in this call when its position changed by more than this.
        private const float MovedMeters = 1f;

        private static readonly FieldInfo GrpField = AccessTools.Field(typeof(BattleUnits), "grp");
        private static readonly FieldInfo EodCycleField = AccessTools.Field(typeof(BattleUnits), "eodcycle");
        private static readonly FieldInfo BattlePassedDaysField = AccessTools.Field(typeof(BattleUnits), "battlepasseddays");
        private static readonly HashSet<string> EmittedAdvice = new HashSet<string>();
        private static string _emittedAdviceBattleKey;

        // Records what vanilla's placement is about to move. Never skips vanilla, writes nothing.
        [HarmonyPrefix]
        internal static void Prefix(BattleUnits __instance, int foralliance, out DeploymentPass __state)
        {
            __state = null;
            using (TelemetryPerf.Scope("tactical.patch.deployment-terrain.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                PatchFirstFire.Mark("tactical.patch.deployment-terrain.prefix");
                if (!Enabled()) return;

                try
                {
                    if ((object)__instance == null) return;
                    if (GameVars.playeralliance == foralliance && !GameVars.ai_vs_ai) return;
                    if (GameVars.tutorialactive && !Tutorial.engaged) return;

                    BattleUnits.Grp[] groups = ReadGroups(__instance);
                    if (groups.Length == 0) return;
                    __state = DeploymentPlanRuntime.Begin(__instance, groups);
                }
                catch (Exception ex)
                {
                    __state = null;
                    OnceLog.Warning(
                        "tactical-deployment-terrain:prefix-failed",
                        "TacticalDeploymentTerrainDisciplinePatch prefix failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(BattleUnits __instance, int foralliance, DeploymentPass __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.deployment-terrain.postfix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                PatchFirstFire.Mark("tactical.patch.deployment-terrain.postfix");
                if (!Enabled() || __state == null) return;

                try
                {
                    if ((object)__instance == null) return;
                    if (GameVars.playeralliance == foralliance && !GameVars.ai_vs_ai) return;
                    if (GameVars.tutorialactive && !Tutorial.engaged) return;

                    BattleUnits.Grp[] groups = ReadGroups(__instance);
                    if (groups.Length == 0 || groups.Length != __state.Groups.Length) return;
                    EnsureSideSnapshot(__instance, foralliance);

                    if (!TryReadInitialDeployment(__instance, out bool initialDeployment))
                        return;

                    string phase = ReadDeploymentPhase(__instance);
                    ResetAdviceLogForBattle(__instance, phase);

                    OnceLog.Info(
                        "tactical-deployment-terrain-advice",
                        "TacticalDeploymentTerrainDisciplinePatch advice surface wired.");

                    // The groups vanilla placed in this call that the doctrine may re-plan.
                    bool[] candidate = new bool[groups.Length];
                    for (int i = 0; i < groups.Length; i++)
                    {
                        BattleUnits.Grp group = groups[i];
                        if (group == null || (object)group.regref == null) continue;

                        Regiment regiment = group.regref;
                        if (!EligibleForVanillaDeploymentPlacement(
                                __instance,
                                group,
                                regiment,
                                foralliance,
                                initialDeployment))
                        {
                            continue;
                        }

                        Vector3 now = regiment.transform.position;
                        Vector3 before = __state.Before[i];
                        float dx = now.x - before.x, dz = now.z - before.z;
                        candidate[i] = dx * dx + dz * dz > MovedMeters * MovedMeters;
                    }

                    if (DeploymentDoctrineEnabled())
                        DeploymentPlanRuntime.Run(__instance, foralliance, __state, candidate, initialDeployment);

                    for (int i = 0; i < groups.Length; i++)
                    {
                        if (candidate[i]) TryDisciplineGroup(__instance, groups[i], groups[i].regref, phase);
                    }
                }
                catch (Exception ex)
                {
                    OnceLog.Warning(
                        "tactical-deployment-terrain:failed",
                        "TacticalDeploymentTerrainDisciplinePatch failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        // Battle core D1 (amended in BC2b): deployment runs before the side's first
        // CheckGlobalAIStrategy tick (day 1 at battle-init frame 30, 23993-23994; later days
        // when the deployment panel closes, 164879-164880), so the side's battle snapshot is
        // built here for the visible-enemy bearing read below. A per-frame cache, not
        // doctrine state; any AIBattle resolves the side's own instance.
        private static void EnsureSideSnapshot(BattleUnits battleUnits, int alliance)
        {
            AIBattle[] battles = battleUnits.aibattle;
            if (battles == null) return;
            for (int i = 0; i < battles.Length; i++)
            {
                if ((UnityEngine.Object)battles[i] == null) continue;
                AIBattle sideBattle = WhiskeyRealism.Tactical.Orchestrator.ArmyEvidenceBuilder.ResolveAiBattleForAlliance(battles[i], alliance);
                WhiskeyRealism.Tactical.Battle.BattleCoreTick.EnsureSnapshot(sideBattle, alliance);
                return;
            }
        }

        private static bool DeploymentDoctrineEnabled()
        {
            return Plugin.EnableDeploymentAndMarchDoctrine != null && Plugin.EnableDeploymentAndMarchDoctrine.Value;
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                   Plugin.Instance.Enabled != null &&
                   Plugin.Instance.Enabled.Value &&
                   Plugin.EnableTacticalDeploymentTerrainDiscipline != null &&
                   Plugin.EnableTacticalDeploymentTerrainDiscipline.Value;
        }

        private static bool EligibleForVanillaDeploymentPlacement(
            BattleUnits battleUnits,
            BattleUnits.Grp group,
            Regiment regiment,
            int alliance,
            bool initialDeployment)
        {
            if ((object)battleUnits == null || group == null) return false;
            if (regiment == null) return false;
            if (group.alliance != alliance) return false;
            if (regiment.alliance != alliance) return false;
            if (regiment.unittyp <= 13) return false;
            if (regiment.isrouted) return false;

            try
            {
                if (regiment.gameObject == null || !regiment.gameObject.activeInHierarchy)
                    return false;
                if ((object)group.go == null)
                    return false;

                bool vanillaOwned =
                    group.isbattledataunit ||
                    (UnityEngine.Object)regiment.groupaiobject == (UnityEngine.Object)regiment.gameObject;
                if (!vanillaOwned)
                    return false;
                if (regiment.groupposition == null)
                    return false;
                if (!TryIsWlCurrentCommandExcluded(regiment, out bool excluded) || excluded)
                    return false;
                if (group.IsApplicableForDeployment(battleUnits, initialDeployment))
                    return false;

                if (!BattleWriterPolicy.DeploymentCorrectionAllowed(
                        regiment.unittyp,
                        FrontlineVectorCount(battleUnits),
                        WouldTakeFortifiedPath(battleUnits, regiment, alliance)))
                    return false;

                return TacticalWriteGuard.CanWrite(regiment);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:eligibility",
                    "TacticalDeploymentTerrainDisciplinePatch eligibility failed: " + ex.GetType().Name);
                return false;
            }
        }

        private static int FrontlineVectorCount(BattleUnits battleUnits)
        {
            Frontline2 zone = battleUnits.frontline2;
            return (object)zone != null && zone.frontlinevector != null ? zone.frontlinevector.Count : 0;
        }

        // Mirrors vanilla's num3 == 1 selection (85684-85715).
        private static bool WouldTakeFortifiedPath(BattleUnits battleUnits, Regiment regiment, int alliance)
        {
            float minimumMorale = GamePrefs.minimummoraleforaistances != null && GamePrefs.minimummoraleforaistances.Length > 1
                ? GamePrefs.minimummoraleforaistances[1]
                : 0f;
            return BattleWriterPolicy.WouldTakeFortifiedDeploymentPath(
                battleUnits.GetBattleType(alliance, usereadinesswhendraw: true),
                regiment.groupmorale,
                minimumMorale,
                EligibleDeploymentSubUnits(regiment));
        }

        // Vanilla list10 (85704-85712): non-group, active, attached, same AI group.
        private static int EligibleDeploymentSubUnits(Regiment regiment)
        {
            Regiment[] attached = regiment.allattachedunits;
            if (attached == null) return 0;
            int count = 0;
            for (int i = 0; i < attached.Length; i++)
            {
                Regiment unit = attached[i];
                if ((UnityEngine.Object)unit == null) continue;
                if (unit.unittyp > 13 || !unit.gameObject.activeInHierarchy || unit.permanentlydetached) continue;
                if ((UnityEngine.Object)unit.groupaiobject != (UnityEngine.Object)regiment.groupaiobject) continue;
                count++;
            }

            return count;
        }

        private static bool TryReadInitialDeployment(BattleUnits battleUnits, out bool initialDeployment)
        {
            initialDeployment = false;
            if ((object)battleUnits == null)
                return false;
            if (BattlePassedDaysField == null)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:missing-battlepasseddays",
                    "Missing BattleUnits.battlepasseddays; tactical deployment terrain discipline disabled.");
                return false;
            }

            try
            {
                object value = BattlePassedDaysField.GetValue(battleUnits);
                if (value is int days)
                {
                    initialDeployment = days <= 0;
                    return true;
                }
                if (value is float daysFloat)
                {
                    initialDeployment = daysFloat <= 0f;
                    return true;
                }

                OnceLog.Warning(
                    "tactical-deployment-terrain:battlepasseddays-type",
                    "Unsupported BattleUnits.battlepasseddays type; tactical deployment terrain discipline disabled.");
                return false;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:battlepasseddays",
                    "Failed reading BattleUnits.battlepasseddays: " + ex.GetType().Name);
                return false;
            }
        }

        private static bool TryIsWlCurrentCommandExcluded(Regiment regiment, out bool excluded)
        {
            excluded = false;

            try
            {
                if (!DLC_WL.dlc_scenarioactive)
                    return true;
                if (DLC_WL.dlc_chosencommander < 0)
                    return true;
                if (GameVars.commander == null || DLC_WL.dlc_chosencommander >= GameVars.commander.Count)
                    return false;

                Regiment currentCommand = GameVars.commander[DLC_WL.dlc_chosencommander].currentcommand;
                if ((UnityEngine.Object)currentCommand == null)
                    return true;
                if ((UnityEngine.Object)currentCommand.groupaiobjectreg == null)
                    return true;

                excluded =
                    (UnityEngine.Object)currentCommand.groupaiobjectreg == (UnityEngine.Object)currentCommand &&
                    (UnityEngine.Object)currentCommand == (UnityEngine.Object)regiment;
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:wl-current-command",
                    "Failed reading W&L current-command deployment exclusion: " + ex.GetType().Name);
                excluded = false;
                return false;
            }
        }

        private static void TryDisciplineGroup(BattleUnits battleUnits, BattleUnits.Grp group, Regiment regiment, string phase)
        {
            try
            {
                Vector3 original = regiment.transform.position;
                float originalFacing = regiment.transform.eulerAngles.y;
                Frontline2 deploymentZone = battleUnits.frontline2;

                TacticalTerrainRuntimeSample center =
                    TacticalTerrainProbe.SampleCenter(regiment, deploymentZone, original);
                IReadOnlyList<TacticalTerrainRuntimeSample> footprint =
                    TacticalTerrainProbe.SampleFootprint(regiment, deploymentZone);
                bool footprintWater = footprint.Any(sample => sample.Water);
                bool footprintOutOfZone = footprint.Any(sample => !sample.InDeploymentZone);
                TacticalEnemyBearingEvidence enemy =
                    TacticalTerrainProbe.GetAssumedEnemyBearing(regiment, original);

                bool terrainFailure = center.Water || footprintWater || !center.InDeploymentZone || footprintOutOfZone;

                if (!terrainFailure)
                    return;

                float preferredFacingDelta = ReadPreferredFacingDeltaDegrees();
                var rules = new TacticalTerrainRules(
                    ReadTerrainMaxCorrectionMeters(),
                    preferredFacingDelta,
                    requireDeploymentZone: true,
                    requireVisibleEnemyForFacing: false);

                TacticalTerrainDecision decision = TacticalTerrainFacingDiscipline.Choose(
                    new TacticalPoint2(original.x, original.z),
                    originalFacing,
                    BuildCandidates(regiment, deploymentZone, original, originalFacing, enemy),
                    enemy,
                    rules);

                EmitEvidence(group, regiment, phase, center, footprintWater, enemy, decision);
                if (!decision.Accepted)
                    return;

                ApplyCorrection(battleUnits, group, regiment, original, decision);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:group",
                    "TacticalDeploymentTerrainDisciplinePatch group correction failed: " + ex.GetType().Name);
            }
        }

        private static void ApplyCorrection(
            BattleUnits battleUnits,
            BattleUnits.Grp group,
            Regiment regiment,
            Vector3 original,
            TacticalTerrainDecision decision)
        {
            try
            {
                if ((object)battleUnits == null || group == null || (object)group.go == null || regiment == null)
                    return;

                float facing = decision.Candidate.FacingDegrees;
                float depth = regiment.groupposition != null ? regiment.groupposition.groupdepth : regiment.depth;
                BattleWriterPolicy.DeploymentLineFrontFromHq(
                    decision.Candidate.Point.X,
                    decision.Candidate.Point.Z,
                    facing,
                    depth,
                    GamePrefs.groupunitdistancetoback,
                    out float frontX,
                    out float frontZ);
                Vector3 front = TacticalTerrainProbe.WithTerrainHeight(new Vector3(frontX, original.y, frontZ));

                TacticalTerrainRuntimeSample frontSample =
                    TacticalTerrainProbe.SampleCenter(regiment, battleUnits.frontline2, front);
                if (frontSample.Water || !frontSample.InDeploymentZone)
                    return;

                DeploymentPlanRuntime.Place(battleUnits, regiment, frontX, frontZ, facing, IntentSource.Deployment);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:correction",
                    "TacticalDeploymentTerrainDisciplinePatch correction failed: " + ex.GetType().Name);
            }
        }

        private static IEnumerable<TacticalTerrainCandidate> BuildCandidates(
            Regiment regiment,
            Frontline2 deploymentZone,
            Vector3 original,
            float originalFacing,
            TacticalEnemyBearingEvidence enemy)
        {
            int max = Math.Max(1, Plugin.TacticalDeploymentTerrainMaxCandidates != null
                ? Plugin.TacticalDeploymentTerrainMaxCandidates.Value
                : DefaultMaxCandidates);
            float maxCorrection = ReadTerrainMaxCorrectionMeters();
            float[] radii = { 0f, 8f, 16f, 32f, 48f, 60f };
            int produced = 0;

            for (int r = 0; r < radii.Length && produced < max; r++)
            {
                float radius = Math.Min(radii[r], maxCorrection);
                for (int angle = 0; angle < 360 && produced < max; angle += 45)
                {
                    Vector3 point = radius <= 0f
                        ? original
                        : PointAt(original, angle, radius);

                    if (TacticalTerrainProbe.TryBuildCandidate(
                            regiment,
                            deploymentZone,
                            point,
                            originalFacing,
                            enemy,
                            out TacticalTerrainCandidate candidate))
                    {
                        yield return candidate;
                    }

                    produced++;
                    if (radius <= 0f) break;
                }
            }
        }

        private static void EmitEvidence(
            BattleUnits.Grp group,
            Regiment regiment,
            string phase,
            TacticalTerrainRuntimeSample center,
            bool footprintWater,
            TacticalEnemyBearingEvidence enemy,
            TacticalTerrainDecision decision)
        {
            try
            {
                if (EmittedAdvice.Count >= MaxAdviceRowsPerBattle) return;

                string unit = SafeUnitName(group, regiment);
                string key =
                    (regiment != null ? regiment.GetInstanceID().ToString(CultureInfo.InvariantCulture) : unit) +
                    "|" + center.TerrainId +
                    "|" + center.Water +
                    "|" + footprintWater +
                    "|" + center.InDeploymentZone +
                    "|" + decision.Signature;

                if (!EmittedAdvice.Add(key)) return;

                string line = TacticalTerrainFacingTelemetry.Format(new TacticalTerrainFacingLogRow(
                    "DoPlacementAIUnitsWithinDeploymentzoneNew",
                    phase,
                    regiment != null ? regiment.alliance : -1,
                    unit,
                    center.TerrainId,
                    center.Water,
                    footprintWater,
                    center.InDeploymentZone,
                    regiment != null ? regiment.transform.eulerAngles.y : 0f,
                    enemy.Visible ? enemy.BearingDegrees : 0f,
                    enemy.Visible ? enemy.DistanceMeters : 0f,
                    decision));

                LogDeploymentTelemetry(line.Replace("[TacDeployTerrain]", "[TacDeployTerrainAdvice]"));
            }
            catch
            {
            }
        }

        private static void LogDeploymentTelemetry(string line)
        {
            TelemetryRouter.LegacyInfoToMainLogIfAllowed(line, TelemetryLayer.Tactical);
        }

        private static void ResetAdviceLogForBattle(BattleUnits battleUnits, string phase)
        {
            string key = BuildAdviceBattleKey(battleUnits, phase);
            if (string.Equals(_emittedAdviceBattleKey, key, StringComparison.Ordinal))
                return;

            _emittedAdviceBattleKey = key;
            EmittedAdvice.Clear();
        }

        private static string BuildAdviceBattleKey(BattleUnits battleUnits, string phase)
        {
            try
            {
                int instanceId = (UnityEngine.Object)battleUnits != null
                    ? ((UnityEngine.Object)battleUnits).GetInstanceID()
                    : 0;
                int days = ReadIntField(battleUnits, BattlePassedDaysField, "battlepasseddays");
                int eod = ReadIntField(battleUnits, EodCycleField, "eodcycle");

                return instanceId.ToString(CultureInfo.InvariantCulture) +
                    ":" + days.ToString(CultureInfo.InvariantCulture) +
                    ":" + eod.ToString(CultureInfo.InvariantCulture) +
                    ":" + phase;
            }
            catch
            {
                return string.IsNullOrWhiteSpace(phase) ? "unknown" : phase;
            }
        }

        private static string ReadDeploymentPhase(BattleUnits battleUnits)
        {
            int eod = ReadIntField(battleUnits, EodCycleField, "eodcycle");
            int days = ReadIntField(battleUnits, BattlePassedDaysField, "battlepasseddays");
            return TacticalDeploymentTelemetry.PhaseFromPrefix(false, false, eod, days);
        }

        private static int ReadIntField(BattleUnits battleUnits, FieldInfo field, string fieldName)
        {
            if ((object)battleUnits == null || field == null) return 0;

            try
            {
                object value = field.GetValue(battleUnits);
                if (value is int intValue) return intValue;
                if (value is float floatValue) return (int)Math.Floor(floatValue);
                return 0;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:read-field",
                    "Failed reading BattleUnits." + fieldName + ": " + ex.GetType().Name);
                return 0;
            }
        }

        private static BattleUnits.Grp[] ReadGroups(BattleUnits battleUnits)
        {
            if ((object)battleUnits == null) return Array.Empty<BattleUnits.Grp>();
            if (GrpField == null)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain-advice:missing-grp",
                    "Missing BattleUnits.grp; tactical deployment terrain advice disabled.");
                return Array.Empty<BattleUnits.Grp>();
            }

            try
            {
                return GrpField.GetValue(battleUnits) as BattleUnits.Grp[] ?? Array.Empty<BattleUnits.Grp>();
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain-advice:grp",
                    "Failed reading BattleUnits.grp for deployment terrain advice: " + ex.GetType().Name);
                return Array.Empty<BattleUnits.Grp>();
            }
        }

        private static float ReadPreferredFacingDeltaDegrees()
        {
            float value = Plugin.TacticalDeploymentFacingPreferredDeltaDegrees != null
                ? Plugin.TacticalDeploymentFacingPreferredDeltaDegrees.Value
                : DefaultPreferredFacingDeltaDegrees;
            return IsPositiveFinite(value) ? value : DefaultPreferredFacingDeltaDegrees;
        }

        private static float ReadTerrainMaxCorrectionMeters()
        {
            float value = Plugin.TacticalDeploymentTerrainMaxCorrectionMeters != null
                ? Plugin.TacticalDeploymentTerrainMaxCorrectionMeters.Value
                : DefaultMaxCorrectionMeters;
            return IsPositiveFinite(value) ? value : DefaultMaxCorrectionMeters;
        }

        private static Vector3 PointAt(Vector3 origin, float angleDegrees, float distance)
        {
            float radians = angleDegrees * (float)Math.PI / 180f;
            return TacticalTerrainProbe.WithTerrainHeight(new Vector3(
                origin.x + (float)Math.Sin(radians) * distance,
                origin.y,
                origin.z + (float)Math.Cos(radians) * distance));
        }

        private static bool IsPositiveFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }

        private static string SafeUnitName(BattleUnits.Grp group, Regiment regiment)
        {
            string value = group != null && !string.IsNullOrWhiteSpace(group.name)
                ? group.name
                : regiment != null ? regiment.name : "-";
            return TacticalCurrentOrderSignature.Safe(value);
        }
    }
}
