namespace WhiskeyRealism.Strategic
{
    // Lookup policy for WlCareerStartSelectionRetryPatch, which runs on every
    // AICampaign/CampaignController frame: no scene lookup unless a W&L command
    // selection is pending, none while a live panel is cached, and otherwise at most
    // one GameObject.Find per LookupEveryUnityFrames.
    public static class WlStartSelectionPanelLookup
    {
        public const int LookupEveryUnityFrames = 15;

        public static bool ShouldFindPanel(bool pending, bool cachedPanelAlive, int unityFrame, int nextFindFrame)
        {
            return pending && !cachedPanelAlive && unityFrame >= nextFindFrame;
        }

        public static int NextFindFrame(int unityFrame)
        {
            return unityFrame + LookupEveryUnityFrames;
        }
    }
}
