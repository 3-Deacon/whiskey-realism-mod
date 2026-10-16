using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.PlayerOrders;

namespace WhiskeyRealism.Strategic
{
    // The pure side of the W&L campaign couriers (plan 2026-10-15-wl-order-flow.md D9, D11).
    //
    // Vanilla's campaign calls into AIBattle.CheckCurrentOrderUpdate (calledfromcampaign: true) never
    // dedupe against the order on the panel (8640 tests !calledfromcampaign), so a continuous caller such
    // as AICampaign.AssignUnitToDefendCapital (11864) re-pops the same order every pass. A campaign call
    // therefore never meets the battle re-admit policy: it is dropped as a re-pop of the active order,
    // or it rides a courier whose delay is vanilla's own campaign formula (WlCourierDelay.CampaignYears)
    // and is then shown by vanilla (a vanilla order) or by WlCampaignDispatchRuntime (a Whiskey order).
    // Only a W&L player whose command is its own campaign-map group is ever sent one of either (vanilla's
    // gate, 8366); a player carried by an AI corps holds none (HoldsStaleCarriedOrder).
    internal static class WlCampaignCourierPolicy
    {
        public const string Repop = "repop";
        public const string CrossScope = "cross-scope";
        public const string UnitGone = "unit-gone";

        // Vanilla's order dedupe radius (8643) in campaign map units, inclusive (D9 (a), D11).
        public const float RepopUnits = WlCourierQueue.SamePointMeters;

        // The order on the panel is of this type and within 110 of this point (flat XZ): the call would
        // only show the same order again. A non-finite point is never a re-pop.
        public static bool RepopOfActive(in GivenOrderView active, int type, float x, float z)
        {
            if (!active.HasOrder || active.Type != type) return false;
            if (!IsFinite(x) || !IsFinite(z) || !IsFinite(active.X) || !IsFinite(active.Z)) return false;
            float dx = x - active.X;
            float dz = z - active.Z;
            return dx * dx + dz * dz <= RepopUnits * RepopUnits;
        }

        // Capture: why a campaign call that reaches the panel is dropped rather than queued, or null.
        // A type 8 (capital defense) is held to the capital fix; every other type to the couriers switch.
        public static string CaptureDropReason(int type, float x, float z, in GivenOrderView active, bool capitalFixOn, bool couriersOn)
        {
            bool applies = type == WlCampaignDispatchPolicy.DefendCapital ? capitalFixOn : couriersOn;
            return applies && RepopOfActive(in active, type, x, z) ? Repop : null;
        }

        // Delivery: why a courier that has arrived must not be shown, or null. A Whiskey order is
        // decided again by WlCampaignDispatchPolicy.Decide against the dispatch history (written only
        // by an accepted delivery, so an entry is never rate-limited against itself); a vanilla order is
        // dropped when the same order became active while it rode.
        public static string DeliveryDropReason(in CourierEntry e, in GivenOrderView active, WlDispatchHistory history, double nowDays,
            bool onCampaignMap, bool unitResolved, bool capitalFixOn = true)
        {
            if (!onCampaignMap || !e.CampaignCall) return CrossScope;
            if (!string.IsNullOrEmpty(e.UnitName) && !unitResolved) return UnitGone;
            if (RecordsHistory(e.Origin))
            {
                string decision = WlCampaignDispatchPolicy.Decide(e.Type, e.X, e.Z, float.NaN, history, in active, nowDays);
                return decision == WlCampaignDispatchPolicy.Issue ? null : decision;
            }
            bool applies = e.Type != WlCampaignDispatchPolicy.DefendCapital || capitalFixOn;
            return applies && RepopOfActive(in active, e.Type, e.X, e.Z) ? Repop : null;
        }

        // Vanilla's CheckDLCWLTopics (123484-123533) sets firstunitreached on a campaign order of type 5,
        // 6, 7 or 16 when the player's group enters its zone, pays the prestige and clears the slot. The
        // acknowledgement (D17 (b)) follows exactly that: a reached order of those types whose slot is
        // empty now.
        public static bool AcknowledgesClear(int type, bool firstUnitReached, bool slotEmptyNow)
        {
            bool clears = type == WlCampaignDispatchPolicy.Redeploy || type == WlCampaignDispatchPolicy.OffensiveOperation
                || type == WlCampaignDispatchPolicy.Engage || type == WlCampaignDispatchPolicy.Offensive;
            return clears && firstUnitReached && slotEmptyNow;
        }

        // The order was queued for the command the player has now (a promotion or a new career in between
        // voids it). An entry with no identity (an old sidecar) is not yet known to be wrong: the runtime
        // stamps it with the current command on the first delivery pass after the load, then checks it as
        // any other. With no current command there is nothing to compare with.
        public static bool BelongsToCommand(string entryCommandKey, string currentCommandKey)
        {
            return string.IsNullOrEmpty(entryCommandKey) || string.IsNullOrEmpty(currentCommandKey)
                || string.Equals(entryCommandKey, currentCommandKey, StringComparison.Ordinal);
        }

        public const string CommandChange = "command-change";
        public const string CarriedPlayer = "carried-player";

        // Before an arrived courier is looked at any further: why it must not be shown to the player as he
        // is now, or null. An order queued for another command (a promotion since) is void. A Whiskey
        // order (CampaignBridge) is for a player who leads his own campaign-map group; if he was moved
        // under a corps while it rode (a transfer keeps the command's name, so the command key still
        // matches), it is dropped: he is carried and is sent no campaign order. A vanilla campaign order is
        // left to vanilla's own gate (8366), which still runs when it is delivered.
        public static string PreDeliveryDropReason(in CourierEntry e, string currentCommandKey, bool playerIsOwnMapGroup)
        {
            if (!BelongsToCommand(e.PlayerCommandKey, currentCommandKey)) return CommandChange;
            if (e.Origin == OrderOrigin.CampaignBridge && !playerIsOwnMapGroup) return CarriedPlayer;
            return null;
        }

        // A Whiskey order (the strategic order bridge) leaves a record in the dispatch history when it is shown.
        public static bool RecordsHistory(OrderOrigin origin)
        {
            return origin == OrderOrigin.CampaignBridge;
        }

        // A campaign order sitting in the slot of a player who is not his own campaign-map group, with W&L
        // active and he is not the CIC (a CIC is sent none either, 8366). Vanilla never gives such a player
        // a campaign order, and Whiskey stopped on 2026-10-16, so one found there is a leftover of the
        // removed #69 dispatch (a save made before, or a demotion); a battle order is the battle flow's
        // and stays. Cleared the way vanilla clears a reached or end-of-day order: the slot set to null
        // (CheckDLCWLTopics 123533, CheckClearingBattleOrdersEOD 47989); AvatarButtonPanel.Update then
        // deletes the zone mark (160794-160799).
        public static bool HoldsStaleCarriedOrder(bool wlActive, bool playerIsCic, bool playerIsOwnMapGroup, bool hasOrder, bool orderIsBattleOrder)
        {
            return wlActive && !playerIsCic && !playerIsOwnMapGroup && hasOrder && !orderIsBattleOrder;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }

    // The campaign queue in the Whiskey sidecar: entries to and from CampaignCourierDto, and the
    // sanity checks a loaded file must pass (a hand-edited or corrupt entry is dropped, never trusted).
    internal static class WlCampaignCourierSidecar
    {
        public static CampaignCourierDto ToDto(in CourierEntry e)
        {
            return new CampaignCourierDto
            {
                Unit = e.UnitName,
                UnitCommander = e.UnitCommanderId,
                Type = e.Type,
                X = e.X,
                Y = e.Y,
                Z = e.Z,
                Name = e.Name,
                Rotation = e.Rotation,
                Width = e.Width,
                Depth = e.Depth,
                Origin = e.Origin.ToString(),
                Issuer = e.IssuerCommanderId,
                PlayerCommand = e.PlayerCommandKey,
                IssuedAt = e.IssuedAt,
                DueAt = e.DueAt,
            };
        }

        // The pending entries that go into the sidecar: those queued for the command the player has now (an
        // order for a command he has left would reach the wrong one after the reload), an unkeyed one with
        // the current command.
        public static void Select(CourierEntry[] pending, int count, string currentCommandKey, List<CampaignCourierDto> into)
        {
            for (int i = 0; i < count && i < pending.Length; i++)
            {
                CourierEntry e = pending[i];
                if (!WlCampaignCourierPolicy.BelongsToCommand(e.PlayerCommandKey, currentCommandKey)) continue;
                if (string.IsNullOrEmpty(e.PlayerCommandKey)) e.PlayerCommandKey = currentCommandKey;
                into.Add(ToDto(in e));
            }
        }

        public static bool TryFromDto(CampaignCourierDto dto, out CourierEntry entry)
        {
            entry = default(CourierEntry);
            if (dto == null) return false;
            if (!IsCampaignType(dto.Type)) return false;
            if (!Finite(dto.X) || !Finite(dto.Y) || !Finite(dto.Z) || !Finite(dto.Rotation) || !Finite(dto.Width) || !Finite(dto.Depth)) return false;
            if (!Finite(dto.IssuedAt) || !Finite(dto.DueAt)) return false;
            if (!TryOrigin(dto.Origin, out OrderOrigin origin)) return false;
            entry = new CourierEntry
            {
                UnitName = string.IsNullOrEmpty(dto.Unit) ? null : dto.Unit,
                UnitCommanderId = dto.UnitCommander,
                Type = dto.Type,
                X = dto.X,
                Y = dto.Y,
                Z = dto.Z,
                Name = dto.Name ?? string.Empty,
                Rotation = dto.Rotation,
                Width = dto.Width,
                Depth = dto.Depth,
                CampaignCall = true,
                Origin = origin,
                IssuerCommanderId = dto.Issuer,
                PlayerCommandKey = string.IsNullOrEmpty(dto.PlayerCommand) ? null : dto.PlayerCommand,
                IssuedAt = dto.IssuedAt,
                DueAt = dto.DueAt,
            };
            return true;
        }

        // At most WlCourierQueue.MaxPending valid entries from a loaded list, in file order.
        public static void Sanitize(List<CampaignCourierDto> source, List<CourierEntry> into)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count && into.Count < WlCourierQueue.MaxPending; i++)
                if (TryFromDto(source[i], out CourierEntry e)) into.Add(e);
        }

        private static bool IsCampaignType(int type)
        {
            switch (type)
            {
                case WlCampaignDispatchPolicy.Redeploy:
                case WlCampaignDispatchPolicy.OffensiveOperation:
                case WlCampaignDispatchPolicy.Engage:
                case WlCampaignDispatchPolicy.DefendCapital:
                case 9:
                case 10:
                case WlCampaignDispatchPolicy.Offensive:
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryOrigin(string text, out OrderOrigin origin)
        {
            origin = OrderOrigin.Vanilla;
            if (text == null) return false;
            if (string.Equals(text, nameof(OrderOrigin.Vanilla), StringComparison.Ordinal)) { origin = OrderOrigin.Vanilla; return true; }
            if (string.Equals(text, nameof(OrderOrigin.CampaignBridge), StringComparison.Ordinal)) { origin = OrderOrigin.CampaignBridge; return true; }
            return false;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }

    // BUG-WL-018 (D9 (b)): vanilla's AssignUnitToDefendCapital sets running and doublequick (forced
    // march, 11865-11866) on every group it keeps in groupstodefendcapital, the player's own included.
    // The patch snapshots the player-owned units' two flags before the call and writes them back after
    // it for the units that are in that list; the player's own forced march (paid, 166967) survives
    // because the snapshot holds whatever he had.
    internal static class CapitalDefenseSpeed
    {
        // CIC: vanilla skips the player's own faction (11616-11625), so nothing runs for it.
        public static bool Applies(bool wlActive, bool factionIsPlayers, bool playerIsCic, bool flagOn)
        {
            return flagOn && wlActive && factionIsPlayers && !playerIsCic;
        }
    }

    // The saved flags, in a buffer that grows once and is reused (no allocation per call after warm-up).
    internal sealed class CapitalSpeedSnapshot<TUnit>
    {
        private TUnit[] _unit = new TUnit[8];
        private bool[] _running = new bool[8];
        private bool[] _doublequick = new bool[8];
        private int _count;

        public int Count => _count;

        public void Capture(TUnit unit, bool running, bool doublequick)
        {
            if (_count == _unit.Length)
            {
                Array.Resize(ref _unit, _count * 2);
                Array.Resize(ref _running, _count * 2);
                Array.Resize(ref _doublequick, _count * 2);
            }
            _unit[_count] = unit;
            _running[_count] = running;
            _doublequick[_count] = doublequick;
            _count++;
        }

        // Writes back the units the call kept in groupstodefendcapital, then forgets the snapshot.
        public void Restore(Func<TUnit, bool> inCapitalList, Action<TUnit, bool, bool> write)
        {
            for (int i = 0; i < _count; i++)
            {
                if (inCapitalList(_unit[i])) write(_unit[i], _running[i], _doublequick[i]);
                _unit[i] = default(TUnit);
            }
            _count = 0;
        }

        public void Clear()
        {
            for (int i = 0; i < _count; i++) _unit[i] = default(TUnit);
            _count = 0;
        }
    }
}
