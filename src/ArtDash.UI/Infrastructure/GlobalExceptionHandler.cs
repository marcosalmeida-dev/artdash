using ArtDash.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ArtDash.UI.Infrastructure;

/// <summary>
/// Turns every exception that escapes a request into an RFC 9457 problem detail, so the API has one
/// error shape: <c>{ type, title, status, detail, errors?, traceId }</c>. Handlers throw meaningful
/// exceptions; nothing in the controllers catches them.
/// </summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer.
            return true;
        }

        var problem = ToProblem(exception);
        var status = problem.Status ?? StatusCodes.Status500InternalServerError;

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogDebug("Request {Method} {Path} answered {Status}: {Message}",
                httpContext.Request.Method, httpContext.Request.Path, status, exception.Message);
        }

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails ToProblem(Exception exception) => exception switch
    {
        RequestValidationException validation => new HttpValidationProblemDetails(
            validation.Errors.ToDictionary(e => e.Key, e => e.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Dados inválidos",
            Detail = validation.Message
        },

        UnauthorizedAccessException => Problem(StatusCodes.Status401Unauthorized, "Não autenticado", exception.Message),
        ForbiddenAccessException => Problem(StatusCodes.Status403Forbidden, "Acesso negado", exception.Message),
        NotFoundException => Problem(StatusCodes.Status404NotFound, "Não encontrado", exception.Message),
        ConflictException => Problem(StatusCodes.Status409Conflict, "Conflito", exception.Message),

        DbUpdateConcurrencyException => Problem(StatusCodes.Status409Conflict, "Conflito",
            "Este registro foi alterado por outra pessoa. Recarregue a página e tente novamente."),

        DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } =>
            Problem(StatusCodes.Status409Conflict, "Registro duplicado", "Já existe um registro com estes dados."),

        DbUpdateException { InnerException: SqlException { Number: 547 } } =>
            Problem(StatusCodes.Status409Conflict, "Registro em uso", "Este registro está em uso e não pode ser alterado desta forma."),

        DomainException => Problem(StatusCodes.Status422UnprocessableEntity, "Operação não permitida", exception.Message),

        BadHttpRequestException badRequest => Problem(badRequest.StatusCode, "Requisição inválida", badRequest.Message),

        _ => Problem(StatusCodes.Status500InternalServerError, "Erro interno",
            "Ocorreu um erro inesperado. Tente novamente em instantes.")
    };

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
