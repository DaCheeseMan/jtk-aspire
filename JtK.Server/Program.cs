using JtK.Server.Data;
using JtK.Server.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// PostgreSQL via Aspire
builder.AddNpgsqlDbContext<AppDbContext>("jtkdb");

// JWT Bearer authentication with Keycloak
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Keycloak:Authority"];
        options.Audience = "jtk-web";
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters.ValidateIssuer = false;
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                ctx.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>()
                    .LogError(ctx.Exception,
                        "JWT authentication failed. Authority={Authority}",
                        options.Authority);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Ignore navigation-property cycles when serialising EF entities directly
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.ReferenceHandler =
        System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);

// CORS for React frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "http://localhost:5174",
                "https://localhost:5173",
                "https://localhost:5174")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Apply EF migrations on startup. Retry with back-off to handle the race where the
    // PostgreSQL container passes its health check before it is fully ready to serve DDL.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var migrationLogger = scope.ServiceProvider
        .GetRequiredService<ILogger<AppDbContext>>();

    const int maxAttempts = 8;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            // Note: EF logs a "Failed executing DbCommand" for the __EFMigrationsHistory
            // SELECT on a brand-new database — that is normal first-run behaviour.
            // MigrateAsync handles it internally and creates the table before proceeding.
            await db.Database.MigrateAsync();
            break;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)); // 1s, 2s, 4s, 8s…
            migrationLogger.LogWarning(ex,
                "Migration attempt {Attempt}/{Max} failed. Retrying in {Delay}s…",
                attempt, maxAttempts, delay.TotalSeconds);
            await Task.Delay(delay);
        }
    }
}

// --- Courts endpoints (public) ---
var courtsApi = app.MapGroup("/api/courts");

courtsApi.MapGet("/", async (AppDbContext db) =>
    await db.Courts.Where(c => c.IsActive).ToListAsync());

courtsApi.MapGet("/{id:int}", async (int id, AppDbContext db) =>
    await db.Courts.FindAsync(id) is Court court && court.IsActive
        ? Results.Ok(court)
        : Results.NotFound());

// --- Bookings endpoints (authenticated) ---
var bookingsApi = app.MapGroup("/api/bookings").RequireAuthorization();

bookingsApi.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? user.FindFirstValue("sub")!;
    return await db.Bookings
        .Include(b => b.Court)
        .Where(b => b.UserId == userId)
        .OrderByDescending(b => b.Date).ThenBy(b => b.StartTime)
        .ToListAsync();
});

bookingsApi.MapPost("/", async (CreateBookingRequest req, ClaimsPrincipal user, AppDbContext db) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? user.FindFirstValue("sub")!;
    var userName = user.FindFirstValue(ClaimTypes.Name)
                ?? user.FindFirstValue("preferred_username")
                ?? userId;

    var court = await db.Courts.FindAsync(req.CourtId);
    if (court is null || !court.IsActive)
        return Results.BadRequest("Court not found.");

    var start = new TimeOnly(req.StartHour, 0);
    var end = start.AddHours(1);

    var overlap = await db.Bookings.AnyAsync(b =>
        b.CourtId == req.CourtId &&
        b.Date == req.Date &&
        b.StartTime == start);

    if (overlap)
        return Results.Conflict("That slot is already booked.");

    var booking = new Booking
    {
        CourtId = req.CourtId,
        UserId = userId,
        UserName = userName,
        Date = req.Date,
        StartTime = start,
        EndTime = end
    };

    db.Bookings.Add(booking);
    await db.SaveChangesAsync();
    return Results.Created($"/api/bookings/{booking.Id}", booking);
});

bookingsApi.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? user.FindFirstValue("sub")!;
    var booking = await db.Bookings.FindAsync(id);
    if (booking is null) return Results.NotFound();
    if (booking.UserId != userId) return Results.Forbid();

    db.Bookings.Remove(booking);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// Runtime config for the frontend (Keycloak authority must be the browser-reachable URL)
app.MapGet("/api/config", (IConfiguration config, IWebHostEnvironment env) =>
{
    var authority = config["Keycloak:ExternalAuthority"] ?? config["Keycloak:Authority"] ?? "";
    // Azure Container Apps terminates TLS externally — ensure the browser gets an HTTPS URL
    if (!env.IsDevelopment())
        authority = authority.Replace("http://", "https://");
    return Results.Ok(new { keycloakAuthority = authority });
});

app.MapDefaultEndpoints();
app.UseFileServer();

app.Run();

record CreateBookingRequest(int CourtId, DateOnly Date, int StartHour);

