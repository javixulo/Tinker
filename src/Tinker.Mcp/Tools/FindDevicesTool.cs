using System.ComponentModel;
using ModelContextProtocol.Server;
using Tinker.Core.Finding;
using Tinker.Core.Models;

namespace Tinker.Mcp.Tools;

[McpServerToolType]
public sealed class FindDevicesTool(DeviceFinder finder)
{
    [McpServerTool(Name = "find_devices", Title = "Find devices", UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description(
        "Finds registered home devices by what is known about them, for example brand and model, or type and location. " +
        "A device matches when every property given equals its own: the whole value is compared, ignoring upper and lower case and accents, not part of it. " +
        "With no property it lists every device. " +
        "Returns the matching devices with all their information, including the device_id that search_manuals needs, ordered by device_id. " +
        "If nothing matches, it is not an error: the list is empty and message says why and what to do next.")]
    public Task<FindDevicesResult> FindDevices(
        [Description("The properties to look for: any fields of a device, none of them required (device_id, brand, model, device_type, location...). A blank field counts as not given. Example: {\"brand\": \"Shelly\", \"location\": \"Living room\"}.")] Device? device = null,
        CancellationToken cancellationToken = default) =>
        finder.FindAsync(device, cancellationToken);
}
