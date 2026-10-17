using System;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14W: the ledger's half of the enemy works doctrine (spec § 7A): the focal point FocalAssault faces, which children of the selected plan assault or
    // face works, and the works hold (requirement-works-hold) the stored-product guard, BC11 and the phase machine read.
    public sealed partial class FeasibilityLedger
    {
        /// <summary>The works hold's reasons (static literals): a works assault whose detail term is at least <see cref="ManeuverFeasibility.RiskyPenalty"/>, and a turn not yet struck.</summary>
        public const string ReasonHopelessAssault = "hopeless-assault", ReasonTurnNotStruck = "turn-not-struck";

        private struct WorksChild
        {
            public string ChildId;
            public bool Assaults, Faces;
            /// <summary>A holding fix facing unbroken works: it is held at stance 2 (the integrator's rule, spec § 7A requirement-works-pin).</summary>
            public bool Pins;
        }

        private FocalPoint _focalFront, _focalBack;
        private bool _hasFocalFront, _hasFocalBack;
        private readonly WorksChild[] _worksChildren = new WorksChild[ManeuverFeasibility.MaxChildren];
        private int _worksChildCount;
        private bool _worksHold;
        private string _worksReason;
        // The row of the works hold (TacticalWorksHold): the worst works assault's detail term and the bar it was held to, and how many children assault or face.
        private float _worksDetail, _worksBarUsed;
        private int _worksAssaultCount;
        private int _worksHash;
        // The world the last heavy tick read BC11's phases from: a publish or a new selection recomputes the hold with it, so the hold of a wing
        // that has reached its Attack does not flip back to turn-not-struck every time an evaluation publishes.
        private IMonitorWorld _worksWorld;

        internal void SetFocalBack(bool has, in FocalPoint focal)
        {
            _hasFocalBack = has;
            _focalBack = focal;
        }

        private void PublishFocal()
        {
            _hasFocalFront = _hasFocalBack;
            _focalFront = _focalBack;
        }

        /// <inheritdoc />
        public bool TryFocalPoint(out FocalPoint focal)
        {
            focal = _focalFront;
            return _hasFocalFront;
        }

        /// <inheritdoc />
        public bool WorksHold(out string reason)
        {
            reason = _worksReason;
            return _worksHold;
        }

        /// <inheritdoc />
        public bool AssaultsWorks(string childId)
        {
            for (int i = 0; i < _worksChildCount; i++)
                if (string.Equals(_worksChildren[i].ChildId, childId, StringComparison.Ordinal)) return _worksChildren[i].Assaults;
            return false;
        }

        /// <inheritdoc />
        public bool FacesWorks(string childId)
        {
            for (int i = 0; i < _worksChildCount; i++)
                if (string.Equals(_worksChildren[i].ChildId, childId, StringComparison.Ordinal)) return _worksChildren[i].Faces;
            return false;
        }

        /// <inheritdoc />
        public bool PinsWorks(string childId)
        {
            for (int i = 0; i < _worksChildCount; i++)
                if (string.Equals(_worksChildren[i].ChildId, childId, StringComparison.Ordinal)) return _worksChildren[i].Pins;
            return false;
        }

        /// <inheritdoc />
        public int WorksHoldHash => _worksHash;

        /// <inheritdoc />
        public float Willingness => Plugin.EnemyWorksDoctrineEnabled() && HasEvaluation ? WorksRules.Willingness(in _job.Input.Army) : -1f;

        /// <summary>What the works hold row says of the selected plan: the worst works assault's detail term and the bar it was held to, the children that assault or face works and how many of them are pinned.</summary>
        internal void WorksReport(out float detail, out float barUsed, out int assaultChildren, out int facingChildren, out int pinnedChildren)
        {
            detail = _worksDetail;
            barUsed = _worksBarUsed;
            assaultChildren = _worksAssaultCount;
            facingChildren = _worksChildCount;
            pinnedChildren = 0;
            for (int i = 0; i < _worksChildCount; i++)
                if (_worksChildren[i].Pins) pinnedChildren++;
        }

        // ---- the Defender's works station (requirement-defender-works-reserve), published with the candidates ----

        private StationPoint _stationFront, _stationBack;
        private float[] _stationShiftFront = new float[ManeuverFeasibility.MaxChildren], _stationShiftBack = new float[ManeuverFeasibility.MaxChildren];
        private string[] _stationIdsFront = new string[ManeuverFeasibility.MaxChildren], _stationIdsBack = new string[ManeuverFeasibility.MaxChildren];
        private int _stationChildrenFront, _stationChildrenBack;

        internal void ClearStationBack()
        {
            _stationBack = default(StationPoint);
            _stationChildrenBack = 0;
        }

        internal void SetStationBack(bool has, in StationPoint station, float[] shiftByChild, int childCount, System.Collections.Generic.IReadOnlyList<string> childIds)
        {
            ClearStationBack();
            if (!has) return;
            _stationBack = station;
            _stationChildrenBack = Math.Min(childCount, ManeuverFeasibility.MaxChildren);
            for (int c = 0; c < _stationShiftBack.Length; c++)
            {
                bool used = c < _stationChildrenBack;
                _stationShiftBack[c] = used ? shiftByChild[c] : float.NaN;
                _stationIdsBack[c] = used && c < childIds.Count ? childIds[c] : null;
            }
        }

        private void PublishStation()
        {
            StationPoint point = _stationFront;
            _stationFront = _stationBack;
            _stationBack = point;
            float[] shifts = _stationShiftFront;
            _stationShiftFront = _stationShiftBack;
            _stationShiftBack = shifts;
            string[] ids = _stationIdsFront;
            _stationIdsFront = _stationIdsBack;
            _stationIdsBack = ids;
            _stationChildrenFront = _stationChildrenBack;
        }

        /// <inheritdoc />
        public bool TryStation(out StationPoint station, float[] ownShiftByChild, out int childCount)
        {
            station = _stationFront;
            childCount = _stationFront.Has ? _stationChildrenFront : 0;
            if (!station.Has) return false;
            for (int c = 0; c < childCount && c < ownShiftByChild.Length; c++) ownShiftByChild[c] = _stationShiftFront[c];
            return true;
        }

        /// <inheritdoc />
        public string StationChildId(int column) => _stationFront.Has && column >= 0 && column < _stationChildrenFront ? _stationIdsFront[column] : null;

        /// <summary>
        /// The latest built evaluation's works picture (single-buffered: it can be one evaluation ahead of the published results; works do not move,
        /// so the difference is the pieces perceived since). Null with the flag off or before the first evaluation.
        /// </summary>
        public IWorksQuery HeldWorks => _job.WorksActive ? _job.Works : null;

        /// <summary>
        /// A candidate of the evaluation in progress (other than FocalAssault) is a turn that can be made: it has an enveloping body, every enveloping
        /// body strikes off held works, no body is after dark, and it is not Impossible (requirement-focal-assault's <c>turn-available</c>).
        /// </summary>
        internal bool BackHasFeasibleTurn()
        {
            for (int r = 0; r < _back.ResultCount; r++)
            {
                CandidateResult result = _back.Results[r];
                if (result.Plan == BattlePlanId.FocalAssault || result.Verdict == FeasibilityVerdict.Impossible) continue;
                bool envelops = false, offWorks = true, afterDark = false;
                for (int b = 0; b < result.BodyCount; b++)
                {
                    BodyEstimate body = _back.Bodies[result.BodyStart + b];
                    if (body.Envelops)
                    {
                        envelops = true;
                        if (body.StrikeOnWorks) offWorks = false;
                    }
                    if (body.Daylight > 0f) afterDark = true;
                }
                if (envelops && offWorks && !afterDark) return true;
            }
            return false;
        }

        /// <summary>
        /// Recomputes the works hold from the selected plan's published entry and BC11's phases (the coordinator's heavy path calls it each tick;
        /// the ledger itself calls it with that world when it publishes or captures a selection). False with the flag off or no selection.
        /// </summary>
        public void RefreshWorksHold(IMonitorWorld world)
        {
            _worksWorld = world;
            RefreshWorks(world);
        }

        private void RefreshWorks(IMonitorWorld world)
        {
            _worksChildCount = 0;
            _worksAssaultCount = 0;
            _worksDetail = 0f;
            _worksBarUsed = 0f;
            _worksHold = false;
            _worksReason = null;
            _worksHash = 0;
            if (!Plugin.EnemyWorksDoctrineEnabled() || !HasSelection) return;
            int found = -1;
            for (int r = 0; r < _front.ResultCount; r++)
                if (_front.Results[r].Plan == SelectedPlan) { found = r; break; }
            if (found < 0) return;
            CandidateResult result = _front.Results[found];
            ReadOnlySpan<BodyEstimate> bodies = Bodies(in result);
            ReadOnlySpan<MemberEstimate> members = Members(in result);

            // Which children assault or face works: the members of a works-assaulting body (a support follows its column into the body), and a holding
            // fix whose strike point lies on held works. A fix pins (stance 2) while the works it faces are not broken in at its own strike point or
            // at the strike point of any enveloping body of the plan: the strike it pins for.
            WorksPicture works = _job.WorksActive ? _job.Works : null;
            bool envelopBrokenIn = false;
            for (int b = 0; works != null && b < bodies.Length && !envelopBrokenIn; b++)
                envelopBrokenIn = bodies[b].Envelops && works.StrikeBrokenIn(bodies[b].StrikeX, bodies[b].StrikeZ);
            for (int m = 0; m < members.Length && _worksChildCount < _worksChildren.Length; m++)
            {
                BodyEstimate body = bodies[members[m].Body];
                bool reserve = members[m].Kind == ManeuverFeasibility.MemberOther && body.Main;
                bool assaults = body.AssaultsWorks && !reserve;
                bool fixFaces = !assaults && members[m].Kind == ManeuverFeasibility.MemberFix && body.StrikeOnWorks;
                if (!assaults && !fixFaces) continue;
                bool pins = fixFaces && !envelopBrokenIn && !(works != null && works.StrikeBrokenIn(body.StrikeX, body.StrikeZ));
                _worksChildren[_worksChildCount++] = new WorksChild { ChildId = members[m].ChildId, Assaults = assaults, Faces = true, Pins = pins };
                if (assaults) _worksAssaultCount++;
            }

            bool hopeless = false;
            for (int b = 0; b < bodies.Length; b++)
            {
                if (!bodies[b].AssaultsWorks) continue;
                if (bodies[b].Detail >= _worksDetail) { _worksDetail = bodies[b].Detail; _worksBarUsed = bodies[b].BarUsed; }
                if (bodies[b].Detail >= ManeuverFeasibility.RiskyPenalty) hopeless = true;
            }
            bool unstruck = false, fixFacesWorks = false;
            for (int m = 0; m < members.Length; m++)
            {
                BodyEstimate body = bodies[members[m].Body];
                if (members[m].Kind == ManeuverFeasibility.MemberFix && body.StrikeOnWorks) fixFacesWorks = true;
                if (members[m].Kind != ManeuverFeasibility.MemberEnvelops || !body.Envelops || body.StrikeOnWorks) continue;
                ManeuverPhase phase = world != null ? world.PhaseOf(members[m].ChildId) : ManeuverPhase.None;
                if (phase == ManeuverPhase.None || phase == ManeuverPhase.Hold || phase == ManeuverPhase.March || phase == ManeuverPhase.Turn) unstruck = true;
            }
            if (hopeless) _worksReason = ReasonHopelessAssault;
            else if (unstruck && fixFacesWorks) _worksReason = ReasonTurnNotStruck;
            _worksHold = _worksReason != null;

            int hash = _worksHold ? (hopeless ? 1 : 2) : 0;
            for (int i = 0; i < _worksChildCount; i++) hash = unchecked(hash * 31 + StableHash(_worksChildren[i].ChildId) + (_worksChildren[i].Assaults ? 1 : 0));
            _worksHash = hash;
        }

        private static int StableHash(string s)
        {
            int h = 17;
            for (int i = 0; s != null && i < s.Length; i++) h = unchecked(h * 31 + s[i]);
            return h;
        }
    }
}
