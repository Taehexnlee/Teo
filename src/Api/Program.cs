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

// ── Serilog: 부트스트랩 로거(초기 예외도 캡처)
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

    // App Insights 연결 문자열이 있으면 트레이스 전송
    var aiConn = ctx.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
    if (!string.IsNullOrWhiteSpace(aiConn))
    {
        var tc = new TelemetryConfiguration { ConnectionString = aiConn };
        cfg.WriteTo.ApplicationInsights(tc, TelemetryConverter.Traces);
    }
});

// (선택) App Insights SDK 활성화: 요청/종속성 자동 수집
builder.Services.AddApplicationInsightsTelemetry();


// === CORS (Vite & Dev/Prod 웹 허용) ===
var allowed = new[]
{
    "http://localhost:5173",
    "http://localhost:5174",
    "http://127.0.0.1:5173",
    "http://127.0.0.1:5174",
    "https://teo-web-dev.azurewebsites.net",
    "https://teowebdevstor001.z8.web.core.windows.net"   // ⬅️ 정적 웹 사이트 도메인 추가

};
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("vite", p => p
        .WithOrigins(allowed)
        .AllowAnyHeader()
        .AllowAnyMethod());
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

        // ✅ 들어온 클레임 키(sub/scp 등) 원문 보존
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

// 🔎 요청 로그 미들웨어(지연/상태코드 등)
app.UseSerilogRequestLogging(opts =>
{
    // 느린 요청만 강조하고 싶다면:
    // opts.GetLevel = (ctx, _, ex) =>
    //     ex != null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error :
    //     ctx.Response.StatusCode >= 400 ? LogEventLevel.Warning :
    //     LogEventLevel.Information;
});

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

    // ✅ 이제 EnsureCreated 대신 자동 마이그레이션
    await db.Database.MigrateAsync();

    app.Logger.LogInformation("EnableDebugEndpoints={Enable} | SQLite CS={CS} | DS={DS} | Env={Env}",
        enableDebug, effective, dataSource, app.Environment.EnvironmentName);
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
        // 개발 편의용: 로컬에서만 사용
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