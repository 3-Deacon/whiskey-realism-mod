using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Cheap per-alliance roster fingerprint read in one pass over
    /// BattleUnits.completeunitlist. completeunitlist is NOT immutable mid-battle:
    /// UpdateUnitList (79974) replaces the list from CreateUnitDefault (89670),
    /// DetachUnits (90239) and ReattachCompany (90609); isrouted / markedforrout /
    /// activeInHierarchy change continuously (routs, reinforcement arrivals).
    /// </summary>
    public readonly struct TacticalRosterSignature : IEquatable<TacticalRosterSignature>
    {
        public TacticalRosterSignature(int listIdentity, int unitCount, int activeCount, int routedCount, int markedForRoutCount, int idHash)
        {
            ListIdentity = listIdentity;
            UnitCount = unitCount;
            ActiveCount = activeCount;
            RoutedCount = routedCount;
            MarkedForRoutCount = markedForRoutCount;
            IdHash = idHash;
        }

        public int ListIdentity { get; }
        public int UnitCount { get; }
        public int ActiveCount { get; }
        public int RoutedCount { get; }
        public int MarkedForRoutCount { get; }
        public int IdHash { get; }

        public bool Equals(TacticalRosterSignature other)
        {
            return ListIdentity == other.ListIdentity
                && UnitCount == other.UnitCount
                && ActiveCount == other.ActiveCount
                && RoutedCount == other.RoutedCount
                && MarkedForRoutCount == other.MarkedForRoutCount
                && IdHash == other.IdHash;
        }

        public override bool Equals(object obj) => obj is TacticalRosterSignature other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = ListIdentity;
                h = h * 31 + UnitCount;
                h = h * 31 + ActiveCount;
                h = h * 31 + RoutedCount;
                h = h * 31 + MarkedForRoutCount;
                return h * 31 + IdHash;
            }
        }

        public override string ToString()
        {
            return "units=" + UnitCount + " active=" + ActiveCount + " routed=" + RoutedCount + " marked=" + MarkedForRoutCount;
        }
    }

    /// <summary>
    /// Inputs that change the approach-avenue picture: scheduledarrival shrinks when
    /// a column arrives (RemoveAt 87620) and grows on new arrivals (Add 87834);
    /// EntryPoint.owner is written at arrival / redeployment (87601, 87691,
    /// 87891-87927, 94743); deployment groups can be re-placed when a new day's
    /// deployment panel closes (DoPlacementAIUnitsWithinDeploymentzoneNew 85524 via
    /// 164879-164880), which follows the battlepasseddays increment (86384).
    /// </summary>
    public readonly struct TacticalAvenueSourceSignature : IEquatable<TacticalAvenueSourceSignature>
    {
        public TacticalAvenueSourceSignature(int scheduledArrivals, int entryPointOwnerHash, int battlePassedDays)
        {
            ScheduledArrivals = scheduledArrivals;
            EntryPointOwnerHash = entryPointOwnerHash;
            BattlePassedDays = battlePassedDays;
        }

        public int ScheduledArrivals { get; }
        public int EntryPointOwnerHash { get; }
        public int BattlePassedDays { get; }

        public bool Equals(TacticalAvenueSourceSignature other)
        {
            return ScheduledArrivals == other.ScheduledArrivals
                && EntryPointOwnerHash == other.EntryPointOwnerHash
                && BattlePassedDays == other.BattlePassedDays;
        }

        public override bool Equals(object obj) => obj is TacticalAvenueSourceSignature other && Equals(other);

        public override int GetHashCode()
        {
            unchecked { return (ScheduledArrivals * 397) ^ (EntryPointOwnerHash * 31) ^ BattlePassedDays; }
        }
    }

    /// <summary>
    /// Pure cache-validity decisions and content hashes shared by CommandTreeRuntime,
    /// TacticalStableSceneCache and TacticalCacheValidator (BR-H5).
    /// </summary>
    internal static class TacticalCacheSignatures
    {
        public static bool ShouldRebuildCommandTree(
            bool cacheValid,
            int cachedShift,
            int currentShift,
            TacticalRosterSignature cachedRoster,
            TacticalRosterSignature liveRoster)
        {
            if (!cacheValid) return true;
            if (cachedShift != currentShift) return true;
            return !cachedRoster.Equals(liveRoster);
        }

        public static int MapObjectivesContentHash(IReadOnlyList<MapObjectiveCapture> objectives)
        {
            if (objectives == null) return 0;
            unchecked
            {
                int h = 17 + objectives.Count;
                for (int i = 0; i < objectives.Count; i++)
                {
                    var o = objectives[i];
                    h = h * 31 + StableStringHash(o.ObjectiveId);
                    h = h * 31 + o.Owner;
                    h = h * 31 + (o.IsActive ? 1 : 0);
                    h = h * 31 + o.Point.X.GetHashCode();
                    h = h * 31 + o.Point.Z.GetHashCode();
                }
                return h;
            }
        }

        public static int AvenuesContentHash(IReadOnlyList<TacticalApproachAvenueObservation> avenues)
        {
            if (avenues == null) return 0;
            unchecked
            {
                int h = 17 + avenues.Count;
                for (int i = 0; i < avenues.Count; i++)
                {
                    var a = avenues[i];
                    h = h * 31 + (int)a.Source;
                    h = h * 31 + a.OwnerAllianceId;
                    h = h * 31 + a.Origin.X.GetHashCode();
                    h = h * 31 + a.Origin.Z.GetHashCode();
                    h = h * 31 + a.Target.X.GetHashCode();
                    h = h * 31 + a.Target.Z.GetHashCode();
                    h = h * 31 + StableStringHash(a.Reason);
                }
                return h;
            }
        }

        // Ordinal, process-independent string hash (string.GetHashCode is randomized
        // per process on .NET Core; keep hashes comparable across runs/logs).
        private static int StableStringHash(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            unchecked
            {
                int h = 5381;
                for (int i = 0; i < value.Length; i++) h = (h * 33) ^ value[i];
                return h;
            }
        }
    }
}
