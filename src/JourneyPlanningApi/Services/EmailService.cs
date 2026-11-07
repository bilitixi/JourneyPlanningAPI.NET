using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text.Json.Nodes;

namespace JourneyPlanningApi.Services;

/// <summary>Delivers one email; returns false (and logs) on failure, like the Flask _send_email.</summary>
public interface IEmailSender
{
    Task<bool> SendAsync(string to, string subject, string textBody, string htmlBody);
}

/// <summary>
/// Verification and password reset emails (Flask deploy branch services/email_service.py). Links point to
/// FRONTEND_URL (default http://localhost:3000): {FRONTEND_URL}/verify-email?token=... and /reset-password?token=...
/// </summary>
public class EmailService
{
    public const string DefaultFrontendUrl = "http://localhost:3000";

    private readonly IEmailSender _sender;
    private readonly string _frontendUrl;

    public EmailService(IEmailSender sender, IConfiguration config)
    {
        _sender = sender;
        var url = config["FRONTEND_URL"];
        _frontendUrl = (string.IsNullOrWhiteSpace(url) ? DefaultFrontendUrl : url).TrimEnd('/');
    }

    public Task<bool> SendVerificationEmailAsync(string email, string token)
    {
        var url = $"{_frontendUrl}/verify-email?token={Uri.EscapeDataString(token)}";
        return _sender.SendAsync(email, "Verify Your Email",
            $"Verify your email address.\n\nClick the link below:\n{url}\n",
            $"""
            <h2>Verify Your Email</h2>
            <p>Please click the link below to verify your account:</p>
            <a href="{WebUtility.HtmlEncode(url)}">Verify Email</a>
            """);
    }

    public Task<bool> SendPasswordResetEmailAsync(string email, string token)
    {
        var url = $"{_frontendUrl}/reset-password?token={Uri.EscapeDataString(token)}";
        return _sender.SendAsync(email, "Reset Your Password",
            $"Reset your password.\n\nClick the link below:\n{url}\n",
            $"""
            <h2>Reset Password</h2>
            <p>Please click the link below to reset your password:</p>
            <a href="{WebUtility.HtmlEncode(url)}">Reset Password</a>
            """);
    }
}

/// <summary>
/// SMTP with STARTTLS, same settings as the Flask app: MAIL_SERVER (smtp.gmail.com), MAIL_PORT (587),
/// MAIL_USERNAME, MAIL_PASSWORD, MAIL_USE_TLS (true), MAIL_DEFAULT_SENDER (MAIL_USERNAME).
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<SmtpEmailSender> _log;
    private readonly string _server;
    private readonly int _port;
    private readonly string? _username;
    private readonly string? _password;
    private readonly bool _useTls;
    private readonly string? _sender;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> log)
    {
        _log = log;
        _server = string.IsNullOrWhiteSpace(config["MAIL_SERVER"]) ? "smtp.gmail.com" : config["MAIL_SERVER"]!;
        _port = int.TryParse(config["MAIL_PORT"], out var port) ? port : 587;
        _username = config["MAIL_USERNAME"];
        _password = config["MAIL_PASSWORD"];
        _useTls = !string.Equals(config["MAIL_USE_TLS"], "false", StringComparison.OrdinalIgnoreCase);
        _sender = string.IsNullOrWhiteSpace(config["MAIL_DEFAULT_SENDER"]) ? _username : config["MAIL_DEFAULT_SENDER"];
    }

    public async Task<bool> SendAsync(string to, string subject, string textBody, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_sender))
        {
            _log.LogError("SMTP error: MAIL_USERNAME / MAIL_DEFAULT_SENDER is not set");
            return false;
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_sender, "Journey Planning"),
                Subject = subject,
                Body = textBody,
            };
            message.To.Add(to);
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html"));

            using var smtp = new SmtpClient(_server, _port) { EnableSsl = _useTls };
            if (!string.IsNullOrEmpty(_username))
                smtp.Credentials = new NetworkCredential(_username, _password);
            // SmtpClient.Timeout does not apply to SendMailAsync; without this a blocked SMTP port hangs the request
            // for minutes (the OS connect timeout).
            using var cts = new CancellationTokenSource(SendTimeout);
            await smtp.SendMailAsync(message, cts.Token);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SMTP error sending to {Server}:{Port}", _server, _port);
            return false;
        }
    }
}

/// <summary>
/// Alternative to SMTP for hosts that block outbound SMTP: Resend's HTTPS API, used when RESEND_API_KEY is set.
/// Sender: RESEND_FROM, an address on a domain verified in Resend (default Resend's test sender
/// onboarding@resend.dev, which only delivers to the Resend account's own address). MAIL_DEFAULT_SENDER is not
/// used here because it is usually a Gmail address, which Resend refuses to send from.
/// </summary>
public class ResendEmailSender : IEmailSender
{
    public const string ApiUrl = "https://api.resend.com/emails";

    private readonly HttpClient _http;
    private readonly ILogger<ResendEmailSender> _log;
    private readonly string? _apiKey;
    private readonly string _from;

    public ResendEmailSender(HttpClient http, IConfiguration config, ILogger<ResendEmailSender> log)
    {
        _http = http;
        _log = log;
        _apiKey = config["RESEND_API_KEY"];
        var sender = config["RESEND_FROM"];
        _from = $"Journey Planning <{(string.IsNullOrWhiteSpace(sender) ? "onboarding@resend.dev" : sender)}>";
    }

    public async Task<bool> SendAsync(string to, string subject, string textBody, string htmlBody)
    {
        var body = new JsonObject
        {
            ["from"] = _from, ["to"] = new JsonArray(to), ["subject"] = subject,
            ["text"] = textBody, ["html"] = htmlBody,
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        try
        {
            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return true;
            _log.LogError("Resend rejected email ({Status}): {Body}",
                (int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogError(ex, "Could not reach Resend");
            return false;
        }
    }
}

/// <summary>
/// Mailgun's HTTPS API, as used by the Flask deploy branch before it switched to SMTP: MAILGUN_API_KEY and
/// MAILGUN_DOMAIN, sending from "Journey Planning &lt;postmaster@{MAILGUN_DOMAIN}&gt;". MAILGUN_API_URL selects the
/// region (default https://api.mailgun.net; EU accounts use https://api.eu.mailgun.net). Works on hosts that block SMTP.
/// </summary>
public class MailgunEmailSender : IEmailSender
{
    private readonly HttpClient _http;
    private readonly ILogger<MailgunEmailSender> _log;
    private readonly string? _apiKey;
    private readonly string? _domain;
    private readonly string _apiUrl;

    public MailgunEmailSender(HttpClient http, IConfiguration config, ILogger<MailgunEmailSender> log)
    {
        _http = http;
        _log = log;
        _apiKey = config["MAILGUN_API_KEY"];
        _domain = config["MAILGUN_DOMAIN"];
        _apiUrl = (string.IsNullOrWhiteSpace(config["MAILGUN_API_URL"]) ? "https://api.mailgun.net" : config["MAILGUN_API_URL"]!)
            .TrimEnd('/');
    }

    public async Task<bool> SendAsync(string to, string subject, string textBody, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_domain))
        {
            _log.LogError("Mailgun error: MAILGUN_DOMAIN is not set");
            return false;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_apiUrl}/v3/{_domain}/messages")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["from"] = $"Journey Planning <postmaster@{_domain}>",
                ["to"] = to,
                ["subject"] = subject,
                ["text"] = textBody,
                ["html"] = htmlBody,
            }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"api:{_apiKey}")));

        try
        {
            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return true;
            _log.LogError("Mailgun error ({Status}): {Body}", (int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogError(ex, "Mailgun error: could not reach {Url}", _apiUrl);
            return false;
        }
    }
}
