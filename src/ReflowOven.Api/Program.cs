using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ReflowOven.Api.Auth;
using ReflowOven.Api.Middleware;
using ReflowOven.Api.Realtime;
using ReflowOven.Application;
using ReflowOven.Infrastructure;
using ReflowOven.Infrastructure.Auth;
using ReflowOven.Infrastructure.Persistence;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

// Configuration is read from appsettings(.Development).json plus real environment variables (the default
// providers). Local secrets live directly in appsettings.json — kept out of git by the "secretscrub" clean
// filter (scripts/setup_git_scrub.sh) — and production injects them as env vars. There is no .env file.

// Bootstrap logger: captures anything thrown during startup (config, DI, migrations) on the console.
// It is replaced by the fully-configured logger once the host is built (see UseSerilog below).
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{

var builder = WebApplication.CreateBuilder(args);

// --- Logging (Serilog → console; everything: requests, login, CRUD, run, DB errors) -----
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

// --- Application + Infrastructure -------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton<ITelemetrySink, SignalRTelemetrySink>();
builder.Services.AddSingleton<ISystemLogSink, SignalRSystemLogSink>();
builder.Services.AddSingleton<INotificationSink, SignalRNotificationSink>();

// --- JSON (pt-BR enum strings, omit nulls) ----------------------------------------------
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddSignalR().AddJsonProtocol(o =>
    o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// --- AuthN (JWT, also accepted via the SignalR query string) ----------------------------
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();

// Never run outside Development on the throwaway dev key (or an empty one): real deployments must
// supply a strong secret via environment variable (Jwt__SigningKey) or user-secrets — never hardcoded.
const string devSigningKeyPlaceholder = "dev-only-change-me-please-use-32-bytes-minimum!";
if (!builder.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey == devSigningKeyPlaceholder))
{
    throw new InvalidOperationException(
        "Jwt:SigningKey ausente ou padrão. Configure um segredo forte via Jwt__SigningKey (env) ou user-secrets fora de Development.");
}

// The dev Master password is a placeholder like the JWT key; never ship it outside Development.
const string devMasterPasswordPlaceholder = "pandewilly";
if (!builder.Environment.IsDevelopment() &&
    string.Equals(builder.Configuration["Master:Password"], devMasterPasswordPlaceholder, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Master:Password ainda é o padrão de dev. Defina um segredo real via Master__Password (env) fora de Development.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            },
        };
    });

// --- AuthZ (authenticated by default; Admin & Calibration policies) ---------------------
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(AuthPolicies.AdminOnly, p => p.RequireRole(nameof(UserType.Admin), nameof(UserType.Master)))
    .AddPolicy(AuthPolicies.OperatorOrAdmin, p => p.RequireRole(nameof(UserType.Admin), nameof(UserType.Regular), nameof(UserType.Master)))
    .AddPolicy(AuthPolicies.MasterOnly, p => p.RequireRole(nameof(UserType.Master)))
    .AddPolicy(AuthPolicies.CalibrationOnly, p => p.RequireClaim("calibration", "true"));

// --- CORS for the Next.js frontend ------------------------------------------------------
const string corsPolicy = "frontend";
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:3000"];
builder.Services.AddCors(o => o.AddPolicy(corsPolicy, p =>
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// --- Rate limiting: brute-force / DoS guard on the anonymous auth endpoints (login/forgot/reset) ------
// Fixed window per client IP. NOTE: behind the Next.js BFF the backend sees the BFF's IP, so this is a
// coarse global backstop; the precise per-account control is AuthService's ILoginThrottle lockout. Forward
// the real client IP (X-Forwarded-For + UseForwardedHeaders) to make this true per-client.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    // Answer in the frontend's {ok,error} shape so the login screen shows a clean message, not a raw 429.
    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await ctx.HttpContext.Response.WriteAsync(
            "{\"ok\":false,\"error\":\"Muitas tentativas. Aguarde um momento e tente novamente.\"}", token);
    };
});

builder.Services.AddHealthChecks();

// --- HTTP request/response logging (with redaction) — wired here, enabled in Development only --------
builder.Services.AddHttpLogging(o =>
{
    o.LoggingFields = HttpLoggingFields.RequestPath | HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestQuery | HttpLoggingFields.RequestBody
        | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.ResponseBody | HttpLoggingFields.Duration;
    o.RequestBodyLogLimit = 4096;
    o.ResponseBodyLogLimit = 4096;
    o.MediaTypeOptions.AddText("application/json");
    o.CombineLogs = true;
});
// Bodies of /api/auth/* (passwords, tokens) are dropped from the logs by this interceptor.
builder.Services.AddHttpLoggingInterceptor<AuthRedactionInterceptor>();

// --- OpenAPI document (served for the Scalar UI; declares the JWT bearer scheme) ---------
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, _) =>
    {
        document.Info.Title = "Reflow Oven API";
        document.Info.Version = "v1";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT Bearer — informe apenas o token.",
        };
        document.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] },
        ];
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// --- Migrate + seed on startup ----------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<ReflowDbContext>();
    var clock = sp.GetRequiredService<IClock>();

    // Migrate (creating the DB + logging a Warning when it doesn't exist yet on a fresh PC), then seed.
    await ReflowOven.Api.StartupDiagnostics.MigrateAndLogAsync(db, app.Logger);
    await DbSeeder.SeedAsync(db, sp.GetRequiredService<IPasswordHasher>(), clock,
        sp.GetRequiredService<IMasterCredentials>(), sp.GetRequiredService<IAdminCredentials>(), sp.GetRequiredService<IRegularCredentials>());
    if (app.Configuration.GetValue<bool>("Seed:Demo"))
        await DbSeeder.SeedDemoAsync(db, sp.GetRequiredService<IPasswordHasher>(), clock);

    // One-shot startup "auditoria" of the persisted/seeded data (users, programs, logs by type).
    await ReflowOven.Api.StartupDiagnostics.LogAuditAsync(sp.GetRequiredService<SystemService>(), app.Logger);
}

// Seed-only mode: `dotnet run -- seed-only` migrates + seeds and exits (no web server).
if (args.Contains("seed-only"))
    return;

// --- Pipeline ---------------------------------------------------------------------------
app.UseMiddleware<ExceptionMiddleware>();

// One concise line per HTTP request — method, path + query string, status, elapsed ms (and SignalR
// handshakes). The query string is logged so filtered endpoints stay legible: e.g. the Log de Operação's
// sub-tabs all hit /api/operation-log but with a different ?category=. Noise is demoted to Verbose (below
// the console threshold): CORS preflight (OPTIONS), health/Scalar/OpenAPI polls, and client-cancelled
// requests (the browser aborting a superseded fetch — not a server error). Real failures stay Error.
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath}{RequestQueryString} responded {StatusCode} in {Elapsed:0.0000} ms";
    // Redact the SignalR ?access_token=<JWT> before it reaches the logs (the hubs authenticate via the
    // query string, and this request-logging line runs in production too).
    options.EnrichDiagnosticContext = (diag, ctx) =>
        diag.Set("RequestQueryString", LogRedaction.Query(ctx.Request.QueryString.Value));
    options.GetLevel = (ctx, _, ex) =>
        ex is OperationCanceledException && ctx.RequestAborted.IsCancellationRequested ? LogEventLevel.Verbose
        : ex is not null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
        : HttpMethods.IsOptions(ctx.Request.Method)
            || ctx.Request.Path.StartsWithSegments("/health")
            || ctx.Request.Path.StartsWithSegments("/scalar")
            || ctx.Request.Path.StartsWithSegments("/openapi")
            ? LogEventLevel.Verbose
            : LogEventLevel.Information;
});

if (app.Environment.IsDevelopment())
{
    // Verbose request/response logging (bodies redacted on /api/auth) — dev only, to keep prod logs lean.
    app.UseHttpLogging();

    // OpenAPI doc at /openapi/v1.json, served through the Scalar reference UI at /scalar.
    // Both are endpoints, so they need AllowAnonymous to escape the authenticated-by-default policy.
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options => options
        .WithTitle("Reflow Oven API")
        .WithTheme(ScalarTheme.Purple))
        .AllowAnonymous();
}

app.UseCors(corsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<RunTelemetryHub>("/hubs/telemetry");
app.MapHub<DiagnosticsHub>("/hubs/diagnostics");
app.MapHub<SystemLogHub>("/hubs/systemlog");
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "A API encerrou inesperadamente durante a inicialização.");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Log hygiene helpers for the request-logging pipeline.</summary>
static class LogRedaction
{
    private static readonly System.Text.RegularExpressions.Regex AccessToken =
        new("(?i)(access_token=)[^&]*", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Masks the value of any <c>access_token</c> query parameter (the SignalR JWT) for logging.</summary>
    public static string Query(string? queryString) =>
        string.IsNullOrEmpty(queryString) ? "" : AccessToken.Replace(queryString, "access_token=***");
}
