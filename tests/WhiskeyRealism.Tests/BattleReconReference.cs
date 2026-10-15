using System;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

// Perf slice B1: the BattleRecon planner as shipped at 742d7e4 (before the hoisted scout
// list, the precomputed closest-visible distance and the per-tick target cap), kept as
// the reference BattleReconSearchTests compares the planner against. Not a test class.
namespace WhiskeyRealism.Tactical.Battle
{
    internal static class BattleReconReference
    {
        public const string OutboundReason = "recon-outbound";
        public const string ReturnReason = "recon-return";
        public const string StopReason = "recon-stop";
        public const string ProbeStopReason = "probe-contact-stop";
        public const string HoldReason = "probe-hold";

        // Vanilla regiment formation index of the square (118174).
        public const int SquareFormation = 4;

        private const UnitFlags ScoutRefusals = UnitFlags.PlayerOwned | UnitFlags.Feud | UnitFlags.Routed |
            UnitFlags.MarkedForRout | UnitFlags.OnRetreat | UnitFlags.InMelee | UnitFlags.Garrisoned |
            UnitFlags.PermanentlyDetached | UnitFlags.DetachedCompany | UnitFlags.IsGroup;

        /// <summary>
        /// One arbiter tick of the side's recon: maintain or start the cavalry mission,
        /// maintain or nominate the probe groups. Allocation-free once the pooled
        /// arrays fit.
        /// </summary>
        public static ReconTickResult Plan(BattleSnapshot s, in ReconSettings settings, ReconPicture picture, ReconMemory memory,
            OrderExecutor executor, IIntentSink sink)
        {
            var result = new ReconTickResult();
            if (s == null || picture == null || memory == null || sink == null) return result;
            double now = s.DoctrineHours;
            if (!settings.Enabled)
            {
                // Rollback: hand everything back to the posture executor and vanilla.
                if (memory.Leg != ReconLeg.None) End(memory, ref result, "disabled");
                for (int b = 0; b < ReconMemory.MaxBands; b++) memory.ClearProbe(b);
                for (int k = 0; k < memory.HoldSlots; k++) memory.ClearHold(k);
                return result;
            }

            PostureSense.AggregateDescendants(s, ref memory.Descendants, ref memory.Moving);
            ProbeTick(s, settings, picture, memory, sink, now, ref result);

            if (memory.Leg != ReconLeg.None) MissionTick(s, settings, memory, executor, sink, now, ref result);
            else TryStartMission(s, settings, picture, memory, sink, now, ref result);
            return result;
        }

        // ---------------------------------------------------------------- mission

        private static void MissionTick(BattleSnapshot s, in ReconSettings st, ReconMemory m, OrderExecutor executor,
            IIntentSink sink, double now, ref ReconTickResult r)
        {
            int idx = s.IndexOfOwn(m.ScoutId);
            if (idx < 0) { End(m, ref r, "scout-lost"); return; }
            ref UnitRecord rec = ref s.Own[idx];
            if (now - m.StartedHours > ReconSettings.MaxMissionHours) { End(m, ref r, "timeout"); return; }
            if ((rec.Flags & (UnitFlags.Alive | UnitFlags.Active)) != (UnitFlags.Alive | UnitFlags.Active) ||
                (rec.Flags & (ScoutRefusals & ~UnitFlags.IsGroup)) != 0)
            {
                End(m, ref r, "scout-lost");
                return;
            }

            // Executor evidence (BC6b pattern): a move by anyone else after ours takes the
            // scout; ours counts only once the executor issued it.
            if (m.Leg != ReconLeg.Observe || m.Confirmed)
            {
                if (executor != null && executor.TryGetLastOutcome(m.ScoutId, OrderChannel.Move, out OrderOutcome outcome) &&
                    outcome.AtHours >= m.SubmittedHours)
                {
                    if (outcome.Source != IntentSource.Recon) { End(m, ref r, "recalled"); return; }
                    if (outcome.Kind == OrderOutcomeKind.GaveUp || outcome.Kind == OrderOutcomeKind.Superseded)
                    {
                        End(m, ref r, "gave-up");
                        return;
                    }
                    if (outcome.Kind == OrderOutcomeKind.Retried) m.NoteRetry();
                    m.Confirm();
                }
                else if (m.Leg == ReconLeg.Outbound && !m.Confirmed && now > m.SubmittedHours)
                {
                    End(m, ref r, "not-issued"); // fail closed: guard, claim, cooldown or validator refused it
                    return;
                }
            }

            float strength = Math.Max(1f, rec.Strength);
            bool hasEnemy = NearestVisible(s, rec.X, rec.Z, out float enemyDistance, out float enemyStrength);
            bool superiorClose = hasEnemy && enemyStrength >= strength && enemyDistance <= st.CorridorMeters;

            switch (m.Leg)
            {
                case ReconLeg.Outbound:
                    if (superiorClose) { StartReturn(s, idx, m, sink, now, ref r); return; }
                    if (hasEnemy && enemyDistance <= st.ObservationStandOff)
                    {
                        sink.Submit(Stop(m.ScoutId, StopReason));
                        r.Intents++;
                        m.EnterLeg(ReconLeg.Observe, m.TargetX, m.TargetZ, now);
                        return;
                    }
                    if (Distance(rec.X, rec.Z, m.TargetX, m.TargetZ) <= OrderArbiter.DedupeMeters ||
                        (m.Confirmed && rec.RegimentPaths <= 0 && now > m.SubmittedHours))
                    {
                        m.EnterLeg(ReconLeg.Observe, m.TargetX, m.TargetZ, now);
                        return;
                    }
                    if (m.Confirmed && !m.Retried && rec.RegimentPaths > 0 && PostureSense.HasPoint(rec.LastWaypointX, rec.LastWaypointZ) &&
                        Distance(rec.LastWaypointX, rec.LastWaypointZ, m.TargetX, m.TargetZ) > OrderArbiter.DedupeMeters)
                    {
                        End(m, ref r, "recalled"); // vanilla re-laid the scout with its brigade
                        return;
                    }
                    sink.Submit(Move(m.ScoutId, m.TargetX, m.TargetZ, OutboundReason));
                    r.Intents++;
                    return;
                case ReconLeg.Observe:
                    if (superiorClose || now - m.LegHours >= ReconSettings.ObserveDwellHours)
                        StartReturn(s, idx, m, sink, now, ref r);
                    return;
                case ReconLeg.Return:
                    if (Distance(rec.X, rec.Z, m.TargetX, m.TargetZ) <= OrderArbiter.DedupeMeters) { End(m, ref r, "returned"); return; }
                    sink.Submit(Move(m.ScoutId, m.TargetX, m.TargetZ, ReturnReason));
                    r.Intents++;
                    return;
            }
        }

        // Back to the scout's slot in its brigade's layout (the shipped #61 leaf slot
        // rule), else to the brigade's position.
        private static void StartReturn(BattleSnapshot s, int idx, ReconMemory m, IIntentSink sink, double now, ref ReconTickResult r)
        {
            ref UnitRecord rec = ref s.Own[idx];
            float x, z;
            if (!LeafSlotTarget.TryResolve(s, idx, 0f, 0f, RefuseFlank.None, out int parent, out x, out z, out _))
            {
                if (parent < 0 || parent >= s.OwnCount) { End(m, ref r, "no-way-back"); return; }
                x = s.Own[parent].X;
                z = s.Own[parent].Z;
            }
            if (Distance(rec.X, rec.Z, x, z) <= OrderArbiter.DedupeMeters) { End(m, ref r, "returned"); return; }
            m.EnterLeg(ReconLeg.Return, x, z, now);
            sink.Submit(Move(m.ScoutId, x, z, ReturnReason));
            r.Intents++;
        }

        private static void TryStartMission(BattleSnapshot s, in ReconSettings st, ReconPicture p, ReconMemory m, IIntentSink sink,
            double now, ref ReconTickResult r)
        {
            if ((r.Events & ReconEvents.MissionEnded) != 0 || !m.StartBudgetLeft(now)) return;
            if (!OwnLine(s, p.FrameHeading, out float centroidX, out float centroidZ, out float frameDepth, out float axisDepth)) return;

            int scout = -1;
            float lookX = 0f, lookZ = 0f, standX = 0f, standZ = 0f;
            ReconTargetKind kind = ReconTargetKind.None;

            // 1. The nearest stale enemy-side entry point.
            float bestDistance = float.MaxValue;
            for (int k = 0; k < p.EntryCount; k++)
            {
                if (!p.EntryStale(k, now)) continue;
                float ex = p.EntryX(k), ez = p.EntryZ(k);
                if (m.RecentTarget(ex, ez, now)) continue;
                float d = Distance(centroidX, centroidZ, ex, ez);
                if (d >= bestDistance) continue;
                int c = NearestScout(s, st, p, ex, ez);
                if (c < 0 || !TryStandOff(s, st, c, ex, ez, out float sx, out float sz)) continue;
                bestDistance = d;
                scout = c; kind = ReconTargetKind.EntryPoint;
                lookX = ex; lookZ = ez; standX = sx; standZ = sz;
            }

            // 2. The stalest coverage band, at the own line's depth.
            if (scout < 0 && p.HasStaleBand && p.StaleBandRecency < ReconSettings.StaleBandRecency && !float.IsNaN(p.FrameHeading))
            {
                BandWorldPoint(p.StaleBandLateral, p.FrameHeading, frameDepth, out float bx, out float bz);
                if (!m.RecentTarget(bx, bz, now)) TryTarget(s, st, p, bx, bz, ReconTargetKind.StaleBand, ref scout, ref kind, ref lookX, ref lookZ, ref standX, ref standZ);
            }

            // 3. Before any contact, below the action gate: straight ahead (along the
            // front axis, or toward the objective while the axis has no target).
            if (scout < 0 && !p.HadContact && p.IntelConfidence < TacticalReinforcementOpportunityDoctrine.MinIntelConfidenceForAction)
            {
                TacticalFrontAxis axis = s.FrontAxis;
                float fx, fz;
                if (axis.IsValid && axis.TargetDistance > 0f)
                {
                    fx = axis.OriginX + axis.ForwardX * (axisDepth + st.ForwardReconMeters);
                    fz = axis.OriginZ + axis.ForwardZ * (axisDepth + st.ForwardReconMeters);
                }
                else if (AdvanceDirection(s, p, centroidX, centroidZ, out float dx, out float dz, out float reach))
                {
                    float advance = Math.Min(st.ForwardReconMeters, reach);
                    fx = centroidX + dx * advance;
                    fz = centroidZ + dz * advance;
                }
                else return;
                if (!m.RecentTarget(fx, fz, now)) TryTarget(s, st, p, fx, fz, ReconTargetKind.Forward, ref scout, ref kind, ref lookX, ref lookZ, ref standX, ref standZ);
            }

            // 4. After contact, below the action gate: look again at the nearest enemy
            // last seen (a Remembered observation), from the stand-off.
            if (scout < 0 && p.HadContact && p.IntelConfidence < TacticalReinforcementOpportunityDoctrine.MinIntelConfidenceForAction)
            {
                EnemyObservation[] obs = s.Observations;
                float nearest = float.MaxValue;
                for (int k = 0; k < s.ObservationCount; k++)
                {
                    if (obs[k].State != ObservationState.Remembered || obs[k].IsReinforcement) continue;
                    float d = Distance(centroidX, centroidZ, obs[k].X, obs[k].Z);
                    if (d >= nearest || m.RecentTarget(obs[k].X, obs[k].Z, now)) continue;
                    int c = NearestScout(s, st, p, obs[k].X, obs[k].Z);
                    if (c < 0 || !TryStandOff(s, st, c, obs[k].X, obs[k].Z, out float sx, out float sz)) continue;
                    nearest = d;
                    scout = c; kind = ReconTargetKind.RememberedEnemy;
                    lookX = obs[k].X; lookZ = obs[k].Z; standX = sx; standZ = sz;
                }
            }

            if (scout < 0) return;
            m.Start(s.Own[scout].InstanceId, kind, lookX, lookZ, standX, standZ, now);
            sink.Submit(Move(m.ScoutId, standX, standZ, OutboundReason));
            r.Intents++;
            r.Events |= ReconEvents.MissionStarted;
            r.StartedKind = kind;
        }

        // The point ObservationStandOff short of a target on the line from the scout,
        // when the scout is farther than that (plus the 60 m move floor) and the point
        // is safe.
        private static bool TryStandOff(BattleSnapshot s, in ReconSettings st, int scout, float tx, float tz, out float sx, out float sz)
        {
            ref UnitRecord sr = ref s.Own[scout];
            sx = 0f;
            sz = 0f;
            float span = Distance(sr.X, sr.Z, tx, tz);
            if (span <= st.ObservationStandOff + OrderArbiter.DedupeMeters) return false;
            sx = tx + (sr.X - tx) / span * st.ObservationStandOff;
            sz = tz + (sr.Z - tz) / span * st.ObservationStandOff;
            return StandPointSafe(s, sr.X, sr.Z, sx, sz, Math.Max(1f, sr.Strength), st);
        }

        private static void TryTarget(BattleSnapshot s, in ReconSettings st, ReconPicture p, float x, float z, ReconTargetKind kind,
            ref int scout, ref ReconTargetKind chosen, ref float lookX, ref float lookZ, ref float standX, ref float standZ)
        {
            int c = NearestScout(s, st, p, x, z);
            if (c < 0) return;
            ref UnitRecord sr = ref s.Own[c];
            if (!PostureSense.IsMoveAway(sr, x, z)) return;
            if (!StandPointSafe(s, sr.X, sr.Z, x, z, Math.Max(1f, sr.Strength), st)) return;
            scout = c; chosen = kind;
            lookX = standX = x;
            lookZ = standZ = z;
        }

        private static int NearestScout(BattleSnapshot s, in ReconSettings st, ReconPicture p, float x, float z)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if (!IsScoutCandidate(s, i, st, p)) continue;
                float d = Distance(s.Own[i].X, s.Own[i].Z, x, z);
                if (d >= bestD) continue;
                bestD = d;
                best = i;
            }
            return best;
        }

        /// <summary>
        /// A recon scout: an own cavalry regiment (not a company) that stands, is fit
        /// and AI-controlled, with no visible enemy inside the stand-off, under a
        /// settled AI brigade the posture producer did not order this tick.
        /// </summary>
        public static bool IsScoutCandidate(BattleSnapshot s, int i, in ReconSettings st, ReconPicture p)
        {
            if (s == null || p == null || i < 0 || i >= s.OwnCount) return false;
            ref UnitRecord rec = ref s.Own[i];
            if (rec.UnitTyp != TacticalUnitType.Cavalry) return false;
            if ((rec.Flags & (UnitFlags.Alive | UnitFlags.Active)) != (UnitFlags.Alive | UnitFlags.Active)) return false;
            if ((rec.Flags & ScoutRefusals) != 0) return false;
            if (rec.RegimentPaths > 0 || rec.QueuedOrders > 0 || rec.MovementMode != 0) return false;
            if (rec.Morale < st.MinMorale) return false;
            if (NearestVisible(s, rec.X, rec.Z, out float d, out _) && d <= st.ObservationStandOff) return false;

            int parent = rec.ParentIndex;
            if (parent < 0 || parent >= s.OwnCount) return false;
            ref UnitRecord g = ref s.Own[parent];
            if ((g.Flags & UnitFlags.IsGroup) == 0 || (g.Flags & UnitFlags.Active) == 0) return false;
            if ((g.Flags & (UnitFlags.PlayerOwned | UnitFlags.Feud | UnitFlags.Routed)) != 0) return false;
            if (g.RegimentPaths > 0 || g.QueuedOrders > 0 || g.AiStanceOrdered == ChargeDoctrine.AssaultStance) return false;
            return parent >= p.PostureMoved.Length || !p.PostureMoved[parent];
        }

        /// <summary>
        /// Never toward a visible superior enemy: refused when a visible enemy at least
        /// as strong lies within the stand-off of the point or within the corridor of
        /// the straight path to it.
        /// </summary>
        public static bool StandPointSafe(BattleSnapshot s, float fromX, float fromZ, float x, float z, float ownStrength, in ReconSettings st)
        {
            if (s == null) return false;
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible || obs[k].IsReinforcement) continue;
                if (obs[k].Strength < ownStrength) continue;
                if (Distance(obs[k].X, obs[k].Z, x, z) <= st.ObservationStandOff) return false;
                if (DistanceToSegment(obs[k].X, obs[k].Z, fromX, fromZ, x, z) <= st.CorridorMeters) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- probe

        private static void ProbeTick(BattleSnapshot s, in ReconSettings st, ReconPicture p, ReconMemory m, IIntentSink sink,
            double now, ref ReconTickResult r)
        {
            TacticalFrontAxis axis = s.FrontAxis;
            bool active = p.Phase == BattlePhase.Probe && !p.SideDefending && axis.IsValid && axis.BandCount > 0;
            int bands = active ? Math.Min(axis.BandCount, ReconMemory.MaxBands) : 0;

            // Holds first (a hold made below is checked from the next tick, when the
            // posture task is no longer the probe rewrite): released when the ledger
            // reassigned the group, the group is gone, or a visible enemy at least as
            // strong is within infantry sight.
            for (int k = 0; k < m.HoldSlots; k++)
            {
                int id = m.HoldGroup(k);
                if (id == 0) continue;
                int idx = s.IndexOfOwn(id);
                if (idx < 0 || idx >= p.PostureTask.Length || p.PostureTask[idx] != m.HoldTask(k) ||
                    SuperiorWithin(s, s.Own[idx], st.InfantrySight))
                    m.ClearHold(k);
            }

            for (int b = 0; b < ReconMemory.MaxBands; b++)
            {
                int id = m.ProbeGroup(b);
                if (id == 0) continue;
                int idx = s.IndexOfOwn(id);
                if (!active || b >= bands || idx < 0)
                {
                    m.ClearProbe(b);
                    r.Events |= ReconEvents.ProbeEnded;
                    continue;
                }
                ref UnitRecord rec = ref s.Own[idx];
                bool contact = NearestVisible(s, rec.X, rec.Z, out float d, out _) && d <= st.InfantrySight;
                bool arrived = Distance(rec.X, rec.Z, m.ProbeX(b), m.ProbeZ(b)) <= OrderArbiter.DedupeMeters;
                bool timedOut = now - m.ProbeStarted(b) > ReconSettings.MaxProbeHours;
                if (!contact && !arrived && !timedOut) continue;

                // The probe is over. Contact: stop (a screen-stance group walks on into
                // contact unless told, 117253). A visible enemy at least as strong sends
                // the group back to its ledger task; otherwise it holds the ground gained.
                if (contact && (rec.RegimentPaths > 0 || (idx < m.Moving.Length && m.Moving[idx] > 0)))
                {
                    sink.Submit(Stop(id, ProbeStopReason));
                    r.Intents++;
                }
                if (!SuperiorWithin(s, rec, st.InfantrySight) && m.Hold(b, rec.X, rec.Z))
                    r.Events |= ReconEvents.ProbeHeld;
                m.ClearProbe(b);
                r.Events |= ReconEvents.ProbeEnded;
            }

            for (int b = 0; b < bands; b++)
            {
                if (m.ProbeGroup(b) != 0 || now - m.ProbeLastStart(b) < ReconSettings.ProbeRepeatHours) continue;
                if (BandHasVisibleEnemy(s, axis, b)) continue;
                int best = -1;
                float bestForward = float.MinValue;
                float bx = 0f, bz = 0f;
                for (int i = 0; i < s.OwnCount; i++)
                {
                    if (!IsProbeCandidate(s, i, p, m)) continue;
                    ref UnitRecord rec = ref s.Own[i];
                    if (axis.SectorOf(rec.X, rec.Z) != b) continue;
                    float forward = (rec.X - axis.OriginX) * axis.ForwardX + (rec.Z - axis.OriginZ) * axis.ForwardZ;
                    bool better = best < 0 || rec.UnitTyp < s.Own[best].UnitTyp ||
                        (rec.UnitTyp == s.Own[best].UnitTyp && forward > bestForward);
                    if (!better) continue;
                    if (!LookPoint(s, i, st, p, out float x, out float z)) continue;
                    best = i;
                    bestForward = forward;
                    bx = x;
                    bz = z;
                }
                if (best < 0) continue;
                ref UnitRecord pick = ref s.Own[best];
                CommandTaskType ledgerTask = best < p.PostureTask.Length ? p.PostureTask[best] : CommandTaskType.None;
                m.Nominate(b, pick.InstanceId, pick.X, pick.Z, bx, bz, ledgerTask, now);
                r.Events |= ReconEvents.ProbeNominated;
            }
        }

        // A visible enemy at least as strong as the group within the given distance.
        private static bool SuperiorWithin(BattleSnapshot s, in UnitRecord rec, float meters)
        {
            float strength = Math.Max(1f, Math.Max(rec.Strength, rec.GroupStrengthAiGroup));
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible || obs[k].IsReinforcement) continue;
                if (obs[k].Strength < strength) continue;
                if (Distance(obs[k].X, obs[k].Z, rec.X, rec.Z) <= meters) return true;
            }
            return false;
        }

        /// <summary>
        /// A held group's posture move is dropped when it would walk the ground the
        /// probe gained back: a target more than the arbiter's dedupe radius behind the
        /// held point along the probe's advance. Forward and along-the-line targets
        /// go; a break-off, a fallback or a recovery keeps its own target.
        /// </summary>
        public static bool HoldBlocks(PostureExecutionTarget kind, float holdX, float holdZ, float dirX, float dirZ, float targetX, float targetZ)
        {
            if (kind == PostureExecutionTarget.DoctrineFallbackTarget || kind == PostureExecutionTarget.FallbackLine ||
                kind == PostureExecutionTarget.RecoveryPath || kind == PostureExecutionTarget.None)
                return false;
            float along = (targetX - holdX) * dirX + (targetZ - holdZ) * dirZ;
            return along < -OrderArbiter.DedupeMeters;
        }

        private static bool IsProbeCandidate(BattleSnapshot s, int i, ReconPicture p, ReconMemory m)
        {
            ref UnitRecord rec = ref s.Own[i];
            UnitFlags f = rec.Flags;
            if ((f & UnitFlags.IsGroup) == 0 || (f & UnitFlags.Active) == 0) return false;
            if (i >= p.AiPool.Length || !p.AiPool[i]) return false;
            if ((f & (UnitFlags.Linked | UnitFlags.Routed | UnitFlags.MarkedForRout | UnitFlags.PlayerOwned | UnitFlags.Feud)) != 0) return false;
            UnitFlags below = i < m.Descendants.Length ? m.Descendants[i] : UnitFlags.None;
            if ((below & UnitFlags.PlayerOwned) != 0) return false;
            if (((f | below) & (UnitFlags.IsInfantry | UnitFlags.IsCavalry)) == 0) return false;
            if (rec.RegimentPaths > 0 || rec.QueuedOrders > 0 || rec.AiStanceOrdered == ChargeDoctrine.AssaultStance) return false;
            return !m.IsNominated(rec.InstanceId);
        }

        private static bool BandHasVisibleEnemy(BattleSnapshot s, in TacticalFrontAxis axis, int band)
        {
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible || obs[k].IsReinforcement) continue;
                if (axis.SectorOf(obs[k].X, obs[k].Z) == band) return true;
            }
            return false;
        }

        /// <summary>
        /// Where a Scout or Probe task looks: one infantry sight depth past the group,
        /// along the side's front axis (toward the objective, and no farther than it,
        /// while the axis has no target), never toward a visible superior enemy.
        /// </summary>
        public static bool LookPoint(BattleSnapshot s, int i, in ReconSettings st, ReconPicture p, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            if (s == null || i < 0 || i >= s.OwnCount) return false;
            ref UnitRecord rec = ref s.Own[i];
            if (!AdvanceDirection(s, p, rec.X, rec.Z, out float dx, out float dz, out float reach)) return false;
            float advance = Math.Min(st.ProbeAdvanceMeters, reach);
            x = rec.X + dx * advance;
            z = rec.Z + dz * advance;
            if (!PostureSense.IsMoveAway(rec, x, z)) return false;
            float strength = Math.Max(1f, Math.Max(rec.Strength, rec.GroupStrengthAiGroup));
            return StandPointSafe(s, rec.X, rec.Z, x, z, strength, st);
        }

        /// <summary>
        /// A Scout or Probe task's forward targets (the doctrine primary target, the
        /// objective approach) are the look point; a break-off (DoctrineFallbackTarget,
        /// ShouldBreakOffRecon) or a recovery path keeps the posture decision's own target.
        /// </summary>
        public static bool UsesLookPoint(CommandTaskType task, PostureExecutionTarget kind)
        {
            if (task != CommandTaskType.Scout && task != CommandTaskType.Probe) return false;
            return kind == PostureExecutionTarget.DoctrinePrimaryTarget || kind == PostureExecutionTarget.ObjectiveApproach;
        }

        /// <summary>
        /// The direction the side looks in from a point: the snapshot front axis once it
        /// has a target (a seen enemy), else toward the operation's objective
        /// (<paramref name="reach"/> = the distance to it). False with neither.
        /// </summary>
        public static bool AdvanceDirection(BattleSnapshot s, ReconPicture p, float fromX, float fromZ, out float dx, out float dz, out float reach)
        {
            dx = 0f;
            dz = 0f;
            reach = float.MaxValue;
            TacticalFrontAxis axis = s.FrontAxis;
            if (axis.IsValid && axis.TargetDistance > 0f)
            {
                dx = axis.ForwardX;
                dz = axis.ForwardZ;
                return true;
            }
            if (p == null || !p.HasObjective) return false;
            float d = Distance(fromX, fromZ, p.ObjectiveX, p.ObjectiveZ);
            if (d < 1f) return false;
            dx = (p.ObjectiveX - fromX) / d;
            dz = (p.ObjectiveZ - fromZ) / d;
            reach = d;
            return true;
        }

        // ---------------------------------------------------------------- facing, follow, skirmishers

        /// <summary>
        /// The bearing from own unit <paramref name="i"/> to the stalest stale enemy-side
        /// entry point (never watched first, nearest among those; else the one watched
        /// longest ago). False when every entry point was watched within the hour.
        /// </summary>
        public static bool TryEntryBearing(BattleSnapshot s, int i, ReconPicture p, out float heading)
        {
            heading = -1f;
            if (s == null || p == null || i < 0 || i >= s.OwnCount) return false;
            double now = s.DoctrineHours;
            ref UnitRecord rec = ref s.Own[i];
            int best = -1;
            double bestWatched = double.MaxValue;
            float bestDistance = float.MaxValue;
            for (int k = 0; k < p.EntryCount; k++)
            {
                if (!p.EntryStale(k, now)) continue;
                double w = p.EntryWatchedHours(k) < 0.0 ? double.NegativeInfinity : p.EntryWatchedHours(k);
                float d = Distance(rec.X, rec.Z, p.EntryX(k), p.EntryZ(k));
                if (w > bestWatched || (w == bestWatched && d >= bestDistance)) continue;
                best = k;
                bestWatched = w;
                bestDistance = d;
            }
            if (best < 0) return false;
            heading = PostureSense.HeadingTo(rec.X, rec.Z, p.EntryX(best), p.EntryZ(best));
            return heading >= 0f;
        }

        /// <summary>A regiment in square (formation 4, 118174); a group's 4 is march column.</summary>
        public static bool IsSquare(int unitTyp, int formation)
        {
            return unitTyp <= TacticalUnitType.MaxCombat && formation == SquareFormation;
        }

        /// <summary>An officer for the cavalry raid filter: a command group (HQ) or a courier.</summary>
        public static bool IsOfficerTarget(int unitTyp)
        {
            return unitTyp > TacticalUnitType.MaxCombat || unitTyp == TacticalUnitType.Courier;
        }

        /// <summary>
        /// Extra metres SkirmisherScreenDistancePatch moves an AI skirmisher company's front link target forward,
        /// or 0 for vanilla's own (unitlinkdistance + skirmisherdistance). Only for a
        /// detached skirmisher company whose command group is in screen stance, or in
        /// attack stance while it moves, with nothing in its own range list
        /// (enemyinrangereg empty, so vanilla's re-link is running, 124467), not in melee,
        /// and no visible enemy within the cancel distance plus the push.
        /// </summary>
        public static float SkirmisherPushMeters(BattleSnapshot s, int i, float vanillaLinkMeters, float pushMeters, float cancelMeters)
        {
            if (s == null || i < 0 || i >= s.OwnCount) return 0f;
            ref UnitRecord rec = ref s.Own[i];
            if (rec.UnitTyp != TacticalUnitType.Skirmisher || (rec.Flags & UnitFlags.DetachedCompany) == 0) return 0f;
            if ((rec.Flags & UnitFlags.Active) == 0) return 0f;
            if ((rec.Flags & (UnitFlags.PlayerOwned | UnitFlags.Feud | UnitFlags.InMelee | UnitFlags.Routed | UnitFlags.MarkedForRout)) != 0) return 0f;
            if (rec.FirstEnemyInRangeId != 0) return 0f;
            int parent = rec.ParentIndex;
            if (parent < 0 || parent >= s.OwnCount) return 0f;
            ref UnitRecord g = ref s.Own[parent];
            bool screening = g.AiStance == TacticalCommandTaskToStanceMapper.StanceScreen;
            bool approaching = g.AiStance == TacticalCommandTaskToStanceMapper.StanceAttack && g.RegimentPaths > 0;
            if (!screening && !approaching) return 0f;
            if (NearestVisible(s, rec.X, rec.Z, out float d, out _) && d <= cancelMeters + pushMeters) return 0f;
            float extra = pushMeters - vanillaLinkMeters;
            return extra > 0f ? extra : 0f;
        }

        // ---------------------------------------------------------------- geometry

        /// <summary>World point of a ledger lateral (x·right + z·right of the frame heading) at a forward depth.</summary>
        public static void BandWorldPoint(float lateral, float headingDegrees, float forwardDepth, out float x, out float z)
        {
            double h = headingDegrees * Math.PI / 180.0;
            float fx = (float)Math.Sin(h), fz = (float)Math.Cos(h);
            // TacticalFrontAxis: right = (forwardZ, -forwardX).
            float rx = fz, rz = -fx;
            x = rx * lateral + fx * forwardDepth;
            z = rz * lateral + fz * forwardDepth;
        }

        // The own combat regiments' centroid, their mean forward depth in the ledger
        // frame (world-anchored) and along the snapshot axis (from its origin).
        private static bool OwnLine(BattleSnapshot s, float frameHeading, out float cx, out float cz, out float frameDepth, out float axisDepth)
        {
            cx = cz = frameDepth = axisDepth = 0f;
            double h = (float.IsNaN(frameHeading) ? 0f : frameHeading) * Math.PI / 180.0;
            float fx = (float)Math.Sin(h), fz = (float)Math.Cos(h);
            TacticalFrontAxis axis = s.FrontAxis;
            int n = 0;
            for (int i = 0; i < s.OwnCount; i++)
            {
                ref UnitRecord u = ref s.Own[i];
                if (u.UnitTyp > TacticalUnitType.MaxCombat || u.UnitTyp == TacticalUnitType.Courier) continue;
                if ((u.Flags & (UnitFlags.Alive | UnitFlags.Active)) != (UnitFlags.Alive | UnitFlags.Active)) continue;
                if ((u.Flags & (UnitFlags.Routed | UnitFlags.DetachedCompany)) != 0) continue;
                cx += u.X;
                cz += u.Z;
                frameDepth += u.X * fx + u.Z * fz;
                if (axis.IsValid) axisDepth += (u.X - axis.OriginX) * axis.ForwardX + (u.Z - axis.OriginZ) * axis.ForwardZ;
                n++;
            }
            if (n == 0) return false;
            cx /= n;
            cz /= n;
            frameDepth /= n;
            axisDepth /= n;
            return true;
        }

        private static bool NearestVisible(BattleSnapshot s, float x, float z, out float distance, out float strength)
        {
            distance = float.MaxValue;
            strength = 0f;
            bool any = false;
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible || obs[k].IsReinforcement) continue;
                float d = Distance(obs[k].X, obs[k].Z, x, z);
                if (d >= distance) continue;
                distance = d;
                strength = obs[k].Strength;
                any = true;
            }
            return any;
        }

        private static void End(ReconMemory m, ref ReconTickResult r, string reason)
        {
            m.End();
            r.Events |= ReconEvents.MissionEnded;
            r.EndReason = reason;
        }

        private static BattleOrderIntent Move(int id, float x, float z, string reason)
        {
            return new BattleOrderIntent
            {
                GroupId = id, Channel = OrderChannel.Move, TargetX = x, TargetZ = z, Heading = -1f,
                Priority = IntentPriority.Support, Source = IntentSource.Recon, Flags = IntentFlags.UseOrderDelay,
                Task = CommandTaskType.Scout, Reason = reason,
            };
        }

        private static BattleOrderIntent Stop(int id, string reason)
        {
            return new BattleOrderIntent
            {
                GroupId = id, Channel = OrderChannel.Stop, Heading = -1f,
                Priority = IntentPriority.Support, Source = IntentSource.Recon, Task = CommandTaskType.Scout, Reason = reason,
            };
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static float DistanceToSegment(float px, float pz, float ax, float az, float bx, float bz)
        {
            float vx = bx - ax, vz = bz - az;
            float len2 = vx * vx + vz * vz;
            if (len2 < 0.001f) return Distance(px, pz, ax, az);
            float u = ((px - ax) * vx + (pz - az) * vz) / len2;
            if (u < 0f) u = 0f;
            if (u > 1f) u = 1f;
            return Distance(px, pz, ax + u * vx, az + u * vz);
        }
    }
}
