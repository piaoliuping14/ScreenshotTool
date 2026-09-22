namespace ScreenshotTool.Models;

/// <summary>
/// 应用设置模型，默认值按《截图工具开发大纲》4.6。
/// </summary>
public class AppSettings
{
    /// <summary>截图快捷键修饰键（0 = 未设置热键；MOD_ALT | MOD_CONTROL | MOD_SHIFT 组合）</summary>
    public int HotkeyModifiers { get; set; } = 0;

    /// <summary>截图快捷键虚拟键码（0 = 未设置）</summary>
    public int HotkeyKey { get; set; } = 0;

    /// <summary>默认保存目录；空 = 「图片\截图工具」</summary>
    public string SaveFolder { get; set; } = "";

    /// <summary>默认画笔颜色（#RRGGBB）</summary>
    public string PenColor { get; set; } = "#FF0000";

    /// <summary>默认画笔粗细（px）</summary>
    public int PenThickness { get; set; } = 3;

    /// <summary>橡皮粗细（px）</summary>
    public int EraserThickness { get; set; } = 20;

    /// <summary>主界面位置记忆（null = 未记忆，按默认右上角显示）</summary>
    public double? MainWindowLeft { get; set; }

    public double? MainWindowTop { get; set; }

    /// <summary>解析后的保存目录（含默认回退）</summary>
    public string ResolveSaveFolder() =>
        string.IsNullOrWhiteSpace(SaveFolder)
            ? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "截图工具")
            : SaveFolder;
}
