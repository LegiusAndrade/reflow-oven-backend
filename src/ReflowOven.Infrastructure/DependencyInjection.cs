using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReflowOven.Infrastructure.Auth;
using ReflowOven.Infrastructure.BackgroundServices;
using ReflowOven.Infrastructure.Email;
using ReflowOven.Infrastructure.Hardware;
using ReflowOven.Infrastructure.Persistence;
using ReflowOven.Infrastructure.Platform;
using ReflowOven.Infrastructure.Run;
using ReflowOven.Infrastructure.Time;

namespace ReflowOven.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Wires persistence (Npgsql), clock, password hashing, JWT, technician credentials, the email
    /// stub, the power board (Simulated|Rs422 by config), the run manager and the control loop.
    /// <c>ICurrentUser</c> and <c>ITelemetrySink</c> are registered by the API layer.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var isDevelopment = string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
        services.AddDbContext<ReflowDbContext>(opt =>
        {
            // The only query that loads two collections is the tiny Settings singleton (~11 notification rows ×
            // 7 chart-series rows), so a single query with that small cartesian product is the cheap, correct
            // choice — make it the explicit default, which also silences EF's MultipleCollectionIncludeWarning.
            opt.UseNpgsql(config.GetConnectionString("Default"),
                o => o.UseQuerySplittingBehavior(Microsoft.EntityFrameworkCore.QuerySplittingBehavior.SingleQuery));
            // In dev, surface full DB error detail (failing column/parameter values) in the logs. The
            // capability stays on for exceptions, but the one-time "sensitive data logging is enabled"
            // boot nag is silenced so it doesn't shout on every startup.
            if (isDevelopment)
                opt.EnableDetailedErrors().EnableSensitiveDataLogging()
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.SensitiveDataLoggingEnabledWarning));
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<ReflowDbContext>());

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.Configure<TechnicianOptions>(config.GetSection(TechnicianOptions.Section));
        services.AddSingleton<ITechnicianCredentials, TechnicianCredentials>();

        // Per-username login lockout (brute-force / BCrypt-DoS guard) — in-process cache, single device.
        services.AddMemoryCache();
        services.AddSingleton<ILoginThrottle, LoginThrottle>();

        // JWT revocation: deactivating/demoting/deleting a user (or a self/admin/recovery password change)
        // cuts the user's outstanding tokens immediately instead of waiting out the 8 h expiry. In-memory
        // hot path + a durable watermark table hydrated at startup (survives the appliance's own restarts).
        // Registered concrete-first so Program.cs can hydrate it before serving traffic.
        services.AddSingleton<TokenRevocationList>();
        services.AddSingleton<ITokenRevocationList>(sp => sp.GetRequiredService<TokenRevocationList>());

        services.Configure<MasterOptions>(config.GetSection(MasterOptions.Section));
        services.AddSingleton<IMasterCredentials, MasterCredentials>();

        services.Configure<AdminOptions>(config.GetSection(AdminOptions.Section));
        services.AddSingleton<IAdminCredentials, AdminCredentials>();
        services.Configure<RegularOptions>(config.GetSection(RegularOptions.Section));
        services.AddSingleton<IRegularCredentials, RegularCredentials>();

        services.Configure<EmailOptions>(config.GetSection(EmailOptions.Section));
        var emailMode = config.GetSection(EmailOptions.Section)["Mode"];
        if (string.Equals(emailMode, "Smtp", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, StubEmailSender>();

        services.Configure<HardwareOptions>(config.GetSection(HardwareOptions.Section));
        var mode = config.GetSection(HardwareOptions.Section)["Mode"];
        if (string.Equals(mode, "Rs422", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IPowerBoard, Rs422PowerBoard>();
        else
            services.AddSingleton<IPowerBoard, SimulatedPowerBoard>();

        services.AddSingleton<IRunManager, RunManager>();
        services.AddHostedService<RunControlLoopService>();

        services.AddSingleton<IAutotuneManager, AutotuneManager>();
        services.AddHostedService<AutotuneLoopService>();

        // OrangePi OS controller: real nmcli/systemctl on the Pi (System:Mode=Linux), else a simulator.
        // The Linux controller uses IHttpClientFactory for the central-server/OTA HTTP probes.
        services.Configure<SystemOptions>(config.GetSection(SystemOptions.Section));
        services.AddHttpClient();
        var systemMode = config.GetSection(SystemOptions.Section)["Mode"];
        if (string.Equals(systemMode, "Linux", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ISystemController, LinuxSystemController>();
        else
            services.AddSingleton<ISystemController, SimulatedSystemController>();

        // The control-board GPIO (LEDs/power-good) is selected independently of the OS controller via
        // System:GpioMode (falls back to System:Mode). This lets real GPIO run while nmcli/systemd stay
        // simulated — e.g. driving the LEDs from inside the dev container without a working systemd.
        var gpioMode = config.GetSection(SystemOptions.Section)["GpioMode"] ?? systemMode;
        if (string.Equals(gpioMode, "Linux", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IBoardGpio, LinuxBoardGpio>();
        else
            services.AddSingleton<IBoardGpio, SimulatedBoardGpio>();

        // Data-retention windows for the append-only stores (operation log, notification trash) applied
        // by the daily sweep in SystemMonitorService. Overridable via Retention__* env/config.
        services.Configure<RetentionOptions>(config.GetSection(RetentionOptions.Section));

        services.AddSingleton<IControlHealth, ControlHealth>();
        services.AddHostedService<SystemMonitorService>();
        services.AddHostedService<ControlHealthService>(); // samples the control board's own health
        services.AddHostedService<StatusLedService>();     // drives the STATUS LED blink-code pattern (GPIO6)

        return services;
    }
}
