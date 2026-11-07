using System.Text.Json.Nodes;
using JourneyPlanningApi.Auth;
using JourneyPlanningApi.Data;
using JourneyPlanningApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JourneyPlanningApi.Services;

public record ServiceResult(object Body, int Status);

public class AuthService
{
    /// <summary>Verification and reset links expire after 15 minutes, as in the Flask app.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _db;
    private readonly JwtService _jwt;
    private readonly EmailService _email;

    public AuthService(AppDbContext db, JwtService jwt, EmailService email) { _db = db; _jwt = jwt; _email = email; }

    // Naive UTC timestamps, like Flask's datetime.utcnow() stored in DATETIME / timestamp without time zone.
    private static DateTime UtcNow => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    /// <summary>
    /// Users whose email matches ignoring case (lower() on both sides works on PostgreSQL, MySQL and SQLite without
    /// extensions). An exact-case match is preferred in case an older database holds addresses differing only by case.
    /// </summary>
    private IQueryable<User> ByEmail(string email)
    {
        var lowered = email.ToLowerInvariant();
        return _db.Users.Where(u => u.Email.ToLower() == lowered).OrderByDescending(u => u.Email == email);
    }

    /// <summary>Equivalent of Python's secrets.token_urlsafe(32).</summary>
    private static string NewToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

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

            if (await ByEmail(email).AnyAsync())
                return new(new { error = "Email already registered" }, 400);

            var user = new User
            {
                FirstName = first, LastName = last, Email = email,
                PasswordHash = Hash(password), Role = "user",
                IsVerified = false, VerificationToken = NewToken(), VerificationExpires = UtcNow + TokenLifetime,
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            if (!await _email.SendVerificationEmailAsync(user.Email, user.VerificationToken))
            {
                // No email went out, so drop the token: otherwise resend-verification answers 429 for 15 minutes.
                user.VerificationToken = null;
                user.VerificationExpires = null;
                await _db.SaveChangesAsync();
                return new(new { error = "User created but email failed" }, 500);
            }

            return new(new { message = "User registered successfully. Please check your email.", user_id = user.Id }, 201);
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
            var user = email is null ? null : await ByEmail(email).FirstOrDefaultAsync();

            if (user is null || !Verify(password, user.PasswordHash))
                return new(new { error = "Invalid email or password" }, 401);

            if (!user.IsVerified)
                return new(new { error = "Please verify your email" }, 403);

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

            var changingEmail = data.ContainsKey("email") && Str(data, "email") != user.Email;
            var changingCredentials = changingEmail || data.ContainsKey("password");
            if (changingCredentials && !Verify(Str(data, "current_password"), user.PasswordHash))
                return new(new { error = "Missing or incorrect current password" }, 401);

            if (data.ContainsKey("email"))
            {
                var email = Str(data, "email");
                if (email is null) return new(new { error = "email must be a string" }, 400);
                if (email != user.Email)
                {
                    if (await ByEmail(email).AnyAsync(u => u.Id != userId))
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
                return new(new { error = "Missing or incorrect current password" }, 401);

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

    public async Task<ServiceResult> VerifyEmailAsync(string? token)
    {
        try
        {
            var user = token is null ? null : await _db.Users.FirstOrDefaultAsync(u => u.VerificationToken == token);
            if (user is null) return new(new { error = "Invalid token" }, 400);
            if (user.VerificationExpires < UtcNow) return new(new { error = "Token expired" }, 400);

            user.IsVerified = true;
            user.VerificationToken = null;
            user.VerificationExpires = null;
            await _db.SaveChangesAsync();
            return new(new { message = "Email verified successfully" }, 200);
        }
        catch (Exception e)
        {
            return new(new { error = e.Message }, 500);
        }
    }

    public async Task<ServiceResult> ResendVerificationEmailAsync(string? email)
    {
        try
        {
            var user = email is null ? null : await ByEmail(email).FirstOrDefaultAsync();
            if (user is null) return new(new { error = "User not found" }, 404);
            if (user.IsVerified) return new(new { error = "Already verified" }, 400);
            if (user.VerificationToken is not null && user.VerificationExpires > UtcNow)
                return new(new { error = "Please wait before requesting another verification email" }, 429);

            user.VerificationToken = NewToken();
            user.VerificationExpires = UtcNow + TokenLifetime;
            await _db.SaveChangesAsync();

            if (!await _email.SendVerificationEmailAsync(user.Email, user.VerificationToken))
            {
                user.VerificationToken = null;
                user.VerificationExpires = null;
                await _db.SaveChangesAsync();
                return new(new { error = "Email send failed" }, 500);
            }
            return new(new { message = "Verification email sent" }, 200);
        }
        catch (Exception e)
        {
            return new(new { error = e.Message }, 500);
        }
    }

    public async Task<ServiceResult> ForgotPasswordAsync(string? email)
    {
        try
        {
            var user = email is null ? null : await ByEmail(email).FirstOrDefaultAsync();
            if (user is null) return new(new { message = "If email exists, reset sent" }, 200);
            if (user.ResetToken is not null && user.ResetExpires > UtcNow)
                return new(new { error = "Please wait before requesting another password reset email" }, 429);

            user.ResetToken = NewToken();
            user.ResetExpires = UtcNow + TokenLifetime;
            await _db.SaveChangesAsync();

            if (!await _email.SendPasswordResetEmailAsync(user.Email, user.ResetToken))
            {
                // No email went out, so drop the token: otherwise the next attempt answers 429 for 15 minutes.
                user.ResetToken = null;
                user.ResetExpires = null;
                await _db.SaveChangesAsync();
                return new(new { error = "Reset email failed" }, 500);
            }
            return new(new { message = "Reset email sent" }, 200);
        }
        catch (Exception e)
        {
            return new(new { error = e.Message }, 500);
        }
    }

    public async Task<ServiceResult> ResetPasswordAsync(string? token, string? newPassword)
    {
        try
        {
            var user = token is null ? null : await _db.Users.FirstOrDefaultAsync(u => u.ResetToken == token);
            if (user is null) return new(new { error = "Invalid token" }, 400);
            if (user.ResetExpires < UtcNow) return new(new { error = "Token expired" }, 400);
            if (newPassword is null) return new(new { error = "new_password must be a string" }, 400);

            user.PasswordHash = Hash(newPassword);
            user.ResetToken = null;
            user.ResetExpires = null;
            await _db.SaveChangesAsync();
            return new(new { message = "Password reset successful" }, 200);
        }
        catch (Exception e)
        {
            return new(new { error = e.Message }, 500);
        }
    }
}
