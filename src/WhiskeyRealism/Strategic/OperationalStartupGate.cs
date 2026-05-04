namespace WhiskeyRealism.Strategic
{
    public sealed class OperationalStartupGate
    {
        private bool _runtimeReadyNotified;

        public bool ShouldNotify(bool dateChanged, bool runtimeReady)
        {
            if (dateChanged) return true;
            if (!runtimeReady || _runtimeReadyNotified) return false;

            _runtimeReadyNotified = true;
            return true;
        }
    }
}
