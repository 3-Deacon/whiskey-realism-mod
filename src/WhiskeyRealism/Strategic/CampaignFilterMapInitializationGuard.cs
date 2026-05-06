using System;

namespace WhiskeyRealism.Strategic
{
    internal readonly struct CampaignFilterMapState
    {
        internal readonly int TownRun;
        internal readonly int IipRun;
        internal readonly int CorporateRun;
        internal readonly int TownCount;
        internal readonly int IipCount;
        internal readonly int CorporateCount;

        internal CampaignFilterMapState(
            int townRun,
            int iipRun,
            int corporateRun,
            int townCount,
            int iipCount,
            int corporateCount)
        {
            TownRun = townRun;
            IipRun = iipRun;
            CorporateRun = corporateRun;
            TownCount = townCount;
            IipCount = iipCount;
            CorporateCount = corporateCount;
        }

        internal bool SamePosition(CampaignFilterMapState other)
        {
            return TownRun == other.TownRun
                && IipRun == other.IipRun
                && CorporateRun == other.CorporateRun
                && TownCount == other.TownCount
                && IipCount == other.IipCount
                && CorporateCount == other.CorporateCount;
        }

        internal string Signature()
        {
            return TownRun + "/" + TownCount + ":"
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

        private readonly int _maxRepeatedNoProgressReturns;
        private string _lastNoProgressSignature;
        private int _repeatedNoProgressReturns;

        internal CampaignFilterMapInitializationGuard(int maxRepeatedNoProgressReturns = DefaultMaxRepeatedNoProgressReturns)
        {
            _maxRepeatedNoProgressReturns = Math.Max(1, maxRepeatedNoProgressReturns);
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
            if (!initialization || exception == null)
                return new CampaignFilterMapGuardDecision(false, "");

            Reset();
            return new CampaignFilterMapGuardDecision(true, "exception:" + exception.GetType().Name + "@" + before.Signature());
        }

        private void Reset()
        {
            _lastNoProgressSignature = null;
            _repeatedNoProgressReturns = 0;
        }
    }
}
