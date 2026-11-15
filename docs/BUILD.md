# 构建与验证

## 环境

- Windows 10 / 11 x64，PowerShell 7。
- 首次构建需要网络，以下载固定版本 .NET SDK 10.0.404 和还原构建依赖。
- `tools/bootstrap_dotnet.ps1` 校验 SDK ZIP 的 SHA512 并解压到 `.tools/`，不全局安装 SDK。
- 正常构建使用已提交的字形和材质，不需要原始 PDF、PSD、Python 或 psd-tools。
- PDF 文字提取使用 [PdfPig 0.1.16](https://www.nuget.org/packages/PdfPig/0.1.16)。复杂扫描页使用 [Windows PDF API](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpage.rendertostreamasync?view=winrt-19041) 渲染后再识别。PdfPig 许可见 [PdfPig.txt](licenses/PdfPig.txt)，随单文件 EXE 和 Windows 压缩包提供。
- OCR 使用内置 PP-OCRv4 识别模型和 ONNX Runtime CPU。模型来源、校验值和许可见 [OCR-Attribution.txt](licenses/OCR-Attribution.txt)。构建单文件版需能找到 Visual Studio 或 Build Tools 的 x64 Visual C++ 可再分发文件。脚本会自动查找，也可用 `./build.ps1 -OcrRedistPath '<x64 CRT 目录>'` 指定。发布 EXE 内含所需运行库和模型。

## 命令

```powershell
# 单文件发布
./build.ps1

# 发布并运行无窗口功能检查
./build.ps1 -Test
```

输出：`release_v3_26_3/AirportSignStudio.exe`。检查结果和样张：`validation_v3_26_3/`。成品为自包含的单个 EXE；程序运行时由 .NET 管理临时解包缓存，不需要随 EXE 携带 config、SDK 或素材文件。

只有显式执行 `./build.ps1 -UiTest` 才会进行窗口检查。`tools/verify_portable.ps1` 也会启动程序窗口；它们不属于默认检查，本次发布均未运行。界面测试不能替代另一台电脑、真实跨屏或模拟器验收。

## 核心代码

| 文件 | 职责 |
|---|---|
| `src/SignEngine.cs` | 字形、标牌排版与图像导出 |
| `src/Atlas.cs` / `src/SlotEditor.cs` | 拼图主界面、菜单与单格编辑 |
| `src/AtlasLayout.cs` / `src/PreviewViewport.cs` | 区域布局、密度计算、预览坐标 |
| `src/AtlasHistory.cs` / `src/ProjectIO.cs` | 撤销重做与工程格式 |
| `src/PolExport.cs` / `src/Weathering.cs` | POL 与油漆渲染 |
| `src/Colors.cs` / `src/BaseColors.cs` | 配色覆盖与四色替换 |
| `src/StudioTheme.cs` / `src/StudioMotion.cs` | 控件样式与动效 |

`MainForm.cs` 保留程序入口和早期单块界面，入口运行拼图窗口。`TypeColors.cs` 保留旧工程及预设兼容逻辑。

## 打包约定

Git 仅保存源码、内置素材、测试样本和说明；SDK、构建缓存、历史版本、备份及验证图片留在本地。EXE、Windows ZIP、源码 ZIP 和 SHA256 校验文件放在 GitHub Releases。源码 ZIP 不包含 SDK，首次构建仍需下载依赖。Windows ZIP 中的说明文件为可选，运行只需 EXE。
