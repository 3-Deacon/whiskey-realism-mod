using System;

namespace WhiskeyRealism.Strategic
{
    public sealed class FormationSnapshot
    {
        public string UnitKey;
        public string ParentUnitKey;
        public int AllianceId;
        public int StableUnitId;
        public string UnitName;
        public string CommanderName;
        public int UnitType;
        public bool IsTopUnit;
        public bool IsGarrisoned;
        public bool GrandArmyStructureAvailable;
        public string AreaKey;
        public string SectorKey;
        public float X;
        public float Z;
        public float GroupStrengthActive;
        public float GroupStrengthDirect;
        public float Morale = 1f;
        public float Readiness = 1f;
        public float RifleAmmo = 1f;
        public float ArtilleryAmmo = 1f;
        public float Supply = 1f;
        public float Fatigue;
        public float WeaponFirepower = 1f;
        public float CommandRange;
        public float BugleRange;
        public bool InBattle;
        public bool OnRetreat;
        public bool HasActivePath;
        public bool IsCavalryCapable;
        public FormationLevel VisibleEnemyLevel = FormationLevel.Unknown;
        public float LocalEnemyStrength;
        public float LocalEnemyExchangePressure;
        public float LocalFriendlySupportStrength;
        public bool SupportCanReach;
        public bool IsPlanTargetArea;
        public bool IsCriticalSector;
        public FrontPosture FrontPosture = FrontPosture.Hold;

        public FormationLevel Level => LevelFromUnitType(UnitType);

        public bool IsAttachedDivision =>
            UnitType == 14 && !IsTopUnit && !string.IsNullOrEmpty(ParentUnitKey);

        public bool IsIndependentTopDivision =>
            UnitType == 14 && IsTopUnit && !IsGarrisoned && GroupStrengthDirect > 1000f;

        public bool IsTopStrategicFormation =>
            !IsGarrisoned && IsTopUnit && UnitType >= 14 && UnitType <= 16;

        public bool CanReceiveDirectDirective =>
            IsTopStrategicFormation;

        public bool CanReceiveDirectMovement =>
            IsTopStrategicFormation;

        public float MinimumAmmo =>
            Math.Min(Clamp01(RifleAmmo), Clamp01(ArtilleryAmmo));

        public static FormationLevel LevelFromUnitType(int unitType)
        {
            if (unitType == 14) return FormationLevel.Division;
            if (unitType == 15) return FormationLevel.Corps;
            if (unitType == 16) return FormationLevel.Army;
            return FormationLevel.Unknown;
        }

        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
