using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    // One weapon model as the arms need reads it (own alliance only).
    public sealed class ArmsModel
    {
        public bool IsArtillery;          // Weapon.isari: vanilla's gun / rifle split (16290)
        public bool FitsBattery;          // restriction contains 2: a field battery can carry it
        public bool FitsLine;             // restriction contains 0 or 1: infantry or cavalry
        public float Strength;            // vanilla's WSF, AICampaign.GetWeaponStrengthFactor(id) (16214)
        public float StockAndOpenOrders;  // weaponstock + order quantity - already delivered (16294, 16311)
        public bool Orderable;            // WeaponOrderability for this alliance
    }

    // One lane (guns = artillery units, rifles = every other basic unit, as the alliance tick
    // splits them, 81589-81605) of an alliance's arming.
    public sealed class ArmsLane
    {
        public int Units;                    // artilleryunits / otherunits
        public float AverageStrength;        // averageweaponstrengthguns / rifles
        public float BestOrderableStrength;  // best WSF the alliance can order for this lane (0: none)
        public float EquippableUnits;        // vanilla's purchase-trigger count (16287-16312)
    }

    public static class ArmsCoverage
    {
        // Fills each lane's EquippableUnits and BestOrderableStrength from the models, the way
        // CheckPurchaseWeapons counts (16287-16312): a model better than the lane's average
        // adds floor((stock + open orders) / pieces per unit), whether or not it can be
        // ordered again; the best orderable model is taken over models that fit the lane.
        // Lanes' Units and AverageStrength are read before the call.
        public static void Fill(IEnumerable<ArmsModel> models, ArmsLane rifles, ArmsLane guns, float gunsPerUnit, float riflesPerUnit)
        {
            if (rifles != null) { rifles.EquippableUnits = 0f; rifles.BestOrderableStrength = 0f; }
            if (guns != null) { guns.EquippableUnits = 0f; guns.BestOrderableStrength = 0f; }
            if (models == null) return;
            foreach (var model in models)
            {
                if (model == null || !Finite(model.Strength)) continue;
                var lane = model.IsArtillery ? guns : rifles;
                if (lane == null) continue;
                float perUnit = model.IsArtillery ? gunsPerUnit : riflesPerUnit;
                if (model.Strength > lane.AverageStrength && perUnit > 0f && Finite(model.StockAndOpenOrders))
                    lane.EquippableUnits += (float)Math.Floor(model.StockAndOpenOrders / perUnit);
                bool fits = model.IsArtillery ? model.FitsBattery : model.FitsLine;
                if (model.Orderable && fits && model.Strength > lane.BestOrderableStrength)
                    lane.BestOrderableStrength = model.Strength;
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
