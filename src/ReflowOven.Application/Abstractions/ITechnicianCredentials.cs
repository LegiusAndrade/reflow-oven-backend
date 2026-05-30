namespace ReflowOven.Application.Abstractions;

/// <summary>
/// The hidden "calibracao" technician credential (frontend default: calibracao / calibra). It is
/// NOT a User row — it lives in configuration and is verified here. Implemented in Infrastructure.
/// </summary>
public interface ITechnicianCredentials
{
    string Username { get; }
    bool Verify(string password);
}
