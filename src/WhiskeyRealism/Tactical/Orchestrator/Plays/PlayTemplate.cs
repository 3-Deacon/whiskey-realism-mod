using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// Geometric template for one tactical archetype. Each playbook
    /// (DoubleEnvelopment, PreparedDefense, etc.) maps to one PlayTemplate
    /// listing the rects (zones) the corps should occupy.
    ///
    /// Templates are army-relative — they project to world coordinates at
    /// runtime against the current army↔enemy axis, so the same template fires
    /// regardless of map orientation. Mirrors Scourge of War's grand-tactics
    /// "play" structure (offai.cpp:1141-1320) where each play is a set of
    /// PLAYRect with PLAYCorpNum + PLAYFTime per slot.
    /// </summary>
    public sealed class PlayTemplate
    {
        public PlayTemplate(string name, BattlePlanId playbookId, IReadOnlyList<PlayRect> rects)
        {
            Name = string.IsNullOrEmpty(name) ? "<unnamed>" : name;
            PlaybookId = playbookId;
            Rects = rects ?? System.Array.Empty<PlayRect>();
        }

        public string Name { get; }
        public BattlePlanId PlaybookId { get; }
        public IReadOnlyList<PlayRect> Rects { get; }
    }
}
