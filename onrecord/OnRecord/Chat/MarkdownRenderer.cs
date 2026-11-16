using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;
using WpfTable = System.Windows.Documents.Table;

namespace OnRecord.Chat;

/// <summary>Renders Markdown into a native WPF FlowDocument (no web view). Dark-glass styling.</summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UsePipeTables()
        .UseAutoLinks()
        .UseTaskLists()
        .Build();

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    private static readonly Brush Text = Frozen(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Muted = Frozen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
    private static readonly Brush CodeBg = Frozen(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
    private static readonly Brush InlineCodeBg = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Rule = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Link = Frozen(Color.FromRgb(0x9C, 0xC8, 0xFF));

    public static FlowDocument Render(string markdown)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14.5,
            Foreground = Text,
            LineHeight = 22,
            TextAlignment = TextAlignment.Left,
        };
        var md = Markdown.Parse(markdown ?? "", Pipeline);
        foreach (var block in md)
            if (RenderBlock(block) is { } b) doc.Blocks.Add(b);
        // Tight outer spacing: no gap above the first / below the last block.
        if (doc.Blocks.FirstBlock is { } first) first.Margin = new Thickness(first.Margin.Left, 0, first.Margin.Right, first.Margin.Bottom);
        if (doc.Blocks.LastBlock is { } last) last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        return doc;
    }

    private static WpfBlock? RenderBlock(MdBlock block)
    {
        switch (block)
        {
            case HeadingBlock h:
            {
                var p = new Paragraph { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) };
                p.FontSize = h.Level switch { 1 => 20, 2 => 18, 3 => 16, _ => 15 };
                AddInlines(p.Inlines, h.Inline);
                return p;
            }
            case ParagraphBlock pb:
            {
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
                AddInlines(p.Inlines, pb.Inline);
                return p;
            }
            case FencedCodeBlock or CodeBlock:
            {
                var code = (LeafBlock)block;
                var text = code.Lines.ToString().TrimEnd('\n', '\r');
                var p = new Paragraph(new Run(text))
                {
                    FontFamily = Mono,
                    FontSize = 13,
                    LineHeight = 19,
                    Background = CodeBg,
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 2, 0, 12),
                };
                return p;
            }
            case QuoteBlock q:
            {
                var s = new Section
                {
                    BorderBrush = Rule,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(12, 0, 0, 0),
                    Margin = new Thickness(0, 0, 0, 10),
                    Foreground = Muted,
                };
                foreach (var child in q) if (RenderBlock(child) is { } b) s.Blocks.Add(b);
                return s;
            }
            case ListBlock list:
            {
                var l = new List
                {
                    MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(22, 0, 0, 0),
                };
                if (list.IsOrdered && int.TryParse(list.OrderedStart, out int start)) l.StartIndex = start;
                foreach (var item in list)
                {
                    var li = new ListItem();
                    if (item is ListItemBlock lib)
                        foreach (var child in lib)
                            if (RenderBlock(child) is { } b)
                            {
                                b.Margin = new Thickness(0, 0, 0, 4);
                                li.Blocks.Add(b);
                            }
                    l.ListItems.Add(li);
                }
                return l;
            }
            case ThematicBreakBlock:
                return new Paragraph { BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 6, 0, 12), FontSize = 2 };
            case Markdig.Extensions.Tables.Table table:
                return RenderTable(table);
            case HtmlBlock html:
                return new Paragraph(new Run(html.Lines.ToString())) { Foreground = Muted, Margin = new Thickness(0, 0, 0, 10) };
            case ContainerBlock container:
            {
                var s = new Section();
                foreach (var child in container) if (RenderBlock(child) is { } b) s.Blocks.Add(b);
                return s;
            }
            case LeafBlock leaf when leaf.Inline != null:
            {
                var p = new Paragraph();
                AddInlines(p.Inlines, leaf.Inline);
                return p;
            }
        }
        return null;
    }

    private static WpfBlock RenderTable(Markdig.Extensions.Tables.Table table)
    {
        var t = new WpfTable { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 12), BorderBrush = Rule, BorderThickness = new Thickness(1) };
        var group = new TableRowGroup();
        int cols = 0;
        foreach (var r in table) if (r is Markdig.Extensions.Tables.TableRow tr) cols = Math.Max(cols, tr.Count);
        for (int i = 0; i < cols; i++) t.Columns.Add(new TableColumn());
        foreach (var r in table)
        {
            if (r is not Markdig.Extensions.Tables.TableRow row) continue;
            var wr = new System.Windows.Documents.TableRow();
            if (row.IsHeader) wr.FontWeight = FontWeights.SemiBold;
            foreach (var c in row)
            {
                var cell = new System.Windows.Documents.TableCell { BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(8, 4, 8, 4) };
                if (c is Markdig.Extensions.Tables.TableCell mdCell)
                    foreach (var child in mdCell)
                        if (RenderBlock(child) is { } b) { b.Margin = new Thickness(0); cell.Blocks.Add(b); }
                wr.Cells.Add(cell);
            }
            group.Rows.Add(wr);
        }
        t.RowGroups.Add(group);
        return t;
    }

    private static void AddInlines(InlineCollection target, ContainerInline? container)
    {
        if (container == null) return;
        foreach (var inline in container)
            if (RenderInline(inline) is { } i) target.Add(i);
    }

    private static WpfInline? RenderInline(MdInline inline)
    {
        switch (inline)
        {
            case LiteralInline lit:
                return new Run(lit.Content.ToString());
            case CodeInline code:
                return new Run(code.Content) { FontFamily = Mono, FontSize = 13, Background = InlineCodeBg };
            case LineBreakInline lb:
                return lb.IsHard ? new LineBreak() : new Run(" ");
            case EmphasisInline em:
            {
                var span = new Span();
                if (em.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                else if (em.DelimiterCount >= 2) span.FontWeight = FontWeights.SemiBold;
                else span.FontStyle = FontStyles.Italic;
                AddInlines(span.Inlines, em);
                return span;
            }
            case LinkInline link when link.IsImage:
                return new Run($"[image: {link.Url}]") { Foreground = Muted };
            case LinkInline link:
            {
                var h = new Hyperlink { Foreground = Link, TextDecorations = null, Cursor = System.Windows.Input.Cursors.Hand };
                AddInlines(h.Inlines, link);
                if (h.Inlines.Count == 0) h.Inlines.Add(new Run(link.Url));
                AttachLink(h, link.Url);
                return h;
            }
            case AutolinkInline auto:
            {
                var h = new Hyperlink(new Run(auto.Url)) { Foreground = Link, TextDecorations = null };
                AttachLink(h, auto.IsEmail ? "mailto:" + auto.Url : auto.Url);
                return h;
            }
            case TaskList task:
                return new Run(task.Checked ? "☑ " : "☐ ");
            case HtmlInline html:
                return new Run(html.Tag) { Foreground = Muted };
            case HtmlEntityInline entity:
                return new Run(entity.Transcoded.ToString());
            case ContainerInline container:
            {
                var span = new Span();
                AddInlines(span.Inlines, container);
                return span;
            }
        }
        return null;
    }

    private static void AttachLink(Hyperlink h, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        h.ToolTip = url;
        h.Click += (_, _) =>
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https" or "mailto"))
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        };
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
