using System.ComponentModel;
using ModelContextProtocol.Server;
using Tinker.Core.Models;
using Tinker.Core.Updating;

namespace Tinker.Mcp.Tools;

[McpServerToolType]
// The SDK uses the C# parameter names as the argument names, so they are written in snake_case (the contract).
#pragma warning disable CA1707, IDE1006
public sealed class UpdateDeviceTool(DeviceUpdater updater)
{
    [McpServerTool(Name = "update_device", UseStructuredContent = true, Idempotent = true, Destructive = false)]
    [Description(
        "Edits a registered home device: its data, its manuals or both. Needs the device_id (find_devices returns it, with the current values) and changes with at least one field to change. " +
        "A field that is not sent keeps its value. A field sent with an empty value (empty text or an empty list) is emptied, except brand, model and status, which cannot be. " +
        "A list (manuals, urls) replaces the current list as a whole: to add an item send the current items plus the new one, to remove one send the list without it, to remove all send an empty list. " +
        "New manuals are read so they can be searched; removed manuals lose their content in the index, but their files are not touched. " +
        "It edits at once, so confirm with the user before changing something they did not ask for. " +
        "Returns ok=true and the device as stored, or ok=false with the errors: each one says what to fix. A rejected update changes nothing. " +
        "device_exists and manual_in_use carry the other device involved; device_not_found means the device_id is wrong, use find_devices.")]
    public Task<UpdateResult> UpdateDevice(
        [Description("The device to edit. Get it from find_devices.")] long? device_id = null,
        [Description("The fields to change, with their new values. At least one is required. device_id cannot be changed.")] DeviceChanges? changes = null,
        CancellationToken cancellationToken = default) =>
        updater.UpdateAsync(device_id, changes, cancellationToken);
}
