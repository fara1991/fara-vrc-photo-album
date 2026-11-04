using FaraVrcAlbum.Content;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Tools.Cli;

/// <summary>`album avatar` `album outfit` `album photo-avatar` の振り分けと表示（処理は AvatarOutfitService）。</summary>
public static class OutfitCommands
{
    public static int Run(Args args, RepoContext repo)
    {
        var editor = new ContentEditor(repo.ContentDir);
        return args.Command switch
        {
            "outfit" => OutfitRun(args, repo, editor),
            "avatar" => AvatarRun(args, repo, editor),
            "photo-avatar" => PhotoAvatar(args, editor),
            _ => throw new UsageException($"不明なコマンドです: {args.Command}"),
        };
    }

    private static int OutfitRun(Args args, RepoContext repo, ContentEditor editor)
    {
        var sub = args.Positional.FirstOrDefault() ?? throw new UsageException(OutfitUsage);
        var content = editor.LoadContent();
        switch (sub)
        {
            case "list":
                foreach (var (id, entry) in content.Outfits)
                {
                    var extra = string.Join(" ", new[] { entry.Shop is null ? null : $"店: {entry.Shop}", entry.Url is null ? null : "URL あり" }.Where(x => x is not null));
                    Console.WriteLine($"{id}  {entry.Name}{(extra.Length > 0 ? "  " + extra : "")}");
                }
                return 0;
            case "add":
                AvatarOutfitService.OutfitAdd(editor, args.Has("apply"), Id(args), args.Require("name"), args.Value("shop"), args.Value("url"), args.Value("note"));
                return Done(args, $"服 {Id(args)} を追加しました");
            case "edit":
                AvatarOutfitService.OutfitEdit(editor, args.Has("apply"), Id(args), args.Value("name"), args.Value("shop"), args.Value("url"), args.Value("note"));
                return Done(args, $"服 {Id(args)} を直しました（空の値で項目を消す）");
            case "remove":
                AvatarOutfitService.OutfitRemove(editor, args.Has("apply"), Id(args));
                return Done(args, $"服 {Id(args)} を外しました");
            default:
                throw new UsageException(OutfitUsage);
        }
    }

    private static int AvatarRun(Args args, RepoContext repo, ContentEditor editor)
    {
        var sub = args.Positional.FirstOrDefault() ?? throw new UsageException(AvatarUsage);
        var content = editor.LoadContent();
        switch (sub)
        {
            case "list":
                foreach (var (id, entry) in content.Avatars)
                {
                    // アバター名は、ユーザーの手元の確認用に表示する（画面・公開データには出さない）
                    Console.WriteLine($"{id}  {entry.Name}  服: {string.Join(", ", entry.OutfitIds.Select(o => content.Outfits.TryGetValue(o, out var outfit) ? outfit.Name : o))}");
                }
                return 0;
            case "add":
                AvatarOutfitService.AvatarAdd(editor, args.Has("apply"), Id(args), args.Require("name"), Split(args.Value("aliases")) ?? [], args.Value("note"));
                return Done(args, $"アバター {Id(args)} を追加しました（服は album avatar link で紐づけます）");
            case "edit":
                AvatarOutfitService.AvatarEdit(editor, args.Has("apply"), Id(args), args.Value("name"), args.Value("aliases") is null ? null : Split(args.Value("aliases")), args.Value("note"));
                return Done(args, $"アバター {Id(args)} を直しました");
            case "link":
                AvatarOutfitService.AvatarLink(editor, args.Has("apply"), Id(args), Split(args.Value("outfits")) ?? throw new UsageException("--outfits id,id を指定してください（順序どおり）"));
                return Done(args, $"アバター {Id(args)} の服を置き換えました");
            case "remove":
                AvatarOutfitService.AvatarRemove(editor, args.Has("apply"), Id(args));
                return Done(args, $"アバター {Id(args)} を外しました");
            default:
                throw new UsageException(AvatarUsage);
        }
    }

    private static int PhotoAvatar(Args args, ContentEditor editor)
    {
        var key = args.Positional.FirstOrDefault() ?? throw new UsageException("使い方: album photo-avatar <写真のキー> <アバター> [--outfits id,id] [--apply]");
        var avatar = args.Positional.ElementAtOrDefault(1) ?? throw new UsageException("使い方: album photo-avatar <写真のキー> <アバター> [--outfits id,id] [--apply]");
        AvatarOutfitService.PhotoAvatar(editor, args.Has("apply"), key, avatar, Split(args.Value("outfits")));
        return Done(args, $"写真 {key} にアバター {avatar} を付けました");
    }

    private static string Id(Args args) => args.Positional.ElementAtOrDefault(1) ?? throw new UsageException("id を指定してください");

    private static List<string>? Split(string? text) =>
        text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static int Done(Args args, string message)
    {
        if (args.Has("apply"))
        {
            Console.WriteLine(message + "。書いたあと content/ の検査を通しました（Git の差分を確かめてから PR に）。");
            return 0;
        }
        Console.WriteLine($"プレビュー（書いていません。検査は通りました）: {message}。");
        Console.WriteLine("問題がなければ同じコマンドに --apply を付けてください。");
        return 0;
    }

    private const string OutfitUsage =
        "使い方: album outfit list ／ album outfit add <id> --name 名前 [--shop 店名] [--url https://…] [--note 備考] [--apply]" +
        " ／ album outfit edit <id> [--name …] [--shop …] [--url …] [--note …] [--apply]（空の値で項目を消す）" +
        " ／ album outfit remove <id> [--apply]（使われていれば拒否）";

    private const string AvatarUsage =
        "使い方: album avatar list ／ album avatar add <id> --name アバター名 [--aliases a,b] [--note 備考] [--apply]" +
        " ／ album avatar edit <id> [--name …] [--aliases …] [--note …] [--apply]" +
        " ／ album avatar link <id> --outfits id,id [--apply]（順序どおり。既定の服を置き換え）" +
        " ／ album avatar remove <id> [--apply]（写真に使われていれば拒否）";
}
