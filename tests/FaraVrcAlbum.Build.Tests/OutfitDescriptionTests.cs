using Xunit;

namespace FaraVrcAlbum.Build.Tests;

/// <summary>服のページの説明: BOOTH の説明から、対応アバターの一覧と利用規約（とその URL）だけを出す。前文は出さない。</summary>
public class OutfitDescriptionTests
{
    [Fact]
    public void KeepsTheAvatarListAndTheTermsAndDropsThePreamble()
    {
        const string text = """
            ※ 値段が変わることがあります。ご了承ください。
            ――――――――
            🖤 サンプルドレス、登場。
            ふわふわのスカートが特徴の、架空の衣装です。

            対応アバター

            ■ Alpha | アルファ
            https://example.booth.pm/items/1
            ■ Beta | ベータ
            https://example.booth.pm/items/2

            ■利用規約
            商用利用：可
            再配布：禁止
            https://example.com/terms
            """;

        var summary = OutfitDescription.Summarize(text)!;

        Assert.StartsWith("【対応アバター】", summary);
        Assert.Contains("■ Alpha | アルファ\nhttps://example.booth.pm/items/1", summary);
        Assert.Contains("■ Beta | ベータ", summary);
        Assert.Contains("【利用規約】\n商用利用：可\n再配布：禁止\nhttps://example.com/terms", summary);
        Assert.DoesNotContain("ふわふわ", summary);
        Assert.DoesNotContain("値段", summary);
    }

    [Fact]
    public void ADescriptionWithNeitherSectionGivesNothing()
    {
        Assert.Null(OutfitDescription.Summarize("かわいい衣装です。\nぜひ使ってね。"));
        Assert.Null(OutfitDescription.Summarize(""));
        Assert.Null(OutfitDescription.Summarize(null));
    }

    [Fact]
    public void ASingleLineThatOnlyLooksLikeAHeadingIsNotUsed()
    {
        // 「対応アバター」という語を含むだけの 1 行や、中身の無い見出しは出さない（前文を代わりに出さない）
        Assert.Null(OutfitDescription.Summarize("対応アバター\n\n別の説明がここから始まります。"));
        Assert.Null(OutfitDescription.Summarize("■利用規約\n"));
    }

    [Fact]
    public void TermsLinkedOnTheSameLineAreKept()
    {
        const string text = "とてもかわいい衣装です。\n利用規約/Terms of use ・Terms 【 https://example.com/terms 】\n更新履歴 ▶ver1.00";

        var summary = OutfitDescription.Summarize(text)!;

        Assert.Equal("【利用規約】\n利用規約/Terms of use ・Terms 【 https://example.com/terms 】", summary);
    }

    [Fact]
    public void TheNextSectionEndsTheTerms()
    {
        const string text = "■利用規約\n1.利用主体\n個人のみ\n2.禁止事項\n転載禁止\n\n■更新履歴\nv1.0 公開";

        var summary = OutfitDescription.Summarize(text)!;

        Assert.Contains("転載禁止", summary);
        Assert.DoesNotContain("更新履歴", summary);
    }
}
