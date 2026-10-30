using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;

namespace FaraVrcAlbum.Desktop;

/// <summary>
/// 画面の枠: メニューバー（ファイル・表示・設定・ヘルプ）と、作業の画面のタブ（Alt+1〜6）。設定（Ctrl+,）と使い方（F1）は別ウィンドウ。
/// 業務の処理は ViewModel にある。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = _vm;

        // 表示メニュー: 作業の画面ごとに 1 項目（Alt+番号）
        for (var i = 0; i < _vm.Pages.Count; i++)
        {
            var item = _vm.Pages[i];
            var menu = new MenuItem { Header = item.Title, InputGestureText = $"Alt+{i + 1}" };
            menu.Click += (_, _) => _vm.Navigate(item.Page);
            ViewMenu.Items.Add(menu);
        }

        PageTabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != PageTabs) return;      // 画面の中の一覧などの選択の変更は無視する
            if (PageTabs.SelectedItem is MainViewModel.PageItem item && !ReferenceEquals(_vm.Current, item.Page))
            {
                _vm.Navigate(item.Page);
            }
        };
        _vm.PropertyChanged += OnViewModelChanged;
        // 起動の読み込みの例外は、捨てた Task に閉じ込めず、観測して App の ShowError で即座に出す
        Loaded += (_, _) => BackgroundTasks.Fire(LoadOnStartupAsync(), App.ShowError);
        KeyDown += OnKeyDown;
        // 確認ダイアログを閉じた後などに、フォーカスが無効になったボタンに残ると Alt+数字が届かない。その時はタブに戻す
        Activated += (_, _) =>
        {
            if (Keyboard.FocusedElement is not UIElement focused || !focused.IsEnabled) PageTabs.Focus();
        };
    }

    /// <summary>
    /// 起動のとき（Loaded）の読み込み。async void（イベントの形）にしない（VSTHRD100）。
    /// 呼び出しは BackgroundTasks.Fire で観測し、投げた例外は App.ShowError で直ちに出す（UnobservedTaskException は GC まで発火しないため、頼らない）。
    /// 退避した設定の知らせは、画面が出た後（ここ）で 1 回だけ行う。
    /// </summary>
    private async Task LoadOnStartupAsync()
    {
        SyncNavigation();
        await _vm.NotifyStartupPreferencesAsync();
        await _vm.Ingest.RefreshCommand.RunAsync();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Current)) SyncNavigation();
    }

    private void SyncNavigation()
    {
        var match = _vm.Pages.FirstOrDefault(p => ReferenceEquals(p.Page, _vm.Current));
        if (match is not null && !ReferenceEquals(PageTabs.SelectedItem, match)) PageTabs.SelectedItem = match;
    }

    /// <summary>Alt+1〜6 で画面を切り替える（キーボードだけで操作できるように）。</summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Alt) return;
        // Alt と同時の数字は Key.System として届き、本当のキーは SystemKey に入る
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var index = key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            _ => -1,
        };
        if (index < 0 || index >= _vm.Pages.Count) return;
        _vm.Navigate(_vm.Pages[index].Page);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ メニュー

    private void OpenSettings_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _vm.OpenSettings();
        new SettingsWindow(_vm.Settings) { Owner = this }.ShowDialog();
    }

    private void OpenSourceFolder_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(_vm.App.Session.SourceRoot);

    private void OpenHelp_Executed(object sender, ExecutedRoutedEventArgs e) => new HelpWindow { Owner = this }.Show();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this,
            "Fara Photo Album\n\n" +
            "VRChat で撮った写真を、写真ブログに載せるためのアプリです。\n" +
            "写真の取り込み・記事の編集・アバターと服の登録・公開した写真の管理ができます。\n\n" +
            $"バージョン {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}",
            "このアプリについて", MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>エクスプローラーで開く。無ければ理由を状態行に出す（作らない）。</summary>
    private void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            MessageBox.Show(this, $"フォルダがありません: {path}", "Fara Photo Album", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });
    }
}
