using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace FaraVrcAlbum.Desktop.Converters;

/// <summary>
/// プレビュー用の、ごく簡単な Markdown の表示（TextBlock.Inlines を作る）。**太字**・*斜体*・[文字](URL)（下線だけ）・見出し（# ）・箇条書き（- ）・引用（> ）・区切り線（---）・改行。
/// サイトの見た目の目安なので、すべての書き方には対応しない（サイトでは正式な Markdown として表示される）。
/// </summary>
public static partial class MarkdownLite
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(MarkdownLite), new PropertyMetadata("", OnTextChanged));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);

    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);

    [GeneratedRegex(@"\*\*(?<b>.+?)\*\*|~~(?<s>.+?)~~|\+\+(?<u>.+?)\+\+|\*(?<i>[^*\s][^*]*?)\*|\[(?<link>[^\]]+)\]\([^)]*\)")]
    private static partial Regex Inline();

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        block.Inlines.Clear();
        var text = (e.NewValue as string ?? "").Replace("\r\n", "\n");
        var heading = Regex.Match(text, @"^(#{1,3})\s+(.*)$", RegexOptions.Singleline);
        if (heading.Success)
        {
            block.Inlines.Add(new Run(heading.Groups[2].Value) { FontWeight = FontWeights.SemiBold, FontSize = heading.Groups[1].Length == 1 ? 20 : 17 });
            return;
        }
        var lines = text.Split('\n');
        for (var l = 0; l < lines.Length; l++)
        {
            if (l > 0) block.Inlines.Add(new LineBreak());
            var line = lines[l];
            // 見出し・区切り線・引用・箇条書きの行（装飾ボタンで付ける記号）を、見た目の目安に直す
            var headingLine = Regex.Match(line, @"^(#{1,3})\s+(.*)$");
            if (headingLine.Success)
            {
                block.Inlines.Add(new Run(headingLine.Groups[2].Value) { FontWeight = FontWeights.SemiBold, FontSize = headingLine.Groups[1].Length == 1 ? 20 : 17 });
                continue;
            }
            if (Regex.IsMatch(line, @"^\s*(---+|\*\*\*+)\s*$"))
            {
                block.Inlines.Add(new Run("────────────────") { Foreground = System.Windows.Media.Brushes.Gray });
                continue;
            }
            var quoted = line.StartsWith("> ", StringComparison.Ordinal);
            if (quoted) line = line[2..];
            var bullet = Regex.Match(line, @"^[-*]\s+(.*)$");
            if (bullet.Success) line = "・" + bullet.Groups[1].Value;
            var lineStartInlines = block.Inlines.Count;
            var at = 0;
            foreach (Match m in Inline().Matches(line))
            {
                if (m.Index > at) block.Inlines.Add(new Run(line[at..m.Index]));
                if (m.Groups["b"].Success) block.Inlines.Add(new Run(m.Groups["b"].Value) { FontWeight = FontWeights.Bold });
                else if (m.Groups["s"].Success) block.Inlines.Add(new Run(m.Groups["s"].Value) { TextDecorations = TextDecorations.Strikethrough });
                else if (m.Groups["u"].Success) block.Inlines.Add(new Run(m.Groups["u"].Value) { TextDecorations = TextDecorations.Underline });
                else if (m.Groups["i"].Success) block.Inlines.Add(new Run(m.Groups["i"].Value) { FontStyle = FontStyles.Italic });
                else block.Inlines.Add(new Run(m.Groups["link"].Value) { TextDecorations = TextDecorations.Underline });
                at = m.Index + m.Length;
            }
            if (at < line.Length) block.Inlines.Add(new Run(line[at..]));
            if (quoted)
            {
                // 引用は、灰色の斜体にする
                foreach (var inline in block.Inlines.Skip(lineStartInlines).OfType<Run>().ToList())
                {
                    inline.Foreground = System.Windows.Media.Brushes.Gray;
                    inline.FontStyle = FontStyles.Italic;
                }
            }
        }
    }
}
