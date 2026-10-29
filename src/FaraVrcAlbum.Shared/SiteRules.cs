using System.Globalization;
using System.Text.RegularExpressions;

namespace FaraVrcAlbum;

/// <summary>記事の slug・写真の key の形式、URL（相対パス）の組み立て、ページ送り、日付の書式。ブラウザとビルドで同じ規則を使う。</summary>
public static partial class SiteRules
{
    /// <summary>
    /// 各ページの本文の最初に出す画像（文字『Fara』が焼き込み済みの WebP。960×240。元は 1920×480。tools/make-header-image.py）。ヘッダーの名前は文字（サイト名『Fara Photo Album』）。サイト内の images/ から読む。
    /// ファイル名の 8 桁は内容の SHA-256 の先頭（キャッシュのため。差し替えるときは名前の 8 桁も変える: tests/FaraVrcAlbum.Web.Tests が一致を確かめる）。docs/header-image.md。
    /// </summary>
    public const string HeaderImage = "images/header-43454dfe.webp";

    public const int HeaderImageWidth = 960;

    public const int HeaderImageHeight = 240;

    /// <summary>記事・タグの slug。kit の build-pages の path（小文字・数字・ハイフン）に載せられる形。</summary>
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    /// <summary>写真の key。yyyy-mm/yyyyMMdd-HHmmss-fff_{内容ハッシュ先頭 8 桁}（作り直しの世代は -r2 など）。</summary>
    [GeneratedRegex(@"^\d{4}-\d{2}/\d{8}-\d{6}-\d{3}_[0-9a-f]{8}(-r\d+)?$")]
    private static partial Regex PhotoKeyPattern();

    public static bool IsValidSlug(string? slug) => slug is not null && SlugPattern().IsMatch(slug);

    public static bool IsValidPhotoKey(string? key) => key is not null && PhotoKeyPattern().IsMatch(key);

    /// <summary>画像の長辺（640・1280・2048）。</summary>
    public static readonly int[] ImageSizes = [640, 1280, 2048];

    /// <summary>画像の URL。baseUrl は末尾が / の形。</summary>
    public static string ImageUrl(string baseUrl, string key, int size) => $"{baseUrl}{key}_{size}.webp";

    /// <summary>一覧のカードの sizes（3 列 / 2 列 / 1 列。app.css の列の切り替え 1200px・600px と同じ値にする）。</summary>
    public const string CardSizes = "(min-width: 1200px) 360px, (min-width: 600px) 45vw, 92vw";

    /// <summary>記事の写真 1 枚のときの sizes の既定（本文の幅は PC で 900px まで）。</summary>
    public const string ArticleSizes = "(min-width: 1000px) 900px, 92vw";

    /// <summary>
    /// 記事のギャラリーの n 枚目の sizes。表示幅に近い値にして、小さく表示される写真が 1280 / 2048 を取らないようにする（app.css のギャラリーの並べ方と合わせる）。
    ///   1 枚: 本文の幅（896px）まで。縦長は高さの上限（78vh）で狭くなる。
    ///   2 枚: 縦横比に応じて 1 行を分け合う（高さがそろう）。
    ///   3 枚以上: 均等高さの行（行の高さ約 340px 前後）なので、幅は縦横比 × 約 340px（150〜896px）。
    /// 768px 以上が PC・タブレット、未満はスマホで 1 列（92vw）。
    /// </summary>
    public static string GallerySizes(IReadOnlyList<Models.PhotoInfo> photos, int index)
    {
        const double column = 896, gap = 8;
        var ratio = photos[index].W / (double)photos[index].H;
        string Px(double value) => Math.Round(Math.Clamp(value, 150, column)).ToString(CultureInfo.InvariantCulture) + "px";
        if (photos.Count <= 1)
            return $"(min-width: 768px) min({column.ToString(CultureInfo.InvariantCulture)}px, calc(78vh * {ratio.ToString("0.####", CultureInfo.InvariantCulture)})), 92vw";
        if (photos.Count == 2)
        {
            var total = photos.Sum(p => p.W / (double)p.H);
            return $"(min-width: 768px) {Px((column - gap) * ratio / total)}, 92vw";
        }
        return $"(min-width: 768px) {Px(ratio * 340)}, 92vw";
    }



    /// <summary>OGP 用の画像（カバー写真のみ作る。1200×630 の JPEG）。</summary>
    public static string OgImageUrl(string baseUrl, string key) => $"{baseUrl}{key}_og.jpg";

    /// <summary>
    /// 相対リンク（&lt;base href="/"&gt; の下で使う）。ホームは "./"、記事は "posts/{slug}"、2 ページ目以降は "page/{n}"。
    /// </summary>
    public static string HomeHref(int page = 1) => page <= 1 ? "./" : $"page/{page}";

    public static string PostHref(string slug) => $"posts/{slug}";

    /// <summary>kit の build-pages に渡す path（先頭が /）。</summary>
    public static string HomePath(int page = 1) => page <= 1 ? "/" : $"/page/{page}";

    public static string PostPath(string slug) => $"/posts/{slug}";

    // 一覧系ページ（タグ・アーカイブ・ワールド）。2 ページ目以降は末尾に /page/{n}。href は相対（<base href="/"> の下で解決）、path は kit に渡す絶対パス。
    private static string Paged(string baseSegment, int page) => page <= 1 ? baseSegment : $"{baseSegment}/page/{page}";

    public static string TagHref(string slug, int page = 1) => Paged($"tags/{slug}", page);

    public static string TagPath(string slug, int page = 1) => "/" + TagHref(slug, page);

    public static string ArchiveHref(int year, int month, int page = 1) =>
        Paged(string.Create(CultureInfo.InvariantCulture, $"archive/{year:0000}/{month:00}"), page);

    public static string ArchivePath(int year, int month, int page = 1) => "/" + ArchiveHref(year, month, page);

    /// <summary>ワールドの href。ID は wrld_ + UUID（URL には wrld_ を除いた UUID を使う）。</summary>
    public static string WorldHref(string worldId, int page = 1) => Paged($"worlds/{WorldId.ToPathSegment(worldId)}", page);

    public static string WorldPath(string worldId, int page = 1) => "/" + WorldHref(worldId, page);

    /// <summary>作者の Twitter。プライバシーポリシーの連絡先・フッターの SNS リンク。</summary>
    public const string AuthorXUrl = "https://twitter.com/game_fara";

    public const string AuthorXHandle = "@game_fara";

    /// <summary>作者の Twitch（フッターの SNS。fara-labs の profile.json と同じ URL）。</summary>
    public const string AuthorTwitchUrl = "https://twitch.tv/game_fara";

    /// <summary>作者の Discord の招待（フッターの SNS。fara-labs の profile.json と同じ URL）。</summary>
    public const string AuthorDiscordUrl = "https://discord.com/invite/B38eCSYyk5";

    /// <summary>トップページのリード文（自己紹介）。</summary>
    public const string HomeLead = "Faraです。2021/7からVRChat始めてますがインバイトで良く引きこもってます。\nたまにVRC潜って撮った写真や、フレンドに撮ってもらった写真をこのサイトに載せてます。";

    public const string WorldsHref = "worlds";

    /// <summary>服の一覧（相対）。</summary>
    public const string OutfitsHref = "outfits";

    /// <summary>服のページ（相対。page 1 は /page/1 を付けない）。id は服のマスタの id（小文字・数字・ハイフン）。</summary>
    public static string OutfitHref(string outfitId, int page = 1) => Paged($"outfits/{outfitId}", page);

    /// <summary>服のページ（絶対パス）。</summary>
    public static string OutfitPath(string outfitId, int page = 1) => "/" + OutfitHref(outfitId, page);
    public const string TagsHref = "tags";

    /// <summary>公開日（JST）の年・月。アーカイブの分け方。月の境目は JST（2026-11-01T00:30+09:00 は 11 月）。</summary>
    public static (int Year, int Month) MonthOf(DateTimeOffset publishedAt)
    {
        var jst = publishedAt.ToOffset(Jst);
        return (jst.Year, jst.Month);
    }

    /// <summary>この日（JST）より前に撮った写真は載せない（2025 年 11 月より前。決定事項）。</summary>
    public static readonly DateTimeOffset EarliestPhotoDate = new(2025, 11, 1, 0, 0, 0, TimeSpan.FromHours(9));

    /// <summary>key の日時部分（yyyyMMdd-HHmmss。JST）。key の形式が正しいことが前提（IsValidPhotoKey）。</summary>
    public static DateTimeOffset PhotoKeyTime(string key)
    {
        var name = key[(key.IndexOf('/') + 1)..];
        var local = DateTime.ParseExact(name[..15], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None);
        return new DateTimeOffset(local, Jst);
    }

    /// <summary>key の「yyyy-mm/」が、ファイル名の年月（yyyyMM）と一致しているか。</summary>
    public static bool PhotoKeyMonthMatches(string key)
    {
        var slash = key.IndexOf('/');
        return slash == 7 && key[..4] + key[5..7] == key[(slash + 1)..(slash + 7)];
    }

    /// <summary>サイドのアーカイブメニュー: archive.json（年 → 月 → 件数）を、新しい年・新しい月の順にする。形式が不正な年・月は捨てる。</summary>
    public static IReadOnlyList<ArchiveYear> ArchiveYears(IReadOnlyDictionary<string, Dictionary<string, int>>? counts)
    {
        if (counts is null) return [];
        var years = new List<ArchiveYear>();
        foreach (var (yearText, months) in counts)
        {
            if (!int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year < 1) continue;
            var list = months
                .Where(m => int.TryParse(m.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 12 && m.Value > 0)
                .Select(m => new ArchiveMonth(int.Parse(m.Key, CultureInfo.InvariantCulture), m.Value))
                .OrderByDescending(m => m.Month)
                .ToList();
            if (list.Count > 0) years.Add(new ArchiveYear(year, list));
        }
        return years.OrderByDescending(y => y.Year).ToList();
    }

    /// <summary>ページ数（記事 0 件でも 1 ページ）。</summary>
    public static int PageCount(int total, int perPage)
    {
        if (perPage < 1) throw new ArgumentOutOfRangeException(nameof(perPage));
        return Math.Max(1, (total + perPage - 1) / perPage);
    }

    /// <summary>page（1 始まり）の分。範囲外は空。</summary>
    public static IReadOnlyList<T> Slice<T>(IReadOnlyList<T> items, int page, int perPage)
    {
        if (page < 1 || perPage < 1) return Array.Empty<T>();
        return items.Skip((page - 1) * perPage).Take(perPage).ToList();
    }

    private static readonly TimeSpan Jst = TimeSpan.FromHours(9);

    /// <summary>InvariantGlobalization 下でも同じ結果になる日付の書式（JST 固定）。例: 2026年10月24日。</summary>
    public static string FormatDate(DateTimeOffset value)
    {
        var jst = value.ToOffset(Jst);
        return string.Create(CultureInfo.InvariantCulture, $"{jst.Year}年{jst.Month}月{jst.Day}日");
    }

    /// <summary>撮影日時の表示（JST）。例: 2026年10月23日 21:33。読めない値は null。</summary>
    public static string? FormatTakenAt(string? takenAt)
    {
        if (string.IsNullOrWhiteSpace(takenAt) || !DateTimeOffset.TryParse(takenAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)) return null;
        var jst = value.ToOffset(Jst);
        return string.Create(CultureInfo.InvariantCulture, $"{jst.Year}年{jst.Month}月{jst.Day}日 {jst.Hour:00}:{jst.Minute:00}");
    }

    /// <summary>機械可読の日付（&lt;time datetime&gt; 用。JST）。</summary>
    public static string FormatIsoDate(DateTimeOffset value) =>
        value.ToOffset(Jst).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>画像の基準 URL を、https:// で始まり / で終わる形にそろえる。それ以外はエラー。</summary>
    public static string NormalizeImageBaseUrl(string? value)
    {
        var text = (value ?? "").Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException($"ImageBaseUrl は https:// で始まる URL にしてください: {text}");
        return text.EndsWith('/') ? text : text + "/";
    }
}

/// <summary>アーカイブメニューの 1 年分（新しい月が先）。</summary>
public sealed record ArchiveYear(int Year, IReadOnlyList<ArchiveMonth> Months)
{
    public int Count => Months.Sum(m => m.Count);
}

/// <summary>アーカイブメニューの 1 か月（公開月 JST の記事数）。</summary>
public sealed record ArchiveMonth(int Month, int Count);
