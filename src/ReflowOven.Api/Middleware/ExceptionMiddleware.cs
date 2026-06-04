namespace ReflowOven.Api.Middleware;

/// <summary>Maps <see cref="AppException"/> to its HTTP status as ProblemDetails; everything else to 500.</summary>
public sealed class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            // Expected business errors (validation 400, conflict 409, …). They ARE handled here —
            // the client gets a clean ProblemDetails. Logged as a Warning, never an unhandled crash.
            logger.LogWarning("Requisição rejeitada ({Status}) em {Method} {Path}: {Detail}",
                ex.StatusCode, context.Request.Method, context.Request.Path, ex.Message);
            await WriteProblemAsync(context, ex.StatusCode, ex.Message);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected before the response finished (rapid navigation, a superseded fetch,
            // React StrictMode's double request). Not a server error — log it quietly (Debug, hidden at the
            // Information console level) and don't try to write a 500 to a socket that is already gone.
            logger.LogDebug("Requisição cancelada pelo cliente em {Method} {Path}.", context.Request.Method, context.Request.Path);
            if (!context.Response.HasStarted)
                context.Response.StatusCode = 499; // client closed request (nginx convention)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro não tratado em {Method} {Path}.", context.Request.Method, context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "Erro interno do servidor.");
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string detail)
    {
        if (context.Response.HasStarted) return;
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrase(status),
            Detail = detail,
        };
        await context.Response.WriteAsJsonAsync(problem);
    }

    private static string ReasonPhrase(int status) => status switch
    {
        400 => "Requisição inválida",
        401 => "Não autenticado",
        403 => "Acesso negado",
        404 => "Não encontrado",
        409 => "Conflito",
        _ => "Erro",
    };
}
