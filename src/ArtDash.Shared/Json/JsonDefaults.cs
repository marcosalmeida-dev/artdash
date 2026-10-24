using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArtDash.Shared.Json;

/// <summary>
/// The one JSON contract used by the API, SignalR and the Blazor client: camelCase names and
/// enums as strings, so payloads stay readable and adding an enum member never shifts values.
/// </summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = Create();

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Apply(options);
        return options;
    }

    /// <summary>Applies the conventions to an options instance owned by a framework (MVC, SignalR).</summary>
    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;

        if (!options.Converters.OfType<JsonStringEnumConverter>().Any())
        {
            options.Converters.Add(new JsonStringEnumConverter());
        }
    }
}
