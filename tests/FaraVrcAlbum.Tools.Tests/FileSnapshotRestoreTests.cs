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
}
