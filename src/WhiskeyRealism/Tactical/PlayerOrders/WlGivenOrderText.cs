using System;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    // The order on the W&L panel after vanilla accepted it (plan 2026-10-15-wl-order-flow.md D19, D20; N4):
    // the real formation name in vanilla's own text, and the panel shown again with the final text in the
    // same frame. Shared by the battle delivery (WlOrderTextRuntime.OnAccepted) and the campaign delivery
    // (WlCampaignCourierRuntime), so there is one definition of which phrase is replaced.
    //
    // [VAR47] is the echelon type name of the unit the order is addressed to
    // (GamePrefs.unittypename[overridesymbol >= 0 ? overridesymbol : unittyp], decompile 223167).
    internal static class WlGivenOrderText
    {
        private static CareerInformationPanel _panel;

        // The order's content with "My <echelon>" replaced by its group unit's formation name; the same
        // string (by value) when nothing matches.
        internal static string WithFormationName(DLC_WL.GivenOrders order)
        {
            if (order == null || (UnityEngine.Object)order.groupunit == null) return order?.content;
            Regiment unit = order.groupunit;
            int symbol = unit.overridesymbol >= 0 ? unit.overridesymbol : unit.unittyp;
            string[] names = GamePrefs.unittypename;
            string echelon = names != null && symbol >= 0 && symbol < names.Length ? names[symbol] : null;
            return WlOrderText.ReplaceMyEchelon(order.content, echelon, WlOrderText.FormationName(unit.name));
        }

        // Shows the panel for the order again (vanilla's ShowNewOrder with its defaults).
        internal static void Show(DLC_WL.GivenOrders order)
        {
            CareerInformationPanel panel = Panel();
            if ((UnityEngine.Object)panel != null) panel.ShowNewOrder(order);
        }

        private static CareerInformationPanel Panel()
        {
            if ((UnityEngine.Object)_panel != null) return _panel;
            _panel = DLC_WL.careerinformationpanelref;
            if ((UnityEngine.Object)_panel != null) return _panel;
            var ui = GameObject.Find("UI");
            CareerInformationPanel[] panels = (UnityEngine.Object)ui != null ? ui.GetComponentsInChildren<CareerInformationPanel>(true) : null;
            _panel = panels != null && panels.Length > 0 ? panels[0] : null;
            return _panel;
        }
    }
}
