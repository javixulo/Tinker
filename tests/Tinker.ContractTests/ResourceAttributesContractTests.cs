using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Tinker.ContractTests;

/// <summary>
/// Every span and log carries who sent it (technical spec, section 3): the name from <c>OTEL_SERVICE_NAME</c>, the version of the
/// build and the environment from <c>OTEL_RESOURCE_ATTRIBUTES</c>. That is how a shared backend tells projects, builds and environments apart.
/// </summary>
public sealed class ResourceAttributesContractTests : IDisposable
{
    private const string Variable = "OTEL_RESOURCE_ATTRIBUTES";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tinker-mcp-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Variable, _previous);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void The_resource_has_the_service_name_the_version_and_the_environment()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        Directory.CreateDirectory(Path.Combine(_root, "manuals"));
        Environment.SetEnvironmentVariable(Variable, "deployment.environment.name=staging");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("OTEL_SERVICE_NAME", "tinker-under-test");
            builder.UseSetting("Tinker:DataPath", Path.Combine(_root, "data"));
            builder.UseSetting("Tinker:ManualsPath", Path.Combine(_root, "manuals"));
            builder.UseSetting("Tinker:EmbeddingModel", "hash");
        });

        var attributes = factory.Services.GetRequiredService<TracerProvider>().GetResource().Attributes.ToDictionary(a => a.Key, a => a.Value);

        Assert.Equal("tinker-under-test", attributes["service.name"]);
        Assert.Equal("staging", attributes["deployment.environment.name"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)attributes["service.version"]));
    }
}
