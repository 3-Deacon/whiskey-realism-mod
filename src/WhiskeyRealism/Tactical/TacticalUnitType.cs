namespace WhiskeyRealism.Tactical
{
    // Vanilla Regiment.unittyp values (unitprefs.txt "Unit Type n# Name": 0 Infantry,
    // 1 Cavalry, 2 Artillery, 3 Skirmishers, 4 Scouts, 5 Courier; groups are > 13).
    public static class TacticalUnitType
    {
        public const int Infantry = 0;
        public const int Cavalry = 1;
        public const int Artillery = 2;
        public const int Skirmisher = 3;
        public const int Scout = 4;
        public const int Courier = 5;
        public const int MaxCombat = 13;
        public const int BattleGroupBrigade = 14;
        public const int BattleGroupDivision = 15;
        public const int BattleGroupArmy = 16;
    }
}
