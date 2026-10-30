using System.Globalization;
using System.Text.RegularExpressions;

namespace FaraVrcAlbum.Images;

/// <summary>
/// 写真のキーと日時（計画書 §5.3・案 A）: <c>yyyy-mm/yyyyMMdd-HHmmss-fff_{内容ハッシュ先頭 8 桁}</c>。
/// 時刻は撮影日時を JST（+09:00）にそろえ、キーはミリ秒まで。XMP の CreateDate を優先し、無ければファイル名の時刻（JST とみなす）。
/// </summary>
public static partial class PhotoKeys
{
    public static readonly TimeSpan Jst = TimeSpan.FromHours(9);

    // 新しい形式: VRChat_yyyy-mm-dd_hh-mm-ss.ms_幅x高.png
    [GeneratedRegex(@"^VRChat_(\d{4})-(\d{2})-(\d{2})_(\d{2})-(\d{2})-(\d{2})\.(\d{1,3})_(\d+)x(\d+)\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DateLastPattern();

    // 古い形式（2025 年の前半まで）: VRChat_幅x高_yyyy-mm-dd_hh-mm-ss.ms.png
    [GeneratedRegex(@"^VRChat_(\d+)x(\d+)_(\d{4})-(\d{2})-(\d{2})_(\d{2})-(\d{2})-(\d{2})\.(\d{1,3})\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SizeFirstPattern();

    /// <summary>VRChat のファイル名から、撮影時刻（JST・ミリ秒）と寸法。新旧どちらの形式でもよい。形式が違えば null。</summary>
    public static (DateTimeOffset TakenAt, int Width, int Height)? ParseFileName(string fileName)
    {
        var m = DateLastPattern().Match(fileName);
        if (m.Success)
        {
            return Build(m, year: 1, month: 2, day: 3, hour: 4, minute: 5, second: 6, ms: 7, width: 8, height: 9);
        }
        m = SizeFirstPattern().Match(fileName);
        if (m.Success)
        {
            return Build(m, year: 3, month: 4, day: 5, hour: 6, minute: 7, second: 8, ms: 9, width: 1, height: 2);
        }
        return null;
    }

    private static (DateTimeOffset TakenAt, int Width, int Height)? Build(Match m, int year, int month, int day, int hour, int minute, int second, int ms, int width, int height)
    {
        int Num(int group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);
        try
        {
            var takenAt = new DateTimeOffset(Num(year), Num(month), Num(day), Num(hour), Num(minute), Num(second), int.Parse(m.Groups[ms].Value.PadRight(3, '0'), CultureInfo.InvariantCulture), Jst);
            return (takenAt, Num(width), Num(height));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>JST にそろえ、ミリ秒未満を切り捨てる。</summary>
    public static DateTimeOffset Normalize(DateTimeOffset takenAt)
    {
        var jst = takenAt.ToOffset(Jst);
        return new DateTimeOffset(jst.Ticks - jst.Ticks % TimeSpan.TicksPerMillisecond, Jst);
    }

    /// <summary>キー（yyyy-mm/yyyyMMdd-HHmmss-fff_xxxxxxxx）。sha256 はフルの小文字 16 進。</summary>
    public static string Build(DateTimeOffset takenAt, string sha256)
    {
        var t = Normalize(takenAt);
        return $"{t.ToString("yyyy-MM", CultureInfo.InvariantCulture)}/{t.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}_{sha256[..8]}";
    }

    /// <summary>JSON（takenAt）の形式: JST・秒まで（キーと秒まで一致させる）。</summary>
    public static string TakenAtText(DateTimeOffset takenAt)
    {
        var t = takenAt.ToOffset(Jst);
        var seconds = new DateTimeOffset(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, Jst);
        return seconds.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    /// <summary>キーの撮影月（yyyy-mm）。</summary>
    public static string MonthOf(string key) => key[..7];
}
