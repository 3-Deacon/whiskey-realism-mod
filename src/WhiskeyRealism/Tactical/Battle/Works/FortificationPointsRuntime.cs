using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC14W requirement-works-picture, the Unity side (excluded from the test csproj). Every works line in a battle is an ObstacleLine whatever
    // made it: the AI's deployment works (Fortification.FortificationGroups.Create adds one to the group's object), the lines the player draws at
    // deployment (BattleUI adds one per drawing) and the lines a save loads (LoadObstacles puts them in BattleUI.obstaclelines). Each keeps its
    // pieces in the public modelobjects (modelobj, completion); Create stops adding pieces at the group's lengthneeded and FinishImmediately
    // destroys the ones it could not afford, so no piece stands past what was built. This class enumerates every ObstacleLine at battle start and
    // again when the deployment phase ends (the AI's lines live only on its groups' objects), re-reads BattleUI.obstaclelines and the lines
    // already known at each evaluation, and hands the pieces to the pure WorksPointTable, which keeps what the side has perceived.
    //
    // BattlefieldSetup.fortifications' own placement candidates and Fortification.occupiedby (cleared again after deployment) are never read.
    // With Enable Enemy Works Doctrine off nothing is read and the evaluation sees no works point. Never throws: an unreadable line degrades
    // to the works samples alone and logs once.
    internal static class FortificationPointsRuntime
    {
        // The deployment-zone classification of a piece is a physics raycast; a battle's lines are read over a few evaluations rather than in one.
        private const int ZoneClassificationsPerRead = 128;

        private sealed class Side
        {
            public readonly WorksPointTable Table = new WorksPointTable();
            public readonly List<ObstacleLine> Lines = new List<ObstacleLine>(16);
            public readonly Dictionary<int, bool> InOwnZone = new Dictionary<int, bool>(256);
            public WorksPiece[] Pieces = new WorksPiece[256];
            public bool Enumerated, WasDeploying;
        }

        private static readonly Side[] Sides = { new Side(), new Side() };

        static FortificationPointsRuntime()
        {
            BattleScope.Register("fortification-points", Reset);
        }

        private static void Reset()
        {
            for (int i = 0; i < Sides.Length; i++)
            {
                Sides[i].Lines.Clear();
                Sides[i].InOwnZone.Clear();
                Sides[i].Enumerated = false;
                Sides[i].WasDeploying = false;
                Sides[i].Table.Merge(default(ReadOnlySpan<WorksPiece>), 0f, 0f);
            }
        }

        /// <summary>
        /// The side's works points after one more read of the battle's lines (null with the flag off). Called once per evaluation by the
        /// feasibility runtime, with the own centroid the farthest-first drop of a crowded battle is measured from.
        /// </summary>
        internal static WorksPointTable Refresh(BattleUnits bunits, int alliance, float ownX, float ownZ)
        {
            if (!Plugin.EnemyWorksDoctrineEnabled() || alliance < 0 || alliance >= Sides.Length || (UnityEngine.Object)bunits == null) return null;
            Side side = Sides[alliance];
            try
            {
                BattleUI bui = OwnDeploymentZoneRuntime.Bui(bunits);
                bool deploying = (UnityEngine.Object)bui != null && bui.deploymentmodeactive;
                // Battle start (the first read) and the end of deployment: the AI's lines are on its groups' objects only.
                if (!side.Enumerated || (side.WasDeploying && !deploying))
                {
                    side.Enumerated = true;
                    ObstacleLine[] found = UnityEngine.Object.FindObjectsOfType<ObstacleLine>();
                    for (int i = 0; i < found.Length; i++) Know(side, found[i]);
                }
                side.WasDeploying = deploying;
                if ((UnityEngine.Object)bui != null && bui.obstaclelines != null)
                    for (int i = 0; i < bui.obstaclelines.Count; i++) Know(side, bui.obstaclelines[i]);

                int count = ReadPieces(side, bunits, alliance);
                side.Table.Merge(new ReadOnlySpan<WorksPiece>(side.Pieces, 0, count), ownX, ownZ);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:works-points-missing",
                    "[EnemyWorks] the battle's ObstacleLines are unreadable, works from the samples alone: " + ex.GetType().Name + " " + ex.Message);
            }
            return side.Table;
        }

        /// <summary>The side's table as the last read left it (null with the flag off); the evaluation probes it inside its slice.</summary>
        internal static WorksPointTable TableOf(int alliance)
            => Plugin.EnemyWorksDoctrineEnabled() && alliance >= 0 && alliance < Sides.Length ? Sides[alliance].Table : null;

        private static void Know(Side side, ObstacleLine line)
        {
            if ((UnityEngine.Object)line == null || side.Lines.Contains(line)) return;
            side.Lines.Add(line);
        }

        // Every piece of every known line whose model object exists, with its completion, position and zone. A line destroyed since the last read
        // is dropped; a piece whose zone is not yet classified waits for the next read (it is not offered, so it counts for neither side).
        private static int ReadPieces(Side side, BattleUnits bunits, int alliance)
        {
            int count = 0, classified = 0;
            Frontline2 zone = bunits.frontline2;
            for (int l = side.Lines.Count - 1; l >= 0; l--)
            {
                ObstacleLine line = side.Lines[l];
                if ((UnityEngine.Object)line == null) { side.Lines.RemoveAt(l); continue; }
                List<ObstacleLine.ModelObject> models = line.modelobjects;
                for (int m = 0; models != null && m < models.Count; m++)
                {
                    ObstacleLine.ModelObject model = models[m];
                    if (model == null || (UnityEngine.Object)model.modelobj == null) continue;
                    int id = model.modelobj.GetInstanceID();
                    Vector3 position = model.modelobj.transform.position;
                    if (!side.InOwnZone.TryGetValue(id, out bool inOwn))
                    {
                        if (classified >= ZoneClassificationsPerRead || (UnityEngine.Object)zone == null) continue;
                        classified++;
                        inOwn = zone.CheckIfWithinZone(position, alliance) >= 0;
                        side.InOwnZone[id] = inOwn;
                    }
                    if (count == side.Pieces.Length) Array.Resize(ref side.Pieces, count * 2);
                    side.Pieces[count++] = new WorksPiece { InstanceId = id, X = position.x, Z = position.z, Completion = model.completion, InOwnZone = inOwn };
                }
            }
            return count;
        }
    }
}
