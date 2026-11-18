using Tinker.Core;
using Xunit;

namespace Tinker.UnitTests;

/// <summary>How the brand of a device is cleaned before it is stored and compared (functional spec, UC-01.8).</summary>
public sealed class DeviceNormalizerTests
{
    [Theory]
    [Trait("Criteria", "UC-01.8")]
    [InlineData("LG", "LG")]
    [InlineData("philips hué", "Philips Hue")]
    [InlineData("bang & olufsen", "Bang & Olufsen")]
    [InlineData("B.S.H.", "BSH")]
    [InlineData("  SAMSUNG   electronics ", "Samsung Electronics")]
    public void Brand_normalization(string given, string expected) => Assert.Equal(expected, DeviceNormalizer.Brand(given));

    [Theory]
    [Trait("Criteria", "UC-01.8")]
    [InlineData("Phílips  Hué!", "Philips Hue")]
    [InlineData("  ", "")]
    [InlineData("***", "")]
    [InlineData("B&O-Play", "B&o-play")]
    public void Brand_keeps_only_letters_digits_spaces_hyphens_and_ampersands(string given, string expected) =>
        Assert.Equal(expected, DeviceNormalizer.Brand(given));

    [Fact]
    [Trait("Criteria", "UC-05.3")]
    public void CleanBrand_does_not_touch_the_casing() => Assert.Equal("philips Hue", DeviceNormalizer.CleanBrand("phílips   Hué."));

    [Theory]
    [Trait("Criteria", "UC-01.8")]
    [InlineData("lct015", "LCT015")]
    [InlineData("  fgs - 223 ", "FGS - 223")]
    [InlineData("a/b-c", "A/B-C")]
    [InlineData(null, "")]
    public void Model_is_uppercase_with_squashed_spaces(string? given, string expected) =>
        Assert.Equal(expected, DeviceNormalizer.Model(given));

    [Theory]
    [Trait("Criteria", "UC-01.8")]
    [InlineData("living room", "Living room")]
    [InlineData("  washing   machine ", "Washing machine")]
    [InlineData("iPad", "IPad")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void FirstUpper_changes_only_the_first_character(string? given, string expected) =>
        Assert.Equal(expected, DeviceNormalizer.FirstUpper(given));
}
