namespace WhiskeyRealism.Tactical.PlayerOrders
{
    internal static class PlayerOrderVanillaMapper
    {
        public static PlayerOrderVanillaMapping Map(
            PlayerOrderIntent intent,
            PlayerOrderScope scope = PlayerOrderScope.Tactical)
        {
            switch (intent)
            {
                case PlayerOrderIntent.RetreatToExit:
                    return new PlayerOrderVanillaMapping(15, intent);
                case PlayerOrderIntent.FallBackToLine:
                case PlayerOrderIntent.HoldObjective:
                    return new PlayerOrderVanillaMapping(12, intent);
                case PlayerOrderIntent.SupportMainEffort:
                    return new PlayerOrderVanillaMapping(scope == PlayerOrderScope.Campaign ? 7 : 4, intent);
                case PlayerOrderIntent.AttackObjective:
                    return new PlayerOrderVanillaMapping(1, intent);
                case PlayerOrderIntent.ProbeObjective:
                    return new PlayerOrderVanillaMapping(5, intent);
                case PlayerOrderIntent.AdvanceToAssemblyArea:
                    return new PlayerOrderVanillaMapping(4, intent);
                case PlayerOrderIntent.DefendCapital:
                    return new PlayerOrderVanillaMapping(8, intent);
                case PlayerOrderIntent.BuildFort:
                    return new PlayerOrderVanillaMapping(9, intent);
                case PlayerOrderIntent.BuildSupplyDepot:
                    return new PlayerOrderVanillaMapping(10, intent);
                case PlayerOrderIntent.RecoverFromCombat:
                    return new PlayerOrderVanillaMapping(13, intent);
                case PlayerOrderIntent.ClearHoldTransition:
                    return new PlayerOrderVanillaMapping(14, intent);
                default:
                    return new PlayerOrderVanillaMapping(-1, PlayerOrderIntent.None);
            }
        }
    }
}
