using System;
using System.Collections.Generic;
using System.Text;

namespace WhiskeyRealism.Tactical.Battle
{
    // The arbiter's intent producers, built once from one explicit list in plan
    // order (plan BC5 producer order, BC7 registry). Pure; BattleCoreTick builds
    // it in its type initializer with a factory that constructs each runtime
    // producer by name.
    //
    // The order is the order producers run in an arbiter tick. It matters only
    // where a producer reads what an earlier one decided this tick: the facing
    // pulse, fire control and recon (BC9: a scout's brigade the posture producer
    // ordered this tick is not used) read the posture producer's per-group record. Claims
    // themselves are resolved by priority, not order (OrderArbiter.Resolve). There
    // is no "Play" slot: the geometric play feeds the allocator's zone roles and
    // has no orders of its own (plan BC5a As built).
    //
    // A factory that returns null, a producer with a different name, or a factory
    // that throws leaves that slot out and lists its name in Missing, so a broken
    // producer never takes the tick down; the runtime logs Missing once per battle.
    public sealed class BattleProducerRegistry
    {
        public static readonly IReadOnlyList<string> Order = new[]
        {
            "Withdrawal", "ArtilleryDoctrine", "ArtilleryPointBlank", "GroupStance", "ChargeDemotion", "Posture",
            "FacingPulse", "FireControl", "DivisionPlay", "Recon", "ReserveRelease", "Charge",
        };

        private const string ThrewKeyPrefix = "tactical-battle-core:producer-threw:";

        private readonly IBattleIntentProducer[] _producers;
        private readonly string[] _threwKeys;

        public BattleProducerRegistry(Func<string, IBattleIntentProducer> factory)
        {
            var built = new List<IBattleIntentProducer>(Order.Count);
            var missing = new StringBuilder();
            for (int i = 0; i < Order.Count; i++)
            {
                string name = Order[i];
                IBattleIntentProducer producer = null;
                try
                {
                    producer = factory != null ? factory(name) : null;
                }
                catch (Exception)
                {
                    producer = null;
                }

                if (producer != null && string.Equals(producer.Name, name, StringComparison.Ordinal))
                {
                    built.Add(producer);
                    continue;
                }
                if (missing.Length > 0) missing.Append(',');
                missing.Append(name);
            }

            _producers = built.ToArray();
            _threwKeys = new string[_producers.Length];
            for (int i = 0; i < _producers.Length; i++) _threwKeys[i] = ThrewKeyPrefix + _producers[i].Name;
            Names = JoinNames(_producers);
            Missing = missing.ToString();
        }

        public int Count => _producers.Length;
        public bool HasProducers => _producers.Length > 0;
        public IBattleIntentProducer this[int index] => _producers[index];

        /// <summary>Registered names in run order, comma-separated (the producers smoke line).</summary>
        public string Names { get; }

        /// <summary>Plan names that could not be built, comma-separated; empty when none.</summary>
        public string Missing { get; }

        /// <summary>
        /// OnceLog key for a producer that threw, built once per producer (a fixed
        /// set of twelve, so the keys stay static); prefixed "tactical" so
        /// OnceLog.ResetTactical re-arms it each battle.
        /// </summary>
        public string ThrewKey(int index) => _threwKeys[index];

        private static string JoinNames(IBattleIntentProducer[] producers)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < producers.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(producers[i].Name);
            }
            return sb.ToString();
        }
    }
}
