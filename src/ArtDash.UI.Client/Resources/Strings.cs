namespace ArtDash.UI.Client.Resources;

/// <summary>
/// Marker for the UI texts in <c>Strings.resx</c> (neutral culture pt-BR). Use through
/// <c>IStringLocalizer&lt;Strings&gt;</c> — injected as <c>L</c> in every component — with dotted keys
/// (<c>L["Common.Save"]</c>, <c>L["Orders.Placed", number]</c>). Add a culture by adding
/// <c>Strings.{culture}.resx</c> with the same keys.
/// </summary>
public sealed class Strings
{
}

/// <summary>Localizer helpers.</summary>
public static class LocalizerExtensions
{
    /// <summary>The label of an enum value: key <c>Enum.{Type}.{Value}</c>.</summary>
    public static string Enum<TEnum>(this IStringLocalizer localizer, TEnum value)
        where TEnum : struct, System.Enum =>
        localizer[$"Enum.{typeof(TEnum).Name}.{value}"];

    /// <summary>The label of a nullable enum value, or <paramref name="empty"/> when null.</summary>
    public static string Enum<TEnum>(this IStringLocalizer localizer, TEnum? value, string empty = "—")
        where TEnum : struct, System.Enum =>
        value is { } v ? localizer.Enum(v) : empty;

    /// <summary>The label of a role name: key <c>Role.{Role}</c>.</summary>
    public static string Role(this IStringLocalizer localizer, string? role) =>
        string.IsNullOrWhiteSpace(role) ? string.Empty : localizer[$"Role.{role}"];
}
