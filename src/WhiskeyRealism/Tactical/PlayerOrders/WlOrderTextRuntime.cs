using System;
using UnityEngine;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    // The game-facing half of the W&L order text (plan 2026-10-15-wl-order-flow.md D10, D19, D20; N4):
    // finds the nearest vanilla objective, the player's position and facing, and the neighbouring
    // formations, and hands them to the pure builder (WlOrderText).
    //
    // Vanilla anchors (decompile, build 14703111): [VAR47] is the echelon type name of the unit the order
    // is addressed to (GamePrefs.unittypename[overridesymbol >= 0 ? overridesymbol : unittyp], 223167),
    // [VAR49] the raw destination name (223183); a subordinate dispatch text opens "My [VAR47]" (tooltips
    // 288, 294, ...). The battle day starts at GamePrefs.daystarthours[month - 1] (19142, 26200), read
    // against BattleUnits.uniStormSystem.hourCounter.
    //
    // Reads own units from the side's battle snapshot and vanilla's objectives; nothing here reads an
    // enemy field (fog-honest). Writes only the content string of the order vanilla just accepted.
    internal static class WlOrderTextRuntime
    {
        private const float NeighbourMeters = 1000f;

        private static bool TextOn()
        {
            return Plugin.Instance != null && Plugin.Instance.Enabled != null && Plugin.Instance.Enabled.Value
                && Plugin.EnableWlOrderText != null && Plugin.EnableWlOrderText.Value;
        }

        // The place an order at (x, z) names. With the text switch off, vanilla names only: the nearest
        // objective, else the plain word "Objective".
        internal static string DestinationFor(float x, float z, string slotClause)
        {
            try
            {
                NearestObjective(new Vector3(x, 0f, z), out string name, out float meters);
                if (!TextOn()) return !string.IsNullOrEmpty(name) && meters <= WlOrderText.ObjectiveNameMeters ? name : "Objective";
                float px = 0f, pz = 0f, facing = 0f;
                if (BattleCore.TryGetSnapshot(GameVars.playeralliance, out BattleSnapshot snapshot) && snapshot != null
                    && snapshot.PlayerCommandIndex >= 0 && snapshot.PlayerCommandIndex < snapshot.OwnCount)
                {
                    UnitRecord player = snapshot.Own[snapshot.PlayerCommandIndex];
                    px = player.X;
                    pz = player.Z;
                    facing = player.RotationY;
                }
                return WlOrderText.Destination(new DestinationInput(name, meters, px, pz, facing, x, z, slotClause));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-order-text:destination", "[W&LOrderText] destination failed: " + ex.GetType().Name + " " + ex.Message);
                return "the stated position";
            }
        }

        // The vanilla objective nearest the point (XZ), with its name and distance; NaN when there is none.
        internal static Objectives NearestObjective(Vector3 point, out string name, out float meters)
        {
            name = null;
            meters = float.NaN;
            Objectives[] all = BattleUnits.objectives;
            if (all == null) return null;
            Objectives nearest = null;
            float best = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                if ((UnityEngine.Object)all[i] == null) continue;
                Vector3 p = all[i].transform.position;
                float dx = p.x - point.x;
                float dz = p.z - point.z;
                float d2 = dx * dx + dz * dz;
                if (d2 >= best) continue;
                best = d2;
                nearest = all[i];
            }
            if (nearest == null) return null;
            name = nearest.objectivename;
            meters = (float)Math.Sqrt(best);
            return nearest;
        }

        // Vanilla showed the order (WlOrderFlow.Accepted): the real formation name in vanilla's own text
        // (N4, whatever the origin), Whiskey's condition lines under it for Whiskey's orders, then the panel
        // is shown again with the final text in the same frame (D20; S9 confirms one pop).
        internal static void OnAccepted(in CourierEntry e)
        {
            try
            {
                if (!TextOn()) return;
                DLC_WL.GivenOrders order = DLC_WL.givenorder;
                if (order == null || order.type != e.Type || (UnityEngine.Object)order.groupunit == null) return;

                Regiment unit = order.groupunit;
                int symbol = unit.overridesymbol >= 0 ? unit.overridesymbol : unit.unittyp;
                string[] names = GamePrefs.unittypename;
                string echelon = names != null && symbol >= 0 && symbol < names.Length ? names[symbol] : null;
                string content = WlOrderText.ReplaceMyEchelon(order.content, echelon, WlOrderText.FormationName(unit.name));

                string lines = WlOrderText.ContentLines(ContentFor(in e));
                if (lines.Length > 0) content = string.IsNullOrEmpty(content) ? lines : content + "\n\n" + lines;
                if (string.Equals(content, order.content, StringComparison.Ordinal)) return;

                order.content = content;
                CareerInformationPanel panel = Panel();
                if ((UnityEngine.Object)panel != null) panel.ShowNewOrder(order);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-order-text:accepted", "[W&LOrderText] composing the order text failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static CareerInformationPanel _panel;

        private static CareerInformationPanel Panel()
        {
            if ((UnityEngine.Object)_panel != null) return _panel;
            _panel = DLC_WL.careerinformationpanelref;
            if ((UnityEngine.Object)_panel != null) return _panel;
            var ui = UnityEngine.GameObject.Find("UI");
            CareerInformationPanel[] panels = (UnityEngine.Object)ui != null ? ui.GetComponentsInChildren<CareerInformationPanel>(true) : null;
            _panel = panels != null && panels.Length > 0 ? panels[0] : null;
            return _panel;
        }

        private static ContentInput ContentFor(in CourierEntry e)
        {
            if (e.Kind == OrderIntentKind.None) return new ContentInput(OrderIntentKind.None, null, false, null, null);
            Neighbours(out string left, out string right);
            return new ContentInput(e.Kind, null, BeforeFirstLight(), left, right);
        }

        // The battle hour is below the month's day start (GamePrefs.daystarthours, 19142).
        private static bool BeforeFirstLight()
        {
            try
            {
                var bunits = UnityEngine.GameObject.Find("GameController")?.GetComponent<BattleUnits>();
                if (bunits == null || bunits.uniStormSystem == null) return false;
                int month = bunits.uniStormSystem.monthCounter - 1;
                int[] starts = GamePrefs.daystarthours;
                return starts != null && month >= 0 && month < starts.Length && bunits.uniStormSystem.hourCounter < starts[month];
            }
            catch
            {
                return false;
            }
        }

        // The nearest own formation of the player's echelon on each side of his command, within 1,000 m
        // laterally and longitudinally, along the side's front axis (his facing when there is none).
        private static void Neighbours(out string left, out string right)
        {
            left = null;
            right = null;
            if (!BattleCore.TryGetSnapshot(GameVars.playeralliance, out BattleSnapshot snapshot) || snapshot == null) return;
            int p = snapshot.PlayerCommandIndex;
            if (p < 0 || p >= snapshot.OwnCount) return;
            UnitRecord player = snapshot.Own[p];

            float fx, fz;
            if (snapshot.FrontAxis.IsValid) { fx = snapshot.FrontAxis.ForwardX; fz = snapshot.FrontAxis.ForwardZ; }
            else
            {
                double yaw = player.RotationY * Math.PI / 180.0;
                fx = (float)Math.Sin(yaw);
                fz = (float)Math.Cos(yaw);
            }
            float rx = fz;   // right of forward, as TacticalFrontAxis.RightX/RightZ
            float rz = -fx;

            int leftIndex = -1, rightIndex = -1;
            float leftLateral = float.MaxValue, rightLateral = float.MaxValue;
            for (int i = 0; i < snapshot.OwnCount; i++)
            {
                if (i == p) continue;
                UnitRecord u = snapshot.Own[i];
                if ((u.Flags & UnitFlags.PlayerOwned) != 0 || (u.Flags & UnitFlags.Alive) == 0 || u.UnitTyp != player.UnitTyp) continue;
                float dx = u.X - player.X;
                float dz = u.Z - player.Z;
                float lateral = dx * rx + dz * rz;
                float along = dx * fx + dz * fz;
                if (Math.Abs(lateral) > NeighbourMeters || Math.Abs(along) > NeighbourMeters) continue;
                if (lateral < 0f && -lateral < leftLateral) { leftLateral = -lateral; leftIndex = i; }
                else if (lateral > 0f && lateral < rightLateral) { rightLateral = lateral; rightIndex = i; }
            }
            left = NameAt(snapshot, leftIndex);
            right = NameAt(snapshot, rightIndex);
        }

        private static string NameAt(BattleSnapshot snapshot, int index)
        {
            if (index < 0) return null;
            Regiment unit = BattleSnapshotBuilder.UnitAt(snapshot, index, enemy: false);
            return (UnityEngine.Object)unit != null ? WlOrderText.FormationName(unit.name) : null;
        }
    }
}
