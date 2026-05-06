using System;

namespace WhiskeyRealism.Strategic
{
    public static class EconomyAllianceDataGuard
    {
        public static bool ShouldSuppress(Exception exception)
        {
            return exception is NullReferenceException;
        }

        public static string FormatIteratorState(
            int iipIndex,
            int iipCount,
            int corporationIndex,
            int corporationCount,
            int townIndex,
            int townCount)
        {
            return "iip=" + iipIndex + "/" + iipCount
                + " corp=" + corporationIndex + "/" + corporationCount
                + " town=" + townIndex + "/" + townCount;
        }
    }
}
