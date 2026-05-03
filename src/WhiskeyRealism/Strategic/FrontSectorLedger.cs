using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public enum FrontPosture
    {
        Hold,
        Delay,
        EconomyOfForce,
        Concede,
        Counterstroke,
        Exploit
    }

    public enum ArmyRole
    {
        Hold,
        Screen,
        Reserve,
        Exploit,
        Counterstroke,
        Recover
    }

    public enum TransferBudgetAction
    {
        Allowed,
        Blocked,
        Concession
    }

    public sealed class FrontLedgerOptions
    {
        public float MinimumHoldRatio = 0.9f;
        public float CriticalHoldRatioBonus = 0.2f;
        public float ConcessionRatio = 0.55f;
        public float ExploitRatio = 1.25f;
        public float MinimumHealthyMorale = 0.45f;
        public float MinimumHealthySupply = 0.35f;
        public float MinimumHealthyReadiness = 0.35f;
    }

    public sealed class FrontSectorInput
    {
        public string SectorKey;
        public Theater Theater;
        public float OwnStrength;
        public float EnemyStrength;
        public float StrategicImportance;
        public bool IsCritical;
        public bool IsPlanTarget;
        public float CommanderAudacity;
        public float CommanderCaution;
        public float AverageMorale = 1f;
        public float AverageSupply = 1f;
        public float AverageReadiness = 1f;
    }

    public sealed class FrontSector
    {
        public string SectorKey;
        public Theater Theater;
        public float OwnStrength;
        public float EnemyStrength;
        public float StrengthRatio;
        public float MinimumHoldRatio;
        public float StrategicImportance;
        public bool IsCritical;
        public bool IsPlanTarget;
        public FrontPosture Posture;
        public ArmyRole Role;
    }

    public sealed class TransferBudgetDecision
    {
        public bool Allowed;
        public TransferBudgetAction Action;
        public string Reason;
        public FrontSector Source;
        public FrontSector Destination;
    }

    public sealed class FrontSectorLedger
    {
        private readonly Dictionary<string, FrontSector> _sectors = new Dictionary<string, FrontSector>();
        private readonly List<FrontSector> _ordered = new List<FrontSector>();

        public IReadOnlyList<FrontSector> Sectors => _ordered;

        public static FrontSectorLedger Build(IEnumerable<FrontSectorInput> inputs, FrontLedgerOptions options = null)
        {
            options = options ?? new FrontLedgerOptions();
            var ledger = new FrontSectorLedger();
            if (inputs == null) return ledger;

            foreach (var input in inputs)
            {
                if (input == null || string.IsNullOrEmpty(input.SectorKey)) continue;

                float ratio = input.OwnStrength / Math.Max(1f, input.EnemyStrength);
                float minHold = options.MinimumHoldRatio +
                                (input.IsCritical ? options.CriticalHoldRatioBonus * Clamp01(input.StrategicImportance) : 0f);

                var sector = new FrontSector
                {
                    SectorKey = input.SectorKey,
                    Theater = input.Theater,
                    OwnStrength = Math.Max(0f, input.OwnStrength),
                    EnemyStrength = Math.Max(0f, input.EnemyStrength),
                    StrengthRatio = ratio,
                    MinimumHoldRatio = minHold,
                    StrategicImportance = Clamp01(input.StrategicImportance),
                    IsCritical = input.IsCritical,
                    IsPlanTarget = input.IsPlanTarget
                };

                sector.Posture = ChoosePosture(input, sector, options);
                sector.Role = ChooseRole(sector);
                ledger._sectors[sector.SectorKey] = sector;
                ledger._ordered.Add(sector);
            }

            return ledger;
        }

        public FrontSector GetSector(string sectorKey)
        {
            if (sectorKey == null) return null;
            _sectors.TryGetValue(sectorKey, out var sector);
            return sector;
        }

        public TransferBudgetDecision EvaluateTransfer(string sourceSectorKey, string destinationSectorKey, float strengthToMove)
        {
            var source = GetSector(sourceSectorKey);
            var destination = GetSector(destinationSectorKey);
            if (source == null)
                return Decision(false, TransferBudgetAction.Blocked, "missing-source", source, destination);

            if (source.Posture == FrontPosture.Hold)
                return Decision(false, TransferBudgetAction.Blocked, "min-hold", source, destination);

            float projectedOwn = Math.Max(0f, source.OwnStrength - Math.Max(0f, strengthToMove));
            float projectedRatio = projectedOwn / Math.Max(1f, source.EnemyStrength);
            if (source.IsCritical && projectedRatio < source.MinimumHoldRatio)
                return Decision(false, TransferBudgetAction.Blocked, "critical-sector-budget", source, destination);

            if (source.Posture == FrontPosture.EconomyOfForce || source.Posture == FrontPosture.Concede)
                return Decision(true, TransferBudgetAction.Concession, source.Posture.ToString(), source, destination);

            return Decision(true, TransferBudgetAction.Allowed, "within-budget", source, destination);
        }

        private static TransferBudgetDecision Decision(bool allowed, TransferBudgetAction action, string reason, FrontSector source, FrontSector destination)
        {
            return new TransferBudgetDecision
            {
                Allowed = allowed,
                Action = action,
                Reason = reason,
                Source = source,
                Destination = destination
            };
        }

        private static FrontPosture ChoosePosture(FrontSectorInput input, FrontSector sector, FrontLedgerOptions options)
        {
            bool unhealthy = input.AverageMorale < options.MinimumHealthyMorale ||
                             input.AverageSupply < options.MinimumHealthySupply ||
                             input.AverageReadiness < options.MinimumHealthyReadiness;

            if (unhealthy)
                return sector.IsCritical ? FrontPosture.Delay : FrontPosture.Concede;

            if (sector.IsPlanTarget && sector.StrengthRatio >= options.ExploitRatio)
                return FrontPosture.Exploit;

            if (sector.IsCritical && sector.StrengthRatio < sector.MinimumHoldRatio)
                return FrontPosture.Hold;

            if (!sector.IsCritical && sector.StrengthRatio < sector.MinimumHoldRatio)
                return sector.StrategicImportance < 0.25f && sector.StrengthRatio < options.ConcessionRatio
                    ? FrontPosture.Concede
                    : FrontPosture.EconomyOfForce;

            if (sector.StrengthRatio >= options.ExploitRatio && input.CommanderAudacity > input.CommanderCaution)
                return FrontPosture.Counterstroke;

            return FrontPosture.Hold;
        }

        private static ArmyRole ChooseRole(FrontSector sector)
        {
            switch (sector.Posture)
            {
                case FrontPosture.Delay:
                    return ArmyRole.Screen;
                case FrontPosture.EconomyOfForce:
                    return ArmyRole.Reserve;
                case FrontPosture.Concede:
                    return ArmyRole.Screen;
                case FrontPosture.Counterstroke:
                    return ArmyRole.Counterstroke;
                case FrontPosture.Exploit:
                    return ArmyRole.Exploit;
                default:
                    return ArmyRole.Hold;
            }
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
