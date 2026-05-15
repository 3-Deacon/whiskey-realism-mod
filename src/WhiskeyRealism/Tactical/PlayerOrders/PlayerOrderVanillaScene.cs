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

        private static bool IsBattleOrderType(int type)
        {
            return type == 0 || type == 1 || type == 2 || type == 3 || type == 4 || type == 11 || type == 12;
        }
    }
}
