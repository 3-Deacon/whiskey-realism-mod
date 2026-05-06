namespace WhiskeyRealism.Strategic
{
    internal readonly struct WlDispatchSanitizerResult
    {
        internal WlDispatchSanitizerResult(string content, bool changed)
        {
            Content = content;
            Changed = changed;
        }

        internal string Content { get; }
        internal bool Changed { get; }
    }

    internal static class WlDispatchSanitizer
    {
        private const string StanceNone = "I will carry on according to your instructions that are to none.";
        private const string StanceNoneReplacement = "I will hold position and await further instructions.";
        private const string NoOrdersNone = "I will none if no other orders are received";
        private const string NoOrdersNoneReplacement = "I will hold position if no other orders are received";

        internal static WlDispatchSanitizerResult Sanitize(int messageType, string content)
        {
            if (!IsCandidateType(messageType) || content == null)
                return new WlDispatchSanitizerResult(content, false);

            string sanitized = content
                .Replace(StanceNone, StanceNoneReplacement)
                .Replace(NoOrdersNone, NoOrdersNoneReplacement);

            return new WlDispatchSanitizerResult(sanitized, sanitized != content);
        }

        internal static bool IsCandidateType(int messageType)
        {
            return messageType == 15 || messageType == 56 || messageType == 57;
        }
    }
}
