namespace WhiskeyRealism.Strategic
{
    public sealed class WlStartSelectionRetryGate
    {
        private readonly int _maxAttempts;
        private readonly int _retryEveryUnityFrames;
        private int _lastAttemptUnityFrame = -1;
        private int _attempts;

        public WlStartSelectionRetryGate(int maxAttempts, int retryEveryUnityFrames)
        {
            _maxAttempts = maxAttempts;
            _retryEveryUnityFrames = retryEveryUnityFrames;
        }

        public int Attempts => _attempts;
        public bool Exhausted => _attempts >= _maxAttempts;

        public void Reset()
        {
            _attempts = 0;
            _lastAttemptUnityFrame = -1;
        }

        public bool ShouldAttempt(bool pending, bool listVisible, int unityFrame)
        {
            if (!pending)
            {
                Reset();
                return false;
            }

            if (listVisible || _attempts >= _maxAttempts) return false;
            if (_lastAttemptUnityFrame >= 0 && unityFrame - _lastAttemptUnityFrame < _retryEveryUnityFrames) return false;

            _lastAttemptUnityFrame = unityFrame;
            _attempts++;
            return true;
        }
    }
}
