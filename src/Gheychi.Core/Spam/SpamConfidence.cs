namespace Gheychi.Core.Spam;

public enum SpamConfidenceLevel
{
    Likely,
    VeryLikely
}

public static class SpamConfidence
{
    public const float VeryLikelyFrom = 0.95f;

    public static SpamConfidenceLevel Level(float probability) =>
        probability >= VeryLikelyFrom ? SpamConfidenceLevel.VeryLikely : SpamConfidenceLevel.Likely;

    /// <summary>Whole percent, never shown as 100 because the model is never certain.</summary>
    public static int Percent(float probability) =>
        Math.Clamp((int)MathF.Floor(probability * 100f), 0, 99);
}
