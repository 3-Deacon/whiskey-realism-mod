using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>What #60's Prefix saw before vanilla placed the side's groups.</summary>
    internal sealed class DeploymentPass
    {
        // BattleUnits.grp as it stood, and each group's regiment position before the call
        // (vanilla's list7, 85600-85604): the base of vanilla's own zone tests and spacing.
        public BattleUnits.Grp[] Groups;
        public Vector3[] Before;
        // GameObject instance id of the W&L current command's AI group object (0 = none). By the
        // Postfix its permanentlydetached is true again (85886), so the Prefix records the group.
        public int WlGroupInstance;
    }

    /// <summary>The counts of one pass, for the TacticalDeploymentPlan row.</summary>
    internal struct DeploymentSummary
    {
        public int Candidates, Replaced, ReserveGroups, ReserveChildren, ClampReverted, Issued, Refused;
        public AssumedEnemySource Source;
    }

    // The runtime half of fog-honest deployment (#60, BC10 decisions 7-9). Vanilla's
    // DoPlacementAIUnitsWithinDeploymentzoneNew (85524-85892) has already placed the side's groups
    // when this runs; the planning is DeploymentDoctrine's (pure), the writes are the order
    // executor's (an immediate-placement Move per re-placed group). Reads: BattleUnits.objectives,
    // frontline2 zone tests, enemy-owned entry points and the map size (public scenario data), the
    // side's battle snapshot (command parents, perception). Reads no enemy unit.
    //
    // Frames: every position the plan holds is a LINE FRONT, the targetpos SetGroupFormation takes
    // (92044-92058); vanilla's HQ stands depth + groupunitdistancetoback behind it. A placed group's
    // HQ is converted with BattleWriterPolicy.DeploymentLineFrontFromHq; objectives are fronts
    // already (vanilla passes the objective position as targetpos, 85875).
    internal static class DeploymentPlanRuntime
    {
        private const int MaxRowsPerBattle = 64;
        private static int _rows;
        private static bool _fogLogged, _reserveLogged;

        // Pooled across passes (two per battle on day 1, one per later day).
        private static DeploymentGroup[] _groups = new DeploymentGroup[64];
        private static Regiment[] _regiments = new Regiment[64];
        private static Vector3[] _before = new Vector3[64];
        private static float[] _clampX = new float[64], _clampZ = new float[64];
        private static readonly DeploymentPlan _plan = new DeploymentPlan();
        private static float[] _entryX = new float[16], _entryZ = new float[16];

        static DeploymentPlanRuntime()
        {
            BattleScope.Register("deployment-plan-runtime", Reset);
        }

        internal static DeploymentPass Begin(BattleUnits bunits, BattleUnits.Grp[] groups)
        {
            var pass = new DeploymentPass { Groups = groups, Before = new Vector3[groups.Length] };
            for (int i = 0; i < groups.Length; i++)
            {
                BattleUnits.Grp g = groups[i];
                pass.Before[i] = g != null && (UnityEngine.Object)g.regref != null ? g.regref.transform.position : default(Vector3);
            }
            pass.WlGroupInstance = ReadWlGroupInstance();
            return pass;
        }

        // The AI group object of the W&L current command (the command's own object when it is
        // its group, 85650): the group vanilla re-attached the command to for this call.
        internal static int ReadWlGroupInstance()
        {
            try
            {
                if (!DLC_WL.dlc_scenarioactive || DLC_WL.dlc_chosencommander < 0) return 0;
                if (GameVars.commander == null || DLC_WL.dlc_chosencommander >= GameVars.commander.Count) return 0;
                Regiment command = GameVars.commander[DLC_WL.dlc_chosencommander].currentcommand;
                if ((UnityEngine.Object)command == null || (UnityEngine.Object)command.groupaiobjectreg == null) return 0;
                return command.groupaiobjectreg.gameObject.GetInstanceID();
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-deployment-terrain:wl-group",
                    "Failed reading the W&L current command's AI group: " + ex.GetType().Name);
                return 0;
            }
        }

        /// <summary>
        /// Re-plans the groups <paramref name="candidate"/> marks (eligible and moved by vanilla in
        /// this call) from what the side can know, proposes the reserve echelon, and places the
        /// groups whose fog-honest placement differs from vanilla's.
        /// </summary>
        internal static DeploymentSummary Run(BattleUnits bunits, int alliance, DeploymentPass pass, bool[] candidate, bool firstDay)
        {
            var summary = new DeploymentSummary();
            if (bunits == null || pass == null || candidate == null) return summary;
            if (!BattleCore.TryGetSnapshot(alliance, out BattleSnapshot s) || s == null) return summary;
            Frontline2 zone = bunits.frontline2;
            if ((UnityEngine.Object)zone == null || BattleUnits.objectives == null) return summary;

            BattleUnits.Grp[] groups = pass.Groups;
            int count = CollectGroups(bunits, s, alliance, pass, candidate);
            summary.Candidates = count;
            if (count == 0) return summary;

            int enemyAlliance = alliance == 0 ? 1 : 0;
            int entries = ReadEnemyEntries(bunits, enemyAlliance);
            WaypointValidatorRuntime.TryMapSize(out float sizeX, out float sizeZ);
            float centreX = sizeX * 0.5f, centreZ = -sizeZ * 0.5f;

            int objectiveCount = BattleUnits.objectives.Length;
            var objectives = new DeploymentObjective[objectiveCount];
            var ownZone = new bool[objectiveCount, count];
            var enemyZone = new bool[objectiveCount, count];
            for (int o = 0; o < objectiveCount; o++)
            {
                Objectives objective = BattleUnits.objectives[o];
                if ((UnityEngine.Object)objective == null) { objectives[o].Owner = -2; continue; }
                Vector3 position = objective.transform.position;
                objectives[o] = new DeploymentObjective { X = position.x, Z = position.z, Owner = objective.owner };
                for (int g = 0; g < count; g++)
                {
                    ownZone[o, g] = zone.CheckIfWithinZone(position, alliance, _before[g]) >= 0;
                    enemyZone[o, g] = zone.CheckIfWithinZone(position, enemyAlliance, _before[g]) >= 0;
                }
            }

            summary.Replaced = DeploymentDoctrine.PlanPlacements(s, _groups, count, objectives, objectiveCount, ownZone, enemyZone,
                alliance, firstDay, _entryX, _entryZ, entries, centreX, centreZ, _plan);
            summary.Source = _plan.Source;

            ProposeAndClamp(zone, alliance, count, ref summary);

            for (int g = 0; g < count; g++)
            {
                DeploymentPlacement placement = _plan.Placements[g];
                if (placement.Kind == PlacementKind.Kept) continue;
                IntentSource source = placement.Kind == PlacementKind.Reserve ? IntentSource.DeploymentReserve : IntentSource.Deployment;
                if (Place(bunits, _regiments[g], placement.X, placement.Z, placement.Heading, source)) summary.Issued++;
                else summary.Refused++;
            }

            Report(alliance, firstDay, summary);
            return summary;
        }

        // The deployment reserve echelon (decision 8): propose, clamp every re-placed front into the
        // zone, and let ApplyReserveClamp revert a reserve child that no longer stands 150 m back.
        private static void ProposeAndClamp(Frontline2 zone, int alliance, int count, ref DeploymentSummary summary)
        {
            int reserved = DeploymentDoctrine.ProposeReserve(_plan, _groups, count);
            int before = reserved;
            if (_clampX.Length < count)
            {
                _clampX = new float[count];
                _clampZ = new float[count];
            }
            for (int g = 0; g < count; g++)
            {
                DeploymentPlacement p = _plan.Placements[g];
                _clampX[g] = p.X;
                _clampZ[g] = p.Z;
                if (p.Kind == PlacementKind.Kept) continue;
                Vector3 front = TacticalTerrainProbe.WithTerrainHeight(new Vector3(p.X, 0f, p.Z));
                Vector3 closest = zone.GetClosestPointInDeploymentZone(front, alliance, _before[g]);
                bool failed = closest == default(Vector3);
                if (!failed)
                {
                    _clampX[g] = closest.x;
                    _clampZ[g] = closest.z;
                }
                else if (p.Kind == PlacementKind.Reserve)
                {
                    // No zone point for a reserve front: put it on the line centroid so it reverts.
                    _clampX[g] = _plan.LineCentroidX;
                    _clampZ[g] = _plan.LineCentroidZ;
                }
            }

            if (_plan.HasReserve)
            {
                int remaining = DeploymentDoctrine.ApplyReserveClamp(_plan, _groups, count, _clampX, _clampZ);
                summary.ReserveGroups = remaining;
                summary.ClampReverted = before - remaining;
            }

            // Replaced fronts take their zone-clamped point (a reserve's was applied above).
            for (int g = 0; g < count; g++)
            {
                if (_plan.Placements[g].Kind != PlacementKind.Replaced) continue;
                _plan.Placements[g].X = _clampX[g];
                _plan.Placements[g].Z = _clampZ[g];
            }
            summary.ReserveChildren = CountReserveChildren(count);
        }

        private static int CountReserveChildren(int count)
        {
            int children = 0;
            for (int g = 0; g < count; g++)
            {
                if (_plan.Placements[g].Kind != PlacementKind.Reserve) continue;
                bool seen = false;
                for (int h = 0; h < g && !seen; h++)
                    seen = _plan.Placements[h].Kind == PlacementKind.Reserve && _groups[h].DirectChildId == _groups[g].DirectChildId;
                if (!seen) children++;
            }
            return children;
        }

        // Fills the pooled group array: one DeploymentGroup per candidate, in grp order.
        private static int CollectGroups(BattleUnits bunits, BattleSnapshot s, int alliance, DeploymentPass pass, bool[] candidate)
        {
            BattleUnits.Grp[] groups = pass.Groups;
            int count = 0;
            for (int i = 0; i < groups.Length && i < candidate.Length; i++)
            {
                if (candidate[i]) count++;
            }
            if (count == 0) return 0;
            if (_groups.Length < count)
            {
                int size = Math.Max(count, _groups.Length * 2);
                _groups = new DeploymentGroup[size];
                _regiments = new Regiment[size];
                _before = new Vector3[size];
            }

            int[] childIds = ReadDirectChildIds(alliance);
            UnitRecord[] own = s.Own;
            float back = GamePrefs.groupunitdistancetoback;
            int n = 0;
            for (int i = 0; i < groups.Length && i < candidate.Length; i++)
            {
                if (!candidate[i]) continue;
                Regiment regiment = groups[i].regref;
                int id = regiment.gameObject.GetInstanceID();
                int ownIndex = s.IndexOfOwn(id);
                Vector3 hq = regiment.transform.position;
                float heading = regiment.transform.eulerAngles.y;
                float depth = regiment.groupposition != null ? regiment.groupposition.groupdepth : regiment.depth;
                BattleWriterPolicy.DeploymentLineFrontFromHq(hq.x, hq.z, heading, depth, back, out float frontX, out float frontZ);

                int child = ownIndex >= 0 ? DeploymentDoctrine.DirectChildOf(s, ownIndex, childIds, childIds.Length) : 0;
                bool tainted = ownIndex >= 0 && ChildHoldsPlayerUnit(s, child);
                bool cavalryOnly = false, artilleryOnly = false;
                if (ownIndex >= 0) LeafKinds(s, ownIndex, out cavalryOnly, out artilleryOnly);
                bool locked = id == pass.WlGroupInstance || ownIndex < 0 ||
                    (own[ownIndex].Flags & (UnitFlags.PlayerOwned | UnitFlags.PlayerCommand | UnitFlags.AncestorOfPlayerCommand)) != 0;

                _groups[n] = new DeploymentGroup
                {
                    GroupId = id,
                    DirectChildId = child,
                    VanillaX = frontX, VanillaZ = frontZ, VanillaHeading = heading,
                    OldX = pass.Before[i].x, OldZ = pass.Before[i].z,
                    DepthMeters = depth,
                    Brigades = regiment.brigadesinaigroup,
                    PlayerOwned = tainted,
                    WlCommandAncestor = locked,
                    Locked = locked,
                    CavalryOnly = cavalryOnly,
                    ArtilleryOnly = artilleryOnly,
                };
                _regiments[n] = regiment;
                _before[n] = pass.Before[i];
                n++;
            }
            return n;
        }

        private static int[] ReadDirectChildIds(int alliance)
        {
            IReadOnlyList<DirectChildSnapshot> children = DirectChildDiscovery.Snapshot(alliance);
            var ids = new int[children.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = TacticalBattleCoordinator.ParseInstanceIdFromChildId(children[i].ChildId);
            return ids;
        }

        // The child, or a unit under it, is the W&L player's own or holds the player's command.
        private static bool ChildHoldsPlayerUnit(BattleSnapshot s, int childInstanceId)
        {
            if (childInstanceId == 0) return false;
            int child = s.IndexOfOwn(childInstanceId);
            if (child < 0) return false;
            const UnitFlags player = UnitFlags.PlayerOwned | UnitFlags.PlayerCommand | UnitFlags.AncestorOfPlayerCommand;
            UnitRecord[] own = s.Own;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if ((own[i].Flags & player) == 0) continue;
                if (i == child || s.IsAncestorOf(child, i)) return true;
            }
            return false;
        }

        // Cavalry-only / artillery-only: every combat regiment under the group is of that arm.
        private static void LeafKinds(BattleSnapshot s, int groupIndex, out bool cavalryOnly, out bool artilleryOnly)
        {
            int cavalry = 0, artillery = 0, other = 0;
            UnitRecord[] own = s.Own;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if (own[i].UnitTyp > TacticalUnitType.MaxCombat || !s.IsAncestorOf(groupIndex, i)) continue;
                if (own[i].UnitTyp == TacticalUnitType.Cavalry) cavalry++;
                else if (own[i].UnitTyp == TacticalUnitType.Artillery) artillery++;
                else other++;
            }
            cavalryOnly = cavalry > 0 && artillery == 0 && other == 0;
            artilleryOnly = artillery > 0 && cavalry == 0 && other == 0;
        }

        /// <summary>The enemy-owned entry points (public scenario data) into the pooled arrays; how many.</summary>
        internal static int ReadEnemyEntries(BattleUnits bunits, int enemyAlliance)
        {
            int n = 0;
            try
            {
                EntryPoint[] points = bunits != null ? bunits.entrypoints : null;
                if (points == null || enemyAlliance < 0) return 0;
                if (_entryX.Length < points.Length)
                {
                    _entryX = new float[points.Length];
                    _entryZ = new float[points.Length];
                }
                for (int i = 0; i < points.Length; i++)
                {
                    EntryPoint e = points[i];
                    if ((UnityEngine.Object)e == null || e.owner != enemyAlliance) continue;
                    Vector3 p = e.transform.position;
                    _entryX[n] = p.x;
                    _entryZ[n] = p.z;
                    n++;
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-deployment-terrain:entry-points",
                    "Failed reading the enemy entry points for deployment: " + ex.GetType().Name);
                return 0;
            }
            return n;
        }

        /// <summary>
        /// The point a side's deployment assumes the enemy is at, seen from (gx, gz): the same rule
        /// the placement uses (DeploymentDoctrine.AssumedEnemyPoint), for the terrain correction's
        /// facing and #58's observer rows.
        /// </summary>
        internal static bool TryAssumedEnemyPoint(int alliance, float gx, float gz, out float x, out float z, out AssumedEnemySource source)
        {
            x = 0f;
            z = 0f;
            source = AssumedEnemySource.MapCentre;
            try
            {
                if (alliance < 0 || alliance > 1) return false;
                BattleUnits bunits = BattleCoreTick.ResolveBattleUnits();
                BattleCore.TryGetSnapshot(alliance, out BattleSnapshot s);
                int entries = ReadEnemyEntries(bunits, alliance == 0 ? 1 : 0);
                WaypointValidatorRuntime.TryMapSize(out float sizeX, out float sizeZ);
                return DeploymentDoctrine.AssumedEnemyPoint(s, gx, gz, _entryX, _entryZ, entries, sizeX * 0.5f, -sizeZ * 0.5f, out x, out z, out source);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-deployment-terrain:assumed-enemy",
                    "Failed reading the assumed enemy point: " + ex.GetType().Name);
                return false;
            }
        }

        /// <summary>One immediate placement of a group at a line front, through the order executor.</summary>
        internal static bool Place(BattleUnits bunits, Regiment regiment, float frontX, float frontZ, float heading, IntentSource source)
        {
            if ((UnityEngine.Object)regiment == null) return false;
            var intent = new BattleOrderIntent
            {
                GroupId = regiment.gameObject.GetInstanceID(),
                Channel = OrderChannel.Move,
                TargetX = frontX,
                TargetZ = frontZ,
                Heading = heading,
                Priority = IntentPriority.Executor,
                Source = DeploymentDoctrine.PlacementSource(OrderExecutor.ForAlliance(regiment.alliance), regiment.gameObject.GetInstanceID(), source),
                Flags = IntentFlags.ImmediatePlacement,
                Reason = "deployment",
            };
            if (!OrderExecutorRuntime.TryPlanInline(bunits, regiment, intent, out ExecutionPlan plan, out OrderOutcome outcome)) return false;
            return OrderExecutorRuntime.ExecutePlannedInline(bunits, regiment, intent, plan, ref outcome);
        }

        private static void Report(int alliance, bool firstDay, in DeploymentSummary summary)
        {
            if (summary.Replaced > 0 && !_fogLogged)
            {
                _fogLogged = true;
                OnceLog.Info("tactical-battle-core:deployment-fog-first-fire",
                    "[Deployment] fog-honest re-placement: alliance=" + alliance + " source=" + summary.Source
                    + " candidates=" + summary.Candidates + " replaced=" + summary.Replaced + " day1=" + firstDay);
            }
            if (summary.ReserveGroups > 0 && !_reserveLogged)
            {
                _reserveLogged = true;
                OnceLog.Info("tactical-battle-core:deployment-reserve-first-fire",
                    "[Deployment] reserve echelon: alliance=" + alliance + " children=" + summary.ReserveChildren
                    + " groups=" + summary.ReserveGroups + " clampReverted=" + summary.ClampReverted);
            }
            if (_rows >= MaxRowsPerBattle) return;
            _rows++;
            DeploymentSummary row = summary;
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalDeploymentPlan", TelemetrySeverity.Info,
                ev => ev
                    .WithSide(alliance)
                    .WithDecision("DeploymentPlan", row.Source.ToString(), alliance + "|" + (firstDay ? 1 : 0) + "|" + row.Candidates)
                    .WithField("day1", firstDay)
                    .WithField("candidates", row.Candidates)
                    .WithField("replaced", row.Replaced)
                    .WithField("issued", row.Issued)
                    .WithField("refused", row.Refused)
                    .WithField("reserveGroups", row.ReserveGroups)
                    .WithField("reserveChildren", row.ReserveChildren)
                    .WithField("clampReverted", row.ClampReverted));
        }

        private static void Reset()
        {
            _rows = 0;
            _fogLogged = false;
            _reserveLogged = false;
        }
    }
}
