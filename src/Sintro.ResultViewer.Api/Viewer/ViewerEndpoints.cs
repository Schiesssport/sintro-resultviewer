using Sintro.ResultViewer.Security;

namespace Sintro.ResultViewer.Viewer;

public static class ViewerEndpoints
{
    private static readonly string[] LiveRoutes = ["/", "/index.html", "/fullscreen/live", "/fullscreen/results", "/fullscreen/live+results"];
    private static readonly string[] DocsRoutes = ["/docs", "/docs.html"];
    private static readonly string[] BrowseRoutes = ["/browse", "/browse.html"];

    // The fullscreen variants are client-side routes, listed so an unknown one is a 404, not a guess.
    public static void MapViewer(this WebApplication app)
    {
        MapPage(app, LiveRoutes, "index.html");
        MapPage(app, DocsRoutes, "docs.html");
        MapPage(app, BrowseRoutes, "browse.html");
    }

    private static void MapPage(WebApplication app, string[] routes, string fileName)
    {
        foreach (var route in routes)
            app.MapGet(route, (HttpContext context, ViewerPage page, SessionToken token) => Render(context, page, token, fileName))
               .ExcludeFromDescription();
    }

    // no-store: the page carries the session token, which must not outlive the process on a shared PC.
    private static IResult Render(HttpContext context, ViewerPage page, SessionToken token, string fileName)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Content(page.Render(fileName, token.Value), "text/html; charset=utf-8");
    }
}
