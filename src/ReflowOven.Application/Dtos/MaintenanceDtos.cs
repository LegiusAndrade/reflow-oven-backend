namespace ReflowOven.Application.Dtos;

public sealed record CategorySizeDto(CleanupId Id, string Label, int Count, long Bytes);

public sealed record DatabaseSizeDto(long TotalBytes, IReadOnlyList<CategorySizeDto> Categories);

public sealed record MaintenanceOverviewDto(
    DatabaseSizeDto Database,
    double DiskFreeGB,
    double DiskTotalGB,
    string Os,
    string OsKernel);

public sealed record CleanupRequest(IReadOnlyList<CleanupId> Categories);

public sealed record CleanupResultDto(int Deleted);

/// <summary>Type-to-confirm factory reset (Confirm must equal "RESETAR").</summary>
public sealed record FactoryResetRequest(string Confirm);
