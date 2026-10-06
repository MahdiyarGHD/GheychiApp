using System.Text.Json;

namespace Gheychi.Infrastructure.Spam;

/// <summary>
/// Describes a model file. Column names live here rather than in code because ML.NET Model Builder names them
/// after the training file's header row, so every retrained model can bring different ones.
/// </summary>
public sealed record SpamModelManifest(int Version, string ModelFile, string TextColumn, string LabelColumn, string SpamLabel)
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
                root.GetProperty("spamLabel").GetString() ?? string.Empty);
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
            || manifest.SpamLabel.Length == 0)
            throw new InvalidDataException("Incomplete spam model manifest.");

        return manifest;
    }

    public static SpamModelManifest Read(string path) => Parse(File.ReadAllBytes(path));
}
