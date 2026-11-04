using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Desktop.Services;

/// <summary>記事の 1 つの入力欄の中身を分けた結果。Errors が空なら保存できる。</summary>
public sealed record ArticleParts(
    string Body,
    string Afterword,
    IReadOnlyList<(string Key, string Caption, string Text)> Photos,
    IReadOnlyList<string> Errors);

/// <summary>
/// 記事の本文を 1 つの入力欄で書くための形（WordPress の本文のように、文章と写真を並べて書く）。
///   写真は 1 行に 1 枚: ![写真の説明](写真の URL)。URL はアップロード済みの写真のもの（記事の写真だけ）。
///   最初の写真より前の文章 → はじめの文章（body）、写真と写真の間の文章 → 後ろの写真の前の文章（text）、最後の写真のあと → おわりの文章（afterword）。
///   写真の行を動かすと、記事の写真の並びも変わる。記事の写真は全部、1 回ずつ書く（足す・外すはできない）。
/// </summary>
public static partial class ArticleDocument
{
    [GeneratedRegex(@"^\s*!\[(?<alt>[^\]\r\n]*)\]\((?<url>[^)\s]+)\)\s*$")]
    private static partial Regex ImageLine();

    /// <summary>写真の下の自動の行（日付：…・服：…・ワールド：…）。記事の項目ではなく、写真の情報から作る行なので、保存のときは読み捨てる。</summary>
    [GeneratedRegex(@"^\s*(日付|服|ワールド)\s*[：:]")]
    private static partial Regex AutoLine();

    /// <summary>写真の下に出す、自動の行。分からない項目（服・ワールド）の行は出さない。</summary>
    public static IReadOnlyList<string> AutoLines(PhotoEditInfo photo)
    {
        var lines = new List<string>();
        if (DateTimeOffset.TryParse(photo.TakenAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var taken))
            lines.Add("日付：" + taken.ToOffset(PhotoKeys.Jst).ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture));
        if (photo.Wear.Count > 0) lines.Add("服：" + string.Join("、", photo.Wear));
        if (photo.WorldName is { Length: > 0 } world) lines.Add("ワールド：" + world);
        return lines;
    }

    /// <summary>写真の行の URL（アップロード済みの写真の 1280 幅）。</summary>
    public static string ImageUrl(string imageBaseUrl, string key) => SiteRules.ImageUrl(imageBaseUrl, key, 1280);

    /// <summary>写真の行。説明の中の ] は使えないので、全角にする。</summary>
    public static string ImageLineOf(string imageBaseUrl, string key, string caption) =>
        $"![{caption.Replace("]", "］").Replace("\n", " ")}]({ImageUrl(imageBaseUrl, key)})";

    /// <summary>記事の項目から、1 つの入力欄の中身を作る。</summary>
    public static string Compose(PostEditModel model, string imageBaseUrl)
    {
        var sb = new StringBuilder();
        void Paragraph(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(text.Trim());
        }
        Paragraph(model.Body);
        foreach (var photo in model.Photos)
        {
            Paragraph(photo.Text);
            var auto = AutoLines(photo);
            Paragraph(ImageLineOf(imageBaseUrl, photo.Key, photo.Caption) + (auto.Count > 0 ? "\n" + string.Join("\n", auto) : ""));
        }
        Paragraph(model.Afterword);
        if (sb.Length > 0) sb.Append('\n');                                            // 最後の行のあとに 1 行の改行（続きを書き足しやすいように）
        return sb.ToString();
    }

    /// <summary>入力欄の中身を、記事の項目に分ける。写真の URL から写真を見つける（記事の写真だけ）。</summary>
    public static ArticleParts Parse(string document, IReadOnlyList<string> postKeys, string imageBaseUrl)
    {
        var errors = new List<string>();
        var photos = new List<(string Key, string Caption, string Text)>();
        var buffer = new List<string>();
        string? body = null;
        var lines = (document ?? "").Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var m = ImageLine().Match(lines[i]);
            if (!m.Success)
            {
                buffer.Add(lines[i]);
                continue;
            }
            while (i + 1 < lines.Length && AutoLine().IsMatch(lines[i + 1])) i++;           // 写真の下の自動の行は、記事の項目ではないので読み捨てる
            var key = KeyOf(m.Groups["url"].Value, imageBaseUrl);
            var text = string.Join("\n", buffer).Trim();
            buffer.Clear();
            if (key is null || !postKeys.Contains(key))
            {
                errors.Add($"{i + 1} 行目: この記事の写真ではありません（{m.Groups["url"].Value}）");
                continue;
            }
            if (photos.Any(p => p.Key == key))
            {
                errors.Add($"{i + 1} 行目: 同じ写真が 2 回あります");
                continue;
            }
            if (body is null)
            {
                body = text;                                                             // 最初の写真より前は、はじめの文章
                text = "";
            }
            photos.Add((key, m.Groups["alt"].Value.Trim(), text));
        }
        var rest = string.Join("\n", buffer).Trim();
        if (body is null)
        {
            body = rest;                                                                 // 写真が 1 枚も無い
            rest = "";
        }
        var missing = postKeys.Where(k => photos.All(p => p.Key != k)).ToList();
        if (missing.Count > 0) errors.Add($"記事の写真のうち {missing.Count} 枚の行がありません（写真の行は消さずに残してください）");
        return new ArticleParts(body, rest, photos, errors);
    }

    /// <summary>写真の URL → 写真のキー（基準 URL + キー + _幅.webp の形だけ）。</summary>
    public static string? KeyOf(string url, string imageBaseUrl)
    {
        if (!url.StartsWith(imageBaseUrl, StringComparison.Ordinal)) return null;
        var m = Regex.Match(url[imageBaseUrl.Length..], @"^(?<key>\d{4}-\d{2}/[^/]+?)_\d+\.webp$");
        return m.Success ? m.Groups["key"].Value : null;
    }

    /// <summary>プレビューの区切り（文章か写真）。写真の Auto は、本文の写真の下にある自動の行（日付・服・ワールド）をそのまま改行でつないだもの。</summary>
    public static IReadOnlyList<(bool IsPhoto, string TextOrKey, string Caption, string Auto)> Blocks(string document, string imageBaseUrl)
    {
        var blocks = new List<(bool, string, string, string)>();
        var buffer = new List<string>();
        void Flush()
        {
            var text = string.Join("\n", buffer).Trim();
            buffer.Clear();
            if (text.Length == 0) return;
            foreach (var paragraph in Regex.Split(text, @"\n\s*\n")) blocks.Add((false, paragraph.Trim(), "", ""));
        }
        var all = (document ?? "").Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < all.Length; i++)
        {
            var m = ImageLine().Match(all[i]);
            if (!m.Success) { buffer.Add(all[i]); continue; }
            Flush();
            var auto = new List<string>();
            while (i + 1 < all.Length && AutoLine().IsMatch(all[i + 1])) auto.Add(all[++i].Trim());
            blocks.Add((true, KeyOf(m.Groups["url"].Value, imageBaseUrl) ?? "", m.Groups["alt"].Value.Trim(), string.Join("\n", auto)));
        }
        Flush();
        return blocks;
    }
}
