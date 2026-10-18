using System;
using System.Collections.Generic;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // One AI alliance's remembered picture of the enemy armies (spec component 2, plan P6, G1).
    // Pure: fed only EnemyDetections (what EnemyDetectionRuntime saw this review), never a vanilla
    // enemy list, destination or plan state. Written only by CampaignPlannerRuntime.ReviewPlan
    // (Update) and by the sidecar restore (Restore, G6); everything else reads Beliefs.
    //
    // Dynamics (P6): a detection sets confidence 1 and radius SeenRadius at its position; an unseen
    // belief keeps its last position with confidence 0.5^(days/7) and radius SeenRadius +
    // DriftPerDay(unittyp) x days, both computed from LastSeenDay (absolute, so a second update on
    // the same day changes nothing). A fort garrison does not move: its radius stays SeenRadius.
    // A belief expires at ExpireDays or below MinConfidence.
    //
    // Identity: beliefs are keyed by UnitKey (name:commander), never by the in-session InstanceId.
    // Per review, in order: (1) two live detections sharing one UnitKey each get the composite key
    // UnitKey#InstanceId and one bounded warning (never merged); (2) exact key match; (3) a
    // detection whose key no belief has re-keys an unmatched belief with the same unit name and
    // UnitTyp within its plausibility radius (its commander changed); (4) a still-new key retires
    // the nearest unmatched belief of the same UnitTyp within its plausibility radius (vanilla
    // renamed the group, 17159); (5) anything left is a new belief. Two armies that are both seen
    // are never merged. The plausibility radius is a normal road march (CampaignSpeedBound
    // .RoadMarchPerDay) x days unseen, at least one day and at most PlausibleDaysCap: identity
    // asks where the group could normally be, not P6's loose uncertainty bound, which is for
    // planning only (integrator decision 2026-10-15, verify-g1 F2).
    //
    // Marks (foresight F4, D11 requirement-mark-beliefs): a Mark detection (a vanilla spotting mark, where a hidden group was last shown) is
    // applied in a second pass, after steps 1-5 ran over the day's other detections only. It matches a belief by exact UnitKey alone (never
    // the re-key of step 3 or the retirement of step 4, and a noisy position never retires another belief); a mark of a group already
    // detected that day, or older than the belief's last-seen day, is ignored; an applied mark sets the position, strength, LastSeenDay =
    // MarkDay, LastSource = Mark and BaseRadius = MarkRadius, and the radius then widens from that base. Step 4 never retires a Mark belief
    // and step 3 re-keys one within PlausibleRadius + BaseRadius. A mark never writes LastEmbarkedDay.
    public sealed class EnemyArmyModel
    {
        public const int ExpireDays = 30;
        public const float HalfLifeDays = 7f;
        public const float MinConfidence = 0.05f;
        // A sighting reports the group's true GetPosition(), so a fresh belief is exact.
        public const float SeenRadius = 0f;
        // Identity matching looks at most a week of normal road march back.
        public const int PlausibleDaysCap = 7;

        private readonly List<EnemyBelief> _beliefs = new List<EnemyBelief>();
        private readonly List<string> _expired = new List<string>();

        // Pooled per-update scratch.
        private readonly List<string> _keys = new List<string>();
        private readonly Dictionary<string, int> _keyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _beliefByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<int> _assigned = new List<int>();   // per detection: belief index, -1 new
        private readonly List<bool> _matched = new List<bool>();  // per belief
        private readonly List<bool> _retired = new List<bool>();  // per belief
        private readonly List<float> _radius = new List<float>(); // per belief, at this review
        private readonly List<float> _plausible = new List<float>(); // per belief, identity matching
        private readonly List<EnemyBelief> _next = new List<EnemyBelief>();
        private readonly List<EnemyDetection> _real = new List<EnemyDetection>();    // the detections that are not marks
        private readonly List<EnemyDetection> _marks = new List<EnemyDetection>();
        private readonly List<string> _applied = new List<string>();
        private readonly HashSet<string> _appliedSet = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<EnemyBelief> Beliefs => _beliefs;

        // UnitKeys of the marks the last Update applied (the [EnemyBeliefs] marks= count and the remembered= exclusion).
        public IReadOnlyList<string> AppliedMarkKeys => _applied;

        // The [EnemyBeliefs] remembered= count: beliefs not last seen today, other than the ones a mark refreshed this review.
        public int RememberedCount(int daySerial)
        {
            int count = 0;
            for (int i = 0; i < _beliefs.Count; i++)
                if (_beliefs[i].LastSeenDay != daySerial && !_appliedSet.Contains(_beliefs[i].UnitKey ?? string.Empty)) count++;
            return count;
        }

        // UnitKeys of the beliefs that expired in the last Update (the sea-invasion "collapsed" signal).
        public IReadOnlyList<string> ExpiredKeys => _expired;

        public static float Confidence(int days)
        {
            return (float)Math.Pow(0.5, Math.Max(0, days) / (double)HalfLifeDays);
        }

        public void Update(IReadOnlyList<EnemyDetection> detected, int daySerial, Func<int, float> driftPerDay, Func<int, float> marchPerDay)
        {
            _expired.Clear();
            _applied.Clear();
            _appliedSet.Clear();
            SplitMarks(detected);
            IReadOnlyList<EnemyDetection> seen = _real;
            int detections = seen.Count;
            EffectiveKeys(seen, detections);

            int beliefs = _beliefs.Count;
            _matched.Clear();
            _retired.Clear();
            _radius.Clear();
            _plausible.Clear();
            _beliefByKey.Clear();
            for (int i = 0; i < beliefs; i++)
            {
                _matched.Add(false);
                _retired.Add(false);
                _radius.Add(RadiusAt(_beliefs[i], daySerial, driftPerDay));
                _plausible.Add(PlausibleRadiusAt(_beliefs[i], daySerial, marchPerDay));
                if (_beliefs[i].UnitKey != null && !_beliefByKey.ContainsKey(_beliefs[i].UnitKey))
                    _beliefByKey[_beliefs[i].UnitKey] = i;
            }

            _assigned.Clear();
            for (int d = 0; d < detections; d++)
            {
                int match = -1;
                if (_beliefByKey.TryGetValue(_keys[d], out int b) && !_matched[b]) match = b;
                _assigned.Add(match);
                if (match >= 0) _matched[match] = true;
            }

            for (int d = 0; d < detections; d++)
            {
                if (_assigned[d] >= 0) continue;
                string name = NameOf(_keys[d]);
                for (int b = 0; b < beliefs; b++)
                {
                    if (_matched[b] || _beliefs[b].UnitTyp != seen[d].UnitTyp) continue;
                    if (!string.Equals(NameOf(_beliefs[b].UnitKey), name, StringComparison.Ordinal)) continue;
                    if (!WithinPlausible(b, seen[d].X, seen[d].Z, _beliefs[b].BaseRadius)) continue;
                    _assigned[d] = b;
                    _matched[b] = true;
                    break;
                }
            }

            for (int d = 0; d < detections; d++)
            {
                if (_assigned[d] >= 0) continue;
                int retire = -1;
                float best = float.MaxValue;
                for (int b = 0; b < beliefs; b++)
                {
                    if (_matched[b] || _retired[b] || _beliefs[b].UnitTyp != seen[d].UnitTyp) continue;
                    if (_beliefs[b].LastSource == DetectionSource.Mark) continue;   // a mark carries its group's identity, so a noisy position retires nothing
                    if (!WithinPlausible(b, seen[d].X, seen[d].Z, 0f)) continue;
                    float dx = _beliefs[b].X - seen[d].X;
                    float dz = _beliefs[b].Z - seen[d].Z;
                    float distSq = dx * dx + dz * dz;
                    if (distSq >= best) continue;
                    best = distSq;
                    retire = b;
                }
                if (retire >= 0) _retired[retire] = true;
            }

            _next.Clear();
            for (int b = 0; b < beliefs; b++)
            {
                if (_matched[b] || _retired[b]) continue;
                var belief = _beliefs[b];
                int days = Math.Max(0, daySerial - belief.LastSeenDay);
                float confidence = Confidence(days);
                if (days >= ExpireDays || confidence < MinConfidence)
                {
                    _expired.Add(belief.UnitKey);
                    continue;
                }
                belief.Confidence01 = confidence;
                belief.UncertaintyRadius = _radius[b];
                _next.Add(belief);
            }

            for (int d = 0; d < detections; d++)
            {
                var detection = seen[d];
                int b = _assigned[d];
                int lastEmbarked = b >= 0 ? _beliefs[b].LastEmbarkedDay : -1;
                if ((detection.Traits & EnemyTraits.Embarked) != 0) lastEmbarked = daySerial;
                // A sighting that shows a lane's arms replaces the old reading; one that shows none leaves it (0 means "not shown").
                float rifleQuality = detection.RifleQuality > 0f ? detection.RifleQuality : (b >= 0 ? _beliefs[b].RifleQuality : 0f);
                float gunQuality = detection.GunQuality > 0f ? detection.GunQuality : (b >= 0 ? _beliefs[b].GunQuality : 0f);
                _next.Add(new EnemyBelief
                {
                    InstanceId = detection.InstanceId,
                    UnitKey = _keys[d],
                    UnitTyp = detection.UnitTyp,
                    X = detection.X,
                    Z = detection.Z,
                    Strength = detection.Strength,
                    LastSeenDay = daySerial,
                    Confidence01 = 1f,
                    UncertaintyRadius = SeenRadius,
                    LastSource = detection.Source,
                    Traits = detection.Traits,
                    LastEmbarkedDay = lastEmbarked,
                    RifleQuality = rifleQuality,
                    GunQuality = gunQuality
                });
            }

            ApplyMarks(daySerial, driftPerDay);

            _beliefs.Clear();
            _beliefs.AddRange(_next);
            _next.Clear();
        }

        private void SplitMarks(IReadOnlyList<EnemyDetection> detected)
        {
            _real.Clear();
            _marks.Clear();
            if (detected == null) return;
            for (int d = 0; d < detected.Count; d++)
            {
                if (detected[d].Source == DetectionSource.Mark) _marks.Add(detected[d]);
                else _real.Add(detected[d]);
            }
        }

        // The second pass over _next (the carried beliefs and the day's new ones). A mark that is too old to count (the belief expiry rule
        // applied to its own age) makes no belief and keeps none alive.
        private void ApplyMarks(int daySerial, Func<int, float> driftPerDay)
        {
            for (int m = 0; m < _marks.Count; m++)
            {
                EnemyDetection mark = _marks[m];
                if (string.IsNullOrEmpty(mark.UnitKey)) continue;
                int age = Math.Max(0, daySerial - mark.MarkDay);
                float confidence = Confidence(age);
                if (age >= ExpireDays || confidence < MinConfidence) continue;
                int at = -1;
                for (int b = 0; b < _next.Count && at < 0; b++)
                    if (string.Equals(_next[b].UnitKey, mark.UnitKey, StringComparison.Ordinal)) at = b;
                EnemyBelief belief = at >= 0 ? _next[at] : default(EnemyBelief);
                if (at >= 0)
                {
                    if (belief.LastSeenDay > mark.MarkDay) continue;                                         // older than what is held
                    if (belief.LastSeenDay == mark.MarkDay && belief.LastSource != DetectionSource.Mark) continue;   // a sighting that day wins
                }
                else belief.LastEmbarkedDay = -1;
                float drift = driftPerDay != null ? driftPerDay(mark.UnitTyp) : 0f;
                if (float.IsNaN(drift) || drift < 0f) drift = 0f;
                belief.InstanceId = mark.InstanceId;
                belief.UnitKey = mark.UnitKey;
                belief.UnitTyp = mark.UnitTyp;
                belief.X = mark.X;
                belief.Z = mark.Z;
                belief.Strength = mark.Strength;
                belief.LastSeenDay = mark.MarkDay;
                belief.LastSource = DetectionSource.Mark;
                belief.Traits = mark.Traits;
                belief.BaseRadius = Math.Max(0f, mark.MarkRadius);
                belief.Confidence01 = confidence;
                belief.UncertaintyRadius = SeenRadius + belief.BaseRadius + drift * age;
                if (at >= 0) _next[at] = belief;
                else _next.Add(belief);
                _applied.Add(mark.UnitKey);
                _appliedSet.Add(mark.UnitKey);
            }
        }

        // Sidecar (G6): the persisted beliefs, keyed by UnitKey; InstanceId is rebound by the next
        // detection under that key. Confidence and radius are recomputed by the next Update.
        public void Restore(IEnumerable<EnemyBelief> beliefs)
        {
            _beliefs.Clear();
            _expired.Clear();
            _applied.Clear();
            _appliedSet.Clear();
            if (beliefs == null) return;
            foreach (var belief in beliefs)
                if (!string.IsNullOrEmpty(belief.UnitKey)) _beliefs.Add(belief);
        }

        internal void Clear()
        {
            _beliefs.Clear();
            _expired.Clear();
            _applied.Clear();
            _appliedSet.Clear();
        }

        // The unit name of a UnitKey ("name:commander", optionally "#instance"): the part before the
        // last ':'. Group names may contain ':'; the commander id never does.
        internal static string NameOf(string unitKey)
        {
            if (string.IsNullOrEmpty(unitKey)) return string.Empty;
            int colon = unitKey.LastIndexOf(':');
            return colon >= 0 ? unitKey.Substring(0, colon) : unitKey;
        }

        private void EffectiveKeys(IReadOnlyList<EnemyDetection> seen, int count)
        {
            _keys.Clear();
            _keyCounts.Clear();
            for (int d = 0; d < count; d++)
            {
                string key = seen[d].UnitKey ?? string.Empty;
                _keyCounts.TryGetValue(key, out int n);
                _keyCounts[key] = n + 1;
            }
            bool collided = false;
            for (int d = 0; d < count; d++)
            {
                string key = seen[d].UnitKey ?? string.Empty;
                if (_keyCounts[key] > 1)
                {
                    key = key + "#" + seen[d].InstanceId.ToString();
                    collided = true;
                }
                _keys.Add(key);
            }
            if (collided)
                OnceLog.Warning("campaign-planner:unitkey-collision",
                    "[EnemyBeliefs] two live enemy groups share one name:commander key; each is kept under name:commander#instance");
        }

        private bool WithinPlausible(int belief, float x, float z, float extraRadius)
        {
            float dx = _beliefs[belief].X - x;
            float dz = _beliefs[belief].Z - z;
            float r = _plausible[belief] + extraRadius;
            return dx * dx + dz * dz <= r * r;
        }

        // Normal road march x days unseen, clamped to [1, PlausibleDaysCap]; a fort garrison does not move.
        private static float PlausibleRadiusAt(in EnemyBelief belief, int daySerial, Func<int, float> marchPerDay)
        {
            if (belief.LastSource == DetectionSource.ObjectiveGarrison) return SeenRadius;
            int days = Math.Min(PlausibleDaysCap, Math.Max(1, daySerial - belief.LastSeenDay));
            float march = marchPerDay != null ? marchPerDay(belief.UnitTyp) : 0f;
            if (float.IsNaN(march) || march < 0f) march = 0f;
            return march * days;
        }

        private static float RadiusAt(in EnemyBelief belief, int daySerial, Func<int, float> driftPerDay)
        {
            if (belief.LastSource == DetectionSource.ObjectiveGarrison) return SeenRadius;
            int days = Math.Max(0, daySerial - belief.LastSeenDay);
            float drift = driftPerDay != null ? driftPerDay(belief.UnitTyp) : 0f;
            if (float.IsNaN(drift) || drift < 0f) drift = 0f;
            return SeenRadius + belief.BaseRadius + drift * days;
        }
    }

    // The per-alliance models (alliances 0 and 1 only) and the readers' one decision: with
    // `Enable Campaign Planner` and `Enable Campaign Fog-Honest Beliefs` both on, a reader always
    // gets the model as the last review left it (empty on a new campaign's first day; after a load
    // it holds the sidecar's beliefs, restored by CampaignPlannerRuntime.Restore and rebound by unit
    // key on the first review); only with either flag off does it get the rollback list (vanilla's
    // omniscient enemies at full confidence). Per campaign: cleared by CampaignScope.
    // A reader inside a review job gets the model itself; a reader outside (a patch between the
    // slices of a time-sliced review, RecruitmentPatch, FortConstructionGovernorPatch) gets the
    // beliefs as the last committed day left them (DailyReviewView, PublishedDay), so it never
    // sees today's beliefs next to yesterday's ledgers.
    internal static class EnemyBeliefStore
    {
        private const int Alliances = 2;
        private static readonly EnemyArmyModel[] Models = { new EnemyArmyModel(), new EnemyArmyModel() };
        private static readonly EnemyBelief[] None = new EnemyBelief[0];
        private static readonly PublishedDay<List<EnemyBelief>[]> Day =
            new PublishedDay<List<EnemyBelief>[]>(NewCopy(), NewCopy());
        private static readonly Action<List<EnemyBelief>[]> Fill = FillFromModels;
        private static readonly Action<List<EnemyBelief>[]> ClearCopy = ClearLists;

        static EnemyBeliefStore()
        {
            CampaignScope.Register("campaign-planner-beliefs", Reset);
        }

        internal static EnemyArmyModel ModelFor(int alliance)
        {
            return alliance >= 0 && alliance < Alliances ? Models[alliance] : null;
        }

        internal static IReadOnlyList<EnemyBelief> Select(int alliance, bool plannerOn, bool fogHonestOn,
                                                         Func<int, IReadOnlyList<EnemyBelief>> omniscient)
        {
            if (alliance < 0 || alliance >= Alliances) return None;
            if (!(plannerOn && fogHonestOn))
                return (omniscient != null ? omniscient(alliance) : null) ?? None;
            return DailyReviewView.ReadsWorking ? Models[alliance].Beliefs : Day.Published[alliance];
        }

        // The day's commit (StrategicCoordinator.PublishStrategicState).
        internal static void PreparePublish()
        {
            Day.Prepare(Fill);
        }

        internal static void SwapPublished()
        {
            Day.Swap();
        }

        private static List<EnemyBelief>[] NewCopy()
        {
            return new[] { new List<EnemyBelief>(), new List<EnemyBelief>() };
        }

        private static void FillFromModels(List<EnemyBelief>[] copy)
        {
            for (int a = 0; a < Alliances; a++)
            {
                var into = copy[a];
                into.Clear();
                var beliefs = Models[a].Beliefs;
                for (int i = 0; i < beliefs.Count; i++) into.Add(beliefs[i]);
            }
        }

        private static void ClearLists(List<EnemyBelief>[] copy)
        {
            for (int a = 0; a < copy.Length; a++) copy[a].Clear();
        }

        private static void Reset()
        {
            for (int a = 0; a < Alliances; a++) Models[a].Clear();
            Day.Reset(ClearCopy);
        }
    }

    // Vanilla's campaign speed terms (plan P6), each as the GamePrefs value or array the runtime
    // reads, plus the believed enemy alliance's live railroadspeedadj (a scalar, never a position).
    public struct CampaignSpeedPrefs
    {
        public float[] UnitBasicSpeedMounted;   // unitbasicspeed[t, 1] by unittyp (133350-133354: unittyp >= 14 mounted)
        public float GroupSpeedCeiling;         // unitbasicspeed[15, 1]: groupspeed's start (120179), it only falls
        public float[] SkilledCartographers;    // movementspeedskilledcartographers by perk level (own and parent, 133369-133376)
        public float[] FlyingColumn;            // movementspeedflyingcolumn (133377-133384)
        public float[] FootCavalry;             // forcedmarchspeedfootcavalry (own and parent, 133516-133525)
        public float[] RiverExpedition;         // rivermovementspeedriverexpedition (terrain 3/4/5, 133482-133491)
        public float[] DoubleQuick, Running;    // doublequickspeedfact / runningspeedfact by unittyp (133509-133516)
        public float RailroadSpeedAdj;          // alliance railroadspeedadj on a train (133401-133404)
        public float BasicUnitSpeed;            // GamePrefs.basicunitspeed (133535)
        public float[] TerrainSpecCost;         // GetTerrainSpeedAdjustment = 1 / cost (132888-132900)
        public float GeneralCampaignFactor;     // generalcampaignunitspeedfactor (134083)
        public float SpeedFactorRetreat, SpeedFactorFallback, FallbackCheat; // 134050-134062
    }

    // DriftPerDay (P6): vanilla's maximum campaign speed of a unit type in map units per game day,
    // the product of every term's maximum in RegimentMove (num33 = num25 x num11 x num28 x num9 x
    // transport x commander x num30, 134100, with num25 the time step in floating years, 30171):
    //   unitbasicspeed[t,1] x unitbasicspeed[15,1] (groupspeed ceiling, applied 133991)
    //   x max(cartographers)^2 x max(flying column)^2 x max(1, doublequick[t], running[t]) x max(foot cavalry)^2
    //   x max(max(river expedition)^2, railroadspeedadj)   (river only on terrain 3/4/5, rail only on a train)
    //   x basicunitspeed x 1 / min(terrainspeccost)        (cost-1 terrain: rivers, crossings, sea, rails)
    //   x generalcampaignunitspeedfactor x 2               (the downhill slope term num31 <= 2, 134013)
    //   x max(1, retreat, fallback x cheat) x max(1, retreat)
    //   / 365.25
    // The last factor pair is G1's correction of P6: num28 takes speedfactorretreat for movement
    // mode 5 (134058) and again when lastpathsetoutofsafetyzone on a group (unittyp > 13, 134062),
    // or the fallback factor (with the AI cheat, 134050-134056) and the safety-zone retreat.
    // Every other term only shrinks the speed (transport cap and aiarrivalfactor <= 1,
    // commanderspeedadjustment 1 on the campaign 132838, wounded, state support, readiness,
    // training, weather, the night stop, land torpedoes). Deliberately loose: a sighting is what
    // narrows a belief again.
    public static class CampaignSpeedBound
    {
        public const float SlopeCap = 2f;      // Mathf.Min(2f, ...) at 134013
        public const float DaysPerYear = 365.25f;

        public const int RoadTerrain = 7;      // terrainspeccost[7], roads (GetTerrainSpeedAdjustment 132893)

        // A normal campaign day on roads: vanilla's speed chain without P6's loose multipliers (no
        // perks, no forced march, no slope, no retreat, no rail or river): unitbasicspeed[t,1]
        // (133350-133354) x unitbasicspeed[15,1] (groupspeed ceiling, 120179, applied 133991) x
        // basicunitspeed (133535) / terrainspeccost[7] (roads, 132888-132900) x
        // generalcampaignunitspeedfactor (134083) / 365.25. Identity matching's plausibility radius.
        public static float RoadMarchPerDay(in CampaignSpeedPrefs p, int unitTyp)
        {
            if (p.UnitBasicSpeedMounted == null || unitTyp < 0 || unitTyp >= p.UnitBasicSpeedMounted.Length) return 0f;
            float road = p.TerrainSpecCost != null && RoadTerrain < p.TerrainSpecCost.Length && p.TerrainSpecCost[RoadTerrain] > 0f
                ? p.TerrainSpecCost[RoadTerrain] : 1f;
            float perDay = Math.Max(0f, p.UnitBasicSpeedMounted[unitTyp]) * Math.Max(0f, p.GroupSpeedCeiling) * Math.Max(0f, p.BasicUnitSpeed)
                / road * Math.Max(0f, p.GeneralCampaignFactor) / DaysPerYear;
            return float.IsNaN(perDay) || float.IsInfinity(perDay) ? 0f : perDay;
        }

        public static float DriftPerDay(in CampaignSpeedPrefs p, int unitTyp)
        {
            if (p.UnitBasicSpeedMounted == null || unitTyp < 0 || unitTyp >= p.UnitBasicSpeedMounted.Length) return 0f;
            float speed = Math.Max(0f, p.UnitBasicSpeedMounted[unitTyp]) * Math.Max(0f, p.GroupSpeedCeiling);
            float cartographers = MaxAtLeastOne(p.SkilledCartographers);
            float flying = MaxAtLeastOne(p.FlyingColumn);
            float footCavalry = MaxAtLeastOne(p.FootCavalry);
            float river = MaxAtLeastOne(p.RiverExpedition);
            speed *= cartographers * cartographers * flying * flying;
            speed *= Math.Max(1f, Math.Max(At(p.DoubleQuick, unitTyp), At(p.Running, unitTyp)));
            speed *= footCavalry * footCavalry;
            speed *= Math.Max(river * river, Math.Max(1f, p.RailroadSpeedAdj));
            speed *= Math.Max(0f, p.BasicUnitSpeed) / MinPositive(p.TerrainSpecCost);
            speed *= Math.Max(0f, p.GeneralCampaignFactor) * SlopeCap;
            float retreat = Math.Max(1f, p.SpeedFactorRetreat);
            speed *= Math.Max(retreat, Math.Max(1f, p.SpeedFactorFallback * Math.Max(1f, p.FallbackCheat))) * retreat;
            float perDay = speed / DaysPerYear;
            return float.IsNaN(perDay) || float.IsInfinity(perDay) ? 0f : perDay;
        }

        private static float MaxAtLeastOne(float[] values)
        {
            float max = 1f;
            if (values == null) return max;
            for (int i = 0; i < values.Length; i++)
                if (values[i] > max) max = values[i];
            return max;
        }

        private static float MinPositive(float[] values)
        {
            float min = float.MaxValue;
            if (values != null)
                for (int i = 0; i < values.Length; i++)
                    if (values[i] > 0f && values[i] < min) min = values[i];
            return min == float.MaxValue ? 1f : min;
        }

        private static float At(float[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length ? values[index] : 0f;
        }
    }

    internal enum DetectionMode
    {
        FogGrid = 0,
        OwnSideWithinFog = 1,
        NoFog = 2,
        ConfigOff = 3
    }

    internal enum UnitRelation
    {
        Ignore = 0,
        Viewer = 1,
        Enemy = 2
    }

    // The detection decision of spec component 2 (plan G1), applied by EnemyDetectionRuntime to
    // every enemy campaign group and to enemy objective-fort garrisons:
    //   (a) GameVars.usefow off or the fog-honest flags off -> NoFog (omniscient, the rollback);
    //   (d) contact: in the same battle as an own formation, or within bugle range -> Contact;
    //   (c) the W&L player's alliance side: !enemy.iswithinfog -> OwnSideWithinFog;
    //   (b) any other side: an own or allied viewer has the enemy inside its own sight radius
    //       (ViewerCoverage, C14) and fows[1].UnitIsHidden says visible -> FogGrid.
    // The grid is asked only when (b) can still decide (NeedsGridProbe), so the usefow NRE (101188)
    // and the probe cost never arise for the other rules.
    internal static class DetectionRules
    {
        internal const float CavalryMajorityShare = 0.5f;

        // Runs `probe` with the launched incidents masked as not launched (when `apply`), and
        // restores them in a finally, also when the probe throws (the exception propagates).
        internal static void RunMasked<TItem>(FieldMask<TItem, float> mask, IList<TItem> incidents, bool apply,
                                              Func<TItem, float> launchedTime, Func<TItem, bool> missing, Action probe,
                                              out string restoreFailure) where TItem : class
        {
            restoreFailure = null;
            try
            {
                if (apply && mask != null) mask.Mask(incidents, item => PlayerIntelIncidents.Launched(launchedTime(item)));
                probe();
            }
            finally
            {
                if (mask != null && mask.Active) restoreFailure = mask.Restore(missing);
            }
        }

        // [EnemyBeliefs] seen= counts the detections that are not marks; the marks are counted apart (the model's applied ones, marks=).
        internal static int SeenCount(IReadOnlyList<EnemyDetection> detections)
        {
            int count = 0;
            if (detections != null)
                for (int i = 0; i < detections.Count; i++)
                    if (detections[i].Source != DetectionSource.Mark) count++;
            return count;
        }

        internal static bool NeedsGridProbe(bool noFog, bool contact, bool sideIsPlayerAlliance, bool viewerCovers)
        {
            return !noFog && !contact && !sideIsPlayerAlliance && viewerCovers;
        }

        internal static bool TryDecide(bool noFog, bool contact, bool sideIsPlayerAlliance, bool ownSideVisible,
                                       bool viewerCovers, bool gridVisible, out DetectionSource source)
        {
            if (noFog) { source = DetectionSource.Seen; return true; }
            if (contact) { source = DetectionSource.Contact; return true; }
            source = DetectionSource.Seen;
            if (sideIsPlayerAlliance) return ownSideVisible;
            return viewerCovers && gridVisible;
        }

        // Within the own formation's bugle range or the enemy's own: vanilla's campaign contact
        // (CheckEnemyContactCampaign 115634-115637) looks out to the closest enemy's bugle range
        // x 1.1 when there is one, else the unit's own.
        internal static bool InBugleContact(float x, float z, float enemyBugle, float[] ownX, float[] ownZ, float[] ownBugle, int count)
        {
            if (ownX == null || ownZ == null || ownBugle == null) return false;
            int n = Math.Min(count, Math.Min(ownX.Length, Math.Min(ownZ.Length, ownBugle.Length)));
            for (int i = 0; i < n; i++)
            {
                float range = Math.Max(ownBugle[i], enemyBugle);
                if (range <= 0f) continue;
                float dx = x - ownX[i];
                float dz = z - ownZ[i];
                if (dx * dx + dz * dz <= range * range) return true;
            }
            return false;
        }

        internal static EnemyTraits Traits(bool embarked, float cavalryShare, bool onOwnSoil, bool nearOwnCoast)
        {
            var traits = default(EnemyTraits);
            if (embarked) traits |= EnemyTraits.Embarked;
            if (cavalryShare > CavalryMajorityShare) traits |= EnemyTraits.CavalryMajority;
            if (onOwnSoil) traits |= EnemyTraits.OnOwnSoil;
            if (nearOwnCoast) traits |= EnemyTraits.NearOwnCoast;
            return traits;
        }

        // The review's mode for one side: the flags off -> ConfigOff, fog off -> NoFog (both
        // omniscient, the rollback), the player-alliance side -> OwnSideWithinFog (rule c), any
        // other side -> FogGrid (rule b).
        internal static DetectionMode Mode(bool fogHonest, bool useFow, bool sideIsPlayerAlliance)
        {
            if (!fogHonest) return DetectionMode.ConfigOff;
            if (!useFow) return DetectionMode.NoFog;
            return sideIsPlayerAlliance ? DetectionMode.OwnSideWithinFog : DetectionMode.FogGrid;
        }

        internal static bool IsOmniscient(DetectionMode mode)
        {
            return mode == DetectionMode.NoFog || mode == DetectionMode.ConfigOff;
        }

        // A FogGrid review without the grid fails closed: contact only for that review, never
        // omniscient.
        internal static bool GridUnavailable(DetectionMode mode, bool gridPresent)
        {
            return mode == DetectionMode.FogGrid && !gridPresent;
        }

        // What a campaign group is to the side (SeparateUnits 10458-10515, AssignUnitToFOW
        // 116520-116534): own or allied groups are viewers (fleets and garrisons paint the grid
        // too); an enemy army is an at-war group that is neither a fleet nor a garrison (enemy
        // garrisons are reached only through objective forts).
        internal static UnitRelation RelationOf(int side, int other, bool alliedWithSide, bool atWarWithSide, bool isFleet, bool isGarrison)
        {
            if (other == side || alliedWithSide) return UnitRelation.Viewer;
            if (atWarWithSide && !isFleet && !isGarrison) return UnitRelation.Enemy;
            return UnitRelation.Ignore;
        }

        // Rule (b)'s grid answer: never asked with fog off (the concealment grid is unallocated,
        // UnitIsHidden NREs at 101188) or without a usable grid; a probe exception answers
        // "not visible" and reports `threw` so the caller detects by contact only for the rest
        // of the review.
        internal static bool GridVisible<TUnit>(bool useFow, bool gridUsable, Func<TUnit, bool> isHidden, TUnit unit, out bool threw)
        {
            threw = false;
            if (!useFow || !gridUsable || isHidden == null) return false;
            try
            {
                return !isHidden(unit);
            }
            catch
            {
                threw = true;
                return false;
            }
        }

        // An enemy objective fort's garrison enters the picture only when the side sees or
        // contacts it (G0 landing F2).
        internal static bool TryObjectiveGarrison(bool seen, int instanceId, string unitKey, int unitTyp, float x, float z, float strength,
                                                  out EnemyDetection detection)
        {
            detection = seen ? ObjectiveGarrison(instanceId, unitKey, unitTyp, x, z, strength) : default(EnemyDetection);
            return seen;
        }

        // An enemy objective fort's garrison that passed the rule above: one detection at the
        // objective target it guards, with the garrison group's own strength (G0 landing F2).
        internal static EnemyDetection ObjectiveGarrison(int instanceId, string unitKey, int unitTyp, float x, float z, float strength)
        {
            return new EnemyDetection
            {
                InstanceId = instanceId,
                UnitKey = unitKey,
                UnitTyp = unitTyp,
                X = x,
                Z = z,
                Strength = Math.Max(0f, strength),
                Source = DetectionSource.ObjectiveGarrison
            };
        }
    }
}
