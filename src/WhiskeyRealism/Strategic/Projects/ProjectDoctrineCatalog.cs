using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Projects
{
    public enum ProjectDoctrineBucket
    {
        None = 0,
        ArmsImport = 1,
        DomesticWeapons = 2,
        NavalBlockade = 3,
        LogisticsRail = 4,
        FinanceCreditAdmin = 5,
        AgricultureIndustry = 6,
        DiplomacyTradeRecognition = 7,
        ManpowerTrainingCivilOrder = 8
    }

    public enum ProjectUiSide
    {
        Military = 0,
        Civil = 1
    }

    public enum ProjectBugReviewState
    {
        None = 0,
        FullyBrokenUntilReviewed = 1,
        PartiallyBrokenUntilReviewed = 2
    }

    public sealed class ProjectDoctrineEntry
    {
        public ProjectDoctrineEntry(
            int projectId,
            string shortName,
            ProjectDoctrineBucket bucket,
            ProjectUiSide uiSide,
            int subsidyLane,
            ProjectBugReviewState bugReviewState)
        {
            ProjectId = projectId;
            ShortName = shortName;
            Bucket = bucket;
            UiSide = uiSide;
            SubsidyLane = subsidyLane;
            BugReviewState = bugReviewState;
        }

        public int ProjectId { get; }
        public string ShortName { get; }
        public ProjectDoctrineBucket Bucket { get; }
        public ProjectUiSide UiSide { get; }
        public int SubsidyLane { get; }
        public ProjectBugReviewState BugReviewState { get; }
    }

    public static class ProjectDoctrineCatalog
    {
        private static readonly Dictionary<int, ProjectDoctrineEntry> ById = BuildById();

        public static readonly IReadOnlyList<ProjectDoctrineEntry> AllActive =
            new List<ProjectDoctrineEntry>(ById.Values).AsReadOnly();

        public static bool TryGet(int projectId, out ProjectDoctrineEntry entry)
        {
            return ById.TryGetValue(projectId, out entry);
        }

        public static ProjectDoctrineEntry Get(int projectId)
        {
            return ById.TryGetValue(projectId, out var entry) ? entry : null;
        }

        public static bool IsInactiveProjectId(int projectId)
        {
            return (projectId >= 20 && projectId <= 29) || (projectId >= 42 && projectId <= 87);
        }

        private static Dictionary<int, ProjectDoctrineEntry> BuildById()
        {
            var entries = new[]
            {
                Entry(0, "Austrian Rifles", ProjectDoctrineBucket.ArmsImport, ProjectUiSide.Military, 5),
                Entry(1, "British Rifles", ProjectDoctrineBucket.ArmsImport, ProjectUiSide.Military, 5),
                Entry(2, "British Artillery", ProjectDoctrineBucket.ArmsImport, ProjectUiSide.Military, 5),
                Entry(3, "French Weapons", ProjectDoctrineBucket.ArmsImport, ProjectUiSide.Military, 5),
                Entry(4, "Prussian Weapons", ProjectDoctrineBucket.ArmsImport, ProjectUiSide.Military, 5),
                Entry(5, "Hall's Carbines", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 0),
                Entry(6, "Confederate Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(7, "Cast Artillery", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(8, "Rifled Artillery", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(9, "Parrott Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(10, "Machineguns", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(11, "Confederate Guns", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(12, "Rebore Muskets", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(13, "Legacy Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(14, "Cavalry Carbines", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(15, "Medium Range Carbines", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(16, "Sharps Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(17, "Repeating Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(18, "CSA Springfield Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(19, "USA Springfield Rifles", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 4),
                Entry(30, "Ironclad Monitors", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(31, "Ironclad Gunboats", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(32, "Union Rebuilt Ironclads", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(33, "CSA Rebuilt Ironclads", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(34, "CSA Ironclad Gunboats", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(35, "Modern Warships", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(36, "Confederate Gunboats", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(37, "Armored Gunboats", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(38, "British Warships", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 5),
                Entry(39, "French Warships", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 5),
                Entry(40, "Gloire Class", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 5),
                Entry(41, "Warrior Class", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 5),
                Entry(88, "Command Reform", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(89, "Organization Reform W&L", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(90, "Organization Reform Base", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(91, "Propaganda", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Civil, 0),
                Entry(92, "Counter-propaganda", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Civil, 0),
                Entry(93, "Occupation Administration", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Civil, 0),
                Entry(94, "Suppress Population", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Civil, 0),
                Entry(95, "Administration Reform", ProjectDoctrineBucket.FinanceCreditAdmin, ProjectUiSide.Civil, 0),
                Entry(96, "Subsidize Banks", ProjectDoctrineBucket.FinanceCreditAdmin, ProjectUiSide.Civil, 1),
                Entry(97, "Improve Credit Rating", ProjectDoctrineBucket.FinanceCreditAdmin, ProjectUiSide.Civil, 1),
                Entry(98, "Market Reform", ProjectDoctrineBucket.FinanceCreditAdmin, ProjectUiSide.Civil, 1, ProjectBugReviewState.FullyBrokenUntilReviewed),
                Entry(99, "Infrastructure Reform", ProjectDoctrineBucket.LogisticsRail, ProjectUiSide.Civil, 3),
                Entry(100, "Logistics Reforms", ProjectDoctrineBucket.LogisticsRail, ProjectUiSide.Military, 4),
                Entry(101, "Military Railroad", ProjectDoctrineBucket.LogisticsRail, ProjectUiSide.Military, 4),
                Entry(102, "Weapon Production", ProjectDoctrineBucket.DomesticWeapons, ProjectUiSide.Military, 3),
                Entry(103, "Send Envoys", ProjectDoctrineBucket.DiplomacyTradeRecognition, ProjectUiSide.Civil, 5),
                Entry(104, "Subsidize Industry", ProjectDoctrineBucket.AgricultureIndustry, ProjectUiSide.Civil, 3),
                Entry(105, "Subsidize Agriculture", ProjectDoctrineBucket.AgricultureIndustry, ProjectUiSide.Civil, 2),
                Entry(106, "Trade Warfare", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 4),
                Entry(107, "Civil Order", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Civil, 4, ProjectBugReviewState.PartiallyBrokenUntilReviewed),
                Entry(108, "Recruit Agents", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(109, "Recruitment Offices", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(110, "Cavalry Reform", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(111, "Cavalry Reform II", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(112, "Artillery Reform", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(113, "Farm Mechanization", ProjectDoctrineBucket.AgricultureIndustry, ProjectUiSide.Civil, 2),
                Entry(114, "Plantation Mechanization", ProjectDoctrineBucket.AgricultureIndustry, ProjectUiSide.Civil, 2),
                Entry(115, "Supply Reform", ProjectDoctrineBucket.LogisticsRail, ProjectUiSide.Military, 4),
                Entry(116, "Cotton is King", ProjectDoctrineBucket.DiplomacyTradeRecognition, ProjectUiSide.Civil, 2),
                Entry(117, "Corn is King", ProjectDoctrineBucket.DiplomacyTradeRecognition, ProjectUiSide.Civil, 2),
                Entry(118, "Training Manuals", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(119, "Railroad Construction", ProjectDoctrineBucket.LogisticsRail, ProjectUiSide.Civil, 0),
                Entry(120, "Improvised Shipyards", ProjectDoctrineBucket.NavalBlockade, ProjectUiSide.Military, 2),
                Entry(121, "Trade Deals", ProjectDoctrineBucket.DiplomacyTradeRecognition, ProjectUiSide.Civil, 5),
                Entry(122, "Military Education", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(123, "Horse Artillery", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4),
                Entry(124, "6-gun Batteries", ProjectDoctrineBucket.ManpowerTrainingCivilOrder, ProjectUiSide.Military, 4)
            };

            var byId = new Dictionary<int, ProjectDoctrineEntry>();
            foreach (var entry in entries)
                byId[entry.ProjectId] = entry;

            return byId;
        }

        private static ProjectDoctrineEntry Entry(
            int id,
            string shortName,
            ProjectDoctrineBucket bucket,
            ProjectUiSide side,
            int lane,
            ProjectBugReviewState bugState = ProjectBugReviewState.None)
        {
            return new ProjectDoctrineEntry(id, shortName, bucket, side, lane, bugState);
        }
    }
}
