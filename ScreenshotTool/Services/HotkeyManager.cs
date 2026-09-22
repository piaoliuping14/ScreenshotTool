using System.Windows.Interop;
using ScreenshotTool.Infrastructure;

namespace ScreenshotTool.Services;

/// <summary>
/// 全局热键管理：注册 / 解绑 / 冲突检测（大纲 4.5）。
/// RegisterHotKey 返回 false 即被其他软件占用（修正大纲中 MOD_ERROR 的笔误）。
/// P0 提供完整实现，P5 接入设置界面重绑。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xB001;

    private readonly IntPtr _hwnd;
    private HwndSource? _source;
    private HwndSourceHook? _hook;

    /// <summary>热键触发（在 UI 线程回调，可直接操作窗口）</summary>
    public event Action? HotkeyPressed;

    /// <summary>当前是否处于已注册状态</summary>
    public bool IsRegistered { get; private set; }

    public HotkeyManager(System.Windows.Window window)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _hook = WndProcHook;
        _source?.AddHook(_hook);
    }

    /// <summary>注册热键；失败 = 快捷键被占用。</summary>
    public bool TryRegister(int modifiers, int key)
    {
        Unregister();
        if (NativeMethods.RegisterHotKey(_hwnd, HotkeyId, (uint)modifiers, (uint)key))
        {
            IsRegistered = true;
            return true;
        }
        return false;
    }

    public void Unregister()
    {
        if (!IsRegistered) return;
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
        IsRegistered = false;
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        if (_hook is not null && _source is not null)
        {
            _source.RemoveHook(_hook);
            _hook = null;
            _source = null;
        }
    }
}
