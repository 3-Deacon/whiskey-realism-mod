using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    [Flags]
    public enum CampaignTownRole
    {
        None = 0,
        Capital = 1,
        MajorCity = 2,
        Workforce = 4,
        Economic = 8,
        Border = 16
    }

    public sealed class CampaignMapTown
    {
        public string CityName;
        public int StateId = -1;
        public string StateName;
        public string StateAbbrev;
        public int Owner = -1;
        public int OriginalOwner = -1;
        public bool IsCapital;
        public float X;
        public float Z;
        public float CitySize;
        public float RepresentingPopulation;
        public float AvailableWorkforceLevel;
        public float PrivateWealth;
        public float IncomeTax;
        public CampaignTownRole Roles;
        public Theater Theater = Theater.Unknown;
    }

    public sealed class CampaignMapState
    {
        public int StateId = -1;
        public string Name;
        public string Abbrev;
        public Theater Theater = Theater.Unknown;
        public int TownCount;
        public int CapitalCount;
        public float X;
        public float Z;
        public float Population;
    }

    public enum CampaignMapAssetKind
    {
        Unknown = 0,
        Fort = 1,
        SeaHarbor = 2,
        RiverHarbor = 3
    }

    public sealed class CampaignMapAsset
    {
        public CampaignMapAssetKind Kind = CampaignMapAssetKind.Unknown;
        public string Name;
        public int StateId = -1;
        public string StateName;
        public string StateAbbrev;
        public int Owner = -1;
        public float X;
        public float Z;
        public int Level;
        public float Condition = 1f;
        public float Blocked;
        public float Capacity;
        public Theater Theater = Theater.Unknown;
    }

    public sealed class CampaignMapLedger
    {
        private readonly Dictionary<string, CampaignMapTown> _townByName =
            new Dictionary<string, CampaignMapTown>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, CampaignMapState> _stateById =
            new Dictionary<int, CampaignMapState>();
        private readonly List<CampaignMapTown> _towns = new List<CampaignMapTown>();
        private readonly List<CampaignMapState> _states = new List<CampaignMapState>();
        private readonly List<CampaignMapAsset> _assets = new List<CampaignMapAsset>();

        public IReadOnlyList<CampaignMapTown> Towns => _towns;
        public IReadOnlyList<CampaignMapState> States => _states;
        public IReadOnlyList<CampaignMapAsset> Assets => _assets;
        public string Signature { get; private set; } = "empty";

        public static CampaignMapLedger Build(IEnumerable<CampaignMapTown> towns)
        {
            return Build(towns, null);
        }

        public static CampaignMapLedger Build(IEnumerable<CampaignMapTown> towns, IEnumerable<CampaignMapAsset> assets)
        {
            var ledger = new CampaignMapLedger();
            if (towns != null)
            {
                foreach (var source in towns)
                {
                    if (source == null || string.IsNullOrEmpty(source.CityName)) continue;
                    var town = CopyTown(source);
                    town.Roles = ClassifyRoles(town);
                    town.Theater = TheaterClassifier.FromStateName(town.StateName);
                    if (town.Theater == Theater.Unknown)
                        town.Theater = TheaterClassifier.FromPosition(town.X, town.Z);

                    ledger._towns.Add(town);
                    if (!ledger._townByName.ContainsKey(town.CityName))
                        ledger._townByName.Add(town.CityName, town);

                    if (town.StateId >= 0)
                        ledger.AddStateContribution(town);
                }
            }

            ledger.FinalizeStates();

            if (assets != null)
            {
                foreach (var source in assets)
                {
                    if (source == null) continue;
                    var asset = CopyAsset(source);
                    asset.Theater = TheaterClassifier.FromStateName(asset.StateName);
                    if (asset.Theater == Theater.Unknown)
                        asset.Theater = TheaterClassifier.FromPosition(asset.X, asset.Z);
                    ledger._assets.Add(asset);
                }
            }

            ledger.Signature = ledger.BuildSignature();
            return ledger;
        }

        public bool TryGetTown(string cityName, out CampaignMapTown town)
        {
            if (cityName == null)
            {
                town = null;
                return false;
            }
            return _townByName.TryGetValue(cityName, out town);
        }

        public bool TryGetStateTheater(int stateId, out Theater theater)
        {
            if (_stateById.TryGetValue(stateId, out var state))
            {
                theater = state.Theater;
                return theater != Theater.Unknown;
            }

            theater = Theater.Unknown;
            return false;
        }

        public Theater GetStateTheaterOrUnknown(int stateId)
        {
            return TryGetStateTheater(stateId, out var theater) ? theater : Theater.Unknown;
        }

        public CampaignMapTown BestDefenseTown(int alliance)
        {
            CampaignMapTown best = null;
            float bestScore = float.MinValue;
            foreach (var town in _towns)
            {
                float score = DefenseScore(town, alliance);
                if (score > bestScore)
                {
                    best = town;
                    bestScore = score;
                }
            }
            return best;
        }

        public string Summary()
        {
            string unionDefense = BestDefenseTown(0)?.CityName ?? "<none>";
            string csaDefense = BestDefenseTown(1)?.CityName ?? "<none>";
            int forts = 0;
            int seaHarbors = 0;
            int riverHarbors = 0;
            foreach (var asset in _assets)
            {
                if (asset.Kind == CampaignMapAssetKind.Fort) forts++;
                else if (asset.Kind == CampaignMapAssetKind.SeaHarbor) seaHarbors++;
                else if (asset.Kind == CampaignMapAssetKind.RiverHarbor) riverHarbors++;
            }

            return $"towns={_towns.Count} states={_states.Count} forts={forts} seaHarbors={seaHarbors} riverHarbors={riverHarbors} unionDefense={unionDefense} csaDefense={csaDefense}";
        }

        private void AddStateContribution(CampaignMapTown town)
        {
            if (!_stateById.TryGetValue(town.StateId, out var state))
            {
                state = new CampaignMapState
                {
                    StateId = town.StateId,
                    Name = town.StateName,
                    Abbrev = town.StateAbbrev
                };
                _stateById.Add(town.StateId, state);
                _states.Add(state);
            }

            state.TownCount++;
            if (town.IsCapital) state.CapitalCount++;
            state.X += town.X;
            state.Z += town.Z;
            state.Population += Math.Max(0f, town.RepresentingPopulation);
        }

        private void FinalizeStates()
        {
            foreach (var state in _states)
            {
                if (state.TownCount > 0)
                {
                    state.X /= state.TownCount;
                    state.Z /= state.TownCount;
                }

                state.Theater = TheaterClassifier.FromStateName(state.Name);
                if (state.Theater == Theater.Unknown)
                    state.Theater = TheaterClassifier.FromPosition(state.X, state.Z);
            }
        }

        private string BuildSignature()
        {
            return _towns.Count + ":" + _states.Count + ":" +
                _assets.Count + ":" +
                (BestDefenseTown(0)?.CityName ?? "") + ":" +
                (BestDefenseTown(1)?.CityName ?? "");
        }

        private static CampaignMapTown CopyTown(CampaignMapTown source)
        {
            return new CampaignMapTown
            {
                CityName = source.CityName,
                StateId = source.StateId,
                StateName = source.StateName,
                StateAbbrev = source.StateAbbrev,
                Owner = source.Owner,
                OriginalOwner = source.OriginalOwner,
                IsCapital = source.IsCapital,
                X = source.X,
                Z = source.Z,
                CitySize = source.CitySize,
                RepresentingPopulation = source.RepresentingPopulation,
                AvailableWorkforceLevel = source.AvailableWorkforceLevel,
                PrivateWealth = source.PrivateWealth,
                IncomeTax = source.IncomeTax
            };
        }

        private static CampaignMapAsset CopyAsset(CampaignMapAsset source)
        {
            return new CampaignMapAsset
            {
                Kind = source.Kind,
                Name = source.Name,
                StateId = source.StateId,
                StateName = source.StateName,
                StateAbbrev = source.StateAbbrev,
                Owner = source.Owner,
                X = source.X,
                Z = source.Z,
                Level = source.Level,
                Condition = source.Condition,
                Blocked = source.Blocked,
                Capacity = source.Capacity
            };
        }

        private static CampaignTownRole ClassifyRoles(CampaignMapTown town)
        {
            var roles = CampaignTownRole.None;
            if (town.IsCapital) roles |= CampaignTownRole.Capital;
            if (town.CitySize >= 0.5f || town.RepresentingPopulation >= 75000f)
                roles |= CampaignTownRole.MajorCity;
            if (town.AvailableWorkforceLevel >= 0.5f)
                roles |= CampaignTownRole.Workforce;
            if (town.PrivateWealth * Math.Max(1f, town.RepresentingPopulation) >= 50000f || town.IncomeTax > 0f)
                roles |= CampaignTownRole.Economic;
            if (town.OriginalOwner >= 0 && town.Owner != town.OriginalOwner)
                roles |= CampaignTownRole.Border;
            return roles;
        }

        private static float DefenseScore(CampaignMapTown town, int alliance)
        {
            float score = 0f;
            if (town.Owner == alliance) score += 0.4f;
            if ((town.Roles & CampaignTownRole.Capital) != 0) score += 3.0f;
            if ((town.Roles & CampaignTownRole.MajorCity) != 0) score += 0.8f;
            if ((town.Roles & CampaignTownRole.Workforce) != 0) score += 0.35f;
            if ((town.Roles & CampaignTownRole.Economic) != 0) score += 0.35f;
            if ((town.Roles & CampaignTownRole.Border) != 0) score += 0.25f;
            score += Math.Max(0f, town.CitySize);
            score += Math.Min(1f, Math.Max(0f, town.RepresentingPopulation) / 100000f) * 0.4f;
            return score;
        }
    }
}
