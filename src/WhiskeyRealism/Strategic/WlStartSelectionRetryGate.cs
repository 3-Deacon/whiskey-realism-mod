namespace WhiskeyRealism.Strategic
{
    public sealed class WlStartSelectionRetryGate
    {
        private readonly int _maxAttempts;
        private readonly int _retryEveryUnityFrames;
        private readonly int _minReadyCampaignFrame;
        private int _lastAttemptUnityFrame = -1;
        private int _attempts;

        public WlStartSelectionRetryGate(int maxAttempts, int retryEveryUnityFrames, int minReadyCampaignFrame = 0)
        {
            _maxAttempts = maxAttempts;
            _retryEveryUnityFrames = retryEveryUnityFrames;
            _minReadyCampaignFrame = minReadyCampaignFrame;
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
            return ShouldAttempt(pending: pending, listVisible: listVisible, panelAvailable: true, unityFrame: unityFrame);
        }

        public bool ShouldAttempt(bool pending, bool listVisible, bool panelAvailable, int unityFrame)
        {
            return ShouldAttempt(pending: pending, listVisible: listVisible, panelAvailable: panelAvailable, campaignFrame: _minReadyCampaignFrame, unityFrame: unityFrame);
        }

        public bool ShouldAttempt(bool pending, bool listVisible, bool panelAvailable, int campaignFrame, int unityFrame)
        {
            return ShouldAttempt(pending, listVisible, panelAvailable, campaignFrame, startupDataReady: false, unityFrame);
        }

        public bool ShouldAttempt(bool pending, bool listVisible, bool panelAvailable, int campaignFrame, bool startupDataReady, int unityFrame)
        {
            if (!pending)
            {
                Reset();
                return false;
            }

            if (!panelAvailable) return false;
            if (campaignFrame < _minReadyCampaignFrame) return false;
            if (listVisible || _attempts >= _maxAttempts) return false;
            if (_lastAttemptUnityFrame >= 0 && unityFrame - _lastAttemptUnityFrame < _retryEveryUnityFrames) return false;

            _lastAttemptUnityFrame = unityFrame;
            _attempts++;
            return true;
        }
    }
}
