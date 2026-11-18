using System.ComponentModel;

namespace Tinker.Core.Models;

/// <summary>
/// A device as the inventory keeps it (functional spec, section 1). Every property is nullable because the same type
/// is also the input of the tools, which must be tolerant: the services validate what is required.
/// </summary>
public sealed record Device
{
    [Description("Assigned by the inventory when the device is registered. Never provide it when registering.")]
    public long? DeviceId { get; init; }
    [Description("Brand of the device, as the user wrote it. Required to register.")]
    public string? Brand { get; init; }
    [Description("Model of the device, as the user wrote it. Required to register.")]
    public string? Model { get; init; }

    /// <summary>Paths relative to the manuals folder.</summary>
    [Description("Manuals of the device (at least one to register): each one is a PDF path relative to the manuals folder, e.g. shelly/dimmer_2_guide.pdf. Absolute paths and .. are not allowed.")]
    public IReadOnlyList<string>? Manuals { get; init; }
    [Description("Type of the device, e.g. Dimmer or Washing machine. Optional.")]
    public string? DeviceType { get; init; }
    [Description("Color of the device. Optional.")]
    public string? Color { get; init; }
    [Description("Location in the home, e.g. Living room. Optional.")]
    public string? Location { get; init; }
    [Description("Invoice of the purchase. Optional.")]
    public string? Invoice { get; init; }
    [Description("Links about the device, e.g. online manuals or support pages. Optional.")]
    public IReadOnlyList<string>? Urls { get; init; }
    [Description("Purchase date, YYYY-MM-DD. Optional.")]
    public DateOnly? PurchaseDate { get; init; }
    [Description("Warranty end date, YYYY-MM-DD. Optional.")]
    public DateOnly? WarrantyEndDate { get; init; }

    /// <summary>One of <see cref="DeviceStatus.All"/>.</summary>
    [Description("Status of the device: active, stored, sold or discarded. Optional.")]
    public string? Status { get; init; }
}

public static class DeviceStatus
{
    public static readonly IReadOnlyList<string> All = ["active", "stored", "sold", "discarded"];
}
