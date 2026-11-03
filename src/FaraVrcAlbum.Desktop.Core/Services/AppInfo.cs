using System.Reflection;

namespace FaraVrcAlbum.Desktop.Services;

/// <summary>
/// デスクトップ版（アプリ）のバージョン。ビルドに埋め込まれた版とコミットを読む。
/// 手元でビルドして使うので、版は既定の「0.0.0-dev」（Desktop.csproj の Version）のまま「開発版」と表示し、
/// どの版かは<b>コミット</b>で見分ける。-p:Version=1.2.3 で版を指定すると「v1.2.3」と表示する。
/// </summary>
public static class AppInfo
{
    /// <summary>バージョンを指定せずにビルドしたときの値。FaraVrcAlbum.Desktop.csproj の Version と同じにする。</summary>
    public const string DevelopmentVersion = "0.0.0-dev";

    private const int ShortCommitLength = 7;

    /// <summary>
    /// 「1.2.3+abcdef0123…」の形のバージョン情報を、版の番号とコミット（先頭の7文字）に分ける。
    /// 読めないときは、開発版として扱う。
    /// </summary>
    public static (string Version, string? Commit) ParseInformationalVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return (DevelopmentVersion, null);

        var parts = informationalVersion.Trim().Split('+', 2);
        var version = string.IsNullOrWhiteSpace(parts[0]) ? DevelopmentVersion : parts[0].Trim();
        var commit = parts.Length > 1 && parts[1].Length > 0
            ? parts[1][..Math.Min(ShortCommitLength, parts[1].Length)]
            : null;
        return (version, commit);
    }

    /// <summary>表示用のバージョン。例:「v1.2.3（コミット: abc1234）」「開発版（コミット: abc1234）」</summary>
    public static string FormatDisplayVersion(string version, string? commit)
    {
        var label = version == DevelopmentVersion ? "開発版" : $"v{version}";
        return commit is null ? label : $"{label}（コミット: {commit}）";
    }

    /// <summary>アセンブリ（アプリの exe）に埋め込まれたバージョンを、表示用の文字列にする。</summary>
    public static string DisplayVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var (version, commit) = ParseInformationalVersion(informational);
        return FormatDisplayVersion(version, commit);
    }
}
