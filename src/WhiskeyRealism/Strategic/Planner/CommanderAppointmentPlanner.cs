using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The historical succession prior for one commander on one command role (SuccessionScheduler
    // .PriorFor): SuccessionPriorBonus while a fired, not yet applied event names him for that
    // role, else 0; eventId is that event (-1 none), so the executor can mark it applied.
    public delegate float SuccessionPrior(int commanderId, string role, out int eventId);

    public struct AppointmentIntent
    {
        public int CommanderId;
        public int TargetInstanceId;   // Regiment or ArmyGroup instance id
        public bool TargetIsArmyGroup;
        public int DisplacedId;        // the sitting commander
        public int ReseatGroupInstanceId; // the army group the appointee leaves, reseated first; -1 none
        public int SuccessionEventId;  // -1 unless the succession prior named the appointee
        public string Reason;          // "competence" | "succession:<id>"
    }

    // Competence plus the historical succession prior (spec component 7, C16, C19; plan P13, P16).
    // Vanilla's job 24 (CheckAICommanderReplacements 17008-17032) already swaps the best idle
    // commander for the worst sitting one rank by rank; the planner adds where: the best available
    // commanders go to the main effort's commands.
    //
    // Targets: the main-effort sector's army groups (unittyp 18), armies (16) and corps (15), in
    // that order, then by strength: never one in battle, never one whose subtree holds the W&L
    // player's command (the formations stage F classes PlayerCommand / PlayerMapGroup /
    // CarryingGroup or CicSide, the snapshot's PlayerCommandInstanceId, and every ancestor of
    // those by ParentInstanceId), never one whose sitting commander is the player, serves under
    // him (IsServingUnderPlayer 60695) or is protected. Candidates: vanilla's AI pool, plus the
    // succession reseat path (ReseatPath: a named historical figure on another army group); the pool
    // (Pickable = CanBePickedForAIReplacement(alliance, true) 60748-60779, Available, not navy,
    // not a generated officer, !refreshcommand), rank within the target's band (CheckSuitableRank
    // 60577-60590: the maximum for every unit type, the minimum only for army groups, since
    // DoCommanderPromotion -> UpdateRankToFit raises a junior appointee), not protected, and not
    // already commanding a main-effort command of the same or a higher echelon. On the W&L
    // player's own alliance in a career only idle commanders (no current command) qualify:
    // vanilla never runs vertical replacement there (CheckVerticalReplacement returns for
    // playeralliance, 17664; BUG-WL-014, user 2026-10-15: leave as vanilla), and job 24 seats
    // only idle commanders (its best per rank must pass CanBePickedForAIReplacement without a
    // command, 84467-84473), so the planner adds no swap vanilla withholds there.
    //
    // Score = GetAverageAttributeValue (59175) + the succession prior for the target's role, for
    // the candidate and the sitting commander alike (history is a weight: a historical figure in
    // place keeps his bonus). The best candidate displaces the sitting commander when his score
    // is at least MinGain higher; vanilla's live rules (the executor's predicate) may still refuse
    // him, and the next candidate is tried. At most one intent per review (plan P8).
    public static class CommanderAppointmentPlanner
    {
        public const float MinGain = 0.10f;               // vanilla expdifferenceforverticalpromotion (campaignprefs), so job 24 would agree
        public const float SuccessionPriorBonus = 0.25f;
        public const int MaxVanillaChecks = 8;            // live vanilla rule checks per review

        private const int ArmyGroupTyp = 18, ArmyTyp = 16, CorpsTyp = 15;

        private struct Target
        {
            public int InstanceId;
            public bool IsArmyGroup;
            public int UnitTyp;
            public float Strength;
            public int CommanderId;
            public float X, Z;
            public string Role;
        }

        private struct Candidate
        {
            public int Id;
            public float Score;
            public int EventId;
            public int ReseatGroup;
        }

        public static bool TryPlan(TheaterPlan plan, CampaignSnapshot s, SectorModel sectors, SuccessionPrior successionPrior,
                                   AppointmentProtection protection, Func<AppointmentIntent, bool> vanillaAllows, out AppointmentIntent intent)
        {
            intent = default(AppointmentIntent);
            if (plan == null || s == null || sectors == null || vanillaAllows == null) return false;
            if (s.Alliance < 0 || s.Alliance > 1 || plan.MainSectorId < 0) return false;
            protection = protection ?? new AppointmentProtection();

            bool playerSide = IsPlayerSide(s);
            if (playerSide && s.Player == PlayerCommandClass.CommanderInChief) return false;

            var holdsPlayer = PlayerContaining(s);
            var targets = MainEffortTargets(plan, s, sectors, holdsPlayer);
            if (targets.Count == 0) return false;
            AssignRoles(s.Alliance, sectors.TheaterOf(plan.MainSectorId), targets);

            int checks = 0;
            var ranked = new List<Candidate>();
            for (int t = 0; t < targets.Count; t++)
            {
                var target = targets[t];
                int currentIndex = IndexOfCommander(s, target.CommanderId);
                if (currentIndex < 0) continue;
                var current = s.Commanders[currentIndex];
                if (current.IsPlayer || current.ServingUnderPlayer || protection.IsProtected(current.Id, s.DaySerial)) continue;
                float currentScore = current.Average + Prior(successionPrior, current.Id, target.Role, out _);

                ranked.Clear();
                for (int i = 0; i < s.Commanders.Count; i++)
                {
                    var c = s.Commanders[i];
                    float prior = Prior(successionPrior, c.Id, target.Role, out int eventId);
                    if (!Eligible(c, target, s, targets, protection, playerSide, holdsPlayer, prior > 0f, out int reseatGroup)) continue;
                    ranked.Add(new Candidate { Id = c.Id, Score = c.Average + prior, EventId = eventId, ReseatGroup = reseatGroup });
                }
                ranked.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Id.CompareTo(b.Id));

                for (int k = 0; k < ranked.Count; k++)
                {
                    if (ranked[k].Score - currentScore < MinGain) break;
                    if (checks++ >= MaxVanillaChecks) return false;
                    var candidate = new AppointmentIntent
                    {
                        CommanderId = ranked[k].Id,
                        TargetInstanceId = target.InstanceId,
                        TargetIsArmyGroup = target.IsArmyGroup,
                        DisplacedId = current.Id,
                        ReseatGroupInstanceId = ranked[k].ReseatGroup,
                        SuccessionEventId = ranked[k].EventId,
                        Reason = ranked[k].EventId >= 0 ? "succession:" + ranked[k].EventId : "competence"
                    };
                    if (!vanillaAllows(candidate)) continue;
                    intent = candidate;
                    return true;
                }
            }
            return false;
        }

        // The role a command plays for the succession events (SuccessionScheduler.Event
        // .ReplacedRole), as the old #6 resolved it (ResolveRoleTargetGroup on main dbc3a6f): a
        // command inside the Shenandoah Valley box is the Valley army; the main effort's principal
        // eastern command outside it is the alliance's main eastern army (ANV / AoP); the
        // principal western command is the Western army. A Valley event with no Valley command
        // finds no role and defers. GeneralInChief is never a command role (C18).
        public static string RoleOf(int alliance, Theater theater, bool primary, bool inValley)
        {
            if (theater == Theater.East)
            {
                if (inValley) return "Valley";
                return primary ? (alliance == 1 ? "ANV" : "AoP") : null;
            }
            if (theater == Theater.West && primary) return "Western";
            return null;
        }

        // The Shenandoah Valley area box of the retired army-area classifier
        // (ArmyAreaClassifier.FromPosition "ShenandoahValley", deleted in G6): x 950..1225,
        // z -760..-400 on the campaign map.
        public static bool InShenandoahValley(float x, float z)
        {
            return x > 950f && x < 1225f && z > -760f && z < -400f;
        }

        // A W&L career and this alliance holds the player (his record is among its commanders).
        private static bool IsPlayerSide(CampaignSnapshot s)
        {
            if (s.Player == PlayerCommandClass.NotCareer) return false;
            for (int i = 0; i < s.Commanders.Count; i++)
                if (s.Commanders[i].IsPlayer) return true;
            return false;
        }

        // Own formations whose subtree holds the player's command: stage F's player chain classes
        // and the player's own command, plus all their ancestors.
        private static HashSet<int> PlayerContaining(CampaignSnapshot s)
        {
            var result = new HashSet<int>();
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                bool player = f.InstanceId == s.PlayerCommandInstanceId
                    || f.Chain == WlChainClass.PlayerCommand || f.Chain == WlChainClass.PlayerMapGroup
                    || f.Chain == WlChainClass.CarryingGroup || f.Chain == WlChainClass.CicSide;
                if (!player) continue;
                int at = i;
                for (int guard = 0; at >= 0 && guard <= s.Own.Count; guard++)
                {
                    if (!result.Add(s.Own[at].InstanceId)) break;
                    at = s.IndexOfOwn(s.Own[at].ParentInstanceId);
                }
            }
            return result;
        }

        private static List<Target> MainEffortTargets(TheaterPlan plan, CampaignSnapshot s, SectorModel sectors, HashSet<int> holdsPlayer)
        {
            var targets = new List<Target>();
            for (int i = 0; i < s.ArmyGroups.Count; i++)
            {
                var g = s.ArmyGroups[i];
                if (g.CommanderId < 0 || sectors.SectorAt(g.X, g.Z) != plan.MainSectorId) continue;
                if (AnyMemberHoldsPlayer(g.MemberInstanceIds, holdsPlayer)) continue;
                targets.Add(new Target { InstanceId = g.InstanceId, IsArmyGroup = true, UnitTyp = ArmyGroupTyp, Strength = g.Strength, CommanderId = g.CommanderId, X = g.X, Z = g.Z });
            }
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (f.UnitTyp != ArmyTyp && f.UnitTyp != CorpsTyp) continue;
                if (f.CommanderId < 0 || (f.Flags & FormationFlags.InBattle) != 0) continue;
                if (holdsPlayer.Contains(f.InstanceId)) continue;
                if (sectors.SectorOfArea(f.AreaIndex) != plan.MainSectorId) continue;
                targets.Add(new Target { InstanceId = f.InstanceId, UnitTyp = f.UnitTyp, Strength = f.Strength, CommanderId = f.CommanderId, X = f.X, Z = f.Z });
            }
            targets.Sort((a, b) =>
            {
                if (a.UnitTyp != b.UnitTyp) return b.UnitTyp.CompareTo(a.UnitTyp);
                if (a.Strength != b.Strength) return b.Strength.CompareTo(a.Strength);
                return a.InstanceId.CompareTo(b.InstanceId);
            });
            return targets;
        }

        private static bool AnyMemberHoldsPlayer(int[] members, HashSet<int> holdsPlayer)
        {
            if (members == null) return false;
            for (int i = 0; i < members.Length; i++)
                if (holdsPlayer.Contains(members[i])) return true;
            return false;
        }

        // The principal command is the first target (highest echelon, strongest) outside the
        // Valley, else the first target (main's "prefer non-Valley, else the largest eastern").
        private static void AssignRoles(int alliance, Theater theater, List<Target> targets)
        {
            int primary = -1;
            for (int i = 0; i < targets.Count && primary < 0; i++)
                if (!InShenandoahValley(targets[i].X, targets[i].Z)) primary = i;
            if (primary < 0) primary = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                t.Role = RoleOf(alliance, theater, i == primary, i != primary && InShenandoahValley(t.X, t.Z));
                targets[i] = t;
            }
        }

        // reseatGroup: the army group a succession figure leaves (the reseat path), -1 none.
        private static bool Eligible(in CommanderRecord c, in Target target, CampaignSnapshot s, List<Target> targets,
                                     AppointmentProtection protection, bool playerSide, HashSet<int> holdsPlayer,
                                     bool namedBySuccession, out int reseatGroup)
        {
            reseatGroup = -1;
            if (c.Id == target.CommanderId) return false;
            if (!c.Available || c.Navy || c.Dummy || c.RefreshCommand || c.ServingUnderPlayer || c.IsPlayer) return false;
            if (!c.Pickable && !ReseatPath(c, s, targets, playerSide, holdsPlayer, namedBySuccession, out reseatGroup)) return false;
            if (!RankFits(s, c.Rank, target.UnitTyp)) return false;
            if (protection.IsProtected(c.Id, s.DaySerial)) return false;
            if (playerSide && c.CommandInstanceId >= 0) return false;
            if (c.CommandInstanceId >= 0 && c.CommandUnitTyp >= target.UnitTyp && IsTarget(targets, c.CommandInstanceId)) return false;
            return true;
        }

        // The succession reseat (spec component 7; main's C-CmdrReplace ReseatThenAppoint):
        // vanilla seats historical figures on auto-created army groups (CreateNewArmyGroup 75653),
        // and CanBePickedForAIReplacement rejects every sitting army-group commander (60770). A
        // figure a fired, unapplied succession event names may still move when he is free apart
        // from his army group (available, not serving under the player, not staff, not the CIC),
        // his group is not a main-effort command and holds nothing of the player's, and this is
        // not the W&L player's alliance (idle commanders only there, P16). The executor reseats
        // his group from vanilla's idle pool first and refuses when no idle successor exists.
        private static bool ReseatPath(in CommanderRecord c, CampaignSnapshot s, List<Target> targets, bool playerSide,
                                       HashSet<int> holdsPlayer, bool namedBySuccession, out int reseatGroup)
        {
            reseatGroup = -1;
            if (!namedBySuccession || playerSide || !c.FreeApartFromArmyGroup) return false;
            for (int i = 0; i < s.ArmyGroups.Count; i++)
            {
                var g = s.ArmyGroups[i];
                if (g.CommanderId != c.Id) continue;
                if (AnyMemberHoldsPlayer(g.MemberInstanceIds, holdsPlayer)) return false;
                for (int t = 0; t < targets.Count; t++)
                    if (targets[t].IsArmyGroup && targets[t].InstanceId == g.InstanceId) return false;
                reseatGroup = g.InstanceId;
                return true;
            }
            return false;
        }

        private static bool IsTarget(List<Target> targets, int instanceId)
        {
            for (int i = 0; i < targets.Count; i++)
                if (!targets[i].IsArmyGroup && targets[i].InstanceId == instanceId) return true;
            return false;
        }

        private static bool RankFits(CampaignSnapshot s, int rank, int unitTyp)
        {
            for (int i = 0; i < s.RankBands.Count; i++)
            {
                var band = s.RankBands[i];
                if (band.UnitTyp != unitTyp) continue;
                if (rank > band.MaxRank) return false;
                return unitTyp != ArmyGroupTyp || rank >= band.MinRank;
            }
            return false;
        }

        private static int IndexOfCommander(CampaignSnapshot s, int id)
        {
            if (id < 0) return -1;
            for (int i = 0; i < s.Commanders.Count; i++)
                if (s.Commanders[i].Id == id) return i;
            return -1;
        }

        private static float Prior(SuccessionPrior prior, int commanderId, string role, out int eventId)
        {
            eventId = -1;
            if (prior == null || role == null) return 0f;
            float bonus = prior(commanderId, role, out eventId);
            if (bonus <= 0f) eventId = -1;
            return Math.Max(0f, bonus);
        }
    }
}
