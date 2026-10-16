using System;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // What the construction steering patches read (plan G5c): one immutable picture per alliance, built inside the daily review from
    // the snapshot (the railroad geometry and the supply sources are the snapshot's) and swapped in whole, so a patch on the alliance
    // tick sees yesterday's picture or today's, never half of either. It is a preference only: the railroad Prefix and the fort
    // filter keep vanilla's own rates and tests, so a stale board costs a worse choice, never a wrong one, and a board older than
    // PlanFreshness.MaxAgeDays is ignored.
    internal sealed class ConstructionBoard
    {
        public int DaySerial;
        // RailScore[i]: the steering preference of BattleUnits.railroad[i]; ConstructionSteeringBoard.ExcludedRailScore for a line
        // the construction ledger suppressed.
        public float[] RailScore = new float[0];
        // FortPreferred[i] / FortRank[i]: AICampaign.fortconstructionsites[i] lies on a MainEffort axis or in a threatened sector,
        // and the construction ledger's score for it.
        public bool[] FortPreferred = new bool[0];
        public float[] FortRank = new float[0];
    }

    internal static class ConstructionSteeringBoard
    {
        // A suppressed line sorts below every other score, so it starts only when nothing else is eligible (the count of starts
        // is vanilla's; the CSA rail cap only decides which line, never how many).
        public const float ExcludedRailScore = -1f;
        // The ledger's top Railroad / Fort candidate wins an otherwise equal score.
        public const float TopBonus = 0.001f;

        private static readonly ConstructionBoard[] Boards = new ConstructionBoard[2];

        static ConstructionSteeringBoard()
        {
            CampaignScope.Register("campaign-planner-construction-board", Reset);
        }

        private static void Reset()
        {
            for (int a = 0; a < Boards.Length; a++) Boards[a] = null;
        }

        internal static void Publish(int alliance, ConstructionBoard board)
        {
            if (alliance >= 0 && alliance < Boards.Length) Boards[alliance] = board;
        }

        internal static bool TryRead(int alliance, out ConstructionBoard board)
        {
            board = alliance >= 0 && alliance < Boards.Length ? Boards[alliance] : null;
            if (board == null) return false;
            if (PlanFreshness.IsFresh(board.DaySerial)) return true;
            board = null;
            return false;
        }

        // fortX/fortZ: AICampaign.fortconstructionsites; fortRange: GamePrefs.rangefortconstructionsite. construction: the last
        // construction pass (weekly; null before the first), whose Railroad and Fort candidates carry their list index.
        internal static ConstructionBoard Build(CampaignSnapshot s, TheaterPlan plan, SectorModel sectors, ConstructionOutput construction,
                                                float[] fortX, float[] fortZ, float fortRange)
        {
            var board = new ConstructionBoard { DaySerial = s.DaySerial };
            BuildRails(board, s, plan, sectors, construction);
            BuildForts(board, s, plan, sectors, construction, fortX, fortZ, fortRange);
            return board;
        }

        // Each line's score is its geometry (RailLineScorer) times the construction ledger's Railroad candidate score for that
        // line, a suppressed line (the CSA rail cap) is excluded, and the ledger's TopRailroad wins a tie.
        private static void BuildRails(ConstructionBoard board, CampaignSnapshot s, TheaterPlan plan, SectorModel sectors, ConstructionOutput construction)
        {
            int count = 0;
            for (int i = 0; i < s.Rails.Count; i++) count = Math.Max(count, s.Rails[i].Index + 1);
            board.RailScore = new float[count];
            for (int i = 0; i < s.Rails.Count; i++)
            {
                var line = s.Rails[i];
                float geometry = RailLineScorer.Score(line, s, plan, sectors);
                float ledger = 1f;
                bool excluded = false;
                if (construction != null)
                {
                    if (TryCandidate(construction, ConstructionCandidateKind.Railroad, line.Index, out var candidate)) ledger = candidate.Score;
                    excluded = IsSuppressed(construction, ConstructionCandidateKind.Railroad, line.Index);
                }
                float score = geometry * ledger;
                if (construction != null && construction.TopRailroad.Kind == ConstructionCandidateKind.Railroad && construction.TopRailroad.SiteIndex == line.Index)
                    score += TopBonus;
                board.RailScore[line.Index] = excluded ? ExcludedRailScore : score;
            }
        }

        // A site is preferred when it lies within fortRange of the MainEffort axis, or in a Hold / Screen sector; its rank is the
        // ledger's Fort candidate score (TopFort carries the bonus).
        private static void BuildForts(ConstructionBoard board, CampaignSnapshot s, TheaterPlan plan, SectorModel sectors, ConstructionOutput construction,
                                       float[] fortX, float[] fortZ, float fortRange)
        {
            int n = fortX != null && fortZ != null ? Math.Min(fortX.Length, fortZ.Length) : 0;
            board.FortPreferred = new bool[n];
            board.FortRank = new float[n];
            bool axis = RailLineScorer.MainAxis(s, plan, out float fromX, out float fromZ, out float toX, out float toZ);
            for (int i = 0; i < n; i++)
            {
                bool onAxis = axis && RailLineScorer.DistanceToSegment(fortX[i], fortZ[i], fromX, fromZ, toX, toZ) <= fortRange;
                bool threatened = plan != null && sectors != null && RailLineScorer.Threatened(plan, sectors, fortX[i], fortZ[i]);
                board.FortPreferred[i] = onAxis || threatened;
                if (construction == null) continue;
                if (TryCandidate(construction, ConstructionCandidateKind.Fort, i, out var candidate)) board.FortRank[i] = candidate.Score;
                if (construction.TopFort.Kind == ConstructionCandidateKind.Fort && construction.TopFort.SiteIndex == i) board.FortRank[i] += TopBonus;
            }
        }

        // The ledger's scored candidate of this kind for list index siteIndex.
        private static bool TryCandidate(ConstructionOutput construction, ConstructionCandidateKind kind, int siteIndex, out ConstructionCandidate candidate)
        {
            var scored = construction.SiteCandidates;
            for (int i = 0; scored != null && i < scored.Length; i++)
            {
                if (scored[i].Kind != kind || scored[i].SiteIndex != siteIndex) continue;
                candidate = scored[i];
                return true;
            }
            candidate = default(ConstructionCandidate);
            return false;
        }

        private static bool IsSuppressed(ConstructionOutput construction, ConstructionCandidateKind kind, int siteIndex)
        {
            if (construction.Suppressions == null) return false;
            for (int i = 0; i < construction.Suppressions.Length; i++)
            {
                var sup = construction.Suppressions[i];
                if (sup.Kind == kind && sup.SiteIndex == siteIndex) return true;
            }
            return false;
        }
    }
}
