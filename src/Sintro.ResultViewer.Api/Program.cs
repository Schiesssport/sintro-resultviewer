using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using Sintro.ResultViewer;
using Sintro.ResultViewer.Data;
using Sintro.ResultViewer.Data.Sintro300;
using Sintro.ResultViewer.Api.V2;
using Sintro.ResultViewer.Live;
using Sintro.ResultViewer.Security;
using Sintro.ResultViewer.Viewer;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddOperatorSettings();

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
builder.Services.AddSingleton<ISintroRepository>(provider => new SintroRepository(
    provider.GetRequiredService<IConfiguration>().GetConnectionString("Sintro")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Sintro is not configured. See appsettings.jsonc."),
    provider.GetRequiredService<ISintroClock>()));
builder.Services.AddHostedService<LaneWatcher>();
builder.Services.AddOpenApi(V2Endpoints.Version);
builder.Services.AddCors();

var app = builder.Build();

var settings = app.Services.GetRequiredService<IOptions<SintroOptions>>().Value;
StartupChecks.Run(app.Logger, settings, app.Services.GetRequiredService<SessionToken>());

// Gate before token; UseWebSockets before UseTokenAuth, or ?token= on the handshake is never accepted.
app.UseNetworkGate();
// A browser on another origin (an event's own pages) may read the API: a preflight carries no token, so CORS answers it
// after the gate and before auth. The gate and the token remain the protection; the origin was never one.
// Only under /api: the viewer pages embed the session token, and the same-origin policy is what keeps it there.
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"),
    api => api.UseCors(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
app.UseWebSockets();
app.UseTokenAuth();

app.MapV2();
app.MapOpenApi();

app.MapViewer();
// Browsers cache heuristically; after an update a display would mix old and new modules until a hard reload.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

// Only after binding is a framework-chosen port known.
app.Lifetime.ApplicationStarted.Register(() => StartupBanner.LogReachableAddresses(
    app.Logger,
    app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
        ?? []));

app.Run();

public partial class Program;
