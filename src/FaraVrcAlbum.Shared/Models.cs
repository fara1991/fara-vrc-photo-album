using System.Text.Json.Serialization;

namespace FaraVrcAlbum.Models;

/// <summary>data/site.json。Build が appsettings.json から書き、アプリが起動時に読む。</summary>
public sealed class SiteSettings
{
    public string SiteName { get; set; } = "";
    public string Author { get; set; } = "";

    /// <summary>画像の基準 URL（末尾に / を付けた形。例: https://vrc-photos.fara-labs.com/）。</summary>
    public string ImageBaseUrl { get; set; } = "";

    /// <summary>
    /// 本番の画像の基準 URL（https://。og:image・JSON-LD・フィード・CSP に使う）。data/site.json には書かない（Build の内部の値）。
    /// 本番の出力では ImageBaseUrl と同じ。確認用の出力では、ImageBaseUrl が同一オリジンの合成画像（相対パス）になるが、og:image などは本番の URL のまま。
    /// </summary>
    [JsonIgnore]
    public string PublicImageBaseUrl { get; set; } = "";

    /// <summary>一覧の 1 ページの件数。</summary>
    public int PostsPerPage { get; set; } = 12;
}

/// <summary>写真に写ったワールド（記事の JSON）。公開データでは name を content/worlds.json のマスタの名前にそろえる。</summary>
public sealed class WorldRef
{
    /// <summary>wrld_ + UUID（小文字）。</summary>
    public string Id { get; set; } = "";

    /// <summary>撮影時点のワールド名（XMP の控え）。省略可。画面に出す名前はマスタの name。</summary>
    public string? Name { get; set; }
}

/// <summary>
/// 記事の中の 1 枚の写真（content/posts/{slug}.json の photos[] と data/posts/{slug}.json で共通）。公開してよい項目だけ。
/// </summary>
public sealed class PhotoInfo
{
    /// <summary>R2 のキー（yyyy-mm/yyyyMMdd-HHmmss-fff_ハッシュ8桁）。画像 URL = ImageBaseUrl + key + "_640.webp" など。</summary>
    public string Key { get; set; } = "";

    /// <summary>表示用（2048 版）画像の幅・高さ（CLS 対策の width / height / aspect-ratio に使う）。</summary>
    public int W { get; set; }

    public int H { get; set; }

    /// <summary>撮影日時（オフセット付き ISO 8601）。省略可。</summary>
    public string? TakenAt { get; set; }

    /// <summary>代表色（6 桁の 16 進。読み込み前の背景色）。省略可。</summary>
    public string? Color { get; set; }

    public string? Caption { get; set; }

    /// <summary>
    /// この写真の前に置く文章（Markdown・記事の元データだけ）。省略可。あると、記事の写真の並びはこの写真の前で区切られ、文章 → 写真の順に出る。
    /// 公開データには書かない（<see cref="TextHtml"/> に変換して入れる）。
    /// </summary>
    public string? Text { get; set; }

    /// <summary>公開データの、この写真の前の文章（HTML。Build が Text から作る）。元データには書かない。</summary>
    public string? TextHtml { get; set; }

    /// <summary>ワールド。省略可（ワールド不明）。</summary>
    public WorldRef? World { get; set; }

    /// <summary>アバター（content/avatars.json の id）。省略可。写真に着ている服は、outfits があればそれ、無ければこのアバターの outfitIds。公開データには書かない。</summary>
    public string? Avatar { get; set; }

    /// <summary>この写真の服（content/outfits.json の id・順序つき）。省略はアバターの既定の服。空の配列は「服は無い」。公開データには書かない（Wear に解決する）。</summary>
    public List<string>? Outfits { get; set; }

    /// <summary>公開データの服（Build が Avatar・Outfits から解決して入れる）。キャプションの「服: …」に出す。無ければ項目ごと省く。</summary>
    public List<OutfitRef>? Wear { get; set; }
}

/// <summary>content/posts/{slug}.json（記事の元データ）。</summary>
public sealed class PostSource
{
    /// <summary>形式の版（1）。省略はエラー（ContentLoader）。</summary>
    public int Schema { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool Draft { get; set; }
    public bool Noindex { get; set; }
    public string? Summary { get; set; }
    public string? Cover { get; set; }
    public List<string> Tags { get; set; } = new();

    /// <summary>Markdown の本文（写真の前に出る、はじめの文章）。</summary>
    public string Body { get; set; } = "";

    public List<PhotoInfo> Photos { get; set; } = new();

    /// <summary>写真のあとに出る、おわりの文章（Markdown）。省略可。</summary>
    public string? Afterword { get; set; }
}

/// <summary>content/worlds.json の 1 件（キーは wrld_ + UUID）。ワールドのマスタ。画面に出す名前・旧名（aliases）。</summary>
public sealed class WorldMasterEntry
{
    public string Name { get; set; } = "";

    /// <summary>旧名・表記ゆれ（画面には出さない。検索を入れるときの対象）。</summary>
    public List<string> Aliases { get; set; } = new();

    /// <summary>
    /// name を観測した写真の撮影日時（ISO 8601・オフセット付き）。新しい写真の名前で name を変えるのは、この日時以降の写真のときだけ。
    /// 省略（既存データ）は、どの写真の名前でも更新してよい。
    /// </summary>
    public string? NameAt { get; set; }

    /// <summary>
    /// VRChat のワールドの画像を、写真と同じ R2 に置いたもの（album worlds --apply が VRChat から取って置く）。省略可。
    /// </summary>
    public WorldImage? Image { get; set; }
}

/// <summary>
/// R2 に置いたワールドの画像。Key は worlds/{uuid}/{file_…}_{版}（VRChat の画像の版が変わると Key も変わる＝キャッシュは immutable のまま）。
/// R2 には Key + _512.webp と Key + _1024.webp（元が小さければ元の大きさ）。W/H は _1024 の寸法。
/// </summary>
public sealed class WorldImage
{
    public string Key { get; set; } = "";
    public int W { get; set; }
    public int H { get; set; }
}

/// <summary>
/// content/world-stats.json（VRChat の公開 API から取ったワールドの情報。手元のアプリの「サイトの画像情報を更新する」ボタン（または album world-stats --apply）で更新する）。
/// 今いる人数のようなリアルタイムの値は持たない。
/// </summary>
public sealed class WorldStatsFile
{
    /// <summary>取得した日時（ISO 8601・オフセット付き）。画面には「○年○月○日時点」と出す。</summary>
    public string FetchedAt { get; set; } = "";

    public Dictionary<string, WorldStats> Worlds { get; set; } = new();
}

/// <summary>1 つのワールドの情報（VRChat の API の値のうち、画面に出すもの）。</summary>
public sealed class WorldStats
{
    /// <summary>ワールドの作者の表示名（作者へのクレジット）。</summary>
    public string AuthorName { get; set; } = "";

    /// <summary>作者が書いた説明文。</summary>
    public string Description { get; set; } = "";

    public int Capacity { get; set; }
    public int RecommendedCapacity { get; set; }
    public long Visits { get; set; }
    public long Favorites { get; set; }

    /// <summary>作者が付けたタグ（author_tag_ を除いた部分）。</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>公開日（ISO 8601）。無ければ空。</summary>
    public string PublicationDate { get; set; } = "";

    /// <summary>最終更新日（ISO 8601）。</summary>
    public string UpdatedAt { get; set; } = "";
}

/// <summary>content/outfits.json の 1 件（キーは服の id）。服の名前は商品名（個人情報ではない）。</summary>
public sealed class OutfitMasterEntry
{
    /// <summary>表示する服の名前。</summary>
    public string Name { get; set; } = "";

    /// <summary>販売元（任意。例: BOOTH の店名）。</summary>
    public string? Shop { get; set; }

    /// <summary>商品のページ（任意。https:// だけ）。キャプションの名前のリンクになる。</summary>
    public string? Url { get; set; }

    /// <summary>備考（画面には出さない）。</summary>
    public string? Note { get; set; }

    /// <summary>商品の画像（BOOTH のサムネイル。https）。アプリが BOOTH Library Manager から取り込む。服のページ・一覧に出す。</summary>
    public string? Thumbnail { get; set; }

    /// <summary>商品の説明（BOOTH の説明の文章）。アプリが取り込む。服のページに出す。</summary>
    public string? Description { get; set; }

    /// <summary>商品のタグ（BOOTH のタグ）。アプリが取り込む。</summary>
    public List<string>? Tags { get; set; }

    /// <summary>BOOTH での公開日・更新日（ISO 8601）。アプリが取り込む。</summary>
    public string? PublishedAt { get; set; }

    public string? UpdatedAt { get; set; }

    /// <summary>BOOTH の情報を取り込んだ日時（ISO 8601）。「○年○月○日時点の BOOTH の情報です」に使う。</summary>
    public string? FetchedAt { get; set; }
}

/// <summary>content/avatars.json の 1 件（キーはアバターの id）。VRChat のアバター名と、そのアバターに着せている服。アバター名は画面に出さない。</summary>
public sealed class AvatarMasterEntry
{
    /// <summary>VRChat 上のアバター名（ユーザーが見る表記）。画面には出さない。</summary>
    public string Name { get; set; } = "";

    /// <summary>表記ゆれ（画面には出さない。紐づけの確認に使う）。</summary>
    public List<string> Aliases { get; set; } = new();

    /// <summary>既定の服（順序つき。content/outfits.json の id）。写真の outfits を省いたときに使う。</summary>
    public List<string> OutfitIds { get; set; } = new();

    /// <summary>備考（画面には出さない）。</summary>
    public string? Note { get; set; }
}

/// <summary>公開データの服 1 点（キャプションに出す名前と、任意の https の URL）。</summary>
public sealed class OutfitRef
{
    /// <summary>服のマスタの id（サイト内の服のページ /outfits/{id} へのリンクに使う）。</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>BOOTH の商品ページなど（https）。服のページの名前のリンク先。</summary>
    public string? Url { get; set; }
}

/// <summary>content/tags.json の 1 件（キーはタグの slug）。</summary>
public sealed class TagMasterEntry
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

/// <summary>タグ（slug と表示名）。</summary>
public sealed class TagInfo
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>data/posts/{slug}.json（記事ページが読む。本文は Build が Markdown から HTML にしたもの）。</summary>
public sealed class PostPage
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string Summary { get; set; } = "";
    public string? Cover { get; set; }
    public List<TagInfo> Tags { get; set; } = new();
    public string BodyHtml { get; set; } = "";
    public List<PhotoInfo> Photos { get; set; } = new();

    /// <summary>写真のあとの、おわりの文章（HTML）。無ければ省く。</summary>
    public string? AfterwordHtml { get; set; }

    /// <summary>前の記事（1 つ古い公開日）。無ければ null。Build が環境に出す記事の中だけから決める（予約公開の記事を本番に漏らさない）。</summary>
    public PostLink? Older { get; set; }

    /// <summary>次の記事（1 つ新しい公開日）。無ければ null。</summary>
    public PostLink? Newer { get; set; }

    /// <summary>関連する記事（同じタグ・同じワールドが多い順。最大 6 件）。無ければ項目ごと省く。</summary>
    public List<PostSummary>? Related { get; set; }
}

/// <summary>前後の記事へのリンク。</summary>
public sealed class PostLink
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
}

/// <summary>一覧のカードに要る分（data/posts-index.json の要素。新しい順）。</summary>
public sealed class PostSummary
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; }
    public string Summary { get; set; } = "";

    /// <summary>代表の写真（カバー）。写真が 1 枚も無い記事は null。</summary>
    public PhotoInfo? Cover { get; set; }

    /// <summary>タグの slug。</summary>
    public List<string> Tags { get; set; } = new();

    public int PhotoCount { get; set; }
}

/// <summary>写真 1 枚の参照（ワールドの写真一覧）。どの記事の写真かを slug で持つ。</summary>
public sealed class PhotoRef
{
    public string Key { get; set; } = "";
    public int W { get; set; }
    public int H { get; set; }
    public string? Color { get; set; }

    /// <summary>この写真がある記事の slug。</summary>
    public string Slug { get; set; } = "";

    public string? TakenAt { get; set; }

    public PhotoInfo ToPhotoInfo() => new() { Key = Key, W = W, H = H, Color = Color, TakenAt = TakenAt };
}

/// <summary>data/worlds.json の要素（ワールドの一覧）。</summary>
public sealed class WorldSummary
{
    /// <summary>wrld_ + UUID。</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
    public int PhotoCount { get; set; }
    public PhotoRef? Cover { get; set; }

    /// <summary>R2 に置いたワールドの画像。無ければ Cover（撮った写真）を出す。</summary>
    public WorldImage? Image { get; set; }
}

/// <summary>data/outfits.json の要素（服の一覧）。服を着て撮った写真が 1 枚以上ある服だけ。</summary>
public sealed class OutfitSummary
{
    /// <summary>服のマスタの id。</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>お店の名前（あれば）。</summary>
    public string? Shop { get; set; }

    public int PhotoCount { get; set; }

    /// <summary>一覧のカードの画像（BOOTH の商品の画像。https）。無ければ画像を出さない（自分の写真は出さない）。</summary>
    public string? Thumbnail { get; set; }
}

/// <summary>data/outfits/{id}.json（その服を着て撮った写真。新しい記事の順）。</summary>
public sealed class OutfitPage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Shop { get; set; }

    /// <summary>BOOTH の商品ページ（https）。名前のリンク先。</summary>
    public string? Url { get; set; }

    /// <summary>BOOTH の商品の画像・説明・タグ・公開日・更新日・取り込んだ日時（あれば）。</summary>
    public string? Thumbnail { get; set; }
    public string? Description { get; set; }
    public List<string>? Tags { get; set; }
    public string? PublishedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public string? FetchedAt { get; set; }

    public List<PhotoRef> Photos { get; set; } = new();
}

/// <summary>data/worlds/{uuid}.json（そのワールドで撮った写真。新しい記事の順）。</summary>
public sealed class WorldPage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>R2 に置いたワールドの画像。</summary>
    public WorldImage? Image { get; set; }

    /// <summary>VRChat から取ったワールドの情報。無ければ出さない。</summary>
    public WorldStats? Stats { get; set; }

    /// <summary>Stats を取得した日時（ISO 8601）。</summary>
    public string? StatsFetchedAt { get; set; }

    public List<PhotoRef> Photos { get; set; } = new();
}

/// <summary>data/tags.json の要素（タグの一覧）。</summary>
public sealed class TagSummary
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int PostCount { get; set; }
}
