// Battle perception probe (BC2a, plan rule R1; runtime, main thread only).
//
// Runs once per doctrine tick per AI-run side, right after BattleSnapshotBuilder.Build:
// decides the perception mode, probes the snapshot's enemy records and every enemy
// reinforcement column, folds the probe into the side's memory (BattlePerception.Update),
// publishes it into the snapshot, builds slice pressure and refits the front axis on what
// is seen (BattleSnapshotBuilder.RefitFrontAxis).
//
// Vanilla anchors (build 14703111):
// - Regiment.fow is the unit's own side grid: fows[1] for every non-player alliance,
//   fows[0] for the player alliance (CreateFOWObjects 116505-116518).
// - FogGrid visibility uses vanilla's own call with vanilla's own arguments (127225):
//   fow.UnitIsHidden(pos, commander cunning, strength / unittypesize[unittyp], liedown,
//   coverobject, covervalue, unittyp, regiment) (UnitIsHidden 101179-101229, which
//   indexes coverconcealment[covertype] and spottingfactor[unittype]). Vanilla does not
//   bounds-check commander or unittyp; here an out-of-range index passes -1, the
//   method's own "unused" default. Melee always reveals (127216, 127231).
// - OwnSideWithinFog (player-alliance AI side): !enemy.iswithinfog, vanilla's own
//   fows[0] verdict for non-player units (127225).
// - RangeOnly: the union of own units' unitrange.enemyinrangereg / enemyinfirerangereg.
//   For an AI side those lists are range-only (122650-122653, 122699-122702) - today's
//   behaviour. The lists skip routed enemies (122644), so a routed enemy is seen when an
//   own unit's list reach covers it (BattlePerception.RangeOnlyReach) and then leaves
//   memory like one seen routing on the fog grid.
// - FogOfWar.firstrunfinished is private (100648) and only ever turns true, after the
//   first full grid pass (100690-100694), until the next InitFOW (100791); it is read by
//   cached reflection until it is true, then latched per side for the battle
//   (PaintedGridLatch: W&L / ai_vs_ai sides use different grids).
//   GameVars.hqinthesaddle blanks the AI grid in battle (101312). UseIntelligenceMap
//   (100594) is logged once per battle (open question in
//   docs/vanilla-vision-and-movement.md).
// - Enemy candidates mirror the vanilla pressure filter: active, unittyp <= 13, not 5
//   (122644). Routed enemies are reported (BattlePerception drops one seen routing).
// - Reinforcements: BattleUnits.scheduledarrival (78960; ScheduledArrival 78610:
//   regimentrefreg, entrypoint, durationleft in hours, cf. hotspots 7975-8004 and
//   CheckIfUnitIsReinforcement 87508). A column counts only when its entry point
//   (EntryPoint.entrypointposition) passes the same probe; in RangeOnly when an own unit's
//   list reach covers it. Strength is regimentrefreg.groupstrength, which vanilla
//   refreshes for arrivals at 84830.
// - Pressure geometry: GamePrefs.aidefensivemaxrange (battleprefs, parsed 54898) and
//   flankpositionshift (unitprefs, 53300), read once per battle; the slice count is the
//   snapshot's (aidefensiveslices, set by the builder).
//
// - Perceived outflanking (BC10): after the pressure build, PerceivedFlanking.Apply fills
//   UnitRecord.Outflanked from the regiments the side sees (UpdateFlanking 122959-123175 with
//   its sectors and range from GamePrefs.fireangles / distancetoenemyforoutflank, read once
//   per battle); the snapshot builder no longer copies vanilla's unfogged value.
//
// - Troop quality (BC8): each enemy row carries its record's Experience() and morale; only a
//   visible row's star experience and morale reach memory (BattlePerception.Update; the
//   player sees a visible regiment's stars, UpdateUnitIcon 121832-121852). Reinforcement
//   columns carry none.
//
// Ids are GameObject.GetInstanceID(), the id UnitRecord.InstanceId carries.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    internal static class BattlePerceptionProbe
    {
        private const int InitialCapacity = 128;

        private sealed class SideState
        {
            internal EnemyObservation[] Memory = new EnemyObservation[InitialCapacity];
            internal int MemoryCount;
            internal PerceptionProbeResult Result = PerceptionProbeResult.Create(InitialCapacity);
            internal bool HasMode;
            internal PerceptionMode LastMode;
            internal PerceptionFallbackReason LastReason;
        }

        // Alliances 0 and 1 only; Europe (2) never runs a battle side.
        private static readonly SideState[] _sides = { new SideState(), new SideState() };
        private static readonly HashSet<int> _rangeVisible = new HashSet<int>();
        // RangeOnly: own unit positions and list reach, refreshed with _rangeVisible.
        private static float[] _ownX = new float[InitialCapacity];
        private static float[] _ownZ = new float[InitialCapacity];
        private static float[] _ownReach = new float[InitialCapacity];
        private static int _ownReachCount;
        private static double _battleStartHours = double.NaN;
        private static bool _geometryRead;
        private static SliceGeometry _geometry;
        private static FlankGeometry _flank;
        private static readonly PaintedGridLatch _paintedGrids = new PaintedGridLatch(2);

        private static FieldInfo _firstRunField;
        private static bool _firstRunResolved;
        private static FieldInfo _bunitsField;
        private static bool _bunitsResolved;

        static BattlePerceptionProbe()
        {
            BattleScope.Register("battle-perception", Reset);
        }

        private static void Reset()
        {
            for (int i = 0; i < _sides.Length; i++)
            {
                _sides[i].MemoryCount = 0;
                _sides[i].Result.Count = 0;
                _sides[i].HasMode = false;
            }
            _rangeVisible.Clear();
            _ownReachCount = 0;
            _battleStartHours = double.NaN;
            _geometryRead = false;
            _paintedGrids.Reset();
        }

        /// <summary>
        /// Perceives the snapshot's side into it, once per build: mode, observations, enemy
        /// and reinforcement summaries, per-unit slice / flank / closest-visible pressure,
        /// then the front-axis refit. Never throws.
        /// </summary>
        internal static void Probe(BattleSnapshot s, AIBattle battle)
        {
            if (s == null || s.Alliance < 0 || s.Alliance >= _sides.Length) return;
            using (TelemetryPerf.Scope("tactical.perception", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    ProbeSide(s, battle);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical.battle-perception:probe-failed",
                        "[BattlePerception] probe threw; the side has no perception this tick: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        private static void ProbeSide(BattleSnapshot s, AIBattle battle)
        {
            int alliance = s.Alliance;
            SideState side = _sides[alliance];
            double nowHours = s.DoctrineHours;
            if (double.IsNaN(_battleStartHours)) _battleStartHours = nowHours;
            ReadGeometryOnce();

            FogOfWar grid = SideGrid(s);
            LogIntelligenceMapOnce(grid);

            bool configOn = Plugin.EnableBattleFogHonestPerception == null || Plugin.EnableBattleFogHonestPerception.Value;
            PerceptionMode mode = BattlePerception.DecideMode(configOn, GameVars.usefow, GameVars.hqinthesaddle,
                FirstRunFinished(grid, alliance), (nowHours - _battleStartHours) * 3600.0,
                alliance == GameVars.playeralliance, out PerceptionFallbackReason reason);

            if (!FillProbe(side, s, battle, mode, grid))
            {
                mode = PerceptionMode.RangeOnly;
                reason = PerceptionFallbackReason.GridNotPainted;
                FillProbe(side, s, battle, mode, grid);
            }

            int needed = side.MemoryCount + side.Result.Count;
            if (side.Memory.Length < needed) Array.Resize(ref side.Memory, Math.Max(needed, side.Memory.Length * 2));
            BattlePerception.Update(side.Memory, ref side.MemoryCount, in side.Result, nowHours);

            BattlePerception.Publish(s, mode, reason, side.Memory, side.MemoryCount, in side.Result);
            SlicePressureBuilder.Build(s, in _geometry);
            using (TelemetryPerf.Scope("tactical.perceived-flanking", TelemetryLayer.Tactical, TelemetryCategory.Performance, 0.5))
                PerceivedFlanking.Apply(s, in _flank);
            BattleSnapshotBuilder.RefitFrontAxis(s);
            ReportModeChange(side, alliance, mode, reason);
        }

        // Returns false when the fog-grid probe threw, so the caller can redo the tick range-only.
        private static bool FillProbe(SideState side, BattleSnapshot s, AIBattle battle, PerceptionMode mode, FogOfWar grid)
        {
            side.Result.Count = 0;
            if (mode == PerceptionMode.RangeOnly) CollectRangeVisible(s);
            try
            {
                MaskPlayerIntelIncidents(mode);
                UnitRecord[] enemies = s.EnemyRecords;
                for (int i = 0; i < s.EnemyRecordCount; i++)
                {
                    if ((enemies[i].Flags & UnitFlags.Active) == 0) continue;
                    if (enemies[i].UnitTyp > 13 || enemies[i].UnitTyp == 5) continue;
                    AddRow(ref side.Result, enemies[i].InstanceId, IsEnemyVisible(mode, grid, s, i), enemies[i].X, enemies[i].Z,
                        enemies[i].Strength, enemies[i].UnitTyp, (enemies[i].Flags & UnitFlags.Routed) != 0,
                        reinforcement: false, arrivalHours: 0f, experience: enemies[i].Experience, morale: enemies[i].Morale,
                        heading: enemies[i].RotationY, fireRange: enemies[i].FireRange, coverValue: enemies[i].CoverValue,
                        coverObject: enemies[i].CoverObject, standing: enemies[i].RegimentPaths <= 0);
                }
                AddReinforcements(side, battle, s.Alliance, mode, grid);
                return true;
            }
            catch (Exception ex)
            {
                if (mode == PerceptionMode.RangeOnly) throw;
                OnceLog.Warning("tactical.battle-perception:fog-probe-failed",
                    "[BattlePerception] fog-grid probe threw, side falls back to range-only: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
            finally
            {
                RestoreIncidentMask();
            }
        }

        private static void RestoreIncidentMask()
        {
            if (!_incidentMask.Active) return;
            string failure = _incidentMask.Restore(incident => incident == null);
            if (failure != null)
                OnceLog.Warning("tactical.battle-perception:incident-restore",
                    "[BattlePerception] W&L incident restore failed: " + failure);
        }

        /// <summary>
        /// BC13: the incident mask around a block of UnitIsHidden calls the ground probe makes (the opportunity producer's
        /// perceived-ground oracle), BUG-TAC-020's mask with the same restore. Applied in FogGrid mode only; dispose restores.
        /// </summary>
        internal static IncidentMaskScope WithIntelIncidentMask(PerceptionMode mode)
        {
            MaskPlayerIntelIncidents(mode);
            return default;
        }

        internal readonly struct IncidentMaskScope : IDisposable
        {
            public void Dispose() => RestoreIncidentMask();
        }

        /// <summary>The side's own fog grid (every unit of a side shares it), or null.</summary>
        internal static FogOfWar GridOf(BattleSnapshot s) => SideGrid(s);

        /// <summary>
        /// Hands the RangeOnly reach arrays the last probe filled (own units' positions and list reach) to the ground
        /// oracle. They describe this tick's roster only after a RangeOnly probe, which is the only mode that asks.
        /// </summary>
        internal static void FillRangeOnlyGround(RangeOnlyPerceivedGround ground)
        {
            ground.SetReach(_ownX, _ownZ, _ownReach, _ownReachCount);
        }

        // FogOfWar.UnitIsHidden returns false for every position while the W&L player's
        // intelligence incident 60 (Lost Orders), 61 or 64 (Scouting Ahead) is active
        // (101183-101186): the incident reveals the enemy to the player. Vanilla asks the AI
        // side's grid (oppositefow) only about non-player units; a player-alliance unit is
        // tested only on the player's own grid, and only under W&L HQ-in-the-saddle
        // (Regiment fog update 127211-127224). So the shortcut was never meant for an AI side. For the FogGrid
        // probe (an AI side's own grid) the incidents are marked not launched for the probe's
        // UnitIsHidden calls and restored in FillProbe's finally (BUG-TAC-020).
        private static readonly WhiskeyRealism.Patches.FieldMask<DLC_WL.Incident, float> _incidentMask =
            new WhiskeyRealism.Patches.FieldMask<DLC_WL.Incident, float>(i => i.launchedtime, (i, v) => i.launchedtime = v,
                BattlePerception.IncidentNotLaunched);
        private static readonly DLC_WL.Incident[] _intelIncidents = new DLC_WL.Incident[BattlePerception.PlayerIntelIncidents.Length];

        private static void MaskPlayerIntelIncidents(PerceptionMode mode)
        {
            if (!BattlePerception.MasksPlayerIntelIncidents(mode, DLC_WL.dlc_scenarioactive)) return;
            List<DLC_WL.Incident> incidents = DLC_WL.incidents;
            if (incidents == null) return;
            int[] ids = BattlePerception.PlayerIntelIncidents;
            for (int i = 0; i < ids.Length; i++)
                _intelIncidents[i] = ids[i] >= 0 && ids[i] < incidents.Count ? incidents[ids[i]] : null;
            int masked = _incidentMask.Mask(_intelIncidents, incident => BattlePerception.IncidentLaunched(incident.launchedtime));
            Array.Clear(_intelIncidents, 0, _intelIncidents.Length);
            if (masked > 0)
                OnceLog.Info("tactical.battle-perception:incident-mask",
                    "[BattlePerception] W&L player intelligence incident (60/61/64) ignored by the AI side's fog-grid probe");
        }

        private static bool IsEnemyVisible(PerceptionMode mode, FogOfWar grid, BattleSnapshot s, int enemyIndex)
        {
            ref UnitRecord record = ref s.EnemyRecords[enemyIndex];
            if ((record.Flags & UnitFlags.InMelee) != 0) return true;
            switch (mode)
            {
                case PerceptionMode.FogGrid:
                {
                    Regiment enemy = BattleSnapshotBuilder.UnitAt(s, enemyIndex, enemy: true);
                    if ((UnityEngine.Object)enemy == null || (UnityEngine.Object)grid == null) return false;
                    return !grid.UnitIsHidden(new Vector3(record.X, record.Y, record.Z), CommanderCunning(record.Commander),
                        RelativeStrength(record.Strength, record.UnitTyp), (record.Flags & UnitFlags.LiedDown) != 0,
                        CoverType(enemy.coverobject), enemy.covervalue, SpottingType(record.UnitTyp), enemy);
                }
                case PerceptionMode.OwnSideWithinFog:
                {
                    Regiment enemy = BattleSnapshotBuilder.UnitAt(s, enemyIndex, enemy: true);
                    return (UnityEngine.Object)enemy != null && !enemy.iswithinfog;
                }
                default:
                    if (_rangeVisible.Contains(record.InstanceId)) return true;
                    return (record.Flags & UnitFlags.Routed) != 0
                        && BattlePerception.WithinAnyReach(_ownX, _ownZ, _ownReach, _ownReachCount, record.X, record.Z);
            }
        }

        private static void AddReinforcements(SideState side, AIBattle battle, int alliance, PerceptionMode mode, FogOfWar grid)
        {
            BattleUnits bunits = Bunits(battle);
            List<BattleUnits.ScheduledArrival> arrivals = (UnityEngine.Object)bunits != null ? bunits.scheduledarrival : null;
            if (arrivals == null) return;
            for (int i = 0; i < arrivals.Count; i++)
            {
                BattleUnits.ScheduledArrival arrival = arrivals[i];
                if (arrival == null) continue;
                Regiment column = arrival.regimentrefreg;
                EntryPoint entry = arrival.entrypoint;
                if ((UnityEngine.Object)column == null || (UnityEngine.Object)entry == null || column.alliance == alliance) continue;
                if (column.gameObject.activeInHierarchy) continue; // arrived: probed as a unit
                Vector3 pos = entry.entrypointposition;
                bool visible = mode == PerceptionMode.RangeOnly
                    ? BattlePerception.WithinAnyReach(_ownX, _ownZ, _ownReach, _ownReachCount, pos.x, pos.z)
                    : (UnityEngine.Object)grid != null && !grid.UnitIsHidden(pos);
                AddRow(ref side.Result, column.gameObject.GetInstanceID(), visible, pos.x, pos.z, column.groupstrength,
                    column.unittyp, routed: false, reinforcement: true, arrivalHours: arrival.durationleft, experience: 0f, morale: 0f);
            }
        }

        private static void AddRow(ref PerceptionProbeResult result, int id, bool visible, float x, float z, float strength,
            int unitTyp, bool routed, bool reinforcement, float arrivalHours, float experience, float morale,
            float heading = 0f, float fireRange = 0f, float coverValue = 0f, int coverObject = 0, bool standing = false)
        {
            int i = result.Count;
            PerceptionProbeResult.EnsureCapacity(ref result, i + 1);
            result.EnemyIds[i] = id;
            result.Visible[i] = visible;
            result.X[i] = x;
            result.Z[i] = z;
            result.Strength[i] = strength;
            result.UnitTyp[i] = unitTyp;
            result.Routed[i] = routed;
            result.IsReinforcement[i] = reinforcement;
            result.ArrivalHours[i] = arrivalHours;
            result.Experience[i] = experience; // true values; BattlePerception.Update keeps the star value of a visible row
            result.Morale[i] = morale;
            // BC10: the covered-crossings inputs of a unit row (a reinforcement column has none).
            result.Heading[i] = heading;
            result.FireRange[i] = fireRange;
            result.CoverValue[i] = coverValue;
            result.CoverObject[i] = coverObject;
            result.Standing[i] = standing;
            result.Count = i + 1;
        }

        // RangeOnly inputs: the ids in own units' range lists, and each live own unit's
        // position and list reach.
        private static void CollectRangeVisible(BattleSnapshot s)
        {
            _rangeVisible.Clear();
            _ownReachCount = 0;
            float[] sight = GamePrefs.maximumlineofsight;
            UnitRecord[] own = s.Own;
            for (int i = 0; i < s.OwnCount; i++)
            {
                Regiment unit = BattleSnapshotBuilder.UnitAt(s, i, enemy: false);
                if ((UnityEngine.Object)unit != null && unit.unitrange != null)
                {
                    AddIds(unit.unitrange.enemyinrangereg);
                    AddIds(unit.unitrange.enemyinfirerangereg);
                }
                if ((own[i].Flags & UnitFlags.Active) == 0 || (own[i].Flags & UnitFlags.Routed) != 0) continue;
                if (_ownReachCount == _ownX.Length)
                {
                    int size = _ownX.Length * 2;
                    Array.Resize(ref _ownX, size);
                    Array.Resize(ref _ownZ, size);
                    Array.Resize(ref _ownReach, size);
                }
                int t = own[i].UnitTyp;
                _ownX[_ownReachCount] = own[i].X;
                _ownZ[_ownReachCount] = own[i].Z;
                _ownReach[_ownReachCount] = BattlePerception.RangeOnlyReach(
                    sight != null && t >= 0 && t < sight.Length ? sight[t] : 0f,
                    GamePrefs.distancetoenemytocancelinterruption);
                _ownReachCount++;
            }
        }

        private static void AddIds(List<Regiment> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                Regiment enemy = list[i];
                if ((UnityEngine.Object)enemy != null) _rangeVisible.Add(enemy.gameObject.GetInstanceID());
            }
        }

        // The side's own grid, from the first live own unit (every unit of a side shares it).
        private static FogOfWar SideGrid(BattleSnapshot s)
        {
            UnitRecord[] own = s.Own;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if ((own[i].Flags & UnitFlags.Active) == 0) continue;
                Regiment unit = BattleSnapshotBuilder.UnitAt(s, i, enemy: false);
                if ((UnityEngine.Object)unit != null && (UnityEngine.Object)unit.fow != null) return unit.fow;
            }
            return null;
        }

        private static float CommanderCunning(int commander)
        {
            var commanders = GameVars.commander;
            if (commanders == null || commander < 0 || commander >= commanders.Count || commanders[commander] == null) return -1f;
            return commanders[commander].GetCommanderCunning();
        }

        private static float RelativeStrength(int strength, int unitTyp)
        {
            int[] sizes = GamePrefs.unittypesize;
            if (sizes == null || unitTyp < 0 || unitTyp >= sizes.Length || sizes[unitTyp] <= 0) return -1f;
            return (float)strength / (float)sizes[unitTyp];
        }

        private static int SpottingType(int unitTyp)
        {
            float[] spotting = GamePrefs.spottingfactor;
            return spotting != null && unitTyp >= 0 && unitTyp < spotting.Length ? unitTyp : -1;
        }

        private static int CoverType(int coverObject)
        {
            float[] cover = GamePrefs.coverconcealment;
            return cover != null && coverObject >= 0 && coverObject < cover.Length ? coverObject : -1;
        }

        private static void ReadGeometryOnce()
        {
            if (_geometryRead) return;
            _geometryRead = true;
            _geometry = new SliceGeometry
            {
                MaxRangeMeters = GamePrefs.aidefensivemaxrange,
                FlankShiftMeters = GamePrefs.flankpositionshift,
            };
            // UpdateFlanking's sectors and range (122993-123010, unitprefs "Fireangle front/back").
            float[] fire = GamePrefs.fireangles;
            _flank = fire != null && fire.Length >= 4
                ? new FlankGeometry
                {
                    FrontFrom = fire[0], FrontTo = fire[1], LeftFrom = fire[2], RightTo = fire[3],
                    RangeMeters = GamePrefs.distancetoenemyforoutflank,
                }
                : default(FlankGeometry); // RangeMeters 0: every code reads 0
        }

        // True once the side's grid has finished its first full pass. The reflected read
        // boxes a bool, so it runs only until the answer is true; then the side latches it.
        private static bool FirstRunFinished(FogOfWar grid, int alliance)
        {
            if ((UnityEngine.Object)grid == null) return false;
            if (_paintedGrids.IsPainted(alliance, grid)) return true;
            if (!_firstRunResolved)
            {
                _firstRunResolved = true;
                try
                {
                    _firstRunField = AccessTools.Field(typeof(FogOfWar), "firstrunfinished");
                }
                catch
                {
                    _firstRunField = null;
                }
                if (_firstRunField == null)
                    OnceLog.Warning("tactical.battle-perception:firstrun-field-missing",
                        "[BattlePerception] FogOfWar.firstrunfinished not found; fog-grid perception stays range-only");
            }
            if (_firstRunField == null) return false;
            try
            {
                if (!(bool)_firstRunField.GetValue(grid)) return false;
                _paintedGrids.MarkPainted(alliance, grid);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static BattleUnits Bunits(AIBattle battle)
        {
            if (!_bunitsResolved)
            {
                _bunitsResolved = true;
                try
                {
                    _bunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
                }
                catch
                {
                    _bunitsField = null;
                }
                if (_bunitsField == null)
                    OnceLog.Warning("tactical.battle-perception:bunits-field-missing",
                        "[BattlePerception] AIBattle.bunits not found; enemy reinforcements are not perceived");
            }
            if (_bunitsField == null) return null;
            try
            {
                return _bunitsField.GetValue(battle) as BattleUnits;
            }
            catch
            {
                return null;
            }
        }

        private static void LogIntelligenceMapOnce(FogOfWar grid)
        {
            if ((UnityEngine.Object)grid == null) return;
            // Literal messages: this runs every tick and must not build a string.
            if (grid.UseIntelligenceMap)
                OnceLog.Info("tactical.battle-perception:intelligence-map", "[BattlePerception] side grid UseIntelligenceMap=true");
            else
                OnceLog.Info("tactical.battle-perception:intelligence-map", "[BattlePerception] side grid UseIntelligenceMap=false");
        }

        private static void ReportModeChange(SideState side, int alliance, PerceptionMode mode, PerceptionFallbackReason reason)
        {
            if (side.HasMode && side.LastMode == mode && side.LastReason == reason) return;
            side.HasMode = true;
            side.LastMode = mode;
            side.LastReason = reason;

            int visible = 0, remembered = 0;
            for (int i = 0; i < side.MemoryCount; i++)
            {
                if (side.Memory[i].State == ObservationState.Visible) visible++;
                else remembered++;
            }
            string modeName = ModeName(mode);
            string reasonName = ReasonName(reason);
            string row = "[BattlePerception] alliance=" + alliance + " mode=" + modeName + " reason=" + reasonName
                + " visible=" + visible + " remembered=" + remembered;
            OnceLog.Info(BattlePerception.ModeLogKey(mode), row);
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "BattlePerception", TelemetrySeverity.Info,
                ev => ev
                    .WithSide(alliance)
                    .WithDecision("BattlePerception", modeName, row)
                    .WithField("mode", modeName)
                    .WithField("reason", reasonName)
                    .WithField("visible", visible)
                    .WithField("remembered", remembered));
        }

        private static string ModeName(PerceptionMode mode)
        {
            switch (mode)
            {
                case PerceptionMode.FogGrid: return "FogGrid";
                case PerceptionMode.OwnSideWithinFog: return "OwnSideWithinFog";
                default: return "RangeOnly";
            }
        }

        private static string ReasonName(PerceptionFallbackReason reason)
        {
            switch (reason)
            {
                case PerceptionFallbackReason.FogDisabled: return "FogDisabled";
                case PerceptionFallbackReason.HqInTheSaddle: return "HqInTheSaddle";
                case PerceptionFallbackReason.GridNotPainted: return "GridNotPainted";
                case PerceptionFallbackReason.ConfigOff: return "ConfigOff";
                default: return "None";
            }
        }
    }
}
