using System.Windows.Input;
using FaraVrcAlbum.Desktop.Services;

namespace FaraVrcAlbum.Desktop.Mvvm;

/// <summary>同期の命令。</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    /// <summary>CanExecute の結果が変わったときに呼ぶ（ボタンの有効・無効を更新する）。</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// 非同期の命令。実行中は再入させない（IsRunning）。キャンセルは Cancel() で、実行中のトークンに伝わる。
/// 例外は onError に渡す（ICommand.Execute は戻り値が無いため、握りつぶさず必ず報告する）。
/// </summary>
public sealed class AsyncCommand(Func<CancellationToken, Task> execute, Action<Exception> onError, Func<bool>? canExecute = null) : ICommand
{
    private CancellationTokenSource? _cts;

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => _cts is not null;

    public bool CanExecute(object? parameter) => !IsRunning && (canExecute?.Invoke() ?? true);

    // RunAsync は例外を onError に渡して終わる。それでも onError 自体が投げた場合に、Task を捨てずに観測する
    public void Execute(object? parameter) => BackgroundTasks.Fire(RunAsync(), onError);

    /// <summary>実行する（テストや、ボタン以外の呼び出しから使う）。例外は onError に渡す。</summary>
    public async Task RunAsync()
    {
        if (!CanExecute(null)) return;
        _cts = new CancellationTokenSource();
        RaiseCanExecuteChanged();
        try
        {
            await execute(_cts.Token);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            onError(e);
        }
        catch (OperationCanceledException)
        {
            // キャンセルは正常な終了として扱う（画面の状態は呼び出し側が決める）
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>実行中の処理を止める（次のファイルの前で止まる）。</summary>
    public void Cancel() => _cts?.Cancel();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
