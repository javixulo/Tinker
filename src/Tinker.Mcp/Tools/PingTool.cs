using System.ComponentModel;
using ModelServer = ModelContextProtocol.Server;

namespace Tinker.Mcp.Tools;

/// <summary>Placeholder so the server starts with one tool. Replace with the real tools (register_device, find_devices, ...).</summary>
[ModelServer.McpServerToolType]
public static class PingTool
{
    [ModelServer.McpServerTool(Name = "ping"), Description("Health check: returns 'pong'.")]
    public static string Ping() => "pong";
}
