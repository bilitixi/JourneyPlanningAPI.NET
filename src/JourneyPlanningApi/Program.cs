using JourneyPlanningApi.Auth;
using JourneyPlanningApi.Data;
using JourneyPlanningApi.Services;
using Microsoft.EntityFrameworkCore;

// Load a local .env file if present (equivalent of python-dotenv). Real env vars still win.
DotNetEnv.Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// CORS: any origin on /api/* (Flask: CORS(app, resources={r"/api/*": {"origins": "*"}}))
builder.Services.AddCors(o => o.AddPolicy("api", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// MySQL configuration: MYSQL_URL / DATABASE_URL, ConnectionStrings:Default, or MYSQL_HOST / PORT / USER /
// PASSWORD / DATABASE (same defaults as db.py). See DatabaseConfig.
var dbConnection = DatabaseConfig.Build(builder.Configuration);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(dbConnection.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0)),
        mysql => mysql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)));

// Hosting platforms such as Render and Heroku tell the app which port to listen on via PORT.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } listenPort)
    builder.WebHost.UseUrls($"http://0.0.0.0:{listenPort}");

builder.Services.AddSingleton<JwtService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpClient<WeatherService>();
builder.Services.AddHttpClient<AiService>();

var app = builder.Build();

// Create tables on startup (Flask: init_db()). Can be skipped with SKIP_DB_INIT=true.
if (!app.Configuration.GetValue<bool>("SKIP_DB_INIT"))
{
    var target = DatabaseConfig.Describe(dbConnection);
    app.Logger.LogInformation("Connecting to MySQL at {Target}", target);
    try
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        app.Logger.LogInformation("Database tables created successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex,
            "Could not initialise the database at {Target}. Set MYSQL_URL (mysql://user:pass@host:port/db) or " +
            "MYSQL_HOST / MYSQL_PORT / MYSQL_USER / MYSQL_PASSWORD / MYSQL_DATABASE to a reachable MySQL server " +
            "(MYSQL_SSL_MODE=Required for most hosted providers), or set SKIP_DB_INIT=true to skip table creation.",
            target);
        return 1;
    }
}

app.UseCors("api");
app.MapGet("/", () => "Hello World!");
app.MapControllers();

app.Run();
return 0;

public partial class Program { }
