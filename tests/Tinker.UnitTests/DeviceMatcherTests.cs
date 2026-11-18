using Tinker.Core.Finding;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.UnitTests;

/// <summary>What counts as "given" and what matches (functional spec, section 2.3). Shared by find_devices and delete_device.</summary>
public sealed class DeviceMatcherTests
{
    private static Device Stored() => new()
    {
        DeviceId = 7,
        Brand = "Philips Hue",
        Model = "LCT015",
        DeviceType = "Bulb",
        Location = "Living room",
        Status = "active",
        Manuals = ["hue/a.pdf", "hue/b.pdf"],
        Urls = ["https://example.com/hue"],
        PurchaseDate = new DateOnly(2024, 5, 1),
    };

    private static bool Matches(Device wanted, Device stored) =>
        DeviceMatcher.Given(wanted).All(c => c.Matches(stored));

    [Fact]
    [Trait("Criteria", "UC-06.3")]
    public void Nothing_given_means_no_criteria() => Assert.Empty(DeviceMatcher.Given(new Device()));

    [Fact]
    [Trait("Criteria", "UC-06.3")]
    public void Blank_texts_empty_lists_and_a_brand_with_no_valid_character_count_as_not_given()
    {
        var blank = new Device { Brand = "!!!", Model = "  ", Color = "", Location = " \t", Manuals = [" ", ""], Urls = [] };

        Assert.Empty(DeviceMatcher.Given(blank));
    }

    [Fact]
    [Trait("Criteria", "UC-05.1, UC-06.1")]
    public void Texts_match_ignoring_case_accents_and_spaces()
    {
        Assert.True(Matches(new Device { Location = "  LIVING   rooM " }, Stored()));
        Assert.True(Matches(new Device { Brand = "philips hué" }, Stored()));
    }

    [Fact]
    [Trait("Criteria", "UC-05.3")]
    public void The_brand_is_cleaned_like_the_stored_one() =>
        Assert.True(Matches(new Device { Brand = "B.S.H." }, Stored() with { Brand = "BSH" }));

    [Fact]
    [Trait("Criteria", "UC-06.2")]
    public void A_part_of_a_value_does_not_match() =>
        Assert.False(Matches(new Device { Location = "Living" }, Stored()));

    [Fact]
    [Trait("Criteria", "UC-06.2")]
    public void Every_property_given_must_match()
    {
        Assert.True(Matches(new Device { Brand = "Philips Hue", Status = "active" }, Stored()));
        Assert.False(Matches(new Device { Brand = "Philips Hue", Status = "archived" }, Stored()));
    }

    [Fact]
    [Trait("Criteria", "UC-06.2")]
    public void A_list_matches_when_the_device_has_every_item_given_in_any_order()
    {
        Assert.True(Matches(new Device { Manuals = ["HUE/B.pdf"] }, Stored()));
        Assert.True(Matches(new Device { Manuals = ["hue/b.pdf", "hue/a.pdf"] }, Stored()));
        Assert.False(Matches(new Device { Manuals = ["hue/a.pdf", "hue/c.pdf"] }, Stored()));
    }

    [Fact]
    public void A_property_the_device_does_not_have_does_not_match() =>
        Assert.False(Matches(new Device { Color = "Red" }, Stored()));

    [Fact]
    public void Dates_and_ids_match_exactly()
    {
        Assert.True(Matches(new Device { PurchaseDate = new DateOnly(2024, 5, 1), DeviceId = 7 }, Stored()));
        Assert.False(Matches(new Device { PurchaseDate = new DateOnly(2024, 5, 2) }, Stored()));
        Assert.False(Matches(new Device { DeviceId = 8 }, Stored()));
    }

    [Fact]
    [Trait("Criteria", "UC-07.1, UC-04.2")]
    public void The_listed_form_names_each_property_given()
    {
        var listed = DeviceMatcher.Given(new Device { Brand = "philips hué", Model = " x1 ", DeviceId = 3 }).Select(c => c.Listed);

        Assert.Equal(["device_id: 3", "brand: philips hue", "model: x1"], listed);
    }
}
