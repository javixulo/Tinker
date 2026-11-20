using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tinker.Core.Models;
using Tinker.Core.Storage;

namespace Tinker.Core.Searching;

/// <summary>
/// UC-08 and UC-09: searches the manuals of one device by meaning. Validates the arguments, checks that the device
/// exists and asks the index, which only ever reads the chunks of that device. <paramref name="queryPrefix"/> is what
/// the embedding model wants in front of a query (technical spec, section 7); the query text is recorded as a span attribute.
/// </summary>
public sealed class ManualSearcher(
    IInventory inventory,
    IManualsIndex index,
    ILogger<ManualSearcher> logger,
    string queryPrefix = "")
{
    public const int DefaultResults = 5;

    public async Task<SearchResult> SearchAsync(long? deviceId, string? query, int? nResults, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("search_manuals");
        activity?.SetTag(Telemetry.DeviceId, deviceId);
        activity?.SetTag(Telemetry.SearchQuery, query);
        SearchResult result;
        try
        {
            result = await SearchCoreAsync(deviceId, query, nResults, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            LogFailed(error);
            result = SearchResult.Failure(new DeviceError(ErrorCodes.Failed, Messages.Failed("Searching the manuals")));
            activity.Fail(error);
        }

        activity?.SetTag(Telemetry.Matches, result.Chunks.Count);
        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        return result;
    }

    private async Task<SearchResult> SearchCoreAsync(long? deviceId, string? query, int? nResults, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        if (deviceId is null)
        {
            problems.Add(Messages.MissingDeviceId);
        }

        if (TextKeys.IsBlank(query))
        {
            problems.Add(Messages.BlankQuery);
        }

        if (nResults is < 1)
        {
            problems.Add(Messages.InvalidResultCount);
        }

        if (problems.Count > 0)
        {
            var message = string.Join(' ', problems);
            LogRejected(ErrorCodes.InvalidFields, message);
            return SearchResult.Failure(new DeviceError(ErrorCodes.InvalidFields, message));
        }

        var id = deviceId!.Value;
        var device = (await inventory.ListAllAsync(cancellationToken)).FirstOrDefault(d => d.DeviceId == id);
        if (device is null)
        {
            return NotFound(id);
        }

        if (device.Manuals is not { Count: > 0 })
        {
            LogNoManuals(id);
            return SearchResult.Empty(Messages.NoManuals);
        }

        var chunks = await index.SearchAsync(id, queryPrefix + query!.Trim(), nResults ?? DefaultResults, cancellationToken);
        if (chunks.Count > 0)
        {
            return SearchResult.Found(chunks);
        }

        // Nothing came back: either the device has no text, or it was deleted since the check above.
        if (!await ExistsAsync(id, cancellationToken))
        {
            return NotFound(id);
        }

        LogNoText(id);
        return SearchResult.Empty(Messages.NoExtractableText);
    }

    private async Task<bool> ExistsAsync(long deviceId, CancellationToken cancellationToken) =>
        (await inventory.ListAllAsync(cancellationToken)).Any(d => d.DeviceId == deviceId);

    private SearchResult NotFound(long deviceId)
    {
        var message = Messages.SearchDeviceNotFound(deviceId);
        LogRejected(ErrorCodes.DeviceNotFound, message);
        return SearchResult.Failure(new DeviceError(ErrorCodes.DeviceNotFound, message));
    }

    // Plain calls instead of [LoggerMessage]: the dotted names of the shared backend (`device.id`) are not valid C# identifiers, and the analyzer wants PascalCase.
#pragma warning disable CA1848, CA1873, CA1727

    private void LogNoText(long deviceId) =>
        logger.LogWarning("Search found no chunks for device {device.id} because its manuals had no extractable text", deviceId);

    private void LogNoManuals(long deviceId) =>
        logger.LogWarning("Search found nothing because device {device.id} has no manuals", deviceId);

    private void LogRejected(string code, string message) =>
        logger.LogWarning("Search rejected with {error.type}: {error.message}", code, message);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Searching the manuals failed");
#pragma warning restore CA1848, CA1873, CA1727
}
