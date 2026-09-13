using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Sintro.ResultViewer.Data;

namespace Sintro.ResultViewer.Tests;

/// <summary>
/// Boots the real API against the seeded dev database (scripts/db-restore.sh).
/// "Today" is pinned to the backup's last shooting day so the today-only default has data.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<SintroRepository>
{
    public const string BackupDate = "2026-07-08";
    public const string Token = "integration-test-token-0123456789";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Sintro")
        ?? "Server=localhost,11433;Database=DBSINTRO300;User Id=sa;Password=Sintro_Dev_2026!;TrustServerCertificate=true;Encrypt=false";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Sintro", ConnectionString);
        builder.UseSetting("Sintro:ApiReadTokens:0", Token);
        builder.UseSetting("Sintro:ReferenceDate", BackupDate);
        builder.UseSetting("Sintro:TimeZone", "Europe/Zurich");
        builder.UseSetting("Sintro:MaxPageSize", "5000");
        // Keep the lane watcher from polling during tests.
        builder.UseSetting("Sintro:Live:PollMilliseconds", "600000");
    }

    public HttpClient CreateAuthorizedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Token}");
        return client;
    }

    public Task<bool> DatabaseReachableAsync() =>
        Services.GetRequiredService<SintroRepository>().CanReachDatabaseAsync(CancellationToken.None);

    /// <summary>
    /// A window wide enough to cover any export. Tests must not assume a particular shooting
    /// day — only the ReferenceDate-pinned default-window test does, and it derives it.
    /// </summary>
    public const string WholeRange = "from=2000-01-01&to=2100-12-31";

    /// <summary>
    /// A licence number taken from the loaded export, so tests never name a real shooter.
    /// Null when the export has no registered shooters at all.
    /// </summary>
    public async Task<string?> AnyLicenceAsync()
    {
        using var client = CreateAuthorizedClient();
        var page = await client.GetFromJsonAsync<Domain.CursorPage<Domain.Shooter>>(
            "/api/v2/shooters?limit=1", SintroJson.Options);

        return page?.Items.FirstOrDefault()?.License;
    }

    /// <summary>A club name from the loaded export, for exercising search without naming one.</summary>
    public async Task<string?> AnyClubNameAsync()
    {
        using var client = CreateAuthorizedClient();
        var page = await client.GetFromJsonAsync<Domain.CursorPage<Domain.Club>>(
            "/api/v2/clubs?limit=1", SintroJson.Options);

        return page?.Items.FirstOrDefault()?.Name;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

/// <summary>
/// Skips rather than fails when no database is reachable, so `dotnet test` outside the
/// compose environment still runs the pure unit tests.
/// </summary>
public sealed class RequiresDatabaseFactAttribute : FactAttribute
{
    public RequiresDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SINTRO_SKIP_DB_TESTS") == "1")
            Skip = "SINTRO_SKIP_DB_TESTS=1";
    }
}
