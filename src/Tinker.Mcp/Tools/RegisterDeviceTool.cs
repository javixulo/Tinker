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
        "Registers a new home device in the inventory together with its manuals, and reads the content of the manuals so it can be searched later. " +
        "Needs brand, model and at least one manual (a PDF path relative to the manuals folder); everything else is optional. " +
        "Returns ok=true and the device as stored, with its device_id, or ok=false with the errors: each one says what to fix. " +
        "A rejected registration changes nothing, so fix every error and call it again. " +
        "If the device already exists the error carries it: do not register it twice.")]
    public Task<RegistrationResult> RegisterDevice(
        [Description("The device to register. brand, model and manuals are required; device_id must not be given.")] Device? device = null,
        CancellationToken cancellationToken = default) =>
        registrar.RegisterAsync(device, cancellationToken);
}
