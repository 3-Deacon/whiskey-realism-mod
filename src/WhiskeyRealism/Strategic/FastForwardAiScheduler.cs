namespace WhiskeyRealism.Strategic
{
    public sealed class FastForwardAiOptions
    {
        public bool Enabled = true;
        public int MaxExtraPassesAt20x = 2;
        public int MaxExtraPassesAt50x = 4;
        public float FrameBudgetMs = 1.5f;
        public float SlowFrameThresholdMs = 8f;
        public int SlowFrameCooldownFrames = 180;
    }

    public sealed class CampaignAiGovernorOptions
    {
        public bool Enabled;
        // Passes per frame that run regardless of the frame budget while work is owed.
        public int MaxPassesAt20x = 2;
        public int MaxPassesAt50x = 3;
        public float FrameBudgetMs = 3f;
        // Owed vanilla work may lag by at most this many frames' worth of passes.
        public int MaxBacklogFrames = 8;
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

        public static bool ShouldThrottleAfterFrame(float vanillaElapsedMs, float extraElapsedMs, FastForwardAiOptions options)
        {
            options = options ?? new FastForwardAiOptions();
            float threshold = options.SlowFrameThresholdMs;
            if (float.IsNaN(threshold) || float.IsInfinity(threshold) || threshold <= 0f)
                threshold = 8f;
            return vanillaElapsedMs >= threshold || extraElapsedMs >= threshold;
        }

        public static int CooldownUntilFrame(int currentFrame, FastForwardAiOptions options)
        {
            options = options ?? new FastForwardAiOptions();
            int cooldown = options.SlowFrameCooldownFrames;
            if (cooldown < 0) cooldown = 0;
            return currentFrame + cooldown;
        }

        public static bool InCooldown(int currentFrame, int cooldownUntilFrame)
        {
            return currentFrame < cooldownUntilFrame;
        }

        // Passes per frame the governor runs regardless of the frame budget.
        public static int GuaranteedPasses(float gameSpeed, CampaignAiGovernorOptions options)
        {
            int vanilla = VanillaPasses(gameSpeed);
            options = options ?? new CampaignAiGovernorOptions();
            int configured = gameSpeed >= 50f ? options.MaxPassesAt50x : options.MaxPassesAt20x;
            return System.Math.Max(1, System.Math.Min(vanilla, configured));
        }

        // Vanilla AICampaign.Update (decompile 11195) owes floor(sqrt(gamespeed)) passes
        // every frame; the governor accrues exactly that, so over time it runs vanilla's
        // job progression (per frame == per game day at any frame rate).
        public static float AccrueOwedPasses(float owedPasses, float gameSpeed)
        {
            if (owedPasses < 0f || float.IsNaN(owedPasses)) owedPasses = 0f;
            return owedPasses + VanillaPasses(gameSpeed);
        }

        // owedPasses = passes still owed before this one. Smooths cost spikes across
        // frames but never starves vanilla's job table:
        //  - guaranteed passes run regardless of budget;
        //  - above MaxBacklogFrames of owed work, passes are forced (bounded lag);
        //  - otherwise run under the frame budget, up to 2x vanilla to repay backlog.
        public static bool ShouldRunGovernedPass(
            int completedPasses,
            float elapsedMs,
            float owedPasses,
            float gameSpeed,
            CampaignAiGovernorOptions options)
        {
            if (owedPasses < 1f) return false;
            if (completedPasses < 0) completedPasses = 0;
            int vanilla = VanillaPasses(gameSpeed);
            if (vanilla <= 0) return false;
            options = options ?? new CampaignAiGovernorOptions();
            if (!options.Enabled) return completedPasses < vanilla;
            if (completedPasses < GuaranteedPasses(gameSpeed, options)) return true;
            if (owedPasses > vanilla * System.Math.Max(1, options.MaxBacklogFrames)) return true;
            if (elapsedMs >= options.FrameBudgetMs) return false;
            return completedPasses < vanilla * 2;
        }

        public static bool ShouldSkipCampaignAiUpdate(bool gamePaused, float gameSpeed)
        {
            return gamePaused || gameSpeed <= 0f;
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
