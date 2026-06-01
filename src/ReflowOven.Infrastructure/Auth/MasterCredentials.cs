using Microsoft.Extensions.Options;

namespace ReflowOven.Infrastructure.Auth;

public sealed class MasterOptions
{
    public const string Section = "Master";

    /// <summary>Dev placeholder: dev.pandewilly / pandewilly. Override the password (real secret) in
    /// production via <c>Master__Password</c> — Program.cs fails fast if left on the placeholder outside Development.</summary>
    public string Username { get; set; } = "dev.pandewilly";
    public string Password { get; set; } = "pandewilly";
    public string Email { get; set; } = "master@reflow.local";
}

public sealed class MasterCredentials(IOptions<MasterOptions> options) : IMasterCredentials
{
    public string Username => options.Value.Username;
    public string Email => options.Value.Email;
    public string Password => options.Value.Password;
}
