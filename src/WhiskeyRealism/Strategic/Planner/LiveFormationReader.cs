using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WhiskeyRealism.Strategic.Planner
{
    // A faction's own top groups (AIFaction.ownunits) as FormationRecords, read live inside a vanilla
    // call. The replacement-flow patches (#3, #4, recruit routing) run between daily reviews, where no
    // CampaignSnapshot is published (the pooled snapshot is rebuilt in place by the review), so they
    // read the few fields their pure policies need straight from the units. The allocation's instance
    // ids are the same Regiment.GetInstanceID values the snapshot recorded. Runtime-only: it touches
    // Unity types, so the console harness does not compile it.
    internal static class LiveFormationReader
    {
        // activeStrength: vanilla's capital need (AssignUnitToDefendCapital 11733-11740) reads
        // groupstrengthactive; the replacement flow reads groupstrengthdirect, the measure the
        // allocation was built on. Chain classes come from one captured player context.
        internal static void Read(IList ownUnits, int allianceId, bool activeStrength, List<FormationRecord> into)
        {
            into.Clear();
            if (ownUnits == null) return;
            var chain = WlChainContext.Capture();
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i] as Regiment;
                if ((UnityEngine.Object)unit == null) continue;
                Vector3 position = unit.GetPosition();
                Vector3 waypoint = unit.GetLastWaypointPos();
                into.Add(new FormationRecord
                {
                    InstanceId = unit.GetInstanceID(),
                    X = position.x,
                    Z = position.z,
                    WaypointX = waypoint.x,
                    WaypointZ = waypoint.z,
                    Strength = activeStrength ? unit.groupstrengthactive : unit.groupstrengthdirect,
                    Morale = unit.groupmorale,
                    Flags = unit.transfertime > 0f ? FormationFlags.InTransfer : FormationFlags.None,
                    Chain = WlStrategicOrderBridge.ChainClassOf(in chain, allianceId, unit)
                });
            }
        }

        // The unit's name for a telemetry row, "-" when it is not in the list.
        internal static string NameOf(IList ownUnits, int instanceId)
        {
            if (ownUnits == null) return "-";
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i] as Regiment;
                if ((UnityEngine.Object)unit != null && unit.GetInstanceID() == instanceId) return ((UnityEngine.Object)unit).name;
            }
            return "-";
        }
    }
}
