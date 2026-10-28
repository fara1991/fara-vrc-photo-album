using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FaraVrcAlbum.Tools.Cli;

/// <summary>
/// 手元の設定ファイルの書き込み（デスクトップ版の設定画面が使う）。どちらも Git 管理外。
///   .env.local … R2 の認証情報とバケット名（KEY=値 の行。ほかの行・コメント・順序は保つ）
///   local/album.local.json … アップロード用フォルダ・元写真のフォルダ
/// 値はどこにも表示しない（呼び出し側も出さない）。
/// </summary>
public static class LocalSettings
{
    /// <summary>.env.local の項目を書き換える。null か空は、その行を消す。無い項目は末尾に足す。</summary>
    public static void SetEnvValues(string path, IReadOnlyDictionary<string, string?> values)
    {
        foreach (var (name, value) in values)
        {
            if (!IsEnvName(name)) throw new ArgumentException($"項目の名前が不正です: {name}");
            if (value is not null && (value.Contains('\n') || value.Contains('\r'))) throw new ArgumentException($"{name} の値に改行は入れられません");
        }
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var done = new HashSet<string>(StringComparer.Ordinal);
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var name = line[..eq].Trim();
            if (!values.TryGetValue(name, out var value)) continue;
            // 同じ項目が複数行あれば、最後の 1 行だけを書き換えて、ほかは消す（読み込みは最後の行が勝つ）
            if (string.IsNullOrEmpty(value) || done.Contains(name)) lines.RemoveAt(i);
            else lines[i] = $"{name}={value}";
            done.Add(name);
        }
        foreach (var (name, value) in values)
        {
            if (!done.Contains(name) && !string.IsNullOrEmpty(value)) lines.Add($"{name}={value}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
    }

    /// <summary>local/album.local.json の項目を書き換える（null か空は消す。ほかの項目は保つ）。</summary>
    public static void SetLocalConfig(string path, IReadOnlyDictionary<string, string?> values)
    {
        var node = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject() : new JsonObject();
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrEmpty(value)) node.Remove(key);
            else node[key] = value;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
    }

    private static bool IsEnvName(string name) => name.Length > 0 && name.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_');
}
