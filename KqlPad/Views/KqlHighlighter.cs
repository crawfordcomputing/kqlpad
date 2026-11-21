using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KqlPad.Views;

/// <summary>Very small KQL tokenizer that paints a RichTextBox in the synthwave palette.</summary>
public static class KqlHighlighter
{
    static readonly string[] Keywords =
    [
        "where", "project", "project-away", "project-keep", "project-rename", "project-reorder", "extend", "summarize",
        "order", "sort", "by", "take", "limit", "top", "mv-expand", "mv-apply", "parse", "parse-where", "join", "union",
        "distinct", "count", "render", "let", "on", "kind", "as", "in", "has", "has_any", "has_all",
        "contains", "contains_cs", "startswith", "endswith", "matches", "regex", "and", "or", "not", "between",
        "asc", "desc", "print", "evaluate", "getschema", "search", "lookup", "invoke", "materialize",
        "datatable", "true", "false", "null", "dynamic", "withsource",
    ];

    static readonly Regex Tokens = new(
        @"(?<comment>//[^\r\n]*)" +
        @"|(?<string>'(?:[^'\\\r\n]|\\.)*'|""(?:[^""\\\r\n]|\\.)*"")" +
        @"|(?<number>\b\d+(?:\.\d+)?(?:ms|s|m|h|d)?\b)" +
        @"|(?<keyword>\b(?:" + string.Join("|", Keywords.OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")\b)" +
        @"|(?<func>\b[A-Za-z_]\w*(?=\())" +
        @"|(?<pipe>\|)" +
        @"|(?<ident>[A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static Brush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }

    static readonly Brush Plain = B("#E4E6FF");
    static readonly Brush KeywordBrush = B("#FEDE5D");
    static readonly Brush FuncBrush = B("#36F9F6");
    static readonly Brush StringBrush = B("#FF8B39");
    static readonly Brush NumberBrush = B("#F97E72");
    static readonly Brush PipeBrush = B("#FF7EDB");
    static readonly Brush TableBrush = B("#72F1B8");
    static readonly Brush CommentBrush = B("#6E76A8");

    public static void Render(RichTextBox box, string text)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        var firstIdentSeen = false;
        var afterSemicolon = false;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var li = 0; li < lines.Length; li++)
        {
            if (li > 0) paragraph.Inlines.Add(new LineBreak());
            var line = lines[li];
            var pos = 0;
            foreach (Match m in Tokens.Matches(line))
            {
                if (m.Index > pos) Add(paragraph, line[pos..m.Index], Plain);
                pos = m.Index + m.Length;

                if (m.Groups["comment"].Success) Add(paragraph, m.Value, CommentBrush, italic: true);
                else if (m.Groups["string"].Success) Add(paragraph, m.Value, StringBrush);
                else if (m.Groups["number"].Success) Add(paragraph, m.Value, NumberBrush);
                else if (m.Groups["keyword"].Success) { Add(paragraph, m.Value, KeywordBrush); }
                else if (m.Groups["func"].Success) Add(paragraph, m.Value, FuncBrush);
                else if (m.Groups["pipe"].Success) Add(paragraph, m.Value, PipeBrush, bold: true);
                else
                {
                    // the first identifier of a statement is the table being queried
                    var isTable = !firstIdentSeen || afterSemicolon;
                    Add(paragraph, m.Value, isTable ? TableBrush : Plain, bold: isTable);
                    firstIdentSeen = true;
                    afterSemicolon = false;
                }
            }
            if (pos < line.Length) Add(paragraph, line[pos..], Plain);
            if (line.TrimEnd().EndsWith(';')) afterSemicolon = true;
        }

        // wide enough for the longest line so the horizontal scrollbar only appears when needed
        var longest = lines.Max(l => l.Length);
        var doc = new FlowDocument(paragraph)
        {
            PagePadding = new Thickness(0),
            PageWidth = Math.Max(200, longest * box.FontSize * 0.62 + 30),
            FontFamily = box.FontFamily,
            FontSize = box.FontSize,
            Foreground = Plain,
        };
        box.Document = doc;
    }

    static void Add(Paragraph p, string text, Brush brush, bool bold = false, bool italic = false)
    {
        var run = new Run(text) { Foreground = brush };
        if (bold) run.FontWeight = FontWeights.SemiBold;
        if (italic) run.FontStyle = FontStyles.Italic;
        p.Inlines.Add(run);
    }
}
