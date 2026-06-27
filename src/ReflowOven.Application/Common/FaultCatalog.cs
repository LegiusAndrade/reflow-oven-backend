namespace ReflowOven.Application.Common;

/// <summary>
/// Resolves a board E-code to its catalogued severity + message, built once from the same
/// <see cref="Defaults.FaultTypes"/> source the database is seeded from — so a fault surfaced live (the
/// Diagnóstico tick) or recorded for a run stays in lock-step with the Diagnóstico/Relatórios catalog.
/// An unknown/uncatalogued code is treated as Crítico so a real fault is never under-reported (the RS422
/// driver already normalises every fault bit to a catalogued code).
/// </summary>
public static class FaultCatalog
{
    private static readonly IReadOnlyDictionary<string, FaultType> ByCode =
        Defaults.FaultTypes().ToDictionary(f => f.Code);

    /// <summary>The catalogued severity + message for an E-code (Crítico fallback for an unknown code).</summary>
    public static (ErrorSeverity Severity, string Message) Resolve(string code) =>
        ByCode.TryGetValue(code, out var ft)
            ? (ft.Severity, ft.Message)
            : (ErrorSeverity.Critico, $"Falha não catalogada da placa de potência ({code}).");
}
