using System.Globalization;

namespace WhiskeyRealism.Strategic.Planner
{
    // The one definition of an "area key": the string the key-based ledgers (formation directives,
    // the construction supply corridor, recruitment's protected area) group units and sites by. It is
    // the planner sector the point lies in ("sector-<id>", SectorModel.SectorAt, so the same colour
    // regions the plan and the allocation use), or, while no sector model is published (the planner
    // flag off, a faction before its first review), the point's theater name: coarser, but every
    // producer and consumer of a key goes through here, so they always agree. Sector ids are
    // renumbered when the live objectives change; keys are rebuilt each daily review and never stored.
    internal static class PlannerAreaKeys
    {
        internal const string SectorPrefix = "sector-";

        internal static string OfSector(int sector)
        {
            return SectorPrefix + sector.ToString(CultureInfo.InvariantCulture);
        }

        internal static string At(SectorModel sectors, float x, float z)
        {
            if (sectors != null)
            {
                int sector = sectors.SectorAt(x, z);
                if (sector >= 0) return OfSector(sector);
            }
            return TheaterClassifier.FromPosition(x, z).ToString();
        }

        // The alliance's published (or, inside a review job, working) sector model.
        internal static string At(int alliance, float x, float z)
        {
            CampaignPlanStore.TryGetSectors(alliance, out var sectors);
            return At(sectors, x, z);
        }

        // The key of the alliance's main objective, null with no main effort.
        internal static string OfMainTarget(int alliance)
        {
            if (!CampaignPlanStore.TryGetMainTarget(alliance, out _, out float x, out float z)) return null;
            return At(alliance, x, z);
        }
    }
}
