using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    internal class BattleHistoryRecord
    {
        public string BattleName;
        public int Day;
        public int Month;
        public int Year;
        public int LandOrSea;
        public int AllianceWon = -1;
        public int BattleResultType = -1;
        public int BattleEndType = -1;
        public Theater Theater = Theater.Unknown;
        public float PositionX;
        public float PositionZ;
        public int[] Alliance = new int[2] { -1, -1 };
        public int[] Commander = new int[2] { -1, -1 };
        public string[] CommanderName = new string[2] { "", "" };
        public int[] Casualties = new int[2];
        public List<int> CommanderKia = new List<int>();

        public int LosingAlliance
        {
            get
            {
                if (AllianceWon < 0) return -1;
                if (Alliance[0] >= 0 && Alliance[0] != AllianceWon) return Alliance[0];
                if (Alliance[1] >= 0 && Alliance[1] != AllianceWon) return Alliance[1];
                return AllianceWon == 0 ? 1 : 0;
            }
        }

        public bool IsLandBattle => LandOrSea == 0;
        public bool IsMajorResult => BattleResultType == 1 || BattleResultType == 3;

        public bool SameBattleKey(BattleHistoryRecord other)
        {
            if (other == null) return false;
            return BattleName == other.BattleName
                && Day == other.Day
                && Month == other.Month
                && Year == other.Year
                && AllianceWon == other.AllianceWon
                && BattleResultType == other.BattleResultType;
        }
    }
}
