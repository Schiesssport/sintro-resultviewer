using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Options;
using Sintro.ResultViewer;
using Sintro.ResultViewer.Data;
using Sintro.ResultViewer.Api.V2;
using Sintro.ResultViewer.Live;
using Sintro.ResultViewer.Security;
using Sintro.ResultViewer.Viewer;

var builder = WebApplication.CreateBuilder(args);

AddOperatorSettings(builder.Configuration);

// ASPNETCORE_URLS is host configuration, which every JSON file outranks — so the address in
// appsettings.jsonc would quietly beat the one a container or a service wrapper was started
// with, and the service would listen somewhere nobody was expecting. The file holds the
// default; an address given from outside it wins, which is the precedence every other setting
// here follows.
if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is { Length: > 0 } urlsFromHost)
    builder.Configuration["Urls"] = urlsFromHost;

// This window is read by whoever is running the event, so it is written for them: no logger
// category, no event id, and the address in a frame of its own. See OperatorConsole.cs.
builder.Logging.AddOperatorConsole();

builder.Services.Configure<SintroOptions>(builder.Configuration.GetSection(SintroOptions.SectionName));
builder.Services.ConfigureHttpJsonOptions(json => SintroJson.Configure(json.SerializerOptions));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISintroClock, SintroClock>();
builder.Services.AddSingleton<SessionToken>();
builder.Services.AddSingleton<LiveHub>();
builder.Services.AddSingleton<ViewerPage>();
builder.Services.AddSingleton(provider => new SintroRepository(
    provider.GetRequiredService<IConfiguration>().GetConnectionString("Sintro")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Sintro is not configured. See appsettings.jsonc."),
    provider.GetRequiredService<ISintroClock>()));
builder.Services.AddHostedService<LaneWatcher>();
builder.Services.AddOpenApi(V2Endpoints.Version);

var app = builder.Build();

var settings = app.Services.GetRequiredService<IOptions<SintroOptions>>().Value;
var sessionToken = app.Services.GetRequiredService<SessionToken>();
StartupChecks.Run(app.Logger, settings, sessionToken);

// Order matters. The network gate refuses a source before the token is even inspected.
// UseWebSockets must precede UseTokenAuth: it installs the feature that makes
// HttpContext.WebSockets.IsWebSocketRequest meaningful, and the token check accepts ?token=
// only on a genuine upgrade request — so with the order reversed every handshake is 401.
app.UseNetworkGate();
app.UseWebSockets();
app.UseTokenAuth();

app.MapV2();
app.MapOpenApi();

// The viewer's own token is injected at serve time, so it never touches disk. The .html
// aliases are mapped too, so the raw templates are never served by the static file handler.
// no-store because the page carries that token: a TV browser or a shared range PC must not keep
// a copy on disk that outlives the process it was minted for.
IResult RenderPage(HttpContext context, ViewerPage page, string fileName)
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Content(page.Render(fileName, sessionToken.Value), "text/html; charset=utf-8");
}

// The fullscreen variants (/fullscreen/live, /results, /leaderboard, /live+results) are
// client-side routes: the server hands out the same page and the viewer reads location.pathname.
// Making them real URLs means the back button works, each display can be bookmarked, and a TV
// browser can be pointed straight at the view it should show. A catch-all keeps new variants a
// front-end-only change.
foreach (var route in new[] { "/", "/index.html", "/fullscreen", "/fullscreen/{**variant}" })
    app.MapGet(route, (HttpContext context, ViewerPage page) => RenderPage(context, page, "index.html")).ExcludeFromDescription();

foreach (var route in new[] { "/docs", "/docs.html" })
    app.MapGet(route, (HttpContext context, ViewerPage page) => RenderPage(context, page, "docs.html")).ExcludeFromDescription();

app.UseStaticFiles();

// After the server has bound, not before: only then is the port known when one was left to the
// framework, and only then is it true that the addresses printed can be connected to.
app.Lifetime.ApplicationStarted.Register(() => StartupChecks.LogReachableAddresses(
    app.Logger,
    app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
        ?? []));

app.Run();

/// <summary>
/// Registers appsettings.jsonc, the single file an operator edits.
///
/// The .jsonc extension is the point: the file carries an explanation of every setting, because
/// the person editing it is standing at a range with no documentation to hand. .NET's JSON reader
/// skips comments either way, but only the extension tells their editor that, so a .json file
/// would show the explanations as errors.
/// </summary>
static void AddOperatorSettings(ConfigurationManager configuration)
{
    configuration.AddJsonFile("appsettings.jsonc", optional: true, reloadOnChange: false);

    // AddJsonFile appends, which would place the file after the environment variables and let it
    // silently beat an explicit override. Move it in among the framework's own JSON files so the
    // usual precedence holds: file, then environment, then command line.
    var added = configuration.Sources[^1];
    configuration.Sources.RemoveAt(configuration.Sources.Count - 1);

    var afterLastJsonFile = 0;
    for (var index = 0; index < configuration.Sources.Count; index++)
    {
        if (configuration.Sources[index] is JsonConfigurationSource) afterLastJsonFile = index + 1;
    }

    configuration.Sources.Insert(afterLastJsonFile, added);
}
