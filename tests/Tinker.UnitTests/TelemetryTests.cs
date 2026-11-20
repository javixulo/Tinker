using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tinker.Core;
using Tinker.Core.Finding;
using Tinker.Core.Models;
using Tinker.Core.Registration;
using Tinker.Core.Removal;
using Tinker.Core.Searching;
using Xunit;

namespace Tinker.UnitTests;

/// <summary>
/// What the services record in their spans and logs (technical spec, section 3): the attributes of each tool call, how an
/// outcome is told apart (a rejection is not a failure), and that the trace id in a <c>failed</c> message is the one of the span.
/// </summary>
public sealed class TelemetryTests
{
    private readonly FakeManualFiles _files = new();
    private readonly FakeInventory _inventory = new();
    private readonly FakeIndex _index = new();
    private readonly FakeTransactions _transactions = new();
    private readonly ListLogger<DeviceRegistrar> _registrarLog = new();

    private DeviceRegistrar Registrar() => new(_files, _inventory, _index, _transactions, _files, Embedder.Hash, _registrarLog);

    private static string[] ErrorTypes(Activity span) => (string[])span.GetTagItem(Telemetry.ErrorType)!;

    [Fact]
    public async Task A_registration_records_the_device_it_created_and_no_error_type()
    {
        using var spans = new SpanRecorder();

        await Registrar().RegisterAsync(new Device { Brand = "Shelly", Model = "Dimmer 2" });

        var span = spans.Span("register_device");
        Assert.Equal("Shelly", span.GetTagItem(Telemetry.DeviceBrand));
        Assert.Equal("Dimmer 2", span.GetTagItem(Telemetry.DeviceModel));
        Assert.Equal(1L, span.GetTagItem(Telemetry.DeviceId));
        Assert.Null(span.GetTagItem(Telemetry.ErrorType));
        Assert.NotEqual(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public async Task A_rejected_registration_records_its_error_codes_without_marking_the_step_as_failed()
    {
        using var spans = new SpanRecorder();

        await Registrar().RegisterAsync(new Device { Brand = "Shelly", Model = " " });

        var span = spans.Span("register_device");
        Assert.Equal([ErrorCodes.InvalidFields], ErrorTypes(span));
        Assert.NotEqual(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public async Task An_unexpected_failure_marks_the_span_as_error_and_the_message_carries_the_trace_id_of_the_span()
    {
        _index.Fails = new InvalidOperationException("index broken");
        using var spans = new SpanRecorder();

        var result = await Registrar().RegisterAsync(new Device { Brand = "Shelly", Model = "X" });

        var span = spans.Span("register_device");
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal([ErrorCodes.Failed], ErrorTypes(span));
        Assert.Contains($"Trace id: {span.TraceId}.", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
        Assert.Equal(spans.TraceId, span.TraceId);
    }

    [Fact]
    public async Task The_log_lines_of_a_call_are_written_inside_its_trace()
    {
        using var spans = new SpanRecorder();

        await Registrar().RegisterAsync(new Device { Brand = "Shelly", Model = "Dimmer 2" });

        Assert.Equal(spans.TraceId, _registrarLog.TraceOf("Device 1 registered"));
    }

    [Fact]
    public async Task A_search_records_the_device_and_the_query_text()
    {
        _inventory.Seed(new Device { Brand = "Shelly", Model = "Dimmer 2", Manuals = ["a.pdf"] });
        _index.Results = [new Chunk("Reset it", 1, "a.pdf", 3)];
        using var spans = new SpanRecorder();

        await new ManualSearcher(_inventory, _index, new ListLogger<ManualSearcher>()).SearchAsync(1, "how to reset", null);

        var span = spans.Span("search_manuals");
        Assert.Equal(1L, span.GetTagItem(Telemetry.DeviceId));
        Assert.Equal("how to reset", span.GetTagItem(Telemetry.SearchQuery));
        Assert.Equal(1, span.GetTagItem(Telemetry.Matches));
    }

    [Fact]
    public async Task A_find_records_how_many_devices_matched()
    {
        _inventory.Seed(new Device { Brand = "Shelly", Model = "A" });
        _inventory.Seed(new Device { Brand = "Shelly", Model = "B" });
        using var spans = new SpanRecorder();

        await new DeviceFinder(_inventory, new ListLogger<DeviceFinder>()).FindAsync(new Device { Brand = "Shelly" });

        Assert.Equal(2, spans.Span("find_devices").GetTagItem(Telemetry.Matches));
    }

    [Fact]
    public async Task A_deletion_records_the_device_it_removed()
    {
        _inventory.Seed(new Device { Brand = "Shelly", Model = "A" });
        using var spans = new SpanRecorder();

        await new DeviceRemover(_inventory, _index, _transactions, new ListLogger<DeviceRemover>()).DeleteAsync(new Device { Brand = "Shelly", Model = "A" });

        Assert.Equal(1L, spans.Span("delete_device").GetTagItem(Telemetry.DeviceId));
    }

    [Fact]
    public void Error_types_are_listed_once_each_and_a_success_has_none()
    {
        using var spans = new SpanRecorder();
        using var source = new ActivitySource("Tinker.UnitTests");
        using var activity = source.StartActivity("step")!;

        activity.SetErrorTypes([]);
        Assert.Null(activity.GetTagItem(Telemetry.ErrorType));

        activity.SetErrorTypes(["a", "b", "a"]);
        Assert.Equal(["a", "b"], (string[])activity.GetTagItem(Telemetry.ErrorType)!);
    }
}
