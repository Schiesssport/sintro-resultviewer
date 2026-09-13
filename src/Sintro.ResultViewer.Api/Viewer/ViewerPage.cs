using System.Collections.Concurrent;

namespace Sintro.ResultViewer.Viewer;

/// <summary>Serves the viewer's HTML with the session token substituted at serve time, so the token never touches disk.</summary>
public sealed class ViewerPage(IWebHostEnvironment environment)
{
    private const string TokenPlaceholder = "{{SESSION_TOKEN}}";

    private readonly ConcurrentDictionary<string, string> _templates = new();

    public string Render(string fileName, string sessionToken) =>
        _templates.GetOrAdd(fileName, ReadTemplate).Replace(TokenPlaceholder, sessionToken);

    private string ReadTemplate(string fileName)
    {
        var file = environment.WebRootFileProvider.GetFileInfo(fileName);
        if (!file.Exists || file.PhysicalPath is null)
            throw new FileNotFoundException($"Viewer template '{fileName}' is missing from wwwroot.");

        return File.ReadAllText(file.PhysicalPath);
    }
}
