using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sintro.ResultViewer;

/// <summary>
/// The one serializer configuration for everything this service emits: the HTTP endpoints, the
/// live WebSocket frames and the test client. The same program must read identically over REST
/// and over the socket ("active", never "Active"), so the options are defined once and shared
/// rather than kept in step by hand.
/// </summary>
public static class SintroJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        // States and reasons read better as names than as integers in a documented API; camelCase
        // matches the documented values ("active", "mixedValuation") and every other field.
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        // Nulls are written, not omitted. "shooter": null and "currentProgram": null are
        // meaningful states that the documentation promises; dropping the keys would force
        // every client to distinguish absent from empty, and would contradict the schema.
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;

        return options;
    }
}
