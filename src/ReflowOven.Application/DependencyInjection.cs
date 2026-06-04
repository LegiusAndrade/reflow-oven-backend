using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ReflowOven.Application.Abstractions;
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
        services.AddScoped<OperationLogService>();
        services.AddScoped<DiagnosticsService>();
        services.AddScoped<MaintenanceService>();
        services.AddScoped<DeviceService>();
        services.AddScoped<SystemService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<SystemLogService>();
        // Fallback no-op push sink; the Api layer overrides this with the SignalR implementation.
        services.TryAddSingleton<ISystemLogSink, NullSystemLogSink>();
        return services;
    }
}
