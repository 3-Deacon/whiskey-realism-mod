using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.Producers;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #80 SkirmisherScreenDistancePatch — Vanilla Regiment.GetUnitLinkData (private, 124208-124390) computes where a
    // linked unit belongs: for a skirmisher company linked to the front of its parent
    // regiment (linkedposition 0) that is unitlinkdistance (10, unitprefs 1069) +
    // skirmisherdistance (20, unitprefs 864) = 30 m ahead (124300-124306, 124359), and
    // UpdateUnitLink (124396-124500) re-paths the company there every
    // updatecyclelinkingbrigades cycle while no enemy is inside its fire range
    // (GetClosestEnemyUnit(firerange) == null, 124467). Moving the company would only
    // last until that re-link (SetWaypoint even clears a type 3/4 link, 91514-91516).
    //
    // This Postfix steers that input instead (BC9, plan decision 9): for an AI
    // skirmisher company of a screening line (or an attack-stance line on the move)
    // with nothing in its range list and no visible enemy near, the returned link
    // target is moved forward along the link rotation so the company stands
    // distancetoenemyforinterruption (100 m, unitprefs 989) ahead of its regiment, the
    // distance at which vanilla stops a moving line on contact. The record's distance
    // fields are recomputed from the new point exactly as GetUnitLinkData computes them
    // (124370-124386), so UpdateUnitLink's gates see a consistent record and a company
    // already standing there is not re-pathed. On contact the rule no longer applies and
    // vanilla's 30 m returns by itself. Result override of vanilla's own input: no
    // Prefix-skip, no write call, no persisted field (linkedoffset is saved with the
    // unit, 73773, so it is not used). Guarded by TacticalWriteGuard.CanWrite (W&L
    // player-owned companies untouched), Commander Mode Active and
    // Enable Battle Reconnaissance. The decision is BattleRecon.SkirmisherPushMeters
    // over the side's battle snapshot (fog-honest).
    [HarmonyPatch(typeof(Regiment), "GetUnitLinkData")]
    internal static class SkirmisherScreenDistancePatch
    {
        private const int FrontLink = 0;
        private const int RiverTerrain = 4;

        private static bool _resolved;
        private static FieldInfo _targetArea, _rotation, _distance, _sollDistance, _onWater, _distanceToWpTarget, _distanceLastWp;

        [HarmonyPostfix]
        internal static void Postfix(Regiment __instance, object __result)
        {
            using (TelemetryPerf.Scope("tactical.patch.skirmisher-screen", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.skirmisher-screen");
                try
                {
                    if (__result == null || (UnityEngine.Object)__instance == null) return;
                    if (__instance.unittyp != TacticalUnitType.Skirmisher || __instance.linkedposition != FrontLink) return;
                    if (Plugin.EnableBattleReconnaissance == null || !Plugin.EnableBattleReconnaissance.Value) return;
                    if (!TacticalWriteGuard.ModeAllowsWrites()) return;
                    if (!BattleCore.TryGetSnapshot(__instance.alliance, out BattleSnapshot snapshot)) return;
                    var go = __instance.gameObject;
                    int index = go != null ? snapshot.IndexOfOwn(go.GetInstanceID()) : -1;
                    if (index < 0) return;

                    float extra = BattleRecon.SkirmisherPushMeters(snapshot, index,
                        GamePrefs.unitlinkdistance + GamePrefs.skirmisherdistance,
                        GamePrefs.distancetoenemyforinterruption,
                        GamePrefs.distancetoenemytocancelinterruption);
                    if (extra <= 0f || !TacticalWriteGuard.CanWrite(__instance) || !Resolve()) return;
                    Push(__instance, __result, extra);
                    OnceLog.Info("tactical-battle-core:skirmisher-screen-first-fire",
                        "[BattleRecon] first skirmisher screen pushed out: an AI skirmisher company of a screening line stands at " +
                        "vanilla's contact distance (distancetoenemyforinterruption) ahead of its regiment instead of the 30 m link");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-battle-core:skirmisher-screen-failed",
                        "[BattleRecon] skirmisher screen push failed; vanilla's link kept: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        private static void Push(Regiment unit, object data, float extra)
        {
            Vector3 target = (Vector3)_targetArea.GetValue(data);
            float rotation = (float)_rotation.GetValue(data);
            // GetAngleTerrainPosStatic's step (26891-26905): forward of angle a is
            // (sin a, cos a); the height is the terrain's at the new point.
            double a = rotation * Math.PI / 180.0;
            var pushed = new Vector3(target.x + (float)Math.Sin(a) * extra, target.y, target.z + (float)Math.Cos(a) * extra);
            BattlefieldSetup bfs = PostureTargets.Bfs();
            if (bfs != null) pushed.y = bfs.GetTerrainHeight(pushed);

            // GetUnitLinkData's own distance fields, from the pushed point (124370-124386).
            Vector3 block = unit.blockobject != null && unit.blockobject.Length > 0 && unit.blockobject[0] != null
                ? unit.blockobject[0].transform.position
                : unit.GetPosition();
            Vector3 start = unit.lastsetwaypointposition == default(Vector3) || unit.regimentpaths == 0
                ? block
                : unit.lastsetwaypointposition;
            _targetArea.SetValue(data, pushed);
            _distance.SetValue(data, XZ(start, pushed));
            _distanceToWpTarget.SetValue(data, XZ(block, pushed));
            _distanceLastWp.SetValue(data, XZ(unit.GetLastTransmittedPathPos(ignoreorderdelay: true), pushed));
            _onWater.SetValue(data, BattlefieldSetup.GetCurrentTerrainOnPos(pushed) == RiverTerrain);
            _sollDistance.SetValue(data, (float)_sollDistance.GetValue(data) + extra);
        }

        private static bool Resolve()
        {
            if (_resolved) return _targetArea != null;
            _resolved = true;
            try
            {
                Type data = AccessTools.Inner(typeof(Regiment), "LinkedUnitData");
                if (data != null)
                {
                    _targetArea = AccessTools.Field(data, "targetarea");
                    _rotation = AccessTools.Field(data, "rotationtarget");
                    _distance = AccessTools.Field(data, "distance");
                    _sollDistance = AccessTools.Field(data, "solldistance");
                    _onWater = AccessTools.Field(data, "onwater");
                    _distanceToWpTarget = AccessTools.Field(data, "distancetowptarget");
                    _distanceLastWp = AccessTools.Field(data, "distancelastwp");
                }
                if (_targetArea == null || _rotation == null || _distance == null || _sollDistance == null ||
                    _onWater == null || _distanceToWpTarget == null || _distanceLastWp == null)
                {
                    _targetArea = null;
                    OnceLog.Warning("tactical-battle-core:skirmisher-screen-fields",
                        "[BattleRecon] Regiment.LinkedUnitData fields not found; skirmisher screen stays vanilla's");
                }
            }
            catch (Exception ex)
            {
                _targetArea = null;
                OnceLog.Warning("tactical-battle-core:skirmisher-screen-fields",
                    "[BattleRecon] Regiment.LinkedUnitData reflection failed; skirmisher screen stays vanilla's: " + ex.Message);
            }
            return _targetArea != null;
        }

        private static float XZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
