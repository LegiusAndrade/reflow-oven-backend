namespace ReflowOven.Application.Abstractions;

/// <summary>
/// Credentials for a config-driven seeded account (name/email + the plaintext password the seeder hashes).
/// Like <see cref="IMasterCredentials"/> these come from configuration so the initial Admin/Regular logins
/// can be set per deployment via env (e.g. <c>Admin__Password</c>) instead of a hard-coded dev default.
/// The password is never returned in any DTO.
/// </summary>
public interface ISeedAccountCredentials
{
    string Username { get; }
    string Email { get; }
    /// <summary>Plaintext, hashed by the seeder when the account is first created. Never serialized.</summary>
    string Password { get; }
}

/// <summary>The initial Admin account (config section <c>Admin</c>). Seeded once; not re-synced afterwards.</summary>
public interface IAdminCredentials : ISeedAccountCredentials;

/// <summary>The initial Regular (operator) account (config section <c>Regular</c>). Seeded once.</summary>
public interface IRegularCredentials : ISeedAccountCredentials;
