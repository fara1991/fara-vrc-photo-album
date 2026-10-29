namespace FaraVrcAlbum.Desktop.Services;

/// <summary>
/// 待たずに投げる Task（起動時の読み込み・画面の切り替えの読み直し・通知など）の例外を観測して、報告先へ渡す。
/// 捨てた Task の例外は、GC が回収するまで UnobservedTaskException が出ないため、表示が大きく遅れるか出ない。
/// ここを通すと、例外は完了の直後に報告先（App の ShowError と同じ経路）へ届く。
/// </summary>
public static class BackgroundTasks
{
    /// <summary>task の完了を待たずに観測する。キャンセルは正常な終了として扱い、それ以外の例外は onError へ渡す。</summary>
    public static void Fire(Task task, Action<Exception> onError) => _ = ObserveAsync(task, onError);

    /// <summary>
    /// 観測の本体。例外は必ずここで捕まえる（この Task 自体は投げない）。
    /// ConfigureAwait は付けない: 継続は呼び出し元の SynchronizationContext（WPF では UI スレッド）で走り、MessageBox を UI スレッドから出せる。
    /// </summary>
    private static async Task ObserveAsync(Task task, Action<Exception> onError)
    {
        try
        {
            await task;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // キャンセルは正常な終了（AsyncCommand と同じ扱い）。それ以外は報告する
            onError(e);
        }
    }
}
