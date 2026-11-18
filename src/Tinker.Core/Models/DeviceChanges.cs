using System.ComponentModel;

namespace Tinker.Core.Models;

/// <summary>
/// The fields of <c>update_device</c> to change (functional spec, section 2.5). A field that is <c>null</c> was not sent
/// and keeps its value; a text that is empty or has only spaces, or an empty list, empties the field. So, unlike
/// <see cref="Device"/>, nothing here is turned into <c>null</c> when it is empty. The dates are text so that an empty
/// one can clear them.
/// </summary>
public sealed record DeviceChanges
{
    [Description("Never give it: the device_id cannot be changed.")]
    public long? DeviceId { get; init; }
    [Description("New brand. Cannot be emptied.")]
    public string? Brand { get; init; }
    [Description("New model. Cannot be emptied.")]
    public string? Model { get; init; }
    [Description("The complete new list of manuals, each one a PDF path relative to the manuals folder. To add one, send the current ones plus the new one; to remove one, send the list without it; an empty list removes all of them.")]
    public IReadOnlyList<string>? Manuals { get; init; }
    [Description("New type of the device. Empty clears it.")]
    public string? DeviceType { get; init; }
    [Description("New color. Empty clears it.")]
    public string? Color { get; init; }
    [Description("New location in the home. Empty clears it.")]
    public string? Location { get; init; }
    [Description("New invoice. Empty clears it.")]
    public string? Invoice { get; init; }
    [Description("The complete new list of links. An empty list clears it.")]
    public IReadOnlyList<string>? Urls { get; init; }
    [Description("New purchase date, YYYY-MM-DD. Empty clears it.")]
    public string? PurchaseDate { get; init; }
    [Description("New warranty end date, YYYY-MM-DD. Empty clears it.")]
    public string? WarrantyEndDate { get; init; }
    [Description("New status: active, inactive or archived. Cannot be emptied.")]
    public string? Status { get; init; }
}
