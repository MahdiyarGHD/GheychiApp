using Microsoft.ML;
using Microsoft.ML.Data;

namespace Gheychi.Infrastructure.Spam;

/// <summary>A loaded model, ready to score texts.</summary>
public sealed class SpamModel
{
    private readonly object _gate = new();
    private readonly PredictionEngine<ModelInput, ModelOutput> _engine;
    private readonly int _spamIndex;

    private SpamModel(int version, PredictionEngine<ModelInput, ModelOutput> engine, int spamIndex)
    {
        Version = version;
        _engine = engine;
        _spamIndex = spamIndex;
    }

    public int Version { get; }

    /// <summary>Loads the model a manifest folder describes.</summary>
    /// <exception cref="InvalidDataException">The folder does not hold a usable model.</exception>
    public static SpamModel Load(string directory)
    {
        var manifest = SpamModelManifest.Read(Path.Combine(directory, SpamModelManifest.FileName));
        try
        {
            var ml = new MLContext();
            var transformer = ml.Model.Load(Path.Combine(directory, manifest.ModelFile), out _);

            var input = SchemaDefinition.Create(typeof(ModelInput));
            input[nameof(ModelInput.Text)].ColumnName = manifest.TextColumn;
            input[nameof(ModelInput.Label)].ColumnName = manifest.LabelColumn;
            var engine = ml.Model.CreatePredictionEngine<ModelInput, ModelOutput>(transformer, inputSchemaDefinition: input);

            // The score vector follows the order of the label column's key values.
            var labels = default(VBuffer<ReadOnlyMemory<char>>);
            engine.OutputSchema[manifest.LabelColumn].GetKeyValues(ref labels);
            var spamIndex = labels.DenseValues().Select(v => v.ToString()).ToList().IndexOf(manifest.SpamLabel);
            if (spamIndex < 0)
                throw new InvalidDataException($"Label '{manifest.SpamLabel}' is not one of the model's labels.");

            var model = new SpamModel(manifest.Version, engine, spamIndex);
            model.Score(string.Empty);
            return model;
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException("The spam model could not be loaded.", ex);
        }
    }

    public float Score(string text)
    {
        // PredictionEngine is not thread-safe.
        lock (_gate)
        {
            var output = _engine.Predict(new ModelInput { Text = text });
            if (_spamIndex >= output.Score.Length)
                throw new InvalidDataException("The model's scores do not match its labels.");
            return output.Score[_spamIndex];
        }
    }

    private sealed class ModelInput
    {
        public string Text { get; set; } = string.Empty;

        // The trained pipeline maps the label column too, so it must exist even though it is never known.
        public string Label { get; set; } = string.Empty;
    }

    private sealed class ModelOutput
    {
        public float[] Score { get; set; } = [];
    }
}
