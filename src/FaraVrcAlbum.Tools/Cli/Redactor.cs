using System.Text.RegularExpressions;

namespace FaraVrcAlbum.Tools.Cli;

/// <summary>
/// エラー表示から秘密を消す（R2 の値そのもの・アクセスキー ID や Secret の形をしたもの・アカウント ID）。
/// AWS の例外メッセージに認証の情報が混ざることがあるため、表示の直前に必ず通す。
/// 形の判定は、正当な値（Git の SHA-1 = 40 桁の小文字 16 進、写真の内容ハッシュ = 64 桁）を消しすぎないように絞っている。
/// </summary>
public static partial class Redactor
{
    private const string Mask = "***";

    /// <summary>アクセスキー ID（AKIA / ASIA + 16 文字）。</summary>
    [GeneratedRegex(@"\b(AKIA|ASIA)[A-Z0-9]{16}\b")]
    private static partial Regex AccessKeyShape();

    /// <summary>
    /// シークレットアクセスキーの形（40 文字の英数字・/・+）。英大文字・英小文字・数字をすべて含むものだけ。
    /// 小文字の 16 進だけの 40 桁（Git のコミット SHA-1）は含まないので消さない。
    /// </summary>
    [GeneratedRegex(@"\b(?=[A-Za-z0-9/+]{40}\b)(?=[A-Za-z0-9/+]*[A-Z])(?=[A-Za-z0-9/+]*[a-z])(?=[A-Za-z0-9/+]*[0-9])[A-Za-z0-9/+]{40}\b")]
    private static partial Regex SecretShape();

    /// <summary>アカウント ID（32 桁の小文字 16 進）。</summary>
    [GeneratedRegex(@"\b[0-9a-f]{32}\b")]
    private static partial Regex AccountShape();

    /// <summary>64 桁の小文字 16 進（SHA-256。写真の内容ハッシュや、認証情報の別の形）。</summary>
    [GeneratedRegex(@"\b[0-9a-f]{64}\b")]
    private static partial Regex Sha256Shape();

    public static string Redact(string text, IEnumerable<string> secrets)
    {
        var result = text;
        foreach (var secret in secrets.Where(s => s.Length >= 4).OrderByDescending(s => s.Length))
        {
            result = result.Replace(secret, Mask, StringComparison.Ordinal);
        }
        result = AccessKeyShape().Replace(result, Mask);
        result = SecretShape().Replace(result, Mask);
        result = Sha256Shape().Replace(result, Mask);
        result = AccountShape().Replace(result, Mask);
        return result;
    }
}
