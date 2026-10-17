using System;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // The ledger's crossing look requests (requirement-crossing-look): the Unknown link crossings the plan depends on, for BC9's recon producer
    // to send a cavalry regiment to look at. Read against the live crossing watch, so a request ends the tick its crossing is seen.
    public sealed partial class FeasibilityLedger
    {
        // The crossing a refused move wants looked at (RiverCrossingRule): one slot, the latest, ended when the crossing is seen.
        private int _gateLink = -1;

        /// <summary>
        /// A move the crossing gate refused wants this link known (a Screen, Probe or posture move across water the side has not seen). It takes
        /// one of the <see cref="ManeuverFeasibility.MaxLookRequests"/> slots, ahead of the plan's own looks when the plan's would crowd it out,
        /// and ends the tick the crossing is no longer Unknown. The newest request replaces an older one.
        /// </summary>
        public void PostGateLook(int link) => _gateLink = link;

        /// <summary>The link the crossing gate last asked to have looked at; -1 when none.</summary>
        public int GateLookLink => _gateLink;

        /// <inheritdoc />
        public int LookRequests(Span<float> xs, Span<float> zs)
        {
            int cap = Math.Min(Math.Min(xs.Length, zs.Length), ManeuverFeasibility.MaxLookRequests);
            CrossingWatch watch = _job.Input.Crossings;
            RiverRegions regions = _job.Input.Regions;
            if (cap <= 0 || watch == null || regions == null || !regions.Ready) return 0;

            // The gate's look keeps its slot: the plan's looks fill the rest.
            bool gate = _gateLink >= 0 && _gateLink < regions.LinkCount && watch.StateOf(_gateLink) == CrossingState.Unknown;
            if (_gateLink >= 0 && !gate) _gateLink = -1;
            int planCap = gate ? cap - 1 : cap;
            int n = 0;
            // 1. the selected plan's chosen routes, in the order the members march them.
            if (HasSelection && TryGet(SelectedPlan, out CandidateResult selected)) n = AddRouteLooks(in selected, watch, regions, xs, zs, n, planCap);

            // 2. the routes of the best-scored other candidates (the evaluation's candidate order is the catalog's score order).
            int rank = 0;
            for (int r = 0; r < _front.ResultCount && n < planCap && rank < ManeuverFeasibility.ConfirmCandidates; r++, rank++)
            {
                CandidateResult candidate = _front.Results[r];
                if (HasSelection && candidate.Plan == SelectedPlan) continue;
                n = AddRouteLooks(in candidate, watch, regions, xs, zs, n, planCap);
            }

            // 3. for a Defender, the crossings the perceived enemy reaches before dark, earliest arrival first: the front's as well as the flank's
            // (the threat list carries the flank's only, because those are the ones a reserve can answer).
            for (int t = 0; t < _lookFrontCount && n < planCap; t++)
                n = AddLook(_lookFront[t], watch, regions, xs, zs, n, planCap);

            // 4. the crossing a refused move is waiting on.
            if (gate) n = AddLook(_gateLink, watch, regions, xs, zs, n, cap);
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
