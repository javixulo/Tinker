namespace Tinker.Core.Models;

/// <summary>One reason why a tool could not do what the client asked (functional spec, section 1).</summary>
public sealed record DeviceError(string Code, string Message, Device? Device = null, IReadOnlyList<Device>? Devices = null);

/// <summary>Outcome of <c>register_device</c>. A rejection is not a tool error: it comes as <c>ok=false</c>.</summary>
public sealed record RegistrationResult(bool Ok, IReadOnlyList<DeviceError> Errors, Device? Device)
{
    public static RegistrationResult Success(Device device) => new(true, [], device);

    public static RegistrationResult Failure(params DeviceError[] errors) => new(false, errors, null);
}

/// <summary>Outcome of <c>find_devices</c>. <c>Message</c> is set when no device matches: why, and what to do next.</summary>
public sealed record FindDevicesResult(bool Ok, IReadOnlyList<Device> Devices, string? Message = null, DeviceError? Error = null)
{
    public static FindDevicesResult Found(IReadOnlyList<Device> devices) => new(true, devices);

    public static FindDevicesResult Empty(string message) => new(true, [], message);

    public static FindDevicesResult Failure(DeviceError error) => new(false, [], null, error);
}

/// <summary>Outcome of <c>delete_device</c>. <c>Device</c> is the device as it was before being deleted.</summary>
public sealed record DeletionResult(bool Ok, Device? Device, DeviceError? Error = null)
{
    public static DeletionResult Success(Device device) => new(true, device);

    public static DeletionResult Failure(DeviceError error) => new(false, null, error);
}

/// <summary>An excerpt of a manual.</summary>
public sealed record Chunk(string Text, long DeviceId, string Manual, int Page);

public static class ErrorCodes
{
    public const string InvalidFields = "invalid_fields";
    public const string ManualNotAllowed = "manual_not_allowed";
    public const string ManualNotFound = "manual_not_found";
    public const string ManualInUse = "manual_in_use";
    public const string DeviceExists = "device_exists";
    public const string DeviceNotFound = "device_not_found";
    public const string AmbiguousDevice = "ambiguous_device";
    public const string Failed = "failed";
}
