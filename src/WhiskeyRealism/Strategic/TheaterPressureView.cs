using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class TheaterPressureView
    {
        public Dictionary<Theater, float> OwnStrengthByTheater = new Dictionary<Theater, float>();
        public Dictionary<Theater, float> EnemyStrengthByTheater = new Dictionary<Theater, float>();

        public float NormalizedPressure(Theater theater)
        {
            EnemyStrengthByTheater.TryGetValue(theater, out float enemy);
            OwnStrengthByTheater.TryGetValue(theater, out float own);
            float total = own + enemy;
            return total <= 1f ? 0f : enemy / total;
        }

        public static TheaterPressureView From(FrontSectorLedger ledger)
        {
            var view = new TheaterPressureView();
            if (ledger == null) return view;
            foreach (var sector in ledger.Sectors)
            {
                if (sector == null) continue;
                Accumulate(view.OwnStrengthByTheater, sector.Theater, sector.OwnStrength);
                Accumulate(view.EnemyStrengthByTheater, sector.Theater, sector.EnemyStrength);
            }
            return view;
        }

        private static void Accumulate(Dictionary<Theater, float> bucket, Theater theater, float value)
        {
            bucket.TryGetValue(theater, out float existing);
            bucket[theater] = existing + value;
        }
    }
}
