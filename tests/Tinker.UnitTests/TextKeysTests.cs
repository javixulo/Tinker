using Tinker.Core;
using Xunit;

namespace Tinker.UnitTests;

/// <summary>The one way text is compared (technical spec, section 7): lowercase, no accents, spaces squashed.</summary>
public sealed class TextKeysTests
{
    [Theory]
    [Trait("Criteria", "UC-05.1, UC-06.1")]
    [InlineData("Habitación", "habitacion")]
    [InlineData("  habitacion ", "habitacion")]
    [InlineData("Philips   Hué", "philips hue")]
    [InlineData("LIVING\tROOM\n", "living room")]
    [InlineData("ñandú", "nandu")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Fold_ignores_case_accents_and_extra_spaces(string given, string expected) =>
        Assert.Equal(expected, TextKeys.Fold(given));

    [Fact]
    [Trait("Criteria", "UC-02.2")]
    public void Fold_of_null_is_empty() => Assert.Equal("", TextKeys.Fold(null));

    [Theory]
    [Trait("Criteria", "UC-02.2")]
    [InlineData("a-b", "ab")]
    [InlineData("a.b", "ab")]
    [InlineData("a&b", "a b")]
    public void Fold_does_not_ignore_other_characters(string left, string right) =>
        Assert.NotEqual(TextKeys.Fold(right), TextKeys.Fold(left));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  \t", true)]
    [InlineData(" a ", false)]
    public void IsBlank_treats_missing_and_whitespace_as_blank(string? text, bool expected) =>
        Assert.Equal(expected, TextKeys.IsBlank(text));

    [Fact]
    public void Squash_trims_and_collapses_whitespace() => Assert.Equal("a b c", TextKeys.Squash("  a \t b\n c  "));
}
