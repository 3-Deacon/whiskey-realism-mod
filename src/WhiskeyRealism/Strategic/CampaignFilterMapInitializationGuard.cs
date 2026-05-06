using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    internal readonly struct CampaignFilterMapState
    {
        internal readonly int TownRun;
        internal readonly int IipRun;
        internal readonly int CorporateRun;
        internal readonly int TownCount;
        internal readonly int SmallTownCount;
        internal readonly int IipCount;
        internal readonly int CorporateCount;

        internal CampaignFilterMapState(
            int townRun,
            int iipRun,
            int corporateRun,
            int townCount,
            int smallTownCount,
            int iipCount,
            int corporateCount)
        {
            TownRun = townRun;
            IipRun = iipRun;
            CorporateRun = corporateRun;
            TownCount = townCount;
            SmallTownCount = smallTownCount;
            IipCount = iipCount;
            CorporateCount = corporateCount;
        }

        internal bool SamePosition(CampaignFilterMapState other)
        {
            return TownRun == other.TownRun
                && IipRun == other.IipRun
                && CorporateRun == other.CorporateRun
                && TownCount == other.TownCount
                && SmallTownCount == other.SmallTownCount
                && IipCount == other.IipCount
                && CorporateCount == other.CorporateCount;
        }

        internal string Signature()
        {
            return TownRun + "/" + TownCount + ":"
                + IipRun + "/" + SmallTownCount + ":"
                + IipRun + "/" + IipCount + ":"
                + CorporateRun + "/" + CorporateCount;
        }
    }

    internal readonly struct CampaignFilterMapGuardDecision
    {
        internal readonly bool ForceComplete;
        internal readonly string Reason;

        internal CampaignFilterMapGuardDecision(bool forceComplete, string reason)
        {
            ForceComplete = forceComplete;
            Reason = reason ?? "";
        }
    }

    internal sealed class CampaignFilterMapInitializationGuard
    {
        internal const int DefaultMaxRepeatedNoProgressReturns = 64;
        internal const int DefaultMaxRuntimeExceptionSuppressionsPerSignature = 2;

        private readonly int _maxRepeatedNoProgressReturns;
        private readonly int _maxRuntimeExceptionSuppressionsPerSignature;
        private string _lastNoProgressSignature;
        private int _repeatedNoProgressReturns;
        private string _lastRuntimeExceptionSignature;
        private int _runtimeExceptionSuppressionsForSignature;

        internal CampaignFilterMapInitializationGuard(
            int maxRepeatedNoProgressReturns = DefaultMaxRepeatedNoProgressReturns,
            int maxRuntimeExceptionSuppressionsPerSignature = DefaultMaxRuntimeExceptionSuppressionsPerSignature)
        {
            _maxRepeatedNoProgressReturns = Math.Max(1, maxRepeatedNoProgressReturns);
            _maxRuntimeExceptionSuppressionsPerSignature = Math.Max(1, maxRuntimeExceptionSuppressionsPerSignature);
        }

        internal CampaignFilterMapGuardDecision Observe(
            bool initialization,
            bool result,
            CampaignFilterMapState before,
            CampaignFilterMapState after)
        {
            if (!initialization || result)
            {
                Reset();
                return new CampaignFilterMapGuardDecision(false, "");
            }

            if (!before.SamePosition(after))
            {
                Reset();
                return new CampaignFilterMapGuardDecision(false, "");
            }

            string signature = after.Signature();
            if (signature == _lastNoProgressSignature)
            {
                _repeatedNoProgressReturns++;
            }
            else
            {
                _lastNoProgressSignature = signature;
                _repeatedNoProgressReturns = 1;
            }

            if (_repeatedNoProgressReturns < _maxRepeatedNoProgressReturns)
                return new CampaignFilterMapGuardDecision(false, "");

            Reset();
            return new CampaignFilterMapGuardDecision(true, "no-progress");
        }

        internal CampaignFilterMapGuardDecision ObserveException(
            bool initialization,
            Exception exception,
            CampaignFilterMapState before)
        {
            if (exception == null)
                return new CampaignFilterMapGuardDecision(false, "");

            if (!initialization)
            {
                if (exception is NullReferenceException && CanAdvanceRuntimeCursor(before))
                {
                    string signature = "runtime-exception:NullReferenceException@" + before.Signature();
                    if (signature == _lastRuntimeExceptionSignature)
                    {
                        _runtimeExceptionSuppressionsForSignature++;
                    }
                    else
                    {
                        _lastRuntimeExceptionSignature = signature;
                        _runtimeExceptionSuppressionsForSignature = 1;
                    }

                    if (_runtimeExceptionSuppressionsForSignature <= _maxRuntimeExceptionSuppressionsPerSignature)
                    {
                        ResetNoProgress();
                        return new CampaignFilterMapGuardDecision(true, "runtime-exception:NullReferenceException");
                    }
                }

                return new CampaignFilterMapGuardDecision(false, "");
            }

            Reset();
            return new CampaignFilterMapGuardDecision(true, "exception:" + exception.GetType().Name + "@" + before.Signature());
        }

        internal static bool TryAdvanceRuntimeCursor(CampaignFilterMapState before, out CampaignFilterMapState after)
        {
            after = before;
            if (!CanAdvanceRuntimeCursor(before))
                return false;

            int townRun = before.TownRun + 1;
            int iipRun = before.IipRun + 1;
            int corporateRun = before.CorporateRun;
            if (iipRun >= before.IipCount)
                corporateRun++;

            if (townRun >= before.TownCount && corporateRun >= before.CorporateCount && iipRun >= before.IipCount)
            {
                after = new CampaignFilterMapState(
                    0,
                    0,
                    -1,
                    before.TownCount,
                    before.SmallTownCount,
                    before.IipCount,
                    before.CorporateCount);
                return true;
            }

            after = new CampaignFilterMapState(
                townRun,
                iipRun,
                corporateRun,
                before.TownCount,
                before.SmallTownCount,
                before.IipCount,
                before.CorporateCount);
            return true;
        }

        internal static string BuildRuntimeDiagnostic(
            CampaignFilterMapState before,
            CampaignFilterMapState after,
            string probeSummary)
        {
            return "cursor=" + before.Signature()
                + " next=" + after.Signature()
                + (string.IsNullOrEmpty(probeSummary) ? "" : " " + probeSummary);
        }

        internal static string[] GetMissingAssignFiltersMapNames(
            bool availableWorkforceReady,
            bool slaveryReady,
            bool tradeAndSupplyReady,
            bool supplyReady,
            bool availableCapitalReady,
            bool transportBottlenecksReady,
            bool marketCapacityReady,
            bool hospitalsReady)
        {
            var missing = new List<string>();
            if (!availableWorkforceReady) missing.Add("availableworkforce");
            if (!slaveryReady) missing.Add("slavery");
            if (!tradeAndSupplyReady) missing.Add("tradeandsupply");
            if (!supplyReady) missing.Add("supply");
            if (!availableCapitalReady) missing.Add("availablecapital");
            if (!transportBottlenecksReady) missing.Add("transportbottlenecks");
            if (!marketCapacityReady) missing.Add("marketcapacity");
            if (!hospitalsReady) missing.Add("hospitals");
            return missing.ToArray();
        }

        private static bool CanAdvanceRuntimeCursor(CampaignFilterMapState state)
        {
            return state.TownRun >= 0
                && state.IipRun >= 0
                && state.CorporateRun >= -1
                && state.TownCount > 0
                && state.SmallTownCount > 0
                && state.IipCount >= 0
                && state.CorporateCount > 0;
        }

        private void Reset()
        {
            _lastNoProgressSignature = null;
            _repeatedNoProgressReturns = 0;
            _lastRuntimeExceptionSignature = null;
            _runtimeExceptionSuppressionsForSignature = 0;
        }

        private void ResetNoProgress()
        {
            _lastNoProgressSignature = null;
            _repeatedNoProgressReturns = 0;
        }
    }
}
