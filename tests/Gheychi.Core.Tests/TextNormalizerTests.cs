using System.Text.Json;
using Gheychi.Core.Spam;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class TextNormalizerTests
{
    // A copy of Normalizer/vectors.json in the sms-spam-dataset repository, where the model is trained.
    public static TheoryData<string, string> Vectors()
    {
        var data = new TheoryData<string, string>();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TextNormalizerVectors.json")));
        foreach (var vector in document.RootElement.EnumerateArray())
            data.Add(vector.GetProperty("input").GetString()!, vector.GetProperty("expected").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Normalize_MatchesTrainingVector(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.Normalize(input));
}
