using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>The own deployment zone while the deployment phase is on (#60's zone, <c>Frontline2.GetClosestPointInDeploymentZone</c>); inactive in the battle.</summary>
    public interface IOwnDeploymentZone
    {
        bool Active { get; }
        /// <summary>The point moved into the zone, nearest to it; false when the zone has none.</summary>
        bool TryClamp(float fromX, float fromZ, ref float x, ref float z);
    }

    public enum LeafReserveMoveKind { Slot, Release }

    /// <summary>One move the planner submitted, for the trace.</summary>
    public readonly struct LeafReserveMove
    {
        public LeafReserveMove(LeafReserveMoveKind kind, int childId, int parentId, float x, float z, float distance, DirectChildRole role, int sector)
        {
            Kind = kind; ChildId = childId; ParentId = parentId; X = x; Z = z; Distance = distance; Role = role; Sector = sector;
        }

        public LeafReserveMoveKind Kind { get; }
        public int ChildId { get; }
        public int ParentId { get; }
        public float X { get; }
        public float Z { get; }
        /// <summary>How far the child stood from the point when the order was made.</summary>
        public float Distance { get; }
        public DirectChildRole Role { get; }
        public int Sector { get; }
    }

    /// <summary>
    /// The Defender's reserve on the field (verify r2 F7: the hold-back and the release changed a label and nothing moved, because a Defender's leaf tasks
    /// are all hold tasks). A reserve child of a holding command stands in a slot behind the command's line: its centre less
    /// <see cref="DeploymentDoctrine.DivisionSupportMeters"/> along the front axis, lanes of at most <see cref="DeploymentDoctrine.ReserveLaneMeters"/> across it
    /// in the order the reserves already stand (the geometry of #60's reserve echelon, <see cref="DeploymentDoctrine.ProposeReserve"/>, at division scale). The slot
    /// stays on the own side of every river line (the BC14 land region of the line centre; a slot in another region or on a river is pulled back toward the line
    /// until it is in it, else there is none) and, while the deployment phase is on, inside the own deployment zone (<see cref="IOwnDeploymentZone"/>);
    /// the executor's waypoint validation steps it back from water and fences. A released reserve (<see cref="LeafReserveRelease"/>) goes to its trigger point:
    /// behind the standing line of the crisis band, the exposed wing's band or the main effort's band (the stand-off of #59's release), and when the
    /// release ends it returns to its slot. Both are executor Moves of source ReserveRelease, so the crossing gate sees them. Never while the child is in contact,
    /// never a player-owned unit or a command holding one. Pure; the producer supplies the snapshot and the sink.
    /// </summary>
    public static class LeafReservePlanner
    {
        public const string SlotReason = "leaf-reserve-slot";
        public const string ReleaseReason = "leaf-reserve-release";

        private const float ClampStepFractions = 4f;

        // Grow-only scratch, single-threaded (the arbiter tick).
        private static bool[] _heldReserve = new bool[0];

        /// <summary>
        /// Plans the slot and release moves of every holding command in <paramref name="commands"/> and submits them. Returns how many were submitted;
        /// each is also appended to <paramref name="moves"/> when it is not null.
        /// </summary>
        public static int Produce(BattleSnapshot s, IReadOnlyList<DefenderReserveRecord> commands, IReadOnlyDictionary<int, DirectChildRole> released,
            IReadOnlyList<LeafRelease> releases, UnitFlags[] descendants, int[] moving, ICrossingPicture picture, IOwnDeploymentZone zone, IIntentSink sink,
            List<LeafReserveMove> moves)
        {
            if (s == null || sink == null || commands == null || commands.Count == 0) return 0;
            TacticalFrontAxis axis = s.FrontAxis;
            if (!axis.IsValid) return 0;
            int own = s.OwnCount;
            if (_heldReserve.Length < own) _heldReserve = new bool[Math.Max(own, _heldReserve.Length * 2)];
            Array.Clear(_heldReserve, 0, own);

            // Which own units stand as held reserves now (their lines are not their own reference).
            for (int c = 0; c < commands.Count; c++)
            {
                DefenderReserveRecord command = commands[c];
                if (!command.CanRelease) continue;
                for (int i = 0; i < command.ChildIds.Length; i++)
                {
                    if (command.BaseRoles[i] != DirectChildRole.Reserve || IsReleased(released, command.ChildIds[i])) continue;
                    int at = s.IndexOfOwn(command.ChildIds[i]);
                    if (at >= 0) _heldReserve[at] = true;
                }
            }

            int submitted = 0;
            for (int c = 0; c < commands.Count; c++)
            {
                DefenderReserveRecord command = commands[c];
                if (!command.CanRelease) continue;
                submitted += ProduceSlots(s, axis, command, released, descendants, moving, picture, zone, sink, moves);
            }
            if (releases != null)
            {
                for (int r = 0; r < releases.Count; r++)
                    submitted += ProduceRelease(s, axis, releases[r], descendants, moving, sink, moves);
            }
            return submitted;
        }

        private static bool IsReleased(IReadOnlyDictionary<int, DirectChildRole> released, int id)
            => released != null && released.ContainsKey(id);

        private static int ProduceSlots(BattleSnapshot s, TacticalFrontAxis axis, in DefenderReserveRecord command, IReadOnlyDictionary<int, DirectChildRole> released,
            UnitFlags[] descendants, int[] moving, ICrossingPicture picture, IOwnDeploymentZone zone, IIntentSink sink, List<LeafReserveMove> moves)
        {
            int n = command.ChildIds.Length;
            UnitRecord[] own = s.Own;

            // The line: the command's subordinates that are not held back, where they stand now.
            float sumX = 0f, sumZ = 0f;
            int line = 0, held = 0;
            for (int i = 0; i < n; i++)
            {
                int at = s.IndexOfOwn(command.ChildIds[i]);
                if (at < 0 || !Standing(in own[at])) continue;
                if (command.BaseRoles[i] == DirectChildRole.Reserve)
                {
                    if (!IsReleased(released, command.ChildIds[i])) held++;
                    continue;
                }
                sumX += own[at].X;
                sumZ += own[at].Z;
                line++;
            }
            if (line == 0 || held == 0) return 0;
            float centreX = sumX / line, centreZ = sumZ / line;

            int submitted = 0;
            for (int i = 0; i < n; i++)
            {
                if (command.BaseRoles[i] != DirectChildRole.Reserve || IsReleased(released, command.ChildIds[i])) continue;
                int at = s.IndexOfOwn(command.ChildIds[i]);
                if (at < 0 || !Standing(in own[at]) || HoldsPlayer(s, at)) continue;

                // Its lane: its rank across the front among the reserves still held in this command.
                float lateral = axis.LateralOf(own[at].X, own[at].Z);
                int rank = 0;
                for (int j = 0; j < n; j++)
                {
                    if (j == i || command.BaseRoles[j] != DirectChildRole.Reserve || IsReleased(released, command.ChildIds[j])) continue;
                    int other = s.IndexOfOwn(command.ChildIds[j]);
                    if (other < 0 || !Standing(in own[other])) continue;
                    float otherLateral = axis.LateralOf(own[other].X, own[other].Z);
                    if (otherLateral < lateral || (otherLateral == lateral && command.ChildIds[j] < command.ChildIds[i])) rank++;
                }
                float lane = held == 1 ? 0f : -DeploymentDoctrine.ReserveLaneMeters + 2f * DeploymentDoctrine.ReserveLaneMeters * rank / (held - 1);

                float x = centreX - axis.ForwardX * DeploymentDoctrine.DivisionSupportMeters + axis.RightX * lane;
                float z = centreZ - axis.ForwardZ * DeploymentDoctrine.DivisionSupportMeters + axis.RightZ * lane;
                if (!OnOwnSide(picture, centreX, centreZ, ref x, ref z)) continue;
                if (zone != null && zone.Active && !zone.TryClamp(own[at].X, own[at].Z, ref x, ref z)) continue;

                if (!MayMove(s, at, descendants, moving, requireParentIdle: true, out _)) continue;
                if (!PostureSense.IsMoveAway(in own[at], x, z)) continue;

                sink.Submit(new BattleOrderIntent
                {
                    GroupId = own[at].InstanceId,
                    Channel = OrderChannel.Move,
                    TargetX = x,
                    TargetZ = z,
                    Heading = axis.HeadingDegrees,
                    Priority = IntentPriority.Support,
                    Source = IntentSource.ReserveRelease,
                    Flags = IntentFlags.UseOrderDelay | IntentFlags.ReliefOrSupport,
                    Task = CommandTaskType.ReserveWait,
                    Reason = SlotReason,
                });
                submitted++;
                moves?.Add(new LeafReserveMove(LeafReserveMoveKind.Slot, own[at].InstanceId, command.ParentInstanceId, x, z,
                    PostureSense.Distance(own[at].X, own[at].Z, x, z), DirectChildRole.Reserve, -1));
            }
            return submitted;
        }

        private static int ProduceRelease(BattleSnapshot s, TacticalFrontAxis axis, in LeafRelease release, UnitFlags[] descendants, int[] moving, IIntentSink sink,
            List<LeafReserveMove> moves)
        {
            int at = s.IndexOfOwn(release.ChildInstanceId);
            if (at < 0 || !Standing(in s.Own[at]) || HoldsPlayer(s, at)) return 0;
            if (!StandingLine(s, axis, at, release.Sector, out float lineX, out float lineZ)) return 0;
            float x = lineX - axis.ForwardX * TacticalReserveCommitGate.ReleaseStandOffMeters;
            float z = lineZ - axis.ForwardZ * TacticalReserveCommitGate.ReleaseStandOffMeters;
            if (!MayMove(s, at, descendants, moving, requireParentIdle: false, out _)) return 0;
            if (!PostureSense.IsMoveAway(in s.Own[at], x, z)) return 0;

            sink.Submit(new BattleOrderIntent
            {
                GroupId = s.Own[at].InstanceId,
                Channel = OrderChannel.Move,
                TargetX = x,
                TargetZ = z,
                Heading = axis.HeadingDegrees,
                Priority = IntentPriority.Support,
                Source = IntentSource.ReserveRelease,
                Flags = IntentFlags.UseOrderDelay | IntentFlags.ReliefOrSupport,
                Task = CommandTaskType.ReleaseReserve,
                Reason = ReleaseReason,
            });
            moves?.Add(new LeafReserveMove(LeafReserveMoveKind.Release, s.Own[at].InstanceId, release.ParentInstanceId, x, z,
                PostureSense.Distance(s.Own[at].X, s.Own[at].Z, x, z), release.Role, release.Sector));
            return 1;
        }

        // The centroid of the own brigade-tier groups standing in the band that are not held reserves: the line the release goes to.
        private static bool StandingLine(BattleSnapshot s, TacticalFrontAxis axis, int self, int sector, out float x, out float z)
        {
            x = z = 0f;
            UnitRecord[] own = s.Own;
            float sx = 0f, sz = 0f;
            int count = 0;
            int tier = own[self].UnitTyp;
            for (int j = 0; j < s.OwnCount; j++)
            {
                if (j == self || own[j].UnitTyp != tier || _heldReserve[j] || !Standing(in own[j])) continue;
                if ((own[j].Flags & UnitFlags.IsGroup) == 0) continue;
                if (axis.SectorOf(own[j].X, own[j].Z) != sector) continue;
                sx += own[j].X;
                sz += own[j].Z;
                count++;
            }
            if (count == 0) return false;
            x = sx / count;
            z = sz / count;
            return true;
        }

        private static bool Standing(in UnitRecord u)
        {
            UnitFlags f = u.Flags;
            return (f & UnitFlags.Alive) != 0 && (f & UnitFlags.Active) != 0 && !PostureSense.Routed(in u)
                && (f & (UnitFlags.PlayerOwned | UnitFlags.Feud)) == 0;
        }

        // The child, or a unit under it, is the W&L player's own or holds the player's command: nothing moves it.
        private static bool HoldsPlayer(BattleSnapshot s, int child)
        {
            const UnitFlags player = UnitFlags.PlayerOwned | UnitFlags.PlayerCommand | UnitFlags.AncestorOfPlayerCommand;
            UnitRecord[] own = s.Own;
            if ((own[child].Flags & player) != 0) return true;
            for (int i = 0; i < s.OwnCount; i++)
                if ((own[i].Flags & player) != 0 && s.IsAncestorOf(child, i)) return true;
            return false;
        }

        // The gates of the leaf pass (LeafBrigadeMovePolicy): none in contact, none over a pending order or an active move, and a slot move
        // not while the holding command itself has an order in flight (vanilla re-forms its subordinates with it).
        private static bool MayMove(BattleSnapshot s, int at, UnitFlags[] descendants, int[] moving, bool requireParentIdle, out string reason)
        {
            reason = null;
            UnitRecord[] own = s.Own;
            UnitFlags below = descendants != null && at < descendants.Length ? descendants[at] : UnitFlags.None;
            if (PostureSense.CloseEngaged(s, at, below)) { reason = "in-contact"; return false; }
            int movers = moving != null && at < moving.Length ? moving[at] : own[at].RegimentPaths > 0 ? 1 : 0;
            bool active = PostureSense.ActiveMove(in own[at], movers);
            if (PostureSense.PendingOrder(in own[at], active, allowStaleQueuedBypass: false)) { reason = "order-pending"; return false; }
            if (requireParentIdle)
            {
                if (active) { reason = "moving"; return false; }
                int parent = own[at].ParentIndex;
                if (parent >= 0 && parent < s.OwnCount)
                {
                    int parentMovers = moving != null && parent < moving.Length ? moving[parent] : own[parent].RegimentPaths > 0 ? 1 : 0;
                    bool parentActive = PostureSense.ActiveMove(in own[parent], parentMovers);
                    if (own[parent].RegimentPaths > 0 || parentActive || PostureSense.PendingOrder(in own[parent], parentActive, allowStaleQueuedBypass: false))
                    {
                        reason = "parent-ordered";
                        return false;
                    }
                }
            }
            return true;
        }

        // The slot stays in the land region of the line centre, the own side of every river line: pulled back toward the line in quarter steps until it is.
        private static bool OnOwnSide(ICrossingPicture picture, float centreX, float centreZ, ref float x, ref float z)
        {
            if (picture == null || !picture.Ready) return true;
            int region = picture.RegionAt(centreX, centreZ);
            if (region < 0) return false;
            float fx = x, fz = z;
            for (int k = 0; k <= (int)ClampStepFractions; k++)
            {
                float f = 1f - k / ClampStepFractions;
                float px = centreX + (fx - centreX) * f, pz = centreZ + (fz - centreZ) * f;
                if (picture.RegionAt(px, pz) != region) continue;
                x = px;
                z = pz;
                return true;
            }
            return false;
        }
    }
}
