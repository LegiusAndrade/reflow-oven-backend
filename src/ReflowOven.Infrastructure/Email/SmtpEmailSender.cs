using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ReflowOven.Infrastructure.Email;

/// <summary>Real SMTP sender (MailKit). Selected by <c>Email:Mode=Smtp</c>; defaults target Gmail (587/STARTTLS).</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _o = options.Value;

    public Task SendPasswordResetAsync(string email, string resetToken, CancellationToken ct = default) =>
        SendAsync(email, "Recuperação de senha — Reflow Oven",
            $"<p>Recebemos um pedido para redefinir sua senha. Use o código abaixo:</p>" +
            $"<p style=\"font-size:20px\"><b>{resetToken}</b></p><p>O código expira em 1 hora.</p>", ct);

    public Task SendNewUserAsync(string email, string userName, string tempPassword, DateTimeOffset changeBy, CancellationToken ct = default) =>
        SendAsync(email, "Bem-vindo ao Reflow Oven — sua senha de acesso",
            $"<p>Olá, <b>{userName}</b>!</p>" +
            $"<p>Sua conta foi criada. Use a senha abaixo no primeiro acesso:</p>" +
            $"<p style=\"font-size:20px\"><b>{tempPassword}</b></p>" +
            $"<p><b>Por segurança, troque sua senha no programa até {changeBy:dd/MM/yyyy}.</b></p>", ct);

    public Task SendPasswordChangeReminderAsync(string email, string userName, DateTimeOffset wasDue, CancellationToken ct = default) =>
        SendAsync(email, "Lembrete: troque sua senha — Reflow Oven",
            $"<p>Olá, <b>{userName}</b>!</p>" +
            $"<p>O prazo para trocar sua senha venceu em {wasDue:dd/MM/yyyy}. " +
            $"Acesse o programa e defina uma nova senha o quanto antes.</p>", ct);

    public async Task SendDiskLowAsync(IEnumerable<string> adminEmails, double freePercent, double freeGB, double totalGB, CancellationToken ct = default)
    {
        var body =
            $"<p><b>Atenção:</b> o espaço livre em disco do dispositivo está baixo.</p>" +
            $"<p>Livre: <b>{freePercent:F0}%</b> ({freeGB:F1} GB de {totalGB:F1} GB).</p>" +
            $"<p>Considere limpar dados antigos na tela de Manutenção.</p>";
        foreach (var to in adminEmails.Where(e => !string.IsNullOrWhiteSpace(e)))
            await SendAsync(to, "Espaço em disco crítico — Reflow Oven", body, ct);
    }

    private async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        var s = _o.Smtp;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_o.FromName, string.IsNullOrWhiteSpace(s.From) ? s.User : s.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(s.Host, s.Port, s.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, ct);
        if (!string.IsNullOrEmpty(s.User))
            await client.AuthenticateAsync(s.User, s.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
        logger.LogInformation("E-mail enviado para {To}: {Subject}", to, subject);
    }
}
