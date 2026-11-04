using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>FileSnapshot.Restore: 1 つ戻せなくても残りは戻し、戻せなかったものを一覧にして投げる。</summary>
public sealed class FileSnapshotRestoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fara-snapshot-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void AFailedRestoreDoesNotStopTheOthersAndIsReportedTogether()
    {
        var a = Path.Combine(_dir, "a.json");
        var b = Path.Combine(_dir, "sub", "b.json");
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.WriteAllText(a, "A");
        File.WriteAllText(b, "B");

        var snapshot = new FileSnapshot();
        snapshot.Capture(a);
        snapshot.Capture(b);

        // b を戻せない状態にする（親の「sub」をファイルにする）。a は戻せるはず
        File.WriteAllText(a, "changed");
        File.Delete(b);
        Directory.Delete(Path.GetDirectoryName(b)!);
        File.WriteAllText(Path.Combine(_dir, "sub"), "not a folder");

        var error = Assert.Throws<IOException>(() => snapshot.Restore());
        Assert.Contains("b.json", error.Message);
        Assert.Equal("A", File.ReadAllText(a));                  // 戻せるものは、途中で止めずに戻している
    }

    [Fact]
    public void ARunThatFailsIsRestoredAndRethrowsTheSameExceptionType()
    {
        var a = Path.Combine(_dir, "a.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(a, "A");

        var error = Assert.Throws<InvalidDataException>(() => FileSnapshot.Run([a], () =>
        {
            File.WriteAllText(a, "changed");
            throw new InvalidDataException("検査の失敗");
        }));
        Assert.Equal("検査の失敗", error.Message);
        Assert.Equal("A", File.ReadAllText(a));
    }

    [Fact]
    public void WhenTheCheckAndTheRestoreBothFailTheOriginalErrorIsKeptAsTheInnerException()
    {
        var b = Path.Combine(_dir, "sub", "b.json");
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.WriteAllText(b, "B");

        var error = Assert.Throws<IOException>(() => FileSnapshot.Run([b], () =>
        {
            // 検査の失敗のあと、戻し先（親のフォルダ）もファイルに化けていて戻せない
            File.WriteAllText(b, "changed");
            Directory.Delete(Path.GetDirectoryName(b)!, recursive: true);
            File.WriteAllText(Path.Combine(_dir, "sub"), "not a folder");
            throw new InvalidDataException("content/ の検査に失敗しました:\n  posts/x.json: 形式が違います");
        }));
        var inner = Assert.IsType<InvalidDataException>(error.InnerException);
        Assert.Contains("形式が違います", inner.Message);                      // 原因は消えず、InnerException と メッセージの両方に残る
        Assert.Contains("形式が違います", error.Message);
        Assert.Contains("元に戻すこともできませんでした", error.Message);
    }

    [Fact]
    public void APreviewRunRestoresEvenWhenItSucceeds()
    {
        var a = Path.Combine(_dir, "a.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(a, "A");

        FileSnapshot.Run([a], () => File.WriteAllText(a, "changed"), keep: false);
        Assert.Equal("A", File.ReadAllText(a));
    }
}
