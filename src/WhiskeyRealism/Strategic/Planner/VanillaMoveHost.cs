using UnityEngine;
using WhiskeyRealism.Patches;

namespace WhiskeyRealism.Strategic.Planner
{
    // The vanilla side of sending one formation (IMoveHost), shared by the mover (G5a) and the reconnaissance executor
    // (G5d): the group of the reviewing alliance by instance id, the final-waypoint test, the move itself and vanilla's AI
    // transport default. A subclass binds Alliance for the pass.
    //
    // Vanilla it rests on (decompile build 14703111): AICampaign.MoveUnitTo 14479-14540 (the move is a queued
    // DefensiveMovingOrder when movearoundenemyunits is set, one order served at a time, 9933-10052; it refuses a target in a
    // state that is neither own, allied nor at war, 14486); BattlefieldSetup.IsTerrainPositionRestricted 25755 (the final
    // waypoint test SetWaypoint applies through CheckIfFinalWaypointIsPassable, 130768).
    internal abstract class VanillaMoveHost : IMoveHost
    {
        protected int Alliance;

        // The live group of a snapshot formation, only for the reviewing alliance's own groups.
        protected Regiment UnitOf(int instanceId)
        {
            var unit = CampaignSnapshotRuntime.OwnUnit(instanceId);
            return unit != null && unit.alliance == Alliance ? unit : null;
        }

        public bool TargetRestricted(float x, float z)
        {
            var bfs = CampaignSnapshotRuntime.ResolveBattlefieldSetup();
            if ((UnityEngine.Object)bfs == null) return false;
            return bfs.IsTerrainPositionRestricted(new Vector3(x, 0f, z), Alliance);
        }

        public bool Move(int instanceId, float x, float z)
        {
            var unit = UnitOf(instanceId);
            if (unit == null) return false;
            return AICampaign.MoveUnitTo(unit, new Vector3(x, unit.GetPosition().y, z), movearoundenemyunits: true);
        }

        // Vanilla gives userails = userivers = true to units of non-player alliances only (112862-112866); the player's
        // alliance keeps stored values (default false, 111466-111470; scenario and save data 74178-74180, 71375-71378).
        public void RaiseTransportModes(int instanceId)
        {
            var unit = UnitOf(instanceId);
            if (unit == null) return;
            unit.userails = true;
            unit.userivers = true;
        }
    }
}
