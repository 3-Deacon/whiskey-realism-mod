using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum EchelonKind
    {
        Unknown = 0,
        Army = 1,
        Corps = 2,
        Division = 3,
        Brigade = 4,
    }

    public abstract class EchelonOrchestrator
    {
        protected EchelonOrchestrator(EchelonKind kind, int allianceId)
        {
            Kind = kind;
            AllianceId = allianceId;
            Children = new List<EchelonOrchestrator>();
        }

        public EchelonKind Kind { get; }
        public int AllianceId { get; }
        public EchelonOrchestrator Parent { get; private set; }
        public List<EchelonOrchestrator> Children { get; }

        public void AddChild(EchelonOrchestrator child)
        {
            if (child == null) return;
            child.Parent = this;
            Children.Add(child);
        }

        public virtual void Tick()
        {
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i]?.Tick();
            }
        }

        public virtual void PropagateIntent()
        {
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i]?.PropagateIntent();
            }
        }
    }
}
