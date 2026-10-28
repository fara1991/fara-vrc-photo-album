using System.Diagnostics;

namespace FaraVrcAlbum.Tools.Cli;

/// <summary>
/// 参照の判定は作業ツリーの記事と台帳だけで行う。作業ツリーが origin/develop 以降と一致していないと、
/// 別のブランチの記事を見て「参照されていない」と判定しうるので、その場合は警告を出す（処理は止めない）。
/// git を実行できないときも、確かめられなかったと警告する。
/// </summary>
public static class WorkTreeCheck
{
    /// <summary>警告の一覧（空なら一致している）。</summary>
    public static IReadOnlyList<string> Warnings(string root)
    {
        var warnings = new List<string>();
        var status = Git(root, "status", "--porcelain");
        if (status is null)
        {
            warnings.Add("git を実行できないため、作業ツリーが origin/develop と一致しているか確かめられません（参照の判定は作業ツリーだけで行います）");
            return warnings;
        }
        if (status.Length > 0) warnings.Add("未コミットの変更があります。参照の判定は作業ツリーの内容だけで行うので、結果を確かめてください");

        var develop = Git(root, "rev-parse", "--verify", "--quiet", "origin/develop");
        if (develop is null || develop.Length == 0)
        {
            warnings.Add("origin/develop が見つからないため、作業ツリーが develop と一致しているか確かめられません（git fetch origin develop を実行してください）");
            return warnings;
        }
        // develop が HEAD の祖先でなければ、develop に無いコミット（未マージのブランチ）の上にいる
        if (Git(root, "merge-base", "--is-ancestor", "origin/develop", "HEAD") is null)
        {
            warnings.Add("作業ツリーは origin/develop より新しくない（develop に無いコミットを含む未マージのブランチ）です。ほかのブランチの記事を見ていないか確かめてください");
        }
        return warnings;
    }

    /// <summary>git を実行して標準出力を返す。終了コードが 0 でなければ null（merge-base --is-ancestor は「祖先でない」で 1）。</summary>
    private static string? Git(string root, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = root };
            foreach (var arg in new[] { "-C", root }.Concat(args)) info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
