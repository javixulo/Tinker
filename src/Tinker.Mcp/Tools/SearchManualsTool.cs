using System.ComponentModel;
using ModelContextProtocol.Server;
using Tinker.Core.Models;
using Tinker.Core.Searching;

namespace Tinker.Mcp.Tools;

[McpServerToolType]
// The SDK uses the C# parameter names as the argument names, so they are written in snake_case (the contract).
#pragma warning disable CA1707, IDE1006
public sealed class SearchManualsTool(ManualSearcher searcher)
{
    [McpServerTool(Name = "search_manuals", UseStructuredContent = true, ReadOnly = true)]
    [Description(
        "Searches the manuals of one registered home device and returns the excerpts that best answer a question, most relevant first. " +
        "Needs the device_id, which find_devices returns. The search is by meaning, not only by exact words, and the manuals and the query are assumed to be in English. " +
        "Each chunk has its text, the device_id, the manual it comes from (path relative to the manuals folder) and its page. " +
        "Answer from the excerpts and cite the manual and page. " +
        "If there is nothing to return, ok is still true, the list is empty and message says why and what to do next. " +
        "ok=false means an error: invalid_fields (a missing or invalid argument), device_not_found (use find_devices) or failed (tell the user, do not retry).")]
    public Task<SearchResult> SearchManuals(
        [Description("The device whose manuals are searched. Get it from find_devices.")] long? device_id = null,
        [Description("What to look for, in natural language and in English: a question or a description (for example 'how do I reset the wifi'). No operators or wildcards. It must not be blank.")] string? query = null,
        [Description("Maximum number of excerpts to return. At least 1. Default 5.")] int? n_results = null,
        CancellationToken cancellationToken = default) =>
        searcher.SearchAsync(device_id, query, n_results, cancellationToken);
}
