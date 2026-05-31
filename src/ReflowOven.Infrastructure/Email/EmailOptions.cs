namespace ReflowOven.Infrastructure.Email;

/// <summary>Email config (<c>Email</c> section). <c>Mode=Stub</c> logs; <c>Mode=Smtp</c> sends via MailKit.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary><c>Stub</c> (default — logs to console) or <c>Smtp</c> (real send).</summary>
    public string Mode { get; set; } = "Stub";

    /// <summary>Display name on the "From" header.</summary>
    public string FromName { get; set; } = "Reflow Oven";

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;

    /// <summary>SMTP user (for Gmail, the full address). Empty = no authentication.</summary>
    public string User { get; set; } = "";

    /// <summary>SMTP password — for Gmail use an <b>App Password</b>. Keep it out of git (user-secrets / env).</summary>
    public string Password { get; set; } = "";

    /// <summary>From address; falls back to <see cref="User"/> when empty.</summary>
    public string From { get; set; } = "";

    /// <summary>STARTTLS on the submission port (587). Set false to auto-negotiate.</summary>
    public bool UseStartTls { get; set; } = true;
}
