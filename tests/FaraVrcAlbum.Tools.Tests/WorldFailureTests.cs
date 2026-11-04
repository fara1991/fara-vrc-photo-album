using System.Net.Http;
using System.Text.Json;
using Amazon.S3;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>ワールドの取得で「そのワールドだけの失敗」として次へ進む例外と、バグとして投げる例外の区別。</summary>
public sealed class WorldFailureTests
{
    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(InvalidDataException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void TheExpectedFailuresAreCountedPerWorld(Type type)
    {
        var e = (Exception)Activator.CreateInstance(type, "x")!;
        Assert.True(WorldImageService.IsPerWorldFailure(e));
    }

    [Fact]
    public void AnR2ServiceErrorIsCountedPerWorld()
    {
        Assert.True(WorldImageService.IsPerWorldFailure(new AmazonS3Exception("bucket")));
    }

    [Theory]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    public void ABugIsNotSwallowedAsAPerWorldFailure(Type type)
    {
        var e = (Exception)Activator.CreateInstance(type, "bug")!;
        Assert.False(WorldImageService.IsPerWorldFailure(e));
    }
}
