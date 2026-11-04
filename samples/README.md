# samples/

CI（GitHub Actions）のビルド・テスト用の、**架空の**記事の中身です（`content/` と同じ形）。本物の記事は `content/` にあり、Git には入れません（`.gitignore`）。

- テストは `content/` があればそれ、無ければここを使います（`tests/**/RepoRoot.cs`）。
- ビルドは `--content samples/content`（または環境変数 `ALBUM_CONTENT_DIR`）で、ここを読めます。ただし**公開には使えません**: 出力にサンプルの記事（slug が `sample-` で始まる）があると、公開前の確認（`.github/scripts/verify-site.mjs`）が止めます。CI のビルドだけ `ALBUM_ALLOW_SAMPLE=1` を付けて通します。
