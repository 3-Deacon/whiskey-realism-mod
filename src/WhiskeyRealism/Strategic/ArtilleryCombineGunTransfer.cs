using System;

namespace WhiskeyRealism.Strategic
{
    internal static class ArtilleryCombineGunTransfer
    {
        internal static int CalculateGunsToTransfer(
            bool isArtillery,
            int sourceGuns,
            int sourceTotalMen,
            int transferredMen)
        {
            if (!isArtillery || sourceGuns <= 0 || sourceTotalMen <= 0 || transferredMen <= 0)
                return 0;

            float ratio = Math.Min(1f, Math.Max(0f, (float)transferredMen / sourceTotalMen));
            int transfer = (int)Math.Ceiling(sourceGuns * ratio);
            if (transfer < 1) transfer = 1;
            if (transfer > sourceGuns) transfer = sourceGuns;
            return transfer;
        }
    }
}
