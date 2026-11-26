using Microsoft.Extensions.DependencyInjection;
using Tinker.Core.Manuals;
using Tinker.Core.Storage;
using Tinker.Mcp;
using Tinker.Mcp.Startup;
using Xunit;

namespace Tinker.ContractTests;

/// <summary>The startup validation of the manuals folder (technical spec, section 4).</summary>
public sealed class ManualsFolderStartupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tinker-mcp-" + Guid.NewGuid().ToString("N"));

    private string Manuals => Path.Combine(_root, "manuals");

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        if (!OperatingSystem.IsWindows() && Directory.Exists(Manuals))
        {
            File.SetUnixFileMode(Manuals, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Directory.Delete(_root, recursive: true);
    }

    // Catches: a container without the group of the manuals folder starts fine and later answers manual_not_found for every manual.
    [Theory]
    [InlineData(UnixFileMode.None)] // cannot be listed
    [InlineData(UnixFileMode.UserRead)] // lists the names but cannot open what is inside
    [InlineData(UnixFileMode.UserExecute)] // opens what is inside but cannot be listed
    [InlineData(UnixFileMode.UserRead, true)] // the same, with nothing inside to try
    public void A_manuals_folder_the_user_cannot_read_stops_the_start_and_says_how_to_fix_it(UnixFileMode mode, bool empty = false)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Needs Unix permissions.");
        if (OperatingSystem.IsWindows())
        {
            return; // for the platform analyzer: the line above already skipped
        }

        Assert.SkipWhen(Environment.IsPrivilegedProcess, "root reads everything, whatever the permissions say.");
        Directory.CreateDirectory(Manuals);
        if (!empty)
        {
            File.WriteAllText(Path.Combine(Manuals, "a.pdf"), "x");
        }

        File.SetUnixFileMode(Manuals, mode);

        var problem = Assert.Single(Validate(), p => p.Contains("manuals folder", StringComparison.Ordinal));

        Assert.Contains("cannot be read", problem, StringComparison.Ordinal);
        Assert.Contains("group_add", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_manuals_folder_is_not_a_problem()
    {
        Directory.CreateDirectory(Manuals);

        Assert.DoesNotContain(Validate(), p => p.Contains("manuals folder", StringComparison.Ordinal));
    }

    private IReadOnlyList<string> Validate()
    {
        var options = new TinkerOptions
        {
            DataPath = Path.Combine(_root, "data"),
            ManualsPath = Manuals,
            // The folder has no model, so nothing tries to load it.
            EmbeddingModelPath = Path.Combine(_root, "no-model"),
        };
        var database = new SqliteDatabase(options.DatabasePath);
        using var services = new ServiceCollection()
            .AddSingleton<IManualsIndex>(_ => throw new InvalidOperationException("Not needed."))
            .BuildServiceProvider();

        return StartupValidation.Validate(options, database, services);
    }
}
