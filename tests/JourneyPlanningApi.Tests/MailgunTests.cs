using System.Net;
using System.Text;
using JourneyPlanningApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JourneyPlanningApi.Tests;

public class MailgunTests
{
    private static (MailgunEmailSender Sender, StubHandler Http) Create(params (string, string?)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Item1, s.Item2)))
            .Build();
        var http = new StubHandler();
        return (new MailgunEmailSender(new HttpClient(http), config, NullLogger<MailgunEmailSender>.Instance), http);
    }

    [Fact]
    public async Task Posts_form_to_mailgun_messages_endpoint_like_the_flask_app()
    {
        var (sender, http) = Create(("MAILGUN_API_KEY", "key-123"), ("MAILGUN_DOMAIN", "mg.bilitixi.org"));
        HttpRequestMessage? seen = null;
        string? body = null;
        http.Respond = req =>
        {
            seen = req;
            body = req.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        Assert.True(await sender.SendAsync("to@x.com", "Reset Your Password", "text body", "<p>html</p>"));

        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal("https://api.mailgun.net/v3/mg.bilitixi.org/messages", seen.RequestUri!.ToString());
        Assert.Equal("Basic", seen.Headers.Authorization!.Scheme);
        Assert.Equal("api:key-123",
            Encoding.UTF8.GetString(Convert.FromBase64String(seen.Headers.Authorization.Parameter!)));
        var form = body!.Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
        Assert.Equal("Journey Planning <postmaster@mg.bilitixi.org>", form["from"]);
        Assert.Equal("to@x.com", form["to"]);
        Assert.Equal("Reset Your Password", form["subject"]);
        Assert.Equal("text body", form["text"]);
        Assert.Equal("<p>html</p>", form["html"]);
    }

    [Fact]
    public async Task Eu_region_url_can_be_configured()
    {
        var (sender, http) = Create(("MAILGUN_API_KEY", "k"), ("MAILGUN_DOMAIN", "mg.example.com"),
            ("MAILGUN_API_URL", "https://api.eu.mailgun.net/"));
        Uri? url = null;
        http.Respond = req => { url = req.RequestUri; return new HttpResponseMessage(HttpStatusCode.OK); };
        Assert.True(await sender.SendAsync("to@x.com", "s", "t", "h"));
        Assert.Equal("https://api.eu.mailgun.net/v3/mg.example.com/messages", url!.ToString());
    }

    [Fact]
    public async Task Rejection_or_missing_domain_reports_failure()
    {
        var (sender, http) = Create(("MAILGUN_API_KEY", "bad"), ("MAILGUN_DOMAIN", "mg.example.com"));
        http.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        Assert.False(await sender.SendAsync("to@x.com", "s", "t", "h"));

        var (noDomain, _) = Create(("MAILGUN_API_KEY", "k"));
        Assert.False(await noDomain.SendAsync("to@x.com", "s", "t", "h"));
    }
}
