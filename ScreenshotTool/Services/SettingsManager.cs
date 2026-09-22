using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using ScreenshotTool.Models;

namespace ScreenshotTool.Services;

/// <summary>
/// 设置读写：JSON 持久化到 %AppData%\ScreenshotTool\settings.json（大纲 P0）。
/// </summary>
public static class SettingsManager
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenshotTool");

    public static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    // 缩进 + 中文不转义，方便用户直接打开查看
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>读取设置；文件不存在或损坏时回退默认值，不阻塞启动。</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null) return settings;
            }
        }
        catch
        {
            // 设置文件损坏 → 使用默认值，后续保存时覆盖修复
        }
        return new AppSettings();
    }

    /// <summary>写入设置（自动创建目录）。</summary>
    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
