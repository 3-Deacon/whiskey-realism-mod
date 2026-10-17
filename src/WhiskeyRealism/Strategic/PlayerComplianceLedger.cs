using System;
using WhiskeyRealism.Tactical.PlayerOrders;

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

    // Where the chain that carries a battle order stands against the order's zone, from the unit the order was
    // addressed to (PlayerComplianceRuntime): the player is judged against what his chain did with the order, not
    // against a clock that started when the order was given (2026-10-17 smoke: eight of nine samples missed, most
    // because the order's own carrier never got there or was halted by contact).
    internal enum ComplianceChain
    {
        Unknown,   // no carrier to read (a campaign order, the carrier is the player's own command): the old clock from issue
        Marching,  // outside the zone, moving
        Arrived,   // the carrier stands in the zone
        Halted,    // outside the zone, in contact (moving or stopped): vanilla's CheckEnemyContact cut the move short
        Abandoned, // outside the zone, stopped, not in contact: the chain is no longer carrying the order out
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
    // exponential average of complied (1) / missed (0) outcomes, starting at 1.
    internal sealed class PlayerComplianceLedger
    {
        // Campaign: three game days to march into the zone.
        public const double CampaignGraceHours = 72.0;
        // Battle: half a battle hour on the night-frozen doctrine clock
        // (TacticalDoctrineTime) to reach a battle order's zone. Kept from the stage F
        // design; vanilla has no deadline of its own (CheckDLCWLTopics pays whenever a
        // unit enters the zone, 123484-123517), so this is calibration: the stage F
        // plan's smoke item checks it against an obedient player's arrival times.
        public const double BattleGraceHours = 0.5;
        // How long a battle order's carrier may still be marching before the sample is given up as neutral: past
        // this the order no longer says anything about the player (calibration: smoke 2026-10-17 orders 2 and 9,
        // carriers that crawled for more than a battle hour without arriving).
        public const double ChainWaitHours = 3.0;
        // Version of the measure a stored estimate was taken with. A sidecar estimate of an older version was taken
        // with the clock from issue and counted automatic holds and halted orders as misses (smoke 2026-10-17:
        // 0.10 from eight such misses); it is not adopted, once.
        public const int MeasureVersion = 2;
        public const float Alpha = 0.25f;
        // Trust until evidence (stage F review, 2026-10-15): the AI plans around the
        // player only after it has seen him ignore orders; nothing is held against
        // him up front.
        public const float Prior = 1f;

        private readonly bool[] _pending = new bool[2];
        private readonly int[] _session = new int[2];
        private readonly double[] _issuedHours = new double[2];
        // When the order's carrier was first seen in the zone; NaN until it was.
        private readonly double[] _arrivedHours = { double.NaN, double.NaN };

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
            _arrivedHours[s] = double.NaN;
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
            return Tick(scope, nowHours, ComplianceChain.Unknown);
        }

        // A battle order is judged from when its carrier arrived in the zone (the grace is the time to follow it
        // there), not from when it was given. A carrier that was halted by contact or abandoned the move before it
        // arrived, or is still marching after ChainWaitHours, ends the sample neutral. Unknown keeps the clock from
        // issue (campaign orders, a carrier that cannot be read).
        public ComplianceOutcome Tick(ComplianceScope scope, double nowHours, ComplianceChain chain)
        {
            int s = (int)scope;
            if (!_pending[s]) return ComplianceOutcome.None;
            double elapsed = nowHours - _issuedHours[s];
            if (double.IsNaN(elapsed) || elapsed < 0.0) return DropPending(scope);
            double grace = scope == ComplianceScope.Campaign ? CampaignGraceHours : BattleGraceHours;

            if (chain == ComplianceChain.Unknown)
            {
                if (elapsed <= grace) return ComplianceOutcome.None;
            }
            else
            {
                if (chain == ComplianceChain.Arrived && double.IsNaN(_arrivedHours[s])) _arrivedHours[s] = nowHours;
                if (double.IsNaN(_arrivedHours[s]))
                {
                    if (chain == ComplianceChain.Halted || chain == ComplianceChain.Abandoned) return DropPending(scope);
                    return elapsed > ChainWaitHours ? DropPending(scope) : ComplianceOutcome.None;
                }
                if (nowHours - _arrivedHours[s] <= grace) return ComplianceOutcome.None;
            }
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

    // Reads a carrier as a ComplianceChain.
    internal static class ComplianceChainReading
    {
        public static ComplianceChain Of(bool carrierKnown, bool inZone, bool inContact, bool moving)
        {
            if (!carrierKnown) return ComplianceChain.Unknown;
            if (inZone) return ComplianceChain.Arrived;
            if (inContact) return ComplianceChain.Halted;
            return moving ? ComplianceChain.Marching : ComplianceChain.Abandoned;
        }
    }

    // Whether a stored estimate was taken with the current measure (PlayerComplianceLedger.MeasureVersion).
    internal static class ComplianceMeasure
    {
        public static bool Accepts(int savedVersion)
        {
            return savedVersion == PlayerComplianceLedger.MeasureVersion;
        }
    }

    // The W&L career a compliance estimate belongs to (chain-of-command spec §E):
    // the chosen commander plus SceneManagement.currentcampaignscenario. That field is
    // set when a campaign loads (LoadCampaignScenario 36642; a campaign battle loaded
    // from a save, 36576), cleared only by a standalone battle's Start (23806-23808,
    // currentoperation 1/7), left alone by LoadCampaignBattleScenario (36648-36661,
    // which points GamePrefs.leveltoload at the battlefield, 36650-36651) and read
    // back to reload the campaign after the battle (36810-36811). So the key stays the
    // same through a campaign battle and its return. The key string is rebuilt only
    // when an input changes: no allocation per pass or tick.
    internal sealed class ComplianceCareerKey
    {
        private int _commander = int.MinValue;
        private string _scenario;

        public string Key { get; private set; }

        // True when the career changed (or is seen for the first time).
        public bool Update(int commander, string campaignScenario)
        {
            string scenario = campaignScenario ?? string.Empty;
            if (Key != null && commander == _commander && string.Equals(scenario, _scenario, StringComparison.Ordinal))
                return false;
            _commander = commander;
            _scenario = scenario;
            Key = commander + ":" + scenario;
            return true;
        }
    }

    // One career's ledger plus the estimate a sidecar load brought in.
    internal sealed class PlayerComplianceCareers
    {
        private readonly ComplianceCareerKey _key = new ComplianceCareerKey();
        private string _savedKey;
        private float _savedReliability;
        private int _savedSamples;

        public PlayerComplianceLedger Ledger { get; private set; } = new PlayerComplianceLedger();

        public string CareerKey => _key.Key;

        // Switches to the given career's ledger; true when it changed. A saved estimate
        // for that career is adopted (PlayerComplianceLedger.Restore).
        public bool Ensure(int commander, string campaignScenario)
        {
            if (!_key.Update(commander, campaignScenario)) return false;
            Ledger = new PlayerComplianceLedger();
            if (_savedKey != null && string.Equals(_savedKey, _key.Key, StringComparison.Ordinal))
                Ledger.Restore(_savedReliability, _savedSamples);
            return true;
        }

        public void Restore(string careerKey, float reliability, int samples)
        {
            if (string.IsNullOrEmpty(careerKey)) return;
            _savedKey = careerKey;
            _savedReliability = reliability;
            _savedSamples = samples;
            if (string.Equals(_key.Key, careerKey, StringComparison.Ordinal))
                Ledger.Restore(reliability, samples);
        }

        // Only the current career's record (after Ensure); another career's saved
        // record never travels into this save.
        public bool TryExport(out string careerKey, out float reliability, out int samples)
        {
            careerKey = _key.Key;
            reliability = Ledger.Reliability;
            samples = Ledger.Samples;
            return careerKey != null && samples > 0;
        }
    }

    // A given order's identity for compliance: its group unit, type and target.
    internal struct ComplianceOrderKey
    {
        // Vanilla's own same-order distance (AIBattle.CheckCurrentOrderUpdate 8643).
        public const float SameTargetUnits = 110f;

        public bool Valid;
        public int Unit;
        public int Type;
        public float X;
        public float Z;

        public ComplianceOrderKey(int unit, int type, float x, float z)
        {
            Valid = true;
            Unit = unit;
            Type = type;
            X = x;
            Z = z;
        }

        public static bool Same(in ComplianceOrderKey a, in ComplianceOrderKey b)
        {
            if (!a.Valid || !b.Valid || a.Unit != b.Unit || a.Type != b.Type) return false;
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return dx * dx + dz * dz < SameTargetUnits * SameTargetUnits;
        }
    }

    internal enum ComplianceOrderEvent
    {
        None,   // no new session, or the first look at a career
        New,    // a new order: count it
        Repeat, // vanilla re-issued the order already counted: not a new sample
    }

    // New orders by givenorderssession (bumped per order, 8651). Vanilla re-issues a
    // campaign order on every AI pass with no dedupe (the 8640-8646 check is
    // battle-only; e.g. capital defense 11864), each with a new session. A re-issue
    // of the same group, type and target is the same order until vanilla gives a
    // different one; a cleared slot (123533) does not end it, or a player standing
    // in the zone would be counted again on every re-issue.
    internal sealed class ComplianceOrderStream
    {
        private int _lastSession = int.MinValue;
        private ComplianceOrderKey _last;

        public ComplianceOrderEvent See(bool hasOrder, int session, in ComplianceOrderKey order)
        {
            if (_lastSession == int.MinValue)
            {
                // First look at this career: an order already standing has an unknown age.
                _lastSession = session;
                if (hasOrder) _last = order;
                return ComplianceOrderEvent.None;
            }
            if (!hasOrder || session == _lastSession) return ComplianceOrderEvent.None;
            _lastSession = session;
            bool repeat = ComplianceOrderKey.Same(in order, in _last);
            _last = order;
            return repeat ? ComplianceOrderEvent.Repeat : ComplianceOrderEvent.New;
        }

        public void Reset()
        {
            _lastSession = int.MinValue;
            _last = default(ComplianceOrderKey);
        }
    }

    // Does an order start a compliance sample? Only when the player's command is outside the order's
    // zone when the order reaches him (plan 2026-10-15-wl-order-flow.md D17 (a)). Vanilla widens a zone
    // to the player's parent group (AIBattle.CheckCurrentOrderUpdate 8402-8429) and the automatic
    // release and follow-up holds sit on him, so "reaching" such a zone is instant, not compliance;
    // counting it inflated the reliability the battle AI reads.
    //
    // The zone is the order mark (OrderMark.Initialize, decompile 199995-200005): centred on the order
    // point, yawed arearotation + 180 degrees, `width` along its local x and `depth` along its local z;
    // inside includes the edge, as vanilla's collider ClosestPoint test does (AvatarButton.InsideCombatZone).
    // A value that cannot be read keeps the old behaviour: a sample starts.
    internal static class ComplianceSampleGate
    {
        private const float EdgeTolerance = 1e-3f;

        // Release and FollowUp are the superior's automatic hold at the player's position (they pay no prestige, and
        // the player cannot fail them): never a sample, whatever the position test says. The hold is built at the
        // player's position when it is decided and shown a courier leg later, 0.06-0.08 h in the 2026-10-17 smoke,
        // while an attached player marches with his brigade: by the time it reached him the zone was behind him,
        // the in-zone test started a sample, and the sample could only be missed.
        public static bool OriginStartsSample(OrderOrigin origin)
        {
            return !WlOrderFlow.IsNonPaying(origin);
        }

        public static bool StartsSample(float playerX, float playerZ, float zoneX, float zoneZ, float rotationDegrees, float width, float depth)
        {
            if (!Finite(playerX) || !Finite(playerZ) || !Finite(zoneX) || !Finite(zoneZ) || !Finite(rotationDegrees)
                || !Finite(width) || !Finite(depth) || width <= 0f || depth <= 0f) return true;
            double yaw = (rotationDegrees + 180.0) * Math.PI / 180.0;
            double sin = Math.Sin(yaw);
            double cos = Math.Cos(yaw);
            double dx = playerX - zoneX;
            double dz = playerZ - zoneZ;
            double along = dx * sin + dz * cos;      // local z (depth)
            double across = dx * cos - dz * sin;     // local x (width)
            bool inside = Math.Abs(across) <= width * 0.5 + EdgeTolerance && Math.Abs(along) <= depth * 0.5 + EdgeTolerance;
            return !inside;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
