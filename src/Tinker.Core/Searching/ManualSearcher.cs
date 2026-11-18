using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tinker.Core.Models;
using Tinker.Core.Storage;

namespace Tinker.Core.Searching;

/// <summary>
/// UC-08 and UC-09: searches the manuals of one device by meaning. Validates the arguments, checks that the device
/// exists and asks the index, which only ever reads the chunks of that device. <paramref name="queryPrefix"/> is what
/// the embedding model wants in front of a query (technical spec, section 7); the query text itself is never logged.
/// </summary>
public sealed partial class ManualSearcher(
    IInventory inventory,
    IManualsIndex index,
    ILogger<ManualSearcher> logger,
    string queryPrefix = "")
{
    public const int DefaultResults = 5;

    public async Task<SearchResult> SearchAsync(long? deviceId, string? query, int? nResults, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("search_manuals");
        activity?.SetTag("tinker.device_id", deviceId);
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
            activity?.SetStatus(ActivityStatusCode.Error);
        }

        activity?.SetTag("tinker.ok", result.Ok);
        activity?.SetTag("tinker.matches", result.Chunks.Count);
        if (result.Error is { } failure)
        {
            activity?.SetTag("tinker.error_codes", failure.Code);
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
        if (!await ExistsAsync(id, cancellationToken))
        {
            return NotFound(id);
        }

        var chunks = await index.SearchAsync(id, queryPrefix + query!.Trim(), nResults ?? DefaultResults, cancellationToken);
        if (chunks.Count > 0)
        {
            LogFound(id, chunks.Count);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Search manuals: {Chunks} excerpts found for device {DeviceId}")]
    private partial void LogFound(long deviceId, int chunks);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Search manuals: device {DeviceId} has no chunks, its manuals have no extractable text")]
    private partial void LogNoText(long deviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Search manuals rejected [{Code}]: {Message}")]
    private partial void LogRejected(string code, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Searching the manuals failed")]
    private partial void LogFailed(Exception error);
}
