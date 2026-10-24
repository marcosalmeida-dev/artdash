using ArtDash.UI.Client.Auth;

namespace ArtDash.UI.Client.Pages.Account;

/// <summary>Turns a failed sign-in/sign-up into the text shown on the form.</summary>
internal static class AuthMessages
{
    public static string Describe(IStringLocalizer localizer, AuthOutcome outcome)
    {
        if (outcome.Errors is { Count: > 0 } errors)
        {
            return string.Join(" ", errors.SelectMany(e => e.Value).Distinct());
        }

        // Resource keys come from AuthSession (transport problems); anything else is the server's message.
        return outcome.Error is { } error && error.StartsWith("Auth.", StringComparison.Ordinal)
            ? localizer[error]
            : outcome.Error ?? localizer["Auth.Failed"];
    }
}
