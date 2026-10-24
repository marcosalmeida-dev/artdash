namespace ArtDash.Domain.Common;

/// <summary>
/// A business rule was violated (e.g. cancelling a delivered order). The message is user-facing
/// and is returned by the API as a 422 problem detail.
/// </summary>
public sealed class DomainException(string message) : Exception(message)
{
}
