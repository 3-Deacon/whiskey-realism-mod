using System;
using System.Collections.Generic;
using System.Text;

namespace WhiskeyRealism.Strategic.Planner
{
    // One alliance's main-effort memory across reviews (plan P4/P5): the current main effort, the
    // challenger the hysteresis is counting, the reviews the current main effort has spent just
    // below its required odds (the odds deadband), the capital-threat latch (the trigger fires on
    // onset only), the decision serial and the campaign seed. Written
    // only by TheaterPlanner.Plan inside the daily review; persisted by the sidecar (G6, through
    // Restore). CampaignSeed 0 means "not computed yet": the first review computes it.
    public sealed class PlannerState
    {
        public int CurrentMainObjective = -1;
        public int ChallengerObjective = -1;
        public int ChallengerDays;
        public int DecisionSerial;
        public long CampaignSeed;
        public int BelowOddsDays;
        public bool CapitalThreatLatched;
        // G5a (chapters decision 5): the DecisionSerial whose main-effort launch the chapter tempo roll
        // released, -1 for none. `TempoReleasedDecision == DecisionSerial` is the one release predicate
        // (the mover sets it on a passed, forced or debug roll and before the first MainEffort dispatch
        // of a decision that needs no roll); DecisionSerial starts at 0 and is 1 at the first decision, so
        // -1 never reads as released. Persisted (PlannerStateDto.tempoReleasedDecision, G6).
        public int TempoReleasedDecision = -1;
        // The day serial of the previous review that ran the executors (PlannerReviewMode.Full with
        // PlannerChapterRules.ExecutorsAllowed): the tempo clock's reference (CampaignTempoGate.TimeDiffYears).
        // -1 none. Persisted (PlannerStateDto.lastFullReviewDay, G6).
        public int LastFullReviewDay = -1;
        // The decision whose first [CampaignTempo] row was emitted (bounds the rows to two per decision).
        // Telemetry only, not persisted: after a reload a decision may log its first row once more.
        public int TempoRowDecision = -1;
        // The decision whose undispatchable-main-effort row was emitted (one row when a kept main effort first has no formation
        // the mover can send; cleared when it has one again). Telemetry only, not persisted.
        public int UndispatchableRowDecision = -1;
        // D6 (smoke amendments 2026-10-16): the one formation from outside the main effort's theater that may join this decision's mass
        // (the Longstreet-to-Chickamauga reinforcement), by unit key, and the decision it was picked for. A later decision picks afresh.
        // Persisted (PlannerStateDto.reinforcementKey / reinforcementDecision).
        public string ReinforcementKey;
        public int ReinforcementDecision = -1;
        // Verify round 1, F1: the DecisionSerial whose main objective was seen scouted. Once a decision's target is scouted it stays scouted
        // for that decision (a march released on a fresh picture is not re-staged when the belief decays); a new decision starts unscouted.
        // -1 none. Persisted (PlannerStateDto.scoutedDecision).
        public int ScoutedDecision = -1;
        // Smoke 2026-10-17 (Q3): the ground that was scouted, the position of the objective record whose picture ScoutedDecision latches.
        // One objective id has several target records (29@3, 29@2, 29@6), and the decision follows the id, so a retarget to another record
        // of the same objective is the same decision; the latch speaks for the scouted record only, never for ground nobody has seen.
        // NaN: not recorded (a file written before the field), the latch then speaks for the decision's target as it did. Persisted
        // (PlannerStateDto.scoutedTargetX / scoutedTargetZ).
        public float ScoutedTargetX = float.NaN;
        public float ScoutedTargetZ = float.NaN;
        // Smoke 2026-10-17 (Q3), hysteresis between the target records of one objective: the record the main effort is on (CurrentTarget), and
        // the record that has beaten it by SwitchMargin on RecordChallengerDays consecutive reviews (RecordChallenger). The same margin and
        // the same SwitchDays as the objective switch (TheaterPlanner.HeldRecord). NaN: none (a new decision, or a file written before the
        // fields: the best record is taken at once). Persisted (PlannerStateDto.currentTargetX/Z, recordChallengerX/Z/Days).
        public float CurrentTargetX = float.NaN;
        public float CurrentTargetZ = float.NaN;
        public float RecordChallengerX = float.NaN;
        public float RecordChallengerZ = float.NaN;
        public int RecordChallengerDays;

        // D6 detail (smoke amendments 2026-10-16): each top formation's home department, a Theater, by unit key. A formation is given the
        // theater of the sector it stands in the first time a review sees it and keeps it until a deliberate cross-department transfer
        // (Transfer); the main effort draws from its own department first. Persisted (PlannerStateDto.departments). A commander change
        // re-keys a formation (name:commander): Observe carries the department over by instance id within a session.
        private readonly Dictionary<string, Theater> _departments = new Dictionary<string, Theater>(StringComparer.Ordinal);
        private readonly Dictionary<int, string> _keyOfInstance = new Dictionary<int, string>();
        // The unit keys the current review's Observe calls saw. PruneUnobserved counts the reviews a saved department's key was not seen and
        // drops it after PruneAfterMissedReviews in a row (a formation missing from one snapshot, inactive or briefly invisible, keeps its home).
        private readonly HashSet<string> _observed = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _missed = new Dictionary<string, int>(StringComparer.Ordinal);
        public const int PruneAfterMissedReviews = 10;

        public IReadOnlyDictionary<string, Theater> Departments => _departments;

        // Foresight F6b-2 (D11 requirement-covering-members, -post-choice): the covering force's memory. CoveringKeys is the unit key of each
        // member (and of an incumbent briefly unavailable) -> the aiarea index it stood in when first claimed; the sector it belongs to today is
        // SectorModel.SectorOfArea of it, because sector ids renumber when an objective is accomplished. CoveringForward is the stored verdict
        // (the forward post is the verdict until one is known), CoveringVerdictDays the reviews the other verdict has led, CoveringVerdictKnown
        // whether a verdict was ever stored for CoveringPostKey (the forward and rear town names with their rounded positions, "none" for a missing
        // post; "none|none" while the force is unbound). Written once in TheaterPlanner.Plan after Decide; persisted (PlannerStateDto.coveringKeys).
        private readonly Dictionary<string, int> _coveringKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        public bool CoveringForward;
        public int CoveringVerdictDays;
        public bool CoveringVerdictKnown;
        public string CoveringPostKey;

        public IReadOnlyDictionary<string, int> CoveringKeys => _coveringKeys;

        // The covering force of this review: exactly these members, each with the area it stands in.
        public void SetCoveringKeys(IEnumerable<KeyValuePair<string, int>> keys)
        {
            _coveringKeys.Clear();
            if (keys == null) return;
            foreach (var pair in keys)
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value >= 0) _coveringKeys[pair.Key] = pair.Value;
        }

        // Dedicated restore (never the positional Restore): a record with no key or no area is skipped.
        public void RestoreCovering(IEnumerable<KeyValuePair<string, int>> keys, bool forward, int verdictDays, bool verdictKnown, string postKey)
        {
            SetCoveringKeys(keys);
            CoveringForward = forward;
            CoveringVerdictDays = verdictDays < 0 ? 0 : verdictDays;
            CoveringVerdictKnown = verdictKnown;
            CoveringPostKey = postKey;
        }

        // Foresight F7 (D11 requirement-commitment): the last mass, the main effort's objective and the members the last non-empty mass had, by unit
        // key (the identity that survives a load; a commander change re-keys it with the formation). Written once per review by the plan, after
        // SelectMass (Commitment.UpdateLastMass); read by the claim table's row 4, which commits the members the mover dispatched. Persisted
        // (PlannerStateDto.lastMass). -1 and no keys: there is none.
        public int LastMassObjective = -1;
        private readonly HashSet<string> _lastMassKeys = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyCollection<string> LastMassKeys => _lastMassKeys;

        public bool InLastMass(string unitKey)
        {
            return unitKey != null && _lastMassKeys.Contains(unitKey);
        }

        // The last mass of this review: exactly these members toward this objective. No objective, or no member with a key, clears it.
        public void SetLastMass(int objectiveId, IEnumerable<string> keys)
        {
            _lastMassKeys.Clear();
            if (objectiveId >= 0 && keys != null)
                foreach (string key in keys)
                    if (!string.IsNullOrEmpty(key)) _lastMassKeys.Add(key);
            LastMassObjective = _lastMassKeys.Count > 0 ? objectiveId : -1;
        }

        // Dedicated restore (never the positional Restore): a record with no key is skipped.
        public void RestoreLastMass(int objectiveId, IEnumerable<string> keys)
        {
            SetLastMass(objectiveId, keys);
        }

        // A review's observation pass: BeginObservation, one Observe per own formation, then PruneUnobserved.
        public void BeginObservation()
        {
            _observed.Clear();
        }

        // Drops the saved departments of unit keys the observation passes have not seen for PruneAfterMissedReviews reviews in a row (a
        // formation that no longer exists would otherwise stay in the sidecar for ever; one missing from a single snapshot is not gone).
        // The caller runs it only after a pass that read formations at all, so a failed read counts nothing. Returns how many were dropped.
        public int PruneUnobserved()
        {
            List<string> gone = null;
            foreach (var key in _departments.Keys)
            {
                if (_observed.Contains(key))
                {
                    _missed.Remove(key);
                    continue;
                }
                _missed.TryGetValue(key, out int missed);
                missed++;
                if (missed >= PruneAfterMissedReviews) (gone ?? (gone = new List<string>())).Add(key);
                else _missed[key] = missed;
            }
            if (gone == null) return 0;
            for (int i = 0; i < gone.Count; i++)
            {
                _departments.Remove(gone[i]);
                _missed.Remove(gone[i]);
            }
            return gone.Count;
        }

        // The appointment executor changed the formation's commander, so its unit key (name:commander) changed with it: carry the
        // department to the new key now, not at the next review, so a save between the two keeps it (verify round 1, concern 3).
        public void RekeyInstance(int instanceId, string newKey)
        {
            if (string.IsNullOrEmpty(newKey) || !_keyOfInstance.TryGetValue(instanceId, out var oldKey)) return;
            _keyOfInstance[instanceId] = newKey;
            if (string.Equals(oldKey, newKey, StringComparison.Ordinal)) return;
            if (string.Equals(ReinforcementKey, oldKey, StringComparison.Ordinal)) ReinforcementKey = newKey;
            CarryKeys(oldKey, newKey);
            if (_departments.TryGetValue(oldKey, out var carried))
            {
                _departments.Remove(oldKey);
                _departments[newKey] = carried;
            }
        }

        // Just before the sidecar is written: every formation the planner has seen this session is re-keyed from its live name and
        // commander (`live`: instance id, current unit key), so a commander changed since the last review - by the planner, by vanilla's
        // job 24, by a death - is saved under the key the next load will compute.
        public void RekeyFromLive(IEnumerable<KeyValuePair<int, string>> live)
        {
            if (live == null) return;
            foreach (var pair in live) RekeyInstance(pair.Key, pair.Value);
        }

        // A commander change re-keys a formation (name:commander): its place in the covering force and in the last mass moves with it.
        private void CarryKeys(string oldKey, string newKey)
        {
            if (_coveringKeys.TryGetValue(oldKey, out int area))
            {
                _coveringKeys.Remove(oldKey);
                _coveringKeys[newKey] = area;
            }
            if (_lastMassKeys.Remove(oldKey)) _lastMassKeys.Add(newKey);
        }

        // The formation's department; `standing` (the theater of the sector it stands in) is its first one. Unknown `standing` records nothing.
        public Theater Observe(int instanceId, string unitKey, Theater standing)
        {
            if (string.IsNullOrEmpty(unitKey)) return standing;
            _observed.Add(unitKey);
            if (_keyOfInstance.TryGetValue(instanceId, out var carriedFrom) && !string.Equals(carriedFrom, unitKey, StringComparison.Ordinal))
                CarryKeys(carriedFrom, unitKey);
            if (_departments.TryGetValue(unitKey, out var known))
            {
                _keyOfInstance[instanceId] = unitKey;
                return known;
            }
            if (_keyOfInstance.TryGetValue(instanceId, out var previousKey) && !string.Equals(previousKey, unitKey, StringComparison.Ordinal)
                && _departments.TryGetValue(previousKey, out var carried))
            {
                _departments.Remove(previousKey);
                _departments[unitKey] = carried;
                _keyOfInstance[instanceId] = unitKey;
                return carried;
            }
            _keyOfInstance[instanceId] = unitKey;
            if (standing == Theater.Unknown) return standing;
            _departments[unitKey] = standing;
            return standing;
        }

        public Theater DepartmentOf(string unitKey)
        {
            return unitKey != null && _departments.TryGetValue(unitKey, out var known) ? known : Theater.Unknown;
        }

        // A deliberate transfer (the one reinforcement of a main-effort decision, TheaterPlanner.NoteReinforcement): the formation's department changes.
        public void Transfer(string unitKey, Theater to)
        {
            if (!string.IsNullOrEmpty(unitKey) && to != Theater.Unknown) _departments[unitKey] = to;
        }

        public void RestoreDepartments(IEnumerable<KeyValuePair<string, Theater>> saved)
        {
            _departments.Clear();
            _keyOfInstance.Clear();
            if (saved == null) return;
            foreach (var pair in saved)
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value != Theater.Unknown) _departments[pair.Key] = pair.Value;
        }

        public void Restore(int mainObjective, int challengerObjective, int challengerDays, int decisionSerial, long campaignSeed,
                            int belowOddsDays = 0, bool capitalThreatLatched = false, int tempoReleasedDecision = -1, int lastFullReviewDay = -1,
                            string reinforcementKey = null, int reinforcementDecision = -1, int scoutedDecision = -1,
                            float scoutedTargetX = float.NaN, float scoutedTargetZ = float.NaN,
                            float currentTargetX = float.NaN, float currentTargetZ = float.NaN,
                            float recordChallengerX = float.NaN, float recordChallengerZ = float.NaN, int recordChallengerDays = 0)
        {
            CurrentTargetX = currentTargetX;
            CurrentTargetZ = currentTargetZ;
            RecordChallengerX = recordChallengerX;
            RecordChallengerZ = recordChallengerZ;
            RecordChallengerDays = recordChallengerDays;
            ScoutedDecision = scoutedDecision;
            ScoutedTargetX = scoutedTargetX;
            ScoutedTargetZ = scoutedTargetZ;
            ReinforcementKey = reinforcementKey;
            ReinforcementDecision = reinforcementDecision;
            BelowOddsDays = belowOddsDays;
            CapitalThreatLatched = capitalThreatLatched;
            TempoReleasedDecision = tempoReleasedDecision;
            LastFullReviewDay = lastFullReviewDay;
            TempoRowDecision = -1;
            UndispatchableRowDecision = -1;
            CurrentMainObjective = mainObjective;
            ChallengerObjective = challengerObjective;
            ChallengerDays = challengerDays;
            DecisionSerial = decisionSerial;
            CampaignSeed = campaignSeed;
        }

        // P5: 64-bit FNV-1a over the UTF-8 bytes of "<scenario>|yyyy-mm-dd" (the scenario is
        // SceneManagement.currentcampaignscenario, 36382/36576; the date is the first review's).
        // Never string.GetHashCode, which differs between runtimes.
        public static long ComputeCampaignSeed(string scenario, int year, int month, int day)
        {
            string key = (scenario ?? "") + "|" + year.ToString("D4") + "-" + month.ToString("D2") + "-" + day.ToString("D2");
            return unchecked((long)Fnv1a64(key));
        }

        internal static ulong Fnv1a64(string text)
        {
            ulong hash = 14695981039346656037UL;
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }

    // Finished battles waiting for each alliance's next plan (P4's decisive-battle trigger).
    // StrategicCoordinator.RecordBattleOutcome forwards every new record (it already drops one with
    // no winner) through CampaignPlannerRuntime.OnBattleRecorded. As the R4 rule it replaces
    // (DirtyPlanIfBattleInvalidates for AllianceWon and LosingAlliance), a battle concerns the
    // alliances that fought it: it is queued for each participant of 0 and 1 (Alliance[], the
    // winner and the loser) and each review takes only its own queue. A queue is not drained while
    // the planner pauses (W&L hierarchy -1) or stands down, so Take drops every battle older than
    // MaxBattleAgeDays instead of judging it against today's main effort.
    public sealed class BattleInbox
    {
        public const int MaxPending = 16;
        public const int MaxBattleAgeDays = 7;
        private readonly List<BattleHistoryRecord>[] _pending = { new List<BattleHistoryRecord>(), new List<BattleHistoryRecord>() };

        internal void Add(BattleHistoryRecord record)
        {
            if (record == null) return;
            for (int a = 0; a < _pending.Length; a++)
            {
                if (!Fought(record, a)) continue;
                var list = _pending[a];
                list.Add(record);
                if (list.Count > MaxPending) list.RemoveAt(0);
            }
        }

        private static bool Fought(BattleHistoryRecord record, int alliance)
        {
            if (record.AllianceWon == alliance || record.LosingAlliance == alliance) return true;
            var sides = record.Alliance;
            return sides != null && Array.IndexOf(sides, alliance) >= 0;
        }

        // The alliance's pending battles no older than MaxBattleAgeDays at daySerial
        // (ReviewCadenceCatchUp.DaySerial), moved into `into` (cleared first); the queue empties.
        internal void Take(int alliance, int daySerial, List<BattleHistoryRecord> into)
        {
            into.Clear();
            if (alliance < 0 || alliance >= _pending.Length) return;
            var list = _pending[alliance];
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (daySerial - ReviewCadenceCatchUp.DaySerial(r.Day, r.Month, r.Year) <= MaxBattleAgeDays) into.Add(r);
            }
            list.Clear();
        }

        internal void Clear()
        {
            for (int a = 0; a < _pending.Length; a++) _pending[a].Clear();
        }
    }
}
