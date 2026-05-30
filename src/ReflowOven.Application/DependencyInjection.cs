using Microsoft.Extensions.DependencyInjection;
using ReflowOven.Application.Services;

namespace ReflowOven.Application;

public static class DependencyInjection
{
    /// <summary>Registers the Application services (scoped). Abstractions are implemented in Infrastructure.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuditService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<ProgramService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<CalibrationService>();
        services.AddScoped<ReportService>();
        services.AddScoped<DiagnosticsService>();
        services.AddScoped<MaintenanceService>();
        services.AddScoped<DeviceService>();
        return services;
    }
}
