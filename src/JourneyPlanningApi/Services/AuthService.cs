using System.Text.Json.Nodes;
using JourneyPlanningApi.Auth;
using JourneyPlanningApi.Data;
using JourneyPlanningApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JourneyPlanningApi.Services;

public record ServiceResult(object Body, int Status);

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;

    public AuthService(AppDbContext db, JwtService jwt) { _db = db; _jwt = jwt; }

    private static string? Str(JsonObject d, string key) =>
        d[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    private static bool Verify(string? password, string hash)
    {
        if (string.IsNullOrEmpty(password)) return false;
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }

    public async Task<ServiceResult> RegisterAsync(JsonObject data)
    {
        try
        {
            var first = Str(data, "first_name");
            var last = Str(data, "last_name");
            var email = Str(data, "email");
            var password = Str(data, "password");
            if (first is null || last is null || email is null || password is null)
                return new(new { error = "first_name, last_name, email and password must be strings" }, 400);

            if (await _db.Users.AnyAsync(u => u.Email == email))
                return new(new { error = "Email already registered" }, 400);

            var user = new User
            {
                FirstName = first, LastName = last, Email = email,
                PasswordHash = Hash(password), Role = "user",
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return new(new { message = "User registered successfully", user_id = user.Id }, 201);
        }
        catch (Exception e)
        {
            return new(new { error = e.Message }, 500);
        }
    }

    public async Task<ServiceResult> LoginAsync(JsonObject data)
    {
        try
        {
            var email = Str(data, "email");
            var password = Str(data, "password");
            var user = email is null ? null : await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user is null || !Verify(password, user.PasswordHash))
                return new(new { error = "Invalid email or password" }, 401);

            var token = _jwt.CreateToken(user.Id, user.Email, user.Role, TimeSpan.FromHours(24));
            return new(new Dictionary<string, object?>
            {
                ["access_token"] = token,
                ["user"] = new Dictionary<string, object?>
                {
                    ["id"] = user.Id,
                    ["email"] = user.Email,
                    ["first_name"] = user.FirstName,
                    ["last_name"] = user.LastName,
                    ["role"] = user.Role,
                },
            }, 200);
        }
        catch (Exception e)
        {
            return new(new { error = e.Message }, 500);
        }
    }

    public async Task<ServiceResult> UpdateUserAsync(int? userId, JsonObject data)
    {
        try
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return new(new { error = "User not found" }, 404);

            var changingCredentials = data.ContainsKey("email") || data.ContainsKey("password");
            if (changingCredentials && !Verify(Str(data, "current_password"), user.PasswordHash))
                return new(new { error = "current_password is required and must be correct to change email or password" }, 401);

            if (data.ContainsKey("email"))
            {
                var email = Str(data, "email");
                if (email is null) return new(new { error = "email must be a string" }, 400);
                if (email != user.Email)
                {
                    if (await _db.Users.AnyAsync(u => u.Email == email && u.Id != userId))
                        return new(new { error = "Email already registered" }, 400);
                    user.Email = email;
                }
            }

            if (data.ContainsKey("first_name"))
                user.FirstName = Str(data, "first_name") ?? throw new ArgumentException("first_name must be a string");
            if (data.ContainsKey("last_name"))
                user.LastName = Str(data, "last_name") ?? throw new ArgumentException("last_name must be a string");
            if (data.ContainsKey("password"))
                user.PasswordHash = Hash(Str(data, "password") ?? throw new ArgumentException("password must be a string"));

            await _db.SaveChangesAsync();
            await _db.Entry(user).ReloadAsync();
            return new(new { message = "User updated successfully", user = user.ToDict() }, 200);
        }
        catch (Exception e)
        {
            return new(new { error = e.Message }, 500);
        }
    }

    public async Task<ServiceResult> DeleteUserAsync(int? userId, JsonObject data)
    {
        try
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return new(new { error = "User not found" }, 404);

            if (!Verify(Str(data, "current_password"), user.PasswordHash))
                return new(new { error = "current_password is required and must be correct to delete this account" }, 401);

            // Remove the user's journeys first to satisfy the foreign key constraint
            _db.Journeys.RemoveRange(_db.Journeys.Where(j => j.UserId == userId));
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();

            return new(new { message = "Account deleted successfully" }, 200);
        }
        catch (Exception e)
        {
            return new(new { error = e.Message }, 500);
        }
    }
}
