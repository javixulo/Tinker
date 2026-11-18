using Tinker.Core.Manuals;
using Xunit;

namespace Tinker.Core.Tests;

/// <summary>How the text of a page is split into chunks (technical spec, section 7). The text is wrapped by the layout of the PDF, as PdfPig returns it.</summary>
public sealed class TextChunkerTests
{
    private const string Settings =
        "Settings\n" +
        "Power On Default Mode - This sets the default output state\n" +
        "when Shelly is powered.\n" +
        "• ON: Configure Shelly to turn ON, when it has power.\n" +
        "• OFF: Configure Shelly to turn OFF, when it has power.\n" +
        "Firmware Update - Update the firmware of Shelly, when a new\n" +
        "version is released.\n" +
        "Factory Reset - Return Shelly to its factory default settings.\n" +
        "Device Reboot - Reboots the device.\n" +
        "Time Zone and Geo-location - Enable or Disable the automat-\n" +
        "ic detection of Time Zone and Geo-location.";

    [Fact]
    public void Nothing_to_split_gives_no_chunks()
    {
        Assert.Empty(TextChunker.Split(""));
        Assert.Empty(TextChunker.Split(" \n \n"));
    }

    [Fact]
    public void A_short_page_is_one_chunk_with_the_wrapped_lines_joined()
    {
        var chunk = Assert.Single(TextChunker.Split(Settings));

        Assert.Contains("Power On Default Mode - This sets the default output state when Shelly is powered.", chunk);
        Assert.Contains("Firmware Update - Update the firmware of Shelly, when a new version is released.", chunk);
    }

    [Fact]
    public void The_hyphen_of_a_wrapped_word_is_undone_but_a_real_hyphen_stays()
    {
        var chunk = Assert.Single(TextChunker.Split(
            "To set it, press the but-\nton for a second. Connect to the Wi-\nFi network, then use sunrise/\nsunset."));

        Assert.Equal("To set it, press the button for a second. Connect to the Wi-Fi network, then use sunrise/sunset.", chunk);
    }

    [Fact]
    public void Entries_and_bullets_are_not_merged_into_one_line()
    {
        var chunk = Assert.Single(TextChunker.Split(Settings));
        var lines = chunk.Split('\n');

        Assert.Contains("Factory Reset - Return Shelly to its factory default settings.", lines);
        Assert.Contains("Device Reboot - Reboots the device.", lines);
        Assert.Contains("Time Zone and Geo-location - Enable or Disable the automatic detection of Time Zone and Geo-location.", lines);
        Assert.Contains("• ON: Configure Shelly to turn ON, when it has power.", lines);
    }

    [Fact]
    public void A_chunk_never_starts_or_ends_inside_a_word_or_an_entry()
    {
        var chunks = TextChunker.Split(Settings, target: 150, max: 200);

        Assert.True(chunks.Count > 1);
        var starts = new[] { "Settings", "Power On", "• ", "Firmware Update", "Factory Reset", "Device Reboot", "Time Zone" };
        Assert.All(chunks, chunk => Assert.Contains(starts, start => chunk.StartsWith(start, StringComparison.Ordinal)));
        Assert.All(chunks, chunk => Assert.EndsWith(".", chunk));
    }

    [Fact]
    public void Every_word_of_the_page_is_in_some_chunk_once()
    {
        var chunks = TextChunker.Split(Settings, target: 150, max: 200);

        var original = Settings.Replace("automat-\nic", "automatic").Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var rebuilt = string.Join('\n', chunks).Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(original, rebuilt);
    }

    [Fact]
    public void A_heading_stays_with_the_text_that_follows()
    {
        var filler = string.Join("\n", Enumerable.Range(1, 6).Select(i => $"Entry {i} - Some text that fills the chunk for the entry number {i}."));

        var chunks = TextChunker.Split($"{filler}\nButton Type\n• One Button mode: the device is controlled with a single switch.", target: 330, max: 400);

        Assert.True(chunks.Count > 1);
        Assert.DoesNotContain(chunks, chunk => chunk.EndsWith("Button Type", StringComparison.Ordinal));
        Assert.Contains(chunks, chunk => chunk.Contains("Button Type\n• One Button mode", StringComparison.Ordinal));
    }

    [Fact]
    public void A_narrow_column_is_not_taken_for_headings()
    {
        var chunk = Assert.Single(TextChunker.Split("The Switch 2 stores\nperiodically (every\nhour) the consumption\ndata in the device\nmemory."));

        Assert.Equal("The Switch 2 stores periodically (every hour) the consumption data in the device memory.", chunk);
    }

    [Fact]
    public void A_list_of_steps_stays_in_one_chunk_while_it_fits_in_the_maximum()
    {
        var steps = string.Join("\n", Enumerable.Range(1, 7).Select(i => $"{i}. Do the step number {i} of this procedure and wait a few seconds."));
        var text = $"Intro text that explains something before the procedure starts, long enough to take space.\nInstallation of the Switch 2:\n{steps}";

        var chunks = TextChunker.Split(text, target: 250, max: 1500);

        var withSteps = chunks.Where(chunk => chunk.Contains("1. Do the step")).ToList();
        var chunkOfList = Assert.Single(withSteps);
        Assert.Contains("7. Do the step number 7", chunkOfList);
        Assert.Contains("Installation of the Switch 2:\n1. Do the step", chunkOfList);
    }

    [Fact]
    public void A_list_longer_than_the_maximum_is_cut_between_steps()
    {
        var steps = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"{i}. Do the step number {i} of this procedure and wait a few seconds."));

        var chunks = TextChunker.Split(steps, target: 250, max: 400);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 400));
        Assert.All(chunks, chunk => Assert.Matches(@"^\d+\. ", chunk));
        Assert.All(chunks, chunk => Assert.EndsWith("seconds.", chunk));
    }

    [Fact]
    public void A_unit_longer_than_the_maximum_is_cut_by_sentences()
    {
        var sentences = Enumerable.Range(1, 12).Select(i => $"This is sentence number {i} of a very long paragraph.");

        var chunks = TextChunker.Split(string.Join(" ", sentences), target: 120, max: 160);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 160));
        Assert.All(chunks, chunk => Assert.StartsWith("This is sentence", chunk));
        Assert.All(chunks, chunk => Assert.EndsWith("paragraph.", chunk));
    }

    [Fact]
    public void A_sentence_longer_than_the_maximum_is_cut_between_words()
    {
        var chunks = TextChunker.Split(string.Join(" ", Enumerable.Repeat("word", 100)), target: 100, max: 120);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 120));
        Assert.All(chunks, chunk => Assert.All(chunk.Split(' '), word => Assert.Equal("word", word)));
    }

    [Fact]
    public void A_tiny_chunk_is_joined_to_its_neighbour()
    {
        var chunks = TextChunker.Split("Entry 1 - " + new string('a', 90) + " end.\nEntry 2 - " + new string('b', 85) + " end.\n14\nPage", target: 100, max: 400);

        Assert.All(chunks, chunk => Assert.True(chunk.Length >= TextChunker.MinimumSize));
    }

    [Fact]
    public void Blank_lines_separate_paragraphs()
    {
        var chunks = TextChunker.Split("First paragraph that goes on\nfor two lines.\n\nSecond paragraph that is separate.", target: 50, max: 70);

        Assert.Equal(["First paragraph that goes on for two lines.", "Second paragraph that is separate."], chunks);
    }
}
