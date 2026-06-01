namespace ReflowOven.Application.Abstractions;

/// <summary>
/// The single dev <c>Master</c> superuser identity (frontend default: dev.pandewilly). Unlike the
/// technician, the Master IS a real <c>User</c> row (seeded), so this only supplies the name/email and
/// the plaintext password the seeder hashes — it is never returned in any DTO. Lives in configuration
/// (section <c>Master</c>); implemented in Infrastructure.
/// </summary>
public interface IMasterCredentials
{
    string Username { get; }
    string Email { get; }
    /// <summary>Plaintext, hashed by the seeder when creating/recreating the Master row. Never serialized.</summary>
    string Password { get; }
}
