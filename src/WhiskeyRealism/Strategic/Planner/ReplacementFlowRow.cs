using System;
using System.Globalization;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The [ReplacementFlow] telemetry row both replacement-flow patches write (recruit routing and the
    // transfer swap): `[ReplacementFlow] alliance=<a> kind=<recruit|transfer> to=<formation> from=<cell|formation>`,
    // one row per alliance and kind on change of (to, from), so a flow that repeats every vanilla pass
    // logs once. Per campaign (CampaignScope).
    internal static class ReplacementFlowRow
    {
        internal const string KindRecruit = "recruit";
        internal const string KindTransfer = "transfer";

        private static readonly string[] Last = new string[4];

        static ReplacementFlowRow()
        {
            CampaignScope.Register("replacement-flow-row", Reset);
        }

        private static void Reset()
        {
            for (int i = 0; i < Last.Length; i++) Last[i] = null;
        }

        internal static void Emit(int alliance, string kind, string to, string from)
        {
            if (alliance < 0 || alliance > 1) return;
            int slot = alliance * 2 + (string.Equals(kind, KindRecruit, StringComparison.Ordinal) ? 0 : 1);
            string signature = to + "|" + from;
            if (string.Equals(Last[slot], signature, StringComparison.Ordinal)) return;
            Last[slot] = signature;
            TelemetryRouter.LegacyInfo(
                "[ReplacementFlow] alliance=" + alliance.ToString(CultureInfo.InvariantCulture) + " kind=" + kind + " to=" + TelemetryLegacyParser.Quote(to) + " from=" + TelemetryLegacyParser.Quote(from),
                TelemetryLayer.Campaign);
        }

        // A map point as "x,z" for a row.
        internal static string Cell(float x, float z)
        {
            return x.ToString("0", CultureInfo.InvariantCulture) + "," + z.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
