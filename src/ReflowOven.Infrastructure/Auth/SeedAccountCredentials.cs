using Microsoft.Extensions.Options;

namespace ReflowOven.Infrastructure.Auth;

/// <summary>The initial Admin account (config section <c>Admin</c>). Set real creds via <c>Admin__*</c> in prod.</summary>
public sealed class AdminOptions
{
    public const string Section = "Admin";
    public string Username { get; set; } = "lucas.silva";
    public string Password { get; set; } = "reflow1234";
    public string Email { get; set; } = "lucas@reflow.local";
}

/// <summary>The initial Regular operator account (config section <c>Regular</c>). Set real creds via <c>Regular__*</c>.</summary>
public sealed class RegularOptions
{
    public const string Section = "Regular";
    public string Username { get; set; } = "vanessa";
    public string Password { get; set; } = "reflow1234";
    public string Email { get; set; } = "vanessa@reflow.local";
}

public sealed class AdminCredentials(IOptions<AdminOptions> options) : IAdminCredentials
{
    public string Username => options.Value.Username;
    public string Email => options.Value.Email;
    public string Password => options.Value.Password;
}

public sealed class RegularCredentials(IOptions<RegularOptions> options) : IRegularCredentials
{
    public string Username => options.Value.Username;
    public string Email => options.Value.Email;
    public string Password => options.Value.Password;
}
