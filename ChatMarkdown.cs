using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace EasyEngine
{
    /// <summary>The AI's answers in the chat use Markdown (**bold**, *italic*, `code`, # titles, - lists): shown as real
    /// formatting instead of the raw asterisks. Underscores are left alone (they are in object and variable names).</summary>
    public static class ChatMarkdown
    {
        private static readonly Regex Inline = new(@"\*\*(.+?)\*\*|`([^`]+)`|(?<![\*\w])\*(?!\s)([^*\n]+?)(?<!\s)\*(?![\*\w])", RegexOptions.Compiled);
        private static readonly Regex Heading = new(@"^\s{0,3}#{1,6}\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex Bullet = new(@"^(\s*)[-*+]\s+(.*)$", RegexOptions.Compiled);
        private static readonly FontFamily Mono = new("Consolas");

        public static FlowDocument ToDocument(string text, Brush fg)
        {
            var doc = new FlowDocument { PagePadding = new Thickness(0), Foreground = fg, FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5, TextAlignment = TextAlignment.Left };
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                string line = raw;
                if (Heading.Match(line) is { Success: true } h) { p.FontWeight = FontWeights.Bold; p.Margin = new Thickness(0, 4, 0, 2); line = h.Groups[1].Value; }
                else if (Bullet.Match(line) is { Success: true } b)
                {
                    p.Margin = new Thickness(8 + b.Groups[1].Value.Length * 6, 0, 0, 2);
                    p.Inlines.Add(new Run("• "));
                    line = b.Groups[2].Value;
                }
                foreach (var inl in Inlines(line)) p.Inlines.Add(inl);
                doc.Blocks.Add(p);
            }
            return doc;
        }

        /// <summary>The line's text as runs (bold / italic / code); any "**" left over is dropped.</summary>
        public static List<Inline> Inlines(string line)
        {
            var list = new List<Inline>();
            int at = 0;
            foreach (Match m in Inline.Matches(line))
            {
                if (m.Index > at) list.Add(new Run(Clean(line[at..m.Index])));
                if (m.Groups[1].Success) list.Add(new Bold(new Run(Clean(m.Groups[1].Value))));
                else if (m.Groups[2].Success) list.Add(new Run(m.Groups[2].Value) { FontFamily = Mono });
                else list.Add(new Italic(new Run(Clean(m.Groups[3].Value))));
                at = m.Index + m.Length;
            }
            if (at < line.Length) list.Add(new Run(Clean(line[at..])));
            return list;
        }

        private static string Clean(string s) => s.Replace("**", "");

        /// <summary>The text as the person reads it (tests).</summary>
        public static string Plain(FlowDocument d) => new TextRange(d.ContentStart, d.ContentEnd).Text.Replace("\r", "").TrimEnd('\n');
    }
}
