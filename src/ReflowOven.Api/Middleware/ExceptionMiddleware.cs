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
            await WriteProblemAsync(context, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro não tratado.");
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
