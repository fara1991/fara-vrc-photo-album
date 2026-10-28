using FaraSiteKit.Core;
using FaraVrcAlbum.Models;

namespace FaraVrcAlbum.Web.Services;

/// <summary>
/// このサイトの名前・作者・保存のキー・確認用ホスト。共通部品（fara-site-kit。フッター・報告フォームなど）が読む SiteOptions に、起動時に入れる（Program.cs）。
/// 他のサイト（pokemon・avatar・bot・portfolio）と、フッターの形を同じにする。
/// </summary>
public static class SiteInfo
{
    public const string Author = "Fara";

    /// <summary>サイトを公開した年（© の年）。</summary>
    public const int PublishedYear = 2026;

    /// <summary>作者のホームページ（確認用のホストで開いているときは、kit の SiteLinks.Localize が確認用に置き換える）。</summary>
    public const string PortfolioUrl = "https://fara-labs.com/";

    /// <summary>確認用サイトのホスト名。</summary>
    public const string DevelopHost = "vrc-album-dev.fara-labs.com";

    /// <summary>設定（テーマ）を入れる localStorage のキー。index.html の theme-init.js の data-storage-key・js/theme-default.js の KEY と同じ値（tests/js が一致を確かめる）。</summary>
    public const string StorageKey = "fara-vrc-album.settings";

    /// <summary>PC のサイドバーを隠しているか（"1" で隠す）の localStorage のキー。index.html の theme-init.js の data-sidebar-key と同じ値。</summary>
    public const string SidebarKey = "fara-vrc-album.sidebar-hidden";

    /// <summary>
    /// フッターの SNS（GitHub・X・Twitch・Discord）。URL は fara-labs の profile.json・pokemon の SiteInfo と同じ値。
    /// 並びは fara-labs と同じ（kit が絵柄を持つ 4 サービスの順）。
    /// </summary>
    public static void Configure(SiteOptions options, SiteSettings settings)
    {
        options.Name = settings.SiteName;
        options.Author = Author;
        options.PublishedYear = PublishedYear;
        options.AuthorHomeUrl = PortfolioUrl;
        options.HomeHref = "./";
        options.DevelopHost = DevelopHost;
        options.StorageKey = StorageKey;
        options.SidebarKey = SidebarKey;
        options.Social.Add(new SiteSocialLink("github", "https://github.com/fara1991"));
        options.Social.Add(new SiteSocialLink("twitter", SiteRules.AuthorXUrl));
        options.Social.Add(new SiteSocialLink("twitch", SiteRules.AuthorTwitchUrl));
        options.Social.Add(new SiteSocialLink("discord", SiteRules.AuthorDiscordUrl));
    }
}
