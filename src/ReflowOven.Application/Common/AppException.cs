namespace ReflowOven.Application.Common;

/// <summary>Application error carrying the HTTP status the API middleware should surface.</summary>
public class AppException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class NotFoundException(string message = "Recurso não encontrado.") : AppException(404, message);

public sealed class ConflictException(string message) : AppException(409, message);

public sealed class ValidationAppException(string message) : AppException(400, message);

public sealed class ForbiddenAppException(string message = "Acesso negado.") : AppException(403, message);
