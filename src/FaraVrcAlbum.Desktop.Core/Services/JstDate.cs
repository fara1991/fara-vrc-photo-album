using System.Globalization;
using System.Text.RegularExpressions;

namespace FaraVrcAlbum.Desktop.Services;

/// <summary>公開日時は日本時間（+09:00）に固定して扱う。画面には「2026-10-24 20:00」の形で出し、保存するときに +09:00 を付ける。</summary>
public static class JstDate
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(9);

    private static readonly Regex HasOffset = new(@"(Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>入力を日時にする。時差が無ければ日本時間として読む。時差がある入力は日本時間に直す。読めなければ false。</summary>
    public static bool TryParse(string? text, out DateTimeOffset value)
    {
        value = default;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return false;
        if (HasOffset.IsMatch(t))
        {
            if (!DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset)) return false;
            value = withOffset.ToOffset(Offset);
            return true;
        }
        if (!DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        value = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Offset);
        return true;
    }

    /// <summary>画面に出す形（日本時間・時差なし）。</summary>
    public static string Display(DateTimeOffset value) => value.ToOffset(Offset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>保存する形（+09:00 つき）。</summary>
    public static string ToIso(DateTimeOffset value) => value.ToOffset(Offset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>保存してある文字列を、画面に出す形にする（読めなければそのまま）。</summary>
    public static string DisplayOf(string stored) => TryParse(stored, out var d) ? Display(d) : stored;
}
