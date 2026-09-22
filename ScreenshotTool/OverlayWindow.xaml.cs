using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ScreenshotTool.Infrastructure;
using ScreenshotTool.Services;
using Rectangle = System.Drawing.Rectangle;

namespace ScreenshotTool;

/// <summary>
/// P2 全屏遮罩选区窗口（大纲 3.2）：
/// 每台显示器一个实例，精确覆盖该屏物理像素区域；
/// 鼠标拖拽画矩形选区，实时显示宽高，Esc 取消。
/// 坐标换算：窗口 DIP × 本屏缩放 + 本屏物理原点 = 全局物理像素。
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly OverlayController _owner;
    private readonly MonitorInfo _monitor;
    private readonly CaptureResult _capture;
    private double _scale;      // 本屏 DPI / 96
    private Point _startDip;    // 拖拽起点（本窗口 DIP）
    private bool _dragging;

    public OverlayWindow(OverlayController owner, MonitorInfo monitor, CaptureResult capture, BitmapSource bgSource)
    {
        InitializeComponent();
        _owner = owner;
        _monitor = monitor;
        _capture = capture;
        _scale = monitor.DpiScale;

        // 先用 DIP 大致定位到目标屏（OnSourceInitialized 后按物理像素精确校正）
        Left = monitor.PhysX / _scale;
        Top = monitor.PhysY / _scale;
        Width = monitor.PhysW / _scale;
        Height = monitor.PhysH / _scale;

        LayoutBackground(bgSource);
        Loaded += (_, _) => ClearSelection();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        _scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;

        // 物理像素精确定位（消除 DIP 舍入误差）
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
            _monitor.PhysX, _monitor.PhysY, _monitor.PhysW, _monitor.PhysH,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        LayoutBackground((BitmapSource)BgImage.Source);
        ClearSelection();
    }

    /// <summary>截图铺底：整张虚拟屏图按 1/scale 缩放，使本屏区域恰好铺满窗口（物理像素 1:1）</summary>
    private void LayoutBackground(BitmapSource source)
    {
        BgImage.Source = source;
        BgImage.Width = _capture.VirtualW / _scale;
        BgImage.Height = _capture.VirtualH / _scale;
        Canvas.SetLeft(BgImage, -(_monitor.PhysX - _capture.VirtualX) / _scale);
        Canvas.SetTop(BgImage, -(_monitor.PhysY - _capture.VirtualY) / _scale);
    }

    // ================= 鼠标拖拽选区 =================

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        _dragging = true;
        _startDip = e.GetPosition(Root);
        CaptureMouse(); // 捕获后跨屏拖拽事件仍路由到本窗口
        _owner.UpdateSelection(this, _startDip, _startDip);
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (!_dragging) return;
        _owner.UpdateSelection(this, _startDip, e.GetPosition(Root));
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        _owner.FinishSelection(this, _startDip, e.GetPosition(Root));
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            _owner.Cancel();
            e.Handled = true;
        }
    }

    // ================= 坐标换算（DIP ↔ 全局物理像素） =================

    /// <summary>拖拽两点（本窗口 DIP，可在窗外）→ 全局物理像素矩形</summary>
    public Rectangle DipToPhysicalRect(Point a, Point b)
    {
        double ax = a.X * _scale + _monitor.PhysX, ay = a.Y * _scale + _monitor.PhysY;
        double bx = b.X * _scale + _monitor.PhysX, by = b.Y * _scale + _monitor.PhysY;
        return Rectangle.FromLTRB(
            (int)Math.Round(Math.Min(ax, bx)),
            (int)Math.Round(Math.Min(ay, by)),
            (int)Math.Round(Math.Max(ax, bx)),
            (int)Math.Round(Math.Max(ay, by)));
    }

    // ================= 选区渲染 =================

    /// <summary>按全局物理选区矩形刷新本窗口的遮罩 / 边框 / 尺寸标签</summary>
    public void ApplySelection(Rectangle phys)
    {
        double w = ActualWidth, h = ActualHeight;
        // 全局物理 → 本窗口 DIP
        double l = (phys.Left - _monitor.PhysX) / _scale;
        double t = (phys.Top - _monitor.PhysY) / _scale;
        double r = (phys.Right - _monitor.PhysX) / _scale;
        double b = (phys.Bottom - _monitor.PhysY) / _scale;
        // 与本屏求交
        double cl = Math.Max(l, 0), ct = Math.Max(t, 0);
        double cr = Math.Min(r, w), cb = Math.Min(b, h);
        bool visible = cr - cl > 0 && cb - ct > 0;

        // 四块遮罩挖洞
        SetRect(MaskTop, 0, 0, w, Math.Max(ct, 0));
        SetRect(MaskBottom, 0, cb, w, Math.Max(h - cb, 0));
        SetRect(MaskLeft, 0, ct, Math.Max(cl, 0), Math.Max(cb - ct, 0));
        SetRect(MaskRight, cr, ct, Math.Max(w - cr, 0), Math.Max(cb - ct, 0));

        if (visible)
        {
            SetRect(SelBorder, cl, ct, cr - cl, cb - ct);
            SelBorder.Visibility = Visibility.Visible;

            // 尺寸标签（物理像素宽 × 高）：优先选区上方，放不下则选区下方
            SizeText.Text = $"{phys.Width} × {phys.Height}";
            SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double bw = SizeBadge.DesiredSize.Width, bh = SizeBadge.DesiredSize.Height;
            double bx = Math.Min(Math.Max(cl, 0), Math.Max(w - bw, 0));
            double by = ct - bh - 6;
            if (by < 0) by = Math.Min(cb + 6, Math.Max(h - bh, 0));
            Canvas.SetLeft(SizeBadge, bx);
            Canvas.SetTop(SizeBadge, by);
            SizeBadge.Visibility = Visibility.Visible;
        }
        else
        {
            SelBorder.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>清空选区：整屏遮罩</summary>
    public void ClearSelection()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        SetRect(MaskTop, 0, 0, w, h);
        SetRect(MaskBottom, 0, 0, 0, 0);
        SetRect(MaskLeft, 0, 0, 0, 0);
        SetRect(MaskRight, 0, 0, 0, 0);
        SelBorder.Visibility = Visibility.Collapsed;
        SizeBadge.Visibility = Visibility.Collapsed;
    }

    private static void SetRect(System.Windows.Shapes.Rectangle r, double x, double y, double w, double h)
    {
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        r.Width = Math.Max(0, w);
        r.Height = Math.Max(0, h);
    }
}

/// <summary>
/// 遮罩选区协调器：每屏创建一个遮罩窗口，统一维护「全局物理像素」选区；
/// 完成 / 取消后回调结果（null = 取消，由 MainWindow 处理保存与恢复）。
/// </summary>
public class OverlayController
{
    private readonly CaptureResult _capture;
    private readonly Action<Rectangle?> _onDone;
    private readonly List<OverlayWindow> _windows = new();
    private bool _finished;

    public OverlayController(CaptureResult capture, Action<Rectangle?> onDone)
    {
        _capture = capture;
        _onDone = onDone;
    }

    /// <summary>创建并显示所有遮罩窗口（UI 线程调用）</summary>
    public void Show()
    {
        var source = CaptureService.ToBitmapSource(_capture.FullBitmap);
        foreach (var m in CaptureService.EnumerateMonitors())
        {
            var win = new OverlayWindow(this, m, _capture, source);
            win.Show();
            _windows.Add(win);
        }
        if (_windows.Count > 0) _windows[0].Focus(); // 接收 Esc
    }

    internal void UpdateSelection(OverlayWindow from, Point startDip, Point curDip)
    {
        if (_finished) return;
        var rect = ClampToVirtual(from.DipToPhysicalRect(startDip, curDip));
        foreach (var w in _windows) w.ApplySelection(rect);
    }

    internal void FinishSelection(OverlayWindow from, Point startDip, Point endDip)
    {
        if (_finished) return;
        var rect = ClampToVirtual(from.DipToPhysicalRect(startDip, endDip));
        CloseAll();
        // 最小 4×4 物理像素，过小视为误点 → 按取消处理
        _onDone(rect.Width >= 4 && rect.Height >= 4 ? rect : null);
    }

    internal void Cancel()
    {
        if (_finished) return;
        CloseAll();
        _onDone(null);
    }

    /// <summary>选区夹取到虚拟屏幕范围内（拖出屏外时）</summary>
    private Rectangle ClampToVirtual(Rectangle r)
    {
        var vs = Rectangle.FromLTRB(_capture.VirtualX, _capture.VirtualY,
            _capture.VirtualX + _capture.VirtualW, _capture.VirtualY + _capture.VirtualH);
        r.Intersect(vs);
        return r;
    }

    private void CloseAll()
    {
        _finished = true;
        foreach (var w in _windows) w.Close();
        _windows.Clear();
    }
}
