using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ArtDash.Application.Common.Behaviors;

/// <summary>Logs every request with its duration and flags the slow ones.</summary>
internal sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private const int SlowRequestThresholdMs = 750;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > SlowRequestThresholdMs)
            {
                logger.LogWarning("Slow request {RequestName} took {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                logger.LogDebug("Handled {RequestName} in {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
            }

            return response;
        }
        catch (Exception ex) when (ex is not (DomainException or RequestValidationException or NotFoundException
                                       or ForbiddenAccessException or ConflictException or OperationCanceledException))
        {
            logger.LogError(ex, "Request {RequestName} failed after {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}

/// <summary>
/// Runs every FluentValidation validator registered for the request before the handler.
/// (artstore-fs registered validators but never executed them; here it is part of the pipeline.)
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var validatorList = validators as IValidator<TRequest>[] ?? validators.ToArray();
        if (validatorList.Length == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validatorList.Select(v => v.ValidateAsync(context, cancellationToken)));
        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();

        if (failures.Count > 0)
        {
            throw RequestValidationException.From(failures);
        }

        return await next();
    }
}
