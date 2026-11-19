using System.ComponentModel;
using ModelContextProtocol.Server;
using Tinker.Core.Models;
using Tinker.Core.Removal;

namespace Tinker.Mcp.Tools;

[McpServerToolType]
public sealed class DeleteDeviceTool(DeviceRemover remover)
{
    [McpServerTool(Name = "delete_device", Title = "Delete a device", UseStructuredContent = true, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description(
        "Deletes one registered home device, with its manuals' content in the search index. The manual files themselves are not touched. " +
        "Identify the device with the properties you know (for example brand and model, or type, brand and location): they must single out exactly one device, and at least one is needed. You do not need its device_id. " +
        "The device is deleted at once, with no confirmation: confirm with the user first. " +
        "Returns the device as it was, or a tool error with an error and nothing deleted: " +
        "device_not_found (no match), ambiguous_device (several match, carried in the error: ask the user which one or give more properties) or invalid_fields (no property given).")]
    public Task<DeletionResult> DeleteDevice(
        [Description("The properties that identify the device: any fields of a device (device_id, brand, model, device_type, location...). At least one is needed and together they must match exactly one device. A blank field counts as not given. Example: {\"brand\": \"Shelly\", \"model\": \"Dimmer 2\"}, or {\"device_id\": 3}.")] Device? device = null,
        CancellationToken cancellationToken = default) =>
        remover.DeleteAsync(device, cancellationToken);
}
