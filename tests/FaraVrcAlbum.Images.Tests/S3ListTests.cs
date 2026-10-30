using Amazon.S3.Model;
using FaraVrcAlbum.Images;
using Xunit;

namespace FaraVrcAlbum.Images.Tests;

/// <summary>R2 の一覧の応答の読み方（S3Objects が null のときも落ちない）。</summary>
public sealed class S3ListTests
{
    [Fact]
    public void AListWithNoMatchingObjectsMeansNoObjectsNotACrash()
    {
        // 該当するオブジェクトが 1 つも無いとき、AWS SDK は S3Objects に空のリストではなく null を返す
        var empty = new ListObjectsV2Response { S3Objects = null };

        Assert.Empty(S3ObjectStore.ObjectsOf(empty));
    }

    [Fact]
    public void AListWithObjectsGivesTheirKeysAndSizesAndAMissingSizeIsZero()
    {
        var response = new ListObjectsV2Response
        {
            S3Objects = [new S3Object { Key = "sync/a.json", Size = 12 }, new S3Object { Key = "sync/b.json", Size = null }],
        };

        Assert.Equal([("sync/a.json", 12L), ("sync/b.json", 0L)], S3ObjectStore.ObjectsOf(response));
    }
}
