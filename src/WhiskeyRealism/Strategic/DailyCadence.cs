namespace WhiskeyRealism.Strategic
{
    public sealed class DailyCadence
    {
        private int _lastDay = -1;
        private int _lastMonth = -1;
        private int _lastYear = -1;

        public bool ShouldFire(int day, int month, int year)
        {
            if (day <= 0 || month <= 0 || year <= 0) return false;
            bool first = _lastDay < 0;
            bool rollover = !first && (day != _lastDay || month != _lastMonth || year != _lastYear);
            if (!first && !rollover) return false;
            _lastDay = day;
            _lastMonth = month;
            _lastYear = year;
            return true;
        }
    }
}
