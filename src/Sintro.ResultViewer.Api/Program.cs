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

// ASPNETCORE_URLS is host configuration, which every JSON file outranks; an address given from outside must still win.
if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is { Length: > 0 } urlsFromHost)
    builder.Configuration["Urls"] = urlsFromHost;

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

// Gate before token; UseWebSockets before UseTokenAuth, or ?token= on the handshake is never accepted.
app.UseNetworkGate();
app.UseWebSockets();
app.UseTokenAuth();

app.MapV2();
app.MapOpenApi();

// no-store: the page carries the session token, which must not outlive the process on a shared PC.
IResult RenderPage(HttpContext context, ViewerPage page, string fileName)
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Content(page.Render(fileName, sessionToken.Value), "text/html; charset=utf-8");
}

// The fullscreen variants are client-side routes; the catch-all keeps a new one a front-end-only change.
foreach (var route in new[] { "/", "/index.html", "/fullscreen", "/fullscreen/{**variant}" })
    app.MapGet(route, (HttpContext context, ViewerPage page) => RenderPage(context, page, "index.html")).ExcludeFromDescription();

foreach (var route in new[] { "/docs", "/docs.html" })
    app.MapGet(route, (HttpContext context, ViewerPage page) => RenderPage(context, page, "docs.html")).ExcludeFromDescription();

app.UseStaticFiles();

// Only after binding is a framework-chosen port known.
app.Lifetime.ApplicationStarted.Register(() => StartupChecks.LogReachableAddresses(
    app.Logger,
    app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
        ?? []));

app.Run();

/// <summary>Registers appsettings.jsonc (.jsonc so editors accept the operator-facing comments).</summary>
static void AddOperatorSettings(ConfigurationManager configuration)
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
