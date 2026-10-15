using System;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Static methods the perk-id transpilers (PerkIdIlRewrites) call from vanilla IL. Public
    // so the patched method body can call them. Each reads its flag on every call, so turning
    // the flag off in the config returns vanilla's id or result at once. Never throw: a
    // failure answers what vanilla would have.
    public static class PerkIdIlHooks
    {
        private static bool _footCavalryLogged;
        private static bool _marchExpLogged;
        private static bool _zouaveSoundLogged;

        // Last CalculateCommanderFame lookup: the unit asked about and the unit whose level
        // the following GetPerkLevel(14) must read. Main thread only.
        private static Regiment _fameUnit;
        private static Regiment _fameLevelHolder;

        static PerkIdIlHooks()
        {
            BattleScope.Register("perk-ids-fame-holder", () =>
            {
                _fameUnit = null;
                _fameLevelHolder = null;
            });
        }

        // A perks swap over Regiment.perks for the Prefix/Finalizer patches.
        internal static PerkListSwap<Regiment, Regiment.Perk> NewPerkSwap()
        {
            return new PerkListSwap<Regiment, Regiment.Perk>(unit => unit.perks, (unit, perks) => unit.perks = perks);
        }

        // Logs a swap's restore failure once under the patch's key.
        internal static void LogRestore(string failure, string key)
        {
            if (failure != null)
                OnceLog.Warning(key, "[Patch:PerkIds] perks restore failed: " + failure);
        }

        internal static bool FixIds()
        {
            try { return Plugin.FixPerkIdMismatches != null && Plugin.FixPerkIdMismatches.Value; }
            catch { return false; }
        }

        internal static bool FixScope()
        {
            try { return Plugin.FixPerkEffectScope != null && Plugin.FixPerkEffectScope.Value; }
            catch { return false; }
        }

        // Autocalc.FightUnit 21080-21082: the Sharpshooter bonus belongs to battle id 1.
        public static int AutocalcSharpshooters(int vanillaId)
        {
            return VanillaPerkTables.RemapId(FixIds(), vanillaId, VanillaPerkTables.BattleSharpshooters);
        }

        // Autocalc.FightUnit 21247 and Autocalc.UpdateFatigue 21368-21370: the Zouave fatigue
        // effects belong to battle id 2 (Regiment.UpdateMorale 128659, UpdateFatigue 132665).
        public static int AutocalcZouave(int vanillaId)
        {
            return VanillaPerkTables.RemapId(FixIds(), vanillaId, VanillaPerkTables.BattleZouave);
        }

        // Autocalc.FightUnit 21155: fightingstrengthembarkedriverexp is River Expedition's.
        public static int AutocalcRiverExpedition(int vanillaId)
        {
            return VanillaPerkTables.RemapId(FixIds(), vanillaId, VanillaPerkTables.CampaignRiverExpedition);
        }

        // Regiment.UpdateMorale 128617: `HasPerk(8) && GetPerkLevel(9) >= 2` pairs Expert
        // Scouts with Cold Steel's level; one battle slot makes it never true. The level read
        // and the melee context name Cold Steel (9).
        public static int MoraleColdSteel(int vanillaId)
        {
            return VanillaPerkTables.RemapId(FixIds(), vanillaId, VanillaPerkTables.BattleColdSteel);
        }

        // RegimentMove 133518: Foot Cavalry forced-march speed.
        public static bool FootCavalrySpeedHas(Regiment unit, int perk)
        {
            using (TelemetryPerf.Scope("campaign.patch.perk-ids.regiment-move.foot-cavalry", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                bool vanilla = unit.HasPerk(perk);
                return vanilla && FootCavalryGate(unit);
            }
        }

        // RegimentMove 133522: the parent army's Foot Cavalry, same gate.
        public static bool FootCavalrySpeedHasParent(Regiment unit, int perk)
        {
            using (TelemetryPerf.Scope("campaign.patch.perk-ids.regiment-move.foot-cavalry-parent", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                bool vanilla = unit.HasPerkParent(perk);
                return vanilla && FootCavalryGate(unit);
            }
        }

        // RegimentMove 134115/134118: Flying Column / Foot Cavalry XP; a fleet's 5/6 are
        // Blockading Squadron / Sea Raiders.
        public static void MarchPerkExp(Regiment unit, float expIncrease, int perk)
        {
            using (TelemetryPerf.Scope("campaign.patch.perk-ids.regiment-move.march-exp", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                bool skip = false;
                try { skip = !VanillaPerkTables.GrantMarchPerkExp(FixIds(), unit.unittyp); }
                catch { }
                if (skip)
                {
                    if (!_marchExpLogged)
                    {
                        _marchExpLogged = true;
                        OnceLog.Info("perk-ids:march-exp", "[Patch:PerkIds] RegimentMove: marching XP for Flying Column / Foot Cavalry withheld from a fleet (5/6 = Blockading Squadron / Sea Raiders)");
                    }
                    return;
                }
                unit.IncreasePerkExp(expIncrease, perk);
            }
        }

        // RegimentMove 133734/133743: the Zouave running sound; a battle HQ's 2 is Balloon Corps.
        public static bool BattleTableHasPerk(Regiment unit, int perk)
        {
            using (TelemetryPerf.Scope("tactical.patch.perk-ids.regiment-move.zouave-sound", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (!unit.HasPerk(perk)) return false;
                bool applies = true;
                try { applies = VanillaPerkTables.BattlePerkCounts(FixIds(), unit.unittyp); }
                catch { }
                if (!applies && !_zouaveSoundLogged)
                {
                    _zouaveSoundLogged = true;
                    OnceLog.Info("perk-ids:zouave-sound", "[Patch:PerkIds] RegimentMove: Zouave running sound withheld from a battle HQ's campaign perk 2 (Balloon Corps)");
                }
                return applies;
            }
        }

        // CalculateCommanderFame 83930: the unit itself first, then vanilla's parent search.
        public static Regiment FameReportersHolder(BattleUnits units, Regiment unit, int perk)
        {
            Regiment ancestor = units.CheckForGroupPerk(unit, perk);
            _fameUnit = unit;
            _fameLevelHolder = null;
            try
            {
                bool selfHas = (UnityEngine.Object)unit != null && unit.HasPerk(perk);
                switch (VanillaPerkTables.FameReportersHolder(selfHas, (UnityEngine.Object)ancestor != null, FixScope()))
                {
                    case PerkHolder.Self:
                        _fameLevelHolder = unit;
                        if ((UnityEngine.Object)ancestor == null)
                            OnceLog.Info("perk-ids:fame-self", "[Patch:PerkIds] CalculateCommanderFame: a unit below the top now counts its own Embedded Reporters");
                        return unit;
                    case PerkHolder.Ancestor:
                        _fameLevelHolder = ancestor;
                        return ancestor;
                    case PerkHolder.AncestorWithOwnLevel:
                        _fameLevelHolder = unit;
                        return ancestor;
                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("perk-ids:fame-holder", "[Patch:PerkIds] fame holder lookup failed, vanilla answer used: " + ex.Message);
                _fameLevelHolder = unit;
                return ancestor;
            }
        }

        // CalculateCommanderFame 83932: the level of the unit that holds the perk.
        public static int FameReportersLevel(Regiment unit, int perk)
        {
            Regiment holder = ReferenceEquals(unit, _fameUnit) && (UnityEngine.Object)_fameLevelHolder != null ? _fameLevelHolder : unit;
            return holder.GetPerkLevel(perk);
        }

        private static bool FootCavalryGate(Regiment unit)
        {
            bool applies = true;
            try
            {
                applies = VanillaPerkTables.FootCavalrySpeedApplies(unit.unittyp, unit.doublequick | unit.running, FixIds(), FixScope());
            }
            catch { }
            if (!applies && !_footCavalryLogged)
            {
                _footCavalryLogged = true;
                OnceLog.Info("perk-ids:foot-cavalry", "[Patch:PerkIds] RegimentMove: Foot Cavalry speed withheld (fleet Sea Raiders, or not on a forced march)");
            }
            return applies;
        }
    }
}
