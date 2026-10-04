using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class LinkAndPlaceDetectionTests
{
    [Fact]
    public void Links_FindsEveryUrlWithHostWithoutWww()
    {
        var found = LinkExtractor.Find("see https://www.example.com/a?b=1 and www.other.org/x ok");

        Assert.Collection(found,
            first =>
            {
                Assert.Equal("https://www.example.com/a?b=1", first.Title);
                Assert.Equal("example.com", first.Host);
            },
            second =>
            {
                Assert.Equal("www.other.org/x", second.Title);
                Assert.Equal("other.org", second.Host);
                Assert.Equal("https://www.other.org/x", second.OpenUrl);
            });
    }

    [Theory]
    [InlineData("Open (https://example.com/page).", "https://example.com/page")]
    [InlineData("go to https://example.com/page!", "https://example.com/page")]
    [InlineData("لینک https://example.com/page،", "https://example.com/page")]
    [InlineData("wiki https://en.wikipedia.org/wiki/Foo_(bar) done", "https://en.wikipedia.org/wiki/Foo_(bar)")]
    public void Links_TrimsSentencePunctuation(string body, string expected)
    {
        Assert.Equal(expected, Assert.Single(LinkExtractor.Find(body)).Title);
    }

    [Fact]
    public void Links_DuplicatesInOneMessageAreCollapsed()
    {
        Assert.Single(LinkExtractor.Find("https://a.com/x https://a.com/x"));
    }

    [Theory]
    [InlineData("no links here")]
    [InlineData("")]
    [InlineData("http://localhost/x")]
    public void Links_NoneFound(string body)
    {
        Assert.Empty(LinkExtractor.Find(body));
    }

    [Theory]
    [InlineData("https://maps.app.goo.gl/AbC123")]
    [InlineData("https://www.google.com/maps/place/Tehran/@35.7,51.4,12z")]
    [InlineData("https://maps.google.com/?q=35.7219,51.3347")]
    [InlineData("https://waze.com/ul?ll=35.72,51.33")]
    [InlineData("https://maps.apple.com/?ll=35.72,51.33")]
    [InlineData("https://neshan.org/maps/places/abc")]
    [InlineData("https://nshn.ir/abcd")]
    [InlineData("https://balad.ir/location?latitude=35.7&longitude=51.4")]
    [InlineData("https://www.openstreetmap.org/#map=16/35.72/51.33")]
    [InlineData("https://yandex.com/maps/-/CCUx")]
    public void Places_MapLinksAreDetected(string url)
    {
        Assert.True(PlaceDetector.IsMapLink(url));
        Assert.Single(PlaceDetector.Find("here: " + url));
    }

    [Theory]
    [InlineData("https://www.google.com/search?q=maps")]
    [InlineData("https://example.com/maps-of-the-world")]
    [InlineData("https://goo.gl/abc")]
    public void Places_OrdinaryLinksAreNotPlaces(string url)
    {
        Assert.False(PlaceDetector.IsMapLink(url));
        Assert.Empty(PlaceDetector.Find(url));
    }

    [Fact]
    public void Places_PlainCoordinatesAreDetected()
    {
        var place = Assert.Single(PlaceDetector.Find("I am at 35.7219, 51.3347 now"));

        Assert.Equal("35.7219, 51.3347", place.Title);
        Assert.Equal(string.Empty, place.Host);
        Assert.Equal("geo:35.7219,51.3347", place.OpenUrl);
    }

    [Fact]
    public void Places_GeoUriIsDetected()
    {
        var place = Assert.Single(PlaceDetector.Find("geo:35.7219,51.3347"));
        Assert.Equal("geo:35.7219,51.3347", place.OpenUrl);
    }

    [Theory]
    [InlineData("price 12.50, 13.75 total")]
    [InlineData("version 1.2.3, 4.5.6")]
    [InlineData("coords 95.1234, 51.3347")]
    [InlineData("coords 35.1234, 181.3347")]
    public void Places_NumbersThatAreNotCoordinatesAreIgnored(string body)
    {
        Assert.Empty(PlaceDetector.Find(body));
    }

    [Fact]
    public void Places_CoordinatesInsideMapLinkAreNotListedTwice()
    {
        var places = PlaceDetector.Find("https://www.google.com/maps/@35.72190,51.33470,17z");
        Assert.Single(places);
    }
}
