using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sintro.ResultViewer.Security;

namespace Sintro.ResultViewer.Tests;

/// <summary>The shipped appsettings.jsonc must parse with its comments and bind to the options the code uses.</summary>
public class AppSettingsTests
{
    private static readonly string Path =
        System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.jsonc");

    private static IConfigurationRoot Load() =>
        new ConfigurationBuilder().AddJsonFile(Path, optional: false).Build();

    [Fact]
    public void theShippedFileParsesWithCommentsIntact()
    {
        Assert.True(File.Exists(Path), $"{Path} was not published alongside the tests");
        Assert.Contains("//", File.ReadAllText(Path));

        var configuration = Load();
        Assert.NotNull(configuration.GetConnectionString("Sintro"));
    }

    [Fact]
    public void itBindsToTheOptionsTheApplicationActuallyUses()
    {
        var options = new SintroOptions();
        Load().GetSection(SintroOptions.SectionName).Bind(options);

        Assert.Empty(options.AllTokens);
        Assert.Null(options.TimeZone);
        Assert.Null(options.ReferenceDate);
        Assert.True(options.DefaultPageSize > 0);
    }

    [Fact]
    public void theAllowlistsAreSpeltOutRatherThanDefaultedInCode()
    {
        var options = new SintroOptions();
        Load().GetSection(SintroOptions.SectionName).Bind(options);

        Assert.NotNull(options.Network.Api);
        Assert.NotNull(options.Network.Web);
        Assert.NotEmpty(options.Network.Api!);
        Assert.NotEmpty(options.Network.Web!);

        // Shipped private-only, so a fresh install warns about nothing.
        Assert.Empty(NetworkGateExtensions.DescribePublicExposure(options));
    }

    [Fact]
    public void theDefaultsInTheFileMatchTheDefaultsInCode()
    {
        var fromFile = new SintroOptions();
        Load().GetSection(SintroOptions.SectionName).Bind(fromFile);

        var fromCode = new SintroOptions();

        Assert.Equal(fromCode.DefaultPageSize, fromFile.DefaultPageSize);
        Assert.Equal(fromCode.MaxPageSize, fromFile.MaxPageSize);
        Assert.Equal(fromCode.Live.PollMilliseconds, fromFile.Live.PollMilliseconds);
    }

    /// <summary>Registering a .jsonc is a hand-written step in Program.cs, not something the framework does.</summary>
    [Collection(ApiCollection.Name)]
    public class Registration(ApiFixture fixture)
    {
        [Fact]
        public void theRunningApplicationLoadsTheJsoncFile()
        {
            var configuration = fixture.Services
                .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

            // A value that exists only in appsettings.jsonc; no code default supplies it.
            Assert.Equal("Warning", configuration["Logging:LogLevel:Microsoft.AspNetCore"]);
        }
    }
}
