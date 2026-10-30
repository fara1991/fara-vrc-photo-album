using System.Globalization;
using System.Text;

namespace FaraVrcAlbum;

/// <summary>
/// 表示名の共通の規則（ワールド名・旧名）。判定と長さは Rune（コードポイント）単位。
/// 除去する: 制御文字・書式文字（双方向の制御 U+202A-202E・U+2066-2069、ゼロ幅スペース U+200B、BOM U+FEFF、タグ文字など）。
/// 残す: ZWJ（U+200D）と ZWNJ（U+200C）。絵文字の結合やペルシア語などの表記を壊さないため。
/// 整える: 前後の空白を取り、内側の空白は 1 つにする。連続する ZWJ/ZWNJ は 1 つにまとめ、前後の ZWJ/ZWNJ は取る。
/// </summary>
public static class DisplayName
{
    private const char ZeroWidthNonJoiner = '‌';
    private const char ZeroWidthJoiner = '‍';

    /// <summary>ZWJ / ZWNJ（結合の制御。除去しない）か。</summary>
    private static bool IsJoiner(Rune rune) => rune.Value is 0x200C or 0x200D;

    /// <summary>除去する文字か（制御 Cc・書式 Cf のうち、結合の制御（ZWJ/ZWNJ）以外）。</summary>
    private static bool IsStrippable(Rune rune)
    {
        if (IsJoiner(rune)) return false;
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.Control or UnicodeCategory.Format;
    }

    private static int RuneCount(string text) => text.EnumerateRunes().Count();

    /// <summary>
    /// 整える。除去すべき文字を除き、連続する ZWJ/ZWNJ を 1 つにし、空白を整え、前後の ZWJ/ZWNJ を取る。
    /// 上限（Rune 単位）を超えるとき、または空になるときは null（表示名として使わない）。
    /// </summary>
    public static string? Clean(string? raw, int maxLength = 64)
    {
        if (raw is null) return null;
        var kept = new StringBuilder();
        var previousWasJoiner = false;
        foreach (var rune in raw.EnumerateRunes())
        {
            if (IsStrippable(rune)) continue;
            if (IsJoiner(rune))
            {
                // 連続する結合の制御は 1 つにまとめる（不自然な並びで、表示が崩れるため）
                if (!previousWasJoiner) kept.Append(rune.ToString());
                previousWasJoiner = true;
                continue;
            }
            previousWasJoiner = false;
            kept.Append(rune.ToString());
        }
        var collapsed = string.Join(' ', kept.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        collapsed = collapsed.Trim(ZeroWidthJoiner, ZeroWidthNonJoiner);
        if (collapsed.Length == 0 || RuneCount(collapsed) > maxLength) return null;
        return collapsed;
    }

    /// <summary>
    /// 検査用: 表示名として使えない理由（使えるなら null）。整える前の値を検査する。
    /// 整えた値と一致しなければ（除去すべき文字・前後や連続の空白・連続する結合文字がある）指摘する。
    /// </summary>
    public static string? Problem(string? name, int maxLength = 64)
    {
        if (string.IsNullOrWhiteSpace(name)) return "name が空です";
        var length = RuneCount(name);
        if (length > maxLength) return $"{maxLength} 文字までです（{length} 文字）";
        var cleaned = Clean(name, maxLength);
        if (cleaned is null) return "表示名として使える文字がありません（制御文字・結合の制御文字だけ）";
        if (cleaned == name) return null;
        if (name.EnumerateRunes().Any(IsStrippable)) return "制御文字・ゼロ幅・双方向の制御文字は使えません";
        return "前後・連続の空白、または連続するゼロ幅の結合文字は整えてください";
    }
}
