using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // Builds one alliance's CampaignSnapshot for the daily review (plan G0, P2) from one pass over
    // vanilla state. Reads only; writes nothing vanilla. Runtime adapter: not in the test csproj.
    //
    // Own formations mirror AIFaction.SeparateUnits (10458-10515): BattleUnits.campaignunitlist
    // (79974-79994; visibility state != 0, GetUnitVisibilityStateOnCampaignMap 82934-82974) units
    // of this alliance that are active, have no garrisonreference and are not fleets. Busy flags
    // come from the faction's operation, depot and capital lists and the vanilla helpers that
    // IsUnitAvailableForOffensiveOperations (14080-14158) consults (FortConstructionOrder
    // .UnitAlreadyConstructing 9785, SeaInvasionForce.GetSeaInvasionForceReference 9296,
    // RaidForce.IsRaidUnit 8937), MoveQueued from DefensiveMovingOrder.OrderRunning (9933).
    // FightingForce mirrors UnitIsFightingForce (16867) without its group-strength recompute.
    // Areas copy only the own-side faction-indexed fields (importancevalues, ownpoints) and
    // supplycapacity (UpdateSupplyCapacity 10772-10802), averageposition and zoneedges; per-faction
    // arrays are sized by alliance count but indexed by faction index (10679-10692), so every read
    // is bound-checked. Vanilla's area aggregates of the other side are never copied (spec C25).
    // Enemy units are not read here at all: G1's EnemyDetectionRuntime fills Detections.
    //
    // Objectives: one record per Town / IIP target of every active objective of either alliance
    // (CampaignObjective.allcampaignobjectives 178548; scenario match and IsDeactivated as
    // GetAvailableObjectives 178827-178866 filters; IDAccomplished 178805). Importance is vanilla's
    // own per-target term for this faction (SnapshotRules.TargetImportance, UpdateImportanceValues
    // 14905-14995), normalized to the snapshot's largest. Garrison strength only for own and allied
    // forts: an enemy fort's garrison reaches the side only as a G1 belief (spec C14/C25).
    // Supply sources: every IIP (owner kept; LogisticsGate accepts own, non-invisible ones) and the
    // supply depots of this alliance or an ally (UpdateIIPSupplyLink 114272-114306). Crossings: the
    // ford and bridge cells of the campaign terrain grid (BattlefieldSetup.rivercrossingpositions,
    // 26628-26638). Commander records are read once per alliance and review day.
    // Rails: BattleUnits.railroad with StartConstruction(alliance, checkonly: true) (77818-77845).
    // Commanders: this alliance's GameVars.commander records with the live getters at their default
    // arguments (as vanilla 14250 calls them) and GetAverageAttributeValue (59175). Rank bands:
    // GetMin/MaxRankForUnittyp (67003-67058; checkforpromotion false, as CheckSuitableRank 60579).
    internal static class CampaignSnapshotRuntime
    {
        private const int FactoryId = 5, FoundryId = 8, IronworksId = 10, ManufactoryId = 12; // CBuilding ids 95107-95113

        private static FieldInfo _offensiveField, _defensiveField, _depotBuildersField, _capitalField, _followedField;
        private static Type _factionType;
        private static Weather _weather;
        private static BattlefieldSetup _bfs;
        private static readonly List<CommanderRecord>[] CommanderCache = { new List<CommanderRecord>(), new List<CommanderRecord>() };
        private static readonly int[] CommanderCacheDay = { -1, -1 };

        static CampaignSnapshotRuntime()
        {
            CampaignScope.Register("campaign-planner-snapshot-cache", ResetCaches);
        }

        private static void ResetCaches()
        {
            for (int a = 0; a < CommanderCache.Length; a++)
            {
                CommanderCache[a].Clear();
                CommanderCacheDay[a] = -1;
            }
            _weather = null;
            _bfs = null;
        }

        private static readonly HashSet<Regiment> OffensiveSet = new HashSet<Regiment>();
        private static readonly HashSet<Regiment> DefensiveSet = new HashSet<Regiment>();
        private static readonly HashSet<Regiment> DepotBuilderSet = new HashSet<Regiment>();
        private static readonly HashSet<Regiment> CapitalSet = new HashSet<Regiment>();
        private static readonly List<Regiment> OwnUnits = new List<Regiment>();
        private static readonly Dictionary<Regiment, int> OwnIndex = new Dictionary<Regiment, int>();
        private static readonly List<int> ParentSize = new List<int>();
        private static readonly HashSet<int> AvailableObjectives = new HashSet<int>();
        private static readonly HashSet<Town> IndustryTowns = new HashSet<Town>();
        private static readonly Dictionary<UnityEngine.Object, float> GarrisonAt = new Dictionary<UnityEngine.Object, float>();
        private static readonly Dictionary<IIP, int> RailLinesOfIip = new Dictionary<IIP, int>();
        private static readonly List<float> RawImportance = new List<float>();

        internal static CampaignSnapshot Build(int alliance, int factionIndex, int daySerial, PlayerCommandClass player,
                                               in WlChainContext chain, DirectorPosture tempo, CampaignSnapshot reuse)
        {
            var s = reuse ?? new CampaignSnapshot();
            int playerCommand = (UnityEngine.Object)chain.Command != null ? chain.Command.GetInstanceID() : -1;
            s.Reset(alliance, factionIndex, daySerial, player, playerCommand, GameVars.usefow, tempo);
            try
            {
                object faction = AICampaignReflect.GetFaction(factionIndex);
                if (faction == null || !ResolveFactionFields(faction.GetType()))
                {
                    OnceLog.Warning("campaign-planner:snapshot-degraded", "[CampaignPlanner] AIFaction lists not readable; snapshot left empty");
                    return s;
                }
                ReadScalars(s, alliance);
                ReadAreas(s, factionIndex);
                ReadFormations(s, alliance, faction, in chain);
                ReadObjectives(s, alliance, faction);
                ReadSupply(s, alliance);
                ReadRails(s, alliance);
                ReadCrossings(s);
                ReadCommanders(s, alliance);
                ReadRankBands(s, alliance);
            }
            catch (Exception ex)
            {
                s.Reset(alliance, factionIndex, daySerial, player, playerCommand, GameVars.usefow, tempo);
                OnceLog.Warning("campaign-planner:snapshot-degraded", "[CampaignPlanner] snapshot build failed, snapshot left empty: " + ex.Message);
            }
            return s;
        }

        // AIFaction is a private nested class (10299); its lists are public fields.
        private static bool ResolveFactionFields(Type factionType)
        {
            if (_factionType != factionType)
            {
                _factionType = factionType;
                _offensiveField = AccessTools.Field(factionType, "unitsinoffensiveoperations");
                _defensiveField = AccessTools.Field(factionType, "unitsindefensiveoperations");
                _depotBuildersField = AccessTools.Field(factionType, "unitsconstructingsupplydepots");
                _capitalField = AccessTools.Field(factionType, "groupstodefendcapital");
            }
            return _offensiveField != null && _defensiveField != null && _depotBuildersField != null && _capitalField != null;
        }

        private static void ReadScalars(CampaignSnapshot s, int alliance)
        {
            var alliances = GameVars.alliance;
            if (alliances != null)
            {
                if (alliance >= 0 && alliance < alliances.Length && (UnityEngine.Object)alliances[alliance].capital != null)
                {
                    Vector3 own = ((Component)alliances[alliance].capital).transform.position;
                    s.CapitalX = own.x;
                    s.CapitalZ = own.z;
                    s.HasCapital = true;
                }
                int enemy = 1 - alliance; // the planner runs for alliances 0 and 1 only
                if (enemy >= 0 && enemy < alliances.Length && (UnityEngine.Object)alliances[enemy].capital != null)
                {
                    Vector3 other = ((Component)alliances[enemy].capital).transform.position;
                    s.EnemyCapitalX = other.x;
                    s.EnemyCapitalZ = other.z;
                    s.HasEnemyCapital = true;
                }
            }
            s.RelocationRange = GamePrefs.aimaximumdistancetosearchforunitrelocations;
            s.TheaterHalfExtentX = GamePrefs.theatersize.x;
            s.TheaterHalfExtentZ = GamePrefs.theatersize.y;
            s.OffensiveDominance = GamePrefs.aidominanceforoffensiveoperations;
            s.MinStrengthForMovement = GamePrefs.aiminimumstrengthformovement;
            s.MinMoraleForMovement = GamePrefs.aiminimummoraleformovement;
        }

        private static void ReadAreas(CampaignSnapshot s, int f)
        {
            var areas = AICampaign.aiarea;
            if (areas == null) return;
            for (int i = 0; i < areas.Count; i++)
            {
                var area = areas[i];
                if (area == null) continue;
                var edges = area.zoneedges;
                int edgeCount = edges != null ? edges.Length : 0;
                var record = new AreaRecord
                {
                    Index = i,
                    X = area.averageposition.x,
                    Z = area.averageposition.z,
                    EdgeX = new float[edgeCount],
                    EdgeZ = new float[edgeCount],
                    Importance = At(area.importancevalues, f),
                    OwnPoints = At(area.ownpoints, f),
                    SupplyCapacity = area.supplycapacity
                };
                for (int e = 0; e < edgeCount; e++)
                {
                    record.EdgeX[e] = edges[e].x;
                    record.EdgeZ[e] = edges[e].z;
                }
                s.Areas.Add(record);
            }
        }

        private static void ReadFormations(CampaignSnapshot s, int alliance, object faction, in WlChainContext chain)
        {
            Fill(OffensiveSet, _offensiveField.GetValue(faction) as IList);
            Fill(DefensiveSet, _defensiveField.GetValue(faction) as IList);
            Fill(DepotBuilderSet, _depotBuildersField.GetValue(faction) as IList);
            Fill(CapitalSet, _capitalField.GetValue(faction) as IList);

            OwnUnits.Clear();
            OwnIndex.Clear();
            var units = BattleUnits.campaignunitlist;
            if (units == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                var reg = units[i];
                if ((UnityEngine.Object)reg == null || reg.alliance != alliance || reg.unittyp == 17) continue;
                if ((UnityEngine.Object)reg.garrisonreference != null || !((Component)reg).gameObject.activeInHierarchy) continue;
                if (BattleUnits.GetUnitVisibilityStateOnCampaignMap(reg) == 0) continue;
                OwnIndex[reg] = OwnUnits.Count;
                OwnUnits.Add(reg);
            }

            bool fixPerkIds = FixPerkIds();
            float layDown = GamePrefs.fow_lineofsightreduction_layndown;
            float rainSnow = GamePrefs.influenceofrainsnowonvisibilitycampaign;
            bool career = DLC_WL.dlc_scenarioactive;
            Weather weather = ResolveWeather();
            for (int i = 0; i < OwnUnits.Count; i++)
            {
                var reg = OwnUnits[i];
                try
                {
                    Vector3 position = reg.GetPosition();
                    Vector3 waypoint = reg.GetLastWaypointPos();
                    int unitTyp = reg.unittyp;
                    float los = At(GamePrefs.maximumlineofsight, unitTyp);
                    bool hasEight = reg.HasPerk(VanillaPerkTables.BattleExpertScouts);
                    float eightFactor = hasEight ? At(GamePrefs.perk_expertscoutsvisibilityrangefactor, reg.GetPerkLevel(VanillaPerkTables.BattleExpertScouts)) : 1f;
                    float climate = (UnityEngine.Object)weather != null ? weather.GetClimateData(((Component)reg).transform.position, 105) : 0f;
                    float incident63 = career && reg.dlcw_isundercommander && DLC_WL.IsIncidentActive(63) ? GamePrefs.dlcwl_incidentscoutingaheadrange : 1f;
                    bool hasDirectCorps = unitTyp == 16
                        && reg.GetAttachedUnitsReg(excludedechainedunits: false, excludeskirmishers: true, searchonlytype: 15, directonly: true, includenonactiveunits: false).Length != 0;

                    s.Own.Add(new FormationRecord
                    {
                        InstanceId = reg.GetInstanceID(),
                        UnitKey = SnapshotRules.UnitKey(((UnityEngine.Object)reg).name, reg.commander),
                        UnitTyp = unitTyp,
                        X = position.x,
                        Z = position.z,
                        WaypointX = waypoint.x,
                        WaypointZ = waypoint.z,
                        Strength = reg.groupstrengthdirect,
                        Morale = reg.groupmorale,
                        Supply01 = SnapshotRules.RawSupply01(reg.groupsupplystate, reg.supplystate),
                        ReadinessStep = CampaignArmyPanel.GetReadinessStep(reg),
                        CommandRange = reg.commanderrange,
                        BugleRange = reg.buglerange,
                        CommanderId = reg.commander,
                        HasTheater = reg.theaterposition != default(Vector3),
                        TheaterX = reg.theaterposition.x,
                        TheaterZ = reg.theaterposition.z,
                        AreaIndex = AreaIndexAt(s, position.x, position.z),
                        Flags = FlagsOf(reg, unitTyp, hasDirectCorps, s.MinStrengthForMovement),
                        Name = ((UnityEngine.Object)reg).name,
                        Chain = WlStrategicOrderBridge.ChainClassOf(in chain, alliance, reg),
                        CavalryShare = SnapshotRules.CavalryShare(reg.groupstrengthperunittypcampaigndirect),
                        SightRadius = SightRadiusFormula.Campaign(unitTyp, los, hasEight, eightFactor, climate, rainSnow,
                            reg.liedown, layDown, incident63, fixPerkIds),
                        ParentInstanceId = -1,
                        UseRails = reg.userails,
                        UseRivers = reg.userivers
                    });
                }
                catch (Exception ex)
                {
                    OwnIndex.Remove(reg);
                    OnceLog.Warning("campaign-planner:snapshot-unit-skipped", "[CampaignPlanner] a formation could not be read and was left out: " + ex.Message);
                }
            }
            ResolveParents(s);
        }

        private static FormationFlags FlagsOf(Regiment reg, int unitTyp, bool hasDirectCorps, float minStrength)
        {
            var flags = FormationFlags.None;
            if (reg.inbattle) flags |= FormationFlags.InBattle;
            if (reg.onretreat) flags |= FormationFlags.OnRetreat;
            if (OffensiveSet.Contains(reg)) flags |= FormationFlags.InOffensiveOps;
            if (DefensiveSet.Contains(reg)) flags |= FormationFlags.InDefensiveOps;
            if (CapitalSet.Contains(reg)) flags |= FormationFlags.CapitalDefense;
            if (DepotBuilderSet.Contains(reg)) flags |= FormationFlags.VanillaDepotBuilder;
            if (AICampaign.FortConstructionOrder.UnitAlreadyConstructing(reg)) flags |= FormationFlags.FortBuilder;
            if (AICampaign.SeaInvasionForce.GetSeaInvasionForceReference(reg) != null) flags |= FormationFlags.SeaInvasion;
            if (AICampaign.RaidForce.IsRaidUnit(reg)) flags |= FormationFlags.RaidForce;
            if (reg.transfertime > 0f) flags |= FormationFlags.InTransfer;
            if (SnapshotRules.IsEmbarked(reg.embarkmentstarttime, reg.currentmodeltype)) flags |= FormationFlags.Embarked;
            if (SnapshotRules.IsFightingForce(unitTyp, hasDirectCorps, reg.groupstrengthdirect, minStrength)) flags |= FormationFlags.FightingForce;
            if (reg.GetLastTransmittedPath() >= 0) flags |= FormationFlags.HasPath;
            if (AICampaign.DefensiveMovingOrder.OrderRunning(reg)) flags |= FormationFlags.MoveQueued;
            return flags;
        }

        // The battle core's D5 rule on the campaign: a formation's parent is the own formation whose
        // allattachedunits (every descendant, 119840+) contains it with the fewest entries. Replaces
        // any transform.parent walk (AGENTS.md).
        private static void ResolveParents(CampaignSnapshot s)
        {
            ParentSize.Clear();
            for (int i = 0; i < s.Own.Count; i++) ParentSize.Add(int.MaxValue);
            for (int g = 0; g < OwnUnits.Count; g++)
            {
                var group = OwnUnits[g];
                if (!OwnIndex.TryGetValue(group, out int groupIndex)) continue;
                var descendants = group.allattachedunits;
                if (descendants == null) continue;
                for (int k = 0; k < descendants.Length; k++)
                {
                    var child = descendants[k];
                    if ((UnityEngine.Object)child == null || child == group || !OwnIndex.TryGetValue(child, out int childIndex)) continue;
                    if (descendants.Length >= ParentSize[childIndex]) continue;
                    ParentSize[childIndex] = descendants.Length;
                    var record = s.Own[childIndex];
                    record.ParentInstanceId = s.Own[groupIndex].InstanceId;
                    s.Own[childIndex] = record;
                }
            }
        }

        private static int AreaIndexAt(CampaignSnapshot s, float x, float z)
        {
            int position = AreaGeometry.AreaAt(s.Areas, x, z);
            return position >= 0 ? s.Areas[position].Index : -1;
        }

        private static void ReadObjectives(CampaignSnapshot s, int alliance, object faction)
        {
            var objectives = CampaignObjective.allcampaignobjectives;
            if (objectives == null) return;

            AvailableObjectives.Clear();
            var available = CampaignObjective.GetAvailableObjectives(alliance);
            if (available != null)
                for (int i = 0; i < available.Count; i++)
                    if ((UnityEngine.Object)available[i] != null) AvailableObjectives.Add(available[i].UniqueObjectiveID);

            IndustryTowns.Clear();
            GarrisonAt.Clear();
            var buildings = BattleUnits.cbuildings;
            if (buildings != null)
            {
                for (int i = 0; i < buildings.Count; i++)
                {
                    var b = buildings[i];
                    if ((UnityEngine.Object)b == null) continue;
                    int type = b.BuildingType;
                    if ((type == FactoryId || type == FoundryId || type == IronworksId || type == ManufactoryId) && (UnityEngine.Object)b.closesttown != null)
                        IndustryTowns.Add(b.closesttown);
                }
            }
            var forts = BattleUnits.fort;
            if (forts != null)
            {
                for (int i = 0; i < forts.Count; i++)
                {
                    var fort = forts[i];
                    if ((UnityEngine.Object)fort == null) continue;
                    if (!SnapshotRules.SideKnowsGarrison(alliance, fort.Owner, AlliedWith(alliance, fort.Owner))) continue;
                    if ((UnityEngine.Object)fort.garrisonunit == null) continue;
                    float strength = fort.garrisonunit.groupstrengthdirect;
                    AddGarrison(fort.closesttown, strength);
                    AddGarrison((UnityEngine.Object)fort.parentiip != null ? fort.parentiip : fort.closestiip, strength);
                }
            }
            CountRailLines();

            int followed = FollowedObjective(faction);
            int f = s.FactionIndex;
            string scenario = GamePrefs.leveltoload;
            float townRadius = GamePrefs.radiustowncentertocapture;
            RawImportance.Clear();
            float maxImportance = 0f;
            for (int i = 0; i < objectives.Count; i++)
            {
                var o = objectives[i];
                if ((UnityEngine.Object)o == null || o.objectives == null) continue;
                if (o.ObjectiveScenario == null || !o.ObjectiveScenario.Contains(scenario) || o.IsDeactivated()) continue;
                int id = o.UniqueObjectiveID;
                bool accomplished = CampaignObjective.IDAccomplished(id);
                for (int t = 0; t < o.objectives.Count; t++)
                {
                    var record = new ObjectiveRecord { ObjectiveId = id, Available = AvailableObjectives.Contains(id), Accomplished = accomplished };
                    float importance;
                    if (o.objectives[t] is Town town && (UnityEngine.Object)town != null)
                    {
                        Vector3 p = ((Component)town).transform.position;
                        record.X = p.x;
                        record.Z = p.z;
                        record.HolderAlliance = town.Owner;
                        record.Roles = SnapshotRules.ObjectiveRoles(town.IsCapital, false, RailLinesNear(p, townRadius), IndustryTowns.Contains(town));
                        record.GarrisonStrength = GarrisonOf(town);
                        importance = SnapshotRules.TargetImportance(true, town.representingpopulation, 0f, AtWar(town.Owner, alliance),
                            CampaignObjective.IsPartOfObjective(followed, town), IsCapitalOfIndex(town, f),
                            GamePrefs.aiimportancepopulation, GamePrefs.aiimportancetransportcapacityweight,
                            GamePrefs.nonwarparticipantsobjectimportanceweighting, GamePrefs.aitownweightfactorobjective, GamePrefs.aiimportancevaluescapital);
                    }
                    else if (o.objectives[t] is IIP iip && (UnityEngine.Object)iip != null)
                    {
                        Vector3 p = ((Component)iip).transform.position;
                        record.X = p.x;
                        record.Z = p.z;
                        record.HolderAlliance = iip.allianceowner;
                        record.Roles = SnapshotRules.ObjectiveRoles(false, iip.IIPType == IIP.id_harbor,
                            RailLinesOfIip.TryGetValue(iip, out int lines) ? lines : 0, false);
                        record.GarrisonStrength = GarrisonOf(iip);
                        importance = SnapshotRules.TargetImportance(false, 0f, iip.GetTransportCapacity(), AtWar(iip.allianceowner, alliance),
                            CampaignObjective.IsPartOfObjective(followed, null, iip), false,
                            GamePrefs.aiimportancepopulation, GamePrefs.aiimportancetransportcapacityweight,
                            GamePrefs.nonwarparticipantsobjectimportanceweighting, GamePrefs.aitownweightfactorobjective, GamePrefs.aiimportancevaluescapital);
                    }
                    else continue;
                    importance = Math.Max(0f, importance);
                    maxImportance = Math.Max(maxImportance, importance);
                    RawImportance.Add(importance);
                    s.Objectives.Add(record);
                }
            }
            for (int i = 0; i < s.Objectives.Count; i++)
            {
                var record = s.Objectives[i];
                record.Importance01 = maxImportance > 0f ? RawImportance[i] / maxImportance : 0f;
                s.Objectives[i] = record;
            }
        }

        // GameVars.alliance[holder].IsAtWarWith(faction alliance), the order vanilla tests (14917, 14958).
        private static bool AtWar(int holder, int alliance)
        {
            var alliances = GameVars.alliance;
            return alliances != null && holder >= 0 && holder < alliances.Length && alliances[holder].IsAtWarWith(alliance);
        }

        private static bool AlliedWith(int alliance, int other)
        {
            var alliances = GameVars.alliance;
            return alliances != null && alliance >= 0 && alliance < alliances.Length && alliances[alliance].IsInAllianceWith(other);
        }

        // Vanilla adds the capital bonus at array index l for the capital of alliance l (14970-14976);
        // the array is faction-indexed, so faction index f gets alliance f's capital (f = 0, 1).
        private static bool IsCapitalOfIndex(Town town, int factionIndex)
        {
            var alliances = GameVars.alliance;
            return factionIndex >= 0 && factionIndex < 2 && alliances != null && factionIndex < alliances.Length
                && (UnityEngine.Object)alliances[factionIndex].capital == (UnityEngine.Object)town;
        }

        private static int FollowedObjective(object faction)
        {
            if (_followedField == null || _followedField.DeclaringType != faction.GetType())
                _followedField = AccessTools.Field(faction.GetType(), "followedcampaignobjective");
            return _followedField != null ? (int)_followedField.GetValue(faction) : -1;
        }

        private static void AddGarrison(UnityEngine.Object at, float strength)
        {
            if (at == null) return;
            GarrisonAt.TryGetValue(at, out float sum);
            GarrisonAt[at] = sum + Math.Max(0f, strength);
        }

        private static float GarrisonOf(UnityEngine.Object at)
        {
            return GarrisonAt.TryGetValue(at, out float strength) ? strength : 0f;
        }

        // How many railroad lines list each IIP (ConstructionSymbol.IIPsForTransportCapacityUpdate).
        private static void CountRailLines()
        {
            RailLinesOfIip.Clear();
            var lines = BattleUnits.railroad;
            if (lines == null) return;
            for (int i = 0; i < lines.Count; i++)
            {
                var iips = lines[i]?.scriptref != null ? lines[i].scriptref.IIPsForTransportCapacityUpdate : null;
                if (iips == null) continue;
                for (int k = 0; k < iips.Length; k++)
                {
                    if ((UnityEngine.Object)iips[k] == null) continue;
                    RailLinesOfIip.TryGetValue(iips[k], out int count);
                    RailLinesOfIip[iips[k]] = count + 1;
                }
            }
        }

        // A town lies on a line when one of the line's IIPs is within the town-centre radius
        // (GamePrefs.radiustowncentertocapture, the radius vanilla captures a town within).
        private static int RailLinesNear(Vector3 town, float radius)
        {
            var lines = BattleUnits.railroad;
            if (lines == null || radius <= 0f) return 0;
            float radiusSq = radius * radius;
            int count = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                var iips = lines[i]?.scriptref != null ? lines[i].scriptref.IIPsForTransportCapacityUpdate : null;
                if (iips == null) continue;
                for (int k = 0; k < iips.Length; k++)
                {
                    if ((UnityEngine.Object)iips[k] == null) continue;
                    Vector3 p = ((Component)iips[k]).transform.position;
                    float dx = p.x - town.x;
                    float dz = p.z - town.z;
                    if (dx * dx + dz * dz > radiusSq) continue;
                    count++;
                    break;
                }
            }
            return count;
        }

        private static void ReadSupply(CampaignSnapshot s, int alliance)
        {
            var iips = BattleUnits.iips;
            if (iips != null)
            {
                for (int i = 0; i < iips.Count; i++)
                {
                    var iip = iips[i];
                    if ((UnityEngine.Object)iip == null) continue;
                    Vector3 p = ((Component)iip).transform.position;
                    s.Supply.Add(new SupplySourceRecord
                    {
                        Id = iip.GetInstanceID(),
                        Kind = SupplySourceKind.Iip,
                        Owner = iip.allianceowner,
                        X = p.x,
                        Z = p.z,
                        TransportCapacity = iip.GetTransportCapacity(),
                        Condition = iip.condition,
                        Built = true,
                        Invisible = iip.IIPType == IIP.id_invisible
                    });
                }
            }

            var buildings = BattleUnits.cbuildings;
            var alliances = GameVars.alliance;
            if (buildings == null || alliances == null || alliance < 0 || alliance >= alliances.Length) return;
            for (int i = 0; i < buildings.Count; i++)
            {
                var depot = buildings[i];
                if ((UnityEngine.Object)depot == null || depot.BuildingType != CBuilding.id_supplydepot) continue;
                if (depot.Owner != alliance && !alliances[alliance].IsInAllianceWith(depot.Owner)) continue;
                Vector3 p = ((Component)depot).transform.position;
                s.Supply.Add(new SupplySourceRecord
                {
                    Id = depot.GetInstanceID(),
                    Kind = SupplySourceKind.Depot,
                    Owner = depot.Owner,
                    X = p.x,
                    Z = p.z,
                    Condition = depot.Condition,
                    Built = depot.constructiontimer <= 0f
                });
            }
        }

        private static void ReadRails(CampaignSnapshot s, int alliance)
        {
            var lines = BattleUnits.railroad;
            if (lines == null) return;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line == null) continue;
                var record = new RailLineRecord
                {
                    Index = i,
                    AIipId = -1,
                    BIipId = -1,
                    Startable = line.StartConstruction(alliance, checkonly: true),
                    UnderConstruction = line.constructionprogress > 0f && line.constructionprogress < 1f,
                    Progress = line.constructionprogress
                };
                Endpoints(line.scriptref != null ? line.scriptref.IIPsForTransportCapacityUpdate : null, ref record);
                s.Rails.Add(record);
            }
        }

        // The line's two farthest-apart IIPs (the array is not ordered along the track).
        private static void Endpoints(IIP[] iips, ref RailLineRecord record)
        {
            if (iips == null) return;
            float best = -1f;
            for (int a = 0; a < iips.Length; a++)
            {
                if ((UnityEngine.Object)iips[a] == null) continue;
                Vector3 pa = ((Component)iips[a]).transform.position;
                for (int b = a; b < iips.Length; b++)
                {
                    if ((UnityEngine.Object)iips[b] == null) continue;
                    Vector3 pb = ((Component)iips[b]).transform.position;
                    float dx = pa.x - pb.x;
                    float dz = pa.z - pb.z;
                    float d = dx * dx + dz * dz;
                    if (d <= best) continue;
                    best = d;
                    record.AX = pa.x;
                    record.AZ = pa.z;
                    record.BX = pb.x;
                    record.BZ = pb.z;
                    record.AIipId = iips[a].GetInstanceID();
                    record.BIipId = iips[b].GetInstanceID();
                }
            }
        }

        private static void ReadCrossings(CampaignSnapshot s)
        {
            var bfs = ResolveBattlefieldSetup();
            var cells = (UnityEngine.Object)bfs != null ? bfs.rivercrossingpositions : null;
            if (cells == null) return;
            for (int i = 0; i < cells.Count; i++)
                s.Crossings.Add(new CrossingRecord { X = cells[i].x, Z = cells[i].z });
        }

        // AICampaign's own lookup (11143).
        private static BattlefieldSetup ResolveBattlefieldSetup()
        {
            if ((UnityEngine.Object)_bfs != null) return _bfs;
            var go = GameObject.Find("GameController");
            _bfs = (UnityEngine.Object)go != null ? go.GetComponent<BattlefieldSetup>() : null;
            return _bfs;
        }

        // The four live attribute getters cost a parent-radius test each (61043-61101); one read per
        // alliance per review day is enough (appointments execute after the snapshot).
        private static void ReadCommanders(CampaignSnapshot s, int alliance)
        {
            if (alliance < 0 || alliance >= CommanderCache.Length)
            {
                ReadCommandersLive(s, alliance);
                return;
            }
            var cache = CommanderCache[alliance];
            if (CommanderCacheDay[alliance] == s.DaySerial)
            {
                s.Commanders.AddRange(cache);
                return;
            }
            cache.Clear();
            CommanderCacheDay[alliance] = -1;
            ReadCommandersLive(s, alliance);
            cache.AddRange(s.Commanders);
            CommanderCacheDay[alliance] = s.DaySerial;
        }

        private static void ReadCommandersLive(CampaignSnapshot s, int alliance)
        {
            var commanders = GameVars.commander;
            if (commanders == null) return;
            bool career = DLC_WL.dlc_scenarioactive;
            int chosen = DLC_WL.dlc_chosencommander;
            for (int id = 0; id < commanders.Count; id++)
            {
                var c = commanders[id];
                if (c == null || c.alliance != alliance) continue;
                try
                {
                    var command = c.currentcommand;
                    bool hasCommand = (UnityEngine.Object)command != null;
                    s.Commanders.Add(new CommanderRecord
                    {
                        Id = id,
                        Rank = c.rank,
                        Initiative = c.GetCommanderInitiative(),
                        Leadership = c.GetCommanderLeadership(),
                        Administration = c.GetCommanderAdministration(),
                        Average = c.GetAverageAttributeValue(),
                        Available = c.IsAvailable(),
                        Navy = c.historicalbranch == 4,
                        Dummy = c.usedummyphoto,
                        RefreshCommand = c.refreshcommand,
                        ServingUnderPlayer = c.IsServingUnderPlayer(),
                        IsPlayer = career && id == chosen,
                        CommandInstanceId = hasCommand ? command.GetInstanceID() : -1,
                        CommandUnitTyp = hasCommand ? command.unittyp : -1,
                        Name = c.combinedname
                    });
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:snapshot-commander-skipped", "[CampaignPlanner] a commander could not be read and was left out: " + ex.Message);
                }
            }
        }

        private static void ReadRankBands(CampaignSnapshot s, int alliance)
        {
            for (int unitTyp = 10; unitTyp <= 18; unitTyp++)
            {
                s.RankBands.Add(new RankBand
                {
                    UnitTyp = unitTyp,
                    MinRank = GameVars.GetMinRankForUnittyp(unitTyp, alliance),
                    MaxRank = GameVars.GetMaxRankForUnittyp(unitTyp, alliance, checkforpromotion: false)
                });
            }
        }

        // FogPerkSightPatch.Enabled: the perk-id sight fix applies with the plugin and the flag on.
        private static bool FixPerkIds()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.FixPerkIdMismatches != null && Plugin.FixPerkIdMismatches.Value;
            }
            catch { return false; }
        }

        private static Weather ResolveWeather()
        {
            if ((UnityEngine.Object)_weather != null) return _weather;
            var go = GameObject.Find("WeatherObj");
            _weather = (UnityEngine.Object)go != null ? go.GetComponent<Weather>() : null;
            return _weather;
        }

        private static void Fill(HashSet<Regiment> set, IList list)
        {
            set.Clear();
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
                if (list[i] is Regiment reg && (UnityEngine.Object)reg != null) set.Add(reg);
        }

        private static float At(float[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length ? values[index] : 0f;
        }
    }
}
