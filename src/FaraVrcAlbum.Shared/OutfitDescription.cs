using System.Text;
using System.Text.RegularExpressions;

namespace FaraVrcAlbum;

/// <summary>
/// 服のページに出す説明文: BOOTH の商品の説明（前文・宣伝・注意書きを含む長い文章）から、<b>対応アバターの一覧</b>と<b>利用規約（とその URL）</b>だけを取り出す。
/// 説明の書き方は商品ごとにまちまちなので、見出しの形（「対応アバター」「■利用規約」など）と、「■ アバター名 + BOOTH の URL」の並びから探す。
/// 見つからなかったものは出さない（前文を代わりに出さない）。全文は BOOTH の商品ページにある。
/// </summary>
public static partial class OutfitDescription
{
    /// <summary>対応アバターの欄の最大行数・利用規約の欄の最大行数と文字数（長すぎる抜き出しは切る）。</summary>
    public const int MaxAvatarLines = 120;
    public const int MaxTermsLines = 30;
    public const int MaxTermsChars = 1500;

    [GeneratedRegex(@"^[\s■◆●◇□▼▽▶▷★☆♦◎〇○◯【】\[\]「」『』（）()🍄✧✨❄️🎄🐱📌⚠️※・▪️•\-–—:：|｜/／~～]+|[\s■◆●◇□▼▽【】\[\]「」『』（）()🍄✧✨:：|｜]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Decoration();

    [GeneratedRegex(@"(対応\s*(アバター|アバタ|モデル|キャラクター|キャラ|素体)|Supported?\s*Avatars?|Compatible\s*Avatars?|Avatars?\s*Support|^対応$|^対応\s*[|｜]|^대응$|対応一覧)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AvatarHeading();

    [GeneratedRegex(@"(利用規約|ご利用規約|利用条件|Terms(\s+of\s+(Use|Service))?|Term\s+of\s+Use|\bTOS\b|이용\s*약관|License|ライセンス)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TermsHeading();

    /// <summary>見出しとして扱わない（文の途中・説明文）。</summary>
    [GeneratedRegex(@"(シェイプキー|について|ください|ございません|していません|できません|します|ます。|です。|は含まれ|付属|はありません|が記載|に準拠|をご確認|ご了承)", RegexOptions.CultureInvariant)]
    private static partial Regex NotHeading();

    [GeneratedRegex(@"^[-―─━=＝＊*~～_・\s]{4,}$", RegexOptions.CultureInvariant)]
    private static partial Regex Separator();

    [GeneratedRegex(@"^([0-9０-９]+[.．)）]|[①-⑳]|[・※-]|.{1,20}[:：]\s*\S)", RegexOptions.CultureInvariant)]
    private static partial Regex TermsItem();

    [GeneratedRegex(@"https?://\S+", RegexOptions.CultureInvariant)]
    private static partial Regex Url();

    [GeneratedRegex(@"^[■◆●◇□▼▽▶▷★☆♦◎〇○◯【\[]", RegexOptions.CultureInvariant)]
    private static partial Regex Marker();

    /// <summary>説明から取り出した文章（「対応アバター」「利用規約」を、見つかったものだけ）。どちらも無ければ null。</summary>
    public static string? Summarize(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var lines = description.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim()).ToArray();

        var avatars = FindAvatars(lines);
        var terms = FindTerms(lines);
        if (avatars is null && terms is null) return null;

        var sb = new StringBuilder();
        if (avatars is not null) sb.Append("【対応アバター】\n").Append(avatars);
        if (terms is not null)
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append("【利用規約】\n").Append(terms);
        }
        return sb.ToString();
    }

    private static string Core(string line) => Decoration().Replace(line, "").Trim();

    private static bool IsHeading(string line, Regex heading, int maxLength)
    {
        if (line.Length == 0 || line.Length > maxLength || Url().IsMatch(line)) return false;
        var core = Core(line);
        return core.Length > 0 && !NotHeading().IsMatch(core) && heading.IsMatch(core);
    }

    /// <summary>次の節の見出しか（短い行で、記号で始まる／区切り線／末尾が「:」）。</summary>
    private static bool StartsNextSection(string line)
    {
        if (Separator().IsMatch(line)) return true;
        if (line.Length is 0 or > 40 || Url().IsMatch(line)) return false;
        return Marker().IsMatch(line) || line.EndsWith(':') || line.EndsWith('：');
    }

    private static string? FindAvatars(string[] lines)
    {
        // 1) 「■ 名前 | 読み」の次の行に BOOTH の URL がある並び（対応アバターの定番の書き方）を全部集める
        var entries = new List<string>();
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            if (lines[i].Length is > 0 and <= 80 && Marker().IsMatch(lines[i]) && !Url().IsMatch(lines[i]) && IsBoothUrl(lines[i + 1]))
            {
                entries.Add(lines[i] + "\n" + lines[i + 1]);
                i++;
            }
        }
        if (entries.Count >= 2) return string.Join("\n", entries.Take(MaxAvatarLines / 2));

        // 2) 見出し「対応アバター」などの下（次の節まで）
        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsHeading(lines[i], AvatarHeading(), 40)) continue;
            var body = new List<string>();
            for (var j = i + 1; j < lines.Length && body.Count < MaxAvatarLines; j++)
            {
                if (lines[j].Length > 0 && StartsNextSection(lines[j]) && body.Count > 0 && !IsListEntry(lines, j)) break;
                if (lines[j].Length == 0 && body.Count > 0 && j + 1 < lines.Length && lines[j + 1].Length == 0) break;
                body.Add(lines[j]);
            }
            var text = string.Join("\n", body).Trim();
            if (IsSubstantial(text)) return text;
        }
        return null;
    }

    /// <summary>1 行だけの短い文（説明文の一部を見出しと取り違えた）ではない: 2 行以上か、URL がある。</summary>
    private static bool IsSubstantial(string text) =>
        text.Split('\n').Count(l => l.Trim().Length > 0) >= 2 || Url().IsMatch(text);

    private static bool IsBoothUrl(string line) => Url().IsMatch(line) && line.Contains("booth.pm", StringComparison.OrdinalIgnoreCase);

    private static bool IsListEntry(string[] lines, int index) =>
        Marker().IsMatch(lines[index]) && index + 1 < lines.Length && IsBoothUrl(lines[index + 1]);

    private static string? FindTerms(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsHeading(lines[i], TermsHeading(), 40)) continue;
            var body = new List<string>();
            var chars = 0;
            for (var j = i + 1; j < lines.Length && body.Count < MaxTermsLines && chars < MaxTermsChars; j++)
            {
                if (body.Any(b => b.Length > 0) && lines[j].Length > 0 && StartsNextSection(lines[j]) && !TermsHeading().IsMatch(lines[j]) && !IsTermsItem(lines[j])) break;
                body.Add(lines[j]);
                chars += lines[j].Length;
            }
            var text = string.Join("\n", body).Trim();
            var heading = Url().Match(lines[i]);                                          // 見出しの行に URL がある（「利用規約: https://…」）
            if (!IsSubstantial(text) && !heading.Success) continue;
            return heading.Success && !text.Contains(heading.Value, StringComparison.Ordinal) ? (heading.Value + "\n" + text).Trim() : text;
        }

        // 見出しの形ではないが、「利用規約」「Terms」と URL が同じ行にある（「利用規約/Terms of use ✅利用規約✅ ・Terms of use【 https://… 】」など）
        var linked = lines.Where(l => l.Length is > 0 and <= 300 && Url().IsMatch(l) && TermsHeading().IsMatch(l)).Take(3).ToList();
        return linked.Count > 0 ? string.Join("\n", linked) : null;
    }

    /// <summary>利用規約の項目（「1.利用主体」「商用利用：可」「・…」「① …」）。次の節の見出しと取り違えない。</summary>
    private static bool IsTermsItem(string line) => TermsItem().IsMatch(line);
}
