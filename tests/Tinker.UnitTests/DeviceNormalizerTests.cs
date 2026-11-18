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
}
