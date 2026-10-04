using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public class LinkDetectorTests
{
    [Fact]
    public void ExtractLink_WithUrl_SplitsCorrectly()
    {
        var text = "Also check this resource link: https://dl-contractors-portal.net/spec-sheet";
        var (before, link) = LinkDetector.ExtractLink(text);

        Assert.Equal("Also check this resource link: ", before);
        Assert.Equal("https://dl-contractors-portal.net/spec-sheet", link);
    }

    [Fact]
    public void ExtractLink_WithoutUrl_ReturnsOriginalText()
    {
        var text = "Plain message with no links.";
        var (before, link) = LinkDetector.ExtractLink(text);

        Assert.Equal("Plain message with no links.", before);
        Assert.Empty(link);
    }
}
