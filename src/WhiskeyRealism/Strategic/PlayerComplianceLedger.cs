using System;

namespace WhiskeyRealism.Strategic
{
    internal enum ComplianceScope
    {
        Campaign = 0,
        Battle = 1,
    }

    internal enum ComplianceOutcome
    {
        None,
        Complied,
        Missed,
        Neutral,
    }

    // How reliably the W&L player executes the orders his superiors send
    // (chain-of-command spec §E). Pure; PlayerComplianceRuntime feeds it from
    // vanilla's given order. No penalty is attached: consumers only plan around an
    // unreliable executor (user decision 2026-10-14).
    //
    // Vanilla anchors (decompile, build 14703111):
    //   DLC_WL.GivenOrders.givenorderssession is bumped for every new order (8651);
    //   Regiment.CheckDLCWLTopics (123468-123537) sets givenorder.firstunitreached
    //   when a player-owned unit without a detachment parent stands in the order
    //   zone, once per session, then clears campaign types 5/6/7/16 (123533).
    //
    // One pending order per scope (vanilla keeps one given order). A new order
    // before the old one resolved, a cleared order and a clock regress are
    // neutral: the player had no fair chance to comply. Reliability is an
    // exponential average of complied (1) / missed (0) outcomes.
    internal sealed class PlayerComplianceLedger
    {
        // Campaign: three game days to march into the zone. Battle: half a battle hour
        // (night-frozen doctrine clock).
        public const double CampaignGraceHours = 72.0;
        public const double BattleGraceHours = 0.5;
        public const float Alpha = 0.25f;
        public const float Prior = 0.5f;

        private readonly bool[] _pending = new bool[2];
        private readonly int[] _session = new int[2];
        private readonly double[] _issuedHours = new double[2];

        public float Reliability { get; private set; } = Prior;
        public int Samples { get; private set; }

        public bool HasPending(ComplianceScope scope) => _pending[(int)scope];

        public int PendingSession(ComplianceScope scope) => _pending[(int)scope] ? _session[(int)scope] : -1;

        // A new order to the player. Returns Neutral when it superseded an
        // unresolved one.
        public ComplianceOutcome OnDispatch(ComplianceScope scope, int session, double nowHours)
        {
            int s = (int)scope;
            ComplianceOutcome superseded = _pending[s] ? ComplianceOutcome.Neutral : ComplianceOutcome.None;
            _pending[s] = true;
            _session[s] = session;
            _issuedHours[s] = nowHours;
            return superseded;
        }

        public ComplianceOutcome OnReached(ComplianceScope scope, int session)
        {
            int s = (int)scope;
            if (!_pending[s] || _session[s] != session) return ComplianceOutcome.None;
            _pending[s] = false;
            Sample(1f);
            return ComplianceOutcome.Complied;
        }

        public ComplianceOutcome Tick(ComplianceScope scope, double nowHours)
        {
            int s = (int)scope;
            if (!_pending[s]) return ComplianceOutcome.None;
            double elapsed = nowHours - _issuedHours[s];
            if (double.IsNaN(elapsed) || elapsed < 0.0) return DropPending(scope);
            double grace = scope == ComplianceScope.Campaign ? CampaignGraceHours : BattleGraceHours;
            if (elapsed <= grace) return ComplianceOutcome.None;
            _pending[s] = false;
            Sample(0f);
            return ComplianceOutcome.Missed;
        }

        // The order left without being reached (vanilla cleared it, the battle or
        // career ended).
        public ComplianceOutcome DropPending(ComplianceScope scope)
        {
            int s = (int)scope;
            if (!_pending[s]) return ComplianceOutcome.None;
            _pending[s] = false;
            return ComplianceOutcome.Neutral;
        }

        // Adopts a saved estimate only while this session has none of its own: the
        // ledger measures the human player, so a reload never rolls it back.
        public bool Restore(float reliability, int samples)
        {
            if (Samples > 0 || samples <= 0 || float.IsNaN(reliability) || float.IsInfinity(reliability)) return false;
            Reliability = Clamp01(reliability);
            Samples = samples;
            return true;
        }

        // Strength a planner should count for a unit: player-owned units count by the
        // player's reliability, AI units fully. A non-finite reliability counts fully.
        public static float StrengthWeight(bool playerOwned, float reliability)
        {
            if (!playerOwned || float.IsNaN(reliability) || float.IsInfinity(reliability)) return 1f;
            return Clamp01(reliability);
        }

        private void Sample(float outcome)
        {
            Reliability = Clamp01(Reliability + Alpha * (outcome - Reliability));
            if (Samples < int.MaxValue) Samples++;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
