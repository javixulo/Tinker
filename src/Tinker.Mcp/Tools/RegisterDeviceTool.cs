using System.ComponentModel;
using ModelContextProtocol.Server;
using Tinker.Core.Models;
using Tinker.Core.Registration;

namespace Tinker.Mcp.Tools;

[McpServerToolType]
public sealed class RegisterDeviceTool(DeviceRegistrar registrar)
{
    [McpServerTool(Name = "register_device", UseStructuredContent = true)]
    [Description(
        "Registers a new home device in the inventory, optionally together with its manuals, and reads the content of the manuals so it can be searched later. " +
        "Needs brand and model; everything else is optional, manuals included (each one is a PDF path relative to the manuals folder). The status is active unless given. Values are stored normalized, and the device is returned as stored. " +
        "Returns ok=true and the device as stored, with its device_id, or ok=false with the errors: each one says what to fix. " +
        "A rejected registration changes nothing, so fix every error and call it again. " +
        "If the device already exists the error carries it: do not register it twice.")]
    public Task<RegistrationResult> RegisterDevice(
        [Description("The device to register. brand and model are required; device_id must not be given.")] Device? device = null,
        CancellationToken cancellationToken = default) =>
        registrar.RegisterAsync(device, cancellationToken);
}
