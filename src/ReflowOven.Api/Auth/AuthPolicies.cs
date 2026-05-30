namespace ReflowOven.Api.Auth;

/// <summary>Authorization policy names (mirroring the frontend's role/route guards).</summary>
public static class AuthPolicies
{
    /// <summary>Admin-only writes (user CRUD, program mutations, runs, settings, maintenance, …).</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>The hidden Calibração tab — requires the technician's "calibration" claim.</summary>
    public const string CalibrationOnly = "CalibrationOnly";
}
