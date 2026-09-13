using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sintro.ResultViewer.Security;

namespace Sintro.ResultViewer.Tests;

/// <summary>
/// The shipped appsettings.jsonc carries an explanation of every setting, because an operator
/// editing it on a range PC has no documentation to hand. .NET's JSON configuration provider
/// reads with CommentHandling.Skip and AllowTrailingCommas, so it parses the file by design
/// rather than by luck — and the .jsonc extension tells the operator's editor the same.
///
/// These tests prove that rather than assuming it, and catch the real risk: a stray edit that
/// makes the file unparseable would otherwise only be discovered when the service refuses to
/// start on the range PC.
/// </summary>
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
        Assert.Contains("//", File.ReadAllText(Path));   // it really does carry comments

        var configuration = Load();
        Assert.NotNull(configuration.GetConnectionString("Sintro"));
    }

    [Fact]
    public void itBindsToTheOptionsTheApplicationActuallyUses()
    {
        var options = new SintroOptions();
        Load().GetSection(SintroOptions.SectionName).Bind(options);

        // Shipped empty on purpose: the operator must supply a token, and startup refuses
        // without one rather than running open.
        Assert.Empty(options.AllTokens);

        // Unset means "use the host's timezone".
        Assert.Null(options.TimeZone);
        Assert.Null(options.ReferenceDate);
        Assert.True(options.DefaultPageSize > 0);
    }

    [Fact]
    public void theAllowlistsAreSpeltOutRatherThanDefaultedInCode()
    {
        // The operator must be able to see and change who may connect. A default applied
        // invisibly in code is one they cannot review or turn off.
        var options = new SintroOptions();
        Load().GetSection(SintroOptions.SectionName).Bind(options);

        Assert.NotNull(options.Network.Api);
        Assert.NotNull(options.Network.Web);
        Assert.NotEmpty(options.Network.Api!);
        Assert.NotEmpty(options.Network.Web!);

        // And what ships is private-only, so an out-of-the-box install warns about nothing.
        Assert.Empty(NetworkGateExtensions.DescribePublicExposure(options));
    }

    [Fact]
    public void theDefaultsInTheFileMatchTheDefaultsInCode()
    {
        // Otherwise the documented file quietly disagrees with what the code does when a key
        // is absent, which is the sort of thing nobody notices until it matters.
        var fromFile = new SintroOptions();
        Load().GetSection(SintroOptions.SectionName).Bind(fromFile);

        var fromCode = new SintroOptions();

        Assert.Equal(fromCode.DefaultPageSize, fromFile.DefaultPageSize);
        Assert.Equal(fromCode.MaxPageSize, fromFile.MaxPageSize);
        Assert.Equal(fromCode.Live.PollMilliseconds, fromFile.Live.PollMilliseconds);
    }

    /// <summary>
    /// The file is only useful if the running application actually reads it — registering a
    /// .jsonc is a hand-written step, not something the framework does for us.
    /// </summary>
    [Collection(ApiCollection.Name)]
    public class Registration(ApiFixture fixture)
    {
        [Fact]
        public void theRunningApplicationLoadsTheJsoncFile()
        {
            var configuration = fixture.Services
                .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

            // A value that exists only in appsettings.jsonc — no code default supplies it.
            Assert.Equal("Warning", configuration["Logging:LogLevel:Microsoft.AspNetCore"]);
        }
    }
}
