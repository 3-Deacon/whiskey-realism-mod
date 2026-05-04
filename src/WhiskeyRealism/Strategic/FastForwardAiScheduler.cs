namespace WhiskeyRealism.Strategic
{
    public sealed class FastForwardAiOptions
    {
        public bool Enabled = true;
        public int MaxExtraPassesAt20x = 2;
        public int MaxExtraPassesAt50x = 4;
        public float FrameBudgetMs = 1.5f;
    }

    public static class FastForwardAiScheduler
    {
        public static int VanillaPasses(float gameSpeed)
        {
            if (gameSpeed <= 0f) return 0;
            return System.Math.Max(1, (int)System.Math.Floor(System.Math.Sqrt(gameSpeed)));
        }

        public static int MaxExtraPasses(float gameSpeed, FastForwardAiOptions options)
        {
            options = options ?? new FastForwardAiOptions();
            if (!options.Enabled) return 0;
            if (gameSpeed >= 50f) return ClampNonNegative(options.MaxExtraPassesAt50x);
            if (gameSpeed >= 20f) return ClampNonNegative(options.MaxExtraPassesAt20x);
            return 0;
        }

        public static bool ShouldRunExtraPass(int completedExtraPasses, float elapsedMs, float gameSpeed, FastForwardAiOptions options)
        {
            options = options ?? new FastForwardAiOptions();
            if (completedExtraPasses < 0) completedExtraPasses = 0;
            if (completedExtraPasses >= MaxExtraPasses(gameSpeed, options)) return false;
            return elapsedMs < options.FrameBudgetMs;
        }

        public static string LogSignature(float gameSpeed, int vanillaPasses, int extraPasses, int maxExtra, bool budgetExhausted)
        {
            return ((int)System.Math.Round(gameSpeed)) + "x:" +
                   vanillaPasses + ":" +
                   maxExtra + ":" +
                   (budgetExhausted ? "budget" : "cap");
        }

        private static int ClampNonNegative(int value)
        {
            return value < 0 ? 0 : value;
        }
    }

    public sealed class FastForwardAiLogGate
    {
        private readonly System.Collections.Generic.HashSet<string> _seen =
            new System.Collections.Generic.HashSet<string>();

        public bool ShouldLog(string signature)
        {
            if (string.IsNullOrEmpty(signature)) return false;
            return _seen.Add(signature);
        }
    }
}
