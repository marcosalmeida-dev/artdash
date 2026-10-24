using FluentValidation.Results;

namespace ArtDash.Application.Common.Exceptions;

// Every exception here is translated to an RFC 9457 problem detail by the API's exception handler:
//   RequestValidationException -> 400, ForbiddenAccessException -> 403, NotFoundException -> 404,
//   ConflictException -> 409, DomainException (Domain) -> 422.

public sealed class NotFoundException(string entityName, object key)
    : Exception($"{entityName} não encontrado(a).")
{
    public string EntityName { get; } = entityName;
    public object Key { get; } = key;
}

public sealed class ForbiddenAccessException(string? message = null)
    : Exception(message ?? "Você não tem permissão para esta operação.")
{
}

public sealed class ConflictException(string message) : Exception(message)
{
}

public sealed class RequestValidationException : Exception
{
    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("Um ou mais campos são inválidos.")
    {
        Errors = errors;
    }

    public RequestValidationException(string property, string message)
        : this(Single(property, message))
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    private static Dictionary<string, string[]> Single(string property, string message) =>
        new() { { property, new[] { message } } };

    public static RequestValidationException From(IEnumerable<ValidationFailure> failures)
    {
        var errors = failures
            .GroupBy(f => CleanPropertyName(f.PropertyName), f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.Distinct().ToArray());

        return new RequestValidationException(errors);
    }

    // Commands wrap the request DTO ("Request.Name"); the client only cares about "Name".
    private static string CleanPropertyName(string propertyName) =>
        propertyName.StartsWith("Request.", StringComparison.Ordinal) ? propertyName["Request.".Length..] : propertyName;
}
