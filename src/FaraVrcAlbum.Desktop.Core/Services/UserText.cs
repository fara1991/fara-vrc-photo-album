namespace FaraVrcAlbum.Desktop.Services;

/// <summary>
/// CLI と共通の処理から来るメッセージを、画面向けの言葉に言い換える（R2 → 写真の保存先、台帳 → 写真の記録 など）。
/// 画面に出す前に通す。CLI の表示は変えない。
/// </summary>
public static class UserText
{
    private static readonly (string From, string To)[] Words =
    [
        ("。再登録するには --force を付ける", ""),
        ("（--ledger の追加分は 0 件）", ""),
        ("台帳（uploaded）", "アップロード済みの記録"),
        ("uploaded フォルダ", "アップロード済みのフォルダ"),
        ("R2 と台帳", "写真の保存先と写真の記録"),
        ("R2 は未確認（認証情報なし）", "保存先はまだ確かめていません（接続の設定がありません）"),
        ("R2 に在るため", "保存先にあるため"),
        ("R2 に不足", "保存先に足りないファイル"),
        ("R2 には", "保存先には"),
        ("R2", "写真の保存先"),
        ("台帳で", "写真の記録で"),
        ("台帳", "写真の記録"),
        ("XMP", "撮影情報"),
        ("マスタ", "一覧"),
    ];

    public static string Plain(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        foreach (var (from, to) in Words) text = text.Replace(from, to, StringComparison.Ordinal);
        return text;
    }
}
