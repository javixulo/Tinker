using System.Diagnostics;
using Tinker.Core;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.IntegrationTests;

/// <summary>
/// The child spans of a tool call (technical spec, section 3), with the real PDF reader, embeddings, transaction and index:
/// each meaningful step is a span under the span of the tool, and only an unexpected failure marks a step as failed.
/// </summary>
public sealed class TelemetrySpansTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    public async Task A_registration_has_a_span_for_reading_embedding_and_storing_under_its_own()
    {
        using var spans = new SpanRecorder();
        var manual = _env.Pdf("a.pdf", "To pair the dimmer hold the reset button for ten seconds.");

        var result = await _env.Register("Shelly", "Dimmer 2", manual);

        Assert.True(result.Ok);
        var tool = spans.Span("register_device");
        foreach (var step in new[] { "extract_manual", "embed_manuals", "run_transaction" })
        {
            Assert.Equal(tool.Id, spans.Span(step).ParentId);
        }

        Assert.True((int)spans.Span("embed_manuals").GetTagItem("chunks.count")! > 0);
    }

    [Fact]
    public async Task A_manual_that_cannot_be_read_is_an_error_type_on_its_step_but_not_a_failed_step()
    {
        using var spans = new SpanRecorder();
        File.WriteAllText(Path.Combine(_env.ManualsPath, "notes.txt"), "just text");

        await _env.Register("Acme", "Box", "notes.txt");

        var step = spans.Span("extract_manual");
        Assert.Equal("manual_unreadable", step.GetTagItem(Telemetry.ErrorType));
        Assert.NotEqual(ActivityStatusCode.Error, step.Status);
    }

    [Fact]
    public async Task A_transaction_that_fails_marks_its_step_as_failed_with_the_type_of_the_exception()
    {
        using var env = new TestEnvironment(wrapIndex: index => new ThrowingIndex(index));
        using var spans = new SpanRecorder();

        await env.Register("Acme", "Box", env.Pdf("a.pdf", "Alpha."));

        var step = spans.Span("run_transaction");
        Assert.Equal(ActivityStatusCode.Error, step.Status);
        Assert.NotNull(step.GetTagItem(Telemetry.ErrorType));
        Assert.Equal(ActivityStatusCode.Error, spans.Span("register_device").Status);
    }

    [Fact]
    public async Task A_search_has_a_span_for_the_index_under_its_own()
    {
        var registered = await _env.Register("Shelly", "Dimmer 2", _env.Pdf("a.pdf", "To pair the dimmer hold the reset button."));
        using var spans = new SpanRecorder();

        await _env.Searcher.SearchAsync(registered.Device!.DeviceId, "pair the dimmer", null);

        Assert.Equal(spans.Span("search_manuals").Id, spans.Span("search_index").ParentId);
    }
}
