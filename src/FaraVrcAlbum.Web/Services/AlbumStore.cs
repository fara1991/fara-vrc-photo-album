using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using FaraVrcAlbum.Models;

namespace FaraVrcAlbum.Web.Services;

/// <summary>
/// Build が書いた data/*.json の読み込み。起動のときに 1 回（サイトの設定と記事の一覧）、ページに移るときに必要な分（記事・ワールド・タグ）を読む。
/// 静的 HTML の本文を空の画面に置き換えないよう、一覧は最初の描画の前に読んでおく（Program.cs）。
/// 一度読んだ一覧（タグ・ワールド）は、メモリに残す。
/// </summary>
public sealed class AlbumStore(HttpClient http)
{
    public SiteSettings Settings { get; private set; } = new();

    /// <summary>公開済みの記事の要約（新しい順）。</summary>
    public IReadOnlyList<PostSummary> Posts { get; private set; } = Array.Empty<PostSummary>();

    /// <summary>タグの slug → 表示名（カードのタグ）。タグの一覧が読めなかったときは空（slug をそのまま出す）。</summary>
    public IReadOnlyDictionary<string, string> TagNames { get; private set; } = new Dictionary<string, string>();

    /// <summary>公開月別の件数（サイドのアーカイブメニュー。新しい年・新しい月が先）。読めなかったときは空（メニューを出さない）。</summary>
    public IReadOnlyList<ArchiveYear> Archive { get; private set; } = Array.Empty<ArchiveYear>();

    private static readonly System.Text.RegularExpressions.Regex OutfitIdPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private readonly ReadOnce<List<TagSummary>> _tags = new();
    private readonly ReadOnce<List<WorldSummary>> _worlds = new();
    private readonly ReadOnce<List<OutfitSummary>> _outfits = new();

    /// <summary>設定と一覧を読む。読めなければ false（コンソールに理由）。</summary>
    public async Task<bool> LoadAsync()
    {
        try
        {
            var settings = http.GetFromJsonAsync("data/site.json", SiteJsonContext.Default.SiteSettings);
            var posts = http.GetFromJsonAsync("data/posts-index.json", SiteJsonContext.Default.ListPostSummary);
            // タグの表示名とアーカイブのメニューは、読めなくても起動できる（名前なし・メニューなしで続ける）。同時に取る（起動の往復を増やさない）
            var tags = TryGetAsync("data/tags.json", SiteJsonContext.Default.ListTagSummary);
            var archive = TryGetAsync("data/archive.json", SiteJsonContext.Default.DictionaryStringDictionaryStringInt32);
            await Task.WhenAll(settings, posts, tags, archive);
            if (settings.Result is null || posts.Result is null) return false;
            Settings = settings.Result;
            // 範囲外は、Build（SiteBuilder）が例外で止める（入力の誤りをビルドで気づかせる）。Web は、古い・壊れた data/site.json でも画面を開けるように既定の 12 に戻す（意図の違い）
            if (Settings.PostsPerPage is < 1 or > 100) Settings.PostsPerPage = 12;
            Posts = posts.Result;
            if (tags.Result is not null) _tags.Seed(tags.Result);                                 // 読めなかったときは、タグの一覧のページが取り直す
            TagNames = TagNamesOf(tags.Result);
            Archive = SiteRules.ArchiveYears(archive.Result);
            return true;
        }
        catch (Exception e) when (IsReadFailure(e))
        {
            Console.Error.WriteLine($"[album] data/*.json を読めません: {e.Message}");
            return false;
        }
    }

    public int PageCount => SiteRules.PageCount(Posts.Count, Settings.PostsPerPage);

    /// <summary>公開月（JST）の記事（新しい順）。</summary>
    public IReadOnlyList<PostSummary> PostsOfMonth(int year, int month) =>
        Posts.Where(p => SiteRules.MonthOf(p.PublishedAt) == (year, month)).ToList();

    /// <summary>タグ（slug）が付いた記事（新しい順）。</summary>
    public IReadOnlyList<PostSummary> PostsOfTag(string slug) => Posts.Where(p => p.Tags.Contains(slug)).ToList();

    /// <summary>記事 1 本分。slug の形が不正・存在しない・読めないときは null。</summary>
    public async Task<PostPage?> GetPostAsync(string slug)
    {
        if (!SiteRules.IsValidSlug(slug)) return null;
        return await TryGetAsync($"data/posts/{slug}.json", SiteJsonContext.Default.PostPage);
    }

    /// <summary>
    /// タグの一覧（読めなければ null）。読めたものは覚えるが、読めなかった（null）ものは覚えず、次に開くとき取り直す
    /// （一度の失敗で、そのあとずっと空になるのを防ぐ）。
    /// </summary>
    public Task<List<TagSummary>?> GetTagsAsync() =>
        _tags.GetAsync(() => TryGetAsync("data/tags.json", SiteJsonContext.Default.ListTagSummary));

    /// <summary>タグの slug → 表示名。同じ slug が 2 つあれば最初のものを使う（例外にして起動を止めない）。</summary>
    private static Dictionary<string, string> TagNamesOf(IEnumerable<TagSummary>? tags)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags ?? []) names.TryAdd(tag.Slug, tag.Name);
        return names;
    }

    /// <summary>ワールドの一覧（読めなければ null）。読めたものは覚えるが、読めなかった（null）ものは覚えず、次に開くとき取り直す。</summary>
    public Task<List<WorldSummary>?> GetWorldsAsync() =>
        _worlds.GetAsync(() => TryGetAsync("data/worlds.json", SiteJsonContext.Default.ListWorldSummary));

    /// <summary>服の一覧（読めなければ null）。読めたものは覚えるが、読めなかった（null）ものは覚えず、次に開くとき取り直す。</summary>
    public Task<List<OutfitSummary>?> GetOutfitsAsync() =>
        _outfits.GetAsync(() => TryGetAsync("data/outfits.json", SiteJsonContext.Default.ListOutfitSummary));

    /// <summary>
    /// 一度読めた一覧だけ覚える。読めなかった（null）ものは覚えず、次に呼ぶとき取り直す。
    /// 同時に呼ばれたときは、取得の Task を 1 つだけ作って待ちを共有する（二重に取らず、待機中の Task を置き換えない）。
    /// </summary>
    private sealed class ReadOnce<T> where T : class
    {
        private readonly object _gate = new();
        private Task<T?>? _task;

        /// <summary>もう読めている値（起動のときに取ったもの）を覚えておく。</summary>
        public void Seed(T value)
        {
            lock (_gate) _task = Task.FromResult<T?>(value);
        }

        public async Task<T?> GetAsync(Func<Task<T?>> load)
        {
            Task<T?> task;
            lock (_gate) task = _task ??= load();
            try
            {
                var value = await task;
                if (value is null) Forget(task);
                return value;
            }
            catch
            {
                Forget(task);
                throw;
            }
        }

        /// <summary>この Task が覚えたものなら、覚えを消す（次に呼ぶとき取り直す）。別の Task に差し替わっていれば触らない。</summary>
        private void Forget(Task<T?> task)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_task, task)) _task = null;
            }
        }
    }

    /// <summary>服 1 つ分（id は服のマスタの id。形式が不正・存在しない・読めないときは null）。</summary>
    public async Task<OutfitPage?> GetOutfitAsync(string id)
    {
        if (!OutfitIdPattern.IsMatch(id)) return null;
        return await TryGetAsync($"data/outfits/{id}.json", SiteJsonContext.Default.OutfitPage);
    }

    /// <summary>ワールド 1 つ分（UUID は wrld_ を除いた形）。形式が不正・存在しない・読めないときは null。</summary>
    public async Task<WorldPage?> GetWorldAsync(string uuid)
    {
        if (WorldId.FromPathSegment(uuid) is null) return null;
        return await TryGetAsync($"data/worlds/{uuid}.json", SiteJsonContext.Default.WorldPage);
    }

    private async Task<T?> TryGetAsync<T>(string url, JsonTypeInfo<T> typeInfo) where T : class
    {
        try
        {
            return await http.GetFromJsonAsync(url, typeInfo);
        }
        catch (Exception e) when (IsReadFailure(e))
        {
            return null;
        }
    }

    private static bool IsReadFailure(Exception e) => e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException;
}
