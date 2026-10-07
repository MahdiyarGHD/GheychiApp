using System.Net;
using Gheychi.Core.Spam;
using Gheychi.Infrastructure.Spam;
using Xunit;

namespace Gheychi.Infrastructure.Tests;

public sealed class GoogleFormSpamReporterTests
{
    private readonly FakeHandler _handler = new();

    private GoogleFormSpamReporter Create() => new(new HttpClient(_handler));

    [Fact]
    public async Task Report_Spam_PostsTheSpamChoiceAndTheNormalizedText()
    {
        const string text = "Win 1000 now at example.com";

        await Create().ReportAsync(text, isSpam: true);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(GoogleFormSpamReporter.FormResponseUrl, request.Url);
        Assert.Equal(GoogleFormSpamReporter.SpamLabel, request.Fields["entry.1968518309"]);
        Assert.Equal(TextNormalizer.Normalize(text), request.Fields["entry.2103089956"]);
        Assert.DoesNotContain("1000", request.Fields["entry.2103089956"]);
    }

    [Fact]
    public async Task Report_NotSpam_PostsTheHamChoice()
    {
        await Create().ReportAsync("سلام، فردا میای؟", isSpam: false);

        Assert.Equal(GoogleFormSpamReporter.HamLabel, Assert.Single(_handler.Requests).Fields["entry.1968518309"]);
    }

    [Fact]
    public async Task Report_TextThatNormalizesToNothing_IsNotSent()
    {
        await Create().ReportAsync("  ‌ ", isSpam: true);

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Report_FormRejects_Throws()
    {
        _handler.Status = HttpStatusCode.BadRequest;

        await Assert.ThrowsAsync<HttpRequestException>(() => Create().ReportAsync("test", isSpam: true));
    }

    private sealed record SentRequest(string Url, Dictionary<string, string> Fields);

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var fields = body.Split('&')
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0].Replace('+', ' ')), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
            Requests.Add(new SentRequest(request.RequestUri!.AbsoluteUri, fields));
            return new HttpResponseMessage(Status);
        }
    }
}
