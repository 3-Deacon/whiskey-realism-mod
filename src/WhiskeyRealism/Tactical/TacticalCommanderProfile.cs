using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalCommandTier
    {
        Unknown = 0,
        Regiment = 1,
        Brigade = 2,
        Division = 3,
        Corps = 4,
        Army = 5
    }

    public readonly struct TacticalCommanderProfile
    {
        public TacticalCommanderProfile(
            int stableId,
            string displayName,
            TacticalCommandTier tier,
            bool isTopUnit,
            bool underPlayerCommander,
            int parentId,
            int alliance,
            int side,
            float initiative01)
        {
            StableId = stableId;
            DisplayName = SanitizeString(displayName);
            Tier = tier;
            IsTopUnit = isTopUnit;
            UnderPlayerCommander = underPlayerCommander;
            ParentId = parentId;
            Alliance = alliance;
            Side = side;
            Initiative01 = Clamp01(initiative01);
        }

        public int StableId { get; }
        public string DisplayName { get; }
        public TacticalCommandTier Tier { get; }
        public bool IsTopUnit { get; }
        public bool UnderPlayerCommander { get; }
        public int ParentId { get; }
        public int Alliance { get; }
        public int Side { get; }
        public float Initiative01 { get; }

        public static TacticalCommanderProfile FromVanillaShape(
            int stableId,
            string displayName,
            int unitType,
            bool isTopUnit,
            bool underPlayerCommander,
            int parentId,
            int alliance,
            int side,
            float initiative01)
        {
            return new TacticalCommanderProfile(
                stableId,
                displayName,
                TierFromUnitType(unitType, isTopUnit),
                isTopUnit,
                underPlayerCommander,
                parentId,
                alliance,
                side,
                initiative01);
        }

        public static TacticalCommandTier TierFromUnitType(int unitType, bool isTopUnit)
        {
            if (unitType < 0) return TacticalCommandTier.Unknown;
            if (unitType <= 13) return TacticalCommandTier.Regiment;
            if (unitType == 14) return TacticalCommandTier.Division;
            if (unitType == 15) return TacticalCommandTier.Brigade;
            if (unitType == 16) return isTopUnit ? TacticalCommandTier.Army : TacticalCommandTier.Corps;
            if (unitType > 16) return TacticalCommandTier.Army;
            return TacticalCommandTier.Unknown;
        }

        private static string SanitizeString(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            return value.Trim();
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0.5f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
