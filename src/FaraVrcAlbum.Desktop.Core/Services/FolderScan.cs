using System.Globalization;

namespace FaraVrcAlbum.Desktop.Services;

/// <summary>Unity のフォルダなどから、アバター・服の名前の候補を読む（読むだけ。ファイルは変えない）。</summary>
public static class FolderScan
{
    /// <summary>フォルダの下（サブフォルダも）の prefab のファイル名（拡張子なし）。.meta は除く。重複は 1 つにまとめる。</summary>
    public static IReadOnlyList<string> PrefabNames(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        return Directory.EnumerateFiles(folder, "*.prefab", options)
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>フォルダ直下のフォルダ名とファイル名（拡張子なし）。服の候補（1 つの服が 1 フォルダか 1 つのパッケージのことが多い）。</summary>
    public static IReadOnlyList<string> TopLevelNames(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var options = new EnumerationOptions { IgnoreInaccessible = true };
        var folders = Directory.EnumerateDirectories(folder, "*", options).Select(Path.GetFileName);
        var files = Directory.EnumerateFiles(folder, "*", options)
            .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileNameWithoutExtension);
        return folders.Concat(files).OfType<string>().Where(n => !n.StartsWith('.')).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 服の候補。フォルダが BOOTH Library Manager のもの（b + 商品 ID のフォルダ）で、商品の情報が読めるときは、
    /// カテゴリ「3D モデル」・サブカテゴリ「3D 衣装」の商品だけを、商品名・お店つきで返す。
    /// 読めないときは、フォルダ直下の名前をそのまま返す（Note に理由）。
    /// </summary>
    public static ImportScan OutfitCandidates(string folder, string? boothDatabase = null)
    {
        if (!Directory.Exists(folder)) return new ImportScan([], "");
        var folders = Directory.EnumerateDirectories(folder, "*", new EnumerationOptions { IgnoreInaccessible = true })
            .Select(Path.GetFileName).OfType<string>().ToList();
        var ids = folders.Select(BoothLibrary.ItemIdOfFolder).Where(id => id is not null).Select(id => id!.Value).ToHashSet();
        if (ids.Count == 0) return new ImportScan(TopLevelNames(folder).Select(n => new ImportEntry(n)).ToList(), "（BOOTH の商品フォルダ（b + 数字）ではないので、フォルダ名をそのまま出しています）");

        var items = BoothLibrary.Load(boothDatabase);
        if (items is null)
            return new ImportScan(TopLevelNames(folder).Select(n => new ImportEntry(n)).ToList(), "（BOOTH Library Manager のデータが読めないので、フォルダ名をそのまま出しています）");
        var byId = items.ToDictionary(i => i.Id);
        var outfits = ids.Where(byId.ContainsKey).Select(id => byId[id])
            .Where(i => i.ParentCategory == BoothLibrary.OutfitParentCategory && i.SubCategory == BoothLibrary.OutfitSubCategory)
            .OrderBy(i => i.Name, StringComparer.Ordinal)
            .Select(i => new ImportEntry(BoothLibrary.ToDisplayName(i.Name), BoothLibrary.ToDisplayName(i.ShopName), BoothLibrary.ToDisplayName(i.ShopName), i.Url))
            .ToList();
        return new ImportScan(outfits, $"（BOOTH の商品 {ids.Count} 件のうち、「{BoothLibrary.OutfitParentCategory}」→「{BoothLibrary.OutfitSubCategory}」だけを出しています）");
    }

    /// <summary>次の id（今ある数字の id の最大 + 1。数字でない id は無視する）。</summary>
    public static string NextId(IEnumerable<string> existing)
    {
        var max = 0;
        foreach (var id in existing)
            if (int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        return (max + 1).ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>取り込みの候補 1 件。Detail は一覧の補足（お店など）、Shop・Url は登録するときの項目。</summary>
public sealed record ImportEntry(string Name, string Detail = "", string? Shop = null, string? Url = null, IReadOnlyList<string>? SuggestedOutfitIds = null, string Suggestion = "");

/// <summary>フォルダを読んだ結果（候補と、一覧の下に出す注記）。</summary>
public sealed record ImportScan(IReadOnlyList<ImportEntry> Entries, string Note);
