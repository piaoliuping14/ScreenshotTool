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
