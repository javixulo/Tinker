using System.Diagnostics;

namespace Tinker.Mcp;

/// <summary>
/// One line per HTTP request, in place of the several that ASP.NET Core writes (which are off, see appsettings.json). Its
/// level depends on the request, something a log level setting cannot do: a successful health check is
/// <c>Debug</c>, so it is hidden by default, and any other request is <c>Information</c>. A server error is a <c>Warning</c>,
/// the health check included.
/// </summary>
public sealed class RequestLogging(RequestDelegate next, ILogger<RequestLogging> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        // An exception goes on to ASP.NET Core, which logs it as an error.
        await next(context);

        var status = context.Response.StatusCode;
        var level = status >= StatusCodes.Status500InternalServerError ? LogLevel.Warning
            : context.Request.Path.StartsWithSegments("/health") ? LogLevel.Debug
            : LogLevel.Information;

#pragma warning disable CA1848, CA1873, CA1727 // The names are the ones of the shared backend (technical spec, section 3).
        logger.Log(level, "Handled {http.request.method} {url.path} with status {http.response.status_code} in {duration_ms} ms",
            context.Request.Method, context.Request.Path.Value, status, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
#pragma warning restore CA1848, CA1873, CA1727
    }
}
