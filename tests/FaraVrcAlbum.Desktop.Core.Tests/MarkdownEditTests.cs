using FaraVrcAlbum.Desktop.Services;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>本文の装飾ボタン（太字・斜体・見出し・箇条書き・リンク・区切り線）の編集の計算。</summary>
public sealed class MarkdownEditTests
{
    private static string Do(string text, int start, int length, Func<string, int, int, MarkdownEdit.Result> edit, out string selected)
    {
        var r = edit(text, start, length);
        var result = r.Apply(text);
        selected = result.Substring(r.SelectStart, r.SelectLength);
        return result;
    }

    [Fact]
    public void BoldWrapsTheSelectionAndSelectingItAgainRemovesIt()
    {
        var text = "今日は晴れでした。";
        var wrapped = Do(text, 0, 3, (t, s, l) => MarkdownEdit.Wrap(t, s, l, "**", "**", "太字"), out var selected);
        Assert.Equal("**今日は**晴れでした。", wrapped);
        Assert.Equal("今日は", selected);                                                // 囲んだ文字が選ばれている（続けて押すと外れる）

        var unwrapped = Do(wrapped, 2, 3, (t, s, l) => MarkdownEdit.Wrap(t, s, l, "**", "**", "太字"), out selected);   // 内側だけを選んで押す
        Assert.Equal(text, unwrapped);
        Assert.Equal(text, Do(wrapped, 0, 7, (t, s, l) => MarkdownEdit.Wrap(t, s, l, "**", "**", "太字"), out _));       // 記号ごと選んで押す
    }

    [Fact]
    public void WithoutASelectionAPlaceholderIsInsertedAndSelected()
    {
        var result = Do("あ", 1, 0, (t, s, l) => MarkdownEdit.Wrap(t, s, l, "*", "*", "斜体にする文字"), out var selected);
        Assert.Equal("あ*斜体にする文字*", result);
        Assert.Equal("斜体にする文字", selected);
    }

    [Fact]
    public void ItalicInsideBoldIsNotTreatedAsRemovingTheBold()
    {
        var bold = "**強調**";
        var result = Do(bold, 2, 2, (t, s, l) => MarkdownEdit.Wrap(t, s, l, "*", "*", "x"), out _);
        Assert.Equal("***強調***", result);                                              // 太字の ** を、斜体の * として外さない
    }

    [Fact]
    public void LinePrefixesAreAddedToEverySelectedLineAndToggledOffAndPhotoLinesAreKept()
    {
        var text = "一行目\n二行目\n![説明](https://x/a_1280.webp)\n四行目";
        var added = Do(text, 0, text.Length, (t, s, l) => MarkdownEdit.ToggleLinePrefix(t, s, l, "- "), out _);
        Assert.Equal("- 一行目\n- 二行目\n![説明](https://x/a_1280.webp)\n- 四行目", added);      // 写真の行は変えない
        var removed = Do(added, 0, added.Length, (t, s, l) => MarkdownEdit.ToggleLinePrefix(t, s, l, "- "), out _);
        Assert.Equal(text, removed);

        var heading = Do("あ\nい", 2, 0, (t, s, l) => MarkdownEdit.ToggleLinePrefix(t, s, l, "## "), out _);   // カーソルの行だけ
        Assert.Equal("あ\n## い", heading);
        Assert.Equal("> 引用", Do("引用", 0, 0, (t, s, l) => MarkdownEdit.ToggleLinePrefix(t, s, l, "> "), out _));
    }

    [Fact]
    public void LinkKeepsTheSelectionAsTheLabelAndSelectsTheUrlPart()
    {
        var result = Do("ここを見てください", 0, 3, MarkdownEdit.Link, out var selected);
        Assert.Equal("[ここを](https://)見てください", result);
        Assert.Equal("https://", selected);
    }

    [Fact]
    public void ARuleIsInsertedAfterTheCurrentLineWithBlankLinesAround()
    {
        var result = Do("前\n後", 0, 0, MarkdownEdit.InsertRule, out _);
        Assert.Equal("前\n\n---\n\n後", result);
    }

    [Fact]
    public void OutOfRangeSelectionsAreClamped()
    {
        Assert.Equal("**x**", Do("", 5, 9, (t, s, l) => MarkdownEdit.Wrap(t, s, l, "**", "**", "x"), out _));
    }

    [Fact]
    public void AlignmentWrapsTheLinesInAFenceAndSwitchesOrRemovesItAndLeavesPhotoLinesAlone()
    {
        const string text = """
            前
            中央にしたい文
            後
            """;
        var centered = Do(text, 2, 0, (t, s, l) => MarkdownEdit.Align(t, s, l, "center"), out _);
        Assert.Equal("""
            前
            :::center
            中央にしたい文
            :::
            後
            """, centered);

        var cursor = centered.IndexOf("中央", StringComparison.Ordinal);
        Assert.Equal("""
            前
            :::right
            中央にしたい文
            :::
            後
            """, Do(centered, cursor, 0, (t, s, l) => MarkdownEdit.Align(t, s, l, "right"), out _));   // 付け替え
        Assert.Equal(text, Do(centered, cursor, 0, (t, s, l) => MarkdownEdit.Align(t, s, l, "left"), out _));                                 // 左揃え = 外す
        Assert.Equal(text, Do(centered, cursor, 0, (t, s, l) => MarkdownEdit.Align(t, s, l, "center"), out _));                               // 同じ揃えをもう一度 = 外す

        var photo = "![説明](https://x/a_1280.webp)";
        Assert.Equal(photo, Do(photo, 0, 0, (t, s, l) => MarkdownEdit.Align(t, s, l, "center"), out _));                                       // 写真の行は変えない
        Assert.Equal("左のまま", Do("左のまま", 0, 0, (t, s, l) => MarkdownEdit.Align(t, s, l, "left"), out _));
    }

    [Fact]
    public void UnderlineAndStrikethroughUseThePlusAndTildeMarks()
    {
        Assert.Equal("++下++線", Do("下線", 0, 1, (t, s, l) => MarkdownEdit.Wrap(t, s, l, "++", "++", "x"), out _));
        Assert.Equal("~~消~~す", Do("消す", 0, 1, (t, s, l) => MarkdownEdit.Wrap(t, s, l, "~~", "~~", "x"), out _));
    }
}
