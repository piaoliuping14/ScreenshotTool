using System.Threading;
using System.Windows;
using ScreenshotTool.Models;
using ScreenshotTool.Services;

namespace ScreenshotTool;

/// <summary>
/// 应用入口：单实例互斥 + 激活已有实例 + 加载设置。
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\ScreenshotTool.SingleInstance";
    private const string ActivateEventName = @"Local\ScreenshotTool.Activate";

    private Mutex? _mutex;
    private bool _ownsMutex;

    /// <summary>激活信号（主实例持有；二次启动时直接 Set 通知其激活主窗口）</summary>
    private readonly EventWaitHandle _activateEvent = new(false, EventResetMode.AutoReset, ActivateEventName);

    /// <summary>全局设置（启动时加载）</summary>
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // ---- 单实例互斥（大纲 P0）----
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // 已有实例在运行：通知其激活主窗口，然后退出
            _activateEvent!.Set();
            Shutdown();
            return;
        }
        _ownsMutex = true;

        // 后台监听“二次启动 → 激活主窗口”信号
        new Thread(WaitForActivateSignal)
        {
            IsBackground = true,
            Name = "SingleInstance.ActivateListener"
        }.Start();

        base.OnStartup(e);

        // ---- 设置模块：启动加载（大纲 P0：设置可读写）----
        Settings = SettingsManager.Load();
        // 旧版默认热键（Ctrl+Shift+A）迁移：新版默认不注册；旧值必为程序写入而非用户设置
        if (Settings.HotkeyModifiers == (Infrastructure.NativeMethods.MOD_CONTROL | Infrastructure.NativeMethods.MOD_SHIFT)
            && Settings.HotkeyKey == 0x41)
        {
            Settings.HotkeyModifiers = 0;
            Settings.HotkeyKey = 0;
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    private void WaitForActivateSignal()
    {
        while (true)
        {
            try { _activateEvent.WaitOne(); }
            catch (ObjectDisposedException) { break; } // 应用退出时句柄已释放

            Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is null || !MainWindow.IsLoaded) return;
                MainWindow.Show();
                MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateEvent?.Dispose();
        if (_ownsMutex)
        {
            try { _mutex!.ReleaseMutex(); } catch { /* 已终止场景下可忽略 */ }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
