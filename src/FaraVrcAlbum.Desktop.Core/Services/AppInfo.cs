using System.Reflection;

namespace FaraVrcAlbum.Desktop.Services;

/// <summary>
/// デスクトップ版（アプリ）のバージョン。ビルドに埋め込まれた版を読む。
/// 手元でビルドして使うので、版は既定の「0.0.0-dev」（Desktop.csproj の Version）のまま「開発版」と表示する。
/// -p:Version=1.2.3 で版を指定すると「v1.2.3」と表示する。
/// コミットの番号は、アプリに表示も埋め込みもしない（Desktop.csproj の IncludeSourceRevisionInInformationalVersion を false にしている）。
/// </summary>
public static class AppInfo
{
    /// <summary>バージョンを指定せずにビルドしたときの値。FaraVrcAlbum.Desktop.csproj の Version と同じにする。</summary>
    public const string DevelopmentVersion = "0.0.0-dev";

    /// <summary>
    /// バージョン情報から、版の番号だけを取り出す。読めないときは、開発版として扱う。
    /// 「+」以降（ビルドのメタデータ）は、使わない。
    /// </summary>
    public static string ParseInformationalVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return DevelopmentVersion;

        var version = informationalVersion.Split('+', 2)[0].Trim();
        return version.Length == 0 ? DevelopmentVersion : version;
    }

    /// <summary>表示用のバージョン。例:「v1.2.3」「開発版」</summary>
    public static string FormatDisplayVersion(string version)
        => version == DevelopmentVersion ? "開発版" : $"v{version}";

    /// <summary>アセンブリ（アプリの exe）に埋め込まれたバージョンを、表示用の文字列にする。</summary>
    public static string DisplayVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return FormatDisplayVersion(ParseInformationalVersion(informational));
    }
}
