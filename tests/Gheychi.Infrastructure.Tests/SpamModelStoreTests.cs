using System.Text;
using Gheychi.Infrastructure.Spam;
using Xunit;

namespace Gheychi.Infrastructure.Tests;

public sealed class SpamModelStoreTests : IDisposable
{
    private static readonly string BundledDirectory = Path.Combine(AppContext.BaseDirectory, "SpamModel");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gheychi_spam_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private SpamModelStore CreateStore() =>
        new(_root, name => Task.FromResult<Stream>(File.OpenRead(Path.Combine(BundledDirectory, name))));

    private static Stream Manifest(int version, string model = "next.mlnet")
    {
        var bundled = File.ReadAllText(Path.Combine(BundledDirectory, SpamModelManifest.FileName));
        var json = bundled
            .Replace("\"version\": 1", $"\"version\": {version}")
            .Replace("spam-v1.mlnet", model);
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private static Stream BundledModel() => File.OpenRead(Path.Combine(BundledDirectory, "spam-v1.mlnet"));

    [Fact]
    public async Task BundledModel_IsInstalledAndScoresSpamAboveDefaultThreshold()
    {
        var classifier = new MlNetSpamClassifier(CreateStore());

        var spam = await classifier.ScoreAsync("تخفیف ویژه ۷۰ درصد فقط امروز! برای خرید روی لینک کلیک کنید lnkd.ir/abc لغو۱۱");
        var personal = await classifier.ScoreAsync("سلام عزیزم، شب میای خونه؟");

        Assert.NotNull(spam);
        Assert.NotNull(personal);
        Assert.Equal(1, spam.Value.ModelVersion);
        Assert.True(spam.Value.Probability >= Core.Spam.SpamDetector.DefaultThreshold);
        Assert.True(personal.Value.Probability < Core.Spam.SpamDetector.DefaultThreshold);
        Assert.True(Directory.Exists(Path.Combine(_root, "1")));
    }

    [Fact]
    public async Task InstallAsync_NewerVersion_BecomesActiveAndRemovesOlder()
    {
        var classifier = new MlNetSpamClassifier(CreateStore());
        Assert.Equal(1, await classifier.GetActiveVersionAsync());

        await using var manifest = Manifest(2);
        await using var model = BundledModel();
        Assert.True(await classifier.InstallAsync(manifest, model));

        Assert.Equal(2, await classifier.GetActiveVersionAsync());
        Assert.Equal(2, (await classifier.ScoreAsync("hello"))?.ModelVersion);
        Assert.False(Directory.Exists(Path.Combine(_root, "1")));
    }

    [Fact]
    public async Task InstalledNewerVersion_SurvivesRestart_AndBundledIsNotReinstalled()
    {
        var first = new MlNetSpamClassifier(CreateStore());
        await using (var manifest = Manifest(3))
        await using (var model = BundledModel())
            Assert.True(await first.InstallAsync(manifest, model));

        var afterRestart = new MlNetSpamClassifier(CreateStore());

        Assert.Equal(3, await afterRestart.GetActiveVersionAsync());
        Assert.False(Directory.Exists(Path.Combine(_root, "1")));
    }

    [Fact]
    public async Task InstallAsync_SameVersion_IsRejected()
    {
        var classifier = new MlNetSpamClassifier(CreateStore());

        await using var manifest = Manifest(1);
        await using var model = BundledModel();

        Assert.False(await classifier.InstallAsync(manifest, model));
        Assert.Equal(1, await classifier.GetActiveVersionAsync());
    }

    [Fact]
    public async Task InstallAsync_CorruptModel_KeepsActiveModel()
    {
        var classifier = new MlNetSpamClassifier(CreateStore());

        await using var manifest = Manifest(2);
        await using var model = new MemoryStream([1, 2, 3, 4]);

        Assert.False(await classifier.InstallAsync(manifest, model));
        Assert.Equal(1, await classifier.GetActiveVersionAsync());
        Assert.Equal(["1"], Directory.GetDirectories(_root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task InstallAsync_UnknownSpamLabel_IsRejected()
    {
        var classifier = new MlNetSpamClassifier(CreateStore());
        var json = new StreamReader(Manifest(2)).ReadToEnd().Replace("\"spamLabel\": \"true\"", "\"spamLabel\": \"spam\"");

        await using var manifest = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await using var model = BundledModel();

        Assert.False(await classifier.InstallAsync(manifest, model));
        Assert.Equal(1, await classifier.GetActiveVersionAsync());
    }

    [Fact]
    public async Task MissingBundledModel_ScoresNothing()
    {
        var store = new SpamModelStore(_root, _ => throw new FileNotFoundException());
        var classifier = new MlNetSpamClassifier(store);

        Assert.Null(await classifier.ScoreAsync("hello"));
        Assert.Null(await classifier.GetActiveVersionAsync());
    }

    [Theory]
    [InlineData("../evil.mlnet")]
    [InlineData("sub/model.mlnet")]
    [InlineData("manifest.json")]
    [InlineData("")]
    public void Manifest_RejectsModelNamesThatAreNotPlainFiles(string model)
    {
        var json = $$"""{"version":2,"model":"{{model}}","textColumn":"t","labelColumn":"l","spamLabel":"s"}""";

        Assert.Throws<InvalidDataException>(() => SpamModelManifest.Parse(Encoding.UTF8.GetBytes(json)));
    }
}
