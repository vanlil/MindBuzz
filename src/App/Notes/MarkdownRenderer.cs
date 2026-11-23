using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Notes;

/// <summary>
/// Turns a Markdown note into Avalonia controls for the topic panel's preview: CommonMark plus
/// GitHub-style tables, task lists and strikethrough. Parsing is Markdig's; the rendering is ours, because no
/// maintained renderer supports Avalonia 12 trimmed and AOT.
/// <para>
/// Raw HTML is never rendered: the pipeline turns it into literal text. Images are never loaded (no
/// network); their alternative text is shown instead. Only links that pass <see cref="LinkUrl.IsAllowed"/>
/// are clickable. Colours come from styles on the <c>md…</c> classes in <c>Themes/Controls.axaml</c>, so a
/// theme change needs no re-render.
/// </para>
/// </summary>
internal static class MarkdownRenderer
{
    /// <summary>The note dialect: CommonMark, pipe tables, task lists, ~~strikethrough~~, and HTML shown as
    /// text.</summary>
    internal static MarkdownPipeline Pipeline { get; } = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .DisableHtml()
        .Build();

    /// <summary>The preview of <paramref name="markdown"/>: a vertical stack of blocks.</summary>
    /// <param name="markdown">The note, verbatim.</param>
    /// <param name="openLink">Called with the address when an allowed link is clicked.</param>
    internal static StackPanel Render(string markdown, Action<string> openLink)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(openLink);

        var renderer = new Renderer(openLink);
        StackPanel root;
        try
        {
            root = renderer.Blocks(Markdown.Parse(markdown, Pipeline));
        }
        catch (ArgumentException)
        {
            // Markdig refuses blocks nested 128 deep ("too deeply nested"), which no real note has: show the text.
            root = new StackPanel { Children = { Plain(markdown) } };
        }

        root.Classes.Add("mdDocument");
        return root;
    }

    /// <summary>
    /// The deepest emphasis, link or span nesting drawn as such; anything deeper is shown as its plain text. Markdig
    /// nests "***…x…***" one level per two stars without a limit, and 16,000 stars on each side overflowed the
    /// stack (Avalonia 12.1.6, Markdig 1.6.0), which ends the process without a recovery write.
    /// </summary>
    internal const int MaxInlineDepth = 32;

    private static SelectableTextBlock Plain(string text) => new() { Text = text, Classes = { "mdParagraph" }, TextWrapping = TextWrapping.Wrap };

    private sealed class Renderer(Action<string> openLink)
    {
        private int _inlineDepth;

        public StackPanel Blocks(ContainerBlock container)
        {
            var panel = new StackPanel { Spacing = 8 };
            foreach (Block block in container)
            {
                if (Block(block) is { } control)
                {
                    panel.Children.Add(control);
                }
            }

            return panel;
        }

        private Control? Block(Block block) => block switch
        {
            HeadingBlock heading => Heading(heading),
            ParagraphBlock paragraph => Text(paragraph.Inline, "mdParagraph"),
            ListBlock list => List(list),
            QuoteBlock quote => Quote(quote),
            FencedCodeBlock or CodeBlock => Code((LeafBlock)block),
            ThematicBreakBlock => new Border { Classes = { "mdRule" } },
            Table table => Table(table),
            LinkReferenceDefinitionGroup => null,
            ContainerBlock container => Blocks(container),
            LeafBlock leaf => Plain(Lines(leaf)),
            _ => null,
        };

        private SelectableTextBlock Heading(HeadingBlock heading)
        {
            SelectableTextBlock text = Text(heading.Inline, "mdHeading");
            text.Classes.Add("mdH" + Math.Clamp(heading.Level, 1, 6).ToString(CultureInfo.InvariantCulture));
            return text;
        }

        private Grid List(ListBlock list)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Classes = { "mdList" } };
            int number = list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int start) ? start : 1;
            int row = 0;

            foreach (Block child in list)
            {
                if (child is not ListItemBlock item)
                {
                    continue;
                }

                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                Control marker = TaskOf(item) is { } task
                    ? new CheckBox { IsChecked = task.Checked, IsHitTestVisible = false, Focusable = false, Classes = { "mdTask" } }
                    : new TextBlock
                    {
                        Text = list.IsOrdered ? string.Create(CultureInfo.CurrentCulture, $"{number}.") : "•",
                        Classes = { "mdBullet" },
                    };
                Grid.SetRow(marker, row);
                grid.Children.Add(marker);

                StackPanel content = Blocks(item);
                content.Spacing = 4;
                Grid.SetRow(content, row);
                Grid.SetColumn(content, 1);
                grid.Children.Add(content);

                number++;
                row++;
            }

            return grid;
        }

        /// <summary>A task list item starts its first paragraph with a <see cref="TaskList"/> inline.</summary>
        private static TaskList? TaskOf(ListItemBlock item) =>
            item.Count > 0 && item[0] is ParagraphBlock { Inline.FirstChild: TaskList task } ? task : null;

        private Border Quote(QuoteBlock quote) => new()
        {
            Classes = { "mdQuote" },
            Child = Blocks(quote),
        };

        private static Border Code(LeafBlock block) => new()
        {
            Classes = { "mdCodeBlock" },
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = new SelectableTextBlock { Text = Lines(block), Classes = { "mdCode" } },
            },
        };

        /// <summary>
        /// A grid of cells; each cell draws its top and left edge, the frame the right and bottom one. A table wider
        /// than the panel scrolls sideways, like a code block, instead of being cut off.
        /// </summary>
        private ScrollViewer Table(Table table)
        {
            int columns = table.ColumnDefinitions.Count;
            var grid = new Grid { Classes = { "mdTable" } };
            for (int i = 0; i < columns; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            }

            int row = 0;
            foreach (Block child in table)
            {
                if (child is not TableRow tableRow)
                {
                    continue;
                }

                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                int column = 0;
                foreach (Block cellBlock in tableRow)
                {
                    if (cellBlock is not TableCell cell || column >= columns)
                    {
                        continue;
                    }

                    var content = Blocks(cell);
                    content.HorizontalAlignment = table.ColumnDefinitions[column].Alignment switch
                    {
                        TableColumnAlign.Center => HorizontalAlignment.Center,
                        TableColumnAlign.Right => HorizontalAlignment.Right,
                        _ => HorizontalAlignment.Left,
                    };

                    var border = new Border { Classes = { "mdCell" }, Child = content };
                    if (tableRow.IsHeader)
                    {
                        border.Classes.Add("mdHeader");
                    }

                    Grid.SetRow(border, row);
                    Grid.SetColumn(border, column);
                    Grid.SetColumnSpan(border, Math.Max(1, cell.ColumnSpan));
                    grid.Children.Add(border);
                    column += Math.Max(1, cell.ColumnSpan);
                }

                row++;
            }

            return new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = new Border { Classes = { "mdTableFrame" }, HorizontalAlignment = HorizontalAlignment.Left, Child = grid },
            };
        }

        private SelectableTextBlock Text(ContainerInline? inline, string cssClass)
        {
            var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Classes = { cssClass } };
            if (inline is not null)
            {
                text.Inlines ??= [];
                Add(text.Inlines, inline);
            }

            return text;
        }

        private void Add(InlineCollection target, ContainerInline container)
        {
            if (_inlineDepth >= MaxInlineDepth)
            {
                target.Add(new Run(PlainText(container)));
                return;
            }

            _inlineDepth++;
            try
            {
                foreach (Markdig.Syntax.Inlines.Inline inline in container)
                {
                    if (Inline(inline) is { } converted)
                    {
                        target.Add(converted);
                    }
                }
            }
            finally
            {
                _inlineDepth--;
            }
        }

        private Avalonia.Controls.Documents.Inline? Inline(Markdig.Syntax.Inlines.Inline inline) => inline switch
        {
            LiteralInline literal => new Run(literal.Content.ToString()),
            CodeInline code => new Run(code.Content) { Classes = { "mdCodeInline" } },
            LineBreakInline lineBreak => lineBreak.IsHard ? new LineBreak() : new Run(" "),
            HtmlEntityInline entity => new Run(entity.Transcoded.ToString()),
            HtmlInline html => new Run(html.Tag),
            AutolinkInline autolink => Link(autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url, new Run(autolink.Url)),
            LinkInline { IsImage: true } image => new Run(string.Format(CultureInfo.CurrentCulture, Localization.Strings.Notes_ImageFormat, PlainText(image))) { Classes = { "mdImage" } },
            LinkInline link => Link(link.Url, Span(link, "mdLinkText")),
            EmphasisInline emphasis => Emphasis(emphasis),
            TaskList => null,
            ContainerInline container => Span(container, "mdSpan"),
            _ => new Run(inline.ToString()),
        };

        private Span Emphasis(EmphasisInline emphasis)
        {
            Span span = emphasis.DelimiterChar == '~'
                ? new Span { TextDecorations = TextDecorations.Strikethrough }
                : emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
            Add(span.Inlines, emphasis);
            return span;
        }

        private Span Span(ContainerInline container, string cssClass)
        {
            var span = new Span { Classes = { cssClass } };
            Add(span.Inlines, container);
            return span;
        }

        /// <summary>
        /// A link: a focusable link button when the address is allowed, so the keyboard reaches it too; otherwise
        /// the text alone, because a link the app refuses to open must not look clickable.
        /// </summary>
        private Avalonia.Controls.Documents.Inline Link(string? url, Avalonia.Controls.Documents.Inline content)
        {
            string address = url?.Trim() ?? string.Empty;
            if (!LinkUrl.IsAllowed(address))
            {
                return content;
            }

            var label = new TextBlock { Inlines = [content], TextWrapping = TextWrapping.Wrap };
            var button = new Button { Classes = { "link", "mdLink" }, Content = label };
            ToolTip.SetTip(button, address);
            Avalonia.Automation.AutomationProperties.SetName(button, address);
            button.Click += (_, _) => openLink(address);
            return new InlineUIContainer(button) { BaselineAlignment = BaselineAlignment.Bottom };
        }

        /// <summary>The literal text inside <paramref name="container"/>, walked without recursion, however
        /// deep.</summary>
        private static string PlainText(ContainerInline container)
        {
            var text = new StringBuilder();
            var pending = new Stack<Markdig.Syntax.Inlines.Inline>();
            for (Markdig.Syntax.Inlines.Inline? child = container.LastChild; child is not null; child = child.PreviousSibling)
            {
                pending.Push(child);
            }

            while (pending.TryPop(out Markdig.Syntax.Inlines.Inline? inline))
            {
                if (inline is LiteralInline literal)
                {
                    text.Append(literal.Content.ToString());
                }
                else if (inline is ContainerInline inner)
                {
                    for (Markdig.Syntax.Inlines.Inline? child = inner.LastChild; child is not null; child = child.PreviousSibling)
                    {
                        pending.Push(child);
                    }
                }
            }

            return text.ToString();
        }

        private static string Lines(LeafBlock block)
        {
            var text = new StringBuilder();
            foreach (Markdig.Helpers.StringLine line in block.Lines.Lines.Take(block.Lines.Count))
            {
                if (text.Length > 0)
                {
                    text.Append('\n');
                }

                text.Append(line.Slice.ToString());
            }

            return text.ToString();
        }
    }
}
