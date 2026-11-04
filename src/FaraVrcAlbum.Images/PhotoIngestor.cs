using System.Globalization;

namespace FaraVrcAlbum.Images;

public enum IngestState
{
    /// <summary>計画のみ（ドライラン）。R2 には書いていない。</summary>
    Planned,

    /// <summary>R2 に書いた（不足分の PUT を行い、台帳に登録した）。</summary>
    Uploaded,

    /// <summary>R2 に全部そろっていて、書き込みは無い。</summary>
    AlreadyUploaded,

    /// <summary>取り込まない（削除済み・PNG でない・撮影日時が無い・2025 年 11 月より前・キーの衝突）。</summary>
    Refused,
}

/// <summary>1 ファイルの結果。LedgerChanged が true のとき、呼び出し側は台帳を保存する。</summary>
public sealed record IngestResult(
    string SourceName,
    IngestState State,
    string? Key,
    string Message,
    IReadOnlyList<string> Warnings,
    int PutCount,
    bool LedgerChanged);

public sealed record IngestOptions
{
    /// <summary>true で R2 に書く。false は計画のみ（store があれば HEAD で既存だけ確かめる）。</summary>
    public bool Apply { get; init; }

    /// <summary>台帳の removed を、明示的に取り消して再登録する。</summary>
    public bool Force { get; init; }

    /// <summary>フォルダの月（inbox/yyyy-mm）。撮影月と違えば警告する（アップロード先は撮影月）。</summary>
    public string? FolderMonth { get; init; }
}

/// <summary>
/// 1 枚の写真を取り込む（計画書 §5.5.3）。冪等: 同じファイルを何度流しても、R2 への PUT は不足分だけ。
/// 手順: 内容ハッシュ → 台帳を引く（removed は --force が無ければ拒否）→ 撮影日時・メタデータ → 変換（メタデータ除去を検査）
/// → 各出力を HEAD → 不足分だけ PUT → 台帳に登録。途中で失敗したら台帳には登録しない（次の実行で HEAD により不足分だけ補われる）。
/// </summary>
public sealed class PhotoIngestor(PhotoLedger ledger, IObjectStore? store)
{
    public async Task<IngestResult> IngestAsync(string path, IngestOptions options, CancellationToken ct = default)
    {
        var name = Path.GetFileName(path);
        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (!PngReader.IsPng(bytes)) return Refuse(name, "PNG ではありません");

        var sha = Hashing.Sha256Hex(bytes);
        var existing = ledger.FindBySha(sha);
        if (existing is { State: LedgerStates.Removed } && !options.Force)
        {
            return Refuse(name, $"台帳で削除済み（{existing.Key}）。再登録するには --force を付ける", existing.Key);
        }

        var xml = PngReader.FindXmp(bytes);
        var xmp = xml is null ? null : XmpReader.Parse(xml);
        var warnings = new List<string>();

        var (takenAt, fromXmp) = ResolveTakenAt(xmp, name);
        if (takenAt is null) return Refuse(name, "撮影日時が分かりません（XMP も VRChat_ のファイル名も無い）");
        if (takenAt.Value < SiteRules.EarliestPhotoDate)
        {
            return Refuse(name, $"2025 年 11 月より前の写真は載せません（撮影 {PhotoKeys.TakenAtText(takenAt.Value)}）");
        }
        if (!fromXmp) warnings.Add("撮影時刻は XMP に無く、ファイル名から取った（JST とみなす）");
        if (xmp is null) warnings.Add("XMP が無いため、ワールドは空になります（後から手で補う）");
        else if (xmp.WorldId is null) warnings.Add("ワールド ID が無いか形式が不正なため、ワールドは空になります");
        else if (DisplayName.Clean(xmp.WorldName) is null)
        {
            // ワールド名が XMP に無い場合と、あるが使えない（制御文字だけ・長すぎる）場合を区別して伝える
            warnings.Add(string.IsNullOrEmpty(xmp.WorldNameRaw)
                ? "ワールド名が XMP に無いため、ワールドの名前は空になります（ワールドはマスタに追加されません）"
                : "ワールド名が使えない（制御文字だけ・長すぎる）ため、ワールドの名前は空になります（ワールドはマスタに追加されません）");
        }

        var key = PhotoKeys.Build(takenAt.Value, sha);
        var keyOwner = ledger.ShaOfKey(key);
        if (keyOwner is not null && keyOwner != sha)
        {
            return Refuse(name, $"キー {key} が別の写真と衝突しています（同じ時刻・ハッシュ 8 桁）。確認が必要", key);
        }

        if (options.FolderMonth is not null && options.FolderMonth != PhotoKeys.MonthOf(key))
        {
            warnings.Add($"フォルダ（{options.FolderMonth}）と撮影月（{PhotoKeys.MonthOf(key)}）が違うため、アップロード先は撮影月です");
        }

        var encoded = PhotoEncoder.Encode(bytes);
        var missing = new List<EncodedVariant>();
        foreach (var variant in encoded.Variants)
        {
            var present = store is not null && await store.ExistsAsync(key + variant.Suffix, ct);
            if (!present) missing.Add(variant);
        }
        var alreadyRegistered = existing is { State: LedgerStates.Uploaded } && existing.Key == key;
        var alreadyOnR2 = alreadyRegistered && missing.Count == 0;

        if (!options.Apply)
        {
            // ドライラン: 何も書かない（R2 の在るかを確かめるだけ）
            if (alreadyOnR2) return new IngestResult(name, IngestState.Planned, key, "計画: R2 に在るため、登録済み", warnings, 0, false);
            var note = store is null ? "R2 は未確認（認証情報なし）" : $"R2 に不足 {missing.Count}/{encoded.Variants.Count} 件";
            return new IngestResult(name, IngestState.Planned, key, $"計画: {note}", warnings, 0, false);
        }

        if (store is null) throw new InvalidOperationException("apply には R2 への接続が必要です");

        foreach (var variant in missing)
        {
            await store.PutAsync(key + variant.Suffix, variant.Data, variant.ContentType, ObjectHeaders.ImmutableImage, ct);
        }

        if (alreadyOnR2) return new IngestResult(name, IngestState.AlreadyUploaded, key, "R2 に在るため、登録済み", warnings, 0, false);

        ledger.Put(sha, BuildEntry(key, name, takenAt.Value, encoded, xmp));
        return new IngestResult(name, IngestState.Uploaded, key,
            $"アップロード {missing.Count} 件（全 {encoded.Variants.Count} 件）", warnings, missing.Count, true);
    }

    /// <summary>撮影時刻: XMP の CreateDate を優先し、無ければファイル名（JST とみなす）。</summary>
    public static (DateTimeOffset? Time, bool FromXmp) ResolveTakenAt(XmpInfo? xmp, string name)
    {
        if (xmp?.CreateDate is { } created) return (PhotoKeys.Normalize(created), true);
        var fromName = PhotoKeys.ParseFileName(name);
        return fromName is { } parsed ? (PhotoKeys.Normalize(parsed.TakenAt), false) : (null, false);
    }

    private static LedgerEntry BuildEntry(string key, string name, DateTimeOffset takenAt, EncodedPhoto encoded, XmpInfo? xmp)
    {
        var entry = new LedgerEntry
        {
            Key = key,
            State = LedgerStates.Uploaded,
            UploadedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            TakenAt = PhotoKeys.TakenAtText(takenAt),
            SourceName = name,
            W = encoded.W,
            H = encoded.H,
            Color = encoded.Color,
            Encoder = PhotoEncoder.Generation,
            Bytes = encoded.Variants.ToDictionary(v => v.Name, v => (long)v.Data.Length),
        };
        if (xmp?.WorldId is not null) entry.World = new LedgerWorld { Id = xmp.WorldId, Name = DisplayName.Clean(xmp.WorldName) ?? "" };
        entry.AvatarName = xmp?.AvatarName;
        entry.AvatarId = xmp?.AvatarId;
        return entry;
    }

    private static IngestResult Refuse(string name, string message, string? key = null) =>
        new(name, IngestState.Refused, key, message, [], 0, false);
}
