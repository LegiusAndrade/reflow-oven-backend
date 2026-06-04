namespace ReflowOven.Api.Auth;

/// <summary>Authorization policy names (mirroring the frontend's role/route guards).</summary>
public static class AuthPolicies
{
    /// <summary>Admin-only writes (user CRUD, program mutations, runs, settings, maintenance, …).
    /// The dev <c>Master</c> superuser also satisfies this (inherits all Admin powers).</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>Admin ONLY — the dev <c>Master</c> does NOT satisfy this. For the few destructive actions the
    /// Master must not perform (deleting active users; the bulk "programas"/"usuários ativos" cleanup).</summary>
    public const string AdminStrict = "AdminStrict";

    /// <summary>Runs may be started/stopped by an operator (Regular), an Admin, or the Master.</summary>
    public const string OperatorOrAdmin = "OperatorOrAdmin";

    /// <summary>The dev <c>Master</c> superuser ONLY (Admins do NOT satisfy this). Guards the "trash":
    /// viewing/restoring/purging the soft-deleted records that ordinary screens hide.</summary>
    public const string MasterOnly = "MasterOnly";

    /// <summary>The hidden Calibração tab — requires the technician's "calibration" claim.</summary>
    public const string CalibrationOnly = "CalibrationOnly";
}
