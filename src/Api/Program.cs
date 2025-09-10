// Program.cs
using Application;
using Infrastructure;
using System.Security.Claims;
using System.IO;
using System.Linq;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Data.Sqlite;

// 🔎 Observability
using Serilog;
using Serilog.Events;
using Microsoft.ApplicationInsights.Extensibility;
using Serilog.Sinks.ApplicationInsights;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog: 부트스트랩 로거
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("App", "TeoApi")
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext()
      .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
      .WriteTo.Console();

    var aiConn = ctx.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
    if (!string.IsNullOrWhiteSpace(aiConn))
    {
        var tc = new TelemetryConfiguration { ConnectionString = aiConn };
        cfg.WriteTo.ApplicationInsights(tc, TelemetryConverter.Traces);
    }
});

// (선택) App Insights SDK
builder.Services.AddApplicationInsightsTelemetry();

// === CORS (Vite & 정적 사이트 & Prod) ===
// 우선순위: appsettings.json:Cors:AllowedOrigins > 기본값
string[] defaultAllowed =
{
    "http://localhost:5173",
    "http://localhost:5174",
    "http://127.0.0.1:5173",
    "http://127.0.0.1:5174",
    "https://teo-web-dev.azurewebsites.net",
    // ✅ Azure Storage Static Website (현재 프런트 실제 도메인)
    "https://teowebdevstor001.z8.web.core.windows.net"
};
var allowed = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? defaultAllowed;

builder.Services.AddCors(opt =>
{
    opt.AddPolicy("vite", p => p
        .WithOrigins(allowed)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials() // Bearer만 쓰면 필수는 아니지만 유연성을 위해 허용
    );
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// === Feature Toggles ===
var isDev = builder.Environment.IsDevelopment();
var enableDebug = builder.Configuration.GetValue<bool?>("EnableDebugEndpoints") ?? isDev;

// === DB ===
var sqliteCs = builder.Configuration.GetConnectionString("Sqlite")
               ?? "Data Source=/data/teo.db";
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(sqliteCs));
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

// === 🔐 JWT (Entra External ID / CIAM) ===
var instance  = builder.Configuration["AzureAdB2C:Instance"]!.TrimEnd('/');
var tenantId  = builder.Configuration["AzureAdB2C:TenantId"]!;
var audience  = builder.Configuration["AzureAdB2C:Audience"]!;
var authority = $"{instance}/{tenantId}/v2.0";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidAudience  = audience,
            NameClaimType  = "name",
            RoleClaimType  = "roles"
        };
        // 들어온 클레임 키(sub/scp 등) 원문 보존
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });

// === 권한/스코프 정책 ===
var scopeClaimTypes = new[]
{
    "scp",
    "http://schemas.microsoft.com/identity/claims/scope"
};
var roleClaimTypes = new[]
{
    "roles",
    ClaimTypes.Role,
    "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
};

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiScope", policy =>
        policy.RequireAssertion(ctx =>
            scopeClaimTypes.Any(t =>
                ctx.User.Claims.Any(c => c.Type == t && c.Value.Split(' ').Contains("access_as_user"))) ||
            roleClaimTypes.Any(t =>
                ctx.User.Claims.Any(c => c.Type == t && c.Value.Split(' ').Contains("access_as_user")))
        )
    );
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 🔎 요청 로그
app.UseSerilogRequestLogging();

// === 미들웨어 순서 ===
app.UseRouting();
app.UseCors("vite");
app.UseAuthentication();
app.UseAuthorization();

// === 앱 시작 시 DB 경로 보장 + 🔁 마이그레이션 적용 ===
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var effective = db.Database.GetDbConnection().ConnectionString;
    var dataSource = new SqliteConnectionStringBuilder(effective).DataSource;

    if (!string.IsNullOrWhiteSpace(dataSource))
    {
        var dir = Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
    await db.Database.MigrateAsync();

    app.Logger.LogInformation("EnableDebugEndpoints={Enable} | SQLite CS={CS} | DS={DS} | Env={Env} | AllowedOrigins={Allowed}",
        enableDebug, effective, dataSource, app.Environment.EnvironmentName, string.Join(",", allowed));
}

// === 헬스 ===
app.MapGet("/health", async (AppDbContext db) =>
{
    try
    {
        var ok = await db.Database.CanConnectAsync();
        return ok ? Results.Ok(new { status = "Healthy" })
                  : Results.Problem("DB not reachable");
    }
    catch (Exception ex)
    {
        return Results.Problem($"DB exception: {ex.Message}");
    }
});

// === 디버그 엔드포인트 (토글) ===
if (enableDebug)
{
    var dbg = app.MapGroup("/debug");

    dbg.MapGet("/dbinfo", (IConfiguration cfg, AppDbContext db) =>
    {
        var fromConfig = cfg.GetConnectionString("Sqlite");
        var effective  = db.Database.GetDbConnection().ConnectionString;
        var builderCs  = new SqliteConnectionStringBuilder(effective);
        var ds         = builderCs.DataSource ?? "";
        var dir        = string.IsNullOrEmpty(ds) ? "" : Path.GetDirectoryName(ds) ?? "";
        var info = new
        {
            fromConfig,
            effective,
            dataSource = ds,
            dirExists  = string.IsNullOrEmpty(dir) ? (bool?)null : Directory.Exists(dir),
            fileExists = string.IsNullOrEmpty(ds)  ? (bool?)null : File.Exists(ds)
        };
        return Results.Json(info);
    });

    dbg.MapPost("/seed", async (AppDbContext db) =>
    {
        await db.Database.MigrateAsync();
        var ok = await db.Database.CanConnectAsync();
        return Results.Ok(new { migrated = true, canConnect = ok });
    });
}

// === 보호 엔드포인트 (JWT + 스코프 필요) ===
app.MapGet("/me", [Authorize(Policy = "ApiScope")] (ClaimsPrincipal user) =>
{
    string? name = user.FindFirst("name")?.Value ?? user.Identity?.Name;
    string? oidRaw  = user.FindFirst("oid")?.Value;
    string? oidMs   = user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
    string? subRaw  = user.FindFirst("sub")?.Value;
    string? subNI   = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    string? upn     = user.FindFirst("preferred_username")?.Value;
    string? scpRaw  = user.FindFirst("scp")?.Value;
    string? scpMs   = user.FindFirst("http://schemas.microsoft.com/identity/claims/scope")?.Value;

    return Results.Ok(new {
        name, oidRaw, oidMs, subRaw, subNI, upn, scpRaw, scpMs
    });
});

app.MapGet("/api/me", [Authorize(Policy = "ApiScope")] (ClaimsPrincipal user) =>
{
    string? name = user.FindFirst("name")?.Value ?? user.Identity?.Name;
    string? oidRaw  = user.FindFirst("oid")?.Value;
    string? oidMs   = user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
    string? subRaw  = user.FindFirst("sub")?.Value;
    string? subNI   = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    string? upn     = user.FindFirst("preferred_username")?.Value;
    string? scpRaw  = user.FindFirst("scp")?.Value;
    string? scpMs   = user.FindFirst("http://schemas.microsoft.com/identity/claims/scope")?.Value;

    return Results.Ok(new {
        name, oidRaw, oidMs, subRaw, subNI, upn, scpRaw, scpMs
    });
});

app.MapControllers();

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}