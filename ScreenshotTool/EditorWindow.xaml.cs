using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotTool.Infrastructure;
using ScreenshotTool.Services;
using DColor = System.Drawing.Color;
using DPen = System.Drawing.Pen;
using DTextureBrush = System.Drawing.TextureBrush;
using DSmoothing = System.Drawing.Drawing2D.SmoothingMode;
using DLineCap = System.Drawing.Drawing2D.LineCap;
using MediaBrush = System.Windows.Media.Brush;
using MediaPoint = System.Windows.Point;
using MColor = System.Windows.Media.Color;
using MBrushes = System.Windows.Media.Brushes;
using MSize = System.Windows.Size;

namespace ScreenshotTool;

/// <summary>
/// P3 编辑窗口（大纲 3.3 / 4.3）：
/// - 显示选区图像，窗口物理位置 = 选区位置（所见即所编辑）
/// - 工具条三级定位：选区下方 → 上方 → 选区内底部
/// - 画笔（颜色/粗细）、橡皮（粗细，原图恢复）；绘制天然裁剪在选区位图边界内
/// - ✓ 保存（Enter 默认动作）/ ✕ 取消（Esc）；P4 将在此追加 📌 钉图
/// </summary>
public partial class EditorWindow : Window
{
    private enum Tool { Pointer, Pen, Eraser }

    private enum ToolbarPlacement { Below, Above }

    private const double ToolbarDip = 36;               // 工具条高（DIP）
    private static readonly int[] PenSizes = { 2, 4, 8 };
    private static readonly int[] EraserSizes = { 10, 20, 40 };
    private static readonly string[] PaletteHex =
        { "#FF0000", "#FF9800", "#FFEB3B", "#4CAF50", "#26C6DA", "#2196F3", "#9C27B0", "#000000", "#FFFFFF" };

    /// <summary>保存成功（参数 = 文件路径），触发后窗口自行关闭</summary>
    public event Action<string>? Saved;

    /// <summary>取消（✕ / Esc / 关闭）</summary>
    public event Action? Cancelled;

    private readonly Bitmap _original;   // 原图副本：橡皮恢复源（大纲 4.3）
    private readonly Bitmap _edited;     // 编辑中位图
    private readonly Rectangle _physRect;
    private readonly MonitorInfo _monitor;
    private WriteableBitmap? _display;
    private ToolbarPlacement _placement;
    private double _scale = 1.0;
    private bool _drawing;
    private PointF _lastPt;
    private bool _closed;
    private Tool _tool = Tool.Pointer; // 默认指针：先拖动定位，避免选区松手后误画
    private DColor _penColor = ParseHex(App.Settings.PenColor);
    private int _penSize = App.Settings.PenThickness;
    private int _eraserSize = App.Settings.EraserThickness;

    private static readonly MediaBrush ActiveBg = new SolidColorBrush(MColor.FromRgb(0x00, 0x89, 0x7B));
    private static readonly MediaBrush InactiveBg = MBrushes.Transparent;
    private static readonly MediaBrush ActiveFg = MBrushes.White;
    private static readonly MediaBrush InactiveFg = new SolidColorBrush(MColor.FromRgb(0xEC, 0xEF, 0xF1));

    public EditorWindow(CaptureResult capture, Rectangle physRect)
    {
        InitializeComponent();
        _physRect = physRect;
        _original = CaptureService.CropSelection(capture, physRect);
        _edited = (Bitmap)_original.Clone();
        _monitor = FindMonitor(physRect) ?? CaptureService.EnumerateMonitors()[0];
        _scale = _monitor.DpiScale;

        LayoutWindow();
        BuildPalette();
        RefreshSizeButtons();
        UpdateToolVisual();
        Loaded += (_, _) => ClampToolbarHorizontal();
    }

    // ================= 布局（三级定位，大纲 3.3） =================

    private void LayoutWindow()
    {
        var work = GetWorkArea(_physRect);
        double toolbarPhys = ToolbarDip * _scale;

        if (work.Bottom - _physRect.Bottom >= toolbarPhys) _placement = ToolbarPlacement.Below;
        else if (_physRect.Top - work.Top >= toolbarPhys) _placement = ToolbarPlacement.Above;
        else _placement = ToolbarPlacement.Below; // 上下都放不下（如最大化窗口）：仍贴下方，允许压任务栏，绝不遮图

        double imgW = _physRect.Width / _scale;
        double imgH = _physRect.Height / _scale;

        RootGrid.RowDefinitions.Clear();
        var imgRow = new RowDefinition { Height = new GridLength(imgH, GridUnitType.Pixel) };
        var toolRow = new RowDefinition { Height = new GridLength(ToolbarDip, GridUnitType.Pixel) };

        if (_placement == ToolbarPlacement.Below)
        {
            // 紧贴选区下方
            RootGrid.RowDefinitions.Add(imgRow);
            RootGrid.RowDefinitions.Add(toolRow);
            Grid.SetRow(EditImage, 0);
            Grid.SetRow(Toolbar, 1);
        }
        else
        {
            // 下方越界 → 选区上方
            RootGrid.RowDefinitions.Add(toolRow);
            RootGrid.RowDefinitions.Add(imgRow);
            Grid.SetRow(Toolbar, 0);
            Grid.SetRow(EditImage, 1);
        }
        Toolbar.VerticalAlignment = VerticalAlignment.Center;

        EditImage.Width = imgW;
        EditImage.Height = imgH;
        Width = imgW;
        Height = imgH + ToolbarDip;
        Left = _physRect.X / _scale;
        Top = _placement == ToolbarPlacement.Above
            ? (_physRect.Y - toolbarPhys) / _scale
            : _physRect.Y / _scale;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        _scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;

        // 物理像素精确对齐选区（消除 DIP 舍入误差；允许向下超出工作区/压任务栏）
        int toolbarPhys = (int)Math.Round(ToolbarDip * _scale);
        int x = _physRect.X, y = _physRect.Y, w = _physRect.Width, h = _physRect.Height;
        if (_placement == ToolbarPlacement.Above) y -= toolbarPhys;
        h += toolbarPhys;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        InitDisplay();
    }

    /// <summary>工具条水平与选区左缘对齐；右越界时左移夹取</summary>
    private void ClampToolbarHorizontal()
    {
        Toolbar.Measure(new MSize(double.PositiveInfinity, double.PositiveInfinity));
        double tw = Toolbar.DesiredSize.Width;
        double left = Math.Min(4, Math.Max(0, RootGrid.ActualWidth - tw - 4));
        Toolbar.Margin = new Thickness(left, 0, 0, 0);
    }

    // ================= 显示（WriteableBitmap 增量刷新，只拷贝脏区域） =================

    private void InitDisplay()
    {
        _display = new WriteableBitmap(_edited.Width, _edited.Height, 96, 96, PixelFormats.Bgra32, null);
        var full = new Rectangle(0, 0, _edited.Width, _edited.Height);
        var bd = _edited.LockBits(full, System.Drawing.Imaging.ImageLockMode.ReadOnly, _edited.PixelFormat);
        try
        {
            _display.WritePixels(new Int32Rect(0, 0, _edited.Width, _edited.Height),
                bd.Scan0, Math.Abs(bd.Stride) * _edited.Height, bd.Stride);
        }
        finally { _edited.UnlockBits(bd); }
        EditImage.Source = _display;
    }

    private void UpdateDisplay(Rectangle dirty)
    {
        dirty.Intersect(Rectangle.FromLTRB(0, 0, _edited.Width, _edited.Height));
        if (dirty.Width <= 0 || dirty.Height <= 0) return;
        var bd = _edited.LockBits(dirty, System.Drawing.Imaging.ImageLockMode.ReadOnly, _edited.PixelFormat);
        try
        {
            // bd.Scan0 指向 dirty 区左上角 → 源矩形从 (0,0) 起；目标偏移 = dirty.X/Y
            _display!.WritePixels(new Int32Rect(0, 0, dirty.Width, dirty.Height),
                bd.Scan0, Math.Abs(bd.Stride) * dirty.Height, bd.Stride, dirty.X, dirty.Y);
        }
        finally { _edited.UnlockBits(bd); }
    }

    // ================= 绘制（大纲 4.3：按下→移动→抬起；裁剪到位图边界） =================

    private void OnImageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_tool == Tool.Pointer)
        {
            // 指针工具：在图内按住拖动 = 移动编辑窗口
            try { DragMove(); } catch (InvalidOperationException) { /* 极端时序下可忽略 */ }
            e.Handled = true;
            return;
        }
        _drawing = true;
        _lastPt = ToBitmapPoint(e.GetPosition(EditImage));
        EditImage.CaptureMouse();
        Stamp(_lastPt);
        e.Handled = true;
    }

    private void OnImageMouseMove(object sender, MouseEventArgs e)
    {
        if (!_drawing) return;
        var cur = ToBitmapPoint(e.GetPosition(EditImage));
        DrawSegment(_lastPt, cur);
        _lastPt = cur;
        e.Handled = true;
    }

    private void OnImageMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing) return;
        _drawing = false;
        EditImage.ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>鼠标 DIP → 位图像素（按显示比例，跨 DPI 安全）</summary>
    private PointF ToBitmapPoint(MediaPoint p)
    {
        double fx = _edited.Width / Math.Max(1.0, EditImage.ActualWidth);
        double fy = _edited.Height / Math.Max(1.0, EditImage.ActualHeight);
        return new PointF((float)(p.X * fx), (float)(p.Y * fy));
    }

    private int CurrentSize => _tool == Tool.Pen ? _penSize : _eraserSize;

    /// <summary>单击落点（圆点）</summary>
    private void Stamp(PointF p)
    {
        int size = CurrentSize, r = size / 2;
        var dirty = Rectangle.FromLTRB((int)p.X - r - 2, (int)p.Y - r - 2,
            (int)p.X + r + 3, (int)p.Y + r + 3);
        using var g = Graphics.FromImage(_edited);
        g.SmoothingMode = DSmoothing.AntiAlias;
        if (_tool == Tool.Pen)
        {
            using var brush = new SolidBrush(_penColor);
            g.FillEllipse(brush, p.X - r, p.Y - r, size, size);
        }
        else
        {
            using var tex = new DTextureBrush(_original); // 橡皮 = 原图恢复
            g.FillEllipse(tex, p.X - r, p.Y - r, size, size);
        }
        UpdateDisplay(dirty);
    }

    /// <summary>拖拽线段（圆帽）</summary>
    private void DrawSegment(PointF a, PointF b)
    {
        int size = CurrentSize, r = size / 2;
        var dirty = Rectangle.FromLTRB(
            (int)Math.Min(a.X, b.X) - r - 2, (int)Math.Min(a.Y, b.Y) - r - 2,
            (int)Math.Max(a.X, b.X) + r + 3, (int)Math.Max(a.Y, b.Y) + r + 3);
        using var g = Graphics.FromImage(_edited);
        g.SmoothingMode = DSmoothing.AntiAlias;
        if (_tool == Tool.Pen)
        {
            using var pen = new DPen(_penColor, size) { StartCap = DLineCap.Round, EndCap = DLineCap.Round };
            g.DrawLine(pen, a, b);
        }
        else
        {
            // 橡皮：TextureBrush 源 = 原图（同坐标系平铺），线段经过处恢复为原图像素
            using var tex = new DTextureBrush(_original);
            using var pen = new DPen(tex, size) { StartCap = DLineCap.Round, EndCap = DLineCap.Round };
            g.DrawLine(pen, a, b);
        }
        UpdateDisplay(dirty);
    }

    // ================= 工具条交互 =================

    /// <summary>拖动工具条移动整个编辑窗口（按钮自身已处理点击，不会误触发）</summary>
    private void OnToolbarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch (InvalidOperationException) { /* 极端时序下可忽略 */ }
        }
    }

    private void OnPointerClick(object sender, RoutedEventArgs e)
    {
        _tool = Tool.Pointer;
        UpdateToolVisual();
    }

    private void OnPenClick(object sender, RoutedEventArgs e)
    {
        _tool = Tool.Pen;
        UpdateToolVisual();
        EraserPopup.IsOpen = false;
        PenPopup.IsOpen = true;
    }

    private void OnEraserClick(object sender, RoutedEventArgs e)
    {
        _tool = Tool.Eraser;
        UpdateToolVisual();
        PenPopup.IsOpen = false;
        EraserPopup.IsOpen = true;
    }

    private void UpdateToolVisual()
    {
        PointerBtn.Background = _tool == Tool.Pointer ? ActiveBg : InactiveBg;
        PointerBtn.Foreground = _tool == Tool.Pointer ? ActiveFg : InactiveFg;
        PenBtn.Background = _tool == Tool.Pen ? ActiveBg : InactiveBg;
        PenBtn.Foreground = _tool == Tool.Pen ? ActiveFg : InactiveFg;
        EraserBtn.Background = _tool == Tool.Eraser ? ActiveBg : InactiveBg;
        EraserBtn.Foreground = _tool == Tool.Eraser ? ActiveFg : InactiveFg;
        EditImage.Cursor = _tool == Tool.Pointer ? Cursors.SizeAll : Cursors.Cross;
    }

    private void BuildPalette()
    {
        foreach (var hex in PaletteHex)
        {
            var b = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(4),
                Background = ParseMediaBrush(hex),
                Margin = new Thickness(3),
                Cursor = Cursors.Hand,
                Tag = hex
            };
            b.MouseLeftButtonDown += OnPaletteClick;
            ColorGrid.Children.Add(b);
        }
        RefreshPaletteHighlight();
    }

    private void OnPaletteClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string hex })
        {
            _penColor = ParseHex(hex);
            _tool = Tool.Pen;
            UpdateToolVisual();
            RefreshPaletteHighlight();
            PenPopup.IsOpen = false;
        }
    }

    private void RefreshPaletteHighlight()
    {
        string cur = ColorTranslatorToHex(_penColor);
        foreach (var b in ColorGrid.Children.OfType<Border>())
        {
            bool sel = b.Tag is string t && t.Equals(cur, StringComparison.OrdinalIgnoreCase);
            b.BorderBrush = sel ? MBrushes.White : null;
            b.BorderThickness = new Thickness(sel ? 2 : 0);
        }
    }

    private void OnPenSizeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t } && int.TryParse(t, out int v))
        {
            _penSize = v;
            RefreshSizeButtons();
        }
    }

    private void OnEraserSizeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t } && int.TryParse(t, out int v))
        {
            _eraserSize = v;
            RefreshSizeButtons();
        }
    }

    private void RefreshSizeButtons()
    {
        Highlight(new Button[] { PenS1, PenS2, PenS3 }, PenSizes, _penSize);
        Highlight(new Button[] { EraS1, EraS2, EraS3 }, EraserSizes, _eraserSize);
    }

    private static void Highlight(Button[] buttons, int[] values, int current)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            bool sel = values[i] == current;
            buttons[i].Background = sel ? ActiveBg : InactiveBg;
            buttons[i].Foreground = sel ? ActiveFg : InactiveFg;
        }
    }

    // ================= 保存 / 取消 =================

    private void OnSaveClick(object sender, RoutedEventArgs e) => Save();

    private void OnCancelClick(object sender, RoutedEventArgs e) => Cancel();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            // 面板开着：先关面板；否则取消编辑
            if (PenPopup.IsOpen || EraserPopup.IsOpen)
            {
                PenPopup.IsOpen = false;
                EraserPopup.IsOpen = false;
            }
            else Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Save(); // 默认动作（大纲 3.3）
            e.Handled = true;
        }
    }

    private void Save()
    {
        if (_closed) return;
        _closed = true;
        string path;
        try
        {
            path = CaptureService.SaveBitmapPng(_edited);
        }
        catch (Exception ex)
        {
            _closed = false;
            MessageBox.Show("保存截图失败：" + ex.Message, "截图工具",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Saved?.Invoke(path);
        Close();
    }

    private void Cancel()
    {
        if (_closed) return;
        _closed = true;
        Cancelled?.Invoke();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        // 关闭即释放（资源纪律，大纲 4.4）
        _edited.Dispose();
        _original.Dispose();
        base.OnClosed(e);
    }

    // ================= 辅助 =================

    private static MonitorInfo? FindMonitor(Rectangle r)
    {
        int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        return CaptureService.EnumerateMonitors().FirstOrDefault(m =>
            cx >= m.PhysX && cx < m.PhysX + m.PhysW &&
            cy >= m.PhysY && cy < m.PhysY + m.PhysH);
    }

    /// <summary>选区中心所在显示器的工作区（物理像素）；失败回退虚拟屏幕</summary>
    private static NativeMethods.RECT GetWorkArea(Rectangle selection)
    {
        var pt = new NativeMethods.POINT
        {
            X = selection.X + selection.Width / 2,
            Y = selection.Y + selection.Height / 2
        };
        IntPtr hMon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (NativeMethods.GetMonitorInfo(hMon, ref mi)) return mi.rcWork;
        return new NativeMethods.RECT
        {
            Left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
            Top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
            Right = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN)
                + NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
            Bottom = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN)
                + NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN)
        };
    }

    private static DColor ParseHex(string hex)
    {
        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return DColor.FromArgb(255, c.R, c.G, c.B);
    }

    private static MediaBrush ParseMediaBrush(string hex)
    {
        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static string ColorTranslatorToHex(DColor c) =>
        $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
