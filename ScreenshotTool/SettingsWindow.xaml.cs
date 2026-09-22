using System.Text;
using System.Windows;
using System.Windows.Input;
using ScreenshotTool.Infrastructure;
using ScreenshotTool.Services;

namespace ScreenshotTool;

/// <summary>
/// 设置窗口（大纲 3.1 设置面板）。
/// 截图保存位置 + 截图快捷键（默认不启用，用户自行绑定，含占用冲突检测）。
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>待保存的自定义目录；null = 使用默认（图片\截图工具）</summary>
    private string? _selectedFolder;

    // ---- 快捷键（0 = 未设置）----
    private int _hkModifiers;
    private int _hkKey;
    private bool _recording;

    public SettingsWindow()
    {
        InitializeComponent();
        _selectedFolder = string.IsNullOrWhiteSpace(App.Settings.SaveFolder)
            ? null
            : App.Settings.SaveFolder;
        _hkModifiers = App.Settings.HotkeyModifiers;
        _hkKey = App.Settings.HotkeyKey;
        RefreshDisplay();
        RefreshHotkey();
    }

    // ================= 保存位置 =================

    private void RefreshDisplay()
    {
        FolderBox.Text = _selectedFolder ?? App.Settings.ResolveSaveFolder();
        HintText.Text = _selectedFolder is null
            ? "当前使用默认位置：图片\\截图工具"
            : "已自定义保存位置，重启后仍然生效";
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择截图保存位置" };
        if (dlg.ShowDialog(this) == true)
        {
            _selectedFolder = dlg.FolderName;
            RefreshDisplay();
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _selectedFolder = null;
        RefreshDisplay();
    }

    // ================= 快捷键 =================

    private void RefreshHotkey()
    {
        RecordButton.Content = _recording ? "录制中…" : "修改";
        HotkeyBox.Text = _recording
            ? "请按下组合键（需含 Ctrl / Alt / Shift，Esc 取消）"
            : FormatHotkey(_hkModifiers, _hkKey);
    }

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        _recording = !_recording;
        RefreshHotkey();
    }

    private void OnClearHkClick(object sender, RoutedEventArgs e)
    {
        _recording = false;
        _hkModifiers = 0;
        _hkKey = 0;
        RefreshHotkey();
    }

    /// <summary>录制状态：捕获按下组合键（Alt 组合键的键值在 SystemKey 中）；非录制时 Esc 关闭窗口</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!_recording)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
            return;
        }
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _recording = false;
            RefreshHotkey();
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 纯修饰键：等待继续按
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return;

        var mods = Keyboard.Modifiers;
        int m = 0;
        if (mods.HasFlag(ModifierKeys.Control)) m |= NativeMethods.MOD_CONTROL;
        if (mods.HasFlag(ModifierKeys.Alt)) m |= NativeMethods.MOD_ALT;
        if (mods.HasFlag(ModifierKeys.Shift)) m |= NativeMethods.MOD_SHIFT;

        if (m == 0)
        {
            HotkeyBox.Text = "需包含 Ctrl / Alt / Shift 之一";
            return;
        }

        _hkModifiers = m;
        _hkKey = KeyInterop.VirtualKeyFromKey(key);
        _recording = false;
        RefreshHotkey();
    }

    private static string FormatHotkey(int mods, int key)
    {
        if (mods == 0 || key == 0) return "未设置";
        var sb = new StringBuilder();
        if ((mods & NativeMethods.MOD_CONTROL) != 0) sb.Append("Ctrl+");
        if ((mods & NativeMethods.MOD_ALT) != 0) sb.Append("Alt+");
        if ((mods & NativeMethods.MOD_SHIFT) != 0) sb.Append("Shift+");
        sb.Append(KeyInterop.KeyFromVirtualKey(key));
        return sb.ToString();
    }

    // ================= 窗口拖动 =================

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch (InvalidOperationException) { /* 极端时序下可忽略 */ }
        }
    }

    // ================= 确定 / 取消 =================

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        // 热键有变化：先试注册（占用 → 提示并停留），成功后立即生效
        if (_hkModifiers != App.Settings.HotkeyModifiers || _hkKey != App.Settings.HotkeyKey)
        {
            if (Application.Current.MainWindow is MainWindow main
                && !main.ApplyHotkey(_hkModifiers, _hkKey))
            {
                _recording = false;
                RefreshHotkey();
                MessageBox.Show(this, "该快捷键已被其他程序占用，请换一个组合。",
                    "截图工具", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        App.Settings.HotkeyModifiers = _hkModifiers;
        App.Settings.HotkeyKey = _hkKey;
        App.Settings.SaveFolder = _selectedFolder ?? "";
        SettingsManager.Save(App.Settings); // 立即持久化到 settings.json
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
