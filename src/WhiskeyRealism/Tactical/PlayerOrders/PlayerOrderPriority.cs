namespace WhiskeyRealism.Tactical.PlayerOrders
{
    internal static class PlayerOrderPriority
    {
        public static int ForIntent(PlayerOrderIntent intent)
        {
            switch (intent)
            {
                case PlayerOrderIntent.RetreatToExit:
                    return 100;
                case PlayerOrderIntent.FallBackToLine:
                    return 90;
                case PlayerOrderIntent.HoldObjective:
                    return 80;
                case PlayerOrderIntent.SupportMainEffort:
                    return 70;
                case PlayerOrderIntent.AttackObjective:
                case PlayerOrderIntent.AdvanceToAssemblyArea:
                    return 60;
                case PlayerOrderIntent.ProbeObjective:
                    return 50;
                case PlayerOrderIntent.BuildSupplyDepot:
                case PlayerOrderIntent.BuildFort:
                case PlayerOrderIntent.DefendCapital:
                    return 30;
                case PlayerOrderIntent.RecoverFromCombat:
                case PlayerOrderIntent.ClearHoldTransition:
                    return 20;
                default:
                    return 0;
            }
        }

        public static int ForActiveVanillaType(int type, PlayerOrderScope scope, PlayerOrderProvenance provenance)
        {
            switch (type)
            {
                case 15:
                    return 100;
                case 12:
                    return 80;
                case 7:
                    return scope == PlayerOrderScope.Campaign ? 90 : 80;
                case 13:
                case 14:
                    return provenance == PlayerOrderProvenance.WhiskeyTactical ||
                        provenance == PlayerOrderProvenance.WhiskeyCampaign
                        ? 20
                        : 100;
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                    return 60;
                case 6:
                case 8:
                case 9:
                case 10:
                case 16:
                    return 30;
                default:
                    return int.MaxValue;
            }
        }
    }
}
