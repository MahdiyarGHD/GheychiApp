using System.Text.Json;
using Gheychi.Core.Spam;

namespace Gheychi.Infrastructure.Spam;

/// <summary>
/// Describes a model file. Column names live here rather than in code because ML.NET Model Builder names them
/// after the training file's header row, so every retrained model can bring different ones.
/// </summary>
/// <param name="Normalizer">The <see cref="TextNormalizer"/> version the model was trained with; 0 for raw text.</param>
/// <param name="Size">Size of the model file in bytes, when the manifest states it.</param>
/// <param name="Sha256">Lowercase hex SHA-256 of the model file, when the manifest states it.</param>
public sealed record SpamModelManifest(
    int Version,
    string ModelFile,
    string TextColumn,
    string LabelColumn,
    string SpamLabel,
    int Normalizer = 0,
    long? Size = null,
    string? Sha256 = null)
{
    public const string FileName = "manifest.json";

    /// <exception cref="InvalidDataException">The manifest is malformed or incomplete.</exception>
    public static SpamModelManifest Parse(byte[] json)
    {
        SpamModelManifest manifest;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            manifest = new SpamModelManifest(
                root.GetProperty("version").GetInt32(),
                root.GetProperty("model").GetString() ?? string.Empty,
                root.GetProperty("textColumn").GetString() ?? string.Empty,
                root.GetProperty("labelColumn").GetString() ?? string.Empty,
                root.GetProperty("spamLabel").GetString() ?? string.Empty,
                root.TryGetProperty("normalizer", out var normalizer) ? normalizer.GetInt32() : 0,
                root.TryGetProperty("size", out var size) ? size.GetInt64() : null,
                root.TryGetProperty("sha256", out var sha256) ? sha256.GetString() : null);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Malformed spam model manifest.", ex);
        }

        // The model name becomes a path; anything but a bare file name could write outside the model folder.
        if (manifest.Version <= 0
            || manifest.ModelFile.Length == 0
            || manifest.ModelFile != Path.GetFileName(manifest.ModelFile)
            || manifest.ModelFile == FileName
            || manifest.TextColumn.Length == 0
            || manifest.LabelColumn.Length == 0
            || manifest.SpamLabel.Length == 0
            || manifest.Normalizer < 0
            || manifest.Size <= 0)
            throw new InvalidDataException("Incomplete spam model manifest.");

        // A model trained on text this app cannot normalize the same way would score every message wrongly.
        if (manifest.Normalizer > TextNormalizer.Version)
            throw new InvalidDataException($"The spam model needs normalizer {manifest.Normalizer}; this app has {TextNormalizer.Version}.");

        return manifest;
    }

    public static SpamModelManifest Read(string path) => Parse(File.ReadAllBytes(path));
}
