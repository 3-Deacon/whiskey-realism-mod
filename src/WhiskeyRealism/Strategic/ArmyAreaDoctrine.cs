using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public enum ArmyAreaBehavior
    {
        Hold,
        Screen,
        Reserve,
        Exploit,
        Counterstroke,
        Recover
    }

    public sealed class ArmyAreaDoctrine
    {
        public string DoctrineId;
        public int AllianceId;
        public string PrimaryAreaKey;
        public List<string> PreferredAreaKeys = new List<string>();
        public float HistoricalWeight;
        public float Flexibility;
        public float OffensiveBias;
        public float DefensiveBias;
    }

    public sealed class ArmyAreaInput
    {
        public string UnitKey;
        public int AllianceId;
        public string UnitName;
        public string CommanderName;
        public string CurrentAreaKey;
        public float Strength;
        public float Readiness = 1f;
    }

    public sealed class ArmyAreaAssignment
    {
        public string UnitKey;
        public string UnitName;
        public string CommanderName;
        public ArmyAreaDoctrine Doctrine;
        public string CurrentAreaKey;
        public string AssignedAreaKey;
        public ArmyAreaBehavior Behavior;
        public bool OutOfArea;
        public string Reason;
    }
}
