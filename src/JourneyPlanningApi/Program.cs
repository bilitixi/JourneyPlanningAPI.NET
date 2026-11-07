using JourneyPlanningApi.Auth;
using JourneyPlanningApi.Data;
using JourneyPlanningApi.Http;
using JourneyPlanningApi.Services;
using Microsoft.EntityFrameworkCore;

// Load a local .env file if present (equivalent of python-dotenv). Real env vars still win.
DotNetEnv.Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// CORS: only the frontends listed in CORS_ORIGINS (default: the production and Vercel UIs plus localhost).
var corsOrigins = new CorsOrigins(builder.Configuration["CORS_ORIGINS"]);
builder.Services.AddCors(o => o.AddPolicy("api", p =>
    p.SetIsOriginAllowed(corsOrigins.IsAllowed).AllowAnyHeader().AllowAnyMethod()));

// Database: MySQL (MYSQL_URL, ConnectionStrings:Default or MYSQL_HOST / PORT / USER / PASSWORD / DATABASE, same
// defaults as db.py) or PostgreSQL (a postgres:// DATABASE_URL, e.g. a Render database). See DatabaseConfig.
var db = DatabaseConfig.Build(builder.Configuration);

// Map DateTime to "timestamp without time zone" on PostgreSQL, like MySQL DATETIME, instead of requiring UTC values.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (db.Provider == DbProvider.PostgreSql)
        options.UseNpgsql(db.ConnectionString,
            o => o.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null));
    else
        options.UseMySql(db.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0)),
            o => o.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null));
});

// Hosting platforms such as Render and Heroku tell the app which port to listen on via PORT.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } listenPort)
    builder.WebHost.UseUrls($"http://0.0.0.0:{listenPort}");

builder.Services.AddSingleton<JwtService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpClient<WeatherService>();
builder.Services.AddHttpClient<AiService>();
builder.Services.AddScoped<EmailService>();
// Email provider: Mailgun's HTTPS API when MAILGUN_API_KEY is set, else Resend's when RESEND_API_KEY is set, else
// SMTP (MAIL_* settings). The HTTPS APIs work on hosts that block outbound SMTP, such as Render.
if (!string.IsNullOrWhiteSpace(builder.Configuration["MAILGUN_API_KEY"]))
    builder.Services.AddHttpClient<IEmailSender, MailgunEmailSender>(c => c.Timeout = SmtpEmailSender.SendTimeout);
else if (!string.IsNullOrWhiteSpace(builder.Configuration["RESEND_API_KEY"]))
    builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(c => c.Timeout = SmtpEmailSender.SendTimeout);
else
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

var app = builder.Build();

// Create tables on startup (Flask: init_db()). Can be skipped with SKIP_DB_INIT=true.
if (!app.Configuration.GetValue<bool>("SKIP_DB_INIT"))
{
    var target = db.Description;
    app.Logger.LogInformation("Connecting to {Target}", target);
    try
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.EnsureCreated();
        var added = await SchemaUpgrade.AddMissingUserColumnsAsync(dbContext, db.Provider);
        if (added.Count > 0)
            app.Logger.LogInformation("Added users columns {Columns}; existing accounts marked as verified",
                string.Join(", ", added));
        app.Logger.LogInformation("Database tables created successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex,
            "Could not initialise the database at {Target}. Set DATABASE_URL (postgresql://... or mysql://...) or " +
            "MYSQL_HOST / MYSQL_PORT / MYSQL_USER / MYSQL_PASSWORD / MYSQL_DATABASE to a reachable MySQL server " +
            "(MYSQL_SSL_MODE=Required for most hosted providers), or set SKIP_DB_INIT=true to skip table creation.",
            target);
        return 1;
    }
}

if (string.IsNullOrWhiteSpace(app.Configuration["FRONTEND_URL"]))
    app.Logger.LogWarning("FRONTEND_URL is not set; email links will point to {Url}", EmailService.DefaultFrontendUrl);

app.UseCors("api");
// Error responses without a body (unknown route, wrong method, ...) get the API's {"error": "..."} shape, so
// clients that always call response.json() see a message instead of a JSON parse error.
app.UseStatusCodePages(async ctx =>
{
    var res = ctx.HttpContext.Response;
    res.ContentType = "application/json";
    var message = res.StatusCode switch
    {
        404 => "Not found",
        405 => "Method not allowed",
        _ => Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(res.StatusCode),
    };
    await res.WriteAsJsonAsync(new { error = message });
});
app.MapGet("/", () => "Hello World!");
app.MapControllers();

app.Run();
return 0;

public partial class Program { }
