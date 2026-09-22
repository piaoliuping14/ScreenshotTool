# ScreenshotTool

轻量级 Windows 桌面截图工具（C# WPF / .NET 8）。

## 功能

- 常驻置顶小浮条，一键截图，位置自动记忆
- 全屏半透明遮罩 + 拖拽矩形选区，实时显示尺寸，Esc 取消
- 内置编辑器：画笔（9 色色板 / 3 档粗细）、橡皮（原图恢复）、指针（拖动截图位置）
- 选区内容与屏幕所见完全一致（多显示器、高 DPI 适配）
- PNG 自动保存：`截图_yyyyMMdd_HHmmss.png`，重名自动追加序号
- 可选全局快捷键（默认不启用，设置中自行绑定，自动检测占用冲突）
- 保存位置可自定义；单实例运行；无托盘、无自启，保持精简

## 下载

前往 [Releases](https://github.com/piaoliuping14/ScreenshotTool/releases/latest) 下载 `ScreenshotTool.exe`（单文件版，约 68 MB，已内置运行时）。

保存到任意文件夹（如 `桌面` 或 `D:\工具\`）双击即可使用，无需安装；删除文件即卸载。

> **国内下载慢？**
> - 加速直链：[ScreenshotTool.exe 加速下载](https://ghfast.top/https://github.com/piaoliuping14/ScreenshotTool/releases/download/v0.1.0/ScreenshotTool.exe)
> - 通用方法：在任意 GitHub 下载链接前加 `https://ghfast.top/` 前缀即可加速（第三方公益代理，失效时可换 `https://gh-proxy.com/`）
> - 也可用多线程下载工具（IDM / Free Download Manager）下载原始链接

## 运行环境

- Windows 10 / 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## 构建

```bash
dotnet build ScreenshotTool/ScreenshotTool.csproj -c Release
```

发布单文件 exe（依赖运行时）：

```bash
dotnet publish ScreenshotTool/ScreenshotTool.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

## 使用

1. 启动后屏幕出现置顶浮条（可拖动，右键菜单可退出）
2. 点「截图」→ 拖拽选区 → 松手进入编辑器
3. 编辑器工具：

   | 图标 | 功能 |
   | --- | --- |
   | ↖ | 指针：按住拖动，移动截图位置 |
   | 🖌 | 画笔：颜色 / 粗细 |
   | 🧽 | 橡皮：恢复原图像素 |
   | ✓ | 保存（Enter） |
   | ✕ | 取消（Esc） |

4. ⚙ 设置：自定义保存位置、绑定截图快捷键

## License

[MIT](LICENSE)
