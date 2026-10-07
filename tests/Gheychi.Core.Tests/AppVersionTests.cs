using Gheychi.Core.Updates;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("1.2.3-rc", 1, 2, 3, 0)]
    [InlineData("1.2.3-rc.4", 1, 2, 3, 4)]
    [InlineData("1.2.3-rc4", 1, 2, 3, 4)]
    [InlineData("1.0", 1, 0, 0, null)]
    public void Parse_ReleaseTags(string text, int major, int minor, int patch, int? candidate) =>
        Assert.Equal(new AppVersion(major, minor, patch, candidate), AppVersion.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("model-v4")]
    [InlineData("1.2.3-beta")]
    [InlineData("1")]
    public void Parse_OtherText_IsNull(string text) => Assert.Null(AppVersion.Parse(text));

    [Theory]
    [InlineData("1.2.4", "1.2.3")]
    [InlineData("1.10.0", "1.9.9")]
    [InlineData("1.2.3", "1.2.3-rc.9")]
    [InlineData("1.2.3-rc.2", "1.2.3-rc.1")]
    [InlineData("1.2.3-rc.1", "1.2.3-rc")]
    [InlineData("1.2.4-rc", "1.2.3")]
    public void Compare_FirstIsNewer(string newer, string older) =>
        Assert.True(AppVersion.Parse(newer)!.Value > AppVersion.Parse(older)!.Value);
}
