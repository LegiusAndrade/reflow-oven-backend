using Microsoft.Extensions.Logging;

namespace ReflowOven.Infrastructure.Email;

/// <summary>Logs emails instead of sending them (dev default). Set <c>Email:Mode=Smtp</c> for real mail.</summary>
public sealed class StubEmailSender(ILogger<StubEmailSender> logger) : IEmailSender
{
    public Task SendPasswordResetAsync(string email, string resetToken, CancellationToken ct = default)
    {
        logger.LogInformation("[stub-email] Recuperação de senha para {Email}: token {Token}", email, resetToken);
        return Task.CompletedTask;
    }

    public Task SendNewUserAsync(string email, string userName, string tempPassword, DateTimeOffset changeBy, CancellationToken ct = default)
    {
        logger.LogInformation("[stub-email] Novo usuário {User} <{Email}>: senha '{Password}' (trocar até {ChangeBy:dd/MM/yyyy})",
            userName, email, tempPassword, changeBy);
        return Task.CompletedTask;
    }

    public Task SendPasswordChangeReminderAsync(string email, string userName, DateTimeOffset wasDue, CancellationToken ct = default)
    {
        logger.LogInformation("[stub-email] Lembrete de troca de senha para {User} <{Email}> (venceu em {WasDue:dd/MM/yyyy})",
            userName, email, wasDue);
        return Task.CompletedTask;
    }

    public Task SendDiskLowAsync(IEnumerable<string> adminEmails, double freePercent, double freeGB, double totalGB, CancellationToken ct = default)
    {
        logger.LogInformation("[stub-email] Alerta de disco baixo para [{Admins}]: {Pct:F0}% livre ({Free:F1}/{Total:F1} GB)",
            string.Join(", ", adminEmails), freePercent, freeGB, totalGB);
        return Task.CompletedTask;
    }
}
