using System;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // The ledger's crossing look requests (requirement-crossing-look): the Unknown link crossings the plan depends on, for BC9's recon producer
    // to send a cavalry regiment to look at. Read against the live crossing watch, so a request ends the tick its crossing is seen.
    public sealed partial class FeasibilityLedger
    {
        // The crossing a refused move wants looked at (RiverCrossingRule): one slot, the first unresolved request, ended when the crossing is seen or after
        // GateLookTtlHours (on the evaluation's clock).
        private int _gateLink = -1;
        private double _gateAt;

        /// <summary>How long an unresolved gate look is kept before a newer refusal may replace it (doctrine hours).</summary>
        public const double GateLookTtlHours = 1.0;

        /// <summary>
        /// A move the crossing gate refused wants this link known (a Screen, Probe or posture move across water the side has not seen). The
        /// request takes one of the <see cref="ManeuverFeasibility.MaxLookRequests"/> slots, after the selected plan's own looks and ahead of the other
        /// candidates' and the Defender's list. An unresolved request is not overwritten by a newer refusal for another link: it stays until the
        /// crossing is no longer Unknown or its time is up.
        /// </summary>
        public void PostGateLook(int link)
        {
            if (link == _gateLink) return;
            if (_gateLink >= 0 && GateLookPending()) return;
            _gateLink = link;
            _gateAt = _job.Input.NowHours;
        }

        /// <summary>The link the crossing gate last asked to have looked at; -1 when none.</summary>
        public int GateLookLink => _gateLink;

        private bool GateLookPending()
        {
            CrossingWatch watch = _job.Input.Crossings;
            if (watch != null && watch.StateOf(_gateLink) != CrossingState.Unknown) return false;
            return _job.Input.NowHours - _gateAt <= GateLookTtlHours;
        }

        /// <inheritdoc />
        public int LookRequests(Span<float> xs, Span<float> zs)
        {
            int cap = Math.Min(Math.Min(xs.Length, zs.Length), ManeuverFeasibility.MaxLookRequests);
            CrossingWatch watch = _job.Input.Crossings;
            RiverRegions regions = _job.Input.Regions;
            if (cap <= 0 || watch == null || regions == null || !regions.Ready) return 0;

            int n = 0;
            // 1. the selected plan's chosen routes, in the order the members march them.
            if (HasSelection && TryGet(SelectedPlan, out CandidateResult selected)) n = AddRouteLooks(in selected, watch, regions, xs, zs, n, cap);

            // 2. the crossing a refused move is waiting on.
            if (_gateLink >= 0)
            {
                if (_gateLink < regions.LinkCount && GateLookPending()) n = AddLook(_gateLink, watch, regions, xs, zs, n, cap);
                else _gateLink = -1;
            }

            // 3. the routes of the best-scored other candidates (the evaluation's candidate order is the catalog's score order).
            int rank = 0;
            for (int r = 0; r < _front.ResultCount && n < cap && rank < ManeuverFeasibility.ConfirmCandidates; r++, rank++)
            {
                CandidateResult candidate = _front.Results[r];
                if (HasSelection && candidate.Plan == SelectedPlan) continue;
                n = AddRouteLooks(in candidate, watch, regions, xs, zs, n, cap);
            }

            // 4. for a Defender, the crossings the perceived enemy reaches before dark, earliest arrival first: the front's as well as the flank's
            // (the threat list carries the flank's only, because those are the ones a reserve can answer).
            for (int t = 0; t < _lookFrontCount && n < cap; t++)
                n = AddLook(_lookFront[t], watch, regions, xs, zs, n, cap);
            return n;
        }

        private int AddRouteLooks(in CandidateResult result, CrossingWatch watch, RiverRegions regions, Span<float> xs, Span<float> zs, int n, int cap)
        {
            for (int m = 0; m < result.MemberCount && n < cap; m++)
            {
                MemberEstimate member = _front.Members[result.MemberStart + m];
                for (int c = 0; c < member.CrossingCount && n < cap; c++)
                    n = AddLook(_front.Crossings[result.CrossingStart + member.CrossingStart + c].Link, watch, regions, xs, zs, n, cap);
            }
            return n;
        }

        // The crossing's point, once, when the side does not know it.
        private static int AddLook(int link, CrossingWatch watch, RiverRegions regions, Span<float> xs, Span<float> zs, int n, int cap)
        {
            if (n >= cap || watch.StateOf(link) != CrossingState.Unknown) return n;
            regions.LinkCrossing(link, out float x, out float z, out _, out _);
            for (int i = 0; i < n; i++)
                if (xs[i] == x && zs[i] == z) return n;
            xs[n] = x;
            zs[n] = z;
            return n + 1;
        }
    }
}
