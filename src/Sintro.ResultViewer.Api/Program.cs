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

var app = builder.Build();

var settings = app.Services.GetRequiredService<IOptions<SintroOptions>>().Value;
StartupChecks.Run(app.Logger, settings, app.Services.GetRequiredService<SessionToken>());

// Gate before token; UseWebSockets before UseTokenAuth, or ?token= on the handshake is never accepted.
app.UseNetworkGate();
app.UseWebSockets();
app.UseTokenAuth();

app.MapV2();
app.MapOpenApi();

app.MapViewer();
app.UseStaticFiles();

// Only after binding is a framework-chosen port known.
app.Lifetime.ApplicationStarted.Register(() => StartupBanner.LogReachableAddresses(
    app.Logger,
    app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
        ?? []));

app.Run();

public partial class Program;
