using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;

namespace WhiskeyRealism.Tactical.Operations
{
    public readonly struct TacticalOperationDirectorInput
    {
        private TacticalOperationDirectorInput(
            OperationRecord current,
            float currentTimeSeconds,
            float ownStrength,
            float reserveFraction,
            float aggression01,
            float caution01,
            BattlefieldObjectiveEstimate[] objectives,
            float operationAssignedStrength = 0f,
            BattleStance stance = BattleStance.Meeting,
            IFeasibilityLookup feasibility = null,
            string latchedObjectiveId = null)
        {
            Current = current;
            CurrentTimeSeconds = SanitizeFloorZero(currentTimeSeconds);
            OwnStrength = SanitizeFloorZero(ownStrength);
            ReserveFraction = Clamp01(reserveFraction);
            Aggression01 = Clamp01(aggression01);
            Caution01 = Clamp01(caution01);
            Objectives = objectives ?? Array.Empty<BattlefieldObjectiveEstimate>();
            OperationAssignedStrength = SanitizeFloorZero(operationAssignedStrength);
            Stance = stance;
            Feasibility = feasibility;
            LatchedObjectiveId = latchedObjectiveId;
        }

        public OperationRecord Current { get; }
        public float CurrentTimeSeconds { get; }
        public float OwnStrength { get; }
        public float ReserveFraction { get; }
        public float Aggression01 { get; }
        public float Caution01 { get; }
        public BattlefieldObjectiveEstimate[] Objectives { get; }
        // Live strength of the command nodes the committed operation has tasked to
        // attack/support (summed by the coordinator from vanilla regiments). 0 = no
        // assignment yet (e.g. the first tick after commitment) -> army strength.
        public float OperationAssignedStrength { get; }
        // The side's battle stance (vanilla GetBattleType via
        // ArmyEvidenceBuilder.ReadBattleStance). A Defender is never sent probing.
        public BattleStance Stance { get; }

        // BC14O: the side's feasibility ledger (its objective table and the pressure of the victory clock) and the main-effort objective latched at the
        // last pick. Null Feasibility is the shipped director: the best-scoring objective, ParallelObjectives open.
        public IFeasibilityLookup Feasibility { get; }
        public string LatchedObjectiveId { get; }

        /// <summary>Enable Objective Doctrine is on and the side has a ledger: the main effort is chosen by <see cref="ObjectiveScoring.PickMainEffort"/>.</summary>
        public bool ObjectiveDoctrine => Feasibility != null && Plugin.ObjectiveDoctrineEnabled();

        public static TacticalOperationDirectorInput ForTest(
            OperationRecord current,
            float currentTimeSeconds,
            float ownStrength,
            float reserveFraction,
            float aggression01,
            float caution01,
            BattlefieldObjectiveEstimate[] objectives)
        {
            return new TacticalOperationDirectorInput(
                current,
                currentTimeSeconds,
                ownStrength,
                reserveFraction,
                aggression01,
                caution01,
                objectives);
        }

        public TacticalOperationDirectorInput WithOperationAssignedStrength(float assignedStrength)
        {
            return new TacticalOperationDirectorInput(
                Current,
                CurrentTimeSeconds,
                OwnStrength,
                ReserveFraction,
                Aggression01,
                Caution01,
                Objectives,
                assignedStrength,
                Stance,
                Feasibility,
                LatchedObjectiveId);
        }

        public TacticalOperationDirectorInput WithStance(BattleStance stance)
        {
            return new TacticalOperationDirectorInput(
                Current,
                CurrentTimeSeconds,
                OwnStrength,
                ReserveFraction,
                Aggression01,
                Caution01,
                Objectives,
                OperationAssignedStrength,
                stance,
                Feasibility,
                LatchedObjectiveId);
        }

        /// <summary>BC14O: the side's ledger and the objective latched at the last pick (null: none).</summary>
        public TacticalOperationDirectorInput WithObjectiveDoctrine(IFeasibilityLookup feasibility, string latchedObjectiveId)
        {
            return new TacticalOperationDirectorInput(
                Current,
                CurrentTimeSeconds,
                OwnStrength,
                ReserveFraction,
                Aggression01,
                Caution01,
                Objectives,
                OperationAssignedStrength,
                Stance,
                feasibility,
                latchedObjectiveId);
        }

        private static float SanitizeFloorZero(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }

    public readonly struct TacticalOperationDirectorDecision
    {
        public TacticalOperationDirectorDecision(OperationRecord operation, string reason, ObjectivePick pick = default(ObjectivePick))
        {
            Operation = operation;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
            Pick = pick;
        }

        public OperationRecord Operation { get; }
        public string Reason { get; }

        /// <summary>BC14O: the main-effort objective this decision was made with (<see cref="ObjectivePick.Has"/> false with the doctrine off or nothing feasible).</summary>
        public ObjectivePick Pick { get; }

        public TacticalOperationDirectorDecision WithPick(ObjectivePick pick) => new TacticalOperationDirectorDecision(Operation, Reason, pick);
    }

    /// <summary>
    /// BC14O (requirement-main-effort-objective): the main-effort objective <see cref="ObjectiveScoring.PickMainEffort"/> chose among the director's
    /// objectives, with the numbers its telemetry row carries. The pick is the army's main effort (the plan's band follows it); the operation's primary
    /// objective is the same unless the side perceives contact elsewhere (the shipped contact-elsewhere rule).
    /// </summary>
    public readonly struct ObjectivePick
    {
        public ObjectivePick(int index, BattlefieldObjectiveEstimate objective, bool hasRow, ObjectiveEstimate row, float total, string runnerUpId, float runnerUpTotal)
        {
            Has = true;
            Index = index;
            Objective = objective;
            HasRow = hasRow;
            Row = row;
            Total = total;
            RunnerUpId = runnerUpId;
            RunnerUpTotal = runnerUpTotal;
        }

        public bool Has { get; }
        /// <summary>The objective's index in the director's objectives.</summary>
        public int Index { get; }
        public BattlefieldObjectiveEstimate Objective { get; }
        /// <summary>The ledger's table has a row for it (<see cref="Row"/>: its value, penalty, prior, hold and score without the director's terms).</summary>
        public bool HasRow { get; }
        public ObjectiveEstimate Row { get; }
        /// <summary>The score with the director's terms: what the pick compared.</summary>
        public float Total { get; }
        /// <summary>The best feasible rival (null with none) and its total.</summary>
        public string RunnerUpId { get; }
        public float RunnerUpTotal { get; }
    }

    // T14 (2026-10-14): a committed operation no longer flickers Committed <->
    // Scouting on a one-tick confidence dip.
    //   - Emergency exits ignore the commit window: contact lost, odds collapse, and a
    //     contact revealed as a screen (main line no longer exposed).
    //   - Inside the commit window nothing else demotes the operation.
    //   - Enter/exit hysteresis: commitment needs formed main-line evidence
    //     (confidence >= 0.65, TacticalDecisionDoctrine.MainLineConfidenceFloor);
    //     a committed operation is only downgraded once confidence falls below
    //     CommittedExitConfidenceFloor (0.5), and re-selection after the window does
    //     not demote it while that exit evidence still holds.
    //   - Once committed, odds are the operation's assigned force (attack/support nodes,
    //     OperationAssignedStrength) against the objective-local enemy, not the whole
    //     army (totalactiveforce) against a local enemy. Before commitment nothing is
    //     assigned yet, so the commit decision still weighs the available army.
    public static class TacticalOperationDirector
    {
        public const float CommittedExitConfidenceFloor = 0.50f;

        public static TacticalOperationDirectorDecision Decide(TacticalOperationDirectorInput input)
        {
            ObjectivePick pick = PickMainEffortObjective(input);
            TacticalOperationDirectorDecision decision = DecideForAnyStance(input, pick);
            if (input.Stance == BattleStance.Defender) decision = ForDefender(decision);
            return decision.WithPick(pick);
        }

        // BC9: a Defender (vanilla GetBattleType 2) is never committed to, nor left in,
        // an offensive shape, whatever path selected it (a new selection, a commit made
        // before the stance was known, a downgrade that keeps the committed shape, the
        // commit window or hysteresis holding the current operation): once an objective
        // is selected, every shape but DefensiveNetwork / DelayAndFallback becomes
        // DefensiveNetwork, and a
        // Committed / Exploiting phase becomes Forming. Scouting stays Scouting (its
        // screens screen; CommandDoctrineAssignment's stance guard turns any Probe into
        // Screen). Its cavalry still scouts (the recon producer).
        private static TacticalOperationDirectorDecision ForDefender(TacticalOperationDirectorDecision decision)
        {
            OperationRecord op = decision.Operation;
            bool defensiveShape = op.Shape == TacticalOperationShape.DefensiveNetwork ||
                op.Shape == TacticalOperationShape.DelayAndFallback;
            bool offensivePhase = op.Phase == TacticalOperationPhase.Committed ||
                op.Phase == TacticalOperationPhase.Exploiting;
            // Planning: nothing selected yet (no objective), nothing to send forward.
            if ((defensiveShape && !offensivePhase) || op.Phase == TacticalOperationPhase.Planning) return decision;
            TacticalOperationPhase phase = offensivePhase ? TacticalOperationPhase.Forming : op.Phase;
            return new TacticalOperationDirectorDecision(
                new OperationRecord(
                    defensiveShape ? op.Shape : TacticalOperationShape.DefensiveNetwork,
                    phase,
                    op.PrimaryObjectiveId,
                    op.MinimumCommitSeconds),
                phase == TacticalOperationPhase.Scouting ? "defender-screen" : "defender-hold");
        }

        private static TacticalOperationDirectorDecision DecideForAnyStance(TacticalOperationDirectorInput input, in ObjectivePick pick)
        {
            bool activeOperation = IsActiveOperation(input.Current.Phase);
            bool committed = input.Current.Phase == TacticalOperationPhase.Committed;
            bool insideCommitWindow = committed && input.CurrentTimeSeconds < input.Current.MinimumCommitSeconds;
            if (committed &&
                TryEvaluateCommittedContact(input, insideCommitWindow, out TacticalOperationDirectorDecision committedContactDecision))
            {
                return committedContactDecision;
            }

            if (insideCommitWindow)
            {
                return new TacticalOperationDirectorDecision(input.Current, "commit-window");
            }

            TacticalOperationDirectorDecision selected = SelectOperation(input, activeOperation, in pick);
            if (committed && IsDemotionOfSameObjective(input.Current, selected.Operation) && HoldsCommittedExitEvidence(input))
            {
                return new TacticalOperationDirectorDecision(input.Current, "commit-hysteresis");
            }

            return selected;
        }

        private static TacticalOperationDirectorDecision SelectOperation(TacticalOperationDirectorInput input, bool activeOperation, in ObjectivePick pick)
        {

            if (activeOperation &&
                !TryFindObjective(input.Objectives, input.Current.PrimaryObjectiveId, out _))
            {
                return new TacticalOperationDirectorDecision(input.Current, "objective-picture-missing");
            }

            if (!TryPickBestObjective(input.Objectives, input.Stance, in pick, out BattlefieldObjectiveEstimate primary))
            {
                return activeOperation
                    ? new TacticalOperationDirectorDecision(input.Current, "objective-picture-missing")
                    : new TacticalOperationDirectorDecision(OperationRecord.Noop, "no-objective");
            }

            // A side that perceives the enemy does not scout for him. The best-scoring objective can be an empty map
            // objective on strong ground while the side's picture already holds a formed enemy line elsewhere (the
            // VisibleEnemyLine objective, fog-honest: only what the side sees). An operation not yet on the empty
            // objective then works the perceived contact; a Defender forms its line (DefensiveNetwork, Forming) on that
            // best-scoring objective, the ledger's own pick of ground (ObjectiveSelectionScore), and does not walk to the
            // objective the enemy was perceived at (smoke 2026-10-17: both sides stayed Scouting through heavy contact).
            if (primary.EnemyStrength <= 0f &&
                !(activeOperation && ObjectiveIdMatches(input.Current.PrimaryObjectiveId, primary.ObjectiveId)) &&
                TryPickPerceivedContact(input.Objectives, input.Stance, out BattlefieldObjectiveEstimate contact))
            {
                if (input.Stance == BattleStance.Defender)
                {
                    return new TacticalOperationDirectorDecision(
                        new OperationRecord(
                            TacticalOperationShape.DefensiveNetwork,
                            TacticalOperationPhase.Forming,
                            primary.ObjectiveId,
                            input.CurrentTimeSeconds + 900f),
                        "contact-elsewhere-hold");
                }

                primary = contact;
            }

            float operationStrength = OperationStrength(input);
            TacticalObjectiveGate objectiveGate = TacticalDecisionDoctrine.ClassifyObjective(
                primary,
                operationStrength,
                input.ReserveFraction);

            // BC9: no enemy seen at a newly selected objective means scouting, never a
            // commit on own / max(1, 0) odds: the fog-honest AI does not know the
            // objective is empty, so the recon producer and the Probe task go and look.
            // An operation already active on that objective keeps the shipped rules
            // (a committed advance on an empty known map objective continues,
            // ShouldCancelCommittedContact).
            if (primary.EnemyStrength <= 0f &&
                !(activeOperation && ObjectiveIdMatches(input.Current.PrimaryObjectiveId, primary.ObjectiveId)))
            {
                return new TacticalOperationDirectorDecision(
                    new OperationRecord(
                        TacticalOperationShape.FixAndFlank,
                        TacticalOperationPhase.Scouting,
                        primary.ObjectiveId,
                        input.CurrentTimeSeconds + 420f),
                    "no-contact-scout");
            }

            if (objectiveGate == TacticalObjectiveGate.ReconnaissanceContact)
            {
                return new TacticalOperationDirectorDecision(
                    new OperationRecord(
                        TacticalOperationShape.FixAndFlank,
                        TacticalOperationPhase.Scouting,
                        primary.ObjectiveId,
                        input.CurrentTimeSeconds + 420f),
                    "recon-contact");
            }

            if (CanParallelAttack(input))
            {
                return new TacticalOperationDirectorDecision(
                    new OperationRecord(
                        TacticalOperationShape.ParallelObjectives,
                        TacticalOperationPhase.Committed,
                        primary.ObjectiveId,
                        input.CurrentTimeSeconds + 1200f),
                    "parallel-advantage");
            }

            if (objectiveGate == TacticalObjectiveGate.ExposedWeakPoint)
            {
                return new TacticalOperationDirectorDecision(
                    new OperationRecord(
                        TacticalOperationShape.FixAndFlank,
                        TacticalOperationPhase.Committed,
                        primary.ObjectiveId,
                        input.CurrentTimeSeconds + 900f),
                    "fix-and-flank");
            }

            float odds = Odds(operationStrength, primary.EnemyStrength);
            TacticalOperationShape shape = odds >= 1.35f
                ? TacticalOperationShape.SingleMainEffort
                : TacticalOperationShape.DefensiveNetwork;
            TacticalOperationPhase phase = odds >= 1.35f
                ? TacticalOperationPhase.Committed
                : TacticalOperationPhase.Forming;

            return new TacticalOperationDirectorDecision(
                new OperationRecord(shape, phase, primary.ObjectiveId, input.CurrentTimeSeconds + 900f),
                "selected");
        }

        private static TacticalOperationDirectorDecision Downgrade(TacticalOperationDirectorInput input)
        {
            return new TacticalOperationDirectorDecision(
                new OperationRecord(
                    input.Current.Shape,
                    TacticalOperationPhase.Scouting,
                    input.Current.PrimaryObjectiveId,
                    input.CurrentTimeSeconds + 420f),
                "contact-downgraded");
        }

        private static bool TryEvaluateCommittedContact(
            TacticalOperationDirectorInput input,
            bool insideCommitWindow,
            out TacticalOperationDirectorDecision decision)
        {
            decision = default(TacticalOperationDirectorDecision);
            if (!TryFindObjective(input.Objectives, input.Current.PrimaryObjectiveId, out BattlefieldObjectiveEstimate primary))
            {
                return false;
            }

            if (TacticalDecisionDoctrine.ShouldCancelCommittedContact(primary))
            {
                decision = SoftAbort(input, "contact-lost");
                return true;
            }

            if (TacticalDecisionDoctrine.ShouldSoftAbortCommitted(
                    primary,
                    OperationStrength(input),
                    input.ReserveFraction))
            {
                decision = SoftAbort(input, "odds-collapse");
                return true;
            }

            // The contact turned out to be a screen (enemy present, main line no longer
            // exposed): not a confidence dip, so it may break the commitment at once.
            if (primary.EnemyStrength > 0f && !primary.MainLineExposed)
            {
                decision = Downgrade(input);
                return true;
            }

            if (insideCommitWindow) return false;

            if (primary.EnemyStrength > 0f && primary.Confidence01 < CommittedExitConfidenceFloor)
            {
                decision = Downgrade(input);
                return true;
            }

            return false;
        }

        // Re-selection after the commit window would demote (Scouting / Forming) the
        // same objective.
        private static bool IsDemotionOfSameObjective(OperationRecord current, OperationRecord selected)
        {
            if (selected.Phase != TacticalOperationPhase.Scouting && selected.Phase != TacticalOperationPhase.Forming)
                return false;
            return ObjectiveIdMatches(current.PrimaryObjectiveId, selected.PrimaryObjectiveId);
        }

        // Exit-side evidence for keeping a commitment: main line still exposed, enemy
        // still present, confidence at or above the exit floor. Odds collapse was already
        // handled by the soft-abort branch.
        private static bool HoldsCommittedExitEvidence(TacticalOperationDirectorInput input)
        {
            if (!TryFindObjective(input.Objectives, input.Current.PrimaryObjectiveId, out BattlefieldObjectiveEstimate primary))
                return false;
            return primary.MainLineExposed
                && primary.EnemyStrength > 0f
                && primary.Confidence01 >= CommittedExitConfidenceFloor;
        }

        // Strength actually assigned to a committed operation; the available army when
        // nothing is committed or no node has been tasked yet.
        private static float OperationStrength(TacticalOperationDirectorInput input)
        {
            if (input.Current.Phase == TacticalOperationPhase.Committed && input.OperationAssignedStrength > 0f)
                return input.OperationAssignedStrength;
            return input.OwnStrength;
        }

        private static TacticalOperationDirectorDecision SoftAbort(
            TacticalOperationDirectorInput input,
            string reason)
        {
            return new TacticalOperationDirectorDecision(
                new OperationRecord(
                    input.Current.Shape,
                    TacticalOperationPhase.SoftAbort,
                    input.Current.PrimaryObjectiveId,
                    input.Current.MinimumCommitSeconds),
                reason);
        }

        private static bool IsActiveOperation(TacticalOperationPhase phase)
        {
            return phase == TacticalOperationPhase.Committed ||
                phase == TacticalOperationPhase.Forming ||
                phase == TacticalOperationPhase.Exploiting ||
                phase == TacticalOperationPhase.Consolidating ||
                phase == TacticalOperationPhase.SoftAbort;
        }

        private static bool CanParallelAttack(TacticalOperationDirectorInput input)
        {
            // BC14O: one main effort; a second objective is a secondary, screened by the allocator.
            if (input.ObjectiveDoctrine) return false;
            if (input.Objectives.Length < 2 || input.ReserveFraction < 0.15f)
            {
                return false;
            }

            float requiredOdds = 1.65f + input.Caution01 * 0.2f;
            if (input.Aggression01 >= 0.7f)
            {
                requiredOdds -= 0.15f;
            }

            float enemyStrength = 0f;
            int usable = 0;
            for (int i = 0; i < input.Objectives.Length; i++)
            {
                BattlefieldObjectiveEstimate objective = input.Objectives[i];
                if (!IsUsableObjective(objective) ||
                    !objective.MainLineExposed ||
                    objective.Confidence01 < 0.7f)
                {
                    continue;
                }

                enemyStrength += Max(1f, objective.EnemyStrength);
                usable++;
            }

            return usable >= 2 && Odds(input.OwnStrength, enemyStrength) >= requiredOdds;
        }

        private static bool TryPickBestObjective(
            BattlefieldObjectiveEstimate[] objectives,
            BattleStance stance,
            in ObjectivePick pick,
            out BattlefieldObjectiveEstimate best)
        {
            // BC14O: the main-effort objective, when the doctrine made a pick; otherwise (doctrine off, nothing feasible) the shipped best score.
            if (pick.Has)
            {
                best = pick.Objective;
                return true;
            }
            best = default(BattlefieldObjectiveEstimate);
            float bestScore = float.NegativeInfinity;
            bool found = false;

            for (int i = 0; i < objectives.Length; i++)
            {
                BattlefieldObjectiveEstimate objective = objectives[i];
                if (!IsUsableObjective(objective))
                {
                    continue;
                }

                float score = ObjectiveSelectionScore(objective, stance);
                if (!found || score > bestScore)
                {
                    best = objective;
                    bestScore = score;
                    found = true;
                }
            }

            return found;
        }

        // The best-scoring objective the side perceives enemy at (the same score as the primary pick), if any.
        private static bool TryPickPerceivedContact(
            BattlefieldObjectiveEstimate[] objectives,
            BattleStance stance,
            out BattlefieldObjectiveEstimate best)
        {
            best = default(BattlefieldObjectiveEstimate);
            float bestScore = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < objectives.Length; i++)
            {
                BattlefieldObjectiveEstimate objective = objectives[i];
                if (!IsUsableObjective(objective) || objective.EnemyStrength <= 0f) continue;
                float score = ObjectiveSelectionScore(objective, stance);
                if (!found || score > bestScore)
                {
                    best = objective;
                    bestScore = score;
                    found = true;
                }
            }

            return found;
        }

        // BC14O one chooser (requirement-main-effort-objective): the director's objectives scored together. A map objective with a row in the ledger's
        // table brings the row's score (value, risk, prior, hold); an infeasible row is out. Any other entry (the enemy line, a recon contact, an own-held
        // objective nobody threatens) scores its value times (1 + U). The director's own terms (every term of ObjectiveSelectionScore but Value) are
        // added to each, and the latched objective keeps the main effort unless a rival wins by the switch margin. Main thread only: the buffers are
        // pooled.
        private static ObjectiveEstimate[] _pickTable = new ObjectiveEstimate[32];
        private static float[] _pickTerms = new float[32];
        private static int[] _pickMap = new int[32];

        internal static ObjectivePick PickMainEffortObjective(TacticalOperationDirectorInput input)
        {
            if (!input.ObjectiveDoctrine) return default(ObjectivePick);
            IFeasibilityLookup ledger = input.Feasibility;
            BattlefieldObjectiveEstimate[] objectives = input.Objectives;
            if (_pickTable.Length < objectives.Length)
            {
                int size = Math.Max(objectives.Length, _pickTable.Length * 2);
                _pickTable = new ObjectiveEstimate[size];
                _pickTerms = new float[size];
                _pickMap = new int[size];
            }
            ObjectivePressure pressure = ledger.Pressure;
            int count = 0;
            for (int i = 0; i < objectives.Length; i++)
            {
                BattlefieldObjectiveEstimate objective = objectives[i];
                if (!IsUsableObjective(objective)) continue;
                ObjectiveEstimate entry;
                if (TryLedgerRow(ledger, objective.ObjectiveId, out entry))
                {
                    if (!entry.Feasible) continue;
                    entry.Id = objective.ObjectiveId;
                }
                else entry = new ObjectiveEstimate { Id = objective.ObjectiveId, Value = objective.Value, Feasible = true, Score = objective.Value * (1f + pressure.U) };
                _pickTable[count] = entry;
                _pickTerms[count] = ObjectiveSelectionScore(objective, input.Stance) - objective.Value;
                _pickMap[count] = i;
                count++;
            }
            var table = new ReadOnlySpan<ObjectiveEstimate>(_pickTable, 0, count);
            var terms = new ReadOnlySpan<float>(_pickTerms, 0, count);
            int at = ObjectiveScoring.PickMainEffort(table, terms, input.LatchedObjectiveId);
            if (at < 0) return default(ObjectivePick);

            string runnerUp = null;
            float runnerUpTotal = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (i == at || !_pickTable[i].Feasible) continue;
                float total = _pickTable[i].Score + _pickTerms[i];
                if (total > runnerUpTotal)
                {
                    runnerUpTotal = total;
                    runnerUp = _pickTable[i].Id;
                }
            }
            bool hasRow = TryLedgerRow(ledger, objectives[_pickMap[at]].ObjectiveId, out ObjectiveEstimate row);
            return new ObjectivePick(_pickMap[at], objectives[_pickMap[at]], hasRow, row, _pickTable[at].Score + _pickTerms[at], runnerUp, runnerUpTotal);
        }

        // The ledger's row for a director objective, by the director's own id match.
        private static bool TryLedgerRow(IFeasibilityLookup ledger, string objectiveId, out ObjectiveEstimate row)
        {
            for (int r = 0; r < ledger.ObjectiveCount; r++)
            {
                if (ledger.TryObjective(r, out row) && ObjectiveIdMatches(row.Id, objectiveId)) return true;
            }
            row = default(ObjectiveEstimate);
            return false;
        }

        // BC12 decision 5. An enemy-held objective is harder the stronger its ground and the harder its approach (the shipped
        // sign); an own or neutral one is worth more the stronger its ground (seize strong ground), and for a Defender the
        // harder the enemy's approach to it, at half weight.
        internal static float ObjectiveSelectionScore(BattlefieldObjectiveEstimate objective, BattleStance stance)
        {
            float score = objective.Value + objective.Confidence01;
            if (objective.EnemyStrength > 0f)
            {
                score -= objective.TerrainStrength + objective.ApproachDifficulty;
            }
            else
            {
                score += objective.TerrainStrength;
                if (stance == BattleStance.Defender) score += 0.5f * objective.ApproachDifficulty;
            }

            if (objective.MainLineExposed)
            {
                score += 0.35f;
            }

            if (objective.Type == TacticalObjectiveType.EnemyLine)
            {
                score += 0.20f;
            }

            if (objective.Type == TacticalObjectiveType.UnknownVanillaObjective &&
                !objective.MainLineExposed &&
                objective.EnemyStrength <= 0f)
            {
                score -= 0.25f;
            }

            return score;
        }

        private static bool TryFindObjective(
            BattlefieldObjectiveEstimate[] objectives,
            string objectiveId,
            out BattlefieldObjectiveEstimate match)
        {
            match = default(BattlefieldObjectiveEstimate);
            if (string.IsNullOrWhiteSpace(objectiveId))
            {
                return false;
            }

            for (int i = 0; i < objectives.Length; i++)
            {
                BattlefieldObjectiveEstimate objective = objectives[i];
                if (ObjectiveIdMatches(objective.ObjectiveId, objectiveId))
                {
                    match = objective;
                    return true;
                }
            }

            return false;
        }

        private static bool ObjectiveIdMatches(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.Ordinal)) return true;
            return string.Equals(NormalizeObjectiveId(left), NormalizeObjectiveId(right), StringComparison.Ordinal);
        }

        private static string NormalizeObjectiveId(string objectiveId)
        {
            if (string.IsNullOrWhiteSpace(objectiveId)) return string.Empty;
            char[] chars = objectiveId.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsWhiteSpace(chars[i]) || chars[i] == '|')
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        private static bool IsUsableObjective(BattlefieldObjectiveEstimate objective)
        {
            return !string.IsNullOrWhiteSpace(objective.ObjectiveId) &&
                objective.Confidence01 > 0f;
        }

        private static float Odds(float ownStrength, float enemyStrength)
        {
            return ownStrength / Max(1f, enemyStrength);
        }

        private static float Max(float left, float right)
        {
            return left > right ? left : right;
        }
    }
}
