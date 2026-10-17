using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Runtime half of the leaf reserve slot's deployment clamp (excluded from the test csproj): while the player's deployment phase is on
    // (BattleUI.deploymentmodeactive, the private BattleUnits.bui 78976), a slot is kept inside the side's own deployment zone with the same
    // vanilla call #60's reserve echelon clamps with (Frontline2.GetClosestPointInDeploymentZone, DeploymentPlanRuntime.ProposeAndClamp). Outside the
    // deployment phase it is inactive and the slot is clamped by the BC14 regions alone. Never throws; an unreadable zone is inactive.
    internal sealed class OwnDeploymentZoneRuntime : IOwnDeploymentZone
    {
        private static Func<object, BattleUI> _bui;
        private static bool _resolved;
        private static readonly OwnDeploymentZoneRuntime[] Sides = { new OwnDeploymentZoneRuntime(), new OwnDeploymentZoneRuntime() };
        private BattleUnits _bunits;
        private int _alliance;

        /// <summary>The side's zone view (pooled: one per side, rebound each call).</summary>
        internal static OwnDeploymentZoneRuntime For(BattleUnits bunits, int alliance)
        {
            if (alliance < 0 || alliance >= Sides.Length) alliance = 0;
            OwnDeploymentZoneRuntime zone = Sides[alliance];
            zone._bunits = bunits;
            zone._alliance = alliance;
            return zone;
        }

        public bool Active
        {
            get
            {
                try
                {
                    if ((UnityEngine.Object)_bunits == null) return false;
                    BattleUI bui = Bui(_bunits);
                    return (UnityEngine.Object)bui != null && bui.deploymentmodeactive;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-battle-core:own-zone-active", "[LeafReserve] the deployment phase flag is unreadable, no zone clamp: " + ex.GetType().Name);
                    return false;
                }
            }
        }

        public bool TryClamp(float fromX, float fromZ, ref float x, ref float z)
        {
            try
            {
                Frontline2 zone = _bunits != null ? _bunits.frontline2 : null;
                if ((UnityEngine.Object)zone == null) return false;
                Vector3 front = TacticalTerrainProbe.WithTerrainHeight(new Vector3(x, 0f, z));
                Vector3 from = TacticalTerrainProbe.WithTerrainHeight(new Vector3(fromX, 0f, fromZ));
                Vector3 closest = zone.GetClosestPointInDeploymentZone(front, _alliance, from);
                if (closest == default(Vector3)) return false;
                x = closest.x;
                z = closest.z;
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:own-zone-clamp", "[LeafReserve] the deployment zone clamp failed, no slot: " + ex.GetType().Name);
                return false;
            }
        }

        private static BattleUI Bui(BattleUnits units)
        {
            if (!_resolved)
            {
                _resolved = true;
                FieldInfo field = AccessTools.Field(typeof(BattleUnits), "bui");
                _bui = CompiledFieldGetter.Build<BattleUI>(field);
                if (_bui == null) OnceLog.Warning("tactical-battle-core:own-zone-bui", "[LeafReserve] BattleUnits.bui not found; no deployment zone clamp");
            }
            return _bui != null ? _bui(units) : null;
        }
    }
}
