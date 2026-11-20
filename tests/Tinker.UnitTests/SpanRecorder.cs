using Xunit;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Tinker.UnitTests;

/// <summary>
/// Records the spans of the "Tinker" source that run inside one test. The test runs under its own root span, and only spans
/// of that trace are kept, so tests running in parallel never see each other's spans.
/// </summary>
internal sealed class SpanRecorder : IDisposable
{
    private static readonly ActivitySource TestSource = new("Tinker.UnitTests");
    private readonly ConcurrentBag<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly Activity _root;

    public SpanRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Tinker" or "Tinker.UnitTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Add,
        };
        ActivitySource.AddActivityListener(_listener);
        _root = TestSource.StartActivity("test", ActivityKind.Internal, parentContext: default)!;
    }

    public ActivityTraceId TraceId => _root.TraceId;

    /// <summary>The one finished span with that name in this test's trace.</summary>
    public Activity Span(string name) => Assert.Single(_spans, s => s.OperationName == name && s.TraceId == _root.TraceId);

    public bool Has(string name) => _spans.Any(s => s.OperationName == name && s.TraceId == _root.TraceId);

    public void Dispose()
    {
        _root.Dispose();
        _listener.Dispose();
    }
}
