using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ScreenshotTool.Infrastructure;

namespace ScreenshotTool.Services;

/// <summary>
/// 单台显示器信息（DPI 感知进程中枚举值均为物理像素，不被系统虚拟化）。
/// </summary>
public class MonitorInfo
{
    public int PhysX, PhysY, PhysW, PhysH; // 物理像素矩形
    public double DpiScale;                // DPI / 96
}

/// <summary>
/// 一次抓屏的结果：整张虚拟屏幕位图 + 虚拟屏幕物理原点。
/// </summary>
public sealed class CaptureResult : IDisposable
{
    public required Bitmap FullBitmap { get; init; }
    public int VirtualX { get; init; }
    public int VirtualY { get; init; }
    public int VirtualW { get; init; }
    public int VirtualH { get; init; }

    public void Dispose() => FullBitmap.Dispose();
}

/// <summary>
/// 抓屏服务（大纲 4.2）：多屏合成、DPI 坐标换算辅助、PNG 保存。
/// 坐标约定：全程统一「虚拟屏幕物理像素」空间，原点 = 虚拟屏幕左上角。
/// </summary>
public static class CaptureService
{
    /// <summary>枚举所有显示器（物理像素矩形 + DPI 缩放系数）</summary>
    public static List<MonitorInfo> EnumerateMonitors()
    {
        var list = new List<MonitorInfo>();
        NativeMethods.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr _, ref NativeMethods.RECT rect, IntPtr _) =>
        {
            uint dpiX = 96;
            try { NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out _); }
            catch { /* 极老系统缺少 shcore：按 100% 缩放处理 */ }
            list.Add(new MonitorInfo
            {
                PhysX = rect.Left,
                PhysY = rect.Top,
                PhysW = rect.Right - rect.Left,
                PhysH = rect.Bottom - rect.Top,
                DpiScale = dpiX / 96.0
            });
            return true;
        };
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return list;
    }

    /// <summary>抓取整个虚拟屏幕（多屏合成一张位图，物理像素）</summary>
    public static CaptureResult CaptureVirtualScreen()
    {
        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        var bmp = new Bitmap(vw, vh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(vx, vy, 0, 0, new Size(vw, vh));
        }
        return new CaptureResult { FullBitmap = bmp, VirtualX = vx, VirtualY = vy, VirtualW = vw, VirtualH = vh };
    }

    /// <summary>GDI 位图 → WPF 图像源（供遮罩窗口铺底显示，跨窗口共享）</summary>
    public static System.Windows.Media.Imaging.BitmapSource ToBitmapSource(Bitmap bmp)
    {
        IntPtr hBmp = bmp.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBmp, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            source.Freeze(); // 冻结后才能跨窗口共享
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(hBmp); // HBITMAP 拷贝完立即释放，防 GDI 句柄泄漏
        }
    }

    /// <summary>裁剪选区 → 新 Bitmap（编辑器持有；橡皮用它做原图恢复，大纲 4.3）</summary>
    public static Bitmap CropSelection(CaptureResult capture, Rectangle physRect)
    {
        int x = Math.Clamp(physRect.X - capture.VirtualX, 0, capture.VirtualW - 1);
        int y = Math.Clamp(physRect.Y - capture.VirtualY, 0, capture.VirtualH - 1);
        int w = Math.Clamp(physRect.Width, 1, capture.VirtualW - x);
        int h = Math.Clamp(physRect.Height, 1, capture.VirtualH - y);
        return capture.FullBitmap.Clone(new Rectangle(x, y, w, h), capture.FullBitmap.PixelFormat);
    }

    /// <summary>保存编辑后的位图（大纲 4.6：截图_yyyyMMdd_HHmmss.png，重名追加序号）</summary>
    public static string SaveBitmapPng(Bitmap image)
    {
        string dir = App.Settings.ResolveSaveFolder();
        Directory.CreateDirectory(dir);
        string baseName = "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(dir, baseName + ".png");
        for (int i = 1; File.Exists(path); i++)
            path = Path.Combine(dir, $"{baseName}_{i}.png");
        image.Save(path, ImageFormat.Png);
        return path;
    }
}
