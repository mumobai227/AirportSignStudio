# 工具说明

正常构建只使用 `bootstrap_dotnet.ps1`，由根目录 `build.ps1` 自动调用。

| 工具 | 用途 | 额外条件 |
|---|---|---|
| `bootstrap_dotnet.ps1` | 下载并校验固定 SDK | 首次需要网络 |
| `build_clean_glyphs.py` | 重建当前字形轮廓 | Python 标准库 |
| `extract_paint_reference.py` | 从两份指定 PSD 重建油漆数据 | Python、psd-tools、原始 PSD |
| `extract_glyphs.py` | v1 历史 PDF 描摹工具 | 按脚本参数提供 PDF 与依赖；不参与当前构建 |
| `verify_svg.cjs` | v3.1 SVG 的独立浏览器渲染检查 | 原 v3.1 验证输出、Playwright、Edge |
| `package_preview.py` | v3.1 PNG/SVG 对照图 | 原 v3.1 验证输出、Pillow、Windows 字体 |
| `verify_portable.ps1` | 单 EXE 启动检查 | 会打开窗口；不在默认检查中运行 |

历史辅助工具需要先准备对应的验证目录，仓库不包含这些生成结果。当前无窗口检查使用根目录 `build.ps1 -Test`。
