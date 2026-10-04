using Microsoft.Extensions.Configuration.Json;

namespace Sintro.ResultViewer;

public static class OperatorSettings
{
    /// <summary>Registers appsettings.jsonc (.jsonc so editors accept the operator-facing comments).</summary>
    public static void AddOperatorSettings(this ConfigurationManager configuration)
    {
        configuration.AddJsonFile("appsettings.jsonc", optional: true, reloadOnChange: false);

        // AddJsonFile appends after the environment sources; move it among the JSON files so environment still wins.
        var added = configuration.Sources[^1];
        configuration.Sources.RemoveAt(configuration.Sources.Count - 1);

        var afterLastJsonFile = 0;
        for (var index = 0; index < configuration.Sources.Count; index++)
        {
            if (configuration.Sources[index] is JsonConfigurationSource) afterLastJsonFile = index + 1;
        }

        configuration.Sources.Insert(afterLastJsonFile, added);
    }
}
