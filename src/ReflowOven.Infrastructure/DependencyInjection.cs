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
            opt.UseNpgsql(config.GetConnectionString("Default"));
            // In dev, surface full DB error detail (failing column/parameter values) in the logs.
            if (isDevelopment) opt.EnableDetailedErrors().EnableSensitiveDataLogging();
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<ReflowDbContext>());

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.Configure<TechnicianOptions>(config.GetSection(TechnicianOptions.Section));
        services.AddSingleton<ITechnicianCredentials, TechnicianCredentials>();

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

        // OrangePi OS controller: real nmcli/systemctl on the Pi (System:Mode=Linux), else a simulator.
        // The Linux controller uses IHttpClientFactory for the central-server/OTA HTTP probes.
        services.Configure<SystemOptions>(config.GetSection(SystemOptions.Section));
        services.AddHttpClient();
        var systemMode = config.GetSection(SystemOptions.Section)["Mode"];
        if (string.Equals(systemMode, "Linux", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ISystemController, LinuxSystemController>();
        else
            services.AddSingleton<ISystemController, SimulatedSystemController>();
        services.AddHostedService<SystemMonitorService>();

        return services;
    }
}
