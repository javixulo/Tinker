using System.Text.Json.Serialization;

namespace Tinker.Core.Models;

/// <summary>One reason why a tool could not do what the client asked (functional spec, section 1).</summary>
public sealed record DeviceError(string Code, string Message, Device? Device = null, IReadOnlyList<Device>? Devices = null);

/// <summary>Outcome of <c>register_device</c>. A rejection has <c>errors</c> and is sent as a tool error (<c>isError</c>).</summary>
public sealed record RegistrationResult(IReadOnlyList<DeviceError> Errors, Device? Device)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no errors.</summary>
    [JsonIgnore]
    public bool Ok => Errors.Count == 0;

    public static RegistrationResult Success(Device device) => new([], device);

    public static RegistrationResult Failure(params DeviceError[] errors) => new(errors, null);
}

/// <summary>Outcome of <c>update_device</c>. <c>Device</c> is the device as stored after the edit.</summary>
public sealed record UpdateResult(IReadOnlyList<DeviceError> Errors, Device? Device)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no errors.</summary>
    [JsonIgnore]
    public bool Ok => Errors.Count == 0;

    public static UpdateResult Success(Device device) => new([], device);

    public static UpdateResult Failure(params DeviceError[] errors) => new(errors, null);
}

/// <summary>Outcome of <c>find_devices</c>. <c>Message</c> is set when no device matches: why, and what to do next.</summary>
public sealed record FindDevicesResult(IReadOnlyList<Device> Devices, string? Message = null, DeviceError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static FindDevicesResult Found(IReadOnlyList<Device> devices) => new(devices);

    public static FindDevicesResult Empty(string message) => new([], message);

    public static FindDevicesResult Failure(DeviceError error) => new([], null, error);
}

/// <summary>Outcome of <c>delete_device</c>. <c>Device</c> is the device as it was before being deleted.</summary>
public sealed record DeletionResult(Device? Device, DeviceError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static DeletionResult Success(Device device) => new(device);

    public static DeletionResult Failure(DeviceError error) => new(null, error);
}

/// <summary>Outcome of <c>search_manuals</c>. <c>Message</c> is set when there is nothing to return: why, and what to do next.</summary>
public sealed record SearchResult(IReadOnlyList<Chunk> Chunks, string? Message = null, DeviceError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static SearchResult Found(IReadOnlyList<Chunk> chunks) => new(chunks);

    public static SearchResult Empty(string message) => new([], message);

    public static SearchResult Failure(DeviceError error) => new([], null, error);
}

/// <summary>An excerpt of a manual.</summary>
public sealed record Chunk(string Text, long DeviceId, string Manual, int Page);

public static class ErrorCodes
{
    public const string InvalidFields = "invalid_fields";
    public const string ManualNotAllowed = "manual_not_allowed";
    public const string ManualNotFound = "manual_not_found";
    public const string ManualUnreadable = "manual_unreadable";
    public const string ManualInUse = "manual_in_use";
    public const string DeviceExists = "device_exists";
    public const string DeviceNotFound = "device_not_found";
    public const string AmbiguousDevice = "ambiguous_device";
    public const string Failed = "failed";
}
