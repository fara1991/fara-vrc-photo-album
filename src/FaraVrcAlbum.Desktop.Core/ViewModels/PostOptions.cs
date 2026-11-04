using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Desktop.ViewModels;

/// <summary>記事の一覧の 1 行。</summary>
public sealed class PostOptionViewModel(PostSummary summary)
{
    public string Slug { get; } = summary.Slug;

    public string Title { get; } = summary.Title;

    public bool Draft { get; } = summary.Draft;

    public IReadOnlyList<string> PhotoKeys { get; } = summary.PhotoKeys;

    public string Cover { get; } = summary.Cover ?? "";

    public string Text => (Draft ? "[非公開] " : "") + (Title.Length > 0 ? Title : Slug) + $"（{PhotoKeys.Count} 枚）" + (summary.Problem is null ? "" : " ※ " + summary.Problem);

    public override string ToString() => Text;
}

/// <summary>アバターの選択肢（「（なし）」は Id が空）。</summary>
public sealed record AvatarOptionViewModel(string Id, string Name)
{
    public static readonly AvatarOptionViewModel None = new("", "（なし）");

    /// <summary>選ぶときに出す文（名前で検索できるように、先頭は名前）。</summary>
    public string Text => Id.Length == 0 ? Name : $"{Name}（{Id}）";

    public override string ToString() => Text;
}
