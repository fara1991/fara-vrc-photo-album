using FaraVrcAlbum.Desktop.Mvvm;
using FaraVrcAlbum.Desktop.Services;

namespace FaraVrcAlbum.Desktop.ViewModels;

/// <summary>
/// 全画面で共有する状態。バケットは本番の 1 つだけ（確認用も本番も同じ R2 を見るため、試験用の選択肢は無い）。
/// </summary>
public sealed class AppState(AlbumSession session) : ObservableObject
{
    public AlbumSession Session { get; } = session;

    /// <summary>画面に出すバケット名（秘密ではない）。</summary>
    public string BucketName => Session.Bucket;
}
