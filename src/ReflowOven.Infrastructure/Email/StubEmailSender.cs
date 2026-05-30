using Microsoft.Extensions.Logging;

namespace ReflowOven.Infrastructure.Email;

/// <summary>Logs the reset token instead of sending mail. Replace with a real SMTP sender. TODO.</summary>
public sealed class StubEmailSender(ILogger<StubEmailSender> logger) : IEmailSender
{
    public Task SendPasswordResetAsync(string email, string resetToken, CancellationToken ct = default)
    {
        logger.LogInformation("[stub] Recuperação de senha para {Email}: token {Token}", email, resetToken);
        return Task.CompletedTask;
    }
}
