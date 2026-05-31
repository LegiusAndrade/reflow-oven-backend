using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
    .AddPolicy(AuthPolicies.AdminOnly, p => p.RequireRole(nameof(UserType.Admin)))
    .AddPolicy(AuthPolicies.CalibrationOnly, p => p.RequireClaim("calibration", "true"));

// --- CORS for the Next.js frontend ------------------------------------------------------
const string corsPolicy = "frontend";
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:3000"];
builder.Services.AddCors(o => o.AddPolicy(corsPolicy, p =>
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

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
    await DbSeeder.SeedAsync(db, sp.GetRequiredService<IPasswordHasher>(), clock);
    if (app.Configuration.GetValue<bool>("Seed:Demo"))
        await DbSeeder.SeedDemoAsync(db, clock);

    // One-shot startup "auditoria" of the persisted/seeded data (users, programs, logs by type).
    await ReflowOven.Api.StartupDiagnostics.LogAuditAsync(sp.GetRequiredService<SystemService>(), app.Logger);
}

// Seed-only mode: `dotnet run -- seed-only` migrates + seeds and exits (no web server).
if (args.Contains("seed-only"))
    return;

// --- Pipeline ---------------------------------------------------------------------------
app.UseMiddleware<ExceptionMiddleware>();

// One concise line per HTTP request (method, path, status, elapsed ms) — and the SignalR handshakes.
app.UseSerilogRequestLogging();

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
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<RunTelemetryHub>("/hubs/telemetry");
app.MapHub<DiagnosticsHub>("/hubs/diagnostics");
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
