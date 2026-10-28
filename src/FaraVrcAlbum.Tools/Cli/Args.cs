namespace FaraVrcAlbum.Tools.Cli;

/// <summary>使い方の誤り（メッセージをそのまま表示して終了コード 2）。</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// コマンドライン引数。<c>album コマンド 位置引数… --値の付くオプション 値 --スイッチ</c> の形。
/// スイッチ（値を取らないもの）は <see cref="Switches"/> に限る。それ以外の --名前 は値を 1 つ取る。
/// </summary>
public sealed class Args
{
    private static readonly HashSet<string> Switches = ["apply", "keep", "force", "append", "help", "ledger"];

    private readonly Dictionary<string, string?> _flags = new(StringComparer.Ordinal);
    private readonly List<string> _positional = [];

    private Args()
    {
    }

    public string Command { get; private set; } = "help";

    public IReadOnlyList<string> Positional => _positional;

    public static Args Parse(string[] argv)
    {
        var args = new Args();
        if (argv.Length == 0) return args;
        args.Command = argv[0];
        for (var i = 1; i < argv.Length; i++)
        {
            var token = argv[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                args._positional.Add(token);
                continue;
            }
            var name = token[2..];
            if (name.Length == 0) throw new UsageException("-- の後ろにオプション名がありません");
            if (Switches.Contains(name))
            {
                args._flags[name] = null;
                continue;
            }
            if (i + 1 >= argv.Length || argv[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new UsageException($"--{name} には値が必要です");
            }
            args._flags[name] = argv[++i];
        }
        return args;
    }

    public bool Has(string name) => _flags.ContainsKey(name);

    public string? Value(string name) => _flags.GetValueOrDefault(name);

    public string Require(string name) => Value(name) ?? throw new UsageException($"--{name} を指定してください");
}
