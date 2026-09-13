using System.Collections.Concurrent;

namespace Sintro.ResultViewer.Viewer;

/// <summary>
/// Serves the viewer's HTML with the per-start session token substituted in. Keeping the token
/// out of wwwroot means it is never written to disk and never served as a static file.
/// </summary>
public sealed class ViewerPage(IWebHostEnvironment environment)
{
    private const string TokenPlaceholder = "{{SESSION_TOKEN}}";

    private readonly ConcurrentDictionary<string, string> _templates = new();

    public string Render(string fileName, string sessionToken)
    {
        var template = _templates.GetOrAdd(fileName, ReadTemplate);
        return template.Replace(TokenPlaceholder, sessionToken);
    }

    private string ReadTemplate(string fileName)
    {
        var file = environment.WebRootFileProvider.GetFileInfo(fileName);
        if (!file.Exists || file.PhysicalPath is null)
            throw new FileNotFoundException($"Viewer template '{fileName}' is missing from wwwroot.");

        return File.ReadAllText(file.PhysicalPath);
    }
}
