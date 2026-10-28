using System.Text;

namespace FaraVrcAlbum.Desktop.Services;

/// <summary>
/// アバター名から、着ている服（登録済みの服）を予測する。アバター名と服の名前を単語に分け（CamelCase・記号・数字の区切り）、
/// 共通の単語が長いほど近いとみなす。よくある語（fara・dress など）は数えない。当たるとは限らないので、「予測」として出すだけで、登録の前に確かめられる。
/// </summary>
public static class OutfitGuess
{
    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "fara", "avatar", "variant", "vrm", "vrc", "prefab", "model", "ver", "version", "dress", "outfit", "costume", "clothes", "set", "base", "for", "the", "and", "re", "v1", "v2", "v3",
    };

    /// <summary>名前を単語に分ける（小文字。英数字の塊・カタカナ・ひらがな・漢字の塊ごと。CamelCase の境目でも分ける）。</summary>
    public static IReadOnlyList<string> Tokens(string name)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        string? kind = null;
        char previous = '\0';
        void Flush()
        {
            if (current.Length > 0) tokens.Add(current.ToString().ToLowerInvariant());
            current.Clear();
        }
        foreach (var ch in name)
        {
            var k = KindOf(ch);
            if (k is null) { Flush(); kind = null; previous = ch; continue; }
            // 英字の小文字 → 大文字（FaraAlmarianDress の Fara|Almarian|Dress）、英字 ↔ 数字、文字の種類が変わるところで区切る
            var boundary = kind is not null && (k != kind || (k == "latin" && char.IsLower(previous) && char.IsUpper(ch)));
            if (boundary) Flush();
            current.Append(ch);
            kind = k;
            previous = ch;
        }
        Flush();
        return tokens;
    }

    private static string? KindOf(char ch)
    {
        if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z') return "latin";
        if (char.IsDigit(ch)) return "digit";
        if (ch is >= '\u30A0' and <= '\u30FF') return "katakana";
        if (ch is >= '\u3040' and <= '\u309F') return "hiragana";
        if (ch is >= '\u4E00' and <= '\u9FFF') return "kanji";
        return null;
    }

    /// <summary>予測する服（近い順。点数が 4 以上のものだけ。同点は一緒に出す。最大 max 件）。</summary>
    public static IReadOnlyList<(string Id, string Name)> Suggest(string avatarName, IReadOnlyList<(string Id, string Name)> outfits, int max = 3)
    {
        var avatarTokens = Tokens(avatarName).Where(t => t.Length >= 3 && !Stop.Contains(t)).ToHashSet(StringComparer.Ordinal);
        if (avatarTokens.Count == 0) return [];
        var scored = new List<(string Id, string Name, int Score)>();
        foreach (var (id, name) in outfits)
        {
            var lower = name.ToLowerInvariant();
            var outfitTokens = Tokens(name).Where(t => t.Length >= 3 && !Stop.Contains(t)).ToHashSet(StringComparer.Ordinal);
            var score = 0;
            foreach (var t in avatarTokens)
            {
                if (outfitTokens.Contains(t)) score += t.Length;                            // 単語が同じ
                else if (t.Length >= 5 && lower.Contains(t, StringComparison.Ordinal)) score += t.Length - 1;   // 服の名前の中に含まれる
            }
            if (score >= 4) scored.Add((id, name, score));
        }
        if (scored.Count == 0) return [];
        var best = scored.Max(s => s.Score);
        return scored.Where(s => s.Score == best).OrderBy(s => s.Name, StringComparer.Ordinal).Take(max).Select(s => (s.Id, s.Name)).ToList();
    }
}
