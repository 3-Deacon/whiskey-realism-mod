namespace WhiskeyRealism.Tactical.PlayerOrders
{
    internal static class PlayerOrderVanillaScene
    {
        public static bool IsGivenOrderActiveForScene(int type, int currentOperation)
        {
            bool inBattle = currentOperation == 1 || currentOperation == 3 || currentOperation == 8;
            bool battleOrder = IsBattleOrderType(type);
            return inBattle ? battleOrder : !battleOrder;
        }

        public static PlayerOrderScope ScopeForVanillaType(int type)
        {
            return IsBattleOrderType(type) ? PlayerOrderScope.Tactical : PlayerOrderScope.Campaign;
        }

        // Whiskey's battle set: vanilla's IsBattleOrder (41772) leaves out 13, 14 and 15, which makes
        // IsActive read an engage-at-will or a retreat as stale in battle; the re-admit logic must see
        // them as the live orders they are. Vanilla's own IsActive is untouched.
        private static bool IsBattleOrderType(int type)
        {
            return (type >= 0 && type <= 4) || (type >= 11 && type <= 15);
        }
    }
}
