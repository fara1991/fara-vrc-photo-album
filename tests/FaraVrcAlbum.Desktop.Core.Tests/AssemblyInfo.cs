using Xunit;

// バケット名の設定（RepoContext の static な値）を試すテストがあるので、並列には走らせない（全体で 1 秒ほど）
[assembly: CollectionBehavior(DisableTestParallelization = true)]
