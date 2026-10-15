using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // One alliance's picture of the campaign for one daily review, built by
    // CampaignSnapshotRuntime.Build from one pass over vanilla state. Pooled: Reset clears every
    // list in place for the next build. Own side only plus public map facts (objectives, supply
    // sources, rails); enemy units enter only as Detections (G1) and Beliefs (G1), never as a
    // vanilla enemy aggregate (spec C25).
    public sealed class CampaignSnapshot
    {
        public int Alliance { get; private set; }
        public int FactionIndex { get; private set; }
        public int DaySerial { get; private set; }
        public PlayerCommandClass Player { get; private set; }
        public int PlayerCommandInstanceId { get; private set; } // -1 none; G2's no-vertical-swap check
        public bool UseFog { get; private set; }
        // MassRatioModifier -> G3 required ratio, MinimumHoldRatioModifier -> G4 hold ratio.
        public DirectorPosture Tempo { get; private set; }

        public float CapitalX, CapitalZ;
        public bool HasCapital;
        public float EnemyCapitalX, EnemyCapitalZ;
        public bool HasEnemyCapital;
        public float RelocationRange;       // GamePrefs.aimaximumdistancetosearchforunitrelocations
        // GamePrefs.theatersize is a Vector2 (49166): IsWithinOperationsTheater tests x against
        // theatersize.x and z against theatersize.y (17105).
        public float TheaterHalfExtentX, TheaterHalfExtentZ;
        public float OffensiveDominance;    // GamePrefs.aidominanceforoffensiveoperations
        public float MinStrengthForMovement, MinMoraleForMovement; // aiminimumstrength/moraleformovement

        public List<FormationRecord> Own { get; } = new List<FormationRecord>();
        public List<AreaRecord> Areas { get; } = new List<AreaRecord>();
        public List<ObjectiveRecord> Objectives { get; } = new List<ObjectiveRecord>();
        public List<SupplySourceRecord> Supply { get; } = new List<SupplySourceRecord>();
        public List<RailLineRecord> Rails { get; } = new List<RailLineRecord>();
        public List<CommanderRecord> Commanders { get; } = new List<CommanderRecord>();
        public List<RankBand> RankBands { get; } = new List<RankBand>();             // 10..18
        public List<EnemyDetection> Detections { get; } = new List<EnemyDetection>(); // G1 EnemyDetectionRuntime
        public List<EnemyBelief> Beliefs { get; } = new List<EnemyBelief>();          // G1 EnemyArmyModel

        internal void Reset(int alliance, int factionIndex, int daySerial, PlayerCommandClass player,
                            int playerCommandInstanceId, bool useFog, DirectorPosture tempo)
        {
            Alliance = alliance;
            FactionIndex = factionIndex;
            DaySerial = daySerial;
            Player = player;
            PlayerCommandInstanceId = playerCommandInstanceId;
            UseFog = useFog;
            Tempo = tempo;

            CapitalX = CapitalZ = 0f;
            HasCapital = false;
            EnemyCapitalX = EnemyCapitalZ = 0f;
            HasEnemyCapital = false;
            RelocationRange = 0f;
            TheaterHalfExtentX = TheaterHalfExtentZ = 0f;
            OffensiveDominance = 0f;
            MinStrengthForMovement = MinMoraleForMovement = 0f;

            Own.Clear();
            Areas.Clear();
            Objectives.Clear();
            Supply.Clear();
            Rails.Clear();
            Commanders.Clear();
            RankBands.Clear();
            Detections.Clear();
            Beliefs.Clear();
        }

        public int IndexOfOwn(int instanceId)
        {
            for (int i = 0; i < Own.Count; i++)
                if (Own[i].InstanceId == instanceId) return i;
            return -1;
        }

        // Sidecar rebinding (G6): a persisted unitKey back to this session's formation.
        public int IndexOfKey(string unitKey)
        {
            if (string.IsNullOrEmpty(unitKey)) return -1;
            for (int i = 0; i < Own.Count; i++)
                if (string.Equals(Own[i].UnitKey, unitKey, StringComparison.Ordinal)) return i;
            return -1;
        }
    }

    // Pure halves of the snapshot's per-formation reads, so the vanilla rules they mirror are
    // harness-tested; CampaignSnapshotRuntime passes the vanilla fields in.
    internal static class SnapshotRules
    {
        // The sidecar's persisted identity (FormationDirectiveRuntime.UnitKey).
        internal static string UnitKey(string name, int commander)
        {
            return name + ":" + commander.ToString();
        }

        // Vanilla GetGroupSupplyState (14063-14066) = max(avg(groupsupplystate[0..3]) * 0.5,
        // supplystate * 0.5): both inputs are 0..1, so it never exceeds 0.5. The planner keeps the
        // raw value; every Whiskey threshold is stated on it (spec C20).
        internal static float RawSupply01(float[] groupSupplyState, float supplyState)
        {
            float group = 0f;
            if (groupSupplyState != null && groupSupplyState.Length >= 4)
                group = (groupSupplyState[0] + groupSupplyState[1] + groupSupplyState[2] + groupSupplyState[3]) / 4f;
            return Clamp01(Math.Max(group, supplyState));
        }

        // Embarking (embarkmentstarttime set by SetEmbarkmentMode, 116348-116360) or afloat
        // (currentmodeltype 1 sea / 2 river, CheckModelReplacement 116383-116417; vanilla's own
        // afloat test at 80724).
        internal static bool IsEmbarked(float embarkmentStartTime, int currentModelType)
        {
            return embarkmentStartTime > 0f || currentModelType == 1 || currentModelType == 2;
        }

        // AICampaign.UnitIsFightingForce (16867-16890) without its UpdateGroupStrength(overridecalc)
        // call for armies: the snapshot reads the last computed groupstrengthdirect and writes nothing.
        internal static bool IsFightingForce(int unitTyp, bool hasDirectCorpsChild, float groupStrengthDirect, float minStrengthForMovement)
        {
            if (unitTyp == 15) return true;
            if (unitTyp < 15 || unitTyp >= 17) return false;
            return !(hasDirectCorpsChild && groupStrengthDirect < minStrengthForMovement);
        }

        // Cavalry (unittyp 1) share of the group's direct strength per unit type, the array
        // vanilla sums at 14213.
        internal static float CavalryShare(int[] strengthPerUnitType)
        {
            if (strengthPerUnitType == null || strengthPerUnitType.Length < 2) return 0f;
            float total = 0f;
            for (int i = 0; i < strengthPerUnitType.Length; i++)
                total += Math.Max(0, strengthPerUnitType[i]);
            return total > 0f ? Math.Max(0, strengthPerUnitType[1]) / total : 0f;
        }

        // Capital = the town is an alliance capital; Port = a harbour IIP (IIPType 1, 103717);
        // RailHub = the target lies on two or more railroad lines; Industry = a factory, foundry,
        // ironworks or manufactory (95107-95113) has it as its closest town.
        internal static ObjectiveRole ObjectiveRoles(bool isCapital, bool isHarbor, int railLines, bool hasIndustry)
        {
            var roles = ObjectiveRole.None;
            if (isCapital) roles |= ObjectiveRole.Capital;
            if (isHarbor) roles |= ObjectiveRole.Port;
            if (railLines >= 2) roles |= ObjectiveRole.RailHub;
            if (hasIndustry) roles |= ObjectiveRole.Industry;
            return roles;
        }

        internal static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value <= 0f) return 0f;
            return value >= 1f ? 1f : value;
        }
    }
}
