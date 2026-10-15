using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Util
{
    // Named reset hooks for static state that lives for one battle or one campaign.
    // Each owner registers its own reset (from its static constructor, so it is
    // registered before it can hold state); battle teardown calls
    // BattleScope.ResetAll and StrategicCoordinator.ResetAll calls
    // CampaignScope.ResetAll, so no teardown path hand-wires individual resets.
    // Registration is idempotent by name (a second registration replaces the
    // first). Every reset runs inside its own try/catch: one failing owner logs a
    // single warning per name and does not stop the others.
    internal sealed class ResetRegistry
    {
        private readonly string _scope;
        private readonly object _gate = new object();
        private readonly List<string> _names = new List<string>();
        private readonly Dictionary<string, Action> _resets = new Dictionary<string, Action>(StringComparer.Ordinal);

        internal ResetRegistry(string scope)
        {
            _scope = scope;
        }

        internal void Register(string name, Action reset)
        {
            if (string.IsNullOrEmpty(name) || reset == null) return;
            lock (_gate)
            {
                if (!_resets.ContainsKey(name)) _names.Add(name);
                _resets[name] = reset;
            }
        }

        internal IReadOnlyList<string> Names
        {
            get
            {
                lock (_gate)
                    return _names.ToArray();
            }
        }

        internal void ResetAll()
        {
            // Snapshot: a reset may touch a type whose static constructor registers.
            string[] names;
            Action[] resets;
            lock (_gate)
            {
                names = _names.ToArray();
                resets = new Action[names.Length];
                for (int i = 0; i < names.Length; i++) resets[i] = _resets[names[i]];
            }

            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    resets[i]();
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("scoped-state:" + _scope + ":" + names[i],
                        "[ScopedState] " + _scope + " reset '" + names[i] + "' failed: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }
    }

    /// <summary>Static state that lives for one battle.</summary>
    internal static class BattleScope
    {
        private static readonly ResetRegistry Registry = new ResetRegistry("battle");

        internal static void Register(string name, Action reset) => Registry.Register(name, reset);

        internal static void ResetAll() => Registry.ResetAll();

        internal static IReadOnlyList<string> RegisteredNames => Registry.Names;
    }

    /// <summary>Static state that lives for one campaign.</summary>
    internal static class CampaignScope
    {
        private static readonly ResetRegistry Registry = new ResetRegistry("campaign");

        internal static void Register(string name, Action reset) => Registry.Register(name, reset);

        internal static void ResetAll() => Registry.ResetAll();

        internal static IReadOnlyList<string> RegisteredNames => Registry.Names;
    }
}
