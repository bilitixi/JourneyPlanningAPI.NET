using JourneyPlanningApi.Auth;
using JourneyPlanningApi.Data;
using JourneyPlanningApi.Services;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

// Load a local .env file if present (equivalent of python-dotenv). Real env vars still win.
DotNetEnv.Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// CORS: any origin on /api/* (Flask: CORS(app, resources={r"/api/*": {"origins": "*"}}))
builder.Services.AddCors(o => o.AddPolicy("api", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// MySQL configuration (MYSQL_HOST / PORT / USER / PASSWORD / DATABASE, same defaults as db.py)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cfg = builder.Configuration;
    var cs = new MySqlConnectionStringBuilder
    {
        Server = cfg["MYSQL_HOST"] ?? "localhost",
        Port = uint.TryParse(cfg["MYSQL_PORT"], out var port) ? port : 3306,
        UserID = cfg["MYSQL_USER"] ?? "root",
        Password = cfg["MYSQL_PASSWORD"] ?? "",
        Database = cfg["MYSQL_DATABASE"] ?? "journey_planning",
    }.ConnectionString;
    options.UseMySql(cs, new MySqlServerVersion(new Version(8, 0, 0)));
});

builder.Services.AddSingleton<JwtService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpClient<WeatherService>();
builder.Services.AddHttpClient<AiService>();

var app = builder.Build();

// Create tables on startup (Flask: init_db()). Can be skipped with SKIP_DB_INIT=true.
if (!app.Configuration.GetValue<bool>("SKIP_DB_INIT"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    app.Logger.LogInformation("Database tables created successfully");
}

app.UseCors("api");
app.MapGet("/", () => "Hello World!");
app.MapControllers();

app.Run();

public partial class Program { }
