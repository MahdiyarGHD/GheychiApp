using System.Xml.Linq;
using Gheychi.Core.Updates;
using Gheychi.Infrastructure.Updates;
using Xunit;

namespace Gheychi.Infrastructure.Tests;

public sealed class GitHubAppReleaseSourceTests
{
    private const string Feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom" xmlns:media="http://search.yahoo.com/mrss/" xml:lang="en-US">
          <link type="text/html" rel="alternate" href="https://github.com/MahdiyarGHD/GheychiApp/releases"/>
          <entry>
            <link rel="alternate" type="text/html" href="https://github.com/MahdiyarGHD/GheychiApp/releases/tag/1.3.0-rc.1"/>
            <title>1.3.0-rc.1</title>
          </entry>
          <entry>
            <link rel="alternate" type="text/html" href="https://github.com/MahdiyarGHD/GheychiApp/releases/tag/v1.2.0"/>
            <title>v1.2.0</title>
          </entry>
          <entry>
            <link rel="alternate" type="text/html" href="https://github.com/MahdiyarGHD/GheychiApp/releases/tag/nightly"/>
            <title>nightly</title>
          </entry>
        </feed>
        """;

    [Fact]
    public void Parse_ReadsReleaseTagsAndPages_SkipsOtherTags()
    {
        var releases = GitHubAppReleaseSource.Parse(XDocument.Parse(Feed));

        Assert.Equal(
            [
                new AppRelease("1.3.0-rc.1", "https://github.com/MahdiyarGHD/GheychiApp/releases/tag/1.3.0-rc.1",
                    "https://github.com/MahdiyarGHD/GheychiApp/releases/download/1.3.0-rc.1/Gheychi-1.3.0-rc.1.apk"),
                new AppRelease("v1.2.0", "https://github.com/MahdiyarGHD/GheychiApp/releases/tag/v1.2.0",
                    "https://github.com/MahdiyarGHD/GheychiApp/releases/download/v1.2.0/Gheychi-1.2.0.apk")
            ],
            releases);
    }
}
