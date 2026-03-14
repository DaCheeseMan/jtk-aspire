using JtK.Server.Data;
using JtK.Server.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Net.Http.Headers;

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

// Named HttpClient for proxying requests to the Keycloak Account API
builder.Services.AddHttpClient("keycloak-account");

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Apply EF migrations on every startup in all environments.
// This creates the schema and seeds the courts data on first run.
// Retry with exponential back-off to handle the race where PostgreSQL passes its
// health check before it is fully ready to serve DDL (common on cold container starts).
{
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// --- Courts endpoints (public) ---
var courtsApi = app.MapGroup("/api/courts");

courtsApi.MapGet("/", async (AppDbContext db) =>
    await db.Courts.Where(c => c.IsActive).ToListAsync());

courtsApi.MapGet("/{id:int}", async (int id, AppDbContext db) =>
    await db.Courts.FindAsync(id) is Court court && court.IsActive
        ? Results.Ok(court)
        : Results.NotFound());

// Returns all bookings for a court in a date range (for the weekly calendar)
courtsApi.MapGet("/{id:int}/bookings", async (int id, DateOnly from, DateOnly to, AppDbContext db) =>
{
    var bookings = await db.Bookings
        .Where(b => b.CourtId == id && b.Date >= from && b.Date <= to)
        .OrderBy(b => b.Date).ThenBy(b => b.StartTime)
        .Select(b => new
        {
            b.Id,
            b.Date,
            b.StartTime,
            b.EndTime,
            b.UserId,
            b.UserName,
            b.UserPhone,
        })
        .ToListAsync();
    return Results.Ok(bookings);
}).RequireAuthorization();

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
    var userPhone = user.FindFirstValue("phone_number") ?? string.Empty;

    var court = await db.Courts.FindAsync(req.CourtId);
    if (court is null || !court.IsActive)
        return Results.BadRequest("Court not found.");

    var start = new TimeOnly(req.StartHour, 0);
    var end = start.AddHours(1);

    // Reject bookings in the past (compare date + time against current local time)
    var nowUtc = DateTime.UtcNow;
    var today = DateOnly.FromDateTime(nowUtc);
    var nowTime = TimeOnly.FromDateTime(nowUtc);
    if (req.Date < today || (req.Date == today && start <= nowTime))
        return Results.BadRequest("Cannot book a slot in the past.");

    // Max 2 future bookings per user
    var futureCount = await db.Bookings.CountAsync(b =>
        b.UserId == userId &&
        (b.Date > today || (b.Date == today && b.StartTime > nowTime)));
    if (futureCount >= 2)
        return Results.BadRequest("Du kan inte ha fler än 2 kommande bokningar.");

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
        UserPhone = userPhone,
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

// --- Profile endpoints (authenticated) ---
// Uses the Keycloak Admin REST API server-side so there are no browser CORS issues
// and no dependency on the token's audience claim.
var profileApi = app.MapGroup("/api/profile").RequireAuthorization();

profileApi.MapGet("/", async (ClaimsPrincipal user, IConfiguration config, IHttpClientFactory httpClientFactory) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub")!;
    var adminToken = await GetKeycloakAdminTokenAsync(config, httpClientFactory);
    if (adminToken is null) return Results.StatusCode(502);

    var adminUrl = $"{config["Keycloak:AdminUrl"]}/admin/realms/jtk/users/{userId}";
    var client = httpClientFactory.CreateClient("keycloak-account");
    var req = new HttpRequestMessage(HttpMethod.Get, adminUrl);
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

    var res = await client.SendAsync(req);
    if (!res.IsSuccessStatusCode) return Results.StatusCode((int)res.StatusCode);

    var kcUser = await res.Content.ReadFromJsonAsync<KeycloakUserRepresentation>();
    return Results.Ok(new
    {
        firstName = kcUser?.FirstName,
        lastName = kcUser?.LastName,
        email = kcUser?.Email,
        attributes = kcUser?.Attributes,
    });
});

profileApi.MapPost("/", async (ClaimsPrincipal user, ProfileUpdateRequest body, IConfiguration config, IHttpClientFactory httpClientFactory) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub")!;
    var adminToken = await GetKeycloakAdminTokenAsync(config, httpClientFactory);
    if (adminToken is null) return Results.StatusCode(502);

    var adminUrl = $"{config["Keycloak:AdminUrl"]}/admin/realms/jtk/users/{userId}";
    var client = httpClientFactory.CreateClient("keycloak-account");

    // Merge attributes from the body with any existing attributes
    var getReq = new HttpRequestMessage(HttpMethod.Get, adminUrl);
    getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
    var getRes = await client.SendAsync(getReq);
    var existing = getRes.IsSuccessStatusCode
        ? await getRes.Content.ReadFromJsonAsync<KeycloakUserRepresentation>()
        : null;

    var mergedAttributes = existing?.Attributes ?? new Dictionary<string, List<string>>();
    if (body.Attributes is not null)
        foreach (var kv in body.Attributes)
            mergedAttributes[kv.Key] = kv.Value;

    var update = new KeycloakUserRepresentation
    {
        FirstName = body.FirstName,
        LastName = body.LastName,
        Email = body.Email,
        Attributes = mergedAttributes,
    };

    var putReq = new HttpRequestMessage(HttpMethod.Put, adminUrl);
    putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
    putReq.Content = JsonContent.Create(update);

    var putRes = await client.SendAsync(putReq);
    return putRes.IsSuccessStatusCode
        ? Results.NoContent()
        : Results.StatusCode((int)putRes.StatusCode);
});

app.MapDefaultEndpoints();
app.UseFileServer();

app.Run();

// Obtains a short-lived Keycloak admin token using the admin-cli client.
static async Task<string?> GetKeycloakAdminTokenAsync(IConfiguration config, IHttpClientFactory factory)
{
    var adminUrl = config["Keycloak:AdminUrl"];
    var password = config["Keycloak:AdminPassword"] ?? "admin";
    if (string.IsNullOrEmpty(adminUrl)) return null;

    var client = factory.CreateClient("keycloak-account");
    var form = new FormUrlEncodedContent([
        new("grant_type", "password"),
        new("client_id", "admin-cli"),
        new("username", "admin"),
        new("password", password),
    ]);
    var res = await client.PostAsync($"{adminUrl}/realms/master/protocol/openid-connect/token", form);
    if (!res.IsSuccessStatusCode) return null;

    var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    return json.GetProperty("access_token").GetString();
}

record CreateBookingRequest(int CourtId, DateOnly Date, int StartHour);
record ProfileUpdateRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    Dictionary<string, List<string>>? Attributes);

class KeycloakUserRepresentation
{
    [System.Text.Json.Serialization.JsonPropertyName("firstName")]
    public string? FirstName { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("lastName")]
    public string? LastName { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("email")]
    public string? Email { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("attributes")]
    public Dictionary<string, List<string>>? Attributes { get; set; }
}


