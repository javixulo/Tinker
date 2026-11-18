using Tinker.Core.Manuals;
using Xunit;

namespace Tinker.UnitTests;

/// <summary>
/// The paths that are rejected by their text alone, before the resolver looks at the disk (technical spec, section 5).
/// The links that need real folders are covered by the integration tests.
/// </summary>
public sealed class ManualPathResolverTests
{
    private readonly ManualPathResolver _resolver = new(Path.Combine(Path.GetTempPath(), "tinker-unit-no-such-folder"));

    [Theory]
    [Trait("Criteria", "UC-02.7")]
    [InlineData("/etc/passwd")]
    [InlineData("\\windows\\system32")]
    [InlineData("C:\\docs\\guide.pdf")]
    [InlineData("c:guide.pdf")]
    [InlineData("~/Documents/guide.pdf")]
    [InlineData("~")]
    [InlineData("%2e%2e/secret.pdf")]
    [InlineData("shelly/%2E%2E/secret.pdf")]
    [InlineData("shelly/guide\0.pdf")]
    public void Paths_that_look_outside_are_rejected_as_outside_the_folder(string manual)
    {
        var result = _resolver.Resolve(manual);

        Assert.Equal(ManualRejection.OutsideFolder, result.Rejection);
        Assert.False(result.Allowed);
        Assert.Null(result.FullPath);
        Assert.Null(result.RelativePath);
    }

    [Theory]
    [Trait("Criteria", "UC-02.8")]
    [InlineData("../secret.pdf")]
    [InlineData("shelly/../secret.pdf")]
    [InlineData("shelly/../shelly/guide.pdf")]
    [InlineData("shelly\\..\\guide.pdf")]
    public void Paths_with_dot_dot_are_rejected_even_when_they_would_end_up_inside(string manual) =>
        Assert.Equal(ManualRejection.ContainsDotDot, _resolver.Resolve(manual).Rejection);

    [Theory]
    [Trait("Criteria", "UC-02.7")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("./")]
    public void The_manuals_folder_itself_is_not_a_manual(string manual) =>
        Assert.Equal(ManualRejection.OutsideFolder, _resolver.Resolve(manual).Rejection);

    [Fact]
    [Trait("Criteria", "UC-02.9")]
    public void The_answer_never_depends_on_whether_the_target_exists() =>
        Assert.Equal(_resolver.Resolve("/etc/passwd"), _resolver.Resolve("/no/such/file"));
}
