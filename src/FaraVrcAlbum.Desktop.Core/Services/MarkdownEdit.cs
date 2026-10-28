namespace FaraVrcAlbum.Desktop.Services;

/// <summary>
/// 本文の装飾ボタン（太字・斜体・見出し・箇条書きなど）の編集の計算。選んでいる範囲に、Markdown の記号を付ける・外す。
/// 入力欄の文字を丸ごと入れ替えず、変わる範囲だけを <see cref="Result"/> で返す（TextBox の編集として 1 回分になり、Ctrl+Z で戻せる）。
/// サイトの本文は Markdown（CommonMark）なので、ここで付ける記号はサイトでもそのまま装飾になる。写真の行（![説明](URL)）は変えない。
/// </summary>
public static class MarkdownEdit
{
    /// <summary>置き換える範囲と、置き換えたあとに選ぶ範囲。</summary>
    public readonly record struct Result(int ReplaceStart, int ReplaceLength, string Replacement, int SelectStart, int SelectLength)
    {
        /// <summary>元の文字に当てはめた結果（テスト・確認用）。</summary>
        public string Apply(string text) => text[..ReplaceStart] + Replacement + text[(ReplaceStart + ReplaceLength)..];
    }

    /// <summary>選んでいる文字を prefix と suffix で囲む（すでに囲んであれば外す）。何も選んでいなければ placeholder を入れて、それを選ぶ。</summary>
    public static Result Wrap(string text, int start, int length, string prefix, string suffix, string placeholder)
    {
        Clamp(text, ref start, ref length);
        var selected = text.Substring(start, length);
        // 選んだ範囲に記号を含めて選んでいる（**太字** を丸ごと選んだ）→ 外す
        if (length >= prefix.Length + suffix.Length && selected.StartsWith(prefix, StringComparison.Ordinal) && selected.EndsWith(suffix, StringComparison.Ordinal)
            && !(prefix.Length == 1 && selected.Length > 1 && selected[1] == prefix[0]))
        {
            var inner = selected[prefix.Length..^suffix.Length];
            return new Result(start, length, inner, start, inner.Length);
        }
        // 記号のすぐ内側だけを選んでいる → 外す（斜体の * が、太字の ** の一部のときは外さない）
        if (length > 0 && start >= prefix.Length && start + length + suffix.Length <= text.Length
            && string.CompareOrdinal(text, start - prefix.Length, prefix, 0, prefix.Length) == 0
            && string.CompareOrdinal(text, start + length, suffix, 0, suffix.Length) == 0
            && !(prefix.Length == 1 && (start - 2 >= 0 && text[start - 2] == prefix[0] || start + length + 1 < text.Length && text[start + length + 1] == suffix[0])))
        {
            return new Result(start - prefix.Length, prefix.Length + length + suffix.Length, selected, start - prefix.Length, length);
        }
        var body = length > 0 ? selected : placeholder;
        return new Result(start, length, prefix + body + suffix, start + prefix.Length, body.Length);
    }

    /// <summary>選んでいる行（選んでいなければカーソルの行）の先頭に prefix を付ける。全部の行にすでに付いていれば外す。空の行と写真の行は変えない。</summary>
    public static Result ToggleLinePrefix(string text, int start, int length, string prefix)
    {
        Clamp(text, ref start, ref length);
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var end = start + length;
        if (length > 0 && end > 0 && text[end - 1] == '\n') end--;
        var lineEnd = text.IndexOf('\n', Math.Min(end, text.Length));
        if (lineEnd < 0) lineEnd = text.Length;
        var lines = text[lineStart..lineEnd].Split('\n');
        bool Editable(string line) => line.Trim().Length > 0 && !line.TrimStart().StartsWith("![", StringComparison.Ordinal);
        var targets = lines.Where(Editable).ToList();
        var remove = targets.Count > 0 && targets.All(l => l.StartsWith(prefix, StringComparison.Ordinal));
        for (var i = 0; i < lines.Length; i++)
        {
            if (!Editable(lines[i])) continue;
            lines[i] = remove ? lines[i][prefix.Length..] : prefix + lines[i];
        }
        var replacement = string.Join("\n", lines);
        return new Result(lineStart, lineEnd - lineStart, replacement, lineStart, replacement.Length);
    }

    /// <summary>
    /// 選んでいる行（カーソルの行）の文字の揃えを変える。:::left / :::center / :::right と :::（閉じる）で囲む（サイトでは揃えとして表示される）。
    /// すでに囲んであれば、同じ揃えなら外し、違う揃えなら付け替える。左揃えは囲みを外す。写真の行を含むときは変えない（本文が写真で分かれるため）。
    /// </summary>
    public static Result Align(string text, int start, int length, string align)
    {
        Clamp(text, ref start, ref length);
        var none = new Result(start, 0, "", start, length);
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var end = start + length;
        if (length > 0 && end > 0 && text[end - 1] == '\n') end--;
        var lineEnd = text.IndexOf('\n', Math.Min(end, text.Length));
        if (lineEnd < 0) lineEnd = text.Length;
        var block = text[lineStart..lineEnd];
        if (block.Split('\n').Any(l => l.TrimStart().StartsWith("![", StringComparison.Ordinal))) return none;

        // すでに囲んである（直前の行が :::揃え、直後の行が :::）か
        var prevEnd = lineStart - 1;                                                       // 直前の行の改行の位置
        var prevStart = prevEnd <= 0 ? 0 : text.LastIndexOf('\n', prevEnd - 1) + 1;
        var prevLine = prevEnd >= 0 ? text[prevStart..prevEnd].TrimEnd('\r') : "";
        var nextStart = lineEnd + 1;
        var nextEnd = nextStart <= text.Length ? text.IndexOf('\n', Math.Min(nextStart, text.Length)) : -1;
        if (nextEnd < 0) nextEnd = text.Length;
        var nextLine = nextStart <= text.Length ? text[Math.Min(nextStart, text.Length)..nextEnd].TrimEnd('\r') : "";
        var wrapped = prevEnd >= 0 && (prevLine is ":::left" or ":::center" or ":::right") && nextLine == ":::";
        if (wrapped)
        {
            var current = prevLine[3..];
            var inner = block;
            if (align == "left" || align == current) return new Result(prevStart, nextEnd - prevStart, inner, prevStart, inner.Length);   // 外す
            var replaced = ":::" + align + "\n" + inner + "\n:::";
            return new Result(prevStart, nextEnd - prevStart, replaced, prevStart, replaced.Length);
        }
        if (align == "left") return none;
        var added = ":::" + align + "\n" + block + "\n:::";
        return new Result(lineStart, lineEnd - lineStart, added, lineStart, added.Length);
    }

    /// <summary>[文字](https://) の形にする。選んでいる文字があればそれをリンクの文字にして、URL の部分を選ぶ。</summary>
    public static Result Link(string text, int start, int length)
    {
        Clamp(text, ref start, ref length);
        var label = length > 0 ? text.Substring(start, length) : "リンクの文字";
        const string Url = "https://";
        return new Result(start, length, $"[{label}]({Url})", start + label.Length + 3, Url.Length);
    }

    /// <summary>今の行のあとに、区切り線（---）を入れる（前後に空の行を置く）。</summary>
    public static Result InsertRule(string text, int start, int length)
    {
        Clamp(text, ref start, ref length);
        var at = text.IndexOf('\n', Math.Min(start + length, text.Length));
        if (at < 0) at = text.Length;
        const string Rule = "\n\n---\n";
        return new Result(at, 0, Rule, at + Rule.Length, 0);
    }

    private static void Clamp(string text, ref int start, ref int length)
    {
        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
    }
}
