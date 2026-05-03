namespace WhiskeyRealism.Strategic
{
    public sealed class WeeklyCadence
    {
        private int _lastWeek = -1;
        private int _lastMonth = -1;
        private int _lastYear = -1;

        public bool ShouldFire(int day, int month, int year)
        {
            if (day <= 0 || month <= 0 || year <= 0) return false;

            int week = (day - 1) / 7;
            bool first = _lastWeek < 0;
            bool rollover = !first && (week != _lastWeek || month != _lastMonth || year != _lastYear);
            if (!first && !rollover) return false;

            _lastWeek = week;
            _lastMonth = month;
            _lastYear = year;
            return true;
        }
    }
}
