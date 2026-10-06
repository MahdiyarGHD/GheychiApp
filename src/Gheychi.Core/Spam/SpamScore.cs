namespace Gheychi.Core.Spam;

/// <summary>How likely a text is spam (0..1), and which model version said so.</summary>
public readonly record struct SpamScore(float Probability, int ModelVersion);
