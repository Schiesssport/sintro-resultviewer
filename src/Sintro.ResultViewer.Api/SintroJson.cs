using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sintro.ResultViewer;

/// <summary>The one serializer configuration for the endpoints, the live frames and the tests.</summary>
public static class SintroJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        // Enums as documented camelCase names ("active", "mixedValuation"), never integers.
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        // "shooter": null and "currentProgram": null are documented states; dropping the key would contradict the schema.
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;

        return options;
    }
}
