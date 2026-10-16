using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The ground of scenario 001's objectives (stage G decision 6, plan G3c). Every 001 objective has
    // an empty target list in Config/campaignobjectives.dat, so vanilla's GetAvailableObjectives
    // (178827-178866: at least one Town or IIP target) never offers one and the snapshot has no
    // candidate. Vanilla names the ground of the ones that have any in CampaignObjective
    // .CheckIfAccomplished (179017-179515), and that is this table's source: the towns each test
    // reads (Town.GetTownFromName, 144004), with the name and state vanilla passes it.
    //
    //   id  alliance  towns                                    vanilla's accomplishment test
    //    2  0         Montgomery                               Owner == 0                   (179059-179076)
    //    3  0         Richmond                                 Owner == 0                   (179078-179095)
    //    4  1         Washington, else Washington D.C.         Owner == 1                   (179097-179118)
    //   14  0         Wheeling, Charleston, Grafton (Virginia) all Owner == 0, not in E     (179236-179258)
    //   15  1         Louisville (Kentucky)                    Owner == 1, no Union troops (179259-179273)
    //   16  1         St.Louis (Missouri)                      Owner == 1, no Union troops (179274-179288)
    //   17  0         Vicksburg, New Orleans, Baton Rouge,     all Owner == 0, not in E     (179289-179313)
    //                 Memphis
    //
    // Every other 001 objective has no ground (morale, the Militia Act, counters, battles, support,
    // the European wars, casualties) and maps to nothing; it stays vanilla's. The mapping never decides
    // when an objective is live: the chapter, the date window and the objective-specific gates stay
    // vanilla's own IsDeactivated (178905-178950), which the snapshot applies before it asks this table.
    // Pure: the runtime resolves the towns and counts their owners.
    internal static class ObjectiveGround
    {
        // One town of an objective's ground. Names are tried in order (vanilla's "Washington", then
        // "Washington D.C."); State "" matches any state, as Town.GetTownFromName's default.
        internal readonly struct GroundTown
        {
            internal readonly string State;
            internal readonly string[] Names;

            internal GroundTown(string state, params string[] names)
            {
                State = state;
                Names = names;
            }
        }

        // Scenario "001" only; 002's abstract ids (3, 4 and others list 002 with empty targets too)
        // stay unmapped (decision 6).
        private const string MappedScenario = "001";
        // Vanilla's IsDeactivated turns objectives 14 (178961) and 17 (178974) off in sub-scenario E,
        // and the accomplishment test of 14 skips it (179236).
        private const string WithoutTheWestAndTheCut = "E";

        private static readonly GroundTown[] None = new GroundTown[0];
        private static readonly GroundTown[] Montgomery = { new GroundTown("", "Montgomery") };
        private static readonly GroundTown[] Richmond = { new GroundTown("", "Richmond") };
        private static readonly GroundTown[] Washington = { new GroundTown("", "Washington", "Washington D.C.") };
        private static readonly GroundTown[] WesternVirginia =
        {
            new GroundTown("Virginia", "Wheeling"), new GroundTown("Virginia", "Charleston"), new GroundTown("Virginia", "Grafton")
        };
        private static readonly GroundTown[] Kentucky = { new GroundTown("Kentucky", "Louisville") };
        private static readonly GroundTown[] Missouri = { new GroundTown("Missouri", "St.Louis") };
        private static readonly GroundTown[] CutConfederacy =
        {
            new GroundTown("", "Vicksburg"), new GroundTown("", "New Orleans"), new GroundTown("", "Baton Rouge"), new GroundTown("", "Memphis")
        };

        // The towns the objective's accomplishment test reads; empty for any other scenario, any
        // ground-less objective, and 14 and 17 in sub-scenario E. Allocation-free.
        internal static IReadOnlyList<GroundTown> TownsFor(string scenario, string subLevel, int objectiveId)
        {
            if (scenario != MappedScenario) return None;
            switch (objectiveId)
            {
                case 2: return Montgomery;
                case 3: return Richmond;
                case 4: return Washington;
                case 14: return subLevel == WithoutTheWestAndTheCut ? None : WesternVirginia;
                case 15: return Kentucky;
                case 16: return Missouri;
                case 17: return subLevel == WithoutTheWestAndTheCut ? None : CutConfederacy;
                default: return None;
            }
        }

        // Vanilla's GetAvailableObjectives rule (178838-178864) with the mapped towns standing in for
        // the empty target list: the objective is the asking alliance's, not deactivated, not
        // accomplished, and at least one mapped town is owned by an alliance at war with the asking
        // alliance (GameVars.alliance[town.Owner].IsAtWarWith(alliance), 178848; the caller counts).
        internal static bool Available(int alliance, int objectiveAlliance, bool deactivated, bool accomplished, int mappedTownsHeldByEnemy)
        {
            return objectiveAlliance == alliance && !deactivated && !accomplished && mappedTownsHeldByEnemy >= 1;
        }
    }
}
