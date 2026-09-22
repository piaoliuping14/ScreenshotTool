using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ScreenshotTool.Services;

namespace ScreenshotTool;

/// <summary>
/// P1 正式主界面：置顶无边框小条。
/// - 应用名 + 截图按钮 + 设置齿轮；空白处拖动；右键菜单退出
/// - 初始位置：主屏工作区右上角；记忆上次位置
/// - 截图时隐藏（HideForCapture）、结束后恢复置顶（RestoreAfterCapture）
/// </summary>
public partial class MainWindow : Window
{
    private const double DefaultMargin = 12;

    public MainWindow()
    {
        InitializeComponent();
        ApplyStartPosition();
    }

    // ================= 位置管理 =================

    private void ApplyStartPosition()
    {
        var s = App.Settings;
        if (s.MainWindowLeft is double l && s.MainWindowTop is double t && IsPositionOnScreen(l, t))
        {
            Left = l;
            Top = t;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - DefaultMargin;
            Top = wa.Top + DefaultMargin;
        }
    }

    /// <summary>校验记忆位置仍在虚拟屏幕内（防止拔掉显示器后窗口消失）</summary>
    private static bool IsPositionOnScreen(double x, double y)
    {
        return x >= SystemParameters.VirtualScreenLeft - 20
            && y >= SystemParameters.VirtualScreenTop - 20
            && x <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60
            && y <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 30;
    }

    // ================= 拖动 =================

    private void OnRootMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch (InvalidOperationException) { /* 极端时序下可忽略 */ }
        }
    }

    // ================= 截图流程（P2：抓屏 → 遮罩选区 → 编辑器，大纲 3.2 / 4.1 / 4.2） =================

    private bool _capturing;
    private string? _lastSavedPath;
    private DispatcherTimer? _bubbleTimer;
    private HotkeyManager? _hotkey;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // 全局热键：默认不注册；用户在设置中绑定后启用（大纲 4.5）
        _hotkey = new HotkeyManager(this);
        _hotkey.HotkeyPressed += StartCapture;
        if (App.Settings.HotkeyModifiers != 0 && App.Settings.HotkeyKey != 0)
            _hotkey.TryRegister(App.Settings.HotkeyModifiers, App.Settings.HotkeyKey);
    }

    /// <summary>应用热键设置（供设置窗口调用，立即生效）；
    /// 返回 false = 组合被其他程序占用（已自动回滚为旧热键）</summary>
    public bool ApplyHotkey(int modifiers, int key)
    {
        if (_hotkey is null) return modifiers == 0;
        if (modifiers == 0 || key == 0)
        {
            _hotkey.Unregister();
            return true;
        }
        if (_hotkey.TryRegister(modifiers, key)) return true;
        // 注册失败：回滚旧热键，保持原设置可用
        if (App.Settings.HotkeyModifiers != 0 && App.Settings.HotkeyKey != 0)
            _hotkey.TryRegister(App.Settings.HotkeyModifiers, App.Settings.HotkeyKey);
        return false;
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e) => StartCapture();

    /// <summary>截图入口（按钮 / 全局热键共用）</summary>
    private async void StartCapture()
    {
        if (_capturing) return; // 防重入（大纲 4.5 竞态要求）
        _capturing = true;

        HideForCapture();
        await Task.Delay(80); // 等待屏幕重绘，防止把自己截进画面（大纲 4.1）

        CaptureResult capture;
        try
        {
            capture = CaptureService.CaptureVirtualScreen();
        }
        catch
        {
            _capturing = false;
            RestoreAfterCapture();
            return;
        }

        var controller = new OverlayController(capture, result =>
        {
            // 遮罩流程结束（UI 线程回调）：进入编辑器或取消（大纲 3.3）
            if (result is Rectangle rect)
            {
                var editor = new EditorWindow(capture, rect);
                editor.Saved += path =>
                {
                    RestoreAfterCapture();
                    ShowSavedBubble(path);
                };
                editor.Cancelled += () => RestoreAfterCapture();
                editor.Closed += (_, _) =>
                {
                    capture.Dispose();
                    _capturing = false;
                };
                editor.Show();
            }
            else
            {
                RestoreAfterCapture();
                capture.Dispose();
                _capturing = false;
            }
        });
        controller.Show();
    }

    /// <summary>截图流程入口：隐藏主界面（不是最小化，大纲 3.1）</summary>
    public void HideForCapture() => Hide();

    /// <summary>截图流程结束：恢复置顶显示</summary>
    public void RestoreAfterCapture()
    {
        Show();
        Topmost = true;
        Activate();
    }

    // ================= 保存成功气泡 =================

    /// <summary>短暂显示「已保存 + 文件名」，2.5 秒后自动消失；点击打开所在文件夹</summary>
    private void ShowSavedBubble(string path)
    {
        _lastSavedPath = path;
        BubbleText.Text = "已保存 " + System.IO.Path.GetFileName(path);
        SavedPopup.IsOpen = true;
        _bubbleTimer?.Stop();
        _bubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _bubbleTimer.Tick += (s, _) =>
        {
            if (s is DispatcherTimer t) t.Stop();
            SavedPopup.IsOpen = false;
        };
        _bubbleTimer.Start();
    }

    private void OnBubbleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_lastSavedPath is not null)
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_lastSavedPath}\"");
        SavedPopup.IsOpen = false;
    }

    // ================= 设置 / 退出 =================

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow { Owner = this };
        settings.ShowDialog();
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        _hotkey?.Dispose(); // 解绑全局热键
        // 记住主界面位置（大纲：可拖动 + 记忆）
        App.Settings.MainWindowLeft = Left;
        App.Settings.MainWindowTop = Top;
        SettingsManager.Save(App.Settings);
        base.OnClosing(e);
    }
}
