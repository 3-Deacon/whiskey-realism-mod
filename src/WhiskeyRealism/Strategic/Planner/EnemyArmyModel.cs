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
    // UnitTyp (its commander changed); (4) a still-new key retires the nearest unmatched belief of
    // the same UnitTyp whose uncertainty radius contains it (vanilla renamed the group, 17159); (5)
    // anything left is a new belief. Two armies that are both seen are never merged.
    public sealed class EnemyArmyModel
    {
        public const int ExpireDays = 30;
        public const float HalfLifeDays = 7f;
        public const float MinConfidence = 0.05f;
        // A sighting reports the group's true GetPosition(), so a fresh belief is exact.
        public const float SeenRadius = 0f;

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
        private readonly List<EnemyBelief> _next = new List<EnemyBelief>();

        public IReadOnlyList<EnemyBelief> Beliefs => _beliefs;

        // UnitKeys of the beliefs that expired in the last Update (the sea-invasion "collapsed" signal).
        public IReadOnlyList<string> ExpiredKeys => _expired;

        public static float Confidence(int days)
        {
            return (float)Math.Pow(0.5, Math.Max(0, days) / (double)HalfLifeDays);
        }

        public void Update(IReadOnlyList<EnemyDetection> seen, int daySerial, Func<int, float> driftPerDay)
        {
            _expired.Clear();
            int detections = seen != null ? seen.Count : 0;
            EffectiveKeys(seen, detections);

            int beliefs = _beliefs.Count;
            _matched.Clear();
            _retired.Clear();
            _radius.Clear();
            _beliefByKey.Clear();
            for (int i = 0; i < beliefs; i++)
            {
                _matched.Add(false);
                _retired.Add(false);
                _radius.Add(RadiusAt(_beliefs[i], daySerial, driftPerDay));
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
                    float dx = _beliefs[b].X - seen[d].X;
                    float dz = _beliefs[b].Z - seen[d].Z;
                    float distSq = dx * dx + dz * dz;
                    if (distSq > _radius[b] * _radius[b] || distSq >= best) continue;
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
                _next.Add(new EnemyBelief
                {
                    InstanceId = detection.InstanceId,
                    UnitKey = _keys[d],
                    UnitTyp = detection.UnitTyp,
                    X = detection.X,
                    Z = detection.Z,
                    Strength = detection.Strength,
                    Guns = detection.Guns,
                    LastSeenDay = daySerial,
                    Confidence01 = 1f,
                    UncertaintyRadius = SeenRadius,
                    LastSource = detection.Source,
                    Traits = detection.Traits,
                    LastEmbarkedDay = lastEmbarked
                });
            }

            _beliefs.Clear();
            _beliefs.AddRange(_next);
            _next.Clear();
        }

        // Sidecar (G6): the persisted beliefs, keyed by UnitKey; InstanceId is rebound by the next
        // detection under that key. Confidence and radius are recomputed by the next Update.
        public void Restore(IEnumerable<EnemyBelief> beliefs)
        {
            _beliefs.Clear();
            _expired.Clear();
            if (beliefs == null) return;
            foreach (var belief in beliefs)
                if (!string.IsNullOrEmpty(belief.UnitKey)) _beliefs.Add(belief);
        }

        internal void Clear()
        {
            _beliefs.Clear();
            _expired.Clear();
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

        private static float RadiusAt(in EnemyBelief belief, int daySerial, Func<int, float> driftPerDay)
        {
            if (belief.LastSource == DetectionSource.ObjectiveGarrison) return SeenRadius;
            int days = Math.Max(0, daySerial - belief.LastSeenDay);
            float drift = driftPerDay != null ? driftPerDay(belief.UnitTyp) : 0f;
            if (float.IsNaN(drift) || drift < 0f) drift = 0f;
            return SeenRadius + drift * days;
        }
    }

    // The per-alliance models (alliances 0 and 1 only) and the readers' one decision: with
    // `Enable Campaign Planner` and `Enable Campaign Fog-Honest Beliefs` both on, a reader always
    // gets the model as the last review left it (empty on a new campaign's first day, sidecar-
    // restored after a load); only with either flag off does it get the rollback list (vanilla's
    // omniscient enemies at full confidence). Per campaign: cleared by CampaignScope.
    internal static class EnemyBeliefStore
    {
        private const int Alliances = 2;
        private static readonly EnemyArmyModel[] Models = { new EnemyArmyModel(), new EnemyArmyModel() };
        private static readonly EnemyBelief[] None = new EnemyBelief[0];

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
            return Models[alliance].Beliefs;
        }

        private static void Reset()
        {
            for (int a = 0; a < Alliances; a++) Models[a].Clear();
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
    // mode 5 (134058) and again when lastpathsetoutofsafetyzone on a group (unittyp > 13, 134061),
    // or the fallback factor (with the AI cheat, 134050-134056) and the safety-zone retreat.
    // Every other term only shrinks the speed (transport cap and aiarrivalfactor <= 1,
    // commanderspeedadjustment 1 on the campaign 132838, wounded, state support, readiness,
    // training, weather, the night stop, land torpedoes). Deliberately loose: a sighting is what
    // narrows a belief again.
    public static class CampaignSpeedBound
    {
        public const float SlopeCap = 2f;      // Mathf.Min(2f, ...) at 134013
        public const float DaysPerYear = 365.25f;

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
        // W&L player-intelligence incidents that make FogOfWar.UnitIsHidden answer "visible" for
        // every position (101183-101186): 60 Lost Orders, 61 and 64 Scouting Ahead.
        // Incident.launchedtime >= 0 is what IsIncidentActive tests (46251-46263). Ported verbatim
        // from BattlePerception on fix/campaign-terrain-bugs (BUG-TAC-020); dedupe when it merges.
        internal static readonly int[] PlayerIntelIncidents = { 60, 61, 64 };
        internal const float IncidentNotLaunched = -1f;
        internal const float CavalryMajorityShare = 0.5f;

        internal static bool IncidentLaunched(float launchedTime)
        {
            return launchedTime >= 0f;
        }

        // Only rule (b) asks an AI side's grid about the player's units; vanilla never does (it
        // computes iswithinfog for non-player units only, 127212-127230), so the incident's
        // shortcut is the player's intelligence, never the AI side's. Rule (c) keeps it.
        internal static bool MasksPlayerIntelIncidents(bool fogGridRule, bool dlcScenarioActive)
        {
            return dlcScenarioActive && fogGridRule;
        }

        // Runs `probe` with the launched incidents masked as not launched (when `apply`), and
        // restores them in a finally, also when the probe throws (the exception propagates).
        internal static void RunMasked<TItem>(FieldMask<TItem, float> mask, IList<TItem> incidents, bool apply,
                                              Func<TItem, float> launchedTime, Func<TItem, bool> missing, Action probe,
                                              out string restoreFailure) where TItem : class
        {
            restoreFailure = null;
            try
            {
                if (apply && mask != null) mask.Mask(incidents, item => IncidentLaunched(launchedTime(item)));
                probe();
            }
            finally
            {
                if (mask != null && mask.Active) restoreFailure = mask.Restore(missing);
            }
        }

        internal static bool NeedsGridProbe(bool noFog, bool contact, bool sideIsPlayerAlliance, bool viewerCovers)
        {
            return !noFog && !contact && !sideIsPlayerAlliance && viewerCovers;
        }

        internal static bool TryDecide(bool noFog, bool contact, bool sideIsPlayerAlliance, bool ownSideVisible,
                                       bool viewerCovers, bool gridVisible, out DetectionSource source)
        {
            if (noFog) { source = DetectionSource.NoFog; return true; }
            if (contact) { source = DetectionSource.Contact; return true; }
            if (sideIsPlayerAlliance)
            {
                source = DetectionSource.OwnSideWithinFog;
                return ownSideVisible;
            }
            source = DetectionSource.FogGrid;
            return viewerCovers && gridVisible;
        }

        // Within the own formation's bugle range or the enemy's own (vanilla's campaign reveal
        // test, 127231, uses both).
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
            var traits = EnemyTraits.None;
            if (embarked) traits |= EnemyTraits.Embarked;
            if (cavalryShare > CavalryMajorityShare) traits |= EnemyTraits.CavalryMajority;
            if (onOwnSoil) traits |= EnemyTraits.OnOwnSoil;
            if (nearOwnCoast) traits |= EnemyTraits.NearOwnCoast;
            return traits;
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
