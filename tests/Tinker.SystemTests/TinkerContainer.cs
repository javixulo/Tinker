using System.Diagnostics;
using ModelContextProtocol.Client;
using Xunit;

namespace Tinker.SystemTests;

/// <summary>
/// The Docker image running as a container, with its own data and manuals volumes, as the compose file runs it: as a
/// non-root user, with the manuals read-only. It is driven with the <c>docker</c> command line. The tests are skipped
/// unless <c>TINKER_SYSTEM_IMAGE</c> names the image to test.
/// </summary>
public sealed class TinkerContainer : IAsyncDisposable
{
    public const string ImageVariable = "TINKER_SYSTEM_IMAGE";
    private const string User = "1000:1000";

    private readonly string _name = "tinker-system-" + Guid.NewGuid().ToString("N")[..12];
    private readonly string _image;
    private McpClient? _client;

    private TinkerContainer(string image) => _image = image;

    private string DataVolume => _name + "-data";

    private string ManualsVolume => _name + "-manuals";

    public McpClient Client => _client ?? throw new InvalidOperationException("The container is not started.");

    /// <summary>Skips the calling test unless an image to test was given.</summary>
    public static string RequireImage()
    {
        var image = Environment.GetEnvironmentVariable(ImageVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(image), $"Set {ImageVariable} to the image to test (for example tinker-mcp:latest).");
        return image!;
    }

    /// <summary>Starts the image with the sample manuals and waits until it answers <c>/health</c>.</summary>
    public static async Task<TinkerContainer> StartAsync()
    {
        var container = new TinkerContainer(RequireImage());
        try
        {
            await container.PrepareVolumesAsync();
            await Docker("start", container._name);
            await container.ConnectAsync();
            return container;
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Runs the image in the foreground with a data folder that its user cannot write to (a new volume belongs to root)
    /// and returns how it ended: a good image stops by itself and says why.
    /// </summary>
    public static async Task<(int ExitCode, string Output)> RunWithUnwritableDataAsync()
    {
        var container = new TinkerContainer(RequireImage());
        try
        {
            await Docker("volume", "create", container.DataVolume);
            await Docker("volume", "create", container.ManualsVolume);
            var result = await DockerRaw("run", "--rm", "--name", container._name, "--user", User,
                "-v", $"{container.DataVolume}:/data", "-v", $"{container.ManualsVolume}:/manuals:ro", container._image);
            return (result.ExitCode, result.Output);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    /// <summary>Restarts the container (same volumes) and reconnects: the published port can change.</summary>
    public async Task RestartAsync()
    {
        await DisposeClientAsync();
        await Docker("restart", _name);
        await ConnectAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeClientAsync();
        await DockerRaw("rm", "-f", _name);
        await DockerRaw("volume", "rm", "-f", DataVolume, ManualsVolume);
    }

    private async Task PrepareVolumesAsync()
    {
        await Docker("volume", "create", DataVolume);
        await Docker("volume", "create", ManualsVolume);

        // A new volume belongs to root: give the data folder to the user of the container, with the image itself.
        await Docker("run", "--rm", "--user", "0", "--entrypoint", "chown", "-v", $"{DataVolume}:/data", _image, User, "/data");

        // The manuals are mounted read-only in the container, so they are copied through a container that is never started.
        var helper = _name + "-copy";
        await Docker("create", "--name", helper, "-v", $"{ManualsVolume}:/manuals", _image);
        try
        {
            await Docker("cp", Path.Combine(AppContext.BaseDirectory, "fixtures", "manuals") + "/.", helper + ":/manuals");
        }
        finally
        {
            await DockerRaw("rm", "-f", helper);
        }

        await Docker("create", "--name", _name, "--user", User, "-p", "127.0.0.1::8080",
            "-v", $"{DataVolume}:/data", "-v", $"{ManualsVolume}:/manuals:ro", _image);
    }

    private async Task ConnectAsync()
    {
        var port = await WaitForHealthAsync();
        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress, "/mcp") }, http);
        _client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    private async Task<string> WaitForHealthAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (!string.Equals(await Docker("inspect", "-f", "{{.State.Running}}", _name), "true", StringComparison.Ordinal))
            {
                var logs = await DockerRaw("logs", _name);
                throw new InvalidOperationException($"The container stopped by itself:\n{logs.Output}");
            }

            var mapping = (await DockerRaw("port", _name, "8080/tcp")).Output.Split('\n')[0].Trim();
            if (mapping.Length > 0)
            {
                var port = mapping[(mapping.LastIndexOf(':') + 1)..];
                try
                {
                    var response = await http.GetAsync($"http://127.0.0.1:{port}/health", TestContext.Current.CancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return port;
                    }
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException)
                {
                }
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        var last = await DockerRaw("logs", "--tail", "30", _name);
        throw new TimeoutException($"/health did not answer within 90 seconds. Last logs:\n{last.Output}");
    }

    private async Task DisposeClientAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }

    private static async Task<string> Docker(params string[] arguments)
    {
        var (exitCode, output) = await DockerRaw(arguments);
        return exitCode == 0
            ? output.Trim()
            : throw new InvalidOperationException($"docker {string.Join(' ', arguments)} failed ({exitCode}):\n{output}");
    }

    private static async Task<(int ExitCode, string Output)> DockerRaw(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("docker could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(CancellationToken.None);
        return (process.ExitCode, await output + await error);
    }
}
