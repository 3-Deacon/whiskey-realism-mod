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
        // MassRatioModifier -> G3 required ratio, MinimumHoldRatioModifier -> G4 hold ratio.
        // Nullable: DirectorMemories[alliance].LastPosture is null until PublishDirectorPosture has
        // run once (the first review after a reset, or a CIC player's alliance); G3/G4 treat null as
        // neutral (both modifiers 0).
        public DirectorPosture Tempo { get; private set; }

        public float CapitalX, CapitalZ;
        public bool HasCapital;
        public float RelocationRange;       // GamePrefs.aimaximumdistancetosearchforunitrelocations
        // GamePrefs.theatersize is a Vector2 (49166): IsWithinOperationsTheater tests x against
        // theatersize.x and z against theatersize.y (17105).
        public float TheaterHalfExtentX, TheaterHalfExtentZ;
        public float OffensiveDominance;    // GamePrefs.aidominanceforoffensiveoperations
        public float MinStrengthForMovement, MinMoraleForMovement; // aiminimumstrength/moraleformovement

        public List<FormationRecord> Own { get; } = new List<FormationRecord>();
        public List<AreaRecord> Areas { get; } = new List<AreaRecord>();
        public List<ObjectiveRecord> Objectives { get; } = new List<ObjectiveRecord>();
        // How many of Objectives the mapped-ground branch built (ObjectiveGround, G3c): scenario 001's
        // objectives have empty target lists, so these records stand in for them.
        public int MappedObjectiveCount;
        public List<SupplySourceRecord> Supply { get; } = new List<SupplySourceRecord>();
        public List<RailLineRecord> Rails { get; } = new List<RailLineRecord>();
        public List<CommanderRecord> Commanders { get; } = new List<CommanderRecord>();
        public List<RankBand> RankBands { get; } = new List<RankBand>();             // 10..18
        public List<ArmyGroupRecord> ArmyGroups { get; } = new List<ArmyGroupRecord>(); // G2 appointment targets
        public List<CrossingRecord> Crossings { get; } = new List<CrossingRecord>(); // fords and bridges (G3)
        public List<EnemyDetection> Detections { get; } = new List<EnemyDetection>(); // G1 EnemyDetectionRuntime
        public List<EnemyBelief> Beliefs { get; } = new List<EnemyBelief>();          // G1 EnemyArmyModel

        internal void Reset(int alliance, int factionIndex, int daySerial, PlayerCommandClass player,
                            int playerCommandInstanceId, DirectorPosture tempo)
        {
            Alliance = alliance;
            FactionIndex = factionIndex;
            DaySerial = daySerial;
            Player = player;
            PlayerCommandInstanceId = playerCommandInstanceId;
            Tempo = tempo;

            CapitalX = CapitalZ = 0f;
            HasCapital = false;
            RelocationRange = 0f;
            TheaterHalfExtentX = TheaterHalfExtentZ = 0f;
            OffensiveDominance = 0f;
            MinStrengthForMovement = MinMoraleForMovement = 0f;

            Own.Clear();
            Areas.Clear();
            Objectives.Clear();
            MappedObjectiveCount = 0;
            Supply.Clear();
            Rails.Clear();
            Commanders.Clear();
            RankBands.Clear();
            ArmyGroups.Clear();
            Crossings.Clear();
            Detections.Clear();
            Beliefs.Clear();
        }

        public int IndexOfOwn(int instanceId)
        {
            for (int i = 0; i < Own.Count; i++)
                if (Own[i].InstanceId == instanceId) return i;
            return -1;
        }

        // The unit key of a formation's command parent (the D5 rule's ParentInstanceId), null for a root or an
        // unknown formation. The one place the command hierarchy is read for the directive ledger.
        public string ParentKeyOf(int instanceId)
        {
            for (int i = 0; i < Own.Count; i++)
            {
                if (Own[i].InstanceId != instanceId) continue;
                int parent = Own[i].ParentInstanceId;
                if (parent < 0) return null;
                for (int j = 0; j < Own.Count; j++)
                    if (Own[j].InstanceId == parent) return Own[j].UnitKey;
                return null;
            }
            return null;
        }

        // The sidecar's rebinding rule (G6): the exact unit key, else the one formation whose name is the key's name part when
        // the commander part no longer matches; -1 when there is none or the name is ambiguous.
        public int IndexOfKeyOrName(string unitKey)
        {
            int exact = IndexOfKey(unitKey);
            if (exact >= 0 || string.IsNullOrEmpty(unitKey)) return exact;
            string name = EnemyArmyModel.NameOf(unitKey);
            int found = -1;
            for (int i = 0; i < Own.Count; i++)
            {
                if (!string.Equals(Own[i].Name, name, StringComparison.Ordinal)) continue;
                if (found >= 0) return -1;
                found = i;
            }
            return found;
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

        // One target's term of vanilla UpdateImportanceValues (14905-14995) for one faction:
        //   IIP:  transport capacity x aiimportancetransportcapacityweight x num   (14918)
        //   town: representingpopulation x aiimportancepopulation x num           (14959)
        //   num = 1 when the holder is at war with the faction, else
        //         nonwarparticipantsobjectimportanceweighting (0.35, campaignprefs 1400-1401), so own
        //         and allied targets count 0.35 (14917, 14958)
        //   + aitownweightfactorobjective when the target is part of the faction's
        //     followedcampaignobjective (IsPartOfObjective, 14919-14922, 14960-14962), not x num
        //   + aiimportancevaluescapital for a town that is alliance l's capital, added at array index
        //     l for l = 0, 1 (14970-14976): vanilla indexes the faction-indexed array by alliance
        //     id there, so the bonus goes to the faction whose index equals the capital's alliance.
        internal static float TargetImportance(bool isTown, float population, float transportCapacity, bool holderAtWar,
                                               bool partOfFollowedObjective, bool capitalOfFactionIndex, float populationWeight,
                                               float transportWeight, float nonWarWeight, float objectiveBonus, float capitalBonus)
        {
            float num = holderAtWar ? 1f : nonWarWeight;
            float value = isTown ? population * populationWeight * num : transportCapacity * transportWeight * num;
            if (partOfFollowedObjective) value += objectiveBonus;
            if (isTown && capitalOfFactionIndex) value += capitalBonus;
            return value;
        }

        internal static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value <= 0f) return 0f;
            return value >= 1f ? 1f : value;
        }
    }

    // The identity of a vanilla list that is built once per load and only replaced by the next
    // load: BattleUnits.railroad (ImportRailroads 82506-82516), AICampaign.aiarea
    // (InitializeAIAreas 11272), BattlefieldSetup.rivercrossingpositions (26628-26638). A cache
    // built from the list stays valid while the same list object holds the same count.
    internal struct StaticSourceKey
    {
        private object _list;
        private int _count;

        internal bool Matches(object list, int count)
        {
            return list != null && ReferenceEquals(_list, list) && _count == count;
        }

        internal void Set(object list, int count)
        {
            _list = list;
            _count = count;
        }

        internal void Clear()
        {
            _list = null;
            _count = 0;
        }
    }

    // The campaign map's railroad geometry, read once per load: each line's IIPs
    // (ConstructionSymbol.IIPsForTransportCapacityUpdate, 188134, nulls dropped, array order kept)
    // with their instance ids and positions. Nothing here depends on ownership or construction, so
    // only a new railroad list invalidates it. Answers what the G0 snapshot scanned every review:
    // how many lines list an IIP, a line's two farthest-apart IIPs, and how many lines pass within
    // a radius of a town (memoized per town and radius).
    internal sealed class RailGeometry
    {
        private readonly List<int> _lineStart = new List<int>();
        private readonly List<int> _ids = new List<int>();
        private readonly List<float> _x = new List<float>();
        private readonly List<float> _z = new List<float>();
        private readonly List<RailLineRecord> _ends = new List<RailLineRecord>();
        private readonly Dictionary<int, int> _linesOfIip = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _linesNearTown = new Dictionary<int, int>();
        private float _townRadius = float.NaN;
        private int _open = -1;

        internal void Clear()
        {
            _lineStart.Clear();
            _ids.Clear();
            _x.Clear();
            _z.Clear();
            _ends.Clear();
            _linesOfIip.Clear();
            _linesNearTown.Clear();
            _townRadius = float.NaN;
            _open = -1;
        }

        // One call per railroad index, null lines included (as an empty line), so line i here is
        // BattleUnits.railroad[i].
        internal void BeginLine()
        {
            _open = _ids.Count;
            _lineStart.Add(_open);
        }

        internal void AddIip(int iipId, float x, float z)
        {
            _ids.Add(iipId);
            _x.Add(x);
            _z.Add(z);
            _linesOfIip.TryGetValue(iipId, out int count);
            _linesOfIip[iipId] = count + 1;
        }

        // The line's two farthest-apart IIPs (the array is not ordered along the track); the
        // first pair found wins a tie, a one-IIP line has both ends on it, an empty line none.
        internal void EndLine()
        {
            int start = _open, end = _ids.Count;
            var ends = new RailLineRecord { AIipId = -1, BIipId = -1 };
            float best = -1f;
            for (int a = start; a < end; a++)
            {
                for (int b = a; b < end; b++)
                {
                    float dx = _x[a] - _x[b];
                    float dz = _z[a] - _z[b];
                    float d = dx * dx + dz * dz;
                    if (d <= best) continue;
                    best = d;
                    ends.AX = _x[a];
                    ends.AZ = _z[a];
                    ends.BX = _x[b];
                    ends.BZ = _z[b];
                    ends.AIipId = _ids[a];
                    ends.BIipId = _ids[b];
                }
            }
            _ends.Add(ends);
            _linesNearTown.Clear();
            _open = -1;
        }

        internal void CopyEndpoints(int line, ref RailLineRecord record)
        {
            if (line < 0 || line >= _ends.Count) return;
            var ends = _ends[line];
            record.AX = ends.AX;
            record.AZ = ends.AZ;
            record.BX = ends.BX;
            record.BZ = ends.BZ;
            record.AIipId = ends.AIipId;
            record.BIipId = ends.BIipId;
        }

        // How many lines list the IIP (once per listing, as vanilla's arrays hold it).
        internal int LinesOfIip(int iipId)
        {
            return _linesOfIip.TryGetValue(iipId, out int count) ? count : 0;
        }

        // A town lies on a line when one of the line's IIPs is within the radius
        // (GamePrefs.radiustowncentertocapture, the radius vanilla captures a town within).
        internal int LinesNearTown(int townId, float x, float z, float radius)
        {
            if (radius <= 0f) return 0;
            if (radius != _townRadius)
            {
                _linesNearTown.Clear();
                _townRadius = radius;
            }
            if (_linesNearTown.TryGetValue(townId, out int memo)) return memo;

            float radiusSq = radius * radius;
            int count = 0;
            for (int line = 0; line < _lineStart.Count; line++)
            {
                int end = line + 1 < _lineStart.Count ? _lineStart[line + 1] : _ids.Count;
                for (int k = _lineStart[line]; k < end; k++)
                {
                    float dx = _x[k] - x;
                    float dz = _z[k] - z;
                    if (dx * dx + dz * dz > radiusSq) continue;
                    count++;
                    break;
                }
            }
            _linesNearTown[townId] = count;
            return count;
        }
    }
}
