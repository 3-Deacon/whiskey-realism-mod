using System;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    // Runtime adapter of WeaponOrderability (not in the test csproj): reads one model's
    // research, import and production state for the asking alliance from GameVars.weapon.
    // A failed read answers "not masked" (vanilla's own candidate rule then stands) with one
    // bounded warning. The G9 purchase pick (OrdnanceRuntime) reads the same three facts per candidate and applies WeaponOrderability.IsOrderable.
    internal static class WeaponOrderabilityRuntime
    {
        internal static bool IsResearchedButUnorderable(int alliance, int weaponId)
        {
            try
            {
                var weapon = Model(weaponId);
                if (weapon == null) return false;
                return WeaponOrderability.IsResearchedButUnorderable(weapon.IsResearched(alliance), weapon.IsImported(alliance), weapon.productionmultiplier);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("weapon-orderability:read", "[WeaponOrderability] weapon state read failed: " + ex.Message);
                return false;
            }
        }

        private static GameVars.Weapon Model(int weaponId)
        {
            var weapons = GameVars.weapon;
            if (weapons == null || weaponId < 0 || weaponId >= weapons.Length) return null;
            return weapons[weaponId];
        }
    }
}
