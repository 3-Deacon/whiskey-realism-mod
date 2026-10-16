using System;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC13 (spec docs/superpowers/specs/2026-10-15-battle-opportunity-design.md § 4): each AI corps and
    // division commander decides by his own temperament. Pure half: the derivation from raw game stats,
    // the per-side table, and the out-of-duty factor. The runtime half (CommanderPersonalityRuntime) reads
    // the stats off GameVars.Commander and fills the table; this file touches no Unity or vanilla type.

    /// <summary>Where a commander's vector came from. The list's own values win over a derivation.</summary>
    public enum PersonalitySource : byte
    {
        Default = 0,
        Registry = 1,
        Derived = 2,
    }

    /// <summary>
    /// The raw (file) stats of one vanilla commander, each 0-1, as GameVars.Commander holds them after
    /// Commander.Load (decompile 59701: the file value divided by 100). Not the live getters (61134-61233),
    /// which fold in experience, parent influence, W&amp;L staff influence and out-of-duty status.
    /// </summary>
    public struct CommanderStats
    {
        public float Initiative, Cunning, Leadership, Administration;
        public bool Veteran, WestPoint, Political;
        /// <summary>False when the stats could not be read (index out of range, reflection failure).</summary>
        public bool Valid;
    }

    public static class CommanderPersonalityDerivation
    {
        // Roster centres of the shipped Config/commanders.txt (spec § 4): initiative mean Union 0.447 / CSA 0.496,
        // administration about 0.55, cunning median about 0.38 (Union 0.35, CSA 0.40), leadership mean about 0.55.
        public const float InitiativeCentre = 0.45f, AdministrationCentre = 0.55f, CunningCentre = 0.38f, LeadershipCentre = 0.55f;

        /// <summary>
        /// The spec's § 4 table, each component clamped to [-1, 1]. No faction vector is added: the stats already
        /// carry the alliance difference, and adding FactionProfiles would count it twice (spec D-A2).
        /// </summary>
        public static PersonalityVector Derive(in CommanderStats s)
        {
            return new PersonalityVector(
                PersonalityVector.Clamp(3.0f * (s.Initiative - InitiativeCentre)),
                PersonalityVector.Clamp(2.0f * (InitiativeCentre - s.Initiative) + 1.0f * (s.Administration - AdministrationCentre) + (s.Political ? 0.3f : 0f)),
                PersonalityVector.Clamp(2.5f * (s.Cunning - CunningCentre) + 1.0f * (s.Initiative - InitiativeCentre)),
                PersonalityVector.Clamp(2.0f * (s.Leadership - LeadershipCentre) + (s.Veteran ? 0.2f : 0f)),
                s.Political ? 0.4f : 0f);
        }

        /// <summary>A signed component in [-1, 1] to the 0-1 form the doctrine consumers read (agg01 and the like).</summary>
        public static float To01(float signed) => (signed + 1f) * 0.5f;

        /// <summary>
        /// The vector of a commander whose stats cannot be read: the faction profile with the rank bias of his
        /// echelon, as the roster does for an unlisted commander today.
        /// </summary>
        public static PersonalityVector Default(int alliance, EchelonKind echelon)
            => TacticalCommanderRoster.ApplyRankBias(FactionProfiles.For(alliance), echelon);

        /// <summary>
        /// Vanilla scales the stats of a commander who is out of duty (status 2 captured, 3 wounded, 4 killed) by
        /// GamePrefs.reductionfactorcommanderoutofduty (unitprefs.txt, shipped 0.5; decompile 61134-61233). The
        /// vector stays pinned; the exploit chance carries the same factor (requirement-commander-duty).
        /// </summary>
        public static float Duty(int status, float reductionFactor) => status != 0 ? reductionFactor : 1f;
    }

    /// <summary>
    /// One per side. Maps a vanilla commander index (UnitRecord.Commander) to the vector that commander keeps for
    /// the battle. A grow-only array indexed by commander index, so a lookup is O(1) and allocation-free after
    /// growth. First write wins: the vector is a trait pinned for the battle (spec D-A1). The coordinator tick is
    /// the only writer; the opportunity producer, the army orchestrator, the friction observer and #44's fallback
    /// read it.
    /// </summary>
    public sealed class CommanderPersonalityTable
    {
        /// <summary>vanilla's commander list holds a few thousand records; a larger index is treated as invalid.</summary>
        public const int MaxCommanderIndex = 65535;

        private PersonalityVector[] _vectors = new PersonalityVector[64];
        private byte[] _sources = new byte[64];
        private bool[] _present = new bool[64];
        private readonly int[] _bySource = new int[3];

        public int Count { get; private set; }

        public int CountBySource(PersonalitySource source) => _bySource[(int)source];

        public bool TryGet(int commanderIndex, out PersonalityVector vector, out PersonalitySource source)
        {
            if (commanderIndex >= 0 && commanderIndex < _present.Length && _present[commanderIndex])
            {
                vector = _vectors[commanderIndex];
                source = (PersonalitySource)_sources[commanderIndex];
                return true;
            }
            vector = default;
            source = PersonalitySource.Default;
            return false;
        }

        /// <summary>Adds the entry unless the index already has one or is invalid. Returns true when it was added.</summary>
        public bool Add(int commanderIndex, in PersonalityVector vector, PersonalitySource source)
        {
            if (commanderIndex < 0 || commanderIndex > MaxCommanderIndex) return false;
            if (commanderIndex >= _present.Length) Grow(commanderIndex + 1);
            if (_present[commanderIndex]) return false;
            _present[commanderIndex] = true;
            _vectors[commanderIndex] = vector;
            _sources[commanderIndex] = (byte)source;
            _bySource[(int)source]++;
            Count++;
            return true;
        }

        public void Clear()
        {
            Array.Clear(_present, 0, _present.Length);
            Array.Clear(_bySource, 0, _bySource.Length);
            Count = 0;
        }

        private void Grow(int needed)
        {
            int size = _present.Length;
            while (size < needed) size *= 2;
            Array.Resize(ref _vectors, size);
            Array.Resize(ref _sources, size);
            Array.Resize(ref _present, size);
        }
    }
}
