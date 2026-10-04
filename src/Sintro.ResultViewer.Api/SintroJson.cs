using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sintro.ResultViewer;

public static class SintroJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        // "shooter": null and "currentProgram": null are documented states; dropping the key would contradict the schema.
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;

        return options;
    }
}
