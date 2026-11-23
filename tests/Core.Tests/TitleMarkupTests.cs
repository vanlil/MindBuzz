using System.Text;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.Core.Tests;

/// <summary>Bold, italic and underline in topic titles, stored as Markdown.</summary>
public sealed class TitleMarkupTests
{
    /// <summary>The runs as text: plain text as is, a styled run as [B,I,U letters:text].</summary>
    private static string Show(string markup)
    {
        ParsedTitle parsed = TitleMarkup.Parse(markup);
        var text = new StringBuilder();
        foreach (TitleRun run in parsed.Runs)
        {
            string part = parsed.Plain.Substring(run.Start, run.Length);
            if (run.Style == TitleStyle.None)
            {
                text.Append(part);
                continue;
            }

            text.Append('[')
                .Append(run.Style.HasFlag(TitleStyle.Bold) ? "B" : string.Empty)
                .Append(run.Style.HasFlag(TitleStyle.Italic) ? "I" : string.Empty)
                .Append(run.Style.HasFlag(TitleStyle.Underline) ? "U" : string.Empty)
                .Append(':').Append(part).Append(']');
        }

        return text.ToString();
    }

    [Theory]
    [InlineData("Buy milk", "Buy milk")]
    [InlineData("Buy **milk**", "Buy [B:milk]")]
    [InlineData("Buy *milk*", "Buy [I:milk]")]
    [InlineData("Buy ++milk++", "Buy [U:milk]")]
    [InlineData("***both***", "[BI:both]")]
    [InlineData("++**all *three***++", "[BU:all ][BIU:three]")]
    [InlineData("**bo**ld", "[B:bo]ld")]
    [InlineData("*a***b**", "[I:a][B:b]")]
    [InlineData("line one\n**line two**", "line one\n[B:line two]")]
    public void Markers_become_styles(string markup, string expected)
    {
        Assert.Equal(expected, Show(markup));
    }

    [Theory]
    [InlineData("3 * 4 * 5")]
    [InlineData("C++ and C++")]
    [InlineData("**open")]
    [InlineData("a + b")]
    [InlineData("**")]
    [InlineData("# not a heading, [no](link), <b>no html</b>")]
    public void Anything_but_a_paired_marker_is_text(string markup)
    {
        Assert.Equal(markup, Show(markup));
        Assert.Equal(markup, TitleMarkup.Plain(markup));
    }

    [Theory]
    [InlineData(@"\*not italic\*", "*not italic*")]
    [InlineData(@"C:\path", @"C:\path")]
    [InlineData(@"a\\*b*", @"a\[I:b]")]
    public void A_backslash_keeps_a_marker_plain(string markup, string expected)
    {
        Assert.Equal(expected, Show(markup));
    }

    [Theory]
    [InlineData("Buy **milk**")]
    [InlineData("*a***b**")]
    [InlineData("++**all *three***++")]
    [InlineData(@"\*stars\* and **bold**")]
    [InlineData("3 * 4 is **twelve**")]
    [InlineData(@"C:\path\*")]
    public void Writing_what_was_read_gives_the_same_styles(string markup)
    {
        ParsedTitle parsed = TitleMarkup.Parse(markup);
        string written = TitleMarkup.Write(parsed.Plain, parsed.Styles);

        Assert.Equal(Show(markup), Show(written));
    }

    [Fact]
    public void Text_that_reads_as_markers_is_escaped_when_written()
    {
        string written = TitleMarkup.Write("*a*", [TitleStyle.None, TitleStyle.None, TitleStyle.None]);

        Assert.Equal("*a*", TitleMarkup.Plain(written));
        Assert.Equal("*a*", Show(written));
    }

    [Theory]
    // A selection: exactly that part, without spaces at its ends.
    [InlineData("make this bold", 5, 4, TitleStyle.Bold, "make **this** bold", "make [B:this] bold")]
    [InlineData("make this bold", 4, 6, TitleStyle.Bold, "make **this** bold", "make [B:this] bold")]
    [InlineData("make this bold", 1, 2, TitleStyle.Underline, "m++ak++e this bold", "m[U:ak]e this bold")]
    // Pressed again: off.
    [InlineData("make **this** bold", 7, 4, TitleStyle.Bold, "make this bold", "make this bold")]
    // Combined.
    [InlineData("make **this** bold", 7, 4, TitleStyle.Italic, "make ***this*** bold", "make [BI:this] bold")]
    [InlineData("make ***this*** bold", 8, 4, TitleStyle.Bold, "make *this* bold", "make [I:this] bold")]
    [InlineData("make **this** bold", 7, 4, TitleStyle.Underline, "make ++**this**++ bold", "make [BU:this] bold")]
    // Partly styled: on for all of it first.
    [InlineData("**make** this", 0, 13, TitleStyle.Bold, "**make this**", "[B:make this]")]
    // Off for part of a styled stretch.
    [InlineData("**make this**", 7, 4, TitleStyle.Bold, "**make** this", "[B:make] this")]
    public void A_selection_toggles_exactly_that_part(string markup, int start, int length, TitleStyle style, string expected, string shown)
    {
        var result = TitleMarkup.Toggle(markup, start, length, style);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Value.Text);
        Assert.Equal(shown, Show(result.Value.Text));
    }

    [Fact]
    public void The_selection_afterwards_is_the_same_text_without_markers()
    {
        var result = TitleMarkup.Toggle("make this bold", 5, 4, TitleStyle.Bold)!.Value;

        Assert.Equal("this", result.Text.Substring(result.SelectionStart, result.SelectionLength));
    }

    [Fact]
    public void Without_a_selection_the_word_the_caret_is_in_toggles_and_the_caret_stays()
    {
        // "make th|is bold"
        var result = TitleMarkup.Toggle("make this bold", 7, 0, TitleStyle.Bold)!.Value;

        Assert.Equal("make **this** bold", result.Text);
        Assert.Equal(9, result.SelectionStart);
        Assert.Equal(0, result.SelectionLength);
        Assert.Equal("make [B:thxis] bold", Show(result.Text.Insert(result.SelectionStart, "x")));
    }

    [Theory]
    [InlineData("make this", 9)]
    [InlineData("make this", 5)]
    [InlineData("make this", 4)]
    [InlineData("", 0)]
    public void At_a_word_edge_or_the_end_the_style_is_for_what_is_typed_next(string markup, int caret)
    {
        Assert.Null(TitleMarkup.Toggle(markup, caret, 0, TitleStyle.Bold));
    }

    [Fact]
    public void Typed_text_gets_the_style_switched_for_it()
    {
        // "make |" with bold on for what comes next, then "it" typed.
        (string text, int caret) = TitleMarkup.StyleTyped("make it", 5, 2, TitleStyle.Bold);

        Assert.Equal("make **it**", text);
        Assert.Equal(9, caret);

        // Typing on stays inside the markers, so it stays bold.
        Assert.Equal("make [B:itself]", Show(text.Insert(caret, "self")));
    }

    [Fact]
    public void Bold_can_be_switched_off_for_what_is_typed_next()
    {
        // "**bold |**": the caret inside bold text, at a space, bold switched off, then "x" typed.
        (string text, _) = TitleMarkup.StyleTyped("**bold x**", 7, 1, TitleStyle.Bold);

        Assert.Equal("[B:bold] x", Show(text));
    }

    [Theory]
    [InlineData("make **this** bold", 8, 0, TitleStyle.Bold)]
    [InlineData("make **this** bold", 7, 4, TitleStyle.Bold)]
    [InlineData("make **this** bold", 0, 18, TitleStyle.None)]
    [InlineData("**bold x**", 7, 0, TitleStyle.Bold)]
    [InlineData("make ++***this***++", 11, 0, TitleStyle.Bold | TitleStyle.Italic | TitleStyle.Underline)]
    public void The_buttons_show_the_style_at_the_caret_or_of_the_selection(string markup, int start, int length, TitleStyle expected)
    {
        Assert.Equal(expected, TitleMarkup.StyleAt(markup, start, length));
    }
}
