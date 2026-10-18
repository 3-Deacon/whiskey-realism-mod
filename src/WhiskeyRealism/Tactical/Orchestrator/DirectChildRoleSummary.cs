using System.Collections.Generic;
using System.Text;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// What the TacticalDirectChildIntent telemetry reports for the allocator's roles on this side of the field. The allocator's
    /// <see cref="DirectChildRole"/> is a structural role: the child that carries the main sector, the children beside it, the children that fix, and the
    /// plays, the reserve gate, the exploit producer and the cascade read it as that. On a Defender nothing advances, so the same child is reported as
    /// what it is on the field, a Defender (<see cref="CommandNodeOperationsRuntime.MapRole"/>, the label the command nodes carry). Behaviour reads the
    /// structural role; the trace reads this.
    /// </summary>
    internal static class DirectChildRoleSummary
    {
        internal readonly struct Counts
        {
            public Counts(int main, int supportMain, int fix, int screen, int refuseLeft, int refuseRight, int reserve, int fallback, int unknown, int defender)
            {
                Main = main;
                SupportMain = supportMain;
                Fix = fix;
                Screen = screen;
                RefuseLeft = refuseLeft;
                RefuseRight = refuseRight;
                Reserve = reserve;
                Fallback = fallback;
                Unknown = unknown;
                Defender = defender;
            }

            public int Main { get; }
            public int SupportMain { get; }
            public int Fix { get; }
            public int Screen { get; }
            public int RefuseLeft { get; }
            public int RefuseRight { get; }
            public int Reserve { get; }
            public int Fallback { get; }
            public int Unknown { get; }

            /// <summary>The children a Defender holds the line with (the allocator's Main, SupportMain, Fix and Refuse roles on a Defender); 0 on every other side.</summary>
            public int Defender { get; }

            public string Summary => "main=" + Main + " sup=" + SupportMain + " fix=" + Fix + " scrn=" + Screen + " rL=" + RefuseLeft + " rR=" + RefuseRight
                + " rsrv=" + Reserve + " fbck=" + Fallback + " unk=" + Unknown + " def=" + Defender;
        }

        /// <summary>The label of an allocated role: the command node role it becomes on this side (Defender for a Defender's line roles).</summary>
        public static string Label(DirectChildRole role, BattleStance stance) => CommandNodeOperationsRuntime.MapRole(role, stance).ToString();

        public static Counts Count(IReadOnlyList<DirectChildIntent> intents, BattleStance stance)
        {
            int main = 0, support = 0, fix = 0, screen = 0, left = 0, right = 0, reserve = 0, fallback = 0, unknown = 0, defender = 0;
            for (int i = 0; intents != null && i < intents.Count; i++)
            {
                DirectChildRole role = intents[i].Role;
                if (CommandNodeOperationsRuntime.MapRole(role, stance) == CommandNodeRole.Defender)
                {
                    defender++;
                    continue;
                }
                switch (role)
                {
                    case DirectChildRole.Main: main++; break;
                    case DirectChildRole.SupportMain: support++; break;
                    case DirectChildRole.Fix: fix++; break;
                    case DirectChildRole.Screen: screen++; break;
                    case DirectChildRole.RefuseLeft: left++; break;
                    case DirectChildRole.RefuseRight: right++; break;
                    case DirectChildRole.Reserve: reserve++; break;
                    case DirectChildRole.Fallback: fallback++; break;
                    default: unknown++; break;
                }
            }
            return new Counts(main, support, fix, screen, left, right, reserve, fallback, unknown, defender);
        }

        /// <summary>One entry per child, <c>childId=Label:sector</c>, in the allocation's order.</summary>
        public static string Detail(IReadOnlyList<DirectChildIntent> intents, BattleStance stance)
        {
            var sb = new StringBuilder();
            for (int i = 0; intents != null && i < intents.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(intents[i].ChildId ?? "?").Append('=').Append(Label(intents[i].Role, stance)).Append(':').Append(intents[i].PrimarySector);
            }
            return sb.ToString();
        }
    }
}
